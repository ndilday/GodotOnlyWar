using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
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
        // divisor converting a fortifying squad's summed engineering skill into a per-turn
        // defensive increment; tuned so a full, trained squad raises a defense by ~1-2/turn
        private const float EngineeringBuildDivisor = 100f;
        // Superlinear scale for how convincing a diversion feint is. The apparent size of the
        // feinting force grows with the square of (1 + impact / this), so a high-margin
        // demonstration can make a force look several times larger than it is while a weak one
        // barely exceeds its real strength. Larger values make feints harder to sell.
        private const float DiversionThreatScale = 4.0f;

        public TurnController() : this(null)
        {
        }

        public TurnController(ISoldierTrainingService trainingService)
        {
            MissionContexts = new List<MissionContext>();
            SpecialMissions = new List<Mission>();
            _npcStrategyController = new FactionStrategyController();
            _trainingService = trainingService;
        }

        public void ProcessTurn(Sector sector)
        {
            MissionContexts.Clear();
            SpecialMissions.Clear();

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

            // The diversion effect is consumed entirely by the planning above; clear it so it
            // never lingers past the turn that produced it.
            ClearDiversionEffects(sector.Planets.Values);

            // --- 2. Mission Execution Phase ---
            var combatOrders = allOrdersThisTurn.Where(o => o.AssignedSquads.Any());
            ProcessCombatMissions(combatOrders);

            var constructionOrders = allOrdersThisTurn.Where(o => !o.AssignedSquads.Any());
            ProcessConstructionOrders(constructionOrders);

            // --- 3. Planetary Simulation & Resolution Phase ---
            ApplyMissionResults();
            TrainNonDeployedPlayerForces(sector);
            AdvanceFleetMovement(sector);
            UpdatePlanets(sector.Planets.Values);
            UpdateIntelligence(sector.Planets.Values);
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
                    trainingService.ApplySoldierWorkExperience(soldier, WeeklyTrainingPoints);
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
                    trainingService.ApplySoldierWorkExperience(soldier, points);
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

        private void ProcessCombatMissions(IEnumerable<Order> combatOrders)
        {
            foreach (Order order in combatOrders)
            {
                if(order.Mission.MissionType == MissionType.DefenseInDepth) continue;
                // Diversions already resolved in the pre-planning shaping phase; their squads
                // remain on the map only to defend (see AssembleDefendingForce) if the feint
                // draws a counterattack this turn.
                if(order.Mission.MissionType == MissionType.Diversion) continue;
                // TODO: decide how to handle patrol orders

                // A construction order with an assigned squad is the player (or any faction)
                // fortifying a region: the squad spends the turn building rather than fighting.
                if (order.Mission is ConstructionMission constructionMission)
                {
                    ResolveSquadConstruction(order, constructionMission);
                    continue;
                }

                bool isPlayerOrder = order.AssignedSquads.First().Faction.IsPlayerFaction;

                List<BattleSquad> involvedBattleSquads = order.AssignedSquads
                                                              .Select(s => new BattleSquad(isPlayerOrder, s))
                                                              .ToList();

                MissionContext context = new MissionContext(order, involvedBattleSquads, new List<BattleSquad>());
                MissionStepOrchestrator.GetStartingStep(context).ExecuteMissionStep(context, 0, null);
                MissionContexts.Add(context);
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
                List<BattleSquad> involvedBattleSquads = order.AssignedSquads
                                                              .Select(s => new BattleSquad(isPlayerOrder, s))
                                                              .ToList();

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
            foreach (var order in constructionOrders)
            {
                if (order.Mission is ConstructionMission mission)
                {
                    ApplyConstruction(mission, mission.MissionSize);
                }
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

        private static void ApplyConstruction(ConstructionMission mission, int amount)
        {
            switch (mission.ConstructionType)
            {
                case DefenseType.Entrenchment:
                    mission.RegionFaction.Entrenchment += amount;
                    break;
                case DefenseType.Detection:
                    mission.RegionFaction.Detection += amount;
                    break;
                case DefenseType.AntiAir:
                    mission.RegionFaction.AntiAir += amount;
                    break;
                case DefenseType.Organization:
                    mission.RegionFaction.Organization = Math.Min(100, mission.RegionFaction.Organization + amount);
                    break;
            }
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
                        context.Order.Mission.RegionFaction.Region.IntelligenceLevel += context.Impact;
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
                            case DefenseType.Detection:
                                regionFaction.Detection -= impact;
                                if (regionFaction.Detection < 0)
                                {
                                    regionFaction.Detection = 0;
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
                int enemiesKilled = context.EnemiesKilled;
                if (regionFaction.Entrenchment > 0)
                {
                    float ratio = 1.0f - Math.Min(regionFaction.Entrenchment / 10.0f, 1.0f);
                    enemiesKilled = (int)(enemiesKilled * ratio);
                }
                regionFaction.Population -= enemiesKilled;
                if (regionFaction.Population < 0)
                {
                    regionFaction.Population = 0;
                }
            }
        }

        private void UpdatePlanets(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
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
            }
        }

        private void EndOfTurnRegionFactionsUpdate(RegionFaction regionFaction, float pdfRatio)
        {
            Planet planet = regionFaction.Region.Planet;
            Faction controllingFaction = planet.GetControllingFaction();
            float newPop = 0;
            switch (regionFaction.PlanetFaction.Faction.GrowthType)
            {
                case GrowthType.Logistic:
                    newPop = ApplyCarryingCapacity(regionFaction.Population * LogisticGrowthRate, regionFaction.Region);
                    break;
                case GrowthType.Conversion:
                    newPop = ConvertPopulation(regionFaction.Region, regionFaction, newPop);
                    if (regionFaction.PlanetFaction.Faction.Id != controllingFaction.Id &&
                        planet.PlanetFactionMap[controllingFaction.Id].Leader != null)
                    {
                        // TODO: see if the governor notices the converted population
                    }
                    break;
                default:
                    newPop = ApplyCarryingCapacity(regionFaction.Population * BaselineGrowthRate, regionFaction.Region);
                    break;
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
            float crowding = 1f - (region.Population / (float)capacity);
            return baseGrowth * crowding;
        }

        private void UpdateRegionFactionForces(RegionFaction regionFaction, float pdfRatio, float newPop)
        {
            Planet planet = regionFaction.Region.Planet;
            bool isDefaultFaction = regionFaction.PlanetFaction.Faction.IsDefaultFaction;
            bool isPlayerFaction = regionFaction.PlanetFaction.Faction.IsPlayerFaction;

            if (isDefaultFaction || isPlayerFaction || !regionFaction.IsPublic)
            {
                // garrison attrition: a fraction of the standing garrison retires each week and
                // must be replaced by fresh recruitment from population growth below
                regionFaction.Garrison -= (long)(regionFaction.Garrison * GarrisonAttritionRate);

                // if the pdf is less than three percent of the population, more people are drafted
                // additionally, secret factions love to infiltrate the PDF
                if (pdfRatio < 0.03f || !regionFaction.IsPublic)
                {
                    regionFaction.Garrison += (long)(newPop * 0.05f);
                }
                else
                {
                    regionFaction.Garrison += (long)(newPop * 0.025f);
                }
            }
            if (planet.IsUnderAssault())
            {
                // TODO: generalize this so that Imperial PDFs can build defenses as well
                if (regionFaction.Organization == -1)
                {
                    // initialize the region faction
                    regionFaction.Organization = 100;
                    regionFaction.Detection = 1;
                    regionFaction.Entrenchment = 1;
                    regionFaction.AntiAir = 1;
                }
            }
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
                long hiddenFactionGarrison = 0;
                long hiddenFactionPopulation = 0;
                long controllingFactionGarrison = 0;
                long controllingFactionPopulation = 0;

                foreach (Region region in planet.Regions)
                {
                    foreach (var regionFaction in region.RegionFactionMap.Values)
                    {
                        if (regionFaction.PlanetFaction == controllingPlanetFaction)
                        {
                            controllingFactionGarrison += regionFaction.Garrison;
                            controllingFactionPopulation += regionFaction.Population;
                        }
                        else if (regionFaction.PlanetFaction == hiddenPlanetFaction)
                        {
                            hiddenFactionGarrison += regionFaction.Garrison;
                            hiddenFactionPopulation += regionFaction.Population;
                        }
                    }
                }

                if (hiddenFactionGarrison > controllingFactionGarrison)
                {
                    // Revolt triggers!
                    //context.Log.Add($"{hiddenFactionType.Name} forces trigger planetary revolt on {planet.Name}!");
                    foreach (Region region in planet.Regions)
                    {
                        if (region.RegionFactionMap.ContainsKey(hiddenFactionType.Id))
                        {
                            RegionFaction revoltingRegionFaction = region.RegionFactionMap[hiddenFactionType.Id];
                            revoltingRegionFaction.IsPublic = true;
                            // if there are any regional defenses, the revolters claim half (plus/minus random roll)
                            if (region.RegionFactionMap.ContainsKey(controllingFaction.Id))
                            {
                                RegionFaction controllingRegionFaction = region.RegionFactionMap[controllingFaction.Id];
                                if (controllingRegionFaction.Detection > 0)
                                {
                                    int revoltShare = controllingRegionFaction.Detection / 2;
                                    revoltShare += (int)RNG.NextRandomZValue();
                                    if (revoltShare > controllingRegionFaction.Detection)
                                    {
                                        revoltShare = controllingRegionFaction.Detection;
                                    }
                                    if (revoltShare < 0)
                                    {
                                        revoltShare = 0;
                                    }
                                    controllingRegionFaction.Detection -= revoltShare;
                                    revoltingRegionFaction.Detection += revoltShare;
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

                long hostileGarrison = SumGarrison(planet, planetFaction);
                long controllingGarrison = SumGarrison(planet, controllingPlanetFaction);
                if (hostileGarrison < 0.7f * controllingGarrison)
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

        private static long SumGarrison(Planet planet, PlanetFaction planetFaction)
        {
            long garrison = 0;
            foreach (Region region in planet.Regions)
            {
                if (region.RegionFactionMap.TryGetValue(planetFaction.Faction.Id, out RegionFaction rf))
                {
                    garrison += rf.Garrison;
                }
            }
            return garrison;
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
                    if (region.IntelligenceLevel > 0)
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

                        // reduce intelligence level by 25%
                        region.IntelligenceLevel *= 0.75f;
                    }
                }
            }
        }

        public void HandlePublicFactionIntelligence(RegionFaction enemyRegionFaction)
        {
            // see if any intelligence gets spent in exchange for special mission opportunities
            float specMissionChance = (float)Math.Log(enemyRegionFaction.Region.IntelligenceLevel, 2) + 1;
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
                    int defenseTotal = enemyRegionFaction.Entrenchment + enemyRegionFaction.Detection + enemyRegionFaction.AntiAir;
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
            // determine whether the faction can hide among the population
            float popRatio = (float)enemyRegionFaction.Population / (float)enemyRegionFaction.Region.Population;
            float zScore = GaussianCalculator.ApproximateInverseNormalCDF(popRatio);
            zScore += enemyRegionFaction.Region.IntelligenceLevel / 10.0f;
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
                if (roll <= enemyRegionFaction.Detection)
                {
                    // sabotage the detection
                    int size = Math.Min(Math.Max((int)RNG.NextRandomZValue() + 1, 1), enemyRegionFaction.Detection);
                    SabotageMission sabotage = new SabotageMission(DefenseType.Detection, size, enemyRegionFaction);
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
