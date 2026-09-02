using OnlyWar.Models;
﻿using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Equippables;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSquad : ICloneable
    {
        private static int _globalAbleSoldiersVersion;

        internal static int AbleSoldiersGeneration =>
            System.Threading.Volatile.Read(ref _globalAbleSoldiersVersion);
        private List<BattleSoldier> _ableSoldiers;
        private int _ableSoldiersSourceCount = -1;
        private int _cachedGlobalAbleSoldiersVersion = -1;
        private int _ableSoldiersVersion;
        private int _statisticsVersion = -1;
        private float _averageArmor;
        private float _averageSize;
        private float _averageRangedEvasion;
        private float _averageConstitution;
        private float _squadMove;
        private readonly int _missionStartingAbleSoldierCount;
        // Physical weapon objects are the sole mutable equipment state for the lifetime of this
        // mission squad. Itemized weapons share AmmunitionReservePool instances; legacy WeaponSet
        // weapons retain weapon-local reserves until the pooled doctrine UI is migrated.
        private readonly List<RangedWeapon> _missionRangedWeapons = [];
        private readonly List<MeleeWeapon> _missionMeleeWeapons = [];
        private readonly Dictionary<(int TemplateId, bool Itemized), int> _rangedAllocationCursor = [];
        private readonly Dictionary<(int TemplateId, bool Itemized), int> _meleeAllocationCursor = [];

        public int Id { get; private set; }
        public string Name { get; private set; }
        public List<BattleSoldier> Soldiers { get; private set; }
        public float CoverModifier { get; private set; }
        public bool IsPlayerSquad { get; private set; }
        // Presentation-side affiliation for battle reports. The Chapter and the Imperial PDF
        // fight on the same side, but IsPlayerSquad must remain Chapter-only because battle rules
        // use it to distinguish player-controlled missions from NPC missions.
        public bool IsPlayerAligned => IsPlayerSquad || Faction?.IsDefaultFaction == true;
        public Faction Faction { get; private set; }
        public PlayerSoldier CampaignCharacter { get; private set; }
        public BattleElementTraits Traits { get; private set; } = new();
        public Squad CampaignSquad => Squad;
        public bool IsInMelee { get; set; }
        public SquadMovementTier MovementTier { get; set; }
        public BattleSquadStatus Status { get; set; }
        public WithdrawalRole WithdrawalRole { get; set; }
        // Latest morale outcome (OnlyWar_TDD.md §6.6). Set each turn by the
        // resolver's morale stage. Steady/Shaken are non-sticky; Routing is mirrored onto
        // WithdrawalRole.Routing. Read by the planner to degrade a Shaken squad's actions.
        public MoraleState MoraleState { get; set; }
        public EngagementOptionKind? LastEngagementOptionKind { get; set; }
        public int? LastScreenThreatSquadId { get; set; }
        public int? LastProtectedSquadId { get; set; }

        public Squad Squad { get; }
        public List<BattleSoldier> AbleSoldiers
        {
            get
            {
                // This property is read repeatedly while planning every soldier's turn. Reuse the
                // filtered list until a wound/removal can change combat eligibility. The count
                // check also keeps direct test/setup mutations of the public Soldiers list safe.
                int globalVersion = System.Threading.Volatile.Read(ref _globalAbleSoldiersVersion);
                if (_ableSoldiers == null
                    || _ableSoldiersSourceCount != Soldiers.Count
                    || _cachedGlobalAbleSoldiersVersion != globalVersion)
                {
                    _ableSoldiers = Soldiers.Where(s => s.IsCombatEffective).ToList();
                    _ableSoldiersSourceCount = Soldiers.Count;
                    _cachedGlobalAbleSoldiersVersion = globalVersion;
                    _ableSoldiersVersion++;
                    _statisticsVersion = -1;
                }

                return _ableSoldiers;
            }
        }

        public BattleSoldier SquadLeader
        {
            get
            {
                return AbleSoldiers.FirstOrDefault(s => s.Soldier.Template.IsSquadLeader);
            }
        }

        // A squad burrows only if every able member can — burrowing is a whole-unit
        // tunnelling maneuver, not something a mixed squad does piecemeal. Drives
        // eruption-into-melee placement (see OnlyWar_TDD.md §6.6).
        public bool CanBurrow
        {
            get
            {
                List<BattleSoldier> able = AbleSoldiers;
                return able.Count > 0
                    && able.All(s => s.Soldier.Template.Species.Abilities.HasFlag(SpeciesAbilities.Burrow));
            }
        }

        // Run is a squad-level tactical tier. A single member in restrictive armor therefore
        // keeps the whole formation from declaring Run; this avoids giving a mixed squad a tier
        // that its slowest member cannot legally perform.
        public bool CanRun => AbleSoldiers.Count > 0 && AbleSoldiers.All(soldier => soldier.CanRun);

        // A squad "provides synapse" iff any of its soldier templates' species carries the
        // ability (OnlyWar_TDD.md §6.6). This reads the full roster, not
        // AbleSoldiers — it describes squad composition, not current combat capability.
        // Squads are species-homogeneous (§3.1), so in practice this is all-or-nothing, but
        // it is written to tolerate a future mixed template without change.
        public bool SquadProvidesSynapse
        {
            get
            {
                return Traits.ProvidesSynapse
                    || Soldiers.Any(s => s.Soldier.Template.Species.Abilities.HasFlag(SpeciesAbilities.Synapse));
            }
        }

        // An HQ squad projects the §4.3 command aura (OnlyWar_TDD.md §6.6; Phase
        // 6). Unlike synapse, SquadTypes.HQ IS the right set for command (§3.2) — a Tyranid
        // Warrior squad provides synapse but no command aura, while every faction's HQ
        // (Captain, Warboss, Hive Tyrant) provides command. Radius and strength are
        // morale-owned code constants (MoraleConstants), never DB data.
        public bool SquadProvidesCommandAura =>
            Traits.ProvidesCommandAura
            || Squad?.SquadTemplate?.SquadType.HasFlag(SquadTypes.HQ) == true;

        // The aura radius this squad projects if it provides command, else 0. Command reach
        // is personal: the best able soldier's (Ego + Tactics skill total) scaled by
        // MoraleConstants.CommandAuraRadiusPerPoint. Using the best ABLE soldier means a
        // downed Captain's surviving Lieutenant still projects a (smaller) aura, and the
        // radius degrades naturally as the command squad is whittled down.
        public float GetCommandAuraRadius(BaseSkill tacticsSkill)
        {
            if (!SquadProvidesCommandAura || tacticsSkill == null)
            {
                return 0f;
            }
            float best = 0f;
            foreach (BattleSoldier soldier in AbleSoldiers)
            {
                float points = soldier.Soldier.Ego
                    + soldier.Soldier.GetTotalSkillValue(tacticsSkill);
                if (points > best)
                {
                    best = points;
                }
            }
            return best * MoraleConstants.CommandAuraRadiusPerPoint;
        }

        // The aura radius this squad projects if it provides synapse, else 0. When multiple
        // synapse-carrying species could coexist in one squad, the largest radius governs.
        public float SynapseRadius
        {
            get
            {
                float max = 0f;
                foreach (BattleSoldier soldier in Soldiers)
                {
                    if (soldier.Soldier.Template.Species.Abilities.HasFlag(SpeciesAbilities.Synapse)
                        && soldier.Soldier.Template.Species.SynapseRadius > max)
                    {
                        max = soldier.Soldier.Template.Species.SynapseRadius;
                    }
                }
                return max;
            }
        }

        public BattleSquad(bool isPlayerSquad, Squad squad)
        {
            Id = squad.Id;
            Name = squad.Name;
            Squad = squad;
            Faction = squad.Faction;
            Soldiers = SoldierPresenceService.PresentMembers(squad)
                .Select(s => new BattleSoldier(s, this)).ToList();
            _missionStartingAbleSoldierCount = AbleSoldiers.Count;
            IsPlayerSquad = isPlayerSquad;
            Traits = new BattleElementTraits(
                ProvidesCommandAura: squad.SquadTemplate?.SquadType.HasFlag(SquadTypes.HQ) == true,
                ProvidesSynapse: squad.SquadTemplate?.ProvidesSynapse == true,
                IsHeadquarters: squad.SquadTemplate?.SquadType.HasFlag(SquadTypes.HQ) == true);
            IsInMelee = false;
            MovementTier = SquadMovementTier.Stationary;
            Status = BattleSquadStatus.Active;
            WithdrawalRole = WithdrawalRole.None;
            MoraleState = MoraleState.Steady;
            LastEngagementOptionKind = null;
            LastScreenThreatSquadId = null;
            LastProtectedSquadId = null;
            // order weapon sets by strength of primary weapon
            AllocateEquipment();
        }

        public BattleSquad(BattleElementSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            Id = spec.TacticalId;
            Name = spec.Name;
            Squad = spec.CampaignSquad;
            CampaignCharacter = spec.CampaignCharacter;
            Faction = spec.Faction ?? CampaignCharacter?.AssignedSquad?.Faction ?? Squad?.Faction;
            Traits = spec.Traits ?? new BattleElementTraits();
            Soldiers = (spec.Members ?? [])
                .Where(member => member != null)
                .Select(member => new BattleSoldier(member, this))
                .ToList();
            _missionStartingAbleSoldierCount = AbleSoldiers.Count;
            IsPlayerSquad = Faction?.IsPlayerFaction == true;
            IsInMelee = false;
            MovementTier = SquadMovementTier.Stationary;
            Status = BattleSquadStatus.Active;
            WithdrawalRole = WithdrawalRole.None;
            MoraleState = MoraleState.Steady;
            AllocateEquipment();
        }

        private BattleSquad(BattleSquad original)
        {
            Id = original.Id;
            Name = original.Name;
            // we shouldn't need to clone the squad
            Squad = original.Squad;
            CampaignCharacter = original.CampaignCharacter;
            Faction = original.Faction;
            Traits = original.Traits;
            IsPlayerSquad = original.IsPlayerSquad;
            IsInMelee = original.IsInMelee;
            MovementTier = original.MovementTier;
            Status = original.Status;
            WithdrawalRole = original.WithdrawalRole;
            MoraleState = original.MoraleState;
            LastEngagementOptionKind = original.LastEngagementOptionKind;
            LastScreenThreatSquadId = original.LastScreenThreatSquadId;
            LastProtectedSquadId = original.LastProtectedSquadId;
            _missionStartingAbleSoldierCount = original._missionStartingAbleSoldierCount;
            Dictionary<AmmunitionReservePool, AmmunitionReservePool> reservePoolCopies = [];
            Dictionary<RangedWeapon, RangedWeapon> rangedWeaponCopies = original._missionRangedWeapons
                .ToDictionary(
                    weapon => weapon,
                    weapon => weapon.DeepCopy(CopyReservePool(weapon.ReservePool, reservePoolCopies)));
            Dictionary<MeleeWeapon, MeleeWeapon> meleeWeaponCopies = original._missionMeleeWeapons
                .ToDictionary(
                    weapon => weapon,
                    weapon => new MeleeWeapon(
                        weapon.Template,
                        weapon.InitialReadyOrder,
                        weapon.IsItemized));
            foreach (BattleSoldier soldier in original.Soldiers)
            {
                foreach (RangedWeapon weapon in soldier.RangedWeapons
                             .Where(weapon => !rangedWeaponCopies.ContainsKey(weapon)))
                {
                    rangedWeaponCopies[weapon] = weapon.DeepCopy(
                        CopyReservePool(weapon.ReservePool, reservePoolCopies));
                }
                foreach (MeleeWeapon weapon in soldier.MeleeWeapons
                             .Where(weapon => !meleeWeaponCopies.ContainsKey(weapon)))
                {
                    meleeWeaponCopies[weapon] = new MeleeWeapon(
                        weapon.Template,
                        weapon.InitialReadyOrder,
                        weapon.IsItemized);
                }
            }
            _missionRangedWeapons.AddRange(rangedWeaponCopies.Values);
            _missionMeleeWeapons.AddRange(meleeWeaponCopies.Values);
            // because of the circular reference, the clone function won't work,
            // so I made a custom BattleSoldier constructor that does basically the same thing
            Soldiers = original.Soldiers
                .Select(s => new BattleSoldier(s, this, rangedWeaponCopies, meleeWeaponCopies))
                .ToList();
        }

        private static AmmunitionReservePool CopyReservePool(
            AmmunitionReservePool source,
            IDictionary<AmmunitionReservePool, AmmunitionReservePool> copies)
        {
            if (source == null) return null;
            if (!copies.TryGetValue(source, out AmmunitionReservePool copy))
            {
                copy = source.DeepCopy();
                copies[source] = copy;
            }
            return copy;
        }

        public object Clone()
        {
            return new BattleSquad(this);
        }

        /// <summary>
        /// Commits the live weapon state from a battle snapshot back to the mission-owned squad.
        /// Battle history snapshots own deep copies, while the mission pool remains the physical
        /// source used by the next battle in the same mission.
        /// </summary>
        internal void CommitEquipmentStateFrom(BattleSquad snapshot)
        {
            if (snapshot == null) return;
            int count = Math.Min(_missionRangedWeapons.Count, snapshot._missionRangedWeapons.Count);
            for (int index = 0; index < count; index++)
            {
                _missionRangedWeapons[index].CopyLiveStateFrom(snapshot._missionRangedWeapons[index]);
            }
        }

        public Coordinate GetSquadBoxSize()
        {
            return Placers.SquadFormationGeometry.For(this).Bounds;
        }

        public BattleSoldier GetRandomSquadMember(IRNG random)
        {
            List<BattleSoldier> ableSoldiers = AbleSoldiers;
            return ableSoldiers[random.GetIntBelowMax(0, ableSoldiers.Count)];
        }

        /// <summary>Warms lazy squad views before concurrent soldier evaluation begins.</summary>
        internal void PrepareForParallelPlanning()
        {
            _ = AbleSoldiers;
            EnsureStatistics();
        }

        public float GetAverageArmor()
        {
            EnsureStatistics();
            return _averageArmor;
        }
    
        public float GetAverageSize()
        {
            EnsureStatistics();
            return _averageSize;
        }

        public float GetAverageRangedEvasion()
        {
            EnsureStatistics();
            return _averageRangedEvasion;
        }

        public float GetAverageConstitution()
        {
            EnsureStatistics();
            return _averageConstitution;
        }

        public float GetSquadMove()
        {
            EnsureStatistics();
            return _squadMove;
        }

        public void RemoveSoldier(BattleSoldier soldier)
        {
            if (Soldiers.Remove(soldier))
            {
                InvalidateAbleSoldiers();
            }
        }

        internal void InvalidateAbleSoldiers()
        {
            // BattleState clones share the underlying ISoldier injury data. A wound applied through
            // one wrapper can therefore change eligibility in another wrapper retained by a chained
            // mission. A global generation invalidates all wrappers lazily without scanning them.
            System.Threading.Interlocked.Increment(ref _globalAbleSoldiersVersion);
            _ableSoldiers = null;
            _ableSoldiersSourceCount = -1;
            _cachedGlobalAbleSoldiersVersion = -1;
            _ableSoldiersVersion++;
            _statisticsVersion = -1;
        }

        public bool ShouldContinueMission()
        {
            int ableSoldierCount = AbleSoldiers.Count;
            if (ableSoldierCount == 0)
            {
                return false;
            }
            Aggression aggression = (CampaignCharacter?.CurrentOrder ?? Squad?.CurrentOrders)
                ?.LevelOfAggression ?? Aggression.Normal;
            if (aggression == Aggression.Aggressive)
            {
                return true;
            }
            else
            {
                // Aggression measures the losses this particular mission element will tolerate.
                // Under-strength squads must not abort merely because their template has empty
                // positions, so compare against the combat-capable roster captured when this
                // BattleSquad was created rather than the template's theoretical maximum.
                // TODO: adjust based on whether the squad leader is still around?
                float ratio = (float)ableSoldierCount / _missionStartingAbleSoldierCount;
                switch (aggression)
                {
                    case Aggression.Avoid:
                        return ratio >= 0.9f;
                    case Aggression.Cautious:
                        return ratio >= 0.75f;
                    case Aggression.Normal:
                        return ratio >= 0.5f;
                    case Aggression.Attritional:
                        return ratio >= 0.25f;
                    default:
                        return false;
                }
            }
        }

        public override string ToString()
        {
            return Name;
        }

        /// <summary>
        /// The range at which this squad prefers to OPEN an engagement against
        /// <paramref name="opposingSquads"/>.
        ///
        /// <para>PHASE 7 (Design/Reference/BattleLogic.md). Kept as a named seam, but it
        /// is now the SAME derived band the squad will steer toward once the fight starts, so
        /// opening there costs zero turns of approach. Phase 6 had it ask
        /// <c>BattleModifiersUtil.CalculateOptimalDistance</c> -- the effectiveness curve's
        /// UN-OPPOSED "where am I still half as effective as at my best" -- which for bolter
        /// marines runs out to weapon reach and opened engagements 800 yards outside the range
        /// those same marines immediately walk to. See
        /// <c>BattleEngagementFrameBuilder.CalculatePreferredOpeningRange</c> for why pricing the
        /// approach collapses the two questions into one.</para>
        ///
        /// <para>Phase 6's scalar-target overloads (and <c>GetPreferredEngagementRange</c>, which
        /// had no other caller) are gone with it: the derivation needs the whole opposing force,
        /// not a representative draw, because the melee arrival term reads how much of that force
        /// closes and how fast.</para>
        /// </summary>
        public int GetPreferredOpeningRange(IReadOnlyCollection<BattleSquad> opposingSquads)
        {
            return (int)BattleEngagementFrameBuilder.CalculatePreferredOpeningRange(
                this, opposingSquads);
        }

        /// <summary>
        /// Redistributes the squad's whole loadout across whoever is still able to fight. Called at the
        /// start of every battle, not once per mission.
        /// </summary>
        /// <remarks>
        /// Allocation used to happen exactly once, in the constructor, and a BattleSquad lives for the
        /// whole mission (see _missionStartingAbleSoldierCount). That was invisible while a mission
        /// contained at most one battle; now that assaults fight repeatedly and raids can be intercepted
        /// on several days, it meant a squad whose heavy gunner died on day 1 carried on all week with
        /// the weapon lying in a ditch.
        ///
        /// Reallocation is FULL rather than orphans-only: the existing method already picks the best
        /// carrier for each weapon set by that weapon's related skill, so re-running it hands the heavy
        /// weapon to the best remaining shooter for free. The accepted cost is that kit can also move
        /// between soldiers who are both still alive - a brother carrying a bolter on day 1 may be handed
        /// the plasma gun on day 3 with no in-fiction reason the player can see. Squad.Loadout lives on
        /// the persistent Squad, so this is squad property being remapped onto bodies rather than
        /// personal kit changing hands, and a fallen carrier's weapon is therefore always recovered.
        ///
        /// No statistics invalidation is needed: EnsureStatistics derives only armor, size, evasion,
        /// constitution and move speed, none of which depend on weapons.
        /// </remarks>
        internal void ReallocateEquipment()
        {
            _rangedAllocationCursor.Clear();
            _meleeAllocationCursor.Clear();
            foreach (BattleSoldier soldier in Soldiers)
            {
                soldier.ClearWeapons();
            }
            AllocateEquipment();
        }

        private void AllocateEquipment()
        {
            _rangedAllocationCursor.Clear();
            _meleeAllocationCursor.Clear();
            List<BattleSoldier> tempSquad = new List<BattleSoldier>(AbleSoldiers);
            // A squad with no able soldiers (every member wiped or fully incapacitated by prior
            // combat) has nothing to equip. Callers should avoid deploying such a squad — see
            // TurnController.ProcessCombatMissions, which skips depleted squads — but guard here so
            // construction never throws on an empty AbleSoldiers (was: tempSquad[0] below).
            if (tempSquad.Count == 0) return;
            // Characters carry kit assigned to the individual, so equip them before the pooled
            // passes below and take them out of contention. The skill-greedy allocation that
            // follows cannot express "this set belongs to this man" — it would happily hand the
            // Captain's relic blade to an Apothecary with a better Sword score. A sergeant is a
            // character like any other now — his element carries a Command Weapon quota — so
            // there is no separate IsSquadLeader pass here any more.
            AllocateCharacterEquipment(tempSquad);
            if (tempSquad.Count == 0) return;
            if (Squad == null)
            {
                foreach (BattleSoldier soldier in tempSquad)
                {
                    soldier.Armor = new Armor(ResolveElementArmor(soldier.Soldier));
                    MarkEquipmentValueSource(soldier);
                }
                return;
            }
            // order the weapon sets by the strength of the primary weapon
            List<WeaponSet> wsList = LoadoutDoctrineService.GetEffectiveLoadout(Squad)
                .OrderByDescending(ws => ws.PrimaryRangedWeapon?.DamageMultiplier ?? ws.PrimaryMeleeWeapon.StrengthMultiplier)
                .ToList();
            // need to allocate weapons from squad weapon sets
            foreach (WeaponSet ws in wsList)
            {
                if(tempSquad.Count() == 0)
                {
                    break;
                }
                // TODO: we'll want to stop assuming Dex as the base stat at some point
                if (ws.PrimaryRangedWeapon != null)
                {
                    BattleSoldier bestShooter = tempSquad.OrderByDescending(s => s.Soldier.GetTotalSkillValue(ws.PrimaryRangedWeapon.RelatedSkill)).First();
                    (List<RangedWeapon> ranged, List<MeleeWeapon> melee) = GetMissionWeapons(ws);
                    bestShooter.AddWeapons(ranged, melee);
                    bestShooter.Armor = new Armor(ResolveElementArmor(bestShooter.Soldier));
                    MarkEquipmentValueSource(bestShooter);
                    tempSquad.Remove(bestShooter);
                }
                else
                {
                    BattleSoldier bestHitter = tempSquad.OrderByDescending(s => s.Soldier.GetTotalSkillValue(ws.PrimaryMeleeWeapon.RelatedSkill)).First();
                    (List<RangedWeapon> ranged, List<MeleeWeapon> melee) = GetMissionWeapons(ws);
                    bestHitter.AddWeapons(ranged, melee);
                    bestHitter.Armor = new Armor(ResolveElementArmor(bestHitter.Soldier));
                    MarkEquipmentValueSource(bestHitter);
                    tempSquad.Remove(bestHitter);
                }
            }
            if(tempSquad.Count() > 0)
            {
                foreach(BattleSoldier soldier in tempSquad)
                {
                    WeaponSet defaultWeapons = ResolveElementDefaultWeapons(soldier.Soldier);
                    (List<RangedWeapon> ranged, List<MeleeWeapon> melee) = GetMissionWeapons(defaultWeapons);
                    soldier.AddWeapons(ranged, melee);
                    // TODO: personalize armor and weapons
                    soldier.Armor = new Armor(ResolveElementArmor(soldier.Soldier));
                    MarkEquipmentValueSource(soldier);
                }
            }
        }

        private static void MarkEquipmentValueSource(BattleSoldier soldier)
        {
            // A live campaign has a sector and therefore uses the itemized rules catalog for
            // tactical attribution even when a pooled carrier is still on the compatibility menu.
            // Isolated battle fixtures often load rules without a sector; keep those legacy
            // WeaponSet battles intrinsic so their zero-value withdrawal premises remain valid.
            soldier.UsesItemizedEquipment = GameDataSingleton.Instance?.IsInitialized == true
                && GameDataSingleton.Instance.GameRulesData?.EquipmentCatalog != null;
        }

        // The soldier's own slot default, falling back to the squad template's default
        // (SquadTemplateElement.DefaultWeaponSetId falls back the same way at load time — see
        // SquadTemplateDataAccess — so for every element without its own authored default this
        // already resolves to the same WeaponSet the squad default would have given).
        private WeaponSet ResolveElementDefaultWeapons(ISoldier soldier)
        {
            if (Squad == null)
            {
                return CharacterLoadoutService.GetEffectiveWeaponSet(soldier)
                    ?? CampaignCharacter?.AssignedSquad?.SquadTemplate?.DefaultWeapons;
            }
            SquadTemplateElement element = Squad.SquadTemplate.Elements
                .FirstOrDefault(e => e.SoldierTemplate == soldier.Template);
            return element?.DefaultWeapons ?? Squad.SquadTemplate.DefaultWeapons;
        }

        /// <summary>
        /// Equips every character in the list from its own resolved loadout and removes it from
        /// the list, leaving only soldiers the pooled allocation should handle. A character whose
        /// role resolves to no weapon set is left in place so he still gets the squad default
        /// rather than deploying unarmed.
        /// </summary>
        private void AllocateCharacterEquipment(List<BattleSoldier> tempSquad)
        {
            for (int i = tempSquad.Count - 1; i >= 0; i--)
            {
                BattleSoldier battleSoldier = tempSquad[i];
                if (TryGetItemizedEquipment(
                        battleSoldier,
                        out List<RangedWeapon> itemizedRanged,
                        out List<MeleeWeapon> itemizedMelee,
                        out Armor itemizedArmor))
                {
                    battleSoldier.AddWeapons(itemizedRanged, itemizedMelee);
                    battleSoldier.Armor = itemizedArmor;
                    tempSquad.RemoveAt(i);
                    continue;
                }
                WeaponSet weaponSet = CharacterLoadoutService.GetEffectiveWeaponSet(battleSoldier.Soldier);
                if (weaponSet == null)
                {
                    continue;
                }
                (List<RangedWeapon> ranged, List<MeleeWeapon> melee) = GetMissionWeapons(weaponSet);
                battleSoldier.AddWeapons(ranged, melee);
                battleSoldier.Armor = new Armor(ResolveElementArmor(battleSoldier.Soldier));
                tempSquad.RemoveAt(i);
            }
        }

        private ArmorTemplate ResolveElementArmor(ISoldier soldier)
        {
            SquadTemplate squadTemplate = Squad?.SquadTemplate
                ?? CampaignCharacter?.AssignedSquad?.SquadTemplate;
            SquadTemplateElement element = squadTemplate?.Elements
                .FirstOrDefault(candidate => candidate.SoldierTemplate == soldier?.Template);
            return element?.DefaultArmor ?? squadTemplate?.Armor;
        }

        private bool TryGetItemizedEquipment(
            BattleSoldier battleSoldier,
            out List<RangedWeapon> rangedWeapons,
            out List<MeleeWeapon> meleeWeapons,
            out Armor armor)
        {
            rangedWeapons = [];
            meleeWeapons = [];
            armor = null;
            if (Squad == null)
            {
                return false;
            }
            GameDataSingleton game = GameDataSingleton.Instance;
            EquipmentRulesCatalog catalog = game.IsInitialized
                ? game.GameRulesData?.EquipmentCatalog
                : null;
            SquadTemplateElement element = Squad.SquadTemplate?.Elements
                .FirstOrDefault(candidate => candidate.SoldierTemplate == battleSoldier.Soldier.Template);
            if (catalog == null || element?.PersonalEquipmentRole == null)
            {
                return false;
            }

            EquipmentLoadoutDoctrine doctrine = game.Sector?.PlayerForce?.Army?.EquipmentLoadoutDoctrine;
            EquipmentKitTemplate authoredRoleKit = catalog.EquipmentKits.GetValueOrDefault(
                element.PersonalEquipmentRole.DefaultKitId);
            EquipmentKitTemplate elementFallbackKit = element.DefaultWeapons == null
                ? null
                : catalog.EquipmentKits.GetValueOrDefault(
                    EquipmentRulesCatalog.GetKitId(element.DefaultWeapons.Id));
            EquipmentKitTemplate squadFallbackKit = Squad.SquadTemplate.DefaultWeapons == null
                ? null
                : catalog.EquipmentKits.GetValueOrDefault(
                    EquipmentRulesCatalog.GetKitId(Squad.SquadTemplate.DefaultWeapons.Id));
            EquipmentValidationContext validationContext = new()
            {
                FactionId = Squad.Faction?.Id,
                SpeciesId = battleSoldier.Soldier.Template.Species?.Id,
                SoldierTemplateId = battleSoldier.Soldier.Template.Id,
                PersonalEquipmentRole = element.PersonalEquipmentRole,
                Strength = battleSoldier.Soldier.Strength,
                BaseCapacity = battleSoldier.Soldier.Template.Species?.BaseCapacity ?? 16f,
                HandGroups = Math.Max(1, battleSoldier.FunctioningHands)
            };
            ResolvedEquipmentLoadout resolved = EquipmentLoadoutService.Resolve(
                battleSoldier.Soldier.Id,
                element,
                doctrine,
                authoredRoleKit,
                elementFallbackKit,
                squadFallbackKit,
                validationContext);
            if (resolved.Loadout == null || resolved.ValidationIssues.Count > 0)
            {
                return false;
            }
            battleSoldier.UsesItemizedEquipment = true;

            Dictionary<int, int> reserveByAmmunitionType = [];
            foreach (EquipmentLoadoutEntry entry in resolved.Loadout.Items)
            {
                if (entry.Equipment.AmmunitionProfile == null)
                {
                    continue;
                }
                int ammunitionId = entry.Equipment.AmmunitionProfile.AmmunitionType.Id;
                reserveByAmmunitionType[ammunitionId] =
                    reserveByAmmunitionType.GetValueOrDefault(ammunitionId)
                    + entry.Quantity * entry.Equipment.AmmunitionProfile.RoundsPerPackage;
            }
            AmmunitionReservePool reservePool = new(reserveByAmmunitionType);

            foreach (EquipmentLoadoutEntry entry in resolved.Loadout.Items)
            {
                EquipmentTemplate equipment = entry.Equipment;
                if (equipment.RangedProfile != null)
                {
                    if (equipment.RangedProfile.AmmunitionBehavior == AmmunitionBehavior.ConsumableItem)
                    {
                        RangedWeapon weapon = AcquireMissionRangedWeapon(
                            ToLegacyRangedTemplate(equipment),
                            reservePool,
                            entry.InitialReadyOrder);
                        weapon.ConsumableQuantity = entry.Quantity;
                        rangedWeapons.Add(weapon);
                    }
                    else
                    {
                        for (int index = 0; index < entry.Quantity; index++)
                        {
                            rangedWeapons.Add(AcquireMissionRangedWeapon(
                                ToLegacyRangedTemplate(equipment),
                                reservePool,
                                index == 0 ? entry.InitialReadyOrder : null));
                        }
                    }
                }
                else if (equipment.MeleeProfile != null)
                {
                    for (int index = 0; index < entry.Quantity; index++)
                    {
                        meleeWeapons.Add(AcquireMissionMeleeWeapon(
                            ToLegacyMeleeTemplate(equipment),
                            itemized: true,
                            index == 0 ? entry.InitialReadyOrder : null));
                    }
                }
            }

            if (resolved.Loadout.Armor?.ArmorProfile != null)
            {
                ArmorProfile profile = resolved.Loadout.Armor.ArmorProfile;
                armor = new Armor(new ArmorTemplate(
                    resolved.Loadout.Armor.Id,
                    resolved.Loadout.Armor.Name,
                    profile.ArmorProvided,
                    profile.StealthModifier,
                    profile.CapacityModifier,
                    profile.PreventsRunning));
            }
            return true;
        }

        private static RangedWeaponTemplate ToLegacyRangedTemplate(EquipmentTemplate equipment)
        {
            RangedWeaponProfile profile = equipment.RangedProfile;
            return new RangedWeaponTemplate(
                equipment.Id,
                equipment.Name,
                profile.Location,
                profile.RelatedSkill,
                profile.Accuracy,
                profile.ArmorMultiplier,
                profile.WoundMultiplier,
                profile.RequiredStrength,
                profile.DamageMultiplier,
                profile.MaximumRange,
                profile.RateOfFire,
                profile.LoadedCapacity,
                profile.Recoil,
                profile.Bulk,
                profile.DoesDamageDegradeWithRange,
                profile.ReloadDuration,
                profile.TemplateType,
                profile.AreaRadius,
                profile.AmmunitionType,
                profile.AmmunitionBehavior,
                profile.ConsumptionRule,
                profile.ReloadAmount,
                profile.RecoveryDuration,
                profile.RecoveryAmount);
        }

        private static MeleeWeaponTemplate ToLegacyMeleeTemplate(EquipmentTemplate equipment)
        {
            MeleeWeaponProfile profile = equipment.MeleeProfile;
            return new MeleeWeaponTemplate(
                equipment.Id,
                equipment.Name,
                profile.Location,
                profile.RelatedSkill,
                profile.Accuracy,
                profile.ArmorMultiplier,
                profile.WoundMultiplier,
                profile.RequiredStrength,
                profile.StrengthMultiplier,
                profile.ParryModifier,
                profile.AttackSpeedMultiplier);
        }

        private (List<RangedWeapon> Ranged, List<MeleeWeapon> Melee) GetMissionWeapons(WeaponSet weaponSet)
        {
            List<RangedWeapon> ranged = [];
            List<MeleeWeapon> melee = [];
            if (weaponSet == null)
            {
                return (ranged, melee);
            }

            foreach (RangedWeapon requested in weaponSet.GetRangedWeapons() ?? Array.Empty<RangedWeapon>())
            {
                ranged.Add(AcquireMissionRangedWeapon(requested.Template));
            }
            foreach (MeleeWeapon requested in weaponSet.GetMeleeWeapons() ?? Array.Empty<MeleeWeapon>())
            {
                melee.Add(AcquireMissionMeleeWeapon(requested.Template));
            }
            return (ranged, melee);
        }

        private RangedWeapon AcquireMissionRangedWeapon(
            RangedWeaponTemplate template,
            AmmunitionReservePool reservePool = null,
            int? initialReadyOrder = null)
        {
            bool itemized = reservePool != null;
            (int TemplateId, bool Itemized) key = (template.Id, itemized);
            int cursor = _rangedAllocationCursor.GetValueOrDefault(key);
            List<RangedWeapon> matching = _missionRangedWeapons
                .Where(weapon => weapon.Template.Id == template.Id
                    && (weapon.ReservePool != null) == itemized)
                .ToList();
            RangedWeapon weapon;
            if (cursor < matching.Count)
            {
                weapon = matching[cursor];
            }
            else
            {
                weapon = new RangedWeapon(template, reservePool, initialReadyOrder);
                if (!itemized && template.AmmunitionType != null)
                {
                    // The legacy WeaponSet bridge has no explicit package rows. Its authored
                    // kit carries one standard package for each magazine, which is enough for
                    // one tactical reload while the itemized allocator is adopted by squads.
                    weapon.ReserveAmmo = template.AmmoCapacity;
                }
                _missionRangedWeapons.Add(weapon);
            }
            _rangedAllocationCursor[key] = cursor + 1;
            return weapon;
        }

        private MeleeWeapon AcquireMissionMeleeWeapon(
            MeleeWeaponTemplate template,
            bool itemized = false,
            int? initialReadyOrder = null)
        {
            (int TemplateId, bool Itemized) key = (template.Id, itemized);
            int cursor = _meleeAllocationCursor.GetValueOrDefault(key);
            List<MeleeWeapon> matching = _missionMeleeWeapons
                .Where(weapon => weapon.Template.Id == template.Id
                    && weapon.IsItemized == itemized)
                .ToList();
            MeleeWeapon weapon;
            if (cursor < matching.Count)
            {
                weapon = matching[cursor];
            }
            else
            {
                weapon = new MeleeWeapon(template, initialReadyOrder, itemized);
                _missionMeleeWeapons.Add(weapon);
            }
            _meleeAllocationCursor[key] = cursor + 1;
            return weapon;
        }

        private void EnsureStatistics()
        {
            List<BattleSoldier> ableSoldiers = AbleSoldiers;
            if (_statisticsVersion == _ableSoldiersVersion)
            {
                return;
            }

            int armorTotal = 0;
            int armoredSoldierCount = 0;
            float sizeTotal = 0;
            float rangedEvasionTotal = 0;
            float constitutionTotal = 0;
            float ableSoldierCount = 0;
            float squadMove = float.MaxValue;

            foreach (BattleSoldier soldier in ableSoldiers)
            {
                if (soldier.Armor != null)
                {
                    armorTotal += soldier.Armor.Template.ArmorProvided;
                    armoredSoldierCount++;
                }

                sizeTotal += soldier.Soldier.Size;
                rangedEvasionTotal += soldier.Soldier.Template.Species.RangedEvasion;
                constitutionTotal += soldier.Soldier.Constitution;
                ableSoldierCount += 1.0f;

                float currentMaxSpeed = soldier.GetMoveSpeed();
                if (currentMaxSpeed < squadMove)
                {
                    squadMove = currentMaxSpeed;
                }
            }

            _averageArmor = armoredSoldierCount == 0 ? 0 : (float)armorTotal / armoredSoldierCount;
            _averageSize = sizeTotal / ableSoldierCount;
            _averageRangedEvasion = rangedEvasionTotal / ableSoldierCount;
            _averageConstitution = constitutionTotal / ableSoldierCount;
            _squadMove = squadMove;
            _statisticsVersion = _ableSoldiersVersion;
        }
    }
}
