﻿using System;
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
            if (Soldiers.Count >= 30)
            {
                numberOfRows = 3;
            }
            else if (Soldiers.Count > 7)
            {
                numberOfRows = 2;
            }
            // membersPerRow is how many soldiers are in each row (back row may be smaller)
            ushort membersPerRow = (ushort)Math.Ceiling((float)(Soldiers.Count) / (float)(numberOfRows));
            return new Tuple<ushort, ushort>((ushort)(membersPerRow * Soldiers[0].Soldier.Template.Species.Width), 
                                             (ushort)(numberOfRows * Soldiers[0].Soldier.Template.Species.Depth));
        }

        public BattleSoldier GetRandomSquadMember()
        {
            return Soldiers[RNG.GetIntBelowMax(0, Soldiers.Count)];
        }

        public List<ChosenRangedWeapon> GetWeaponsForRange(float range)
        {
            List<ChosenRangedWeapon> list = [];
            foreach(BattleSoldier soldier in Soldiers)
            {
                ChosenRangedWeapon bestWeapon = null;
                float bestStrength = 0;
                foreach(RangedWeapon weapon in soldier.RangedWeapons)
                {
                    if (soldier.Soldier.FunctioningHands == 0) continue;
                    ChosenRangedWeapon newWeapon = new ChosenRangedWeapon(weapon, soldier.Soldier);
                    float newStrength = newWeapon.GetStrengthAtRange(range);
                    if( bestWeapon == null || bestStrength < newStrength 
                        || (bestStrength == newStrength && newWeapon.ActiveWeapon.Template.Accuracy > bestWeapon.ActiveWeapon.Template.Accuracy))
                    {
                        bestWeapon = newWeapon;
                        bestStrength = newStrength;
                    }
                }
                if(bestWeapon != null)
                {
                    list.Add(bestWeapon);
                }
            }
            return list;
        }
    
        public float GetAverageArmor()
        {
            int runningTotal = 0;
            int squadSize = 0;
            foreach(BattleSoldier soldier in Soldiers)
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
            foreach(BattleSoldier soldier in Soldiers)
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
            foreach (BattleSoldier soldier in Soldiers)
            {
                runningTotal += soldier.Soldier.Constitution;
                squadSize += 1.0f;
            }
            return runningTotal / squadSize;
        }

        public float GetSquadMove()
        {
            float runningTotal = float.MaxValue;
            foreach (BattleSoldier soldier in Soldiers)
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

        private void AllocateEquipment()
        {
            List<BattleSoldier> tempSquad = new List<BattleSoldier>(Soldiers);
            List<WeaponSet> wsList = Squad.Loadout.ToList();
            // need to allocate weapons from squad weapon sets
            if (Soldiers[0].Soldier.Template.IsSquadLeader)
            {
                // for now, sgt always gets default weapons
                Soldiers[0].AddWeapons(Squad.SquadTemplate.DefaultWeapons.GetRangedWeapons(), Squad.SquadTemplate.DefaultWeapons.GetMeleeWeapons());
                // TODO: personalize armor and weapons
                Soldiers[0].Armor = new Armor(Squad.SquadTemplate.Armor);
                tempSquad.RemoveAt(0);
            }
            foreach (WeaponSet ws in wsList)
            {
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
