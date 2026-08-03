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
        public IReadOnlyList<SoldierTemplateRequirement> PromotionRequirements { get; }
        // The soldier's point value — its weight in force generation and in casualty/survivor
        // accounting against the strategic pools. A squad's battle value is the sum of its members'
        // (PRD §4.24). Optional/defaulted for templates that predate populated point values.
        public int BattleValue { get; }
        /// <summary>
        /// Doctrinal share of canonical offensive output supplied by melee. Runtime battle roles
        /// additionally weight this by the able roster, usable grips, ammunition and readiness.
        /// </summary>
        public float MeleeFraction { get; }
        public bool HasAuthoredMeleeFraction { get; }

        public SoldierTemplate(int id, Species species, string name, byte rank, byte subrank,
                               bool isSquadLeader, byte specialistType,
                               IReadOnlyCollection<ValueTuple<BaseSkill, float>> mosTraining,
                               TrainingProfile workExperienceTrainingProfile = null,
                               int battleValue = 0,
                               IReadOnlyList<SoldierTemplateRequirement> promotionRequirements = null,
                               float? meleeFraction = null)
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
            HasAuthoredMeleeFraction = meleeFraction.HasValue;
            MeleeFraction = Math.Clamp(meleeFraction ?? 0, 0, 1);
            PromotionRequirements = promotionRequirements ?? [];
        }
    }
}
