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
        public IReadOnlyCollection<ValueTuple<BaseSkill, float>> MosTraining { get; }
        public TrainingProfile WorkExperienceTrainingProfile { get; }
        // The soldier's point value — its weight in force generation and in casualty/survivor
        // accounting against the strategic pools. A squad's battle value is the sum of its members'
        // (PRD §4.24). Optional/defaulted for templates that predate populated point values.
        public int BattleValue { get; }

        public SoldierTemplate(int id, Species species, string name, byte rank, byte subrank,
                               bool isSquadLeader, byte specialistType,
                               IReadOnlyCollection<ValueTuple<BaseSkill, float>> mosTraining,
                               TrainingProfile workExperienceTrainingProfile = null,
                               int battleValue = 0)
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
            BattleValue = battleValue;
        }
    }
}
