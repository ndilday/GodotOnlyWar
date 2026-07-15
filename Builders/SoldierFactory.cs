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

        public Soldier GenerateNewSoldier(SoldierTemplate template, IRNG random)
        {
            return GenerateNewSoldier(template, random, null);
        }

        public Soldier GenerateNewSoldier(
            SoldierTemplate template,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            Soldier soldier = GenerateNewSoldier(template.Species, null, random, entityIds);

            foreach (Tuple<BaseSkill, float> skillBoost in template.MosTraining)
            {
                soldier.AddSkillPoints(skillBoost.Item1, skillBoost.Item2);
            }

            return soldier;
        }

        public Soldier GenerateNewSoldier(
            Species species,
            IReadOnlyList<SkillTemplate> newRecruitSkills,
            IRNG random)
        {
            return GenerateNewSoldier(species, newRecruitSkills, random, null);
        }

        public Soldier GenerateNewSoldier(
            Species species,
            IReadOnlyList<SkillTemplate> newRecruitSkills,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            Soldier soldier = new Soldier(species.BodyTemplate)
            {
                Id = entityIds?.GetNextId() ?? _nextId
            };
            if (entityIds == null)
            {
                _nextId++;
            }

            soldier.Strength = species.Strength.BaseValue
                + (float)(random.NextRandomZValue() * species.Strength.StandardDeviation);
            soldier.Dexterity = species.Dexterity.BaseValue
                + (float)(random.NextRandomZValue() * species.Dexterity.StandardDeviation);
            soldier.Constitution = species.Constitution.BaseValue
                + (float)(random.NextRandomZValue() * species.Constitution.StandardDeviation);
            soldier.Ego = species.Ego.BaseValue
                + (float)(random.NextRandomZValue() * species.Ego.StandardDeviation);
            soldier.Charisma = species.Charisma.BaseValue
                + (float)(random.NextRandomZValue() * species.Charisma.StandardDeviation);
            soldier.Perception = species.Perception.BaseValue
                + (float)(random.NextRandomZValue() * species.Perception.StandardDeviation);
            soldier.Intelligence = species.Intelligence.BaseValue
                + (float)(random.NextRandomZValue() * species.Intelligence.StandardDeviation);

            soldier.AttackSpeed = species.AttackSpeed.BaseValue
                + (float)(random.NextRandomZValue() * species.AttackSpeed.StandardDeviation);
            soldier.MoveSpeed = species.MoveSpeed.BaseValue
                + (float)(random.NextRandomZValue() * species.MoveSpeed.StandardDeviation);
            soldier.Size = species.Size.BaseValue
                + (float)(random.NextRandomZValue() * species.Size.StandardDeviation);
            soldier.PsychicPower = species.PsychicPower.BaseValue
                + (float)(random.NextRandomZValue() * species.PsychicPower.StandardDeviation);

            if (newRecruitSkills != null)
            {
                foreach (SkillTemplate skillTemplate in newRecruitSkills)
                {
                    float roll = skillTemplate.BaseValue
                        + (float)(random.NextRandomZValue() * skillTemplate.StandardDeviation);
                    if (roll > 0)
                    {
                        soldier.AddSkillPoints(skillTemplate.BaseSkill, roll);
                    }
                }

            }

            return soldier;
        }

        public Soldier[] GenerateNewSoldiers(int count, SoldierTemplate template, IRNG random)
        {
            return GenerateNewSoldiers(count, template, random, null);
        }

        public Soldier[] GenerateNewSoldiers(
            int count,
            SoldierTemplate template,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            Soldier[] soldierArray = new Soldier[count];
            for(int i = 0; i < count; i++)
            {
                soldierArray[i] = GenerateNewSoldier(template, random, entityIds);
            }
            return soldierArray;
        }

        public Soldier[] GenerateNewSoldiers(
            int count,
            Species species,
            IReadOnlyList<SkillTemplate> newRecruitSkills,
            IRNG random)
        {
            return GenerateNewSoldiers(count, species, newRecruitSkills, random, null);
        }

        public Soldier[] GenerateNewSoldiers(
            int count,
            Species species,
            IReadOnlyList<SkillTemplate> newRecruitSkills,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            Soldier[] soldierArray = new Soldier[count];
            for (int i = 0; i < count; i++)
            {
                soldierArray[i] = GenerateNewSoldier(species, newRecruitSkills, random, entityIds);
            }
            return soldierArray;
        }
    }
}
