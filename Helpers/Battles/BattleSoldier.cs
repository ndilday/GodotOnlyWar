using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSoldier
    {
        private readonly List<RangedWeapon> _equippedRangedWeapons = [];
        private readonly List<MeleeWeapon> _equippedMeleeWeapons = [];
        private readonly Dictionary<RangedWeapon, IReadOnlyList<int>> _rangedWeaponHandGroups = [];
        private readonly Dictionary<MeleeWeapon, IReadOnlyList<int>> _meleeWeaponHandGroups = [];
        private IReadOnlyList<int> _functioningHandGroupIds = Array.Empty<int>();
        private HashSet<int> _functioningHandGroupIdSet = [];
        private bool _canFight;
        private bool _isSlow;
        private Body _cachedInjuryBody;
        private int _cachedInjuryRevision = -1;
        private Body _weaponGripInjuryBody;
        private int _weaponGripInjuryRevision = -1;
        private bool _synchronizingWeaponGrips;
        private readonly List<RangedWeapon> _rangedWeaponGripWorklist = [];
        private readonly List<MeleeWeapon> _meleeWeaponGripWorklist = [];
        private readonly List<RangedWeapon> _rangedWeaponDropWorklist = [];
        private readonly List<MeleeWeapon> _meleeWeaponDropWorklist = [];

        public ISoldier Soldier { get; private set; }

        public ValueTuple<int, int>? TopLeft { get; set; }
        public ushort Orientation { get; set; }
        public BattleSquad BattleSquad { get; private set; }

        public IReadOnlyList<RangedWeapon> EquippedRangedWeapons
        {
            get
            {
                SynchronizeWeaponGrips();
                return _equippedRangedWeapons;
            }
        }

        public IReadOnlyList<MeleeWeapon> EquippedMeleeWeapons
        {
            get
            {
                SynchronizeWeaponGrips();
                return _equippedMeleeWeapons;
            }
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

        public bool CanFight
        {
            get
            {
                EnsureInjuryState();
                return _canFight;
            }
        }

        public bool IsSlow
        {
            get
            {
                EnsureInjuryState();
                return _isSlow;
            }
        }

        public IReadOnlyList<int> FunctioningHandGroupIds
        {
            get
            {
                EnsureInjuryState();
                return _functioningHandGroupIds;
            }
        }

        public int FunctioningHands
        {
            get
            {
                EnsureInjuryState();
                return _functioningHandGroupIds.Count;
            }
        }
        public bool CanUseTwoHandedWeapon => FunctioningHands >= 2;

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
                return FunctioningHands - occupiedHands;
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
            TopLeft = null;
            Aim = null;
            IsInMelee = false;
            Stance = Stance.Standing;
            CurrentSpeed = 0;
            LeftoverMovement = 0;
            EnemiesTakenDown = 0;
            ReloadingPhase = 0;
            TargetId = null;
            RefreshInjuryState();
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
            // Equipped-weapon getters synchronize their grip assignments. Initialize the
            // injury cache before copying those collections so synchronization sees the
            // soldier's actual functioning hand groups instead of the empty defaults.
            RefreshInjuryState();
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
            _equippedMeleeWeapons.AddRange(soldier.EquippedMeleeWeapons);
            _equippedRangedWeapons.AddRange(soldier.EquippedRangedWeapons);
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

            // if leg/foot injuries, slow soldier down
            if (IsSlow)
            {
                return baseMoveSpeed * 0.75f;
            }
            return baseMoveSpeed;
        }

        internal void RefreshInjuryState()
        {
            Body body = Soldier.Body;
            int[] functioningHandGroupIds = Soldier.FunctioningHandGroupIds.ToArray();
            _functioningHandGroupIds = Array.AsReadOnly(functioningHandGroupIds);
            _functioningHandGroupIdSet = functioningHandGroupIds.ToHashSet();
            _canFight = Soldier.CanFight;

            _isSlow = false;
            foreach (HitLocation location in body.HitLocations)
            {
                if (location.Template.IsMotive
                    && location.Wounds.WoundTotal >= (uint)WoundLevel.Major)
                {
                    _isSlow = true;
                    break;
                }
            }

            _cachedInjuryBody = body;
            _cachedInjuryRevision = body.InjuryRevision;
        }

        private void EnsureInjuryState()
        {
            Body body = Soldier.Body;
            if (!ReferenceEquals(_cachedInjuryBody, body)
                || _cachedInjuryRevision != body.InjuryRevision)
            {
                RefreshInjuryState();
            }
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
            if (weapon == null)
            {
                return false;
            }

            SynchronizeWeaponGrips();
            if (weapon.Template.IsThrown)
            {
                if (!_equippedRangedWeapons.Contains(weapon))
                {
                    _equippedRangedWeapons.Add(weapon);
                }
                _rangedWeaponHandGroups[weapon] = [];
                return true;
            }

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
            _rangedWeaponDropWorklist.Clear();
            foreach (KeyValuePair<RangedWeapon, IReadOnlyList<int>> entry in _rangedWeaponHandGroups)
            {
                if (entry.Value.Contains(handGroupId))
                {
                    _rangedWeaponDropWorklist.Add(entry.Key);
                }
            }
            foreach (RangedWeapon weapon in _rangedWeaponDropWorklist)
            {
                _equippedRangedWeapons.Remove(weapon);
                _rangedWeaponHandGroups.Remove(weapon);
            }
            _rangedWeaponDropWorklist.Clear();

            _meleeWeaponDropWorklist.Clear();
            foreach (KeyValuePair<MeleeWeapon, IReadOnlyList<int>> entry in _meleeWeaponHandGroups)
            {
                if (entry.Value.Contains(handGroupId))
                {
                    _meleeWeaponDropWorklist.Add(entry.Key);
                }
            }
            foreach (MeleeWeapon weapon in _meleeWeaponDropWorklist)
            {
                _equippedMeleeWeapons.Remove(weapon);
                _meleeWeaponHandGroups.Remove(weapon);
            }
            _meleeWeaponDropWorklist.Clear();
        }

        public void ClearReadiedWeapons()
        {
            ClearReadiedRangedWeapons();
            ClearReadiedMeleeWeapons();
        }

        public void ClearReadiedRangedWeapons()
        {
            _equippedRangedWeapons.Clear();
            _rangedWeaponHandGroups.Clear();
        }

        public void ClearReadiedMeleeWeapons()
        {
            _equippedMeleeWeapons.Clear();
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
            IReadOnlyList<int> functioningGroups = FunctioningHandGroupIds;
            if (requiredHands == 0 || functioningGroups.Count < requiredHands)
            {
                return [];
            }

            if (requestedGroupIds != null)
            {
                int[] requested = requestedGroupIds.Distinct().ToArray();
                return requested.Length == requiredHands
                    && requested.All(_functioningHandGroupIdSet.Contains)
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
            Body body = Soldier.Body;
            bool injuryStateChanged = !ReferenceEquals(_weaponGripInjuryBody, body)
                || _weaponGripInjuryRevision != body.InjuryRevision;
            EnsureInjuryState();
            if (_synchronizingWeaponGrips || _equippedRangedWeapons == null || _equippedMeleeWeapons == null)
            {
                return;
            }

            // Equipment changes are routed through this class and keep the grip mappings
            // synchronized eagerly. Getter reads only need to react to injury changes.
            if (!injuryStateChanged)
            {
                return;
            }

            _synchronizingWeaponGrips = true;
            try
            {
                RemoveStaleOrUnusableWeapons(
                    _equippedRangedWeapons,
                    _rangedWeaponHandGroups,
                    _rangedWeaponGripWorklist);
                RemoveStaleOrUnusableWeapons(
                    _equippedMeleeWeapons,
                    _meleeWeaponHandGroups,
                    _meleeWeaponGripWorklist);

                BindUntrackedWeapons(
                    _equippedRangedWeapons,
                    _rangedWeaponHandGroups,
                    _rangedWeaponGripWorklist);
                BindUntrackedWeapons(
                    _equippedMeleeWeapons,
                    _meleeWeaponHandGroups,
                    _meleeWeaponGripWorklist);
                _weaponGripInjuryBody = body;
                _weaponGripInjuryRevision = body.InjuryRevision;
            }
            finally
            {
                _synchronizingWeaponGrips = false;
            }
        }

        private void BindUntrackedWeapons<TWeapon>(
            List<TWeapon> weapons,
            Dictionary<TWeapon, IReadOnlyList<int>> grips,
            List<TWeapon> worklist)
            where TWeapon : class
        {
            worklist.Clear();
            foreach (TWeapon weapon in weapons)
            {
                if (!grips.ContainsKey(weapon))
                {
                    worklist.Add(weapon);
                }
            }

            foreach (TWeapon weapon in worklist)
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
            worklist.Clear();
        }

        private void RemoveStaleOrUnusableWeapons<TWeapon>(
            List<TWeapon> weapons,
            Dictionary<TWeapon, IReadOnlyList<int>> grips,
            List<TWeapon> worklist)
            where TWeapon : class
        {
            worklist.Clear();
            foreach (KeyValuePair<TWeapon, IReadOnlyList<int>> entry in grips)
            {
                bool unusable = false;
                foreach (int groupId in entry.Value)
                {
                    if (!_functioningHandGroupIdSet.Contains(groupId))
                    {
                        unusable = true;
                        break;
                    }
                }
                if (!weapons.Contains(entry.Key) || unusable)
                {
                    worklist.Add(entry.Key);
                }
            }

            foreach (TWeapon weapon in worklist)
            {
                weapons.Remove(weapon);
                grips.Remove(weapon);
            }
            worklist.Clear();
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
