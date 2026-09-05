using System.Collections.Generic;

namespace OnlyWar.Models.Soldiers
{
    public enum TrainingTargetType
    {
        Skill = 1,
        Attribute = 2
    }

    public class TrainingProfileEntry
    {
        public TrainingTargetType TargetType { get; }
        public BaseSkill Skill { get; }
        public Attribute? Attribute { get; }
        public float Weight { get; }

        public TrainingProfileEntry(BaseSkill skill, float weight)
        {
            TargetType = TrainingTargetType.Skill;
            Skill = skill;
            Weight = weight;
        }

        public TrainingProfileEntry(Attribute attribute, float weight)
        {
            TargetType = TrainingTargetType.Attribute;
            Attribute = attribute;
            Weight = weight;
        }
    }

    public class TrainingProfile
    {
        public int Id { get; }
        public string Name { get; }
        public IReadOnlyCollection<TrainingProfileEntry> Entries { get; }

        public TrainingProfile(int id, string name, IReadOnlyCollection<TrainingProfileEntry> entries)
        {
            Id = id;
            Name = name;
            Entries = entries;
        }
    }
}
