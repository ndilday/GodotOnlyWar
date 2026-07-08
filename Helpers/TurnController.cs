using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    class TurnController
    {
        public List<MissionContext> MissionContexts { get; private set; }
        public List<Mission> SpecialMissions { get; private set; }
        public List<StrategicCombatResult> StrategicCombatResults { get; private set; }
        // Set by ProcessScenario when the opening scenario resolves this turn (win or lapse), so
        // MainGameScene can surface a notification. Null on a turn with no resolution
        // (Design/OpeningScenario.md §6.2). Cleared at the start of each ProcessTurn.
        public string ScenarioNotification { get; private set; }

        private readonly FactionStrategyController _npcStrategyController;
        private readonly ISoldierTrainingService _trainingService;
        private const float WeeklyTrainingPoints = 0.2f;
        // Maximum (uncrowded) weekly population growth rates. Realized growth is scaled down
        // by the carrying-capacity crowding factor (see ApplyCarryingCapacity). These are set
        // so that a world at a typical fill (~50-75% of capacity) still roughly doubles its
        // population per century, matching the canon "doubles every ~100 Terran years."
        private const float LogisticGrowthRate = 0.0006f;
        private const float BaselineGrowthRate = 0.0004f;
        // fraction of a standing garrison that retires each week (PRD Strategic Layer Phase 2)
        private const float GarrisonAttritionRate = 0.001f;
        private const float GarrisonDraftRate = 0.025f;
        private const float EmergencyGarrisonDraftRate = 0.05f;
        private const float ActiveAssaultGarrisonDraftRate = 0.15f;
        // divisor converting a fortifying squad's summed engineering skill into a per-turn
        // defensive increment; tuned so a full, trained squad raises a defense by ~1-2/turn
        private const float EngineeringBuildDivisor = 100f;
        // Superlinear scale for how convincing a diversion feint is. The apparent size of the
        // feinting force grows with the square of (1 + impact / this), so a high-margin
        // demonstration can make a force look several times larger than it is while a weak one
        // barely exceeds its real strength. Larger values make feints harder to sell.
        private const float DiversionThreatScale = 4.0f;
        // Flat Requisition granted to the chapter each time a governor request is fulfilled
        // (PRD 4.23 / Supply & Requisition Phase 1 faucet). No pledges or delivery scheduling
        // yet; this is the minimal earn->spend loop backing the Apothecary procedure sink.
        private const int RequisitionPerRequestFulfilled = 100;

        // --- Tyranid biomass model (PRD §4.24) ---
        // A Consumption faction (Tyranids) has no birthrate; it grows only by eating. Its organized
        // troops split their effort between Predate (killing co-located headcount) and Consume
        // (stripping the land's carrying capacity), allocated to equalize marginal yield. Predation's
        // per-troop yield scales with the log of remaining prey — easy to find when abundant, sharply
        // diminishing as they thin out; consumption's scales with the square root of remaining biomass
        // — fast to strip a rich region, mildly diminishing. Both are normalized to the same yield at
        // the reference availability, so the two curves diverge by shape rather than by an arbitrary
        // scale gap. Half of everything eaten becomes new Tyranid population; the rest is the
        // inefficiency of rendering raw biomass into finished bioforms. All values are first-pass
        // tunables. See PRD §4.24 and the Design opening-scenario notes.
        private const double BiomassReferenceAvailability = 1_000_000.0;
        private const double BiomassAppetitePerTroop = 0.5;
        private const double ConsumptionDiminishingExponent = 0.5;
        private const double BiomassFeedEfficiency = 0.5;
        private const int BiomassAllocationSteps = 128;
        // Each week a degraded region heals this fraction of the gap back toward its natural ceiling —
        // a slow ecological recovery, so a consumed world stays blighted long after liberation.
        private const float CarryingCapacityRecoveryRate = 0.01f;
        // Each week this fraction of an overrun (hidden) Imperial remnant flees to adjacent governed
        // regions, seeking the nearest fortification rather than being slaughtered in place (§4.24).
        private const double ImperialEmigrationRate = 0.05;

        // --- Tyranid forced expansion (PRD §4.24 Tyranid Troop AI, step 2) ---
        // The share of a swarm's mobile force that spreads to a richer neighbour at full local
        // depletion (a bare region). Scaled down by the region's actual depletion, so a rich region
        // barely spreads (it gorges) while a stripped one sends a large fraction onward. First-pass
        // tunable.
        private const double TyranidExpansionShare = 0.5;

        // --- Genestealer Cult maneuvers (PRD §4.24) ---
        // A public Cult grinds the PDF down via cross-border raids (the offensive machinery). This
        // governs its IDLE force — Cult population in regions with no active Imperial enemy left to
        // fight nearby: each week this fraction of that pocket's mobile force shifts toward an
        // adjacent region nearer the fighting; where the fight is wholly out of reach it turns to
        // sacrificial predation instead. First-pass tunable.
        private const double CultRelocationRate = 0.25;

        // --- Situational awareness / intel (unified per-(faction, region) model) ---
        // Each turn a faction's awareness of a region decays toward zero (active knowledge from
        // patrols/recon goes stale), then is topped up by its standing sources. A standing listening
        // post contributes IntelPerListeningPostLevel per level per turn; with the decay rate below,
        // the geometric steady state of a level-L post is exactly L (so the old +2%/level strategic
        // bonus and stealth difficulty carry over at equilibrium while now ramping in and decaying
        // out). A patrol is recon of one's own ground: it adds a flat base plus a log term in the
        // patrol's headcount. All playtest-pending.
        private const float IntelDecayRate = 0.75f;
        private const float IntelPerListeningPostLevel = 0.2f;
        private const float IntelPatrolBaseGain = 1.0f;
        private readonly Dictionary<PlanetFaction, Dictionary<Region, float>> _turnIntelGains = new();

        public TurnController() : this(null)
        {
        }

        public TurnController(ISoldierTrainingService trainingService)
        {
            MissionContexts = new List<MissionContext>();
            SpecialMissions = new List<Mission>();
            StrategicCombatResults = new List<StrategicCombatResult>();
            _npcStrategyController = new FactionStrategyController();
            _trainingService = trainingService;
        }

        public void ProcessTurn(Sector sector)
        {
            MissionContexts.Clear();
            SpecialMissions.Clear();
            StrategicCombatResults.Clear();
            _turnIntelGains.Clear();
            ScenarioNotification = null;

            // --- 0. Shaping Phase ---
            // Diversion missions resolve before strategic planning so the feint they project is
            // already in place when factions decide where to garrison and attack this turn. This
            // is what lets a diversion pull enemy attention away from the player's other forces.
            List<Order> allOrdersThisTurn = sector.Orders.Values.ToList();
            ProcessDiversionMissions(allOrdersThisTurn.Where(o => o.Mission.MissionType == MissionType.Diversion && o.AssignedSquads.Any()));

            // --- 1. Strategic Planning Phase ---
            // Let each NPC faction generate its orders
            var enemyFactions = GameDataSingleton.Instance.GameRulesData.Factions.Where(f => !f.IsPlayerFaction && !f.IsDefaultFaction);
            foreach (var faction in enemyFactions)
            {
                allOrdersThisTurn.AddRange(_npcStrategyController.GenerateFactionOrders(faction, sector));
            }

            // The Imperial PDF (default faction) plans defensively: it fortifies and builds listening
            // posts to hold worlds under assault, but launches no offensives (PRD §4.24). Without this
            // the PDF could raise no defenses at all — only enemy factions previously planned.
            Faction defaultFaction = GameDataSingleton.Instance.GameRulesData.DefaultFaction;
            if (defaultFaction != null)
            {
                allOrdersThisTurn.AddRange(
                    _npcStrategyController.GenerateFactionOrders(defaultFaction, sector, defensiveOnly: true));
            }

            // The diversion effect is consumed entirely by the planning above; clear it so it
            // never lingers past the turn that produced it.
            ClearDiversionEffects(sector.Planets.Values);

            // --- 2. Mission Execution Phase ---
            var strategicCombatOrders = allOrdersThisTurn.Where(o => o.Mission is StrategicCombatMission);
            ProcessStrategicCombatMissions(strategicCombatOrders);

            var combatOrders = allOrdersThisTurn.Where(o => o.AssignedSquads.Any());
            ProcessCombatMissions(combatOrders);

            var constructionOrders = allOrdersThisTurn.Where(o => !o.AssignedSquads.Any() && o.Mission is ConstructionMission);
            ProcessConstructionOrders(constructionOrders);

            // --- 3. Planetary Simulation & Resolution Phase ---
            ApplyMissionResults();
            ProcessMedical(sector);
            TrainNonDeployedPlayerForces(sector);
            AdvanceFleetMovement(sector);
            UpdatePlanets(sector.Planets.Values);
            UpdateIntelligence(sector.Planets.Values);

            // --- 4. Scenario Resolution Phase ---
            // Resolve the opening objective after the planet sim has settled this turn, so the
            // win/lapse checks read the post-combat, post-growth state of the promised world.
            ProcessScenario(sector);
        }

        // Runs a planet-scoped slice of the weekly turn for a single world, for the given number of
        // weeks. Used by the opening-scenario stamp to let the promised world evolve during
        // generation before the player arrives — the revealed cult grinds the PDF down, then the
        // stranded Tyranid swarm feeds and spreads (Design/OpeningScenario.md §4.24, "Opening
        // Scenario Application"). It deliberately omits everything that is not local to this planet
        // or that belongs to the player's own upkeep: no player training or medical, no fleet
        // movement, no other planets, and no scenario resolution (the scenario is not yet assigned
        // during generation). The date is not advanced, so the Chapter's founding date is unaffected.
        internal void SimulatePlanetForward(Sector sector, Planet planet, int turns)
        {
            GameRulesData data = GameDataSingleton.Instance.GameRulesData;
            List<Faction> enemyFactions = data.Factions
                .Where(f => !f.IsPlayerFaction && !f.IsDefaultFaction).ToList();
            Faction defaultFaction = data.DefaultFaction;

            GameLog.Info(() => $"SimulatePlanetForward '{planet.Name}': {turns} turns");
            for (int week = 0; week < turns; week++)
            {
                System.Diagnostics.Stopwatch weekTimer = System.Diagnostics.Stopwatch.StartNew();
                MissionContexts.Clear();
                SpecialMissions.Clear();
                StrategicCombatResults.Clear();
                _turnIntelGains.Clear();

                // Strategic planning, scoped to this one planet. Enemy factions plan offensively;
                // the Imperial PDF plans defensively (fortify/listen only), exactly as ProcessTurn
                // does sector-wide.
                List<Order> orders = new List<Order>();
                foreach (Faction faction in enemyFactions)
                {
                    orders.AddRange(_npcStrategyController.GenerateFactionOrders(faction, sector, planet));
                }
                if (defaultFaction != null)
                {
                    orders.AddRange(_npcStrategyController.GenerateFactionOrders(
                        defaultFaction, sector, planet, defensiveOnly: true));
                }

                List<Order> strategicCombatOrders = orders.Where(o => o.Mission is StrategicCombatMission).ToList();
                ProcessStrategicCombatMissions(strategicCombatOrders);

                List<Order> combatOrders = orders.Where(o => o.AssignedSquads.Any()).ToList();
                GameLog.Debug(() =>
                    $"  week {week + 1}/{turns} '{planet.Name}': {orders.Count} orders, "
                    + $"{strategicCombatOrders.Count} strategic, {combatOrders.Count} combat "
                    + $"({combatOrders.Sum(o => o.AssignedSquads.Sum(s => s.Members.Count))} soldiers committed)");

                ProcessCombatMissions(combatOrders);
                ProcessConstructionOrders(orders.Where(o => !o.AssignedSquads.Any() && o.Mission is ConstructionMission));

                ApplyMissionResults();
                UpdatePlanet(planet);
                UpdateIntelligence(planet);

                GameLog.Info(() =>
                    $"  week {week + 1}/{turns} '{planet.Name}' done in {weekTimer.ElapsedMilliseconds}ms");
            }
        }

        // Resolves the "Promised World" opening objective (Design/OpeningScenario.md §6.2). Runs
        // only while the scenario is Pending; both outcomes move the *current* Sector Lord's
        // opinion (resolved on demand so credit/blame lands on whoever holds the seat now) and
        // surface a notification via ScenarioNotification.
        //   Win   — no Tyranid presence remains on the promised world: grant it as the Chapter
        //           World and raise the Sector Lord's opinion.
        //   Lapse — the world is fully overrun (no Imperial and no player presence left): withdraw
        //           the promise (no world granted) and lower the Sector Lord's opinion. A vacant
        //           seat makes the opinion move a no-op, but the lapse still resolves.
        public void ProcessScenario(Sector sector)
        {
            CampaignScenario scenario = sector.Scenario;
            if (scenario is not { State: ObjectiveState.Pending })
            {
                return;
            }

            GameRulesData data = GameDataSingleton.Instance.GameRulesData;
            Planet promised = sector.GetPlanet(scenario.PromisedPlanetId);
            Faction player = sector.PlayerForce.Faction;
            Faction imperial = data.DefaultFaction;

            // Win requires the world FULLY back in Imperial/player hands — every hostile faction
            // cleared, not merely the Tyranid swarm. The promised world starts with a revealed
            // Genestealer Cult as well as the swarm (§3.1a), and driving out the swarm while a cult
            // still holds ground is not liberation. So the objective completes only when no enemy
            // (non-default, non-player) faction has any presence left on the planet.
            bool enemiesRemain = HasEnemyPresence(promised);
            if (!enemiesRemain)
            {
                scenario.State = ObjectiveState.Won;
                // Reward path: install the player as the planet-wide controlling faction.
                SectorBuilder.ReplaceChapterPlanetFaction(promised, player);
                Character lord = sector.GetSectorLord();
                if (lord != null)
                {
                    lord.OpinionOfPlayerForce += ScenarioRules.SectorLordOpinionReward;
                }
                ScenarioNotification =
                    $"[b]The Promised World is liberated.[/b]\n\n{promised.Name} is cleansed of the "
                    + "enemy — swarm and cult alike — and granted to your Chapter. It is your home "
                    + "now — hold it in the Emperor's name.";
                return;
            }

            bool imperialRemains = HasPresence(promised, imperial.Id);
            bool playerRemains = HasPresence(promised, player.Id);
            if (!imperialRemains && !playerRemains)
            {
                scenario.State = ObjectiveState.Lapsed;
                // Promise withdrawn — no world granted. Reputation hit on the current seat; a
                // vacant seat (the capital itself fell) makes this a no-op without blocking the lapse.
                Character lord = sector.GetSectorLord();
                if (lord != null)
                {
                    lord.OpinionOfPlayerForce -= ScenarioRules.SectorLordOpinionPenalty;
                }
                ScenarioNotification =
                    $"[b]The Promised World is lost.[/b]\n\n{promised.Name} has fallen — no Imperial "
                    + "or Astartes presence remains to hold it. The promise is withdrawn, and your "
                    + "standing with the Sector Lord suffers for it. The war goes on.";
            }
        }

        // A faction has presence on a planet if any region holds a population or a standing
        // garrison for it. Depopulated factions are already pruned from the region maps in
        // UpdatePlanets, so this reads the settled post-turn state.
        private static bool HasPresence(Planet planet, int factionId)
        {
            return planet.Regions.Any(r =>
                r.RegionFactionMap.TryGetValue(factionId, out RegionFaction rf)
                && (rf.Population > 0 || rf.Garrison > 0));
        }

        // Any hostile faction — every faction that is neither the default (Imperial) nor the player —
        // with population or garrison anywhere on the planet. Used for the liberation check: the world
        // is retaken only when no enemy of any kind (Tyranid swarm, Genestealer Cult, …) remains.
        private static bool HasEnemyPresence(Planet planet)
        {
            return planet.Regions.Any(r =>
                r.RegionFactionMap.Values.Any(rf =>
                    !rf.PlanetFaction.Faction.IsDefaultFaction
                    && !rf.PlanetFaction.Faction.IsPlayerFaction
                    && (rf.Population > 0 || rf.Garrison > 0)));
        }

        private void AdvanceFleetMovement(Sector sector)
        {
            foreach (TaskForce taskForce in sector.Fleets.Values)
            {
                FleetTravelAdvanceResult result = taskForce.AdvanceTravelOneWeek();
                if (result.ExitedWarp)
                {
                    ApplyWarpSubjectiveTraining(taskForce, result.WarpSubjectiveWeeksElapsed);
                    taskForce.WarpSubjectiveTrainingApplied = true;
                }
            }
        }

        // Weekly medical resolution: wounds knit closed over time for the whole chapter
        // (deployed or not — a week passes for everyone), except locations that require a
        // replacement procedure. This is the substrate the Apothecary recovery countdowns
        // display and that medical procedures will ride on in a later pass.
        private void ProcessMedical(Sector sector)
        {
            Army army = sector.PlayerForce?.Army;
            if (army == null)
            {
                return;
            }
            MedicalTurnProcessor.ApplyWeeklyHealing(army.OrderOfBattle?.GetAllMembers());
            MedicalTurnProcessor.ResolveProcedures(army.MedicalProcedures, army.PlayerSoldierMap);
        }

        private void TrainNonDeployedPlayerForces(Sector sector)
        {
            ISoldierTrainingService trainingService = _trainingService ?? CreateTrainingService();
            List<Squad> squads = (sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
                ?? Enumerable.Empty<Squad>()).ToList();

            List<Squad> scoutSquads = squads.Where(s => IsScoutSquad(s) && CanTrainThisCampaignWeek(s)).ToList();
            Dictionary<int, TrainingFocuses> scoutFocusMap = scoutSquads.ToDictionary(s => s.Id, s => s.TrainingFocus);
            trainingService.TrainScouts(scoutSquads, scoutFocusMap, WeeklyTrainingPoints);

            foreach (Squad squad in squads.Where(s => !IsScoutSquad(s) && CanTrainThisCampaignWeek(s)))
            {
                if (squad.CurrentOrders != null) continue;

                foreach (ISoldier soldier in squad.Members)
                {
                    trainingService.ApplySoldierWorkExperience(soldier, squad, WeeklyTrainingPoints);
                }
            }
        }

        private void ApplyWarpSubjectiveTraining(TaskForce taskForce, double subjectiveWeeks)
        {
            if (subjectiveWeeks <= 0) return;

            ISoldierTrainingService trainingService = _trainingService ?? CreateTrainingService();
            List<Squad> embarkedSquads = taskForce.Ships
                .SelectMany(ship => ship.LoadedSquads)
                .Where(squad => squad.CurrentOrders == null)
                .ToList();
            float points = (float)(WeeklyTrainingPoints * subjectiveWeeks);

            List<Squad> scoutSquads = embarkedSquads.Where(IsScoutSquad).ToList();
            Dictionary<int, TrainingFocuses> scoutFocusMap = scoutSquads.ToDictionary(s => s.Id, s => s.TrainingFocus);
            trainingService.TrainScouts(scoutSquads, scoutFocusMap, points);

            foreach (Squad squad in embarkedSquads.Where(squad => !IsScoutSquad(squad)))
            {
                foreach (ISoldier soldier in squad.Members)
                {
                    trainingService.ApplySoldierWorkExperience(soldier, squad, points);
                }
            }
        }

        private static bool CanTrainThisCampaignWeek(Squad squad)
        {
            return squad.BoardedLocation?.Fleet?.TravelPhase != FleetTravelPhase.InWarp;
        }

        private static bool IsScoutSquad(Squad squad)
        {
            return (squad.SquadTemplate.SquadType & SquadTypes.Scout) == SquadTypes.Scout;
        }

        private static ISoldierTrainingService CreateTrainingService()
        {
            GameRulesData rules = GameDataSingleton.Instance.GameRulesData;
            RatingCalculator ratingCalculator = new(rules.RatingDefinitions, rules.RatingAwardTiers,
                                                    rules.BaseSkillMap, StaticRNG.Instance);
            return new SoldierTrainingCalculator(rules.BaseSkillMap.Values, rules.TrainingProfiles.Values,
                                                 ratingCalculator);
        }

        internal void ProcessStrategicCombatMissions(IEnumerable<Order> strategicCombatOrders)
        {
            var resolver = new StrategicCombatResolver(recordIntelGain: RecordIntelGain);
            foreach (Order order in strategicCombatOrders)
            {
                if (order.Mission is not StrategicCombatMission mission) continue;

                StrategicCombatResult result = resolver.Resolve(mission);
                StrategicCombatResults.Add(result);
                GameLog.Debug(() =>
                    $"Strategic combat {result.Attacker?.Name} -> {DescribeRegionFaction(result.Target)}: "
                    + $"outcome={result.Outcome}, won={result.AttackerWon}, controlChanged={result.ControlChanged}, "
                    + $"committed={result.CommittedBattleValue}, defenderBV={result.DefenderBattleValue}, "
                    + $"effective={result.AttackerEffectiveStrength:F0}/{result.DefenderEffectiveStrength:F0}, "
                    + $"losses={result.AttackerLosses}/{result.DefenderLosses}, survivors={result.AttackerSurvivors}, "
                    + $"contributions={DescribeStrategicContributions(mission.Contributions)}");
            }
        }

        private void ProcessCombatMissions(IEnumerable<Order> combatOrders)
        {
            foreach (Order order in combatOrders)
            {
                if(order.Mission.MissionType == MissionType.DefenseInDepth) continue;
                // Diversions already resolved in the pre-planning shaping phase; their squads
                // remain on the map only to defend (see AssembleDefendingForce) if the feint
                // draws a counterattack this turn.
                if(order.Mission.MissionType == MissionType.Diversion) continue;
                // A patrol is a standing defensive screen, not an active mission: its squads land in
                // the region and hold (FactionStrategyController.PlanPatrolMissionsOnPlanet), joining
                // the defence if the region is raided (AssembleDefendingForce) and denying enemy
                // recon that tries to scout it. The order itself launches nothing this turn.
                if (order.Mission.MissionType == MissionType.Patrol) continue;

                // A construction order with an assigned squad is the player (or any faction)
                // fortifying a region: the squad spends the turn building rather than fighting.
                if (order.Mission is ConstructionMission constructionMission)
                {
                    ResolveSquadConstruction(order, constructionMission);
                    continue;
                }

                bool isPlayerOrder = order.AssignedSquads.First().Faction.IsPlayerFaction;

                // A combat order persists across turns (ProcessTurn never clears orders), so once a
                // squad is wiped or fully incapacitated — dead soldiers are permanently removed from
                // Squad.Members in BattleTurnResolver — the order would otherwise re-deploy a squad
                // that has no able soldiers left and crash in BattleSquad construction. A depleted
                // squad cannot fight, so deploy only squads that still have an able member.
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
                MissionContext context = new MissionContext(order, involvedBattleSquads, new List<BattleSquad>());
                MissionStepOrchestrator.GetStartingStep(context).ExecuteMissionStep(context, 0, null);
                MissionContexts.Add(context);
                GameLog.Debug(() =>
                    $"Combat mission result {order.AssignedSquads.First().Faction.Name} "
                    + $"{order.Mission.MissionType} -> {DescribeRegionFaction(order.Mission.RegionFaction)}: "
                    + $"impact={context.Impact:F2}, enemiesKilled={context.EnemiesKilled}, days={context.DaysElapsed}, "
                    + $"logEntries={context.Log.Count}");
            }
        }

        // Diversions resolve in the pre-planning shaping phase: each runs its overt demonstration
        // to accumulate Impact, then projects a perceived-threat (and, if aggressive, provocation)
        // effect that the factions read when generating orders this same turn.
        private void ProcessDiversionMissions(IEnumerable<Order> diversionOrders)
        {
            foreach (Order order in diversionOrders)
            {
                bool isPlayerOrder = order.AssignedSquads.First().Faction.IsPlayerFaction;
                // As in ProcessCombatMissions, never construct a BattleSquad from a depleted squad.
                List<BattleSquad> involvedBattleSquads = order.AssignedSquads
                                                              .Where(s => s.Members.Any(m => m.CanFight))
                                                              .Select(s => new BattleSquad(isPlayerOrder, s))
                                                              .ToList();
                if (involvedBattleSquads.Count == 0) continue;

                MissionContext context = new MissionContext(order, involvedBattleSquads, new List<BattleSquad>());
                MissionStepOrchestrator.GetStartingStep(context).ExecuteMissionStep(context, 0, null);
                MissionContexts.Add(context);
                ApplyDiversionEffect(order, context);
            }
        }

        private void ApplyDiversionEffect(Order order, MissionContext context)
        {
            Mission mission = order.Mission;
            RegionFaction targetFaction = mission.RegionFaction;
            long actualManpower = order.AssignedSquads.Sum(s => s.Members.Count);
            if (actualManpower <= 0) return;

            // MissionSize, when set, caps how convincing the feint can be.
            float clampedImpact = mission.MissionSize > 0
                ? Math.Min(context.Impact, mission.MissionSize)
                : context.Impact;
            if (clampedImpact <= 0) return;

            float multiplier = (float)Math.Pow(1 + clampedImpact / DiversionThreatScale, 2);
            float apparentThreat = actualManpower * multiplier;
            // The real force is already counted in the enemy's threat assessment via its landed
            // squads, so only the phantom remainder is the feint's contribution.
            targetFaction.PerceivedThreatBonus += apparentThreat - actualManpower;

            // At Normal aggression or higher the feint is loud enough to bait the enemy into
            // committing a counterattack toward the feinting force's own region.
            if (order.LevelOfAggression >= Aggression.Normal)
            {
                Squad feintSquad = order.AssignedSquads.First();
                Region feintRegion = feintSquad.CurrentRegion;
                if (feintRegion != null
                    && feintRegion.RegionFactionMap.TryGetValue(feintSquad.Faction.Id, out RegionFaction feintFaction))
                {
                    feintFaction.ProvocationLevel += clampedImpact;
                }
            }
        }

        // Clears the transient diversion effect after the factions have generated their orders for
        // the turn, so a feint never influences more than the single turn that produced it.
        private void ClearDiversionEffects(IEnumerable<Planet> planets)
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

        private void ProcessConstructionOrders(IEnumerable<Order> constructionOrders)
        {
            // squad-less construction orders (NPC faction development) resolve instantly at a
            // fixed mission size and don't create a context
            List<Order> orders = constructionOrders.ToList();
            foreach (var order in orders)
            {
                if (order.Mission is ConstructionMission mission)
                {
                    ApplyConstruction(mission, mission.MissionSize);
                }
            }
            if (orders.Count > 0)
            {
                GameLog.Debug(() =>
                    $"Construction resolved: orders={orders.Count}, {SummarizeConstructionOrders(orders)}");
            }
        }

        // Resolves a construction order carried out by an assigned squad (e.g. the player
        // fortifying a region). The amount built scales with both squad size and engineering
        // skill: every able soldier contributes its Engineering (Fortification) skill value,
        // and the summed contribution is divided down to a defensive increment (minimum 1 so
        // an assigned squad always makes some progress).
        private void ResolveSquadConstruction(Order order, ConstructionMission mission)
        {
            BaseSkill engineering = GameDataSingleton.Instance.GameRulesData.Skills.EngineeringFortification;
            float totalSkill = order.AssignedSquads
                .SelectMany(s => s.Members)
                .Sum(soldier => soldier.GetTotalSkillValue(engineering));
            int amount = Math.Max(1, (int)(totalSkill / EngineeringBuildDivisor));
            ApplyConstruction(mission, amount);
        }

        // Applies a resolved recon mission's result. The scouting faction sharpens its own belief
        // about the reconnoitred region faction's strength — the intelligence its offensive
        // targeting acts on (FactionStrategyController; PRD §4.24) — by however much the recon
        // actually learned. A scout detected and driven off by the region's patrol never reaches
        // PerformReconMissionStep, so its Impact (and thus belief gained) stays ~zero: natural
        // denial via the contested stealth check, not a flat override. AddRegionIntel ignores
        // non-positive amounts. During full turn processing, player/default recon gains are pooled
        // through RecordIntelGain and later surfaced through player-visible RegionIntel; enemy recon
        // remains faction-owned and does not leak into the player's knowledge of the region.
        internal static void ResolveReconResult(
            Faction reconningFaction,
            RegionFaction target,
            float impact,
            Action<PlanetFaction, Region, float> recordIntelGain = null)
        {
            if (target == null) return;
            PlanetFaction reconningPlanetFaction =
                reconningFaction != null
                && target.Region.Planet.PlanetFactionMap.TryGetValue(reconningFaction.Id, out PlanetFaction pf)
                    ? pf
                    : null;
            float observerBefore = reconningPlanetFaction?.GetRegionIntel(target.Region) ?? 0f;
            if (reconningPlanetFaction != null)
            {
                if (recordIntelGain != null)
                {
                    recordIntelGain(reconningPlanetFaction, target.Region, impact);
                }
                else
                {
                    reconningPlanetFaction.AddRegionIntel(target.Region, impact);
                }
            }
            float observerAfter = reconningPlanetFaction?.GetRegionIntel(target.Region) ?? observerBefore;
            GameLog.Debug(() =>
                $"Recon result {reconningFaction?.Name ?? "Unknown"} -> {DescribeRegionFaction(target)}: "
                + $"impact={impact:F2}, regionIntel={observerBefore:F2}->{observerAfter:F2}");
        }

        private static void ApplyConstruction(ConstructionMission mission, int amount)
        {
            int before = GetConstructionLevel(mission);
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
                    mission.RegionFaction.Organization = Math.Min(100, mission.RegionFaction.Organization + amount);
                    break;
            }
            int after = GetConstructionLevel(mission);
            GameLog.Trace(() =>
                $"Construction applied {DescribeRegionFaction(mission.RegionFaction)}: "
                + $"{mission.ConstructionType} {before}->{after} (requested +{amount})");
        }

        private static int GetConstructionLevel(ConstructionMission mission)
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
            var missions = orders
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
                    $"{g.Key.Faction}/{g.Key.Planet}/{g.Key.Region} {g.Key.ConstructionType}+{g.Sum(m => m.MissionSize)}"));
        }

        private static string DescribeStrategicContributions(IEnumerable<StrategicCombatContribution> contributions)
        {
            var parts = contributions
                .Where(c => c.BattleValue > 0)
                .Select(c => $"{c.StagingFaction?.Region.Name ?? "unknown"}:{c.BattleValue}")
                .ToList();
            return parts.Count == 0 ? "none" : string.Join(",", parts);
        }

        private static string DescribeRegionFaction(RegionFaction regionFaction)
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

        private void ApplyMissionResults()
        {
            foreach (MissionContext context in MissionContexts)
            {
                // This logic is moved from the old ProcessMissions method
                RegionFaction regionFaction = context.Order.Mission.RegionFaction;
                switch (context.Order.Mission.MissionType)
                {
                    case MissionType.Assassination:
                        // 10 ^ (impact*100) / population = amount of org log
                        // example impact 1 in a population of 1000 = -1 org point
                        int orgLost = (int)(context.Impact * 100 / regionFaction.Population);
                        regionFaction.Organization -= Math.Min(orgLost, regionFaction.Organization);
                        break;
                    case MissionType.Recon:
                        // Intel gained is whatever the recon actually accomplished: a scout that was
                        // detected and driven off by the region's patrol before infiltrating never
                        // reaches PerformReconMissionStep, so its Impact (and thus belief gained)
                        // stays ~zero. A patrol contests recon by raising the defender's own-region
                        // intel, which lifts the stealth-check difficulty — not a flat override.
                        ResolveReconResult(context.Order.AssignedSquads.FirstOrDefault()?.Faction,
                                           regionFaction, context.Impact, RecordIntelGain);
                        break;
                    case MissionType.Sabotage:
                        SabotageMission sabotageMission = (SabotageMission)context.Order.Mission;
                        int impact = (int)Math.Min(context.Impact, sabotageMission.MissionSize);
                        switch (sabotageMission.DefenseType)
                        {
                            case DefenseType.Entrenchment:
                                regionFaction.Entrenchment -= impact;
                                if (regionFaction.Entrenchment < 0)
                                {
                                    regionFaction.Entrenchment = 0;
                                }
                                break;
                            case DefenseType.ListeningPost:
                                regionFaction.ListeningPost -= impact;
                                if (regionFaction.ListeningPost < 0)
                                {
                                    regionFaction.ListeningPost = 0;
                                }
                                break;
                            case DefenseType.AntiAir:
                                regionFaction.AntiAir -= impact;
                                if (regionFaction.AntiAir < 0)
                                {
                                    regionFaction.AntiAir = 0;
                                }
                                break;
                        }
                        break;
                }
                // Casualties come out of the defender's fighting strength — Population for a horde
                // whose numbers are its army, otherwise Garrison — never its civilian population.
                // Measured in battle value (the fallen defenders' point values) so a few elite
                // losses weigh more than a mass of conscripts (PRD §4.24). Entrenchment still blunts
                // the toll a stormed-into region actually suffers, with diminishing returns: level 5
                // halves tactical casualties, and higher levels keep helping asymptotically.
                long defenderCasualties = FallenBattleValue(context.OpposingSquads);
                if (regionFaction.Entrenchment > 0)
                {
                    float casualtyMultiplier = 1.0f / (1.0f + regionFaction.Entrenchment / 5.0f);
                    defenderCasualties = (long)(defenderCasualties * casualtyMultiplier);
                }
                long defenderStrengthBefore = regionFaction.MilitaryStrength;
                regionFaction.RemoveMilitaryStrength(defenderCasualties);
                GameLog.Debug(() =>
                    $"Mission attrition {context.Order.Mission.MissionType} -> {DescribeRegionFaction(regionFaction)}: "
                    + $"defenderLosses={defenderCasualties}, defenderStrength={defenderStrengthBefore}->{regionFaction.MilitaryStrength}");

                // A surviving AI offensive no longer evaporates: it withdraws to its staging region
                // (raid) or seizes the contested ground (invade).
                ResolveOffensiveSurvivors(context);
            }
        }

        // Total battle value of the soldiers in these squads who were downed in the engagement — the
        // strategic strength destroyed, in the point currency the pools are kept in (PRD §4.24).
        private static long FallenBattleValue(IEnumerable<BattleSquad> squads)
        {
            if (squads == null) return 0;
            return squads
                .SelectMany(squad => squad.Soldiers)
                .Where(soldier => !soldier.CanFight)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);
        }

        // Total battle value of the soldiers in these squads still able to fight — the surviving
        // strength an offensive brings home (raid) or plants on the ground it takes (invade).
        private static long AbleBattleValue(IEnumerable<BattleSquad> squads)
        {
            if (squads == null) return 0;
            return squads
                .SelectMany(squad => squad.AbleSoldiers)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);
        }

        // Accounts the survivors of an AI offensive rather than letting them dissolve (PRD §4.24).
        // On a raid the survivors withdraw to their staging region; on an invasion they remain and
        // establish (or reinforce) the attacker's presence on the ground they fought over. Player
        // assaults use persistent roster squads and are handled by the normal squad lifecycle.
        private static void ResolveOffensiveSurvivors(MissionContext context)
        {
            if (context.Order.Mission.MissionType != MissionType.Advance) return;
            BattleSquad first = context.MissionSquads.FirstOrDefault();
            if (first == null || first.IsPlayerSquad) return;

            long survivors = AbleBattleValue(context.MissionSquads);
            if (survivors <= 0) return; // the offensive was wiped out — nothing returns or holds

            Faction attacker = first.Squad.Faction;
            if (attacker.InvadesOnVictory)
            {
                EstablishInvaderPresence(attacker, context.Order.Mission.RegionFaction.Region, survivors);
                GameLog.Debug(() =>
                    $"Offensive survivors {attacker.Name}: established foothold in "
                    + $"{context.Order.Mission.RegionFaction.Region.Planet.Name}/"
                    + $"{context.Order.Mission.RegionFaction.Region.Name}, survivors={survivors}");
            }
            else if (first.Squad.CurrentRegion != null
                     && first.Squad.CurrentRegion.RegionFactionMap.TryGetValue(attacker.Id, out RegionFaction home))
            {
                home.AddMilitaryStrength(survivors);
                GameLog.Debug(() =>
                    $"Offensive survivors {attacker.Name}: returned to "
                    + $"{home.Region.Planet.Name}/{home.Region.Name}, survivors={survivors}");
            }
        }

        // Establishes or reinforces an invading faction's RegionFaction in a region it has assaulted,
        // creating the backing PlanetFaction if the faction had no prior foothold on the world. The
        // surviving strength is added in battle-value points.
        internal static void EstablishInvaderPresence(Faction attacker, Region region, long survivors)
        {
            if (region.RegionFactionMap.TryGetValue(attacker.Id, out RegionFaction existing))
            {
                existing.AddMilitaryStrength(survivors);
                return;
            }
            Planet planet = region.Planet;
            if (!planet.PlanetFactionMap.TryGetValue(attacker.Id, out PlanetFaction planetFaction))
            {
                planetFaction = new PlanetFaction(attacker) { IsPublic = true };
                planet.PlanetFactionMap[attacker.Id] = planetFaction;
            }
            RegionFaction foothold = new RegionFaction(planetFaction, region)
            {
                IsPublic = true,
                Organization = 100
            };
            foothold.AddMilitaryStrength(survivors);
            region.RegionFactionMap[attacker.Id] = foothold;
        }

        private void UpdatePlanets(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                UpdatePlanet(planet);
            }
        }

        // Advances every faction's per-region situational awareness on the planet by one turn:
        // first decays all held beliefs (patrol/recon knowledge going stale), then tops up each held
        // region from its standing sources — the passive listening-post sensor floor and any active
        // patrol sweeping its own ground. Offensive beliefs about enemy regions (held on the
        // observing PlanetFaction but with no local structure/patrol) therefore simply fade unless
        // refreshed by a fresh recon (ResolveReconResult).
        private void UpdateRegionIntel(Planet planet)
        {
            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
            {
                foreach (Region region in planetFaction.RegionIntel.Keys.ToList())
                {
                    planetFaction.SetRegionIntel(region, planetFaction.GetRegionIntel(region) * IntelDecayRate);
                }
            }

            foreach (Region region in planet.Regions)
            {
                foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                {
                    float gain = regionFaction.ListeningPost * IntelPerListeningPostLevel;
                    int patrolStrength = regionFaction.LandedSquads
                        .Where(s => s.CurrentOrders?.Mission.MissionType == MissionType.Patrol)
                        .Sum(s => s.Members.Count);
                    if (patrolStrength > 0)
                    {
                        gain += IntelPatrolBaseGain + (float)Math.Log10(patrolStrength);
                    }
                    RecordIntelGain(regionFaction.PlanetFaction, region, gain);
                }
            }

            ApplyTurnIntelGains(planet);
        }

        private void RecordIntelGain(PlanetFaction planetFaction, Region region, float gain)
        {
            if (planetFaction == null || region == null || gain <= 0f) return;
            if (!_turnIntelGains.TryGetValue(planetFaction, out Dictionary<Region, float> factionGains))
            {
                factionGains = new Dictionary<Region, float>();
                _turnIntelGains[planetFaction] = factionGains;
            }
            factionGains[region] = factionGains.TryGetValue(region, out float existing)
                ? existing + gain
                : gain;
        }

        private void ApplyTurnIntelGains(Planet planet)
        {
            if (_turnIntelGains.Count == 0) return;

            List<PlanetFaction> sharingFactions = planet.PlanetFactionMap.Values
                .Where(SharesPlayerVisibleIntel)
                .ToList();
            Dictionary<Region, float> pooledSharingGains = new();

            foreach (KeyValuePair<PlanetFaction, Dictionary<Region, float>> factionEntry in _turnIntelGains.ToList())
            {
                PlanetFaction planetFaction = factionEntry.Key;
                bool presentOnPlanet =
                    planet.PlanetFactionMap.TryGetValue(planetFaction.Faction.Id, out PlanetFaction presentFaction)
                    && ReferenceEquals(presentFaction, planetFaction);

                foreach (KeyValuePair<Region, float> gainEntry in factionEntry.Value.ToList())
                {
                    if (gainEntry.Key.Planet != planet) continue;

                    if (presentOnPlanet)
                    {
                        if (SharesPlayerVisibleIntel(planetFaction))
                        {
                            pooledSharingGains[gainEntry.Key] =
                                pooledSharingGains.TryGetValue(gainEntry.Key, out float existing)
                                    ? existing + gainEntry.Value
                                    : gainEntry.Value;
                        }
                        else
                        {
                            planetFaction.AddRegionIntel(gainEntry.Key, gainEntry.Value);
                        }
                    }

                    factionEntry.Value.Remove(gainEntry.Key);
                }

                if (factionEntry.Value.Count == 0)
                {
                    _turnIntelGains.Remove(planetFaction);
                }
            }

            foreach (KeyValuePair<Region, float> pooledGain in pooledSharingGains)
            {
                foreach (PlanetFaction sharingFaction in sharingFactions)
                {
                    sharingFaction.AddRegionIntel(pooledGain.Key, pooledGain.Value);
                }
            }
        }

        private static bool SharesPlayerVisibleIntel(PlanetFaction planetFaction) =>
            planetFaction?.Faction.IsPlayerFaction == true || planetFaction?.Faction.IsDefaultFaction == true;

        private void UpdatePlanet(Planet planet)
        {
            {
                // Tyranid troop AI step 2 (PRD §4.24): the swarm spreads toward fresh biomass before
                // it eats, so force that expands is not also counted consuming at home. Fight (step 1)
                // was already committed and drawn from the pool during strategic planning.
                ResolveTyranidExpansion(planet);

                foreach (Region region in planet.Regions)
                {
                    float pdfRatio = region.PlanetaryDefenseForces / (float)region.Population;
                    // snapshot the values: depopulated factions are removed from the map below
                    foreach (RegionFaction regionFaction in region.RegionFactionMap.Values.ToList())
                    {
                        if(regionFaction.Population <= 0)
                        {
                            region.RegionFactionMap.Remove(regionFaction.PlanetFaction.Faction.Id);
                        }
                        else
                        {
                            EndOfTurnRegionFactionsUpdate(regionFaction, pdfRatio);
                        }
                    }

                    // Tyranid biomass step: after the week's ordinary growth, the swarm eats
                    // (predate headcount + consume the land), then the land heals a little back
                    // toward its natural ceiling (PRD §4.24).
                    ResolveBiomassConsumption(region);
                    RecoverCarryingCapacity(region);
                }

                // Imperial remnant lifecycle (PRD §4.24), in two passes so emigration reads the
                // finalized hide/unhide states: first surface or hide each region's remnant, then
                // let the hidden survivors trickle out to adjacent governed regions.
                foreach (Region region in planet.Regions)
                {
                    UpdateImperialRemnantState(region);
                }
                foreach (Region region in planet.Regions)
                {
                    ProcessImperialEmigration(region);
                }

                // Genestealer Cult idle-force maneuvers, after the remnant hide/unhide is settled so
                // the "active PDF nearby?" test reads the finalized states (PRD §4.24).
                foreach (Region region in planet.Regions)
                {
                    ResolveCultManeuvers(region);
                }

                CheckForPlanetaryRevolt(planet);
                CheckForRevoltSuppression(planet);

                // snapshot the values: depopulated factions are removed from the map below
                foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values.ToList())
                {
                    // PlanetFaction has no population of its own, so derive the
                    // faction's planet-wide population from its region factions here.
                    long planetFactionPopulation = planet.Regions.Sum(
                        r => r.RegionFactionMap.TryGetValue(planetFaction.Faction.Id, out RegionFaction rf)
                            ? rf.Population
                            : 0);
                    // if the planetFaction no longer has any population on the planet, remove it
                    if (planetFactionPopulation <= 0)
                    {
                        planet.PlanetFactionMap.Remove(planetFaction.Faction.Id);
                    }
                    // see if this faction leader is the sort who'd request aid from the player
                    else if (planetFaction.Leader != null)
                    {
                        EndOfTurnLeaderUpdate(planet, planetFaction);
                    }
                }

                // End-of-turn situational awareness: decay every faction's beliefs, then refresh
                // from standing listening posts and patrols. Runs after depopulated factions are
                // pruned so dead beliefs are not carried forward.
                UpdateRegionIntel(planet);
            }
        }

        internal void EndOfTurnRegionFactionsUpdate(RegionFaction regionFaction, float pdfRatio)
        {
            Planet planet = regionFaction.Region.Planet;
            Faction controllingFaction = planet.GetControllingFaction();
            float newPop = 0;
            // An overrun Imperial remnant (a hidden default faction — Stage 3 of the hide/unhide
            // lifecycle, PRD §4.24) is a population gone to ground under a public enemy: it neither
            // grows organically nor drafts a garrison, so it skips growth resolution entirely.
            bool isOverrunRemnant = regionFaction.PlanetFaction.Faction.IsDefaultFaction && !regionFaction.IsPublic;
            // GrowthMultiplier (default 1.0) is a general multiplicative throttle on *organic* growth,
            // available for future tuning of organically-growing factions (e.g. Ork feral penalty,
            // revolt tuning). It applies only to the Logistic/baseline branches below. Conversion is
            // not organic growth, and Consumption factions (Tyranids) have no organic birthrate at all
            // (they grow only by eating biomass — PRD §4.24), so neither reads it.
            if (!isOverrunRemnant)
            {
                switch (regionFaction.PlanetFaction.Faction.GrowthType)
                {
                    case GrowthType.Logistic:
                        newPop = ApplyCarryingCapacity(
                            regionFaction.Population * LogisticGrowthRate * regionFaction.GrowthMultiplier,
                            regionFaction.Region);
                        break;
                    case GrowthType.Conversion:
                        // Conversion factions (Genestealer Cult) grow only while HIDDEN. Converting
                        // the populace is clandestine — cultists steal individuals away at night to
                        // be implanted — which depends on cover, opportunity, and a target populace
                        // still going about its life. Once the cult reveals itself into open warfare
                        // that all collapses: the front replaces the shadows and there is neither the
                        // means nor the inclination to keep proselytizing. A public converting faction
                        // therefore makes no converts and does not grow this turn.
                        if (!regionFaction.IsPublic)
                        {
                            newPop = ConvertPopulation(regionFaction.Region, regionFaction, newPop);
                            if (regionFaction.PlanetFaction.Faction.Id != controllingFaction.Id &&
                                planet.PlanetFactionMap[controllingFaction.Id].Leader != null)
                            {
                                // TODO: see if the governor notices the converted population
                            }
                        }
                        break;
                    case GrowthType.Consumption:
                        // Consumption factions (Tyranids) have no organic birthrate; all their growth
                        // comes from eating biomass (Predate/Consume), applied in the biomass step
                        // rather than here (PRD §4.24). Organic growth is therefore zero.
                        newPop = 0;
                        break;
                    default:
                        newPop = ApplyCarryingCapacity(
                            regionFaction.Population * BaselineGrowthRate * regionFaction.GrowthMultiplier,
                            regionFaction.Region);
                        break;
                }
            }
            // probabilistic rounding of the fractional remainder, handling both growth
            // (positive) and over-capacity decline (negative)
            float whole = (float)Math.Truncate(newPop);
            float fraction = newPop - whole;
            if (RNG.GetLinearDouble() < Math.Abs(fraction))
            {
                whole += Math.Sign(fraction);
            }
            regionFaction.Population += (long)whole;
            if (regionFaction.Population < 0)
            {
                regionFaction.Population = 0;
            }
            UpdateRegionFactionForces(regionFaction, pdfRatio, newPop);
        }

        // Per-organized-troop marginal predation yield at a given level of remaining prey:
        // logarithmic (easy to find prey when abundant, sharply diminishing as they thin), and
        // normalized to BiomassAppetitePerTroop at the reference availability. PRD §4.24.
        private static double PredationMarginalYield(double preyRemaining)
        {
            if (preyRemaining <= 0) return 0;
            return BiomassAppetitePerTroop * Math.Log(1 + preyRemaining) / Math.Log(1 + BiomassReferenceAvailability);
        }

        // Per-organized-troop marginal consumption yield at a given level of remaining biomass:
        // a gentle power (square root) — fast to strip a rich region, mildly diminishing — likewise
        // normalized to BiomassAppetitePerTroop at the reference availability. PRD §4.24.
        private static double ConsumptionMarginalYield(double biomassRemaining)
        {
            if (biomassRemaining <= 0) return 0;
            return BiomassAppetitePerTroop * Math.Pow(biomassRemaining / BiomassReferenceAvailability, ConsumptionDiminishingExponent);
        }

        // Tyranid forced expansion (PRD §4.24 Tyranid Troop AI, step 2). After the swarm has committed
        // force to any fight — the offensive machinery, which already draws that force from its pool
        // during planning — its remaining organized force spreads to neighbours to reach fresh
        // biomass, biased by how depleted the home region is: a rich region keeps the swarm home to
        // gorge, a stripped one pushes it onward toward the richest neighbour. This runs BEFORE
        // ResolveBiomassConsumption so the force that leaves is not also counted as eating at home —
        // the "spent twice" reconciliation that completes the fight → expand → consume allocation.
        internal static void ResolveTyranidExpansion(Planet planet)
        {
            // Compute every move from the pre-expansion snapshot so a swarm cannot cascade across the
            // whole map within one turn.
            var moves = new List<(RegionFaction Source, Region Destination, long Amount)>();
            foreach (Region region in planet.Regions)
            {
                foreach (RegionFaction swarm in region.RegionFactionMap.Values
                    .Where(rf => rf.PlanetFaction.Faction.GrowthType == GrowthType.Consumption && rf.Population > 0))
                {
                    long organized = (long)(swarm.Population * (Math.Max(swarm.Organization, 0) / 100.0));
                    if (organized <= 0) continue;

                    double homeBiomass = RegionBiomass(region);
                    Region richest = region.GetAdjacentRegions().OrderByDescending(RegionBiomass).FirstOrDefault();
                    // Only spread toward genuinely richer ground; a swarm ringed by equally-stripped
                    // regions stays and finishes what little is left rather than thrashing.
                    if (richest == null || RegionBiomass(richest) <= homeBiomass) continue;

                    long movers = Math.Min(swarm.Population,
                        (long)(organized * RegionDepletion(region) * TyranidExpansionShare));
                    if (movers > 0)
                    {
                        moves.Add((swarm, richest, movers));
                    }
                }
            }

            foreach ((RegionFaction source, Region destination, long amount) in moves)
            {
                long sourceBefore = source.Population;
                source.Population -= amount;
                EstablishInvaderPresence(source.PlanetFaction.Faction, destination, amount);
                // No GrowthMultiplier to propagate: Consumption factions have no organic birthrate
                // (their growth is the biomass step, which ignores GrowthMultiplier — PRD §4.24), so
                // a fresh foothold cannot outgrow the parent swarm regardless.
                GameLog.Trace(() =>
                    $"Tyranid expansion {source.PlanetFaction.Faction.Name} "
                    + $"{source.Region.Planet.Name}/{source.Region.Name}->{destination.Name}: "
                    + $"moved={amount} (sourcePop {sourceBefore}->{source.Population}), "
                    + $"depletion={RegionDepletion(source.Region):F2}, "
                    + $"destBiomass={RegionBiomass(destination):F0}");
            }
        }

        // Edible biomass currently in a region: the headcount the swarm can predate (all non-Tyranid
        // population) plus the carrying capacity it can consume.
        private static double RegionBiomass(Region region)
        {
            long prey = region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.GrowthType != GrowthType.Consumption)
                .Sum(rf => rf.Population);
            return prey + Math.Max(0, region.CarryingCapacity);
        }

        // How stripped a region is (PRD §4.24), 0 (untouched) to 1 (bare): the average shortfall of
        // its remaining land (capacity vs. its natural ceiling) and its remaining edible headcount
        // (vs. that same ceiling, a stand-in for the population it once held). A stripped region
        // pushes its swarm onward.
        private static double RegionDepletion(Region region)
        {
            double ceiling = Math.Max(1, region.MaximumCarryingCapacity);
            double capFraction = Math.Clamp(region.CarryingCapacity / ceiling, 0, 1);
            long prey = region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.GrowthType != GrowthType.Consumption)
                .Sum(rf => rf.Population);
            double preyFraction = Math.Clamp(prey / ceiling, 0, 1);
            return 1.0 - 0.5 * (capFraction + preyFraction);
        }

        // The Tyranid biomass step (PRD §4.24): each Consumption faction's organized troops eat the
        // region, splitting their effort between predation (killing co-located headcount) and
        // consumption (stripping carrying capacity) to equalize marginal yield, with half of all
        // biomass eaten becoming new Tyranid population. Capacity recovery is handled separately.
        internal static void ResolveBiomassConsumption(Region region)
        {
            foreach (RegionFaction consumer in region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.GrowthType == GrowthType.Consumption).ToList())
            {
                // The mobile organized force does the eating; the unorganized remainder is the
                // gestating brood/feeding pools. In Phase 2 the whole organized force feeds — the
                // fight/expand allocation that skims it first arrives with the troop AI (PRD §4.24).
                double troops = consumer.Population * (consumer.Organization / 100.0);
                if (troops <= 0) continue;

                List<RegionFaction> prey = region.RegionFactionMap.Values
                    .Where(rf => rf.PlanetFaction.Faction.GrowthType != GrowthType.Consumption && rf.Population > 0)
                    .ToList();
                double preyRemaining = prey.Sum(rf => rf.Population);
                double biomassRemaining = Math.Max(0, region.CarryingCapacity);

                // Water-filling: hand the force out in chunks, each to whichever action currently
                // yields more per troop, depleting that pool as we go. The stopping point is the
                // equilibrium where shifting force either way would lower total yield.
                double predated = 0;
                double consumed = 0;
                double chunk = troops / BiomassAllocationSteps;
                double troopsRemaining = troops;
                for (int step = 0; step < BiomassAllocationSteps && troopsRemaining > 0; step++)
                {
                    double thisChunk = Math.Min(chunk, troopsRemaining);
                    troopsRemaining -= thisChunk;
                    double predationYield = PredationMarginalYield(preyRemaining);
                    double consumptionYield = ConsumptionMarginalYield(biomassRemaining);
                    if (predationYield <= 0 && consumptionYield <= 0) break;
                    if (predationYield >= consumptionYield)
                    {
                        double kills = Math.Min(preyRemaining, thisChunk * predationYield);
                        preyRemaining -= kills;
                        predated += kills;
                    }
                    else
                    {
                        double eaten = Math.Min(biomassRemaining, thisChunk * consumptionYield);
                        biomassRemaining -= eaten;
                        consumed += eaten;
                    }
                }

                long killed = (long)predated;
                long stripped = (long)consumed;
                long swarmPopBefore = consumer.Population;
                long capacityBefore = region.CarryingCapacity;
                int preyFactionCount = prey.Count;
                long preyBefore = (long)(preyRemaining + predated); // pre-consumption prey headcount
                ApplyPredationKills(prey, killed);
                region.CarryingCapacity = Math.Max(0, region.CarryingCapacity - stripped);
                long converted = (long)((killed + stripped) * BiomassFeedEfficiency);
                consumer.Population += converted;
                GameLog.Trace(() =>
                    $"Biomass consume {DescribeRegionFaction(consumer)}: troops={troops:F0}, "
                    + $"predated={killed} (prey {preyBefore} across {preyFactionCount} factions), "
                    + $"consumed={stripped} (capacity {capacityBefore}->{region.CarryingCapacity}), "
                    + $"converted={converted} (swarmPop {swarmPopBefore}->{consumer.Population})");
            }
        }

        // Distributes predation kills across the region's prey factions in proportion to their share
        // of the surviving headcount, so a region that is 90% one faction / 10% another is culled in
        // that 9:1 ratio (PRD §4.24). The last faction absorbs any rounding remainder.
        private static void ApplyPredationKills(List<RegionFaction> prey, long totalKilled)
        {
            if (totalKilled <= 0) return;
            long preyTotal = prey.Sum(rf => rf.Population);
            if (preyTotal <= 0) return;
            long applied = 0;
            for (int i = 0; i < prey.Count; i++)
            {
                RegionFaction rf = prey[i];
                long share = i == prey.Count - 1
                    ? totalKilled - applied
                    : (long)(totalKilled * (double)rf.Population / preyTotal);
                share = Math.Clamp(share, 0, rf.Population);
                rf.Population -= share;
                applied += share;
            }
        }

        // A degraded region heals a small fraction of the gap back toward its natural ceiling each
        // week. Consumption outpaces this while the swarm feeds; once the swarm is gone the land
        // slowly recovers. A gap that rounds to zero still heals by one, guaranteeing eventual full
        // recovery of a liberated world. PRD §4.24.
        internal static void RecoverCarryingCapacity(Region region)
        {
            if (region.CarryingCapacity >= region.MaximumCarryingCapacity) return;
            long gap = region.MaximumCarryingCapacity - region.CarryingCapacity;
            long recovered = (long)(gap * CarryingCapacityRecoveryRate);
            if (recovered <= 0) recovered = 1;
            long capacityBefore = region.CarryingCapacity;
            region.CarryingCapacity = Math.Min(region.MaximumCarryingCapacity, region.CarryingCapacity + recovered);
            GameLog.Trace(() =>
                $"Capacity recovery {region.Planet.Name}/{region.Name}: "
                + $"{capacityBefore}->{region.CarryingCapacity} (+{region.CarryingCapacity - capacityBefore} "
                + $"toward ceiling {region.MaximumCarryingCapacity})");
        }

        // Genestealer Cult idle-force behavior (PRD §4.24). A public Cult grinds down the PDF through
        // cross-border raids (the offensive machinery); this pass governs the force in regions with no
        // active Imperial enemy left to fight nearby. Such a pocket flows toward an adjacent region
        // nearer the fighting; where the fight is wholly out of reach it falls to sacrificial
        // predation — slaughtering the local Imperial population as offerings to the Star Children.
        // That killing does NOT grow the Cult: conversion (resolved in the growth step) is its only
        // path to numbers.
        internal static void ResolveCultManeuvers(Region region)
        {
            RegionFaction cult = region.RegionFactionMap.Values.FirstOrDefault(
                rf => rf.IsPublic && rf.PlanetFaction.Faction.GrowthType == GrowthType.Conversion);
            if (cult == null || cult.Population <= 0) return;

            int organization = Math.Max(cult.Organization, 0);
            long organized = (long)(cult.Population * (organization / 100.0));
            if (organized <= 0) return;

            // On the front: an active Imperial enemy in this or an adjacent region is engaged by the
            // raid machinery, so leave this pocket's force to that fight.
            if (HasActiveImperialEnemyNearby(region)) return;

            // Idle: flow toward an adjacent region that is itself on the front, consolidating the
            // swarm toward where PDFs remain.
            Region frontward = region.GetAdjacentRegions().FirstOrDefault(HasActiveImperialEnemyNearby);
            if (frontward != null)
            {
                RelocateCultForce(cult, frontward, organized);
                return;
            }

            // Wholly out of reach of the fight: sacrificial predation of the local Imperial population.
            SacrificialCultPredation(region, organized);
        }

        // An active Imperial enemy is a public PDF/player force able to fight back — a standing
        // garrison or landed player squads. A remnant gone to ground (hidden, no garrison) is not one.
        private static bool HasActiveImperialEnemyNearby(Region region)
        {
            return region.GetSelfAndAdjacentRegions().Any(r => r.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic
                && (rf.PlanetFaction.Faction.IsDefaultFaction || rf.PlanetFaction.Faction.IsPlayerFaction)
                && (rf.Garrison > 0 || rf.LandedSquads.Count > 0)));
        }

        // Shifts a share of an idle Cult pocket's mobile force into an adjacent region nearer the
        // fight, establishing or reinforcing the Cult there (its population is its military strength).
        private static void RelocateCultForce(RegionFaction cult, Region destination, long organized)
        {
            long movers = Math.Min(cult.Population, (long)(organized * CultRelocationRate));
            if (movers <= 0) return;
            cult.Population -= movers;

            Faction cultFaction = cult.PlanetFaction.Faction;
            if (!destination.RegionFactionMap.TryGetValue(cultFaction.Id, out RegionFaction destCult))
            {
                if (!destination.Planet.PlanetFactionMap.TryGetValue(cultFaction.Id, out PlanetFaction destPlanetFaction))
                {
                    destPlanetFaction = new PlanetFaction(cultFaction) { IsPublic = true };
                    destination.Planet.PlanetFactionMap[cultFaction.Id] = destPlanetFaction;
                }
                destCult = new RegionFaction(destPlanetFaction, destination)
                {
                    IsPublic = true,
                    Organization = Math.Max(cult.Organization, 0)
                };
                destination.RegionFactionMap[cultFaction.Id] = destCult;
            }
            destCult.IsPublic = true;
            destCult.Population += movers;
        }

        // The Cult slaughters the local Imperial population (hidden remnant and civilians) as
        // sacrifices, using the same logarithmic predation yield as the swarm — but it gains nothing:
        // killing is not the Cult's growth, conversion is (PRD §4.24).
        private static void SacrificialCultPredation(Region region, long organized)
        {
            List<RegionFaction> prey = region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.IsDefaultFaction && rf.Population > 0)
                .ToList();
            double preyRemaining = prey.Sum(rf => rf.Population);
            if (preyRemaining <= 0) return;

            long killed = (long)Math.Min(preyRemaining, organized * PredationMarginalYield(preyRemaining));
            ApplyPredationKills(prey, killed);
            // Deliberately no Cult population gain.
        }

        // Advances the region's default-Imperial faction through the hide/unhide lifecycle (PRD
        // §4.24), evaluated per region rather than at planet scale like the infiltrator revolt path:
        //   Besieged  → Overrun  : a governing remnant whose garrison has fallen to zero with a
        //                          public enemy present goes to ground (IsPublic = false).
        //   Overrun   → Liberated: a hidden remnant surfaces once the region holds no public enemy,
        //                          resuming open governance and rebuilding its garrison over time.
        internal static void UpdateImperialRemnantState(Region region)
        {
            RegionFaction defaultFaction = region.RegionFactionMap.Values
                .FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
            if (defaultFaction == null) return;

            bool publicEnemy = HasPublicEnemy(region);
            if (defaultFaction.IsPublic)
            {
                if (defaultFaction.Garrison <= 0 && publicEnemy)
                {
                    defaultFaction.IsPublic = false;
                }
            }
            else if (!publicEnemy)
            {
                defaultFaction.IsPublic = true;
            }
        }

        // A region holds a public enemy if any non-player, non-default faction is public and still
        // has a presence (population or garrison). The player's own forces do not count — the
        // remnant hides from hostile occupiers, not from the relieving Astartes.
        private static bool HasPublicEnemy(Region region)
        {
            return region.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic
                && !rf.PlanetFaction.Faction.IsPlayerFaction
                && !rf.PlanetFaction.Faction.IsDefaultFaction
                && (rf.Population > 0 || rf.Garrison > 0));
        }

        // Each week a fraction of an overrun (hidden) remnant flees to adjacent regions the Imperium
        // still governs, distributed in proportion to each refuge's population (refugees pour toward
        // the nearest dense fortification). Deliberately unclamped by destination capacity —
        // overfilling a refuge is intended; the crowding term then models the resulting deprivation
        // (PRD §4.24). A fully surrounded remnant (no governed neighbour) cannot flee.
        internal static void ProcessImperialEmigration(Region region)
        {
            RegionFaction remnant = region.RegionFactionMap.Values.FirstOrDefault(
                rf => rf.PlanetFaction.Faction.IsDefaultFaction && !rf.IsPublic);
            if (remnant == null || remnant.Population <= 0) return;

            List<RegionFaction> refuges = region.GetAdjacentRegions()
                .Select(r => r.RegionFactionMap.Values.FirstOrDefault(
                    rf => rf.PlanetFaction.Faction.IsDefaultFaction && rf.IsPublic))
                .Where(rf => rf != null)
                .ToList();
            if (refuges.Count == 0) return;

            long emigrants = (long)(remnant.Population * ImperialEmigrationRate);
            if (emigrants <= 0) return;
            remnant.Population -= emigrants;

            long refugeTotal = refuges.Sum(rf => rf.Population);
            long distributed = 0;
            for (int i = 0; i < refuges.Count; i++)
            {
                // The last refuge absorbs the rounding remainder so the whole cohort is placed.
                long share = i == refuges.Count - 1
                    ? emigrants - distributed
                    : refugeTotal > 0
                        ? (long)(emigrants * (double)refuges[i].Population / refugeTotal)
                        : emigrants / refuges.Count; // even split when every refuge is empty
                refuges[i].Population += share;
                distributed += share;
            }
        }

        // Scales organic population change by a logistic crowding factor (1 - pop/capacity):
        // near-maximal growth when the region is sparsely populated, tapering to zero at
        // capacity, and turning gently negative above capacity so an overfull region drifts
        // back down. A carrying capacity of 0 (or less) is treated as uncapped, leaving the
        // base growth unchanged.
        private static float ApplyCarryingCapacity(float baseGrowth, Region region)
        {
            long capacity = region.CarryingCapacity;
            if (capacity <= 0)
            {
                return baseGrowth;
            }
            // Crowding is measured against the non-consumer population only: Tyranids devour the
            // land's capacity rather than living off it, so their headcount must not inflate the
            // crowding that limits ordinary growth (PRD §4.24).
            float crowding = 1f - (region.NonConsumerPopulation / (float)capacity);
            return baseGrowth * crowding;
        }

        private void UpdateRegionFactionForces(RegionFaction regionFaction, float pdfRatio, float newPop)
        {
            Planet planet = regionFaction.Region.Planet;
            bool isDefaultFaction = regionFaction.PlanetFaction.Faction.IsDefaultFaction;
            bool isPlayerFaction = regionFaction.PlanetFaction.Faction.IsPlayerFaction;
            // An overrun remnant (hidden default faction) has gone to ground: it drafts no garrison,
            // unlike a hidden infiltrator, which still quietly builds one (PRD §4.24).
            bool isOverrunRemnant = isDefaultFaction && !regionFaction.IsPublic;

            if ((isDefaultFaction || isPlayerFaction || !regionFaction.IsPublic) && !isOverrunRemnant)
            {
                // garrison attrition: a fraction of the standing garrison retires each week and
                // must be replaced by fresh recruitment from population growth below
                regionFaction.Garrison -= (long)(regionFaction.Garrison * GarrisonAttritionRate);

                float draftRate = GarrisonDraftRate;
                if (isDefaultFaction && regionFaction.IsPublic && HasPublicNpcFactionInRegion(regionFaction))
                {
                    draftRate = ActiveAssaultGarrisonDraftRate;
                }
                // if the pdf is less than three percent of the population, more people are drafted;
                // additionally, secret factions love to infiltrate the PDF
                else if (pdfRatio < 0.03f || !regionFaction.IsPublic)
                {
                    draftRate = EmergencyGarrisonDraftRate;
                }

                regionFaction.Garrison += (long)(newPop * draftRate);
            }
        }

        private static bool HasPublicNpcFactionInRegion(RegionFaction regionFaction)
        {
            return regionFaction.Region.RegionFactionMap.Values.Any(other =>
                other != regionFaction
                && other.IsPublic
                && !other.PlanetFaction.Faction.IsPlayerFaction
                && !other.PlanetFaction.Faction.IsDefaultFaction);
        }

        private void CheckForPlanetaryRevolt(Planet planet)
        {
            Faction controllingFaction = planet.GetControllingFaction();
            PlanetFaction controllingPlanetFaction = planet.PlanetFactionMap[controllingFaction.Id];
            Faction hiddenFactionType = null;
            PlanetFaction hiddenPlanetFaction = null;

            // Find a hidden faction on the planet (assuming only one hidden faction for now)
            foreach (var planetFaction in planet.PlanetFactionMap.Values)
            {
                if (!planetFaction.IsPublic && !planetFaction.Faction.IsDefaultFaction && !planetFaction.Faction.IsPlayerFaction)
                {
                    hiddenFactionType = planetFaction.Faction;
                    hiddenPlanetFaction = planetFaction;
                    break; // Assuming only one hidden faction per planet for now
                }
            }

            // If no hidden faction, no revolt possible
            if (hiddenPlanetFaction != null)
            {
                // Compare fighting strength, not raw garrison: for a horde faction (a cult) the whole
                // membership rises, so its strength is population (+ its armed cells), whereas the
                // controlling Imperium fields only its PDF garrison (RegionFaction.MilitaryStrength).
                long hiddenFactionStrength = 0;
                long controllingFactionStrength = 0;

                foreach (Region region in planet.Regions)
                {
                    foreach (var regionFaction in region.RegionFactionMap.Values)
                    {
                        if (regionFaction.PlanetFaction == controllingPlanetFaction)
                        {
                            controllingFactionStrength += regionFaction.MilitaryStrength;
                        }
                        else if (regionFaction.PlanetFaction == hiddenPlanetFaction)
                        {
                            hiddenFactionStrength += regionFaction.MilitaryStrength;
                        }
                    }
                }

                if (hiddenFactionStrength > controllingFactionStrength)
                {
                    // Revolt triggers!
                    //context.Log.Add($"{hiddenFactionType.Name} forces trigger planetary revolt on {planet.Name}!");
                    foreach (Region region in planet.Regions)
                    {
                        if (region.RegionFactionMap.ContainsKey(hiddenFactionType.Id))
                        {
                            RegionFaction revoltingRegionFaction = region.RegionFactionMap[hiddenFactionType.Id];
                            revoltingRegionFaction.IsPublic = true;
                            // Going public, the cult's covert armed cells throw off concealment and
                            // join the open rising: fold the standing garrison into the fighting
                            // population, leaving no separate hidden garrison behind.
                            revoltingRegionFaction.Population += revoltingRegionFaction.Garrison;
                            revoltingRegionFaction.Garrison = 0;
                            // if there are any regional defenses, the revolters claim half (plus/minus random roll)
                            if (region.RegionFactionMap.ContainsKey(controllingFaction.Id))
                            {
                                RegionFaction controllingRegionFaction = region.RegionFactionMap[controllingFaction.Id];
                                if (controllingRegionFaction.ListeningPost > 0)
                                {
                                    int revoltShare = controllingRegionFaction.ListeningPost / 2;
                                    revoltShare += (int)RNG.NextRandomZValue();
                                    if (revoltShare > controllingRegionFaction.ListeningPost)
                                    {
                                        revoltShare = controllingRegionFaction.ListeningPost;
                                    }
                                    if (revoltShare < 0)
                                    {
                                        revoltShare = 0;
                                    }
                                    controllingRegionFaction.ListeningPost -= revoltShare;
                                    revoltingRegionFaction.ListeningPost += revoltShare;
                                }
                                if (controllingRegionFaction.AntiAir > 0)
                                {
                                    int revoltShare = controllingRegionFaction.AntiAir / 2;
                                    revoltShare += (int)RNG.NextRandomZValue();
                                    if (revoltShare > controllingRegionFaction.AntiAir)
                                    {
                                        revoltShare = controllingRegionFaction.AntiAir;
                                    }
                                    if (revoltShare < 0)
                                    {
                                        revoltShare = 0;
                                    }
                                    controllingRegionFaction.AntiAir -= revoltShare;
                                    revoltingRegionFaction.AntiAir += revoltShare;
                                }
                                if (controllingRegionFaction.Entrenchment > 0)
                                {
                                    int revoltShare = controllingRegionFaction.Entrenchment / 2;
                                    revoltShare += (int)RNG.NextRandomZValue();
                                    if (revoltShare > controllingRegionFaction.Entrenchment)
                                    {
                                        revoltShare = controllingRegionFaction.Entrenchment;
                                    }
                                    if (revoltShare < 0)
                                    {
                                        revoltShare = 0;
                                    }
                                    controllingRegionFaction.Entrenchment -= revoltShare;
                                    revoltingRegionFaction.Entrenchment += revoltShare;
                                }
                                // also negatively impact controlling faction's Organization
                                controllingRegionFaction.Organization = (int)(RNG.GetLinearDouble() * 100);
                            }
                        }

                    }
                    hiddenPlanetFaction.IsPublic = true; // Make PlanetFaction public as well


                }
            }
        }

        private void CheckForRevoltSuppression(Planet planet)
        {
            // mirror of CheckForPlanetaryRevolt: a faction that has gone public stays in
            // open war until its garrison is beaten well back below the controlling faction's,
            // at which point it retreats into hiding again. The 0.7 factor (vs. the 1.0 revolt
            // threshold) provides hysteresis so a faction doesn't flap between states.
            Faction controllingFaction = planet.GetControllingFaction();
            if (controllingFaction.IsPlayerFaction) return;
            PlanetFaction controllingPlanetFaction = planet.PlanetFactionMap[controllingFaction.Id];

            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
            {
                if (!planetFaction.IsPublic
                    || planetFaction == controllingPlanetFaction
                    || planetFaction.Faction.IsDefaultFaction
                    || planetFaction.Faction.IsPlayerFaction)
                {
                    continue;
                }

                long hostileStrength = SumMilitaryStrength(planet, planetFaction);
                long controllingStrength = SumMilitaryStrength(planet, controllingPlanetFaction);
                if (hostileStrength < 0.7f * controllingStrength)
                {
                    // the revolt has been put down; the faction goes back underground
                    planetFaction.IsPublic = false;
                    foreach (Region region in planet.Regions)
                    {
                        if (region.RegionFactionMap.TryGetValue(planetFaction.Faction.Id, out RegionFaction rf))
                        {
                            rf.IsPublic = false;
                        }
                    }
                }
            }
        }

        private static long SumMilitaryStrength(Planet planet, PlanetFaction planetFaction)
        {
            long strength = 0;
            foreach (Region region in planet.Regions)
            {
                if (region.RegionFactionMap.TryGetValue(planetFaction.Faction.Id, out RegionFaction rf))
                {
                    strength += rf.MilitaryStrength;
                }
            }
            return strength;
        }

        private void EndOfTurnLeaderUpdate(Planet planet, PlanetFaction planetFaction)
        {
            // governors age and eventually die; if this one dies, a successor takes over and
            // the rest of the leader update is skipped this week
            if (AgeAndCheckForDeath(planet, planetFaction))
            {
                return;
            }

            if (planetFaction.Leader.ActiveRequest != null)
            {
                // see if the request has been fulfilled
                if (planetFaction.Leader.ActiveRequest.IsRequestCompleted())
                {
                    // remove the active request
                    GameDataSingleton.Instance.Sector.PlayerForce.Requests.Remove(planetFaction.Leader.ActiveRequest);
                    planetFaction.Leader.ActiveRequest = null;
                    // improve leader opinion of player
                    planetFaction.Leader.OpinionOfPlayerForce +=
                        planetFaction.Leader.Appreciation * (1 - planetFaction.Leader.OpinionOfPlayerForce);
                    // fulfilling the request also earns a Requisition grant (the supply faucet)
                    GameDataSingleton.Instance.Sector.PlayerForce.Army.Requisition += RequisitionPerRequestFulfilled;
                }
                else
                {
                    // decrement the leader's opinion based on the unfulfilled request
                    // the average governor will drop 0.01 opinion per week.
                    planetFaction.Leader.OpinionOfPlayerForce -= 0.005f / planetFaction.Leader.Patience;
                    // TODO: some notion of canceling a request?
                }
            }
            else if (planetFaction.Leader.OpinionOfPlayerForce > 0)
            {
                GenerateRequests(planet, planetFaction);
            }
        }

        private bool AgeAndCheckForDeath(Planet planet, PlanetFaction planetFaction)
        {
            Character leader = planetFaction.Leader;
            // age the governor once per year, at the turn of the year
            if (GameDataSingleton.Instance.Date.Week == 1)
            {
                leader.Age++;
            }

            // weekly death roll: chance rises with age and falls with the planet's importance
            // (more important worlds afford their governors better rejuvenat care).
            float ageFactor = Math.Max(0, leader.Age - 50) / 50f;
            float importanceFactor = 1f - (Math.Min(planet.Importance, 6000) / 12000f);
            float weeklyDeathChance = ageFactor * 0.002f * importanceFactor;
            if (RNG.GetLinearDouble() >= weeklyDeathChance)
            {
                return false;
            }

            // the governor has died; cancel any active request and install a successor.
            // The successor is generated with random traits/opinion for now; tying the
            // starting opinion to the predecessor and sector reputation is deferred (PRD 4.16).
            if (leader.ActiveRequest != null)
            {
                GameDataSingleton.Instance.Sector.PlayerForce.Requests.Remove(leader.ActiveRequest);
                leader.ActiveRequest = null;
            }
            List<Character> characters = GameDataSingleton.Instance.Sector.Characters;
            characters.Remove(leader);
            int newId = characters.Count == 0 ? 0 : characters.Max(c => c.Id) + 1;
            Character successor = CharacterBuilder.GenerateCharacter(newId, planetFaction.Faction);
            characters.Add(successor);
            planetFaction.Leader = successor;
            return true;
        }

        private void GenerateRequests(Planet planet, PlanetFaction planetFaction)
        {
            // Astartes are a strategic asset; governors call on them for open warfare, not for
            // rooting out hidden cults. A request is raised for a faction in open revolt (public).
            Faction threatFaction = FindPublicHostileFaction(planet, planetFaction);
            bool generate = false;

            if (threatFaction != null)
            {
                // Investigation acts as early warning: it gates how quickly the governor
                // recognizes the open threat and decides to act on it.
                if (RNG.GetLinearDouble() < planetFaction.Leader.Investigation)
                {
                    generate = true;
                }
            }
            else
            {
                // no real open threat; a paranoid governor may imagine an invasion (false alarm)
                if (RNG.GetLinearDouble() < planetFaction.Leader.Paranoia)
                {
                    generate = true;
                }
            }

            if (generate)
            {
                // determine if the leader actually wants to call on the player
                float chance = planetFaction.Leader.Neediness * planetFaction.Leader.OpinionOfPlayerForce;
                if (RNG.GetLinearDouble() < chance)
                {
                    IRequest request = RequestFactory.Instance.GenerateNewRequest(
                        planet, planetFaction.Leader, threatFaction, GameDataSingleton.Instance.Date);
                    planetFaction.Leader.ActiveRequest = request;
                    GameDataSingleton.Instance.Sector.PlayerForce.Requests.Add(request);
                }
            }
        }

        private static Faction FindPublicHostileFaction(Planet planet, PlanetFaction planetFaction)
        {
            foreach (PlanetFaction other in planet.PlanetFactionMap.Values)
            {
                if (other.Faction.Id != planetFaction.Faction.Id
                    && other.IsPublic
                    && !other.Faction.IsDefaultFaction
                    && !other.Faction.IsPlayerFaction)
                {
                    return other.Faction;
                }
            }
            return null;
        }

        private float ConvertPopulation(Region region, RegionFaction regionFaction, float newPop)
        {
            RegionFaction defaultFaction = region.RegionFactionMap.Values.First(pf => pf.PlanetFaction.Faction.IsDefaultFaction);
            // converting factions always convert one new member per week
            if (defaultFaction?.Population > 0)
            {
                defaultFaction.Population--;
                regionFaction.Population++;
                float pdfChance = (float)defaultFaction.Garrison / defaultFaction.Population;
                if (RNG.GetLinearDouble() < pdfChance)
                {
                    defaultFaction.Garrison--;
                    regionFaction.Garrison++;
                }
                if (regionFaction.Population > 100)
                {
                    // at larger sizes, converting factions
                    // also grow organically 
                    // at a much faster rate than a normal population
                    newPop = regionFaction.Population * 0.002f;
                }
            }

            return newPop;
        }

        private void UpdateIntelligence(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                UpdateIntelligence(planet);
            }
        }

        private void UpdateIntelligence(Planet planet)
        {
            {
                foreach (Region region in planet.Regions)
                {
                    // 25% chance of unexecuted special missions being removed
                    // snapshot the list: expired missions are removed from it below
                    foreach (Mission mission in region.SpecialMissions.ToList())
                    {
                        if (RNG.GetIntBelowMax(0, 4) == 0)
                        {
                            // TODO: add to the end of turn log that the intelligence grew stale
                            region.SpecialMissions.Remove(mission);
                        }
                    }
                    float visibleIntel = region.GetPlayerVisibleIntel();
                    if (visibleIntel > 0)
                    {
                        foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                        {
                            if (regionFaction.PlanetFaction.Faction.IsPlayerFaction || regionFaction.PlanetFaction.Faction.IsDefaultFaction)
                            {
                                continue;
                            }
                            if (regionFaction.IsPublic)
                            {
                                HandlePublicFactionIntelligence(regionFaction);
                            }
                            else
                            {
                                HandleHiddenFactionIntelligence(regionFaction);
                            }
                        }
                    }
                }
            }
        }

        public void HandlePublicFactionIntelligence(RegionFaction enemyRegionFaction)
        {
            // see if any intelligence gets spent in exchange for special mission opportunities
            float specMissionChance = (float)Math.Log(enemyRegionFaction.Region.GetPlayerVisibleIntel(), 2) + 1;
            // subtract one for each special mission already identified
            specMissionChance -= enemyRegionFaction.Region.SpecialMissions.Count;
            for (int i = 0; i < specMissionChance; i++)
            {
                double chance = RNG.NextRandomZValue();
                if (chance >= 2)
                {
                    GenerateAssassinationMission(enemyRegionFaction);
                }
                else if (chance >= 1)
                {
                    // sabotage
                    // add up the amount of entrenchment, detection, and antiair in this region
                    int defenseTotal = enemyRegionFaction.Entrenchment + enemyRegionFaction.ListeningPost + enemyRegionFaction.AntiAir;
                    if (defenseTotal == 0)
                    {
                        GenerateAmbushMission(enemyRegionFaction);
                    }
                    else
                    {
                        GenerateSabotageMission(enemyRegionFaction, defenseTotal);
                    }
                }
                else if (chance >= 0)
                {
                    GenerateAmbushMission(enemyRegionFaction);

                }
            }
        }

        public void HandleHiddenFactionIntelligence(RegionFaction enemyRegionFaction)
        {
            // determine whether the faction can hide among the population. The ratio can reach (or
            // exceed) 1 when a hidden faction has grown to dominate a region's populace, so clamp it
            // to the open (0,1) interval the inverse-normal CDF requires — a faction that is nearly
            // the whole population is effectively impossible to keep concealed (large positive zScore).
            long regionPopulation = Math.Max(1, enemyRegionFaction.Region.Population);
            float popRatio = Math.Clamp(
                (float)enemyRegionFaction.Population / regionPopulation, 0.0001f, 0.9999f);
            float zScore = GaussianCalculator.ApproximateInverseNormalCDF(popRatio);
            zScore += enemyRegionFaction.Region.GetPlayerVisibleIntel() / 10.0f;
            double chance = RNG.NextRandomZValue();
            if (chance < zScore)
            {
                int size = Math.Max((int)(zScore - chance), 1);
                // found a hidden faction cell
                enemyRegionFaction.Region.SpecialMissions.Add(new Mission(MissionType.Extermination, enemyRegionFaction, size));
            }
        }

        private void GenerateAmbushMission(RegionFaction enemyRegionFaction)
        {
            //make it an ambush, instead
            double maxSize = Math.Log10(enemyRegionFaction.Garrison);
            int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), (int)maxSize);
            Mission ambush = new Mission(MissionType.Ambush, enemyRegionFaction, size);
            enemyRegionFaction.Region.SpecialMissions.Add(ambush);
            SpecialMissions.Add(ambush);
        }

        private void GenerateSabotageMission(RegionFaction enemyRegionFaction, int defenseTotal)
        {
            int roll = RNG.GetIntBelowMax(0, defenseTotal);
            if (roll <= enemyRegionFaction.Entrenchment)
            {
                // saborage the entrenchments
                int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), enemyRegionFaction.Entrenchment);
                SabotageMission sabotage = new SabotageMission(DefenseType.Entrenchment, size, enemyRegionFaction);
                enemyRegionFaction.Region.SpecialMissions.Add(sabotage);
                SpecialMissions.Add(sabotage);
            }
            else
            {
                roll -= enemyRegionFaction.Entrenchment;
                if (roll <= enemyRegionFaction.ListeningPost)
                {
                    // sabotage the listening posts
                    int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), enemyRegionFaction.ListeningPost);
                    SabotageMission sabotage = new SabotageMission(DefenseType.ListeningPost, size, enemyRegionFaction);
                    enemyRegionFaction.Region.SpecialMissions.Add(sabotage);
                    SpecialMissions.Add(sabotage);
                }
                else
                {
                    // sabotage the antiair
                    int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), enemyRegionFaction.AntiAir);
                    SabotageMission sabotage = new SabotageMission(DefenseType.AntiAir, size, enemyRegionFaction);
                    enemyRegionFaction.Region.SpecialMissions.Add(sabotage);
                    SpecialMissions.Add(sabotage);
                }
            }
        }

        private void GenerateAssassinationMission(RegionFaction enemyRegionFaction)
        {
            // assassination
            // assume that each degree of magnitude of population increases the "size" of the highest leader
            // for example, with Tyranids, this could be
            // 1-10: Prime
            // 11-100: Broodlord
            // 101-1000: Zoenthope?
            // 1001-10000: Hive Tyrant
            int max = (int)Math.Log10(enemyRegionFaction.Population);
            int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), max);
            Mission ass = new Mission(MissionType.Assassination, enemyRegionFaction, size);
            enemyRegionFaction.Region.SpecialMissions.Add(ass);
            SpecialMissions.Add(ass);
        }

    }
}
