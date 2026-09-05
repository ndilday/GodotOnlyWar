using System.Collections.Generic;
using OnlyWar.Models;
using OnlyWar.Models.Events;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Units;
using OnlyWar.Helpers;

namespace OnlyWar.Contracts.Generation;

/// <summary>
/// Tokens consumed by the opening-scenario briefing composer. All values are resolved by
/// Generation from the candidate sector: the chapter, the promised world and its subsector, the
/// invading faction, and the promising authority. TemplateSelector picks one of the deterministic
/// templates; Generation passes a stable per-seed value (the promised planet's id) so seed plus
/// scenario reproduces the same briefing. The composition itself is narrative policy and lives
/// with its owner behind <see cref="IGenerationNarrativePort"/>.
/// </summary>
public readonly struct BriefingTokens
{
    public string ChapterName { get; init; }
    public string PlanetName { get; init; }
    public string SubsectorName { get; init; }
    public string AuthorityName { get; init; }
    public string AuthorityTitle { get; init; }
    public string EnemyName { get; init; }
    public int TemplateSelector { get; init; }
}

/// <summary>
/// Latent-population and invasion seeding Generation asks for but does not own. The capability
/// rules behind these live with the faction owner; Generation only says when they apply during the
/// authored opening (SB-09).
/// </summary>
public interface IGenerationSeedingPort
{
    /// <summary>Seeds ambient ghost populations onto genuinely empty grid tiles.</summary>
    void SeedGhostPopulations(Sector sector, GameRulesData rules);

    /// <summary>
    /// Flips a hidden regional presence into open revolt through the same transition the campaign
    /// uses, so the reveal strips embedded personnel and picks up emergence advantage.
    /// </summary>
    void RevealRegionFaction(RegionFaction regionFaction);

    /// <summary>Establishes the authored beachhead as a persistent invasion force.</summary>
    void EstablishOpeningInvasion(Sector sector, Planet planet, Faction invader);
}

/// <summary>Narrative policy Generation calls for the founding briefing and chronicle.</summary>
public interface IGenerationNarrativePort
{
    string GetAuthorityTitle(GovernanceTier tier);

    /// <summary>Composes the founding directive; <paramref name="invasion"/> selects the variant.</summary>
    string ComposeOpeningBriefing(BriefingTokens tokens, bool invasion);

    /// <summary>
    /// Opens the campaign event recorder for a newly built force, so events raised during the rest
    /// of generation are captured. Recorder lifetime is campaign event policy, not generation.
    /// </summary>
    void AttachEventRecorder(PlayerForce playerForce);

    /// <summary>Records the founding of the chapter as a campaign event.</summary>
    void RecordChapterFounding(
        PlayerForce playerForce,
        Date date,
        ChapterFoundedPayload payload,
        int? chapterMasterId,
        string chapterMasterName,
        int promisedPlanetId,
        string promisedPlanetName);

    /// <summary>Projects the founding events recorded during generation into the chronicle.</summary>
    void ReconcileChronicle(PlayerForce playerForce);
}

/// <summary>
/// Fleet and duty-station capabilities Generation needs to park the founding chapter in orbit.
/// Owned by the campaign fleet/personnel services.
/// </summary>
public interface IGenerationFleetPort
{
    Ship SelectInitialFlagship(Faction faction, IReadOnlyList<Ship> ships);

    /// <summary>
    /// Seats every administrative formation at its duty station. Throws
    /// <see cref="System.InvalidOperationException"/> if the chapter cannot be seated, which fails
    /// the candidate rather than publishing a half-stationed force.
    /// </summary>
    void SeatAdministrativeFormations(Unit orderOfBattle, Ship flagship);
}

/// <summary>
/// Founding role assignment. Ranking soldiers against chapter roles is personnel policy; Generation
/// consumes it rather than owning it (SB-09, plan §3.2).
/// </summary>
public interface IFoundingRoleAdvisor
{
    /// <summary>
    /// Ranks the founding intake for every role, best first. Generation consumes the lists in
    /// preference order (demand → consume; Design/Reference/FoundingRoleAssignment.md), so the
    /// same soldier legitimately appears on several lists until one of them claims him.
    /// </summary>
    IReadOnlyDictionary<FoundingRole, List<PlayerSoldier>> BuildCandidateLists(
        IReadOnlyList<PlayerSoldier> soldiers,
        RatingConsumerBindings ratingBindings);
}

/// <summary>
/// Runs the authored opening's planet-scoped warm-up against the candidate campaign. Implemented
/// outside Generation so the generator never references turn simulation, which is what keeps the
/// two acyclic (plan §3.1). The implementation builds a session over the candidate sector; it never
/// publishes it, advances the campaign date, or runs player upkeep, fleet travel or scenario
/// resolution.
/// </summary>
public interface ICandidateWarmupSimulator
{
    /// <summary>
    /// Opens the candidate session that every warm-up pass for this sector will share. Generation
    /// calls it once, at the point in the authored sequence where the simulation becomes live, so
    /// the passes see the same planning and intelligence state they would have inside one turn
    /// controller.
    /// </summary>
    void BeginCandidate(Sector sector, GameRulesData rules, Date currentDate);

    void SimulatePlanetForward(Sector sector, Planet planet, int turns);
}

/// <summary>The support services one generation run is given.</summary>
public sealed record GenerationSupport(
    IGenerationSeedingPort Seeding,
    IGenerationNarrativePort Narrative,
    IGenerationFleetPort Fleet,
    IFoundingRoleAdvisor FoundingRoles,
    ICandidateWarmupSimulator Warmup,
    ISoldierTrainingService Training);
