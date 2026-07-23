using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class AreaAttackAction : IAction
    {
        private readonly BattleGridManager _grid;
        private readonly IRNG _random;
        private readonly List<string> _victimNames = [];
        private readonly List<string> _friendlyVictimNames = [];
        private string _soldierName;
        private string _weaponName;
        private bool _isResolved;

        public int ActorId => ShooterId;
        public int ShooterId { get; }
        public int TargetId { get; }
        public int WeaponId { get; }
        public IReadOnlyList<int> VictimIds { get; private set; } = [];
        public IReadOnlyList<int> FriendlyVictimIds { get; private set; } = [];
        public bool IsFriendlyFire => FriendlyVictimIds.Count > 0;
        public List<WoundResolution> WoundResolutions { get; } = [];

        public AreaAttackAction(
            int shooterId,
            int targetId,
            int weaponId,
            BattleGridManager grid,
            IRNG random)
        {
            ShooterId = shooterId;
            TargetId = targetId;
            WeaponId = weaponId;
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public void Execute(BattleState state)
        {
            // Hit locations and damage must be generated once. Battle history replays execute
            // the same action object again and must reuse the original resolutions.
            if (_isResolved)
            {
                return;
            }

            BattleSoldier shooter = state.GetSoldier(ShooterId);
            RangedWeapon weapon = shooter.EquippedRangedWeapons
                .First(candidate => candidate.Template.Id == WeaponId);
            _soldierName = shooter.Soldier.Name;
            _weaponName = weapon.Template.Name;

            VictimIds = ConeTemplate.GetVictimIds(
                _grid,
                ShooterId,
                TargetId,
                weapon.Template.MaximumRange,
                weapon.Template.AreaRadius);

            bool shooterSide = _grid.GetSoldierSide(ShooterId);
            FriendlyVictimIds = VictimIds
                .Where(victimId => _grid.GetSoldierSide(victimId) == shooterSide)
                .ToList();
            HashSet<int> friendlyVictimIds = FriendlyVictimIds.ToHashSet();

            foreach (int victimId in VictimIds)
            {
                BattleSoldier victim = state.GetSoldier(victimId);
                _victimNames.Add(victim.Soldier.Name);
                if (friendlyVictimIds.Contains(victimId))
                {
                    _friendlyVictimNames.Add(victim.Soldier.Name);
                }

                WoundResolution woundResolution = HandleHit(shooter, weapon, victim);
                if (woundResolution != null)
                {
                    WoundResolutions.Add(woundResolution);
                }
            }

            weapon.LoadedAmmo = (ushort)Math.Max(0, weapon.LoadedAmmo - weapon.Template.FuelPerBurst);
            shooter.Aim = null;
            shooter.TurnsShooting++;
            _isResolved = true;
        }

        private WoundResolution HandleHit(
            BattleSoldier shooter,
            RangedWeapon weapon,
            BattleSoldier target)
        {
            HitLocation hitLocation = HitLocationCalculator.DetermineHitLocation(target, _random);
            if (hitLocation.IsSevered)
            {
                return null;
            }

            float range = _grid.GetDistanceBetweenSoldiers(ShooterId, target.Soldier.Id);
            float damage = BattleModifiersUtil.CalculateDamageAtRange(weapon, range)
                * (3.5f + ((float)_random.NextRandomZValue() * 1.75f));
            float effectiveArmor = target.Armor.Template.ArmorProvided
                * weapon.Template.ArmorMultiplier;
            float penetratingDamage = damage - effectiveArmor;
            if (penetratingDamage <= 0)
            {
                return null;
            }

            return new WoundResolution(
                shooter,
                weapon.Template,
                target,
                penetratingDamage * weapon.Template.WoundMultiplier,
                hitLocation);
        }

        public string Description()
        {
            string victims = _victimNames.Count == 0 ? "no one" : JoinNames(_victimNames);
            string description = $"{_soldierName} engulfs {victims} in flame with a {_weaponName}\n";
            if (_friendlyVictimNames.Count > 0)
            {
                description += $"Friendly fire: {JoinNames(_friendlyVictimNames)} caught in the flames\n";
            }

            description += ShootAction.DescribeHits(VictimIds.Count, WoundResolutions.Count);
            foreach (WoundResolution wound in WoundResolutions)
            {
                description += wound.Description;
            }

            return description;
        }

        private static string JoinNames(IReadOnlyList<string> names)
        {
            return names.Count switch
            {
                0 => string.Empty,
                1 => names[0],
                2 => $"{names[0]} and {names[1]}",
                _ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}"
            };
        }
    }
}
