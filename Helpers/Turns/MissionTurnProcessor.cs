using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Assault;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
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
        private readonly Action<RegionFaction, long, Faction> _recordScenarioPdfLost;

        internal MissionTurnProcessor(
            GameSession session,
            Action<PlanetFaction, Region, float> recordIntelGain,
            Action<RegionFaction, long, Faction> recordScenarioPdfLost)
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
            _recordScenarioPdfLost = recordScenarioPdfLost;
        }

        internal void ProcessStrategicCombatMissions(
            IEnumerable<Order> strategicCombatOrders,
            ICollection<StrategicCombatResult> strategicCombatResults)
        {
            var resolver = new StrategicCombatResolver(
                rng: _session.Random,
                recordIntelGain: _recordIntelGain);
            foreach (Order order in strategicCombatOrders)
            {
                if (order.Mission is not StrategicCombatMission mission) continue;

                long targetStrengthBefore = mission.RegionFaction?.MilitaryStrength ?? 0;
                StrategicCombatResult result = resolver.Resolve(mission);
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

                bool isPlayerOrder = order.AssignedSquads.First().Faction.IsPlayerFaction;

                // Never construct a BattleSquad from a depleted squad. This also protects orders
                // whose force was wiped out earlier in the same resolution pass.
                List<BattleSquad> involvedBattleSquads = order.AssignedSquads
                    .Where(s => s.Members.Any(m => m.CanFight))
                    .Select(s => new BattleSquad(isPlayerOrder, s))
                    .ToList();
                if (involvedBattleSquads.Count == 0) continue;

                GameLog.Debug(() =>
                    $"Combat mission start {order.AssignedSquads.First().Faction.Name} "
                    + $"{order.Mission.MissionType} -> {DescribeRegionFaction(order.Mission.RegionFaction)}: "
                    + $"squads={order.AssignedSquads.Count}, soldiers={order.AssignedSquads.Sum(s => s.Members.Count)}, "
                    + $"battleValue={SquadBattleValue(order.AssignedSquads)}");
                IEnumerable<List<BattleSquad>> missionElements =
                    BuildMissionElements(order.Mission.MissionType, involvedBattleSquads);

                foreach (List<BattleSquad> elementSquads in missionElements)
                {
                    MissionContext context = new(order, elementSquads, new List<BattleSquad>());
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
            MissionDayScheduler.Run(
                scheduled.Select(mission => mission.Driver).ToList(),
                onDayStart: _ => ResetCommittedAttention(),
                onActingDayStart: (missions, day) =>
                    ResolveReciprocalAssaults(missions, day));

            foreach (ScheduledMission mission in scheduled)
            {
                MissionContext context = mission.Context;
                Order order = mission.Order;
                missionContexts.Add(context);
                if (mission.IsPlayerOrder)
                {
                    MissionOutcomeRecorder.RecordMissionOutcome(context, _session.CurrentDate);
                    MissionFieldExperienceLog.LogGains(context, mission.XpBefore);
                }
                GameLog.Debug(() =>
                    $"Combat mission result {order.AssignedSquads.First().Faction.Name} "
                    + $"{order.Mission.MissionType} -> {DescribeRegionFaction(order.Mission.RegionFaction)}: "
                    + $"elementSquads={context.MissionSquads.Count}, impact={context.Impact:F2}, "
                    + $"enemiesKilled={context.EnemiesKilled}, days={context.DaysElapsed}, "
                    + $"killCredits={context.EnemyKillCredits}, "
                    + $"logEntries={context.Log.Count}");
            }
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
            Faction firstAttacker = first?.MissionSquads.FirstOrDefault()?.Squad?.Faction;
            Faction secondAttacker = second?.MissionSquads.FirstOrDefault()?.Squad?.Faction;
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

        private void ResolveSquadConstruction(
            Order order,
            ConstructionMission mission,
            ICollection<ConstructionProgressReport> constructionReports)
        {
            BaseSkill engineering = _session.Rules.Skills.EngineeringFortification;
            float totalSkill = order.AssignedSquads
                .SelectMany(s => s.Members)
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
                order.AssignedSquads.Select(s => s.Name).ToList(),
                order.AssignedSquads.Any(s => s.Faction?.IsPlayerFaction == true),
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
