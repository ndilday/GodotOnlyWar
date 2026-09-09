using OnlyWar.Domain;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Medical.Readiness;
using MedicalDutyReadinessPolicy = OnlyWar.Medical.Readiness.DutyReadinessPolicy;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Medical.Readiness
{
    /// <summary>
    /// The authoritative individual readiness policy. It has no Godot dependency and can be used
    /// at UI selection, order mutation, loadout allocation, and mission/battle boundaries.
    /// </summary>
    public static class DutyReadinessService
    {
        public static DutyReadinessEvaluation Evaluate(
            PlayerSoldier soldier,
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram recruitmentProgram = null)
        {
            if (soldier == null)
            {
                return DutyReadinessPolicy.Evaluate(new DutyReadinessFacts(
                    "Soldier", false, false, false, 0, WoundLevel.None));
            }
            DutyReadinessFacts facts = new(
                soldier.Name,
                soldier.IsCombatEffective,
                soldier.HasUntreatedSeveredLimb,
                soldier.IsUndergoingMedicalProcedure
                    || ReadinessReservations.IsReserved(recruitmentProgram, soldier.Id),
                soldier.FunctioningHands,
                MedicalDutyReadinessPolicy.GetWorstWoundLevel(soldier.Body));
            DutyReadinessPolicyOptions options = new(
                doctrine?.InjuryThreshold,
                doctrine?.RequireDutyReadySquadLeader ?? false,
                doctrine?.MinimumDutyReadySquadStrength ?? 0);
            return MedicalDutyReadinessPolicy.Evaluate(facts, options);
        }

        public static DutyReadinessEvaluation Evaluate(
            ISoldier soldier,
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram recruitmentProgram = null)
        {
            if (soldier is PlayerSoldier player)
            {
                return Evaluate(player, doctrine, recruitmentProgram);
            }

            bool combatEffective = soldier?.IsCombatEffective == true;
            return MedicalDutyReadinessPolicy.Evaluate(new DutyReadinessFacts(
                soldier?.Name ?? "Soldier",
                combatEffective,
                false,
                false,
                combatEffective ? 2 : 0,
                MedicalDutyReadinessPolicy.GetWorstWoundLevel(soldier?.Body)));
        }

        public static bool IsDutyReady(
            PlayerSoldier soldier,
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram recruitmentProgram = null) =>
            Evaluate(soldier, doctrine, recruitmentProgram).IsDutyReady;

        public static IReadOnlyList<ISoldier> GetDutyReadyMembers(
            Squad squad,
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram recruitmentProgram = null)
        {
            if (squad == null) return Array.Empty<ISoldier>();
            return squad.Members
                .Where(member => member is not PlayerSoldier player
                    ? member?.IsCombatEffective == true
                    : Evaluate(player, doctrine, recruitmentProgram).IsDutyReady)
                .ToList();
        }

        public static WoundLevel GetWorstWoundLevel(Body body) =>
            MedicalDutyReadinessPolicy.GetWorstWoundLevel(body);

        public static WoundLevel GetWorstWoundLevel(ISoldier soldier) =>
            GetWorstWoundLevel(soldier?.Body);

        // Short query aliases keep callers from reimplementing the body scan when their domain
        // language already says "worst wound". Both names intentionally share the same pure path.
        public static WoundLevel GetWorstWound(Body body) => GetWorstWoundLevel(body);

        public static WoundLevel GetWorstWound(ISoldier soldier) => GetWorstWoundLevel(soldier);

        public static WoundLevel GetWorstWoundLevel(Wounds wounds) =>
            MedicalDutyReadinessPolicy.GetWorstWoundLevel(wounds);

        public static WoundLevel GetWorstWound(Wounds wounds) => GetWorstWoundLevel(wounds);

        public static int SeverityIndex(WoundLevel level) =>
            MedicalDutyReadinessPolicy.SeverityIndex(level);
    }
}
