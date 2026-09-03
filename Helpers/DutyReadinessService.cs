using OnlyWar.Helpers.Recruitment;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public enum DutyReadinessReasonCode
    {
        Ready = 0,
        CombatIncapacitation,
        UntreatedSeverance,
        InsufficientFunctioningArms,
        ProcedureReservation,
        ChapterInjuryThreshold
    }

    /// <summary>
    /// Typed result for the individual Chapter duty decision. The reason is deliberately not a
    /// string-only flag: UI, orders, and mission assembly must be able to agree on the same cause.
    /// </summary>
    public sealed record DutyReadinessEvaluation(
        bool IsDutyReady,
        DutyReadinessReasonCode ReasonCode,
        string Reason,
        WoundLevel? WorstWoundLevel = null)
    {
        public bool IsReady => IsDutyReady;
        public bool IsAllowed => IsDutyReady;
        public static DutyReadinessEvaluation Ready { get; } =
            new(true, DutyReadinessReasonCode.Ready, null, null);
    }

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
                return Reject(DutyReadinessReasonCode.CombatIncapacitation,
                    "No soldier is available for duty.");
            }

            if (recruitmentProgram == null)
            {
                PlayerForce playerForce = GameDataSingleton.Instance?.Sector?.PlayerForce;
                // As with doctrine resolution, detached test/inspection models must not inherit
                // procedure reservations from an unrelated live singleton that happens to use the
                // same soldier ids.
                if (playerForce?.Faction != null
                    && ReferenceEquals(soldier.AssignedSquad?.Faction, playerForce.Faction))
                {
                    recruitmentProgram = playerForce.RecruitmentProgram;
                }
            }

            // Keep the unconditional exclusions ahead of the combat predicate. PlayerSoldier's
            // legacy IsCombatEffective projection also includes active medical reservations, but
            // the typed result must still explain that state as a procedure reservation.
            if (soldier.HasUntreatedSeveredLimb)
            {
                return Reject(DutyReadinessReasonCode.UntreatedSeverance,
                    $"{soldier.Name} has an untreated severed limb.");
            }
            if (soldier.IsUndergoingMedicalProcedure
                || RecruitmentPromotionService.IsReservedForProcedure(
                    recruitmentProgram, soldier.Id))
            {
                return Reject(DutyReadinessReasonCode.ProcedureReservation,
                    $"{soldier.Name} is reserved for a medical or recruitment procedure.");
            }
            if (!soldier.IsCombatEffective)
            {
                return Reject(DutyReadinessReasonCode.CombatIncapacitation,
                    $"{soldier.Name} is combat-incapacitated.");
            }
            if (soldier.FunctioningHands < 2)
            {
                return Reject(DutyReadinessReasonCode.InsufficientFunctioningArms,
                    $"{soldier.Name} has fewer than two functioning arm/hand groups.");
            }

            WoundLevel worst = GetWorstWoundLevel(soldier.Body);
            if (doctrine?.InjuryThreshold is WoundLevel threshold
                && worst != WoundLevel.None
                && IsAtOrAbove(worst, threshold))
            {
                return Reject(
                    DutyReadinessReasonCode.ChapterInjuryThreshold,
                    $"{soldier.Name} is withheld by the Chapter's {threshold} injury threshold.",
                    worst);
            }

            return worst == WoundLevel.None
                ? DutyReadinessEvaluation.Ready
                : new DutyReadinessEvaluation(true, DutyReadinessReasonCode.Ready, null, worst);
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

            return soldier?.IsCombatEffective == true
                ? DutyReadinessEvaluation.Ready
                : Reject(DutyReadinessReasonCode.CombatIncapacitation,
                    $"{soldier?.Name ?? "Soldier"} is combat-incapacitated.");
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

        public static WoundLevel GetWorstWoundLevel(Body body)
        {
            if (body == null) return WoundLevel.None;
            return body.HitLocations
                .Select(location => GetWorstWoundLevel(location?.Wounds))
                .OrderByDescending(SeverityIndex)
                .FirstOrDefault();
        }

        public static WoundLevel GetWorstWoundLevel(ISoldier soldier) =>
            GetWorstWoundLevel(soldier?.Body);

        // Short query aliases keep callers from reimplementing the body scan when their domain
        // language already says "worst wound". Both names intentionally share the same pure path.
        public static WoundLevel GetWorstWound(Body body) => GetWorstWoundLevel(body);

        public static WoundLevel GetWorstWound(ISoldier soldier) => GetWorstWoundLevel(soldier);

        public static WoundLevel GetWorstWoundLevel(Wounds wounds)
        {
            if (wounds == null) return WoundLevel.None;
            if (wounds.UnsurvivableWounds > 0) return WoundLevel.Unsurvivable;
            if (wounds.MortalWounds > 0) return WoundLevel.Mortal;
            if (wounds.MassiveWounds > 0) return WoundLevel.Massive;
            if (wounds.CriticalWounds > 0) return WoundLevel.Critical;
            if (wounds.MajorWounds > 0) return WoundLevel.Major;
            if (wounds.ModerateWounds > 0) return WoundLevel.Moderate;
            if (wounds.MinorWounds > 0) return WoundLevel.Minor;
            if (wounds.NegligibleWounds > 0) return WoundLevel.Negligible;
            return WoundLevel.None;
        }

        public static WoundLevel GetWorstWound(Wounds wounds) => GetWorstWoundLevel(wounds);

        public static int SeverityIndex(WoundLevel level) => level switch
        {
            WoundLevel.Negligible => 1,
            WoundLevel.Minor => 2,
            WoundLevel.Moderate => 3,
            WoundLevel.Major => 4,
            WoundLevel.Critical => 5,
            WoundLevel.Massive => 6,
            WoundLevel.Mortal => 7,
            WoundLevel.Unsurvivable => 8,
            _ => 0
        };

        private static bool IsAtOrAbove(WoundLevel actual, WoundLevel threshold) =>
            SeverityIndex(actual) >= SeverityIndex(threshold);

        private static DutyReadinessEvaluation Reject(
            DutyReadinessReasonCode code,
            string reason,
            WoundLevel? worst = null) =>
            new(false, code, reason, worst);
    }

    // Name used by a few domain-facing callers; keep it as a thin façade so there is still one
    // implementation and one result vocabulary.
    public static class ChapterDutyReadinessService
    {
        public static DutyReadinessEvaluation Evaluate(
            PlayerSoldier soldier,
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram recruitmentProgram = null) =>
            DutyReadinessService.Evaluate(soldier, doctrine, recruitmentProgram);

        public static bool IsDutyReady(
            PlayerSoldier soldier,
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram recruitmentProgram = null) =>
            DutyReadinessService.IsDutyReady(soldier, doctrine, recruitmentProgram);

        public static WoundLevel GetWorstWoundLevel(Body body) =>
            DutyReadinessService.GetWorstWoundLevel(body);

        public static WoundLevel GetWorstWound(Body body) =>
            DutyReadinessService.GetWorstWound(body);
    }
}
