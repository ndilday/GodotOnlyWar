using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Runtime.Factories;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Domain.Extensions;
using OnlyWar.Operations.Missions.Recon;
using OnlyWar.Operations.StrategicCombat;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Operations.Missions.Raid
{
    public class LightningRaidMissionStep : IMissionStep
    {
        public string Description => "Lightning Raid";

        public bool ConsumesDay => true;

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            long defenderStrategicValue = StrategicCombatResolver.CalculateDefenderStrategicValue(
                enemyFaction, execution.Campaign.PhysicalForces);
            if (defenderStrategicValue <= 0)
            {
                context.NoViableTarget = true;
                context.AddLog($"Day {context.DaysElapsed}: No military target found in {enemyFaction.Region.Name}.");
                return MissionStepResult.Continue(new WithdrawIfAbleMissionStep());
            }

            BaseSkill tactics = execution.Rules.Tactics;
            long attackerStrategicValue = Math.Max(1, AbleStrategicValue(context.MissionSquads));
            float difficulty = 10.0f
                               + (float)Math.Log10(Math.Max(defenderStrategicValue, 1))
                               - (float)Math.Log10(attackerStrategicValue);
            LeaderMissionTest missionTest = new(tactics, difficulty);

            context.DaysElapsed++;
            context.AddLog($"Day {context.DaysElapsed}: Force searches for an exposed target in {enemyFaction.Region.Name}.");
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);

            double opportunity = Math.Clamp(0.35 + GaussianCalculator.ApproximateNormalCDF(margin) * 0.9, 0.25, 1.25);
            long targetStrategicValue = Math.Min(
                defenderStrategicValue,
                Math.Max(1, (long)Math.Round(attackerStrategicValue * opportunity)));

            var request = new ForceGenerationRequest
            {
                Faction = enemyFaction.PlanetFaction.Faction,
                TargetStrategicValue = Math.Min(targetStrategicValue, StrategicCombatRules.MassCombatStrategicValueFloor - 1),
                Profile = ForceCompositionProfile.Garrison
            };
            List<OperationalMissionElement> opposingSquads = ForceGenerator.GenerateForce(
                    request,
                    execution.Random,
                    execution.EntityIds)
                .Select(squad => execution.EngagementElements.CreateSquad(false, squad))
                .ToList();

            if (opposingSquads.Count == 0)
            {
                context.NoViableTarget = true;
                context.AddLog($"Day {context.DaysElapsed}: The raiders find no isolated force to engage.");
                return MissionStepResult.Continue(new WithdrawIfAbleMissionStep());
            }

            context.OpposingSquads = opposingSquads;
            GameLog.Debug(() =>
                $"Lightning raid {context.MissionSquads.FirstOrDefault()?.Faction?.Name ?? "Unknown"} -> "
                + $"{enemyFaction.Region.Planet.Name}/{enemyFaction.Region.Name}: "
                + $"attackerBV={attackerStrategicValue}, defenderBV={defenderStrategicValue}, "
                + $"tacticsDifficulty={difficulty:F2}, margin={margin:F2}, targetBV={targetStrategicValue}, "
                + $"generatedOpposingBV={AbleStrategicValue(opposingSquads)}");

            // A raid withdraws whatever the engagement's outcome, so the withdrawal is a mandatory
            // follow-up rather than the engagement's resume target - MeetingEngagementMissionStep
            // declines to resume when the force is spent, which would strand a raid that withdrew
            // under fire but was still able to walk home. WithdrawIfAbleMissionStep is the former
            // ExfiltrateIfNeeded helper, now shared with PerformAssassinationMissionStep, which
            // applied byte-identical logic.
            return MissionStepResult.Continue(
                new MeetingEngagementMissionStep(),
                margin,
                then: new WithdrawIfAbleMissionStep());
        }

        private static long AbleStrategicValue(IEnumerable<OperationalMissionElement> squads)
        {
            return squads
                .SelectMany(squad => squad.AbleMembers)
                .Sum(soldier => (long)soldier.StrategicValue);
        }
    }
}
