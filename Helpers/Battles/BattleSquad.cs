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

        public int Id { get; private set; }
        public string Name { get; private set; }
        public List<BattleSoldier> Soldiers { get; private set; }
        public float CoverModifier { get; private set; }
        public bool IsPlayerSquad { get; private set; }
        // Presentation-side affiliation for battle reports. The Chapter and the Imperial PDF
        // fight on the same side, but IsPlayerSquad must remain Chapter-only because battle rules
        // use it to distinguish player-controlled missions from NPC missions.
        public bool IsPlayerAligned => IsPlayerSquad || Squad?.Faction?.IsDefaultFaction == true;
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
                    _ableSoldiers = Soldiers.Where(s => s.CanFight).ToList();
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

        // A squad "provides synapse" iff any of its soldier templates' species carries the
        // ability (OnlyWar_TDD.md §6.6). This reads the full roster, not
        // AbleSoldiers — it describes squad composition, not current combat capability.
        // Squads are species-homogeneous (§3.1), so in practice this is all-or-nothing, but
        // it is written to tolerate a future mixed template without change.
        public bool SquadProvidesSynapse
        {
            get
            {
                return Soldiers.Any(s => s.Soldier.Template.Species.Abilities.HasFlag(SpeciesAbilities.Synapse));
            }
        }

        // An HQ squad projects the §4.3 command aura (OnlyWar_TDD.md §6.6; Phase
        // 6). Unlike synapse, SquadTypes.HQ IS the right set for command (§3.2) — a Tyranid
        // Warrior squad provides synapse but no command aura, while every faction's HQ
        // (Captain, Warboss, Hive Tyrant) provides command. Radius and strength are
        // morale-owned code constants (MoraleConstants), never DB data.
        public bool SquadProvidesCommandAura =>
            Squad?.SquadTemplate?.SquadType.HasFlag(SquadTypes.HQ) == true;

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
            Soldiers = squad.Members.Select(s => new BattleSoldier(s, this)).ToList();
            _missionStartingAbleSoldierCount = AbleSoldiers.Count;
            IsPlayerSquad = isPlayerSquad;
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

        private BattleSquad(BattleSquad original)
        {
            Id = original.Id;
            Name = original.Name;
            // we shouldn't need to clone the squad
            Squad = original.Squad;
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
            // because of the circular reference, the clone function won't work,
            // so I made a custom BattleSoldier constructor that does basically the same thing
            Soldiers = original.Soldiers.Select(s => new BattleSoldier(s, this)).ToList();
        }

        public object Clone()
        {
            return new BattleSquad(this);
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
            if (Squad.CurrentOrders.LevelOfAggression == Aggression.Aggressive)
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
                switch (Squad.CurrentOrders.LevelOfAggression)
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
            return Squad.Name;
        }

        /// <summary>
        /// The range at which this squad prefers to OPEN an engagement against
        /// <paramref name="opposingSquads"/>.
        ///
        /// <para>PHASE 7 (Design/Active/EngagementScoringOverhaul.md). Kept as a named seam, but it
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
            foreach (BattleSoldier soldier in Soldiers)
            {
                soldier.ClearWeapons();
            }
            AllocateEquipment();
        }

        private void AllocateEquipment()
        {
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
                    bestShooter.AddWeapons(ws.GetRangedWeapons(), ws.GetMeleeWeapons());
                    bestShooter.Armor = new Armor(Squad.SquadTemplate.Armor);
                    tempSquad.Remove(bestShooter);
                }
                else
                {
                    BattleSoldier bestHitter = tempSquad.OrderByDescending(s => s.Soldier.GetTotalSkillValue(ws.PrimaryMeleeWeapon.RelatedSkill)).First();
                    bestHitter.AddWeapons(ws.GetRangedWeapons(), ws.GetMeleeWeapons());
                    bestHitter.Armor = new Armor(Squad.SquadTemplate.Armor);
                    tempSquad.Remove(bestHitter);
                }
            }
            if(tempSquad.Count() > 0)
            {
                foreach(BattleSoldier soldier in tempSquad)
                {
                    WeaponSet defaultWeapons = ResolveElementDefaultWeapons(soldier.Soldier);
                    soldier.AddWeapons(defaultWeapons.GetRangedWeapons(), defaultWeapons.GetMeleeWeapons());
                    // TODO: personalize armor and weapons
                    soldier.Armor = new Armor(Squad.SquadTemplate.Armor);
                }
            }
        }

        // The soldier's own slot default, falling back to the squad template's default
        // (SquadTemplateElement.DefaultWeaponSetId falls back the same way at load time — see
        // SquadTemplateDataAccess — so for every element without its own authored default this
        // already resolves to the same WeaponSet the squad default would have given).
        private WeaponSet ResolveElementDefaultWeapons(ISoldier soldier)
        {
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
                WeaponSet weaponSet = CharacterLoadoutService.GetEffectiveWeaponSet(battleSoldier.Soldier);
                if (weaponSet == null)
                {
                    continue;
                }
                battleSoldier.AddWeapons(weaponSet.GetRangedWeapons(), weaponSet.GetMeleeWeapons());
                battleSoldier.Armor = new Armor(Squad.SquadTemplate.Armor);
                tempSquad.RemoveAt(i);
            }
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
