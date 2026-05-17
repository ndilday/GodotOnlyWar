using System;
using System.Collections.Generic;

namespace OnlyWar.Models.Soldiers
{
    public class SoldierTemplate
    {
        public int Id { get; }
        public string Name { get; }
        public bool IsSquadLeader { get; }
        public byte SpecialistType { get; }
        public byte Rank { get; }
        public byte Subrank { get; }
        public Species Species { get; }
        public IReadOnlyCollection<Tuple<BaseSkill, float>> MosTraining { get; } 
        public TrainingProfile WorkExperienceTrainingProfile { get; }

        public SoldierTemplate(int id, Species species, string name, byte rank, byte subrank,
                               bool isSquadLeader, byte specialistType, 
                               IReadOnlyCollection<Tuple<BaseSkill, float>> mosTraining,
                               TrainingProfile workExperienceTrainingProfile = null)
        {
            Id = id;
            Species = species;
            Name = name;
            IsSquadLeader = isSquadLeader;
            SpecialistType = specialistType;
            Rank = rank;
            Subrank = subrank;
            MosTraining = mosTraining;
            WorkExperienceTrainingProfile = workExperienceTrainingProfile;
        }
    }
}
