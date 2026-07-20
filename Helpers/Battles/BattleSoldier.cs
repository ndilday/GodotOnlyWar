using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSoldier
    {
        private List<RangedWeapon> _equippedRangedWeapons;
        private List<MeleeWeapon> _equippedMeleeWeapons;
        private readonly Dictionary<RangedWeapon, IReadOnlyList<int>> _rangedWeaponHandGroups = [];
        private readonly Dictionary<MeleeWeapon, IReadOnlyList<int>> _meleeWeaponHandGroups = [];
        private bool _synchronizingWeaponGrips;

        public ISoldier Soldier { get; private set; }

        public ValueTuple<int, int>? TopLeft { get; set; }
        public ushort Orientation { get; set; }
        public BattleSquad BattleSquad { get; private set; }

        public List<RangedWeapon> EquippedRangedWeapons
        {
            get
            {
                SynchronizeWeaponGrips();
                return _equippedRangedWeapons;
            }
            private set => _equippedRangedWeapons = value;
        }

        public List<MeleeWeapon> EquippedMeleeWeapons
        {
            get
            {
                SynchronizeWeaponGrips();
                return _equippedMeleeWeapons;
            }
            private set => _equippedMeleeWeapons = value;
        }

        public List<MeleeWeapon> MeleeWeapons { get; private set; }
        public List<RangedWeapon> RangedWeapons { get; private set; }
        public Armor Armor { get; set; }
        public int? TargetId { get; set; }
        public bool IsInMelee { get; set; }
        public ushort ReloadingPhase { get; set; }
        public Stance Stance { get; set; }
        public float CurrentSpeed { get; set; }
        public float LeftoverMovement { get; set; }

        public float TurnsRunning { get; set; }
        public ushort TurnsShooting { get; set; }
        public ushort TurnsSwinging { get; set; } 
        public ushort TurnsDefending { get; set; }
        public ushort TurnsAiming { get; set; }
        public uint WoundsTaken { get; set; }

        public ushort EnemiesTakenDown { get; set; }

        public int HandsFree
        {
            get
            {
                SynchronizeWeaponGrips();
                int occupiedHands = _rangedWeaponHandGroups.Values
                    .Concat(_meleeWeaponHandGroups.Values)
                    .SelectMany(groupIds => groupIds)
                    .Distinct()
                    .Count();
                return Soldier.FunctioningHands - occupiedHands;
            }
        }

        public ValueTuple<int, int>? BottomRight
        {
            get
            {
                if (TopLeft == null) return null;
                if(!BattleOrientation.IsFootprintRotated(Orientation))
                {
                    return new ValueTuple<int, int>(TopLeft.Value.Item1 + Soldier.Template.Species.Width,
                                               TopLeft.Value.Item2 - Soldier.Template.Species.Depth);
                }
                else
                {
                    return new ValueTuple<int, int>(TopLeft.Value.Item1 + Soldier.Template.Species.Depth,
                                               TopLeft.Value.Item2 - Soldier.Template.Species.Width);
                }
            }
        }

        public IReadOnlyList<ValueTuple<int, int>> PositionList
        {
            get
            {
                List<ValueTuple<int, int>> list = [];
                if (TopLeft != null)
                {
                    for (int w = TopLeft.Value.Item1; w < BottomRight.Value.Item1; w++)
                    {
                        for (int d = BottomRight.Value.Item2; d < TopLeft.Value.Item2; d++)
                        {
                            list.Add(new ValueTuple<int, int>(w, d));
                        }
                    }
                }
                return list;
            }
        }

        // aim stores the target, aiming weapon, and addiional seconds the aim has been maintained
        public ValueTuple<int, RangedWeapon, int>? Aim { get; set; }

        public BattleSoldier(ISoldier soldier, BattleSquad squad)
        {
            Soldier = soldier;
            BattleSquad = squad;
            MeleeWeapons = [];
            RangedWeapons = [];
            EquippedMeleeWeapons = [];
            EquippedRangedWeapons = [];
            TopLeft = null;
            Aim = null;
            IsInMelee = false;
            Stance = Stance.Standing;
            CurrentSpeed = 0;
            LeftoverMovement = 0;
            EnemiesTakenDown = 0;
            ReloadingPhase = 0;
            TargetId = null;
        }


        // Copy constructor — the single copy path for BattleSoldier. Used by
        // BattleSquad's copy constructor to snapshot battle state for BattleHistory
        // replay. The underlying ISoldier is shared by design: the replay reads
        // per-snapshot battle fields (position, stance, wounds taken, etc.) and the
        // action log, not an independent body, and the squad back-reference must be
        // set to the cloned squad, which a parameterless Clone() cannot do.
        public BattleSoldier(BattleSoldier soldier, BattleSquad squad)
        {
            Soldier = soldier.Soldier;
            BattleSquad = squad;
            TopLeft = soldier.TopLeft;
            Orientation = soldier.Orientation;
            Armor = soldier.Armor;
            IsInMelee = soldier.IsInMelee;
            ReloadingPhase = soldier.ReloadingPhase;
            Stance = soldier.Stance;
            CurrentSpeed = soldier.CurrentSpeed;
            LeftoverMovement = soldier.LeftoverMovement;
            TurnsRunning = soldier.TurnsRunning;
            TurnsShooting = soldier.TurnsShooting;
            TurnsSwinging = soldier.TurnsSwinging;
            TurnsDefending = soldier.TurnsDefending;
            TurnsAiming = soldier.TurnsAiming;
            WoundsTaken = soldier.WoundsTaken;
            EnemiesTakenDown = soldier.EnemiesTakenDown;
            Aim = soldier.Aim;
            EquippedMeleeWeapons = soldier.EquippedMeleeWeapons.ToList();
            EquippedRangedWeapons = soldier.EquippedRangedWeapons.ToList();
            foreach (MeleeWeapon weapon in EquippedMeleeWeapons)
            {
                _meleeWeaponHandGroups[weapon] = soldier.GetHandGroupIds(weapon).ToArray();
            }
            foreach (RangedWeapon weapon in EquippedRangedWeapons)
            {
                _rangedWeaponHandGroups[weapon] = soldier.GetHandGroupIds(weapon).ToArray();
            }
            MeleeWeapons = soldier.MeleeWeapons;
            RangedWeapons = soldier.RangedWeapons;
            TargetId = soldier.TargetId;
        }
        public bool CanFight
        {
            get
            {
                return Soldier.CanFight;
            }
        }
        
        public void AddWeapons(IReadOnlyCollection<RangedWeapon> rangedWeapons, IReadOnlyCollection<MeleeWeapon> meleeWeapons)
        {
            if (rangedWeapons?.Count > 0)
            {
                RangedWeapons.AddRange(rangedWeapons);
            }
            if (meleeWeapons?.Count > 0)
            {
                MeleeWeapons.AddRange(meleeWeapons);
            }

            // thrown weapons (grenades) ride on the belt and are thrown directly from
            // it, so they never occupy a hand or compete for the equipped slots
            List<RangedWeapon> handRangedWeapons = RangedWeapons.Where(w => !w.Template.IsThrown).ToList();
            if (handRangedWeapons.Count > 0)
            {
                if (handRangedWeapons.Count == 1 )
                {
                    foreach (RangedWeapon weapon in handRangedWeapons)
                    {
                        ReadyWeapon(weapon);
                    }
                }
                else if (handRangedWeapons[0].Template.Location == EquipLocation.OneHand && handRangedWeapons[1].Template.Location == EquipLocation.OneHand)
                {
                    ReadyWeapon(handRangedWeapons[0]);
                    ReadyWeapon(handRangedWeapons[1]);
                }
                else
                {
                    ReadyWeapon(handRangedWeapons[0]);
                }
            }
            if (MeleeWeapons.Count > 0)
            {
                if (EquippedRangedWeapons.Count == 0)
                {
                    // we have two hands free for close combat weapons
                    if (MeleeWeapons.Count == 1)
                    {
                        foreach (MeleeWeapon weapon in MeleeWeapons)
                        {
                            ReadyWeapon(weapon);
                        }
                    }
                    else if (MeleeWeapons[0].Template.Location == EquipLocation.OneHand && MeleeWeapons[1].Template.Location == EquipLocation.OneHand)
                    {
                        ReadyWeapon(MeleeWeapons[0]);
                        ReadyWeapon(MeleeWeapons[1]);

                    }
                    else
                    {
                        ReadyWeapon(MeleeWeapons[0]);
                    }
                }
                else if (EquippedRangedWeapons.Count == 1 && EquippedRangedWeapons[0].Template.Location == EquipLocation.OneHand)
                {
                    if(MeleeWeapons[0].Template.Location == EquipLocation.OneHand)
                    {
                        ReadyWeapon(MeleeWeapons[0]);
                    }
                    else if(MeleeWeapons.Count > 1 && MeleeWeapons[1].Template.Location == EquipLocation.OneHand)
                    {
                        ReadyWeapon(MeleeWeapons[1]);
                    }
                }
            }
        }

        public float GetMoveSpeed()
        {
            float baseMoveSpeed = Soldier.MoveSpeed;
            bool isSlow = Soldier.Body.HitLocations.Where(hl => hl.Template.IsMotive)
                                                       .Any(hl => hl.Wounds.WoundTotal >= (uint)WoundLevel.Major);

            // if leg/foot injuries, slow soldier down
            if (isSlow)
            {
                return baseMoveSpeed * 0.75f;
            }
            return baseMoveSpeed;
        }

        public MeleeWeapon GetPrimaryMeleeWeapon(MeleeWeapon defaultWeapon)
        {
            if (EquippedMeleeWeapons.Count > 0)
            {
                return EquippedMeleeWeapons[0];
            }

            return defaultWeapon;
        }

        public MeleeWeapon GetSecondaryMeleeWeapon()
        {
            return IsDualWieldingMelee() ? EquippedMeleeWeapons[1] : null;
        }

        public bool IsDualWieldingMelee()
        {
            return EquippedMeleeWeapons.Count >= 2
                && EquippedMeleeWeapons[0].Template.Location == EquipLocation.OneHand
                && EquippedMeleeWeapons[1].Template.Location == EquipLocation.OneHand;
        }

        public float GetMeleeParryModifier()
        {
            float total = 0;
            foreach (MeleeWeapon weapon in EquippedMeleeWeapons)
            {
                total += weapon.Template.ParryModifier;
            }

            return total;
        }

        public IReadOnlyList<int> GetHandGroupIds(RangedWeapon weapon)
        {
            SynchronizeWeaponGrips();
            return weapon != null && _rangedWeaponHandGroups.TryGetValue(weapon, out IReadOnlyList<int> groupIds)
                ? groupIds
                : [];
        }

        public IReadOnlyList<int> GetHandGroupIds(MeleeWeapon weapon)
        {
            SynchronizeWeaponGrips();
            return weapon != null && _meleeWeaponHandGroups.TryGetValue(weapon, out IReadOnlyList<int> groupIds)
                ? groupIds
                : [];
        }

        public bool ReadyWeapon(RangedWeapon weapon, IReadOnlyCollection<int> handGroupIds = null)
        {
            if (weapon == null || weapon.Template.IsThrown)
            {
                return false;
            }

            SynchronizeWeaponGrips();
            IReadOnlyList<int> selectedGroups = SelectHandGroups(weapon.Template, handGroupIds);
            if (selectedGroups.Count == 0)
            {
                return false;
            }

            UnequipWeaponsUsing(selectedGroups);
            _equippedRangedWeapons.Add(weapon);
            _rangedWeaponHandGroups[weapon] = selectedGroups;
            return true;
        }

        public bool ReadyWeapon(MeleeWeapon weapon, IReadOnlyCollection<int> handGroupIds = null)
        {
            if (weapon == null)
            {
                return false;
            }

            SynchronizeWeaponGrips();
            IReadOnlyList<int> selectedGroups = SelectHandGroups(weapon.Template, handGroupIds);
            if (selectedGroups.Count == 0)
            {
                return false;
            }

            UnequipWeaponsUsing(selectedGroups);
            _equippedMeleeWeapons.Add(weapon);
            _meleeWeaponHandGroups[weapon] = selectedGroups;
            return true;
        }

        public void DropWeaponsUsingHandGroup(int handGroupId)
        {
            SynchronizeWeaponGrips();
            foreach (RangedWeapon weapon in _rangedWeaponHandGroups
                .Where(entry => entry.Value.Contains(handGroupId))
                .Select(entry => entry.Key)
                .ToList())
            {
                _equippedRangedWeapons.Remove(weapon);
                _rangedWeaponHandGroups.Remove(weapon);
            }
            foreach (MeleeWeapon weapon in _meleeWeaponHandGroups
                .Where(entry => entry.Value.Contains(handGroupId))
                .Select(entry => entry.Key)
                .ToList())
            {
                _equippedMeleeWeapons.Remove(weapon);
                _meleeWeaponHandGroups.Remove(weapon);
            }
        }

        public void ClearReadiedWeapons()
        {
            _equippedRangedWeapons.Clear();
            _equippedMeleeWeapons.Clear();
            _rangedWeaponHandGroups.Clear();
            _meleeWeaponHandGroups.Clear();
        }

        public override string ToString()
        {
            return Soldier.Name;
        }

        private IReadOnlyList<int> SelectHandGroups(
            WeaponTemplate template,
            IReadOnlyCollection<int> requestedGroupIds)
        {
            int requiredHands = GetHandsForWeapon(template);
            IReadOnlyList<int> functioningGroups = Soldier.FunctioningHandGroupIds;
            if (requiredHands == 0 || functioningGroups.Count < requiredHands)
            {
                return [];
            }

            if (requestedGroupIds != null)
            {
                int[] requested = requestedGroupIds.Distinct().ToArray();
                return requested.Length == requiredHands
                    && requested.All(functioningGroups.Contains)
                        ? requested
                        : [];
            }

            HashSet<int> occupied = _rangedWeaponHandGroups.Values
                .Concat(_meleeWeaponHandGroups.Values)
                .SelectMany(groupIds => groupIds)
                .ToHashSet();
            return functioningGroups
                .OrderBy(groupId => occupied.Contains(groupId))
                .ThenBy(groupId => groupId)
                .Take(requiredHands)
                .ToArray();
        }

        private void UnequipWeaponsUsing(IReadOnlyCollection<int> handGroupIds)
        {
            foreach (int groupId in handGroupIds)
            {
                DropWeaponsUsingHandGroup(groupId);
            }
        }

        private void SynchronizeWeaponGrips()
        {
            if (_synchronizingWeaponGrips || _equippedRangedWeapons == null || _equippedMeleeWeapons == null)
            {
                return;
            }

            _synchronizingWeaponGrips = true;
            try
            {
                foreach (RangedWeapon stale in _rangedWeaponHandGroups.Keys
                    .Where(weapon => !_equippedRangedWeapons.Contains(weapon))
                    .ToList())
                {
                    _rangedWeaponHandGroups.Remove(stale);
                }
                foreach (MeleeWeapon stale in _meleeWeaponHandGroups.Keys
                    .Where(weapon => !_equippedMeleeWeapons.Contains(weapon))
                    .ToList())
                {
                    _meleeWeaponHandGroups.Remove(stale);
                }

                HashSet<int> functioningGroups = Soldier.FunctioningHandGroupIds.ToHashSet();
                foreach (RangedWeapon unusable in _rangedWeaponHandGroups
                    .Where(entry => entry.Value.Any(groupId => !functioningGroups.Contains(groupId)))
                    .Select(entry => entry.Key)
                    .ToList())
                {
                    _equippedRangedWeapons.Remove(unusable);
                    _rangedWeaponHandGroups.Remove(unusable);
                }
                foreach (MeleeWeapon unusable in _meleeWeaponHandGroups
                    .Where(entry => entry.Value.Any(groupId => !functioningGroups.Contains(groupId)))
                    .Select(entry => entry.Key)
                    .ToList())
                {
                    _equippedMeleeWeapons.Remove(unusable);
                    _meleeWeaponHandGroups.Remove(unusable);
                }

                BindUntrackedWeapons(_equippedRangedWeapons, _rangedWeaponHandGroups);
                BindUntrackedWeapons(_equippedMeleeWeapons, _meleeWeaponHandGroups);
            }
            finally
            {
                _synchronizingWeaponGrips = false;
            }
        }

        private void BindUntrackedWeapons<TWeapon>(
            List<TWeapon> weapons,
            Dictionary<TWeapon, IReadOnlyList<int>> grips)
            where TWeapon : class
        {
            foreach (TWeapon weapon in weapons.Where(candidate => !grips.ContainsKey(candidate)).ToList())
            {
                if (weapon is RangedWeapon thrownWeapon && thrownWeapon.Template.IsThrown)
                {
                    grips[weapon] = [];
                    continue;
                }

                WeaponTemplate template = weapon switch
                {
                    RangedWeapon ranged => ranged.Template,
                    MeleeWeapon melee => melee.Template,
                    _ => null
                };
                IReadOnlyList<int> selectedGroups = SelectHandGroups(template, null);
                if (selectedGroups.Count == 0)
                {
                    weapons.Remove(weapon);
                    continue;
                }

                UnequipWeaponsUsing(selectedGroups);
                if (!weapons.Contains(weapon))
                {
                    weapons.Add(weapon);
                }
                grips[weapon] = selectedGroups;
            }
        }

        private static int GetHandsForWeapon(WeaponTemplate template)
        {
            return template?.Location switch
            {
                EquipLocation.OneHand => 1,
                EquipLocation.TwoHand => 2,
                _ => 0
            };
        }
    }
}
