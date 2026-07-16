using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles.Actions
{
    /// <summary>
    /// A thrown or launched blast (grenade) attack. The delivery check scatters the
    /// impact point on failure; everyone inside the blast circle — friend, foe, and
    /// the thrower himself — is auto-hit, with damage falling off quadratically from
    /// the impact center to the template rim.
    /// </summary>
    public class BlastAttackAction : IAction
    {
        private readonly BattleGridManager _grid;
        private readonly IRNG _random;
        private readonly List<string> _victimNames = [];
        private readonly List<string> _friendlyVictimNames = [];
        private string _soldierName;
        private string _targetName;
        private string _weaponName;
        private bool _isThrown;
        private float _scatterCellDistance;
        private bool _isResolved;

        public int ActorId => ShooterId;
        public int ShooterId { get; }
        public int TargetId { get; }
        public int WeaponId { get; }
        public float Range { get; }
        public bool UseBulk { get; }
        public Tuple<int, int> ImpactCell { get; private set; }
        public bool DidScatter { get; private set; }
        public IReadOnlyList<int> VictimIds { get; private set; } = [];
        public IReadOnlyList<int> FriendlyVictimIds { get; private set; } = [];
        public bool IsFriendlyFire => FriendlyVictimIds.Count > 0;
        public List<WoundResolution> WoundResolutions { get; } = [];

        public BlastAttackAction(
            int shooterId,
            int targetId,
            int weaponId,
            float range,
            bool useBulk,
            BattleGridManager grid,
            IRNG random)
        {
            ShooterId = shooterId;
            TargetId = targetId;
            WeaponId = weaponId;
            Range = range;
            UseBulk = useBulk;
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public void Execute(BattleState state)
        {
            // Scatter, hit locations, and damage must be generated once. Battle history
            // replays execute the same action object again and must reuse the originals.
            if (_isResolved)
            {
                return;
            }

            BattleSoldier shooter = state.GetSoldier(ShooterId);
            BattleSoldier target = state.GetSoldier(TargetId);
            // A grenade rides on the belt (RangedWeapons) without occupying a hand, so it
            // is usually not among the equipped weapons; a launched blast weapon is.
            RangedWeapon weapon = shooter.EquippedRangedWeapons
                .Concat(shooter.RangedWeapons)
                .First(candidate => candidate.Template.Id == WeaponId);
            _soldierName = shooter.Soldier.Name;
            _targetName = target.Soldier.Name;
            _weaponName = weapon.Template.Name;
            _isThrown = weapon.Template.IsThrown;

            // A grenade is thrown at a spot, not a person: range vs a stationary point
            // and Bulk while moving apply; size, RangedEvasion, aim, and rate-of-fire
            // modifiers do not.
            float skill = shooter.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
            float modifier = BattleModifiersUtil.CalculateRangeModifier(Range, 0f);
            if (UseBulk)
            {
                modifier -= weapon.Template.Bulk;
            }
            float roll = 10.5f + (3.0f * (float)_random.NextRandomZValue());
            float margin = skill + modifier - roll;
            double directionRoll = _random.GetLinearDouble();

            ImpactCell = BlastTemplate.ResolveImpactCell(
                _grid, ShooterId, TargetId, margin, directionRoll);
            DidScatter = margin < 0;
            _scatterCellDistance = DidScatter
                ? -margin * BlastTemplate.ScatterDistancePerPoint
                : 0f;

            IReadOnlyList<BlastTemplate.BlastVictim> victims = BlastTemplate.GetVictims(
                _grid, ImpactCell, weapon.Template.AreaRadius);
            VictimIds = victims.Select(victim => victim.SoldierId).ToList();

            bool shooterSide = _grid.GetSoldierSide(ShooterId);
            FriendlyVictimIds = VictimIds
                .Where(victimId => _grid.GetSoldierSide(victimId) == shooterSide)
                .ToList();
            HashSet<int> friendlyVictimIds = FriendlyVictimIds.ToHashSet();

            foreach (BlastTemplate.BlastVictim blastVictim in victims)
            {
                BattleSoldier victim = state.GetSoldier(blastVictim.SoldierId);
                _victimNames.Add(victim.Soldier.Name);
                if (friendlyVictimIds.Contains(blastVictim.SoldierId))
                {
                    _friendlyVictimNames.Add(victim.Soldier.Name);
                }

                WoundResolution woundResolution = HandleHit(
                    shooter,
                    weapon,
                    victim,
                    blastVictim.DistanceFromImpact);
                if (woundResolution != null)
                {
                    WoundResolutions.Add(woundResolution);
                }
            }

            weapon.LoadedAmmo = (ushort)Math.Max(0, weapon.LoadedAmmo - 1);
            shooter.Aim = null;
            shooter.TurnsShooting++;
            _isResolved = true;
        }

        private WoundResolution HandleHit(
            BattleSoldier shooter,
            RangedWeapon weapon,
            BattleSoldier target,
            float distanceFromImpact)
        {
            HitLocation hitLocation = HitLocationCalculator.DetermineHitLocation(target, _random);
            if (hitLocation.IsSevered)
            {
                return null;
            }

            // Quadratic falloff: full damage at the impact center, zero at the rim,
            // applied to the raw damage roll before armor subtraction.
            float falloff = 1f - (distanceFromImpact / weapon.Template.AreaRadius);
            float damage = weapon.Template.DamageMultiplier
                * (3.5f + ((float)_random.NextRandomZValue() * 1.75f))
                * falloff * falloff;
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
            string delivery = _isThrown ? "hurls" : "fires";
            string description = $"{_soldierName} {delivery} a {_weaponName} at {_targetName}\n";
            if (DidScatter)
            {
                string verb = _isThrown ? "throw" : "shot";
                description += $"The {verb} goes wide, landing {_scatterCellDistance:0.#} cells off target\n";
            }

            string victims = _victimNames.Count == 0 ? "no one" : JoinNames(_victimNames);
            description += $"The blast catches {victims}\n";
            if (_friendlyVictimNames.Count > 0)
            {
                description += $"Friendly fire: {JoinNames(_friendlyVictimNames)} caught in the blast\n";
            }

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
