using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.StrategicCombat;

namespace OnlyWar.Helpers.Missions.Assault
{
    public class PrepareAssaultMissionStep : IMissionStep
    {
        // Tactical assaults must stay table-sized. Larger garrisons belong in the strategic
        // resolver; if a tactical order reaches this step after the defender mobilized, cap the
        // generated garrison to the same limits used when deciding tactical-vs-strategic combat.
        private const long MaxTacticalGarrisonBattleValue = StrategicCombatRules.MassCombatBattleValueFloor - 1;

        public string Description { get { return "Prepare Assault"; } }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // The attacker's preparation check remains the same
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.Skills.Tactics;
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, 10.0f);
            context.Log.Add($"Day {context.DaysElapsed}: Force prepares to assault {context.Order.Mission.RegionFaction.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.MissionSquads);

            // Assemble the defending force from actual units and garrisons
            context.OpposingSquads = AssembleDefendingForce(context.Order.Mission.RegionFaction, margin);

            if (context.OpposingSquads.Count == 0)
            {
                // No defenders, the assault is an uncontested success.
                // This could be a separate mission step in the future (e.g., "Secure Unopposed Region").
                context.Log.Add($"Day {context.DaysElapsed}: Assault on {context.Order.Mission.RegionFaction.Region.Name} is unopposed.");
                context.Impact += 5; // Give a significant positive impact for taking territory freely.
                // a more robust system would properly transfer ownership here
                return;
            }

            new MeetingEngagementMissionStep().ExecuteMissionStep(context, margin, null);
        }

        private List<BattleSquad> AssembleDefendingForce(RegionFaction defendingRegionFaction, float attackerMarginOfSuccess)
        {
            var defendingForce = new List<BattleSquad>();
            bool defenderIsPlayer = defendingRegionFaction.PlanetFaction.Faction.IsPlayerFaction;

            // 1. Get all landed squads in the region with defensive orders. A diversion force is
            // deliberately in the open, so it too is caught up in the fighting if its feint draws
            // a counterattack into the region it is standing in. A standing patrol is likewise a
            // screen posted to engage raiders — it joins the defence of the region it patrols.
            var defendingSquads = defendingRegionFaction.LandedSquads
                                                        .Where(s => s.CurrentOrders?.Mission.MissionType == MissionType.DefenseInDepth
                                                                 || s.CurrentOrders?.Mission.MissionType == MissionType.Diversion
                                                                 || s.CurrentOrders?.Mission.MissionType == MissionType.Patrol)
                                                        .ToList();

            if (defendingSquads.Any())
            {
                defendingForce.AddRange(defendingSquads.Select(s => new BattleSquad(defenderIsPlayer, s)));
            }

            // 2. Generate squads based on the garrison count
            if (defendingRegionFaction.Garrison > 0)
            {
                // Attacker's success in preparation reduces the effectiveness of the garrison mobilization
                float cdf = GaussianCalculator.ApproximateNormalCDF(attackerMarginOfSuccess);
                float multiplier = (float)Math.Pow(2, 1 - (2 * cdf));
                long effectiveGarrison = (long)(defendingRegionFaction.Garrison * multiplier);
                long targetBattleValue = Math.Min(effectiveGarrison * 10L, MaxTacticalGarrisonBattleValue);

                var request = new ForceGenerationRequest
                {
                    Faction = defendingRegionFaction.PlanetFaction.Faction,
                    TargetBattleValue = targetBattleValue,
                    Profile = ForceCompositionProfile.Garrison
                };
                var garrisonSquads = CapTacticalForce(ForceGenerator.GenerateForce(request));
                defendingForce.AddRange(garrisonSquads.Select(s => new BattleSquad(false, s))); // Garrisons are never player squads
            }

            return defendingForce;
        }

        private static List<Squad> CapTacticalForce(IEnumerable<Squad> squads)
        {
            List<Squad> capped = new();
            int actors = 0;
            foreach (Squad squad in squads)
            {
                if (capped.Count >= StrategicCombatRules.MaxGeneratedSquads) break;
                int squadActors = squad.Members.Count;
                if (actors + squadActors > StrategicCombatRules.MaxTacticalActors) break;

                capped.Add(squad);
                actors += squadActors;
            }
            return capped;
        }
    }
}
