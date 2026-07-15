using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    internal sealed class PlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        private readonly PlayerForce _playerForce;

        public PlayerBattleAftermathSink(PlayerForce playerForce)
        {
            // NPC-only and reduced simulation sessions may legitimately have no player force.
            // Keep the boundary available for those sessions, but fail explicitly if a player
            // aftermath policy ever tries to apply a campaign effect without player state.
            _playerForce = playerForce;
        }

        public void MoveToFallenBrothers(PlayerSoldier soldier)
        {
            if (soldier == null)
            {
                throw new ArgumentNullException(nameof(soldier));
            }
            PlayerForce playerForce = RequirePlayerForce();
            if (playerForce.Army == null)
            {
                throw new InvalidOperationException(
                    "A player army is required to record a fallen battle participant.");
            }

            soldier.AssignedSquad?.RemoveSquadMember(soldier);
            soldier.AssignedSquad = null;
            playerForce.Army.PlayerSoldierMap.Remove(soldier.Id);
            playerForce.Army.FallenBrothers[soldier.Id] = soldier;
        }

        public void AddRecoveredGeneseed(float purity) =>
            RequirePlayerForce().AddRecoveredGeneseed(purity);

        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) =>
            RequirePlayerForce().AddToBattleHistory(date, title, subEvents.ToList());

        private PlayerForce RequirePlayerForce()
        {
            if (_playerForce == null)
            {
                throw new InvalidOperationException(
                    "Player battle aftermath requires a player force in the current game session.");
            }

            return _playerForce;
        }
    }
}
