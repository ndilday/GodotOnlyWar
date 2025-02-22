using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Equippables;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSquad : ICloneable
    {
        public int Id { get; private set; }
        public string Name { get; private set; }
        public List<BattleSoldier> Soldiers { get; private set; }
        public float CoverModifier { get; private set; }
        public bool IsPlayerSquad { get; private set; }
        public bool IsInMelee { get; set; }

        public Squad Squad { get; }

        public List<BattleSoldier> AbleSoldiers
        {
            get
            {
                return Soldiers.Where(s => s.CanFight).ToList();
            }
        }

        public BattleSoldier SquadLeader
        {
            get
            {
                return AbleSoldiers.FirstOrDefault(s => s.Soldier.Template.IsSquadLeader);
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
            // because of the circular reference, the clone function won't work,
            // so I made a custom BattleSoldier constructor that does basically the same thing
            Soldiers = original.Soldiers.Select(s => new BattleSoldier(s, this)).ToList();
        }

        public object Clone()
        {
            return new BattleSquad(this);
        }

        public Tuple<ushort, ushort> GetSquadBoxSize()
        {
            int numberOfRows = 1;
            if (AbleSoldiers.Count >= 30)
            {
                numberOfRows = 3;
            }
            else if (AbleSoldiers.Count > 7)
            {
                numberOfRows = 2;
            }
            // membersPerRow is how many soldiers are in each row (back row may be smaller)
            ushort membersPerRow = (ushort)Math.Ceiling((float)(AbleSoldiers.Count) / (float)(numberOfRows));
            return new Tuple<ushort, ushort>((ushort)(membersPerRow * AbleSoldiers[0].Soldier.Template.Species.Width), 
                                             (ushort)(numberOfRows * AbleSoldiers[0].Soldier.Template.Species.Depth));
        }

        public BattleSoldier GetRandomSquadMember()
        {
            return AbleSoldiers[RNG.GetIntBelowMax(0, AbleSoldiers.Count)];
        }

        public float GetAverageArmor()
        {
            int runningTotal = 0;
            int squadSize = 0;
            foreach(BattleSoldier soldier in AbleSoldiers)
            {
                if(soldier.Armor != null)
                {
                    runningTotal += soldier.Armor.Template.ArmorProvided;
                    squadSize++;
                }
            }
            if (squadSize == 0) return 0;
            return (float)runningTotal / (float)squadSize;
        }
    
        public float GetAverageSize()
        {
            float squadSize = 0;
            float runningTotal = 0;
            foreach(BattleSoldier soldier in AbleSoldiers)
            {
                runningTotal += soldier.Soldier.Size;
                squadSize += 1.0f;
            }
            return runningTotal / squadSize;
        }

        public float GetAverageConstitution()
        {
            float squadSize = 0;
            float runningTotal = 0;
            foreach (BattleSoldier soldier in AbleSoldiers)
            {
                runningTotal += soldier.Soldier.Constitution;
                squadSize += 1.0f;
            }
            return runningTotal / squadSize;
        }

        public float GetSquadMove()
        {
            float runningTotal = float.MaxValue;
            foreach (BattleSoldier soldier in AbleSoldiers)
            {
                float currentMaxSpeed = soldier.GetMoveSpeed();
                if (currentMaxSpeed < runningTotal)
                {
                    runningTotal = currentMaxSpeed;
                }
            }
            return runningTotal;
        }

        public void RemoveSoldier(BattleSoldier soldier)
        {
            Soldiers.Remove(soldier);
        }

        public override string ToString()
        {
            return Squad.Name;
        }

        public int GetPreferredEngagementRange(float targetSize, float targetArmor, float targetCon)
        {
            return (int)AbleSoldiers.Average(s => BattleModifiersUtil.CalculateOptimalDistance(s, targetSize, targetArmor, targetCon));
        }

        private void AllocateEquipment()
        {
            List<BattleSoldier> tempSquad = new List<BattleSoldier>(AbleSoldiers);
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
    }
}
