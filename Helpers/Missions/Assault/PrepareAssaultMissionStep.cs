using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Turns;
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

        // Base difficulty of the defenders' preparation check. Mirrors the attacker's own 10.0f so
        // neither side is structurally favoured: an evenly-matched pair of commanders produces a net
        // margin near zero, which leaves garrison mobilisation where it would have been before this
        // contest existed.
        private const float DefensivePreparationDifficulty = 10.0f;

        // Difficulty reduction per shared level of Entrenchment. Deliberately the same magnitude as
        // MissionStealthDifficulty.SurveillanceWeight, so a level of works is worth about as much to a
        // defence as a point of regional intel is to spotting an intruder.
        private const float EntrenchmentPreparationBonus = 0.5f;

        public string Description { get { return "Prepare Assault"; } }

        public void ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep returnStep)
        {
            MissionContext context = execution.State;
            // The attacker's preparation check remains the same
            BaseSkill tactics = execution.Rules.Tactics;
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, 10.0f);
            string attacker = context.MissionSquads
                .Select(squad => squad?.Squad?.Faction?.Name)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Unknown force";
            string defender = context.Order.Mission.RegionFaction.PlanetFaction.Faction.Name;
            string region = context.Order.Mission.RegionFaction.Region.Name;
            context.AddLog($"Day {context.DaysElapsed}: {attacker} prepares to assault {defender} forces in {region}.");
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);

            // Assemble the defending force from actual units and garrisons
            context.OpposingSquads = AssembleDefendingForce(
                context.Order.Mission.RegionFaction,
                margin,
                execution.Random,
                execution.EntityIds,
                tactics);

            if (context.OpposingSquads.Count == 0)
            {
                // No defenders, the assault is an uncontested success.
                // This could be a separate mission step in the future (e.g., "Secure Unopposed Region").
                context.AddLog($"Day {context.DaysElapsed}: {attacker}'s assault on {defender} forces in {region} is unopposed.");
                context.Impact += 5; // Give a significant positive impact for taking territory freely.
                // a more robust system would properly transfer ownership here
                return;
            }

            new MeetingEngagementMissionStep().ExecuteMissionStep(execution, margin, null);
        }

        internal List<BattleSquad> AssembleDefendingForce(
            RegionFaction defendingRegionFaction,
            float attackerMarginOfSuccess,
            IRNG random,
            IEntityIdAllocator entityIds = null,
            BaseSkill defenderTactics = null)
        {
            var defendingForce = new List<BattleSquad>();

            // A defence order protects the geographic region, not merely one faction's enclave
            // within it, so every allied presence in the assaulted region is pooled into the
            // defence. Until diplomacy exists that means the Chapter and the world's own defence
            // forces and nobody else (FactionDispositionService.AreAllied) - two xenos factions
            // sharing a region do NOT reinforce each other, they each defend alone.
            List<RegionFaction> alliedDefenders = defendingRegionFaction.Region.RegionFactionMap.Values
                .Where(rf => FactionDispositionService.AreAllied(rf.PlanetFaction.Faction, defendingRegionFaction.PlanetFaction.Faction))
                .ToList();

            // 1. Get all landed squads in the region with defensive orders. A diversion force is
            // deliberately in the open, so it too is caught up in the fighting if its feint draws
            // a counterattack into the region it is standing in. A standing patrol is likewise a
            // screen posted to engage raiders — it joins the defence of the region it patrols.
            var defendingSquads = GetRegionalDefensiveSquads(defendingRegionFaction);

            List<BattleSquad> landedDefenders = defendingSquads
                .Select(s => new BattleSquad(s.Faction?.IsPlayerFaction == true, s))
                .ToList();
            defendingForce.AddRange(landedDefenders);

            // 1b. A Defense order means the ground was PREPARED, and prepared ground contests the
            // attacker's own preparation instead of merely absorbing it.
            float effectiveAttackerMargin = ContestPreparation(
                defendingRegionFaction,
                landedDefenders,
                attackerMarginOfSuccess,
                defenderTactics,
                random);

            // 2. Generate squads for each allied faction's abstract garrison.
            foreach (RegionFaction alliedDefender in alliedDefenders.Where(rf => rf.Garrison > 0))
            {
                // Attacker's success in preparation reduces the effectiveness of the garrison
                // mobilization - net of whatever the defenders' own preparation clawed back.
                float cdf = GaussianCalculator.ApproximateNormalCDF(effectiveAttackerMargin);
                float multiplier = (float)Math.Pow(2, 1 - (2 * cdf));
                long effectiveGarrison = (long)(alliedDefender.Garrison * multiplier);
                // Garrison already lives in strategic battle-value points; the old x10 conversion
                // massively over-mobilised defenders after SoldierTemplate.BattleValue was
                // recalculated onto real per-template values.
                long targetBattleValue = effectiveGarrison <= 0
                    ? 0
                    : Math.Min(
                        Math.Max(effectiveGarrison, alliedDefender.PlanetFaction.Faction.MinimumForceRequest),
                        MaxTacticalGarrisonBattleValue);

                var request = new ForceGenerationRequest
                {
                    Faction = alliedDefender.PlanetFaction.Faction,
                    TargetBattleValue = targetBattleValue,
                    Profile = ForceCompositionProfile.Garrison
                };
                var garrisonSquads = CapTacticalForce(
                    ForceGenerator.GenerateForce(request, random, entityIds));
                defendingForce.AddRange(garrisonSquads.Select(s => new BattleSquad(false, s))); // Garrisons are never player squads
            }

            return defendingForce;
        }

        /// <summary>
        /// The attacker's preparation margin, net of the defenders' own. This is what finally makes a
        /// Defense order worth issuing.
        /// </summary>
        /// <remarks>
        /// Before this, Defense and Patrol were mechanically identical - two adjacent `continue`
        /// statements in MissionTurnProcessor, both pulled into the region's defence by
        /// GetRegionalDefensiveSquads, neither doing anything else. Patrol additionally granted intel
        /// and search effort, so Defense was strictly dominated and the "defender advantage applies"
        /// of PRD §4.13 was never implemented at all.
        ///
        /// The advantage lands here rather than inside the battle because
        /// <see cref="BattleSquad.CoverModifier"/> is declared but never read by the battle engine, so
        /// there is currently no in-battle channel to attach prepared positions to. Routing it through
        /// garrison mobilisation keeps the change out of the tactical resolver entirely, which is also
        /// what keeps seeded battle baselines intact.
        ///
        /// Only squads actually holding a Defense order contest. Everything else
        /// GetRegionalDefensiveSquads returns - a patrol screen, an exposed diversion force, a show of
        /// force - fights when the region is attacked but did not prepare the ground, so it is present
        /// without shaping the engagement. That split is the whole point: detection and presence are
        /// one thing, fighting from prepared positions is another.
        /// </remarks>
        internal static float ContestPreparation(
            RegionFaction defendingRegionFaction,
            List<BattleSquad> landedDefenders,
            float attackerMarginOfSuccess,
            BaseSkill defenderTactics,
            IRNG random)
        {
            // Callers that do not supply the rules' Tactics skill (older test call sites, and any
            // path that assembles a defence outside a mission execution) keep the previous
            // uncontested behaviour rather than silently skipping the roll's RNG draw.
            if (defenderTactics == null || random == null) return attackerMarginOfSuccess;

            List<BattleSquad> prepared = landedDefenders
                .Where(bs => bs.Squad?.CurrentOrders?.Mission.MissionType == MissionType.DefenseInDepth)
                .ToList();
            if (prepared.Count == 0) return attackerMarginOfSuccess;

            // Entrenchment is the physical expression of a prepared defence, so it lowers the
            // difficulty of the defenders' check. Shared works pool across public allies exactly as
            // they do everywhere else (RegionDefenses.GetShared).
            double entrenchment =
                RegionDefenses.GetShared(defendingRegionFaction, DefenseType.Entrenchment);
            float difficulty = DefensivePreparationDifficulty
                - (float)(entrenchment * EntrenchmentPreparationBonus);

            // LeaderMissionTest also routes field experience to player soldiers, so a player Defense
            // order now earns Tactics XP - previously it earned nothing at all, because the order ran
            // no checks whatsoever.
            float defenderMargin = new LeaderMissionTest(defenderTactics, difficulty)
                .RunMissionCheck(prepared, random);
            float net = attackerMarginOfSuccess - defenderMargin;
            GameLog.Debug(() =>
                $"Defense preparation {MissionTurnProcessor.DescribeRegionFaction(defendingRegionFaction)}: "
                + $"squads={prepared.Count}, entrenchment={entrenchment:F2}, difficulty={difficulty:F2}, "
                + $"attackerMargin={attackerMarginOfSuccess:F2}, defenderMargin={defenderMargin:F2} "
                + $"-> net={net:F2}");
            return net;
        }

        internal static List<Squad> GetRegionalDefensiveSquads(RegionFaction defendingRegionFaction)
        {
            Faction defender = defendingRegionFaction.PlanetFaction.Faction;
            return defendingRegionFaction.Region.RegionFactionMap.Values
                .Where(rf => FactionDispositionService.AreAllied(rf.PlanetFaction.Faction, defender))
                .SelectMany(rf => rf.LandedSquads)
                .Where(s => s.CurrentOrders?.Mission.MissionType == MissionType.DefenseInDepth
                         || s.CurrentOrders?.Mission.MissionType == MissionType.Diversion
                         || s.CurrentOrders?.Mission.MissionType == MissionType.Patrol
                         // A show of force that stood by while the region it garrisons was overrun
                         // would be no show of force at all - it defends like any standing screen.
                         || s.CurrentOrders?.Mission.MissionType == MissionType.ShowOfForce)
                .ToList();
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
