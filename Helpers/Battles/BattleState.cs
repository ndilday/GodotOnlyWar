using OnlyWar.Helpers.Battles;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Battles
{
	public class BattleState
	{
		private readonly Dictionary<int, BattleSoldier> _soldiers;
		private readonly Dictionary<int, BattleSquad> _attackerBattleSquads;
		private readonly Dictionary<int, BattleSquad> _opposingBattleSquads;
		private readonly Dictionary<int, BattleSquad> _allAttackerBattleSquads;
		private readonly Dictionary<int, BattleSquad> _allOpposingBattleSquads;

		public int TurnNumber { get; private set; }
		public IReadOnlyDictionary<int, BattleSoldier> Soldiers { get => _soldiers; }
		public IReadOnlyDictionary<int, BattleSquad> AttackerSquads { get => _attackerBattleSquads; }
		public IReadOnlyDictionary<int, BattleSquad> OpposingSquads { get => _opposingBattleSquads; }
		// Compatibility seam: the resolver currently consumes AttackerSquads/OpposingSquads as
		// active collections. Full rosters retain status-transitioned squads for snapshots and
		// aftermath until the resolver migrates to explicit active projections.
		public IReadOnlyDictionary<int, BattleSquad> AllAttackerSquads { get => _allAttackerBattleSquads; }
		public IReadOnlyDictionary<int, BattleSquad> AllOpposingSquads { get => _allOpposingBattleSquads; }
		public IReadOnlyDictionary<int, BattleSquad> ActiveAttackerSquads { get => _attackerBattleSquads; }
		public IReadOnlyDictionary<int, BattleSquad> ActiveOpposingSquads { get => _opposingBattleSquads; }
		public BattleSideState AttackerSide { get; }
		public BattleSideState OpposingSide { get; }

		// Constructor for creating the initial state
		public BattleState(IReadOnlyDictionary<int, BattleSquad> attackerSquads,
						   IReadOnlyDictionary<int, BattleSquad> opposingSquads)
			: this(attackerSquads, opposingSquads,
				new BattleSideProfile(Models.Orders.Aggression.Normal, BattleRole.Attacker),
				new BattleSideProfile(Models.Orders.Aggression.Normal, BattleRole.Defender)) { }

		public BattleState(IReadOnlyDictionary<int, BattleSquad> attackerSquads,
						   IReadOnlyDictionary<int, BattleSquad> opposingSquads,
						   BattleSideProfile attackerProfile,
						   BattleSideProfile opposingProfile)
			: this(0, attackerSquads, opposingSquads, attackerProfile, opposingProfile, null, null) { }

		// Constructor for creating a new state based on a previous state (deep copy)
		public BattleState(BattleState original)
			: this(original.TurnNumber + 1, original.AllAttackerSquads, original.AllOpposingSquads,
				null, null, original.AttackerSide, original.OpposingSide) { }

		public void AdvanceTurn()
		{
			TurnNumber++;
		}

		private BattleState(int turnNumber, IReadOnlyDictionary<int, BattleSquad> attackerSquads,
			IReadOnlyDictionary<int, BattleSquad> opposingSquads, BattleSideProfile attackerProfile,
			BattleSideProfile opposingProfile, BattleSideState attackerSide, BattleSideState opposingSide)
		{
			TurnNumber = turnNumber;
			bool isNewEngagement = attackerSide == null && opposingSide == null;

			// deep copy the squads, which will also deep copy the soldiers in the squads
			_allAttackerBattleSquads = attackerSquads.ToDictionary(
				kvp => kvp.Key,
				kvp => CloneSquad(kvp.Value, isNewEngagement));
			_allOpposingBattleSquads = opposingSquads.ToDictionary(
				kvp => kvp.Key,
				kvp => CloneSquad(kvp.Value, isNewEngagement));
			_attackerBattleSquads = _allAttackerBattleSquads
				.Where(kvp => kvp.Value.Status == BattleSquadStatus.Active)
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			_opposingBattleSquads = _allOpposingBattleSquads
				.Where(kvp => kvp.Value.Status == BattleSquadStatus.Active)
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

			AttackerSide = attackerSide == null
				? CreateSideState(attackerProfile, _allAttackerBattleSquads.Values)
				: new BattleSideState(attackerSide);
			OpposingSide = opposingSide == null
				? CreateSideState(opposingProfile, _allOpposingBattleSquads.Values)
				: new BattleSideState(opposingSide);

			// add the soldiers to the soldier map from the squads
			_soldiers = new Dictionary<int, BattleSoldier>();
			foreach (BattleSquad squad in ActiveAttackerSquads.Values.Concat(ActiveOpposingSquads.Values))
			{
				foreach (BattleSoldier soldier in squad.AbleSoldiers)
				{
					_soldiers[soldier.Soldier.Id] = soldier;
				}
			}
		}

		private static BattleSquad CloneSquad(BattleSquad source, bool removePriorCasualties)
		{
			BattleSquad clone = (BattleSquad)source.Clone();
			if (!removePriorCasualties)
			{
				return clone;
			}

			// A BattleSquad survives for the whole mission, so its full wrapper roster still
			// contains soldiers removed from an earlier engagement. Their underlying injury state
			// is shared and correctly says they cannot fight, but retaining those wrappers in a new
			// BattleState makes them reappear in the next day's formation and starting-strength
			// snapshot. A new engagement begins with survivors only. Turn-to-turn copies do not run
			// this pruning because they must retain the battle's own disengaged and casualty history.
			foreach (BattleSoldier casualty in clone.Soldiers.Where(soldier => !soldier.CanFight).ToList())
			{
				clone.RemoveSoldier(casualty);
			}

			return clone;
		}

		private static BattleSideState CreateSideState(BattleSideProfile profile, IEnumerable<BattleSquad> squads)
		{
			List<BattleSoldier> soldiers = squads.SelectMany(squad => squad.Soldiers).ToList();
			return new BattleSideState(profile,
				soldiers.Sum(soldier => soldier.Soldier.Template.BattleValue), soldiers.Count);
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
			if (AllAttackerSquads.ContainsKey(squadId))
			{
				return AllAttackerSquads[squadId];
			}
			else
			{
				return AllOpposingSquads[squadId];
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
			// that actually owns the squad instead of consulting affiliation flags such as
			// IsPlayerSquad or the report-only IsPlayerAligned flag.
			// Cleanup can encounter several casualties from the same wiped squad. The first
			// casualty removes the squad; subsequent casualties must therefore be harmless.
			BattleSquad ownedSquad = GetSquad(squad.Id);
			ownedSquad.Status = BattleSquadStatus.Eliminated;
			ownedSquad.WithdrawalRole = WithdrawalRole.None;
			_attackerBattleSquads.Remove(ownedSquad.Id);
			_opposingBattleSquads.Remove(ownedSquad.Id);
		}

		public void DisengageSquad(BattleSquad squad)
		{
			if (squad == null) throw new ArgumentNullException(nameof(squad));
			BattleSquad ownedSquad = GetSquad(squad.Id);
			if (ownedSquad.Status != BattleSquadStatus.Active) return;

			ownedSquad.Status = BattleSquadStatus.Disengaged;
			ownedSquad.WithdrawalRole = WithdrawalRole.None;
			_attackerBattleSquads.Remove(ownedSquad.Id);
			_opposingBattleSquads.Remove(ownedSquad.Id);
			foreach (BattleSoldier soldier in ownedSquad.Soldiers)
			{
				_soldiers.Remove(soldier.Soldier.Id);
			}
		}

		public void RemoveSoldier(int soldierId)
		{
			_soldiers.Remove(soldierId);
		}
	}
}
