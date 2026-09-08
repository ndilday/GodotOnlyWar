using System;
using System.Collections.Generic;
using OnlyWar.Application;
using OnlyWar.Generation.Contracts;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Operations.Contracts;
using OnlyWar.Helpers.Narrative;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Events;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Units;

namespace OnlyWar.Helpers.Application.Adapters.Generation
{
    /// <summary>
    /// Application-side implementations of the generation ports (plan §3.4). They live outside
    /// OnlyWar.Generation so the generator never references campaign policy or turn simulation,
    /// which is what keeps generation and simulation acyclic. SB-10 moves this file into the
    /// Application assembly with the same dependency direction.
    /// </summary>
    public static class CandidateGenerationSupport
    {
        /// <summary>
        /// Builds the support bundle for one generation run. Everything it composes operates on the
        /// candidate sector passed to each call; nothing here reads or installs an active campaign.
        /// </summary>
        public static GenerationSupport For(
            GameRulesData rules,
            Date currentDate,
            IRNG random,
            IReadinessDecisions readiness,
            IOperationsPersonnelSurface personnel,
            IOrderCommitmentSurface commitments,
            BattleServices battle)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (currentDate == null) throw new ArgumentNullException(nameof(currentDate));
            if (readiness == null) throw new ArgumentNullException(nameof(readiness));
            if (personnel == null) throw new ArgumentNullException(nameof(personnel));
            if (commitments == null) throw new ArgumentNullException(nameof(commitments));
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            return new GenerationSupport(
                new CampaignGenerationSeedingAdapter(rules, currentDate, random),
                new CampaignGenerationNarrativeAdapter(),
                new CampaignGenerationFleetAdapter(personnel, commitments),
                new FoundingRoleAdvisorAdapter(),
                new CandidateSessionWarmupSimulator(
                    random, readiness, personnel, commitments, battle),
                BuildTrainingService(rules, random));
        }

        /// <summary>
        /// The training/rating policy a founding is evaluated against. Constructing it is campaign
        /// personnel policy, not generation, so it is composed here and handed over as a contract.
        /// </summary>
        public static ISoldierTrainingService BuildTrainingService(GameRulesData rules, IRNG random)
        {
            RatingCalculator ratingCalculator = new(
                rules.RatingDefinitions, rules.RatingAwardTiers, rules.BaseSkillMap, random);
            return new SoldierTrainingCalculator(
                rules.BaseSkillMap.Values,
                rules.TrainingProfiles.Values,
                ratingCalculator,
                rules.Skills,
                rules.ScoutTrainingOptions.Options);
        }
    }

    public sealed class CampaignGenerationSeedingAdapter : IGenerationSeedingPort
    {
        private readonly GameRulesData _rules;
        private readonly Date _currentDate;
        private readonly IRNG _random;

        public CampaignGenerationSeedingAdapter(GameRulesData rules, Date currentDate, IRNG random)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _currentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public void SeedGhostPopulations(Sector sector, GameRulesData rules) =>
            GhostPlanetSeeder.Seed(sector, rules, _random);

        public void RevealRegionFaction(RegionFaction regionFaction) =>
            FactionRevealService.Reveal(regionFaction);

        public void EstablishOpeningInvasion(Sector sector, Planet planet, Faction invader) =>
            new FactionCapabilityCampaignProcessor(
                    new GameSession(_rules, sector, _currentDate, _random))
                .EstablishOpeningInvasion(sector, planet, invader);
    }

    public sealed class CampaignGenerationNarrativeAdapter : IGenerationNarrativePort
    {
        public string GetAuthorityTitle(GovernanceTier tier) =>
            BriefingComposer.GetAuthorityTitle(tier);

        public string ComposeOpeningBriefing(BriefingTokens tokens, bool invasion) =>
            invasion
                ? BriefingComposer.ComposeInvasionPromisedWorldBriefing(tokens)
                : BriefingComposer.ComposePromisedWorldBriefing(tokens);

        public void AttachEventRecorder(PlayerForce playerForce) =>
            playerForce?.GetCampaignEventRecorder();

        public void RecordChapterFounding(
            PlayerForce playerForce,
            Date date,
            ChapterFoundedPayload payload,
            int? chapterMasterId,
            string chapterMasterName,
            int promisedPlanetId,
            string promisedPlanetName) =>
            playerForce.RecordChapterFounded(
                date, payload, chapterMasterId, chapterMasterName,
                promisedPlanetId, promisedPlanetName);

        public void ReconcileChronicle(PlayerForce playerForce) =>
            ChapterChronicleProjector.Reconcile(
                playerForce?.CampaignEventLedger,
                playerForce?.ChapterChronicle,
                playerForce?.CampaignIdentity);
    }

    public sealed class CampaignGenerationFleetAdapter : IGenerationFleetPort
    {
        private readonly IOperationsPersonnelSurface _personnel;
        private readonly IOrderCommitmentSurface _commitments;

        public CampaignGenerationFleetAdapter(
            IOperationsPersonnelSurface personnel,
            IOrderCommitmentSurface commitments)
        {
            _personnel = personnel ?? throw new ArgumentNullException(nameof(personnel));
            _commitments = commitments ?? throw new ArgumentNullException(nameof(commitments));
        }

        public Ship SelectInitialFlagship(Faction faction, IReadOnlyList<Ship> ships) =>
            new FlagshipService().SelectInitialFlagship(faction, ships);

        public void SeatAdministrativeFormations(Unit orderOfBattle, Ship flagship)
        {
            AdministrativeStationResult result =
                new AdministrativeStationService(_personnel, _commitments)
                    .SeatAll(orderOfBattle, flagship);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Unable to seat Chapter administrative formations: {result.Message}");
            }
        }
    }

    public sealed class FoundingRoleAdvisorAdapter : IFoundingRoleAdvisor
    {
        public IReadOnlyDictionary<FoundingRole, List<PlayerSoldier>> BuildCandidateLists(
            IReadOnlyList<PlayerSoldier> soldiers,
            RatingConsumerBindings ratingBindings)
        {
            RoleSuitabilityService suitability =
                new(new List<PlayerSoldier>(soldiers), ratingBindings);
            Dictionary<FoundingRole, List<PlayerSoldier>> lists = new();
            foreach (FoundingRole role in Enum.GetValues<FoundingRole>())
            {
                lists[role] = suitability.CreateCandidateList(role);
            }
            return lists;
        }
    }

    /// <summary>
    /// Runs the authored opening's planet warm-up on a turn controller built over the candidate
    /// sector. The candidate is never published, the campaign date is never advanced, and player
    /// upkeep, fleet travel and scenario resolution never run — SimulatePlanetForward is the
    /// planet-scoped subset only.
    /// </summary>
    public sealed class CandidateSessionWarmupSimulator : ICandidateWarmupSimulator
    {
        private readonly IRNG _random;
        private readonly IReadinessDecisions _readiness;
        private readonly IOperationsPersonnelSurface _personnel;
        private readonly IOrderCommitmentSurface _commitments;
        private readonly BattleServices _battle;
        private TurnController _controller;

        public CandidateSessionWarmupSimulator(
            IRNG random,
            IReadinessDecisions readiness,
            IOperationsPersonnelSurface personnel,
            IOrderCommitmentSurface commitments,
            BattleServices battle)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _personnel = personnel ?? throw new ArgumentNullException(nameof(personnel));
            _commitments = commitments ?? throw new ArgumentNullException(nameof(commitments));
            _battle = battle ?? throw new ArgumentNullException(nameof(battle));
        }

        // One controller drives every warm-up pass for a candidate, as it did when the generator
        // constructed it directly. The pre- and post-landing sims therefore still share a turn
        // intelligence ledger and planning state; building a fresh controller per pass would
        // silently reset both between them. Constructing the session here also keeps the campaign
        // event bindings attaching at exactly the point in the authored sequence they used to.
        public void BeginCandidate(Sector sector, GameRulesData rules, Date currentDate) =>
            _controller = new TurnController(
                new GameSession(rules, sector, currentDate, _random),
                _readiness,
                _personnel,
                _commitments,
                _battle);

        public void SimulatePlanetForward(Sector sector, Planet planet, int turns)
        {
            if (_controller == null)
            {
                throw new InvalidOperationException(
                    "BeginCandidate must open the candidate session before warm-up runs.");
            }
            _controller.SimulatePlanetForward(sector, planet, turns);
        }
    }
}
