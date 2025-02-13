using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;

namespace OnlyWar.Builders
{
    class SoldierFactory
    {
        private SoldierFactory() { }
        private static SoldierFactory _instance;
        private static int _nextId = 0;
        public static SoldierFactory Instance
        {
            get
            {
                if(_instance == null)
                {
                    _instance = new SoldierFactory();
                }
                return _instance;
            }
        }

        public void SetCurrentHighestSoldierId(int highestId)
        {
            _nextId = highestId + 1;
        }

        public Soldier GenerateNewSoldier(SoldierTemplate template)
        {
            Soldier soldier = GenerateNewSoldier(template.Species, null);

            foreach (Tuple<BaseSkill, float> skillBoost in template.MosTraining)
            {
                soldier.AddSkillPoints(skillBoost.Item1, skillBoost.Item2);
            }

            return soldier;
        }

        public Soldier GenerateNewSoldier(Species species, IReadOnlyList<SkillTemplate> newRecruitSkills)
        {
            Soldier soldier = new Soldier(species.BodyTemplate)
            {
                Id = _nextId
            };
            _nextId++;

            soldier.Strength = species.Strength.BaseValue
                + (float)(RNG.NextRandomZValue() * species.Strength.StandardDeviation);
            soldier.Dexterity = species.Dexterity.BaseValue
                + (float)(RNG.NextRandomZValue() * species.Dexterity.StandardDeviation);
            soldier.Constitution = species.Constitution.BaseValue
                + (float)(RNG.NextRandomZValue() * species.Constitution.StandardDeviation);
            soldier.Ego = species.Ego.BaseValue
                + (float)(RNG.NextRandomZValue() * species.Ego.StandardDeviation);
            soldier.Charisma = species.Charisma.BaseValue
                + (float)(RNG.NextRandomZValue() * species.Charisma.StandardDeviation);
            soldier.Perception = species.Perception.BaseValue
                + (float)(RNG.NextRandomZValue() * species.Perception.StandardDeviation);
            soldier.Intelligence = species.Intelligence.BaseValue
                + (float)(RNG.NextRandomZValue() * species.Intelligence.StandardDeviation);

            soldier.AttackSpeed = species.AttackSpeed.BaseValue
                + (float)(RNG.NextRandomZValue() * species.AttackSpeed.StandardDeviation);
            soldier.MoveSpeed = species.MoveSpeed.BaseValue
                + (float)(RNG.NextRandomZValue() * species.MoveSpeed.StandardDeviation);
            soldier.Size = species.Size.BaseValue
                + (float)(RNG.NextRandomZValue() * species.Size.StandardDeviation);
            soldier.PsychicPower = species.PsychicPower.BaseValue
                + (float)(RNG.NextRandomZValue() * species.PsychicPower.StandardDeviation);

            if (newRecruitSkills != null)
            {
                foreach (SkillTemplate skillTemplate in newRecruitSkills)
                {
                    float roll = skillTemplate.BaseValue
                        + (float)(RNG.NextRandomZValue() * skillTemplate.StandardDeviation);
                    if (roll > 0)
                    {
                        soldier.AddSkillPoints(skillTemplate.BaseSkill, roll);
                    }
                }

            }

            return soldier;
        }

        public Soldier[] GenerateNewSoldiers(int count, SoldierTemplate template)
        {
            Soldier[] soldierArray = new Soldier[count];
            for(int i = 0; i < count; i++)
            {
                soldierArray[i] = GenerateNewSoldier(template);
            }
            return soldierArray;
        }

        public Soldier[] GenerateNewSoldiers(int count, Species species, IReadOnlyList<SkillTemplate> newRecruitSkills)
        {
            Soldier[] soldierArray = new Soldier[count];
            for (int i = 0; i < count; i++)
            {
                soldierArray[i] = GenerateNewSoldier(species, newRecruitSkills);
            }
            return soldierArray;
        }
    }
}
