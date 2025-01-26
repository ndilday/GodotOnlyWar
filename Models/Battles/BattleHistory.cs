using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Models.Battles
{
    public class BattleTurn
    {
        public int TurnNumber { get; }
        public List<AimAction> AimActions { get; }
        public List<MeleeAttackAction> MeleeAttackActions { get; }
        public List<ShootAction> ShootActions { get; }
        public List<MoveAction> MoveActions { get; }
        public List<ReadyMeleeWeaponAction> ReadyMeleeWeaponActions { get; }
        public List<ReadyRangedWeaponAction> ReadyRangedWeaponActions { get; }
        public List<ReloadRangedWeaponAction> ReloadActions { get; }
        public List<MoveResolution> MoveResolutions { get; }
        public List<WoundResolution> WoundResolutions { get; }

        public BattleTurn(int turnNumber)
        {
            TurnNumber = turnNumber;
            AimActions = new List<AimAction>();
            MeleeAttackActions = new List<MeleeAttackAction>();
            ShootActions = new List<ShootAction>();
            MoveActions = new List<MoveAction>();
            ReadyMeleeWeaponActions = new List<ReadyMeleeWeaponAction>();
            ReadyRangedWeaponActions = new List<ReadyRangedWeaponAction>();
            ReloadActions = new List<ReloadRangedWeaponAction>();
            MoveResolutions = new List<MoveResolution>();
            WoundResolutions = new List<WoundResolution>();
        }
    }

    public class BattleHistory
    {
        public List<Squad> PlayerSquads { get; }
        public List<Squad> OpposingSquads { get; }
        public List<BattleTurn> Turns { get; }

        public BattleHistory(List<Squad> playerSquads, List<Squad> opposingSquads)
        {
            PlayerSquads = playerSquads;
            OpposingSquads = opposingSquads;
            Turns = new List<BattleTurn>();
        }
    }
}
