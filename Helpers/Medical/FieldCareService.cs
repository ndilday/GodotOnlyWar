using OnlyWar.Helpers;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Medical
{
    /// <summary>One brother's wound stepped down one band by an Apothecary.</summary>
    public sealed record FieldCareTreatment(
        int SoldierId,
        string SoldierName,
        string LocationName,
        WoundLevel FromBand,
        int WoundsMoved,
        float Cost,
        int Day = 0);

    /// <summary>
    /// What field care did for one order (or one garrison location) over the days it ran. Carried
    /// into the mission report so the player can see the Apothecary he sent forward doing something
    /// -- see §7 trap 3 of Design/Reference/SpecialistAttachment.md: the attached specialist's
    /// field-care work must remain visible whether or not he is duty-ready for the engagement.
    /// </summary>
    public sealed class FieldCareReport
    {
        public List<string> ApothecaryNames { get; } = [];
        public List<int> ApothecaryIds { get; } = [];
        public List<FieldCareTreatment> Treatments { get; } = [];
        public float CapacitySpent { get; private set; }
        public HashSet<int> TreatedSoldierIds { get; } = [];

        public int TreatmentCount => Treatments.Count;
        public int TreatedSoldierCount => TreatedSoldierIds.Count;
        public bool HasApothecary => ApothecaryIds.Count > 0;

        internal void RecordTreatment(FieldCareTreatment treatment)
        {
            Treatments.Add(treatment);
            TreatedSoldierIds.Add(treatment.SoldierId);
            CapacitySpent += treatment.Cost;
        }
    }

    /// <summary>
    /// Apothecary field care (Design/Reference/CasualtyRealism.md §2.6, Phase 2b).
    ///
    /// An Apothecary converts his Medical rating into a DAILY wound capacity and spends it on the
    /// wounded within his reach, as forced wound-band demotions that take effect the moment they
    /// happen. A brother hit in a day-2 assault and treated that evening enters the day-3 battle at
    /// reduced severity -- which is the entire reason this is a daily pass rather than a credit
    /// banked until turn processing.
    ///
    /// REACH IS THE ORDER. Every wounded brother in the order's assigned squads, plus its attached
    /// soldiers. That falls out of Phase 2a's order-level attachment for free, and it is the payoff
    /// SpecialistAttachment.md §3.1 predicted.
    ///
    /// WHERE IT RUNS. Once per distinct <see cref="Order"/> per campaign day, from
    /// <c>MissionDayScheduler</c>'s scheduler-level <c>onDayEnd</c> hook. NEVER off a
    /// <c>MissionStepDriver</c>: one order fans out into several independent single-squad drivers
    /// under <c>MissionForceMode.IndependentSquads</c>, and a driver-hung pass would treat the same
    /// order's wounded once per element -- an Apothecary silently worth 3x on a Recon order. The
    /// dedup is the caller's (MissionTurnProcessor collects distinct orders once), and Phase 1b's
    /// daily-healing pass established the same precedent.
    ///
    /// WHAT IT CANNOT DO. Unfreeze a replacement-eligible location -- surgery remains surgery --
    /// touch a severed location, or repair an augmetic location. It is a bonus on top of natural
    /// healing, never a prerequisite for it: <c>MedicalTurnProcessor.ApplyWeeklyHealing</c> stays
    /// unconditional for everyone else.
    ///
    /// A CONSEQUENCE WORTH STATING, because it is not obvious from §2.6 alone: since
    /// <c>HitLocation.IsReplacementEligible</c> is true for severed non-vital locations only.
    /// Field care can therefore continue working on crippled locations, while a brother who has
    /// actually lost a non-vital part remains a surgical case. An Apothecary in the field cannot
    /// shortcut that surgery. What he CAN do is return the walking wounded to the line, including
    /// men carrying several severe wounds who would otherwise be out for months.
    ///
    /// BATTLE BOUNDARY. An individually duty-ready Apothecary may be materialized as a one-person
    /// battle element by BattleSquadFactory. Field-care capacity remains a separate between-days
    /// medical effect and is not itself a tactical aura.
    /// </summary>
    public static class FieldCareService
    {
        /// <summary>
        /// One order's daily field care. Call once per distinct order per day; calling it twice for
        /// the same day double-treats.
        /// </summary>
        public static void ApplyDailyFieldCare(
            Order order,
            FieldCareReport report,
            IReadOnlyList<BaseSkill> medicalSkills = null,
            int day = 0,
            RatingConsumerBindings ratingBindings = null)
        {
            if (order == null || report == null) return;

            List<PlayerSoldier> underOrder = EnumerateUnderOrder(order).ToList();
            List<PlayerSoldier> apothecaries = underOrder.Where(IsAvailableApothecary).ToList();
            if (apothecaries.Count == 0) return;

            foreach (PlayerSoldier apothecary in apothecaries)
            {
                if (report.ApothecaryIds.Contains(apothecary.Id)) continue;
                report.ApothecaryIds.Add(apothecary.Id);
                report.ApothecaryNames.Add(apothecary.Name);
            }

            RunOneDay(apothecaries, underOrder, report, medicalSkills, day, order.Id,
                ratingBindings ?? RatingConsumerBindings.CreateDefault());
        }

        /// <summary>
        /// Garrison care (§2.6). An Apothecary NOT on a mission treats co-located brothers who are
        /// likewise not on a mission -- the Apothecarium at rest, which is where most convalescence
        /// actually happens. Same capacity, same triage; reach is co-location rather than a shared
        /// order.
        ///
        /// GARRISON VERSUS FIELD PRIORITY (§3.3, decided): FIELD WINS, and it wins by construction
        /// rather than by a rule. An Apothecary under an order is excluded here by the very
        /// "not on a mission" test that defines the garrison pool, so the two pools are disjoint and
        /// no man can spend the same day twice. The cost of sending him forward is therefore visible
        /// exactly where §2.6 wanted it: the backlog at home stops moving.
        ///
        /// Resolves during turn processing rather than on a day loop, since with nobody fighting
        /// there is no reason to iterate days -- but it runs the identical daily routine
        /// <see cref="FieldCareConstants.GarrisonDaysPerTurn"/> times, re-triaging between each, so
        /// the two halves cannot drift apart.
        /// </summary>
        public static IReadOnlyList<FieldCareReport> ApplyGarrisonFieldCare(
            IEnumerable<PlayerSoldier> chapterMembers,
            IReadOnlyList<BaseSkill> medicalSkills = null,
            RatingConsumerBindings ratingBindings = null)
        {
            List<FieldCareReport> reports = [];
            if (chapterMembers == null) return reports;

            List<PlayerSoldier> garrison = chapterMembers
                .Where(soldier => soldier != null && !IsOnMission(soldier))
                .ToList();

            foreach (IGrouping<string, PlayerSoldier> location in garrison
                .GroupBy(GetLocationKey)
                .Where(group => group.Key != null)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                List<PlayerSoldier> present = location.ToList();
                List<PlayerSoldier> apothecaries = present.Where(IsAvailableApothecary).ToList();
                if (apothecaries.Count == 0) continue;

                FieldCareReport report = new();
                foreach (PlayerSoldier apothecary in apothecaries)
                {
                    report.ApothecaryIds.Add(apothecary.Id);
                    report.ApothecaryNames.Add(apothecary.Name);
                }

                for (int day = 1; day <= FieldCareConstants.GarrisonDaysPerTurn; day++)
                {
                    RunOneDay(apothecaries, present, report, medicalSkills, day, null,
                        ratingBindings ?? RatingConsumerBindings.CreateDefault());
                }
                reports.Add(report);
            }

            return reports;
        }

        /// <summary>
        /// Everyone an order can reach: its squads' members plus the individuals attached to it
        /// (Phase 2a). Deduplicated, because an attached specialist is still on his home squad's
        /// roll and that squad could in principle also be assigned.
        /// </summary>
        public static IEnumerable<PlayerSoldier> EnumerateUnderOrder(Order order)
        {
            if (order == null) return [];
            return order.Force.AllPlayerSoldiers;
        }

        /// <summary>
        /// The base skills that compose the Medical rating, resolved from the data-driven rating
        /// definitions rather than by name. Deliberately not a NamedSkillRegistry entry: the
        /// registry throws on a missing skill at load, and field-care XP is not worth making the
        /// rules database fail to open over.
        /// </summary>
        public static IReadOnlyList<BaseSkill> ResolveMedicalSkills(
            IEnumerable<RatingDefinition> ratingDefinitions,
            IReadOnlyDictionary<int, BaseSkill> baseSkillMap,
            RatingConsumerBindings ratingBindings = null)
        {
            if (ratingDefinitions == null || baseSkillMap == null) return [];
            ratingBindings ??= RatingConsumerBindings.CreateDefault();
            RatingDefinition medical = ratingDefinitions
                .FirstOrDefault(definition => definition.Key == ratingBindings[
                    RatingConsumerRole.MedicalCapacity]);
            if (medical == null) return [];
            return medical.Components
                .Where(component => component.ComponentType == RatingComponentType.SkillTotal)
                .OrderBy(component => component.Ordinal)
                .Select(component =>
                    baseSkillMap.TryGetValue(component.TargetId, out BaseSkill skill) ? skill : null)
                .Where(skill => skill != null)
                .ToList();
        }

        /// <summary>
        /// Is this brother committed to an operation? True when he is attached to an order as an
        /// individual OR his squad is under orders. Settles CasualtyRealism §3.3's garrison/field
        /// partition with one expression, as SpecialistAttachment.md §8 predicted it would.
        /// </summary>
        public static bool IsOnMission(PlayerSoldier soldier) =>
            soldier?.CurrentOrder != null
            || (soldier?.IndividualPosting == null && soldier?.AssignedSquad?.CurrentOrders != null);

        /// <summary>
        /// Where this brother is, for co-location purposes. Routed through
        /// <see cref="PlayerSoldier.EffectiveRegion"/> rather than <c>AssignedSquad.CurrentRegion</c>
        /// -- SpecialistAttachment.md §8 trap 2: an attached Apothecary's home squad may sit aboard
        /// ship while he is forward, and reading the squad would believe he is in two places.
        /// Null when he has no determinable location, which drops him out of every pool.
        /// </summary>
        public static string GetLocationKey(PlayerSoldier soldier)
        {
            if (soldier == null) return null;
            // Aboard ship beats a region: a boarded squad's CurrentRegion may still be set from
            // wherever it embarked, which is the same precedence MedicalProcedureService.SameLocation
            // applies.
            Models.CampaignLocation location = CampaignLocationService.ForSoldier(soldier);
            if (location?.Ship != null) return $"ship:{location.Ship.Id}";
            return location?.Region == null ? null : $"region:{location.Region.Id}";
        }

        /// <summary>
        /// Fit to practise medicine: an Apothecary by template, and neither down nor immobilised.
        /// Identified by template name through <see cref="MedicalProcedureService"/>, the existing
        /// single source of truth for who counts as one.
        /// </summary>
        public static bool IsAvailableApothecary(PlayerSoldier soldier) =>
            soldier != null
            && MedicalProcedureService.IsApothecary(soldier)
            && soldier.IsCombatEffective;

        /// <summary>
        /// The Apothecaries who would treat this brother if he needed it today, under whichever of
        /// the two passes he falls into. Drives the Apothecarium screen's field-care readout, so the
        /// player can see BEFORE committing that sending the Apothecary forward leaves the men at
        /// home uncovered -- the tension §2.6 wanted made legible.
        ///
        /// Derived from exactly the same predicates the passes use, so the screen cannot promise
        /// care the engine will not deliver.
        /// </summary>
        public static IReadOnlyList<PlayerSoldier> GetCoveringApothecaries(
            PlayerSoldier soldier, IEnumerable<PlayerSoldier> chapterMembers)
        {
            if (soldier == null) return [];

            Order order = soldier.CurrentOrder ?? soldier.AssignedSquad?.CurrentOrders;
            if (order != null)
            {
                return EnumerateUnderOrder(order).Where(IsAvailableApothecary).ToList();
            }

            if (chapterMembers == null) return [];
            string key = GetLocationKey(soldier);
            if (key == null) return [];
            return chapterMembers
                .Where(candidate => candidate != null
                    && !IsOnMission(candidate)
                    && IsAvailableApothecary(candidate)
                    && GetLocationKey(candidate) == key)
                .ToList();
        }

        public static float GetCapacity(
            PlayerSoldier apothecary,
            RatingConsumerBindings ratingBindings = null) =>
            FieldCareConstants.GetDailyCapacity(GetMedicalRating(apothecary, ratingBindings));

        public static float GetMedicalRating(
            PlayerSoldier apothecary,
            RatingConsumerBindings ratingBindings = null) =>
            (ratingBindings ?? RatingConsumerBindings.CreateDefault()).Get(
                apothecary?.SoldierEvaluationHistory?.LastOrDefault(),
                RatingConsumerRole.MedicalCapacity);

        // ---- The daily pass -------------------------------------------------------------------

        /// <summary>
        /// One day's care for one pool. Greedy worst-first to exhaustion, with NO per-soldier cap
        /// (§3.2, decided): one brother may absorb the whole day if he is the worst case, which is
        /// the point -- Astartes healing already handles light wounds without help, so spreading
        /// capacity thin returns nobody to the line. Daily re-triage is what keeps that fair: once
        /// the worst case's RecoveryTimeLeft drops below the next man's, the queue reorders on its
        /// own with no explicit cap needed.
        ///
        /// USE-IT-OR-LOSE-IT (§3.2, decided): whatever is left at the end of the day is gone. It is
        /// the simpler and more defensible reading of a man's working day, and it removes a piece of
        /// per-Apothecary state that would otherwise need persisting.
        /// </summary>
        private static void RunOneDay(
            IReadOnlyList<PlayerSoldier> apothecaries,
            IReadOnlyList<PlayerSoldier> pool,
            FieldCareReport report,
            IReadOnlyList<BaseSkill> medicalSkills,
            int day,
            int? orderId,
            RatingConsumerBindings ratingBindings)
        {
            float capacity = apothecaries.Sum(apothecary =>
                GetCapacity(apothecary, ratingBindings));
            if (capacity <= 0f) return;

            float spent = 0f;
            IReadOnlyDictionary<int, int> tieBreak = BuildTieBreak(pool);

            // Re-triage after every treatment, not once per day: a demotion changes the man's
            // RecoveryTimeLeft and can hand the queue to somebody else mid-day, which is the same
            // self-levelling behaviour the daily re-triage gives across days.
            while (true)
            {
                bool treated = false;
                foreach (PlayerSoldier patient in Triage(pool, tieBreak))
                {
                    HitLocation location = FindWorstTreatableLocation(patient);
                    if (location == null) continue;

                    (WoundLevel band, int count) = location.Wounds.FindTreatableBand();
                    float cost = FieldCareConstants.GetDemotionCost(band, count);
                    if (cost > capacity - spent)
                    {
                        // Not affordable today. Fall through to the next man rather than stopping:
                        // worst-first is a priority, not a promise to leave capacity idle.
                        continue;
                    }

                    location.Wounds.ApplyTreatmentDemotion();
                    spent += cost;
                    report.RecordTreatment(new FieldCareTreatment(
                        patient.Id, patient.Name, location.Template?.Name ?? "wound",
                        band, count, cost, day));
                    GameLog.Debug(() =>
                        $"FIELD_CARE order={(orderId.HasValue ? orderId.Value.ToString() : "garrison")} "
                        + $"day={day} apothecaries=[{string.Join(",", apothecaries.Select(a => $"{a.Name}#{a.Id}"))}] "
                        + $"patient={patient.Name}#{patient.Id} location={location.Template?.Name ?? "wound"} "
                        + $"from={band} woundsMoved={count} cost={cost:F2}");
                    treated = true;
                    break;
                }
                if (!treated) break;
            }

            GrantMedicalExperience(apothecaries, medicalSkills, spent, ratingBindings);
        }

        /// <summary>
        /// TRIAGE (§2.6, decided): most severe first, then Rank desc, then Subrank desc, then a
        /// seeded random. Severity is <see cref="Wounds.RecoveryTimeLeft"/> -- the PLAYER-VISIBLE
        /// number, deliberately, so the order the player sees on the Apothecarium screen is the
        /// order the game actually runs.
        ///
        /// Measured over TREATABLE locations only. A brother whose worst wound is a crippled leg
        /// awaiting a replacement procedure would otherwise sit permanently at the head of a queue
        /// he can never be helped out of, blocking everyone behind him.
        /// </summary>
        private static IEnumerable<PlayerSoldier> Triage(
            IReadOnlyList<PlayerSoldier> pool,
            IReadOnlyDictionary<int, int> tieBreak)
        {
            return pool
                .Where(soldier => soldier != null && GetTreatableSeverity(soldier) > 0)
                .OrderByDescending(GetTreatableSeverity)
                .ThenByDescending(soldier => soldier.Template?.Rank ?? 0)
                .ThenByDescending(soldier => soldier.Template?.Subrank ?? 0)
                .ThenBy(soldier => tieBreak.TryGetValue(soldier.Id, out int key) ? key : 0)
                .ThenBy(soldier => soldier.Id);
        }

        /// <summary>
        /// A random ordinal per soldier, drawn from the shared session RNG like every other piece of
        /// gameplay randomness.
        ///
        /// An earlier version used a private <see cref="Random"/> seeded from the order and day, to
        /// avoid perturbing the shared stream and moving seeded battle baselines. That was the wrong
        /// instinct and the user reverted it: seeded reproducibility matters for sector and chapter
        /// GENERATION, which all runs before any battle, so a medical pass cannot disturb what
        /// anyone actually relies on. Keys are still assigned in soldier-id order so the draw does
        /// not depend on how the caller happened to enumerate the pool.
        /// </summary>
        private static IReadOnlyDictionary<int, int> BuildTieBreak(IReadOnlyList<PlayerSoldier> pool)
        {
            Dictionary<int, int> keys = [];
            foreach (PlayerSoldier soldier in pool
                .Where(soldier => soldier != null)
                .OrderBy(soldier => soldier.Id))
            {
                if (keys.ContainsKey(soldier.Id)) continue;
                keys[soldier.Id] = RNG.GetIntBelowMax(0, int.MaxValue);
            }
            return keys;
        }

        private static byte GetTreatableSeverity(PlayerSoldier soldier)
        {
            byte worst = 0;
            if (soldier?.Body == null) return worst;
            foreach (HitLocation location in soldier.Body.HitLocations)
            {
                if (!IsTreatable(location)) continue;
                byte weeks = location.Wounds.RecoveryTimeLeft();
                if (weeks > worst) worst = weeks;
            }
            return worst;
        }

        private static HitLocation FindWorstTreatableLocation(PlayerSoldier soldier)
        {
            HitLocation worst = null;
            uint worstTotal = 0;
            if (soldier?.Body == null) return null;
            foreach (HitLocation location in soldier.Body.HitLocations)
            {
                if (!IsTreatable(location)) continue;
                if (location.Wounds.WoundTotal > worstTotal)
                {
                    worstTotal = location.Wounds.WoundTotal;
                    worst = location;
                }
            }
            return worst;
        }

        /// <summary>
        /// A location field care may work on. Mirrors the weekly natural-healing exclusions exactly
        /// -- a severed location is gone, an augmetic location needs specialist repair, and a
        /// replacement-eligible severed non-vital locations stay frozen until a cybernetic or
        /// vat-grown procedure treats them. Bands below Moderate are excluded by
        /// <see cref="Wounds.FindTreatableBand"/> itself.
        /// </summary>
        private static bool IsTreatable(HitLocation location)
        {
            if (location == null
                || location.IsCybernetic
                || location.IsSevered
                || location.IsCoveredBySeveredParent
                || location.IsReplacementEligible)
            {
                return false;
            }
            return location.Wounds.FindTreatableBand().Count > 0;
        }

        private static void GrantMedicalExperience(
            IReadOnlyList<PlayerSoldier> apothecaries,
            IReadOnlyList<BaseSkill> medicalSkills,
            float capacitySpent,
            RatingConsumerBindings ratingBindings)
        {
            if (capacitySpent <= 0f || medicalSkills == null || medicalSkills.Count == 0) return;

            float totalCapacity = apothecaries.Sum(apothecary =>
                GetCapacity(apothecary, ratingBindings));
            if (totalCapacity <= 0f) return;

            foreach (PlayerSoldier apothecary in apothecaries)
            {
                // Split by contribution, so the Master of the Apothecarion working alongside a
                // junior brother takes the larger share of the practice as well as the larger share
                // of the load.
                float share = GetCapacity(apothecary, ratingBindings) / totalCapacity;
                float points = capacitySpent * share
                    * FieldCareConstants.MedicalExperiencePerCapacitySpent;
                if (points <= 0f) continue;
                foreach (BaseSkill skill in medicalSkills)
                {
                    apothecary.AddSkillPoints(skill, points);
                }
            }
        }
    }
}
