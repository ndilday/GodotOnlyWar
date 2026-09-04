using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Helpers.Strategy;

namespace OnlyWar.Helpers.Missions.Assault
{
    public class PrepareAssaultMissionStep : IMissionStep
    {
        // Tactical assaults must stay table-sized. Larger defences belong in the strategic resolver; if a
        // tactical order reaches this step after the defender mobilized, cap the generated force to the
        // same limits used when deciding tactical-vs-strategic combat. This is what keeps a hive's
        // enormous reserve from producing an untenable tactical battle.
        private const long MaxTacticalDefenderBattleValue = StrategicCombatRules.MassCombatBattleValueFloor - 1;

        // Base difficulty of the defenders' preparation check. Mirrors the attacker's own 10.0f so
        // neither side is structurally favoured: an evenly-matched pair of commanders produces a net
        // margin near zero, which leaves garrison mobilisation where it would have been before this
        // contest existed.
        private const float DefensivePreparationDifficulty = 10.0f;

        // Difficulty reduction per shared level of Entrenchment. Deliberately the same magnitude as
        // MissionStealthDifficulty.SurveillanceWeight, so a level of works is worth about as much to a
        // defence as a point of regional intel is to spotting an intruder.
        private const float EntrenchmentPreparationBonus = 0.5f;

        // Baseline difficulty of a patrol being astride the approach an attack actually used. Set so that
        // a mid-sized attacker (magnitude ~3, i.e. around a thousand battle value) is roughly even money
        // for an undiverted patrol at Normal aggression, a full advance is near-certain to be seen, and a
        // small raid slipping through is a real possibility.
        private const float PatrolDetectionBaseDifficulty = 13.0f;

        // How much each point of attention drawn elsewhere costs the patrol's chance of being in position.
        // Deliberately steep: this is the payoff for a successful feint, and the design intent is that
        // pulling a screen aside meaningfully changes what happens when the real blow lands.
        private const float PatrolDetectionAttentionPenalty = 2.0f;

        public string Description { get { return "Prepare Assault"; } }

        // An assault now spends days. Before this it consumed none at all: prep check, gather the
        // defenders, one battle, done - the entire operation resolved on day 0, which meant there was
        // no window during which anything could interfere with it and the day model bought the biggest
        // mission in the game precisely nothing.
        public bool ConsumesDay => true;

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;

            // An advance holds what it takes (MissionReturnPolicy.Hold), so it gets the full week of
            // attempts rather than surrendering the last day to a trip home.
            if (context.OperatingDaysSpent)
            {
                context.AddLog(
                    $"Day {context.DaysElapsed}: The assault has spent its week without taking "
                    + $"{context.Order.Mission.RegionFaction.Region.Name}.");
                return MissionStepResult.Complete;
            }

            // The across-days half of the aggression threshold. MeetingEngagementMissionStep already
            // declines to resume when a single battle leaves the force spent; this catches the force
            // that survived each individual fight but has been ground down over several of them.
            if (context.MissionLossesExceedAggressionThreshold)
            {
                context.AddLog(
                    $"Day {context.DaysElapsed}: Assault broken off - losses beyond "
                    + $"{context.Order.LevelOfAggression} tolerance.");
                GameLog.Debug(() =>
                    $"Assault broken off {MissionTurnProcessor.DescribeRegionFaction(context.Order.Mission.RegionFaction)}: "
                    + $"aggression={context.Order.LevelOfAggression}, day={context.DaysElapsed}, "
                    + $"bv={context.CurrentMissionBattleValue}/{context.StartingMissionBattleValue}");
                return MissionStepResult.Complete;
            }

            context.DaysElapsed++;
            // The attacker's preparation check remains the same
            BaseSkill tactics = execution.Rules.Tactics;
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, 10.0f);
            string attacker = context.MissionSquads
                .Select(squad => squad?.Faction?.Name)
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
                tactics,
                context.DefenderBattleValueDestroyed,
                // How much force is arriving, which is what decides whether a patrol could plausibly
                // have missed it.
                context.CurrentMissionBattleValue);

            if (context.OpposingSquads.Count == 0)
            {
                RegionFaction target = context.Order.Mission.RegionFaction;
                long remainingDisorganized = Math.Max(
                    0L,
                    target.DisorganizedMilitaryStrength
                    - context.DisorganizedDefenderBattleValueDestroyed);
                long destructionCapacity = SaturatingScale(
                    context.CurrentMissionBattleValue,
                    StrategicCombatRules.UndefendedAssaultDestructionMultiplier);
                long destroyed = Math.Min(remainingDisorganized, destructionCapacity);
                context.RecordDisorganizedDefenderLosses(destroyed);
                context.AddLog(
                    $"Day {context.DaysElapsed}: {attacker}'s assault on {defender} forces in "
                    + $"{region} is unopposed; {destroyed:N0} disorganized BV destroyed.");
                context.Impact += 5;

                long stillRemaining = remainingDisorganized - destroyed;
                return stillRemaining > 0 && !context.OperatingDaysSpent
                    ? MissionStepResult.Continue(new PrepareAssaultMissionStep())
                    : MissionStepResult.Complete;
            }

            // Resume this step after the engagement, so an assault that is still willing and able comes
            // back tomorrow for another attempt. MeetingEngagementMissionStep only resumes when the
            // force survived and did not withdraw under fire, so a decisive defeat ends the assault
            // here; the guards at the top of this method end it when the week or the force's tolerance
            // runs out. An Aggressive order has no tolerance and so genuinely fights until the region
            // falls or the force is destroyed, which is what the setting should mean.
            return MissionStepResult.Continue(
                new MeetingEngagementMissionStep(), margin, new PrepareAssaultMissionStep());
        }

        private static long SaturatingScale(long battleValue, double multiplier)
        {
            if (battleValue <= 0 || multiplier <= 0) return 0;
            double scaled = battleValue * multiplier;
            return scaled >= long.MaxValue ? long.MaxValue : Math.Max(1L, (long)scaled);
        }

        internal List<BattleSquad> AssembleDefendingForce(
            RegionFaction defendingRegionFaction,
            float attackerMarginOfSuccess,
            IRNG random,
            IEntityIdAllocator entityIds = null,
            BaseSkill defenderTactics = null,
            long defenderBattleValueAlreadyDestroyed = 0,
            long attackerBattleValue = 0)
        {
            var defendingForce = new List<BattleSquad>();

            // A defence order protects the geographic region, not merely one faction's enclave
            // within it, so every allied presence in the assaulted region is pooled into the
            // defence. Until diplomacy exists that means the Chapter and the world's own defence
            // forces and nobody else (FactionRelationshipService.AreAllied) - two xenos factions
            // sharing a region do NOT reinforce each other, they each defend alone.
            List<RegionFaction> alliedDefenders = defendingRegionFaction.Region.RegionFactionMap.Values
                .Where(rf => FactionRelationshipService.AreAllied(
                    rf.PlanetFaction.Faction,
                    defendingRegionFaction.PlanetFaction.Faction,
                    defendingRegionFaction.Region.Planet))
                .ToList();

            // 1. Get all landed squads in the region with defensive orders. A diversion force is
            // deliberately in the open, so it too is caught up in the fighting if its feint draws
            // a counterattack into the region it is standing in. A standing patrol is likewise a
            // screen posted to engage raiders — it joins the defence of the region it patrols.
            var defendingSquads = GetRegionalDefensiveSquads(defendingRegionFaction);

            List<BattleSquad> landedDefenders = defendingSquads
                .Select(s => BattleSquadFactory.Create(
                    s.Faction?.IsPlayerFaction == true,
                    s,
                    s.Faction?.IsPlayerFaction == true
                        ? GameDataSingleton.Instance?.Sector?.PlayerForce?.Army?.ChapterOperationalDoctrine
                        : null))
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .ToList();

            // 1a. A patrol fights only if it saw this coming. Everything else here - a Defense order, an
            // exposed diversion force, a show of force - is standing on the ground by intent and is
            // caught up in the fighting regardless.
            landedDefenders = landedDefenders
                .Where(bs => (bs.CampaignCharacter?.CurrentOrder ?? bs.Squad?.CurrentOrders)
                        ?.Mission?.MissionType != MissionType.Patrol
                    || PatrolDetectedAttack(bs, attackerBattleValue, defenderTactics, random))
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

            // 2. Materialise each allied defender's RESERVE - the battle value its controller held back
            // to defend this ground (FactionThreatAssessment.CalculateRequiredDefensiveBattleValue).
            //
            // This used to read raw RegionFaction.Garrison, which was wrong for almost every defender in
            // the game. Garrison is Imperial-specific: MilitaryStrength resolves to Population for a
            // PopulationIsMilitary horde, and FactionRevealService ZEROES Garrison when a cult or revolt
            // reveals. So the old `Where(rf => rf.Garrison > 0)` filter never fired for a revealed cult,
            // revolt or Tyranid region, and an assault on one faced no abstract defence whatsoever -
            // frequently landing in the unopposed branch below and taking the ground for free. The
            // strategic resolver and LightningRaidMissionStep already priced defenders off
            // MilitaryStrength, so this brings the assault path in line with its own siblings rather
            // than inventing a new defence.
            //
            // The reserve read here is the ASSIGNMENT the region's controller made during planning
            // (RegionFaction.AssignedDefensiveBattleValue), not the requirement it computed. This step
            // used to call CalculateRequiredDefensiveBattleValue itself, which returns an unbounded
            // want derived from adjacent enemy strength and says nothing about whether the troops to
            // meet it exist. Materialising soldiers to fill that want meant a region holding ~200 BV of
            // organized strength fielded a 1499 BV defence - roughly three times its entire army,
            // generated out of nothing, and bounded only by MaxTacticalDefenderBattleValue and the
            // actor cap rather than by anything the defender actually had.
            //
            // The reserve and the region's landed patrol squads (added above) are intended to be
            // disjoint: patrol screens are drawn from SPARE troops, i.e. what is left after the
            // reserve. Note that this is an intent, not an enforced invariant - patrols and recon debit
            // only the planner's transient SpareTroops and never OrganizedMilitaryStrength, so a
            // screening squad is counted both in the pool the reserve is clamped against and in
            // LandedSquads. Clamping the reserve to organized strength bounds the overlap to at most
            // the screen's own battle value instead of leaving it unbounded, but it does not eliminate
            // it. Nothing is debited at generation time.
            //
            // The reserve is drawn down by whatever this mission has already destroyed. Without that, a
            // multi-day assault would raise a fresh full-strength defence every morning - the region's
            // strength is not reduced until MissionAftermathProcessor runs at the end of the turn - so
            // the attacker could never actually win, only run out of days or of its own casualty
            // tolerance while re-fighting an identical battle. Spreading the deduction across allied
            // defenders in proportion to their share keeps a multi-faction defence consistent with the
            // single-faction case.
            Dictionary<RegionFaction, long> reserves = alliedDefenders.ToDictionary(
                rf => rf,
                ResolveDefensiveReserve);
            long totalReserve = reserves.Values.Sum();
            long remainingToDeduct = Math.Max(0L, defenderBattleValueAlreadyDestroyed);
            foreach (RegionFaction alliedDefender in alliedDefenders)
            {
                long reserve = reserves[alliedDefender];
                if (reserve <= 0) continue;

                long share = totalReserve <= 0
                    ? 0
                    : (long)((double)remainingToDeduct * reserve / totalReserve);
                long survivingReserve = Math.Max(0L, reserve - share);
                if (survivingReserve <= 0) continue;

                // Attacker's success in preparation reduces the effectiveness of the defence's
                // mobilization - net of whatever the defenders' own preparation clawed back.
                float cdf = GaussianCalculator.ApproximateNormalCDF(effectiveAttackerMargin);
                float multiplier = (float)Math.Pow(2, 1 - (2 * cdf));
                long effectiveReserve = (long)(survivingReserve * multiplier);
                // The reserve already lives in strategic battle-value points; the old x10 conversion
                // massively over-mobilised defenders after SoldierTemplate.BattleValue was
                // recalculated onto real per-template values.
                long targetBattleValue = effectiveReserve <= 0
                    ? 0
                    : Math.Min(
                        Math.Min(
                            Math.Max(effectiveReserve, alliedDefender.PlanetFaction.Faction.MinimumForceRequest),
                            MaxTacticalDefenderBattleValue),
                        // Never mobilise more than actually survives: MinimumForceRequest would otherwise
                        // resurrect a defence ground down below one squad's worth, so the last fragment
                        // of it could never be finished off.
                        survivingReserve);

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
        /// The battle value a defender actually holds this ground with: the assignment its controller
        /// committed during planning, falling back to deriving that same clamp when no planning pass
        /// has ever run for this region.
        /// </summary>
        /// <remarks>
        /// The fallback is not cosmetic. A region faction has no assignment when it was created after
        /// the last planning pass, when its faction plans only on turns it is threatened, or when the
        /// save predates the field. Treating "never planned" as an assignment of zero would field no
        /// abstract defence at all and hand the attacker the ground for free - the same class of bug
        /// that reading raw Garrison used to cause for revealed cults and hordes. So an unplanned
        /// region derives the clamp here instead: the requirement it would have computed, bounded by
        /// the organized troops it actually has. That is the same expression the planner writes, which
        /// is why an unplanned region and a freshly planned one defend identically.
        /// </remarks>
        private static long ResolveDefensiveReserve(RegionFaction defender)
        {
            if (defender.AssignedDefensiveBattleValue.HasValue)
            {
                return Math.Max(0L, defender.AssignedDefensiveBattleValue.Value);
            }

            return Math.Min(
                defender.GetDeployedStrength(),
                FactionThreatAssessment.CalculateRequiredDefensiveBattleValue(defender));
        }

        /// <summary>
        /// Whether a patrol was looking the right way when this attack arrived, and therefore whether it
        /// joins the defence at all.
        /// </summary>
        /// <remarks>
        /// This is the distinction between Patrol and Defense that the whole redesign turns on. A Defense
        /// order holds prepared ground and always fights. A patrol is dispersed and sweeping, so whether
        /// it is in position is a question with an answer, and the answer is what the player buys when
        /// they screen a region — or takes away when they feint at one.
        ///
        /// Two terms make it work. A larger formation crossing into the region is harder to miss, so
        /// difficulty falls with the attacker's magnitude: a full advance is near-impossible to overlook,
        /// a lightning raid is a genuine gamble. And attention a diversion drew elsewhere pushes it back
        /// up — a diverted patrol has not failed to detect, it detected the wrong thing, which is
        /// precisely what the feint was for. That makes the mechanic cut both ways the moment the AI
        /// learns to feint.
        /// </remarks>
        internal static bool PatrolDetectedAttack(
            BattleSquad patrol,
            long attackerBattleValue,
            BaseSkill defenderTactics,
            IRNG random)
        {
            // Callers without the rules' Tactics skill (older test call sites, and any path assembling a
            // defence outside a mission execution) keep the previous unconditional behaviour rather than
            // silently dropping the patrol from the defence.
            if (defenderTactics == null || random == null) return true;

            RegionFaction patrolled = ResolvePatrolledFaction(patrol);
            float committed = patrolled?.CommittedAttention ?? 0f;
            float difficulty = PatrolDetectionBaseDifficulty
                - MissionStealthDifficulty.Magnitude(attackerBattleValue)
                + committed * PatrolDetectionAttentionPenalty
                // A bold patrol ranges wider and is likelier to be astride the approach: aggression's
                // EFFECT axis, matching PatrolSweepMissionStep.
                + MissionAggressionModifiers.EffectDifficulty(
                    (patrol.CampaignCharacter?.CurrentOrder ?? patrol.Squad?.CurrentOrders)
                        ?.LevelOfAggression ?? Aggression.Normal);

            float margin = new LeaderMissionTest(defenderTactics, difficulty)
                .RunMissionCheck(new List<BattleSquad> { patrol }, random);
            bool detected = margin > 0f;
                GameLog.Debug(() =>
                    $"Patrol detection {patrol.Name}: difficulty={difficulty:F2} "
                + $"(attackerBV={attackerBattleValue}, committedAttention={committed:F2}), "
                + $"margin={margin:F2} -> {(detected ? "IN POSITION" : "looking the wrong way")}");
            return detected;
        }

        // The patrol's own presence in the region it patrols, which is where its committed attention
        // lives. Falls back to null rather than guessing when the squad has no resolvable presence.
        private static RegionFaction ResolvePatrolledFaction(BattleSquad patrol)
        {
            Order order = patrol.CampaignCharacter?.CurrentOrder ?? patrol.Squad?.CurrentOrders;
            RegionFaction anchored = order?.Mission?.RegionFaction;
            if (anchored != null) return anchored;
            Region region = patrol.CampaignCharacter?.EffectiveRegion ?? patrol.Squad?.CurrentRegion;
            int? factionId = patrol.Faction?.Id;
            if (region == null || factionId == null) return null;
            return region.RegionFactionMap.TryGetValue(factionId.Value, out RegionFaction rf) ? rf : null;
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
                .Where(bs => (bs.CampaignCharacter?.CurrentOrder ?? bs.Squad?.CurrentOrders)
                    ?.Mission?.MissionType == MissionType.DefenseInDepth)
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
            List<Squad> landedDefenders = defendingRegionFaction.Region.RegionFactionMap.Values
                .Where(rf => FactionRelationshipService.AreAllied(
                    rf.PlanetFaction.Faction,
                    defender,
                    defendingRegionFaction.Region.Planet))
                .SelectMany(rf => rf.LandedSquads)
                .Where(s => s.CurrentOrders?.Mission.MissionType == MissionType.DefenseInDepth
                         || s.CurrentOrders?.Mission.MissionType == MissionType.Diversion
                         || s.CurrentOrders?.Mission.MissionType == MissionType.Patrol
                         // A show of force that stood by while the region it garrisons was overrun
                         // would be no show of force at all - it defends like any standing screen.
                         || s.CurrentOrders?.Mission.MissionType == MissionType.ShowOfForce)
                .ToList();

            // A strategic invasion force's command squad is strategic state, not a normal LandedSquad. It still
            // has to be the first-class defender when its region is assaulted; otherwise the
            // persistent commander can only die in strategic combat or assassination and never gets
            // to exercise the defensive-battle priority described by the invasion-force rules.
            if (GameDataSingleton.Instance?.Sector?.StrategicInvasionForces is IReadOnlyList<StrategicInvasionForce> forces)
            {
                landedDefenders.AddRange(forces
                    .Where(force => force.IsActive
                        && force.Faction == defender
                        && force.CurrentRegion == defendingRegionFaction.Region
                        && force.CommandSquad != null)
                    .Select(force => force.CommandSquad));
            }

            return landedDefenders.Distinct().ToList();
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
