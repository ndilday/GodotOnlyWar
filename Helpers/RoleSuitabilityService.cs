using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Soldiers;

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
    /// See Design/FoundingRoleAssignment.md for the role criteria table.
    /// </summary>
    public sealed class RoleSuitabilityService
    {
        private readonly Dictionary<FoundingRole, List<PlayerSoldier>> _candidates;

        public RoleSuitabilityService(IEnumerable<PlayerSoldier> soldiers)
        {
            // Psykers belong to the Librarius and nothing else.
            List<PlayerSoldier> pool = soldiers.Where(s => s.PsychicPower <= 0).ToList();
            _candidates = new Dictionary<FoundingRole, List<PlayerSoldier>>();
            foreach (FoundingRole role in Enum.GetValues<FoundingRole>())
            {
                _candidates[role] = pool
                    .Where(s => IsEligible(role, Evaluation(s)))
                    .OrderByDescending(s => SortKey(role, Evaluation(s)))
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

        private static bool IsEligible(FoundingRole role, SoldierEvaluation e)
        {
            return role switch
            {
                FoundingRole.ChapterMaster => true,
                FoundingRole.MasterOfTheForge => e.TechRating > 100 && e.LeadershipRating > 60,
                FoundingRole.Techmarine => e.TechRating > 75,
                FoundingRole.MasterOfTheApothecarion => e.MedicalRating > 115 && e.LeadershipRating > 60,
                FoundingRole.Apothecary => e.MedicalRating > 95,
                FoundingRole.MasterOfSanctity => e.PietyRating > 100 && e.LeadershipRating > 60,
                FoundingRole.Chaplain => e.PietyRating > 90,
                FoundingRole.VeteranCaptain => e.LeadershipRating > 75
                    && e.MeleeRating > 105 && e.RangedRating > 110,
                FoundingRole.Captain => true,
                FoundingRole.VeteranSergeant => IsVeteranCandidate(e) && e.LeadershipRating > 60,
                // Rank-and-file veterans: sergeant-grade leaders are ranked in the
                // VeteranSergeant list instead, mirroring the old veterans.Except(leaders).
                FoundingRole.Veteran => IsVeteranCandidate(e) && e.LeadershipRating <= 60,
                FoundingRole.Champion => true,
                FoundingRole.Ancient => true,
                FoundingRole.TacticalSergeant => IsTacticalCandidate(e) && e.LeadershipRating > 50,
                FoundingRole.TacticalMarine => IsTacticalCandidate(e) && e.LeadershipRating < 50,
                FoundingRole.AssaultSergeant => IsAssaultCandidate(e) && e.LeadershipRating > 50,
                FoundingRole.AssaultMarine => IsAssaultCandidate(e) && e.LeadershipRating < 50,
                FoundingRole.DevastatorSergeant => IsDevastatorCandidate(e) && e.LeadershipRating > 50,
                FoundingRole.DevastatorMarine => IsDevastatorCandidate(e) && e.LeadershipRating < 50,
                FoundingRole.ScoutSergeant => true,
                _ => false
            };
        }

        private static float SortKey(FoundingRole role, SoldierEvaluation e)
        {
            return role switch
            {
                FoundingRole.ChapterMaster => e.LeadershipRating,
                FoundingRole.MasterOfTheForge => e.TechRating,
                FoundingRole.Techmarine => e.TechRating,
                FoundingRole.MasterOfTheApothecarion => e.MedicalRating,
                FoundingRole.Apothecary => e.MedicalRating,
                FoundingRole.MasterOfSanctity => e.PietyRating,
                FoundingRole.Chaplain => e.PietyRating,
                FoundingRole.VeteranCaptain => e.LeadershipRating,
                FoundingRole.Captain => e.LeadershipRating,
                FoundingRole.VeteranSergeant => e.LeadershipRating,
                FoundingRole.Veteran => e.MeleeRating,
                FoundingRole.Champion => e.MeleeRating,
                FoundingRole.Ancient => e.AncientRating,
                FoundingRole.TacticalSergeant => e.LeadershipRating,
                FoundingRole.TacticalMarine => e.RangedRating,
                FoundingRole.AssaultSergeant => e.LeadershipRating,
                FoundingRole.AssaultMarine => e.MeleeRating,
                FoundingRole.DevastatorSergeant => e.LeadershipRating,
                FoundingRole.DevastatorMarine => e.RangedRating,
                FoundingRole.ScoutSergeant => e.LeadershipRating,
                _ => 0f
            };
        }

        // Tactical baseline plus an Adamantium-level spike in either combat rating.
        private static bool IsVeteranCandidate(SoldierEvaluation e)
        {
            bool tacticalBaseline = e.MeleeRating > 90 && e.RangedRating > 105;
            bool adamantiumCombatSpike = e.MeleeRating > 115 || e.RangedRating > 120;
            return tacticalBaseline && adamantiumCombatSpike;
        }

        private static bool IsTacticalCandidate(SoldierEvaluation e)
        {
            return e.MeleeRating > 90 && e.RangedRating > 105;
        }

        private static bool IsAssaultCandidate(SoldierEvaluation e)
        {
            return e.MeleeRating > 90 && e.RangedRating > 95 && e.RangedRating < 105;
        }

        private static bool IsDevastatorCandidate(SoldierEvaluation e)
        {
            return e.MeleeRating > 80 && e.MeleeRating < 90 && e.RangedRating > 95;
        }
    }
}
