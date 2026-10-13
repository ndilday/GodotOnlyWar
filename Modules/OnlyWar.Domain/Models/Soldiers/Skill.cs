using System;

namespace OnlyWar.Domain.Soldiers
{
    public enum Attribute
    {
        Strength = 1,
        Dexterity = 2,
        Constitution = 3,
        Intelligence = 4,
        Presence = 5,
        Ego = 6
    }

    public enum SkillCategory
    {
        Ranged = 1,
        Gunnery = 2,
        Melee = 3,
        Vehicle = 4,
        Military = 5,
        Professional = 6,
        Tech = 7,
        Apothecary = 8,
        Espionage = 9
    }

    public class BaseSkill
    {
        public int Id;
        public SkillCategory Category;
        public string Name;
        // Stable rules-data identity. Name is presentation text and may be localized or
        // rewritten by a mod without changing the gameplay meaning of the skill.
        public string SkillKey;
        public Attribute BaseAttribute;
        public float Difficulty;
        public BaseSkill(
            int id,
            SkillCategory category,
            string name,
            Attribute baseAttribute,
            float difficulty,
            string skillKey = null)
        {
            Id = id;
            Category = category;
            Name = name;
            SkillKey = skillKey;
            BaseAttribute = baseAttribute;
            Difficulty = difficulty;
        }
    }

    public class Skill
    {
        public BaseSkill BaseSkill { get; private set; }
        public float PointsInvested { get; private set; }

        /// <summary>The bonus of a skill the soldier has no record of.</summary>
        public const float UntrainedBonus = -4f;

        public float SkillBonus => CalculateBonus(PointsInvested, BaseSkill.Difficulty);

        /// <summary>
        /// <c>log2(points + p0) - difficulty</c>, with <c>p0 = 2^(difficulty - 4)</c>. At 0 points
        /// this is exactly <see cref="UntrainedBonus"/>, so every fraction of a point is better than
        /// untrained, and the curve has no break. With many points it is the plain
        /// <c>log2(points) - difficulty</c>.
        /// </summary>
        public static float CalculateBonus(float points, float difficulty)
        {
            double offset = System.Math.Pow(2, difficulty + UntrainedBonus);
            return (float)System.Math.Log(System.Math.Max(0f, points) + offset, 2) - difficulty;
        }
        public Skill(BaseSkill baseSkill, float points = 0)
        {
            BaseSkill = baseSkill;
            if (points < 0) PointsInvested = 0;
            else PointsInvested = points;
        }
        public void AddPoints(float points)
        {
            if(points > 0)
            {
                PointsInvested += points;
            }
        }
    }
}
