using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Missions.Raid
{
    public class LightningRaidMissionStep : IMissionStep
    {
        public string Description => "Lightning Raid";

        public bool ConsumesDay => true;

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            long defenderBattleValue = StrategicCombatResolver.CalculateDefenderBattleValue(enemyFaction);
            if (defenderBattleValue <= 0)
            {
                context.NoViableTarget = true;
                context.AddLog($"Day {context.DaysElapsed}: No military target found in {enemyFaction.Region.Name}.");
                return MissionStepResult.Continue(new WithdrawIfAbleMissionStep());
            }

            BaseSkill tactics = execution.Rules.Tactics;
            long attackerBattleValue = Math.Max(1, AbleBattleValue(context.MissionSquads));
            float difficulty = 10.0f
                               + (float)Math.Log10(Math.Max(defenderBattleValue, 1))
                               - (float)Math.Log10(attackerBattleValue);
            LeaderMissionTest missionTest = new(tactics, difficulty);

            context.DaysElapsed++;
            context.AddLog($"Day {context.DaysElapsed}: Force searches for an exposed target in {enemyFaction.Region.Name}.");
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);

            double opportunity = Math.Clamp(0.35 + GaussianCalculator.ApproximateNormalCDF(margin) * 0.9, 0.25, 1.25);
            long targetBattleValue = Math.Min(
                defenderBattleValue,
                Math.Max(1, (long)Math.Round(attackerBattleValue * opportunity)));

            var request = new ForceGenerationRequest
            {
                Faction = enemyFaction.PlanetFaction.Faction,
                TargetBattleValue = Math.Min(targetBattleValue, StrategicCombatRules.MassCombatBattleValueFloor - 1),
                Profile = ForceCompositionProfile.Garrison
            };
            List<BattleSquad> opposingSquads = ForceGenerator.GenerateForce(
                    request,
                    execution.Random,
                    execution.EntityIds)
                .Select(squad => new BattleSquad(false, squad))
                .ToList();

            if (opposingSquads.Count == 0)
            {
                context.NoViableTarget = true;
                context.AddLog($"Day {context.DaysElapsed}: The raiders find no isolated force to engage.");
                return MissionStepResult.Continue(new WithdrawIfAbleMissionStep());
            }

            context.OpposingSquads = opposingSquads;
            GameLog.Debug(() =>
                $"Lightning raid {context.MissionSquads.FirstOrDefault()?.Squad.Faction?.Name ?? "Unknown"} -> "
                + $"{enemyFaction.Region.Planet.Name}/{enemyFaction.Region.Name}: "
                + $"attackerBV={attackerBattleValue}, defenderBV={defenderBattleValue}, "
                + $"tacticsDifficulty={difficulty:F2}, margin={margin:F2}, targetBV={targetBattleValue}, "
                + $"generatedOpposingBV={AbleBattleValue(opposingSquads)}");

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

        private static long AbleBattleValue(IEnumerable<BattleSquad> squads)
        {
            return squads
                .SelectMany(squad => squad.AbleSoldiers)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);
        }
    }
}
