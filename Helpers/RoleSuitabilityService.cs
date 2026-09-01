using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// The founding roles a soldier can be ranked for. Librarius roles are absent by
    /// design: psychic ability is a categorical gate, not a score, so the Librarius is
    /// staffed directly from the psyker pool (see NewChapterBuilder.AssignLibrarians)
    /// and psykers are excluded from every list this service produces.
    /// </summary>
    public enum FoundingRole
    {
        ChapterMaster,
        MasterOfTheForge,
        Techmarine,
        MasterOfTheApothecarion,
        Apothecary,
        MasterOfSanctity,
        Chaplain,
        VeteranCaptain,
        Captain,
        VeteranSergeant,
        Veteran,
        Champion,
        Ancient,
        TacticalSergeant,
        TacticalMarine,
        AssaultSergeant,
        AssaultMarine,
        DevastatorSergeant,
        DevastatorMarine,
        ScoutSergeant
    }

    /// <summary>
    /// Ranks evaluated soldiers for each founding role: per role, an eligibility filter
    /// and a best-first sort over the soldier's initial evaluation. Ineligibility is
    /// expressed by omission from the role's list — there are no sentinel scores.
    /// See Design/Reference/FoundingRoleAssignment.md for the role criteria table.
    /// </summary>
    public sealed class RoleSuitabilityService
    {
        private readonly Dictionary<FoundingRole, List<PlayerSoldier>> _candidates;
        private readonly RatingConsumerBindings _ratings;

        public RoleSuitabilityService(
            IEnumerable<PlayerSoldier> soldiers,
            RatingConsumerBindings ratings = null)
        {
            _ratings = ratings ?? RatingConsumerBindings.CreateDefault();
            // Psykers belong to the Librarius and nothing else.
            List<PlayerSoldier> pool = soldiers.Where(s => s.PsychicPower <= 0).ToList();
            _candidates = new Dictionary<FoundingRole, List<PlayerSoldier>>();
            foreach (FoundingRole role in Enum.GetValues<FoundingRole>())
            {
                _candidates[role] = pool
                    .Where(s => IsEligible(role, Evaluation(s), _ratings))
                    .OrderByDescending(s => SortKey(role, Evaluation(s), _ratings))
                    .ToList();
            }
        }

        /// <summary>
        /// A fresh, mutable best-first candidate list for the role. Callers own the
        /// copy and are responsible for skipping soldiers assigned elsewhere.
        /// </summary>
        public List<PlayerSoldier> CreateCandidateList(FoundingRole role)
        {
            return new List<PlayerSoldier>(_candidates[role]);
        }

        private static SoldierEvaluation Evaluation(PlayerSoldier soldier)
        {
            return soldier.SoldierEvaluationHistory[0];
        }

        private static bool IsEligible(
            FoundingRole role,
            SoldierEvaluation e,
            RatingConsumerBindings ratings)
        {
            return role switch
            {
                FoundingRole.ChapterMaster => true,
                FoundingRole.MasterOfTheForge => Rating(ratings, e, RatingConsumerRole.TechnicalCapability) > 100
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) > 60,
                FoundingRole.Techmarine => Rating(ratings, e, RatingConsumerRole.TechnicalCapability) > 75,
                FoundingRole.MasterOfTheApothecarion => Rating(ratings, e, RatingConsumerRole.MedicalCapacity) > 115
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) > 60,
                FoundingRole.Apothecary => Rating(ratings, e, RatingConsumerRole.MedicalCapacity) > 95,
                FoundingRole.MasterOfSanctity => Rating(ratings, e, RatingConsumerRole.SpiritualCapability) > 100
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) > 60,
                FoundingRole.Chaplain => Rating(ratings, e, RatingConsumerRole.SpiritualCapability) > 90,
                FoundingRole.VeteranCaptain => Rating(ratings, e, RatingConsumerRole.CommandLeadership) > 75
                    && Rating(ratings, e, RatingConsumerRole.MeleeCombat) > 105
                    && Rating(ratings, e, RatingConsumerRole.RangedCombat) > 110,
                FoundingRole.Captain => true,
                FoundingRole.VeteranSergeant => IsVeteranCandidate(e, ratings)
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) > 60,
                // Rank-and-file veterans: sergeant-grade leaders are ranked in the
                // VeteranSergeant list instead, mirroring the old veterans.Except(leaders).
                FoundingRole.Veteran => IsVeteranCandidate(e, ratings)
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) <= 60,
                FoundingRole.Champion => true,
                FoundingRole.Ancient => true,
                FoundingRole.TacticalSergeant => IsTacticalCandidate(e, ratings)
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) > 50,
                FoundingRole.TacticalMarine => IsTacticalCandidate(e, ratings)
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) < 50,
                FoundingRole.AssaultSergeant => IsAssaultCandidate(e, ratings)
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) > 50,
                FoundingRole.AssaultMarine => IsAssaultCandidate(e, ratings)
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) < 50,
                FoundingRole.DevastatorSergeant => IsDevastatorCandidate(e, ratings)
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) > 50,
                FoundingRole.DevastatorMarine => IsDevastatorCandidate(e, ratings)
                    && Rating(ratings, e, RatingConsumerRole.CommandLeadership) < 50,
                FoundingRole.ScoutSergeant => true,
                _ => false
            };
        }

        private static float SortKey(
            FoundingRole role,
            SoldierEvaluation e,
            RatingConsumerBindings ratings)
        {
            return role switch
            {
                FoundingRole.ChapterMaster => Rating(ratings, e, RatingConsumerRole.CommandLeadership),
                FoundingRole.MasterOfTheForge => Rating(ratings, e, RatingConsumerRole.TechnicalCapability),
                FoundingRole.Techmarine => Rating(ratings, e, RatingConsumerRole.TechnicalCapability),
                FoundingRole.MasterOfTheApothecarion => Rating(ratings, e, RatingConsumerRole.MedicalCapacity),
                FoundingRole.Apothecary => Rating(ratings, e, RatingConsumerRole.MedicalCapacity),
                FoundingRole.MasterOfSanctity => Rating(ratings, e, RatingConsumerRole.SpiritualCapability),
                FoundingRole.Chaplain => Rating(ratings, e, RatingConsumerRole.SpiritualCapability),
                FoundingRole.VeteranCaptain => Rating(ratings, e, RatingConsumerRole.CommandLeadership),
                FoundingRole.Captain => Rating(ratings, e, RatingConsumerRole.CommandLeadership),
                FoundingRole.VeteranSergeant => Rating(ratings, e, RatingConsumerRole.CommandLeadership),
                FoundingRole.Veteran => Rating(ratings, e, RatingConsumerRole.MeleeCombat),
                FoundingRole.Champion => Rating(ratings, e, RatingConsumerRole.MeleeCombat),
                FoundingRole.Ancient => Rating(ratings, e, RatingConsumerRole.AncientService),
                FoundingRole.TacticalSergeant => Rating(ratings, e, RatingConsumerRole.CommandLeadership),
                FoundingRole.TacticalMarine => Rating(ratings, e, RatingConsumerRole.RangedCombat),
                FoundingRole.AssaultSergeant => Rating(ratings, e, RatingConsumerRole.CommandLeadership),
                FoundingRole.AssaultMarine => Rating(ratings, e, RatingConsumerRole.MeleeCombat),
                FoundingRole.DevastatorSergeant => Rating(ratings, e, RatingConsumerRole.CommandLeadership),
                FoundingRole.DevastatorMarine => Rating(ratings, e, RatingConsumerRole.RangedCombat),
                FoundingRole.ScoutSergeant => Rating(ratings, e, RatingConsumerRole.CommandLeadership),
                _ => 0f
            };
        }

        // Tactical baseline plus an Adamantium-level spike in either combat rating.
        private static bool IsVeteranCandidate(SoldierEvaluation e, RatingConsumerBindings ratings)
        {
            float melee = Rating(ratings, e, RatingConsumerRole.MeleeCombat);
            float ranged = Rating(ratings, e, RatingConsumerRole.RangedCombat);
            bool tacticalBaseline = melee > 90 && ranged > 105;
            bool adamantiumCombatSpike = melee > 115 || ranged > 120;
            return tacticalBaseline && adamantiumCombatSpike;
        }

        private static bool IsTacticalCandidate(SoldierEvaluation e, RatingConsumerBindings ratings)
        {
            return Rating(ratings, e, RatingConsumerRole.MeleeCombat) > 90
                && Rating(ratings, e, RatingConsumerRole.RangedCombat) > 105;
        }

        private static bool IsAssaultCandidate(SoldierEvaluation e, RatingConsumerBindings ratings)
        {
            float ranged = Rating(ratings, e, RatingConsumerRole.RangedCombat);
            return Rating(ratings, e, RatingConsumerRole.MeleeCombat) > 90
                && ranged > 95 && ranged < 105;
        }

        private static bool IsDevastatorCandidate(SoldierEvaluation e, RatingConsumerBindings ratings)
        {
            float melee = Rating(ratings, e, RatingConsumerRole.MeleeCombat);
            return melee > 80 && melee < 90
                && Rating(ratings, e, RatingConsumerRole.RangedCombat) > 95;
        }

        private static float Rating(
            RatingConsumerBindings ratings,
            SoldierEvaluation evaluation,
            RatingConsumerRole role) => ratings.Get(evaluation, role);
    }
}
