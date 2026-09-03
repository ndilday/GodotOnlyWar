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
        private bool _canMove;
        private bool _hasUntreatedSeveredLimb;
        private float _motiveSpeedMultiplier = 1f;
        private Body _cachedInjuryBody;
        private int _cachedInjuryRevision = -1;
        private TakeOutLocationTerm[] _unitTakeOutTerms = [];
        private Body _takeOutTermsBody;
        private int _takeOutTermsRevision = -1;
        private Stance _takeOutTermsStance;
        private Body _weaponGripInjuryBody;
        private int _weaponGripInjuryRevision = -1;
        private bool _synchronizingWeaponGrips;
        private readonly List<RangedWeapon> _rangedWeaponGripWorklist = [];
        private readonly List<MeleeWeapon> _meleeWeaponGripWorklist = [];
        private readonly List<RangedWeapon> _rangedWeaponDropWorklist = [];
        private readonly List<MeleeWeapon> _meleeWeaponDropWorklist = [];

        public ISoldier Soldier { get; private set; }
        /// <summary>
        /// True when this wrapper was equipped from the itemized equipment catalog. Legacy
        /// weapon-set fixtures retain intrinsic tactical value until their allocation path is
        /// migrated, which keeps old mod/test fixtures deterministic during the transition.
        /// </summary>
        public bool UsesItemizedEquipment { get; internal set; }

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

        /// <summary>
        /// This soldier is sprinting this turn (the Run tier). A running soldier cannot bring a
        /// weapon up to parry and is not fighting defensively at all, so in melee his defence
        /// falls back to raw foot speed — see
        /// <see cref="Actions.MeleeAttackAction.GetRunningDefenderMeleeSkill"/>. The planners set
        /// it alongside <see cref="CurrentSpeed"/>, and a soldier who stops to fight clears it.
        /// </summary>
        public bool IsRunning { get; set; }
        public float CurrentSpeed { get; set; }
        public float LeftoverMovement { get; set; }

        public float TurnsRunning { get; set; }
        public ushort TurnsShooting { get; set; }
        public ushort TurnsSwinging { get; set; } 
        public ushort TurnsDefending { get; set; }
        public ushort TurnsAiming { get; set; }
        public uint WoundsTaken { get; set; }

        // Margin-scaled "learn by doing" skill XP accrued from this battle's attack rolls
        // (BattleExperienceCalculator), summed here per roll and applied once in the aftermath.
        // Separate ranged/melee buckets so each lands on the right weapon's related skill.
        public float RangedSkillXp { get; set; }
        public float MeleeSkillXp { get; set; }

        public ushort EnemiesTakenDown { get; set; }

        /// <summary>Hands and vital locations only -- see <see cref="ISoldier.CanFight"/>.</summary>
        public bool CanFight
        {
            get
            {
                EnsureInjuryState();
                return _canFight;
            }
        }

        /// <summary>Motive locations only -- see <see cref="ISoldier.CanMove"/>.</summary>
        public bool CanMove
        {
            get
            {
                EnsureInjuryState();
                return _canMove;
            }
        }

        /// <summary>
        /// Still a participant in the battle: able to fight and able to move, with an untreated
        /// severed limb removing the soldier immediately. This is the predicate the planners,
        /// targeting, and casualty removal use.
        /// </summary>
        public bool IsCombatEffective
        {
            get
            {
                EnsureInjuryState();
                return !_hasUntreatedSeveredLimb && _canFight && _canMove;
            }
        }

        public bool HasUntreatedSeveredLimb
        {
            get
            {
                EnsureInjuryState();
                return _hasUntreatedSeveredLimb;
            }
        }

        /// <summary>
        /// What his motive wounds leave of his foot speed, 1.0 down to 0.0 -- see
        /// <see cref="OnlyWar.Models.Soldiers.MotiveImpairment"/>. Replaces the old binary
        /// <c>IsSlow</c> / flat x0.75, which fired at Major on any motive location and said
        /// nothing about how bad the wound was.
        /// </summary>
        public float MotiveSpeedMultiplier
        {
            get
            {
                EnsureInjuryState();
                return _motiveSpeedMultiplier;
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

        /// <summary>
        /// This soldier's take-out location vector at the unit weapon -- see
        /// <see cref="RemovalMath.BuildUnitTakeOutTerms"/>, which is where the meaning lives. Every
        /// shooter, weapon and range scoring against him rescales this one vector instead of
        /// re-walking his body, so the memo is the difference between one body traversal per
        /// (soldier, stance, injury) and one per (shooter, weapon, range, candidate option).
        ///
        /// Cached on the same terms as the injury state above -- body identity plus
        /// <see cref="Body.InjuryRevision"/> -- and additionally on <see cref="Stance"/>, which
        /// sets the hit-lottery weights the vector is normalized against. Like the injury state it
        /// is materialized up front by <see cref="PrepareForParallelPlanning"/> so planning workers
        /// only ever read it.
        /// </summary>
        internal IReadOnlyList<TakeOutLocationTerm> UnitTakeOutTerms
        {
            get
            {
                EnsureTakeOutTerms();
                return _unitTakeOutTerms;
            }
        }
        public bool CanUseTwoHandedWeapon => FunctioningHands >= 2;

        /// <summary>
        /// Tactical value of the currently resolved equipment. This is intentionally derived from
        /// live carried templates, not stored back onto SoldierTemplate.BattleValue, so strategic
        /// force-generation accounting remains intrinsic.
        /// </summary>
        public int EffectiveBattleValue => UsesItemizedEquipment
            ? EffectiveBattleValueCalculator.CalculateRuntime(
                Soldier,
                Armor,
                RangedWeapons,
                MeleeWeapons)
            : Soldier?.Template?.BattleValue ?? 0;

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
            IsRunning = false;
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
            : this(soldier, squad, null, null)
        {
        }

        internal BattleSoldier(
            BattleSoldier soldier,
            BattleSquad squad,
            IReadOnlyDictionary<RangedWeapon, RangedWeapon> rangedWeaponCopies,
            IReadOnlyDictionary<MeleeWeapon, MeleeWeapon> meleeWeaponCopies)
        {
            Soldier = soldier.Soldier;
            BattleSquad = squad;
            // Equipped-weapon getters synchronize their grip assignments. Initialize the
            // injury cache before copying those collections so synchronization sees the
            // soldier's actual functioning hand groups instead of the empty defaults.
            RefreshInjuryState();
            TopLeft = soldier.TopLeft;
            Orientation = soldier.Orientation;
            Armor = soldier.Armor == null ? null : new Armor(soldier.Armor.Template);
            UsesItemizedEquipment = soldier.UsesItemizedEquipment;
            IsInMelee = soldier.IsInMelee;
            ReloadingPhase = soldier.ReloadingPhase;
            Stance = soldier.Stance;
            IsRunning = soldier.IsRunning;
            CurrentSpeed = soldier.CurrentSpeed;
            LeftoverMovement = soldier.LeftoverMovement;
            TurnsRunning = soldier.TurnsRunning;
            TurnsShooting = soldier.TurnsShooting;
            TurnsSwinging = soldier.TurnsSwinging;
            TurnsDefending = soldier.TurnsDefending;
            TurnsAiming = soldier.TurnsAiming;
            WoundsTaken = soldier.WoundsTaken;
            RangedSkillXp = soldier.RangedSkillXp;
            MeleeSkillXp = soldier.MeleeSkillXp;
            EnemiesTakenDown = soldier.EnemiesTakenDown;
            Dictionary<RangedWeapon, RangedWeapon> rangedCopies = rangedWeaponCopies == null
                ? soldier.RangedWeapons.ToDictionary(weapon => weapon, weapon => weapon.DeepCopy())
                : new Dictionary<RangedWeapon, RangedWeapon>(rangedWeaponCopies);
            Dictionary<MeleeWeapon, MeleeWeapon> meleeCopies = meleeWeaponCopies == null
                ? soldier.MeleeWeapons.ToDictionary(weapon => weapon, weapon => new MeleeWeapon(weapon.Template))
                : new Dictionary<MeleeWeapon, MeleeWeapon>(meleeWeaponCopies);
            Aim = soldier.Aim is (int, RangedWeapon, int) aim
                && rangedCopies.TryGetValue(aim.Item2, out RangedWeapon aimedWeapon)
                ? (aim.Item1, aimedWeapon, aim.Item3)
                : null;
            _equippedMeleeWeapons.AddRange(soldier.EquippedMeleeWeapons
                .Select(weapon => meleeCopies.GetValueOrDefault(weapon))
                .Where(weapon => weapon != null));
            _equippedRangedWeapons.AddRange(soldier.EquippedRangedWeapons
                .Select(weapon => rangedCopies.GetValueOrDefault(weapon))
                .Where(weapon => weapon != null));
            foreach (MeleeWeapon weapon in EquippedMeleeWeapons)
            {
                MeleeWeapon originalWeapon = meleeCopies
                    .First(pair => ReferenceEquals(pair.Value, weapon)).Key;
                _meleeWeaponHandGroups[weapon] = soldier.GetHandGroupIds(originalWeapon).ToArray();
            }
            foreach (RangedWeapon weapon in EquippedRangedWeapons)
            {
                RangedWeapon originalWeapon = rangedCopies
                    .First(pair => ReferenceEquals(pair.Value, weapon)).Key;
                _rangedWeaponHandGroups[weapon] = soldier.GetHandGroupIds(originalWeapon).ToArray();
            }
            MeleeWeapons = soldier.MeleeWeapons
                .Select(weapon => meleeCopies.GetValueOrDefault(weapon))
                .Where(weapon => weapon != null)
                .ToList();
            RangedWeapons = soldier.RangedWeapons
                .Select(weapon => rangedCopies.GetValueOrDefault(weapon))
                .Where(weapon => weapon != null)
                .ToList();
            TargetId = soldier.TargetId;
        }
        
        /// <summary>
        /// Strips everything this soldier is carrying, so a squad's loadout can be redistributed.
        /// </summary>
        /// <remarks>
        /// <see cref="AddWeapons"/> APPENDS - it does an AddRange and then re-grips. Re-running squad
        /// allocation without clearing first would leave a soldier carrying two bolters after the second
        /// battle of a mission and three after the third. This is the paired clear.
        ///
        /// Aim and ReloadingPhase go too: both reference or describe a specific weapon, and after a
        /// redistribution the soldier may not be holding it any more.
        /// </remarks>
        internal void ClearWeapons()
        {
            RangedWeapons.Clear();
            MeleeWeapons.Clear();
            _equippedRangedWeapons.Clear();
            _equippedMeleeWeapons.Clear();
            _rangedWeaponHandGroups.Clear();
            _meleeWeaponHandGroups.Clear();
            Aim = null;
            ReloadingPhase = 0;
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

            if (ReadyInitialPreferences())
            {
                return;
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

        /// <summary>Whether this soldier's armor permits the Run tactical tier.</summary>
        public bool CanRun => Armor?.Template?.PreventsRunning != true;

        private bool ReadyInitialPreferences()
        {
            List<(int Order, RangedWeapon Ranged, MeleeWeapon Melee)> preferences = RangedWeapons
                .Where(weapon => !weapon.Template.IsThrown && weapon.InitialReadyOrder.HasValue)
                .Select(weapon => (weapon.InitialReadyOrder.Value, weapon, (MeleeWeapon)null))
                .Concat(MeleeWeapons
                    .Where(weapon => weapon.InitialReadyOrder.HasValue)
                    .Select(weapon => (weapon.InitialReadyOrder.Value, (RangedWeapon)null, weapon)))
                // Lower numbers have higher priority. Apply lower-priority choices first so a
                // later high-priority two-handed choice can displace them when hands conflict.
                .OrderByDescending(item => item.Item1)
                .ToList();
            if (preferences.Count == 0) return false;

            foreach ((int _, RangedWeapon ranged, MeleeWeapon melee) in preferences)
            {
                if (ranged != null) ReadyWeapon(ranged);
                else ReadyWeapon(melee);
            }
            return true;
        }

        /// <summary>
        /// Foot speed after motive wounds. Zero only when he cannot walk at all, and a soldier in
        /// that state is not combat effective, so the planners have already dropped him from
        /// <see cref="BattleSquad.AbleSoldiers"/> before this could return 0.
        /// </summary>
        public float GetMoveSpeed()
        {
            return Soldier.MoveSpeed * MotiveSpeedMultiplier;
        }

        internal void RefreshInjuryState()
        {
            Body body = Soldier.Body;
            // Held by reference, not copied: Soldier caches this on the same InjuryRevision key
            // and already hands out an immutable view, so the old ToArray/AsReadOnly pair was two
            // allocations buying nothing.
            IReadOnlyList<int> functioningHandGroupIds = Soldier.FunctioningHandGroupIds;
            _functioningHandGroupIds = functioningHandGroupIds;
            _functioningHandGroupIdSet = new HashSet<int>(functioningHandGroupIds);
            _canFight = Soldier.CanFight;
            _motiveSpeedMultiplier = MotiveImpairment.CalculateSpeedMultiplier(body);
            _canMove = _motiveSpeedMultiplier > 0f;
            _hasUntreatedSeveredLimb = Soldier.HasUntreatedSeveredLimb;

            _cachedInjuryBody = body;
            _cachedInjuryRevision = body.InjuryRevision;
        }

        /// <summary>
        /// Materializes every lazily maintained value that battle planners may read from another
        /// worker. Battle planning runs against a frozen turn-start state, so these values remain
        /// valid until action execution begins.
        /// </summary>
        internal void PrepareForParallelPlanning()
        {
            RefreshInjuryState();
            SynchronizeWeaponGrips();
            // After the injury refresh, not before: the vector reads IsCombatEffective and
            // FunctioningHandGroupIds, both of which are injury-derived.
            RefreshTakeOutTerms();
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

        private void EnsureTakeOutTerms()
        {
            Body body = Soldier.Body;
            if (!ReferenceEquals(_takeOutTermsBody, body)
                || _takeOutTermsRevision != body.InjuryRevision
                || _takeOutTermsStance != Stance)
            {
                RefreshTakeOutTerms();
            }
        }

        private void RefreshTakeOutTerms()
        {
            Body body = Soldier.Body;
            _unitTakeOutTerms = RemovalMath.BuildUnitTakeOutTerms(this);
            _takeOutTermsBody = body;
            _takeOutTermsRevision = body.InjuryRevision;
            _takeOutTermsStance = Stance;
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
