using OnlyWar.Models;
﻿using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Equippables;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSquad : ICloneable
    {
        private static int _globalAbleSoldiersVersion;
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
        // eruption-into-melee placement (see Design/EvasionBurrowAndAmbush.md).
        public bool CanBurrow
        {
            get
            {
                List<BattleSoldier> able = AbleSoldiers;
                return able.Count > 0
                    && able.All(s => s.Soldier.Template.Species.Abilities.HasFlag(SpeciesAbilities.Burrow));
            }
        }

        public BattleSquad(bool isPlayerSquad, Squad squad)
        {
            Id = squad.Id;
            Name = squad.Name;
            Squad = squad;
            Soldiers = squad.Members.Select(s => new BattleSoldier(s, this)).ToList();
            IsPlayerSquad = isPlayerSquad;
            IsInMelee = false;
            MovementTier = SquadMovementTier.Stationary;
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
            List<BattleSoldier> ableSoldiers = AbleSoldiers;
            int numberOfRows = 1;
            if (ableSoldiers.Count >= 30)
            {
                numberOfRows = 3;
            }
            else if (ableSoldiers.Count > 7)
            {
                numberOfRows = 2;
            }
            // membersPerRow is how many soldiers are in each row (back row may be smaller)
            ushort membersPerRow = (ushort)Math.Ceiling((float)ableSoldiers.Count / numberOfRows);
            ushort maxWidth = ableSoldiers.Max(s => s.Soldier.Template.Species.Width);
            ushort maxDepth = ableSoldiers.Max(s => s.Soldier.Template.Species.Depth);
            return new Coordinate((ushort)(membersPerRow * maxWidth),
                                             (ushort)(numberOfRows * maxDepth));
        }

        public BattleSoldier GetRandomSquadMember(IRNG random)
        {
            List<BattleSoldier> ableSoldiers = AbleSoldiers;
            return ableSoldiers[random.GetIntBelowMax(0, ableSoldiers.Count)];
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
                // see how large the squad currently is compared to its maximum size
                // TODO: adjust based on whether the squad leader is still around?
                float ratio = (float)ableSoldierCount / Squad.SquadTemplate.Elements.Sum(e => e.MaximumNumber);
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

        public int GetPreferredEngagementRange(float targetSize, float targetArmor, float targetCon, float targetRangedEvasion = 0)
        {
            return (int)AbleSoldiers.Average(s => BattleModifiersUtil.CalculateOptimalDistance(s, targetSize, targetArmor, targetCon, targetRangedEvasion));
        }

        private void AllocateEquipment()
        {
            List<BattleSoldier> tempSquad = new List<BattleSoldier>(AbleSoldiers);
            // A squad with no able soldiers (every member wiped or fully incapacitated by prior
            // combat) has nothing to equip. Callers should avoid deploying such a squad — see
            // TurnController.ProcessCombatMissions, which skips depleted squads — but guard here so
            // construction never throws on an empty AbleSoldiers (was: tempSquad[0] below).
            if (tempSquad.Count == 0) return;
            // order the weapon sets by the strength of the primary weapon
            List<WeaponSet> wsList = Squad.Loadout.OrderByDescending(ws => ws.PrimaryRangedWeapon?.DamageMultiplier ?? ws.PrimaryMeleeWeapon.StrengthMultiplier).ToList();
            // need to allocate weapons from squad weapon sets
            if (tempSquad[0].Soldier.Template.IsSquadLeader)
            {
                // for now, sgt always gets default weapons
                tempSquad[0].AddWeapons(Squad.SquadTemplate.DefaultWeapons.GetRangedWeapons(), Squad.SquadTemplate.DefaultWeapons.GetMeleeWeapons());
                // TODO: personalize armor and weapons
                tempSquad[0].Armor = new Armor(Squad.SquadTemplate.Armor);
                tempSquad.RemoveAt(0);
            }
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
                    soldier.AddWeapons(Squad.SquadTemplate.DefaultWeapons.GetRangedWeapons(), Squad.SquadTemplate.DefaultWeapons.GetMeleeWeapons());
                    // TODO: personalize armor and weapons
                    soldier.Armor = new Armor(Squad.SquadTemplate.Armor);
                }
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
