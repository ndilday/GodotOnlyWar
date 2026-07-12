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

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            long defenderBattleValue = StrategicCombatResolver.CalculateDefenderBattleValue(enemyFaction);
            if (defenderBattleValue <= 0)
            {
                context.NoViableTarget = true;
                context.AddLog($"Day {context.DaysElapsed}: No military target found in {enemyFaction.Region.Name}.");
                ExfiltrateIfNeeded(context);
                return;
            }

            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.Skills.Tactics;
            long attackerBattleValue = Math.Max(1, AbleBattleValue(context.MissionSquads));
            float difficulty = 10.0f
                               + (float)Math.Log10(Math.Max(defenderBattleValue, 1))
                               - (float)Math.Log10(attackerBattleValue);
            LeaderMissionTest missionTest = new(tactics, difficulty);

            context.DaysElapsed++;
            context.AddLog($"Day {context.DaysElapsed}: Force searches for an exposed target in {enemyFaction.Region.Name}.");
            float margin = missionTest.RunMissionCheck(context.MissionSquads);

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
            List<BattleSquad> opposingSquads = ForceGenerator.GenerateForce(request)
                .Select(squad => new BattleSquad(false, squad))
                .ToList();

            if (opposingSquads.Count == 0)
            {
                context.NoViableTarget = true;
                context.AddLog($"Day {context.DaysElapsed}: The raiders find no isolated force to engage.");
                ExfiltrateIfNeeded(context);
                return;
            }

            context.OpposingSquads = opposingSquads;
            GameLog.Debug(() =>
                $"Lightning raid {context.MissionSquads.FirstOrDefault()?.Squad.Faction?.Name ?? "Unknown"} -> "
                + $"{enemyFaction.Region.Planet.Name}/{enemyFaction.Region.Name}: "
                + $"attackerBV={attackerBattleValue}, defenderBV={defenderBattleValue}, "
                + $"tacticsDifficulty={difficulty:F2}, margin={margin:F2}, targetBV={targetBattleValue}, "
                + $"generatedOpposingBV={AbleBattleValue(opposingSquads)}");

            new MeetingEngagementMissionStep().ExecuteMissionStep(context, margin, null);
            ExfiltrateIfNeeded(context);
        }

        private static void ExfiltrateIfNeeded(MissionContext context)
        {
            if (!context.MissionSquads.Any(squad => squad.ShouldContinueMission())) return;
            if (context.Order.Mission.RegionFaction.Region == context.MissionSquads.First().Squad.CurrentRegion) return;

            new ExfiltrateMissionStep().ExecuteMissionStep(context, 0.0f, null);
        }

        private static long AbleBattleValue(IEnumerable<BattleSquad> squads)
        {
            return squads
                .SelectMany(squad => squad.AbleSoldiers)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);
        }
    }
}
