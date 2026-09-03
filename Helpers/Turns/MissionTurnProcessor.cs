using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Medical;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Assault;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Executes the mission portion of a weekly turn. The caller owns the result collections and
    /// the cross-cutting turn services, which keeps this processor independent of TurnController's
    /// mutable state while preserving the existing resolution order.
    /// </summary>
    internal sealed class MissionTurnProcessor
    {
        private const float EngineeringBuildDivisor = 100f;

        private readonly GameSession _session;
        private readonly MissionRules _missionRules;
        private readonly BattleExecutionContext _battleExecution;
        private readonly Action<PlanetFaction, Region, float> _recordIntelGain;
        private readonly Action<IntelObservation> _recordTargetObservation;
        private readonly Action<RegionFaction, long, Faction> _recordScenarioPdfLost;

        internal MissionTurnProcessor(
            GameSession session,
            Action<PlanetFaction, Region, float> recordIntelGain,
            Action<RegionFaction, long, Faction> recordScenarioPdfLost,
            Action<IntelObservation> recordTargetObservation = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _missionRules = new MissionRules(
                _session.Rules.Skills.Stealth,
                _session.Rules.Skills.Tactics);
            BattleAftermathDependencies aftermath = new(
                _session.CurrentDate,
                _session.Random,
                new PlayerBattleAftermathSink(_session.Sector.PlayerForce));
            _battleExecution = new BattleExecutionContext(
                _session.Rules,
                _session.Random,
                aftermath);
            _recordIntelGain = recordIntelGain;
            _recordTargetObservation = recordTargetObservation;
            _recordScenarioPdfLost = recordScenarioPdfLost;
        }

        internal void ProcessStrategicCombatMissions(
            IEnumerable<Order> strategicCombatOrders,
            ICollection<StrategicCombatResult> strategicCombatResults)
        {
            var resolver = new StrategicCombatResolver(
                rng: _session.Random,
                recordIntelGain: _recordIntelGain,
                recordTargetObservation: _recordTargetObservation);
            foreach (Order order in strategicCombatOrders)
            {
                if (order.Mission is not StrategicCombatMission mission) continue;

                long targetStrengthBefore = mission.RegionFaction?.MilitaryStrength ?? 0;
                StrategicCombatResult result = resolver.Resolve(mission);
                long? inferredForceId = null;
                if (!order.StrategicInvasionForceId.HasValue)
                {
                    List<long> inferredIds = order.AssignedSquads
                        .Select(squad => _session.Sector.StrategicInvasionForces.FirstOrDefault(force =>
                            force.IsActive && force.CommandSquad == squad)?.Id)
                        .Where(id => id.HasValue)
                        .Select(id => id.Value)
                        .Distinct()
                        .ToList();
                    // A direct order can be built by a caller without the runtime identity. Only
                    // infer it when exactly one command identity owns the order; never pick the
                    // first active invasion force when multiple identities are present.
                    inferredForceId = inferredIds.Count == 1 ? inferredIds[0] : null;
                }
                result.OriginatingStrategicInvasionForceId = order.StrategicInvasionForceId ?? inferredForceId;
                long targetStrengthAfter = mission.RegionFaction?.MilitaryStrength ?? 0;
                _recordScenarioPdfLost?.Invoke(
                    mission.RegionFaction,
                    Math.Max(0, targetStrengthBefore - targetStrengthAfter),
                    mission.Attacker);
                strategicCombatResults.Add(result);
                GameLog.Debug(() =>
                    $"Strategic combat {result.Attacker?.Name} -> {DescribeRegionFaction(result.Target)}: "
                    + $"outcome={result.Outcome}, won={result.AttackerWon}, controlChanged={result.ControlChanged}, "
                    + $"committed={result.CommittedBattleValue}, defenderBV={result.DefenderBattleValue}, "
                    + $"effective={result.AttackerEffectiveStrength:F0}/{result.DefenderEffectiveStrength:F0}, "
                    + $"losses={result.AttackerLosses}/{result.DefenderLosses}, survivors={result.AttackerSurvivors}, "
                    + $"contributions={DescribeStrategicContributions(mission.Contributions)}");
            }
        }

        // One mission queued for the day scheduler, plus what its completion needs afterwards. The
        // post-mission work (outcome recording, field-XP diffing) used to run inline right after the
        // mission finished; with every mission advancing a day at a time, none of them are finished
        // until the scheduler returns, so the pieces have to be held until then.
        private sealed class ScheduledMission
        {
            internal Order Order { get; init; }
            internal MissionContext Context { get; init; }
            internal MissionStepDriver Driver { get; init; }
            internal bool IsPlayerOrder { get; init; }
            internal SoldierProgressLog.ProgressSnapshot XpBefore { get; init; }
        }

        internal void ProcessCombatMissions(
            IEnumerable<Order> combatOrders,
            ICollection<MissionContext> missionContexts,
            ICollection<ConstructionProgressReport> constructionReports = null)
        {
            List<ScheduledMission> scheduled = new();

            foreach (Order order in combatOrders)
            {
                if (order?.Mission?.MissionType == MissionType.Recruitment)
                {
                    continue;
                }
                if (order?.Mission?.RegionFaction == null
                    && order?.Mission?.Region != null
                    && order.Mission.TargetFaction != null)
                {
                    ResolveNoContactSearch(order);
                    continue;
                }

                if (order?.Mission?.MissionType == MissionType.Extermination
                    && FactionCapabilities.HasDormantPopulations(order.Mission.TargetFaction)
                    && (order.Mission.RegionFaction == null
                        || order.Mission.RegionFaction.StrategicInvasionForceId == null))
                {
                    ResolveDormantPopulationCulling(order, missionContexts);
                    continue;
                }

                // A Defense order still runs no steps of its own: it is a posture, resolved when
                // something attacks (PrepareAssaultMissionStep.ContestPreparation).
                if (order.Mission.MissionType == MissionType.DefenseInDepth) continue;
                // Patrol, by contrast, IS an active mission now - a week of sweeping its own ground, with
                // a daily check that earns field experience (PatrolSweepMissionStep). It used to sit here
                // beside Defense as a bare `continue`, which is why it granted no experience at all.
                // Likewise a show of force: the squads hold position to be seen. They are pulled
                // into battle only if the region is attacked, via GetRegionalDefensiveSquads.
                if (order.Mission.MissionType == MissionType.ShowOfForce) continue;

                if (order.Mission is ConstructionMission constructionMission)
                {
                    ResolveSquadConstruction(order, constructionMission, constructionReports);
                    continue;
                }

                bool isPlayerOrder = order.OwnerFaction?.IsPlayerFaction == true
                    || order.Force.AllPlayerSoldiers.Any();
                TacticalEntityIdAllocator entityIds = new();
                ChapterOperationalDoctrine doctrine = isPlayerOrder
                    ? _session.Sector?.PlayerForce?.Army?.ChapterOperationalDoctrine
                    : null;

                // Player formations are admitted through the canonical duty decision. A squad that
                // fails its structural doctrine remains assigned but is kept out of this battle;
                // the issue is carried into the mission log instead of disappearing through a raw
                // IsCombatEffective filter.
                List<string> readinessMessages = [];
                List<SquadReadinessBlocker> readinessBlockers = [];
                List<MissionSquadReadinessIssue> readinessIssues = [];
                List<BattleSquad> involvedBattleSquads = [];
                foreach (Squad squad in order.AssignedSquads)
                {
                    if (squad == null) continue;
                    SquadReadinessSnapshot readiness = isPlayerOrder
                        ? SquadReadinessService.Evaluate(squad, doctrine: doctrine)
                        : null;
                    BattleSquad battleSquad = isPlayerOrder
                        ? BattleSquadFactory.Create(true, squad, doctrine)
                        : new BattleSquad(false, squad);
                    if (battleSquad.AbleSoldiers.Count > 0)
                    {
                        involvedBattleSquads.Add(battleSquad);
                    }
                    else if (isPlayerOrder)
                    {
                        if (readiness?.StructuralBlockers.Count > 0)
                        {
                            readinessBlockers.AddRange(readiness.StructuralBlockers);
                            string message =
                                $"{squad.Name} withheld: {string.Join(", ", readiness.StructuralBlockers.Select(SquadReadinessPresentation.BlockerLabel))}.";
                            readinessMessages.Add(message);
                            readinessIssues.Add(new MissionSquadReadinessIssue(
                                squad,
                                MissionAvailabilityStatus.SquadStructuralBlocker,
                                readiness.StructuralBlockers,
                                message));
                        }
                        else
                        {
                            string message = $"{squad.Name} withheld: no duty-ready members are available.";
                            readinessMessages.Add(message);
                            readinessIssues.Add(new MissionSquadReadinessIssue(
                                squad,
                                MissionAvailabilityStatus.NoDutyReadyParticipants,
                                [],
                                message));
                        }
                    }
                }

                involvedBattleSquads.AddRange(order.AssignedCharacters
                    .Select(character => isPlayerOrder
                        ? BattleSquadFactory.CreateAttachedCharacter(
                            character,
                            entityIds.GetNextId(),
                            order.OwnerFaction,
                            doctrine)
                        : character.IsCombatEffective
                            ? new BattleSquad(new BattleElementSpec(
                                entityIds.GetNextId(),
                                character.Name,
                                character.AssignedSquad?.Faction ?? order.OwnerFaction,
                                new ISoldier[] { character },
                                new BattleElementTraits(
                                    IsHeadquarters: character.AssignedSquad?.SquadTemplate?.SquadType
                                        .HasFlag(SquadTypes.HQ) == true),
                                CampaignSquad: character.AssignedSquad,
                                CampaignCharacter: character))
                            : null)
                    .Where(battleSquad => battleSquad != null)
                    .ToList());
                if (isPlayerOrder)
                {
                    readinessMessages.AddRange(order.AssignedCharacters
                        .Where(character => !DutyReadinessService.Evaluate(character, doctrine).IsDutyReady)
                        .Select(character =>
                            $"{character.Name} withheld: {DutyReadinessService.Evaluate(character, doctrine).Reason ?? "not duty-ready"}."));
                }
                if (involvedBattleSquads.Count == 0)
                {
                    MissionContext unavailable = new(order, [], [], doctrine);
                    unavailable.MarkAvailabilityBlocked(
                        readinessBlockers.Count > 0
                            ? MissionAvailabilityStatus.SquadStructuralBlocker
                            : MissionAvailabilityStatus.NoDutyReadyParticipants,
                        readinessBlockers);
                    foreach (MissionSquadReadinessIssue issue in readinessIssues)
                    {
                        unavailable.RecordReadinessIssue(issue);
                    }
                    unavailable.NoViableTarget = true;
                    foreach (string message in readinessMessages)
                    {
                        unavailable.AddLog($"Duty readiness: {message}");
                    }
                    if (readinessMessages.Count == 0)
                    {
                        unavailable.AddLog("Duty readiness: no eligible force was available for this mission.");
                    }
                    missionContexts.Add(unavailable);
                    continue;
                }

                // Squad.Faction resolves through SquadTemplate.Faction and can be absent; an
                // unguarded read in a log-only path means raising the log level throws. See the
                // matching guard in BattleTurnResolver's constructor.
                GameLog.Debug(() =>
                    $"Combat mission start {order.OwnerFaction?.Name ?? "Unknown faction"} "
                    + $"{order.Mission.MissionType} -> {DescribeRegionFaction(order.Mission.RegionFaction)}: "
                    + $"squads={order.AssignedSquads.Count}, soldiers={order.AssignedSquads.Sum(s => s.Members.Count)}, "
                    + $"battleValue={SquadBattleValue(order.AssignedSquads)}");
                IEnumerable<List<BattleSquad>> missionElements =
                    BuildMissionElements(order.Mission.MissionType, involvedBattleSquads);

                foreach (List<BattleSquad> elementSquads in missionElements)
                {
                    MissionContext context = new(order, elementSquads, new List<BattleSquad>(), doctrine);
                    if (readinessBlockers.Count > 0)
                    {
                        context.MarkAvailabilityBlocked(
                            MissionAvailabilityStatus.SquadStructuralBlocker,
                            readinessBlockers);
                    }
                    foreach (MissionSquadReadinessIssue issue in readinessIssues)
                    {
                        context.RecordReadinessIssue(issue);
                    }
                    foreach (string message in readinessMessages)
                    {
                        context.AddLog($"Duty readiness: {message}");
                    }
                    var execution = new MissionExecutionContext(
                        context,
                        _missionRules,
                        _session.Random,
                        _battleExecution,
                        new TacticalEntityIdAllocator());
                    scheduled.Add(new ScheduledMission
                    {
                        Order = order,
                        Context = context,
                        Driver = new MissionStepDriver(
                            execution, MissionStepOrchestrator.GetStartingStep(execution)),
                        IsPlayerOrder = isPlayerOrder,
                        XpBefore = isPlayerOrder
                            ? MissionFieldExperienceLog.Snapshot(context.StartingPlayerParticipants)
                            : null
                    });
                }
            }

            // Every mission now advances a day at a time, together, rather than each running to
            // completion in turn. Missions operating in the same region therefore see each other's
            // effects as they land - which is what makes a diversion able to shelter an infiltrator.

            // ONE ENTRY PER DISTINCT ORDER (Design/Reference/SpecialistAttachment.md §8 trap 1).
            // BuildMissionElements fans a single order into several independent single-squad
            // elements under MissionForceMode.IndependentSquads, each with its own driver and its
            // own MissionContext -- so a field-care pass keyed on the ELEMENT would treat the same
            // order's wounded once per element and make one Apothecary silently worth three. The
            // dedup lives here, at the scheduler's altitude, exactly as Phase 1b's daily-healing
            // pass does.
            Dictionary<int, Order> distinctPlayerOrders = [];
            Dictionary<int, FieldCareReport> fieldCareByOrder = [];
            foreach (ScheduledMission mission in scheduled.Where(m => m.IsPlayerOrder))
            {
                if (mission.Order == null || distinctPlayerOrders.ContainsKey(mission.Order.Id))
                {
                    continue;
                }
                distinctPlayerOrders[mission.Order.Id] = mission.Order;
                fieldCareByOrder[mission.Order.Id] = new FieldCareReport();
            }
            IReadOnlyList<BaseSkill> medicalSkills = FieldCareService.ResolveMedicalSkills(
                _session.Rules?.RatingDefinitions, _session.Rules?.BaseSkillMap,
                _session.Rules?.RatingConsumers);

            MissionDayScheduler.Run(
                scheduled.Select(mission => mission.Driver).ToList(),
                onDayStart: _ => ResetCommittedAttention(),
                onActingDayStart: (missions, day) =>
                    ResolveReciprocalAssaults(missions, day),
                onDayEnd: day =>
                {
                    ApplyDailyHealing();
                    ApplyDailyFieldCare(
                        distinctPlayerOrders,
                        fieldCareByOrder,
                        medicalSkills,
                        _session.Rules?.RatingConsumers,
                        day);
                });

            foreach (ScheduledMission mission in scheduled)
            {
                MissionContext context = mission.Context;
                Order order = mission.Order;
                // Every element of an order carries the same order-wide medical summary. The
                // TREATMENT ran once (see the dedup above); this is only the report, and each
                // element's debrief is its own screen, so showing it on each is right rather than
                // double-counting.
                if (order != null
                    && fieldCareByOrder.TryGetValue(order.Id, out FieldCareReport fieldCare)
                    && fieldCare.HasApothecary)
                {
                    context.FieldCare = fieldCare;
                }
                missionContexts.Add(context);
                if (mission.IsPlayerOrder)
                {
                    MissionOutcomeRecorder.RecordMissionOutcome(context, _session.CurrentDate);
                    MissionFieldExperienceLog.LogGains(context, mission.XpBefore);
                }
                GameLog.Debug(() =>
                    $"Combat mission result {order.OwnerFaction?.Name ?? "Unknown faction"} "
                    + $"{order.Mission.MissionType} -> {DescribeRegionFaction(order.Mission.RegionFaction)}: "
                    + $"elementSquads={context.MissionSquads.Count}, impact={context.Impact:F2}, "
                    + $"enemiesKilled={context.EnemiesKilled}, days={context.DaysElapsed}, "
                    + $"killCredits={context.EnemyKillCredits}, "
                    + $"logEntries={context.Log.Count}");
            }
        }

        private void ResolveDormantPopulationCulling(
            Order order,
            ICollection<MissionContext> missionContexts)
        {
            RegionFaction target = order.Mission.RegionFaction;
            Region region = order.Mission.Region;
            Faction targetFaction = order.Mission.TargetFaction;
            List<BattleSquad> squads = order.AssignedSquads
                .Where(squad => squad != null)
                .Select(squad => BattleSquadFactory.Create(
                    true,
                    squad,
                    _session.Sector?.PlayerForce?.Army?.ChapterOperationalDoctrine))
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .ToList();
            MissionContext context = new(order, squads, []);
            if (squads.Count == 0)
            {
                context.MarkAvailabilityBlocked(MissionAvailabilityStatus.NoDutyReadyParticipants);
                context.NoViableTarget = true;
                context.AddLog("Duty readiness: no eligible force was available for dormant-population culling.");
            }
            PlanetFaction observer = region?.Planet?.PlanetFactionMap
                .GetValueOrDefault(order.OwnerFaction?.Id ?? _session.Rules.PlayerFaction.Id);
            FactionIntelBelief belief = observer?.GetTargetBelief(region, targetFaction);
            bool contact = target != null
                && (belief?.Level >= IntelLevel.Located
                || belief?.Level >= IntelLevel.Confirmed
                    && _session.Random.GetLinearDouble() < 0.75);
            if (contact)
            {
                DormantPopulationCullingResult result = DormantPopulationCulling.Resolve(
                    target, belief, _session.Rules.FactionBehaviorRules,
                    observer?.GetRegionAwareness(region) is float awareness
                        ? (long)Math.Max(0, awareness * 100)
                        : 0);
                if (result.PopulationRemoved > 0)
                {
                    target.RemoveMilitaryStrength(result.PopulationRemoved);
                    target.DormantConsolidation = Math.Clamp(
                        target.DormantConsolidation - result.ConsolidationRemoved, 0.0, 1.0);
                }
                context.Impact = result.PopulationRemoved;
                context.EnemiesKilled = result.PopulationRemoved > 0 ? 1 : 0;
                context.AddLog($"Dormant population culling confirmed contact and removed {result.PopulationRemoved:N0} population.");
            }
            else
            {
                context.NoViableTarget = true;
                context.AddLog(target == null
                    ? "Dormant population culling found no contact; the search consumed this operation's capacity."
                    : "Dormant population culling failed to confirm contact; the search consumed this operation's capacity.");
            }
            missionContexts.Add(context);
        }

        private void ResolveNoContactSearch(Order order)
        {
            if (_recordTargetObservation == null || order?.Mission?.Region == null)
            {
                return;
            }

            Faction observerFaction = order.Force.Squads
                .Select(squad => squad?.Faction)
                .Concat(order.Force.Characters.Select(character => character?.AssignedSquad?.Faction))
                .FirstOrDefault(faction => faction != null);
            if (observerFaction == null) return;

            Planet planet = order.Mission.Region.Planet;
            PlanetFaction observer = planet.PlanetFactionMap.GetValueOrDefault(observerFaction.Id);
            if (observer == null)
            {
                observer = new PlanetFaction(observerFaction)
                {
                    IsPublic = observerFaction.IsPlayerFaction
                };
                planet.PlanetFactionMap[observerFaction.Id] = observer;
                planet.NotifyPlanetFactionAdded(observer);
            }

            Faction target = order.Mission.TargetFaction;
            FactionIntelBelief previous = observer.GetTargetBelief(order.Mission.Region, target);
            float negativeEvidence = -Math.Max(
                0.25f,
                Math.Min(2f, previous?.Evidence ?? 0.25f));
            _recordTargetObservation(new IntelObservation(
                observer,
                order.Mission.Region,
                target,
                negativeEvidence,
                null,
                null,
                IntelObservationSource.Recon,
                0));
            GameLog.Debug(() =>
                $"No-contact search {observerFaction.Name} -> "
                + $"{order.Mission.Region.Planet.Name}/{order.Mission.Region.Name}/"
                + $"{target.Name}; evidence={negativeEvidence:F2}");
        }

        internal static void ResolveReciprocalAssaults(
            IReadOnlyList<MissionStepDriver> missions,
            int day,
            Action<MissionStepDriver, MissionStepDriver> resolvePair = null)
        {
            List<MissionStepDriver> ready = missions
                .Where(driver => !driver.IsComplete
                    && driver.NextStep is PrepareAssaultMissionStep
                    && driver.State.Order?.Mission?.MissionType == MissionType.Advance
                    && !driver.State.OperatingDaysSpent
                    && !driver.State.MissionLossesExceedAggressionThreshold
                    && driver.State.MissionSquads.Any(
                        squad => squad.AbleSoldiers.Count > 0)
                    && driver.State.DaysElapsed < day)
                .ToList();
            HashSet<MissionStepDriver> paired = [];

            foreach (MissionStepDriver candidateFirst in ready)
            {
                if (paired.Contains(candidateFirst)) continue;
                MissionStepDriver first = candidateFirst;
                MissionStepDriver second = ready.FirstOrDefault(candidate =>
                    !ReferenceEquals(candidate, first)
                    && !paired.Contains(candidate)
                    && AreReciprocalAssaults(first.State, candidate.State));
                if (second == null) continue;

                // Put the player on the resolver's first side whenever exactly one participant is
                // player-controlled. Existing battle history attributes career kill credit to that
                // side, so preserving the orientation keeps reports and experience correct.
                if (second.State.MissionSquads.Any(squad => squad.IsPlayerSquad)
                    && !first.State.MissionSquads.Any(squad => squad.IsPlayerSquad))
                {
                    (first, second) = (second, first);
                }

                (resolvePair ?? ReciprocalAssaultResolver.ResolveDay)(first, second);
                paired.Add(first);
                paired.Add(second);
            }
        }

        internal static bool AreReciprocalAssaults(MissionContext first, MissionContext second)
        {
            RegionFaction firstTarget = first?.Order?.Mission?.RegionFaction;
            RegionFaction secondTarget = second?.Order?.Mission?.RegionFaction;
            Faction firstAttacker = first?.MissionSquads.FirstOrDefault()?.Faction;
            Faction secondAttacker = second?.MissionSquads.FirstOrDefault()?.Faction;
            if (firstTarget == null || secondTarget == null
                || firstAttacker == null || secondAttacker == null)
            {
                return false;
            }

            return ReferenceEquals(firstTarget.Region, secondTarget.Region)
                && firstTarget.PlanetFaction.Faction.Id == secondAttacker.Id
                && secondTarget.PlanetFaction.Faction.Id == firstAttacker.Id;
        }

        internal static IReadOnlyList<List<BattleSquad>> BuildMissionElements(
            MissionType missionType,
            List<BattleSquad> involvedBattleSquads)
        {
            if (MissionForcePolicy.GetMode(missionType) == MissionForceMode.IndependentSquads)
            {
                return involvedBattleSquads
                    .Select(squad => new List<BattleSquad> { squad })
                    .ToList();
            }
            return new List<List<BattleSquad>> { involvedBattleSquads };
        }

        // Committed attention is per-DAY state, so it is wiped before each day resolves: a feint that
        // pulled the screen aside yesterday shelters nobody today. Sweeping the whole sector is
        // cheap (no allocation, a few hundred region-factions) and avoids having to track which
        // regions a shaping step touched.
        // End-of-day recovery for the whole Chapter, not just the squads on this order
        // (Design/Reference/CasualtyRealism.md §2.5). Swept over the full order of battle because the
        // day boundary is a property of the campaign, not of any one mission - and because the
        // pass is idempotent and cheap, sweeping is simpler and safer than reconciling which men
        // belong to which of the day's drivers. Run exactly once per day from the scheduler's
        // onDayEnd hook; see MissionDayScheduler.Run for why it cannot hang off a driver.
        private void ApplyDailyHealing()
        {
            MedicalTurnProcessor.ApplyDailyHealing(
                _session.Sector?.PlayerForce?.Army?.OrderOfBattle?.GetAllMembers());
        }

        /// <summary>
        /// Apothecary field care for the day just ended (Design/Reference/CasualtyRealism.md §2.6).
        /// Runs after the day's fighting and after natural daily healing, so a brother hit today and
        /// treated tonight enters TOMORROW's battle at reduced severity -- battle setup reads live
        /// wound state per battle, which is what makes the daily seam worth having at all.
        ///
        /// Once per distinct order, never per mission element: see the dedup at the call site.
        ///
        /// An order whose own missions finished early keeps receiving care until the day loop ends,
        /// which is deliberate: the force is still in the field until the turn resolves, and its
        /// squads still hold CurrentOrders, so the garrison pass will not pick them up either. The
        /// alternative -- stopping care the moment the last element completes -- would mean an
        /// Apothecary idling beside wounded brothers because the shooting stopped.
        /// </summary>
        private static void ApplyDailyFieldCare(
            IReadOnlyDictionary<int, Order> distinctPlayerOrders,
            IReadOnlyDictionary<int, FieldCareReport> reports,
            IReadOnlyList<BaseSkill> medicalSkills,
            RatingConsumerBindings ratingBindings,
            int day)
        {
            foreach (KeyValuePair<int, Order> entry in distinctPlayerOrders)
            {
                FieldCareService.ApplyDailyFieldCare(
                    entry.Value, reports[entry.Key], medicalSkills, day, ratingBindings);
            }
        }

        private void ResetCommittedAttention()
        {
            foreach (Planet planet in _session.Sector.Planets.Values)
            {
                foreach (Region region in planet.Regions)
                {
                    foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                    {
                        regionFaction.CommittedAttention = 0f;
                    }
                }
            }
        }

        internal static void ProcessConstructionOrders(IEnumerable<Order> constructionOrders)
        {
            // Squad-less construction orders resolve instantly at the planner's (possibly
            // fractional) build amount and do not create a mission context.
            List<Order> orders = constructionOrders.ToList();
            foreach (Order order in orders)
            {
                if (order.Mission is ConstructionMission mission)
                {
                    ApplyConstruction(mission, mission.BuildAmount);
                }
            }
            if (orders.Count > 0)
            {
                GameLog.Debug(() =>
                    $"Construction resolved: orders={orders.Count}, {SummarizeConstructionOrders(orders)}");
            }
        }

        /// <summary>
        /// Resolves squad-less feed orders: a Consumption faction eats with the force its controller could spare.
        /// </summary>
        /// <remarks>
        /// Dispatched alongside squad-less construction and for the same reason - the order resolves
        /// instantly and creates no MissionContext. What makes it a mission rather than the planet
        /// update's old side effect is the budget: the troops here are the residual left after
        /// defence, offensives, development, patrols and spreading, not the whole force re-derived
        /// from scratch (Design/Reference/ConsumptionFeedingAsMission.md).
        /// </remarks>
        internal static void ProcessFeedOrders(IEnumerable<Order> feedOrders)
        {
            List<Order> orders = feedOrders.ToList();
            foreach (Order order in orders)
            {
                if (order.Mission is FeedMission mission)
                {
                    ConsumptionTurnProcessor.ResolveFeeding(
                        mission.RegionFaction,
                        mission.CommittedBattleValue);
                }
            }
            if (orders.Count > 0)
            {
                GameLog.Debug(() =>
                    $"Feeding resolved: orders={orders.Count}, "
                    + $"committedBV={orders.Sum(o => ((FeedMission)o.Mission).CommittedBattleValue)}");
            }
        }

        private void ResolveSquadConstruction(
            Order order,
            ConstructionMission mission,
            ICollection<ConstructionProgressReport> constructionReports)
        {
            BaseSkill engineering = _session.Rules.Skills.EngineeringFortification;
            float totalSkill = order.Force.AllSoldiers
                .Sum(soldier => soldier.GetTotalSkillValue(engineering));
            // Construction produces no MissionContext, so the levels captured here are the only
            // record the end-of-turn report can be built from (issue #5: without them a fortifying
            // squad is indistinguishable from an idle one). Only the "before" side of the shared
            // position is captured - the report reads the "after" live, once the whole turn has
            // settled, so it can never quote a rating the region has already moved past.
            double before = GetConstructionLevel(mission);
            double sharedBefore = GetSharedConstructionLevel(mission);
            ApplyConstructionPoints(mission, totalSkill / EngineeringBuildDivisor);
            constructionReports?.Add(new ConstructionProgressReport(
                mission.ConstructionType,
                mission.RegionFaction,
                order.Force.Squads.Select(s => s.Name)
                    .Concat(order.Force.Characters.Select(character => character.Name))
                    .ToList(),
                order.OwnerFaction?.IsPlayerFaction == true,
                before,
                GetConstructionLevel(mission),
                sharedBefore));
        }

        /// <summary>
        /// Applies a squad's weekly engineering output, measured in construction points rather than
        /// levels.
        /// </summary>
        /// <remarks>
        /// A squad's output does not depend on how good the works already are, so its effort is a
        /// flat number of points and the level it buys decelerates on the same 10x-per-band curve
        /// the AI pays through DefenseBuildCost. Adding the raw figure to the level instead - as
        /// this did before - let a squad buy a level of Massive works for the same week of labour
        /// that bought its first level of Minimal ones.
        /// </remarks>
        internal static void ApplyConstructionPoints(ConstructionMission mission, double points)
        {
            if (mission.ConstructionType == DefenseType.Organization)
            {
                ApplyConstruction(mission, points);
                return;
            }

            double before = GetConstructionLevel(mission);
            RegionDefenses.Build(mission.RegionFaction, mission.ConstructionType, points);
            double after = GetConstructionLevel(mission);
            GameLog.Trace(() =>
                $"Construction applied {DescribeRegionFaction(mission.RegionFaction)}: "
                + $"{mission.ConstructionType} {before:F2}->{after:F2} (+{points:F2} points)");
        }

        /// <summary>
        /// Applies a level delta directly. Used by the NPC development planner, whose build amounts
        /// are already priced against the band they are buying into (FactionStrategyController), so
        /// structural improvements arrive as levels. Reorganization interprets the same amount as
        /// effort and converts it to a fixed quantity of military BV.
        /// </summary>
        internal static void ApplyConstruction(ConstructionMission mission, double amount)
        {
            double before = GetConstructionLevel(mission);
            if (mission.ConstructionType == DefenseType.Organization)
            {
                long requested = amount <= 0
                    ? 0
                    : Math.Max(1L, (long)Math.Round(
                        amount * StrategicCombatRules.ReorganizationBattleValuePerEffort));
                mission.RegionFaction.ReorganizeMilitaryStrength(requested);
            }
            else
            {
                mission.RegionFaction.AddDefense(mission.ConstructionType, amount);
            }
            double after = GetConstructionLevel(mission);
            GameLog.Trace(() =>
                $"Construction applied {DescribeRegionFaction(mission.RegionFaction)}: "
                + $"{mission.ConstructionType} {before:F2}->{after:F2} (requested +{amount:F2})");
        }

        internal static double GetSharedConstructionLevel(ConstructionMission mission) =>
            mission.ConstructionType == DefenseType.Organization
                ? mission.RegionFaction.Organization
                : RegionDefenses.GetShared(mission.RegionFaction, mission.ConstructionType);

        internal static double GetConstructionLevel(ConstructionMission mission)
        {
            return mission.ConstructionType switch
            {
                DefenseType.Organization => mission.RegionFaction.Organization,
                _ => mission.RegionFaction.GetDefense(mission.ConstructionType)
            };
        }

        private static string SummarizeConstructionOrders(IEnumerable<Order> orders)
        {
            List<ConstructionMission> missions = orders
                .Select(o => o.Mission)
                .OfType<ConstructionMission>()
                .ToList();
            if (missions.Count == 0) return "none";

            return string.Join("; ", missions
                .GroupBy(m => new
                {
                    Planet = m.RegionFaction.Region.Planet.Name,
                    Region = m.RegionFaction.Region.Name,
                    Faction = m.RegionFaction.PlanetFaction.Faction.Name,
                    m.ConstructionType
                })
                .Select(g =>
                    $"{g.Key.Faction}/{g.Key.Planet}/{g.Key.Region} {g.Key.ConstructionType}+{g.Sum(m => m.BuildAmount):F2}"));
        }

        private static string DescribeStrategicContributions(
            IEnumerable<StrategicCombatContribution> contributions)
        {
            List<string> parts = contributions
                .Where(c => c.BattleValue > 0)
                .Select(c => $"{c.StagingFaction?.Region.Name ?? "unknown"}:{c.BattleValue}")
                .ToList();
            return parts.Count == 0 ? "none" : string.Join(",", parts);
        }

        internal static string DescribeRegionFaction(RegionFaction regionFaction)
        {
            if (regionFaction == null) return "unknown";
            return $"{regionFaction.Region.Planet.Name}/{regionFaction.Region.Name}/"
                + $"{regionFaction.PlanetFaction.Faction.Name}";
        }

        private static long SquadBattleValue(IEnumerable<Squad> squads)
        {
            return squads
                .SelectMany(squad => squad.Members)
                .Sum(member => (long)member.Template.BattleValue);
        }
    }
}
