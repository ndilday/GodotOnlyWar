using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Missions;
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
        private const float DiversionThreatScale = 4.0f;

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

        internal void ProcessCombatMissions(
            IEnumerable<Order> combatOrders,
            ICollection<MissionContext> missionContexts)
        {
            foreach (Order order in combatOrders)
            {
                if (order.Mission.MissionType == MissionType.DefenseInDepth) continue;
                // Diversions already resolved in the pre-planning shaping phase; their squads
                // remain on the map only to defend if the feint draws a counterattack this turn.
                if (order.Mission.MissionType == MissionType.Diversion) continue;
                // A patrol is a standing defensive screen, not an active mission.
                if (order.Mission.MissionType == MissionType.Patrol) continue;

                if (order.Mission is ConstructionMission constructionMission)
                {
                    ResolveSquadConstruction(order, constructionMission);
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
                    MissionStepOrchestrator.GetStartingStep(execution)
                        .ExecuteMissionStep(execution, 0, null);
                    missionContexts.Add(context);
                    if (isPlayerOrder)
                    {
                        MissionOutcomeRecorder.RecordMissionOutcome(context, _session.CurrentDate);
                    }
                    GameLog.Debug(() =>
                        $"Combat mission result {order.AssignedSquads.First().Faction.Name} "
                        + $"{order.Mission.MissionType} -> {DescribeRegionFaction(order.Mission.RegionFaction)}: "
                        + $"elementSquads={elementSquads.Count}, impact={context.Impact:F2}, "
                        + $"enemiesKilled={context.EnemiesKilled}, days={context.DaysElapsed}, "
                        + $"killCredits={context.EnemyKillCredits}, "
                        + $"logEntries={context.Log.Count}");
                }
            }
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

        // Diversions resolve in the pre-planning shaping phase so the projected threat already
        // exists when NPC factions choose their orders for the turn.
        internal void ProcessDiversionMissions(
            IEnumerable<Order> diversionOrders,
            ICollection<MissionContext> missionContexts)
        {
            foreach (Order order in diversionOrders)
            {
                bool isPlayerOrder = order.AssignedSquads.First().Faction.IsPlayerFaction;
                List<BattleSquad> involvedBattleSquads = order.AssignedSquads
                    .Where(s => s.Members.Any(m => m.CanFight))
                    .Select(s => new BattleSquad(isPlayerOrder, s))
                    .ToList();
                if (involvedBattleSquads.Count == 0) continue;

                MissionContext context = new(order, involvedBattleSquads, new List<BattleSquad>());
                var execution = new MissionExecutionContext(
                    context,
                    _missionRules,
                    _session.Random,
                    _battleExecution,
                    new TacticalEntityIdAllocator());
                MissionStepOrchestrator.GetStartingStep(execution)
                    .ExecuteMissionStep(execution, 0, null);
                missionContexts.Add(context);
                if (isPlayerOrder)
                {
                    MissionOutcomeRecorder.RecordMissionOutcome(context, _session.CurrentDate);
                }
                ApplyDiversionEffect(order, context);
            }
        }

        internal static void ClearDiversionEffects(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                foreach (Region region in planet.Regions)
                {
                    foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                    {
                        regionFaction.PerceivedThreatBonus = 0;
                        regionFaction.ProvocationLevel = 0;
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

        private static void ApplyDiversionEffect(Order order, MissionContext context)
        {
            Mission mission = order.Mission;
            RegionFaction targetFaction = mission.RegionFaction;
            long actualManpower = order.AssignedSquads.Sum(s => s.Members.Count);
            if (actualManpower <= 0) return;

            float clampedImpact = mission.MissionSize > 0
                ? Math.Min(context.Impact, mission.MissionSize)
                : context.Impact;
            if (clampedImpact <= 0) return;

            float multiplier = (float)Math.Pow(1 + clampedImpact / DiversionThreatScale, 2);
            float apparentThreat = actualManpower * multiplier;
            // The real force is already counted through its landed squads, so only the phantom
            // remainder contributes to the feint.
            targetFaction.PerceivedThreatBonus += apparentThreat - actualManpower;

            if (order.LevelOfAggression >= Aggression.Normal)
            {
                Squad feintSquad = order.AssignedSquads.First();
                Region feintRegion = feintSquad.CurrentRegion;
                if (feintRegion != null
                    && feintRegion.RegionFactionMap.TryGetValue(
                        feintSquad.Faction.Id,
                        out RegionFaction feintFaction))
                {
                    feintFaction.ProvocationLevel += clampedImpact;
                }
            }
        }

        private void ResolveSquadConstruction(Order order, ConstructionMission mission)
        {
            BaseSkill engineering = _session.Rules.Skills.EngineeringFortification;
            float totalSkill = order.AssignedSquads
                .SelectMany(s => s.Members)
                .Sum(soldier => soldier.GetTotalSkillValue(engineering));
            ApplyConstruction(mission, totalSkill / EngineeringBuildDivisor);
        }

        internal static void ApplyConstruction(ConstructionMission mission, double amount)
        {
            double before = GetConstructionLevel(mission);
            switch (mission.ConstructionType)
            {
                case DefenseType.Entrenchment:
                    mission.RegionFaction.Entrenchment += amount;
                    break;
                case DefenseType.ListeningPost:
                    mission.RegionFaction.ListeningPost += amount;
                    break;
                case DefenseType.AntiAir:
                    mission.RegionFaction.AntiAir += amount;
                    break;
                case DefenseType.Organization:
                    mission.RegionFaction.Organization = Math.Min(
                        100,
                        mission.RegionFaction.Organization + (int)Math.Round(amount));
                    break;
            }
            double after = GetConstructionLevel(mission);
            GameLog.Trace(() =>
                $"Construction applied {DescribeRegionFaction(mission.RegionFaction)}: "
                + $"{mission.ConstructionType} {before:F2}->{after:F2} (requested +{amount:F2})");
        }

        internal static double GetConstructionLevel(ConstructionMission mission)
        {
            return mission.ConstructionType switch
            {
                DefenseType.Entrenchment => mission.RegionFaction.Entrenchment,
                DefenseType.ListeningPost => mission.RegionFaction.ListeningPost,
                DefenseType.AntiAir => mission.RegionFaction.AntiAir,
                DefenseType.Organization => mission.RegionFaction.Organization,
                _ => 0
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
