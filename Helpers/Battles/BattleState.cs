using OnlyWar.Helpers.Battles;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Battles
{
	public class BattleState
	{
		private readonly Dictionary<int, IReadOnlyList<Tuple<int, int>>> _soldierPositionsMap;
		private readonly Dictionary<int, BattleSoldier> _soldiers;
		private readonly Dictionary<int, BattleSquad> _attackerBattleSquads;
		private readonly Dictionary<int, BattleSquad> _opposingBattleSquads;

		public int TurnNumber { get; private set; }
		public IReadOnlyDictionary<int, BattleSoldier> Soldiers { get => _soldiers; }
		public IReadOnlyDictionary<int, IReadOnlyList<Tuple<int, int>>> SoldierPositionsMap { get => _soldierPositionsMap; }
		public IReadOnlyDictionary<int, BattleSquad> AttackerSquads { get => _attackerBattleSquads; }
		public IReadOnlyDictionary<int, BattleSquad> OpposingSquads { get => _opposingBattleSquads; }

		// Constructor for creating the initial state
		public BattleState(IReadOnlyDictionary<int, BattleSquad> attackerSquads,
						   IReadOnlyDictionary<int, BattleSquad> opposingSquads) : this(0, attackerSquads, opposingSquads) { }

		// Constructor for creating a new state based on a previous state (deep copy)
		public BattleState(BattleState original) : this(original.TurnNumber + 1, original.AttackerSquads, original.OpposingSquads) { }

		private BattleState(int turnNumber, IReadOnlyDictionary<int, BattleSquad> attackerSquads, IReadOnlyDictionary<int, BattleSquad> opposingSquads)
		{
			TurnNumber = turnNumber;

			// deep copy the squads, which will also deep copy the soldiers in the squads
			_attackerBattleSquads = attackerSquads.ToDictionary(
				kvp => kvp.Key,
				kvp => (BattleSquad)kvp.Value.Clone());
			_opposingBattleSquads = opposingSquads.ToDictionary(
				kvp => kvp.Key,
				kvp => (BattleSquad)kvp.Value.Clone());

			// add the soldiers to the soldier map from the squads
			_soldiers = new Dictionary<int, BattleSoldier>();
			_soldierPositionsMap = new Dictionary<int, IReadOnlyList<Tuple<int, int>>>();
			foreach (BattleSquad squad in AttackerSquads.Values.Concat(OpposingSquads.Values))
			{
				foreach (BattleSoldier soldier in squad.AbleSoldiers)
				{
					_soldiers[soldier.Soldier.Id] = soldier;
					_soldierPositionsMap[soldier.Soldier.Id] = soldier.PositionList;
				}
			}
		}

		// Helper method to get a soldier by ID
		public BattleSoldier GetSoldier(int soldierId)
		{
			return _soldiers[soldierId];
		}

		// Helper method to get a squad by ID
		public BattleSquad GetSquad(int squadId)
		{
			// You might need to search both AttackerSquads and OpposingSquads
			if (AttackerSquads.ContainsKey(squadId))
			{
				return AttackerSquads[squadId];
			}
			else
			{
				return OpposingSquads[squadId];
			}
		}

		public void RemoveSquad(BattleSquad squad)
		{
			if (squad == null)
			{
				throw new ArgumentNullException(nameof(squad));
			}

			// These collections represent the two tactical sides supplied to the resolver, not
			// player and NPC affiliation. An NPC attacker is in AttackerSquads (the mission side),
			// while a defending Chapter squad can be in OpposingSquads. Remove from the collection
			// that actually owns the squad instead of consulting the presentation-only
			// IsPlayerSquad flag.
			// Cleanup can encounter several casualties from the same wiped squad. The first
			// casualty removes the squad; subsequent casualties must therefore be harmless.
			_attackerBattleSquads.Remove(squad.Id);
			_opposingBattleSquads.Remove(squad.Id);
		}
	}
}
