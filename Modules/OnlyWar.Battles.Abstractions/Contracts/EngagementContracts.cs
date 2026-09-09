using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles.Abstractions;

/// <summary>
/// Opaque state owned by the tactical implementation for one engagement element. Operations may
/// carry the handle across mission stages, but cannot inspect or mutate tactical state through it.
/// </summary>
public interface IEngagementState
{
}

 

/// <summary>Opaque replay retained for the host until the UI projection boundary is migrated.</summary>
public interface IBattleReplay
{
}

public enum EngagementRole
{
    Attacker = 0,
    Defender = 1,
    Ambusher = 2,
    Ambushed = 3,
    AssassinationAttacker = 4
}

public enum EngagementPlacement
{
    Meeting = 0,
    FirstSideAmbushed = 1,
    SecondSideAmbushed = 2
}

public enum EngagementBurrowSide
{
    None = 0,
    First = 1,
    Second = 2,
    Both = 3
}

public enum EngagementSide
{
    First = 0,
    Second = 1
}

public enum EngagementEndReason
{
    Annihilation = 0,
    Withdrawal = 1,
    Rout = 2,
    MutualDisengagement = 3,
    TurnCap = 4
}

public sealed record EngagementSideProfile(
    Aggression Aggression,
    EngagementRole Role);

/// <summary>
/// Tactical metadata supplied at the campaign-to-engagement boundary. These are facts, not a
/// reference to a BattleSquad or another Battles implementation type.
/// </summary>
public sealed record EngagementElementTraits(
    bool ProvidesCommandAura = false,
    bool ProvidesSynapse = false,
    bool IsHeadquarters = false);

/// <summary>Detached affiliation facts carried with an engagement participant.</summary>
public sealed record EngagementFactionFacts(
    int Id,
    string Name,
    bool IsPlayerFaction,
    bool IsDefaultFaction);

/// <summary>Detached region identity used to resolve the tactical location at composition time.</summary>
public sealed record EngagementLocation(
    int RegionId,
    string RegionName,
    string PlanetName);

/// <summary>
/// A frozen participant set for one engagement element. Members are the explicit mutable combat
/// graph needed by Battles to apply wounds; no Campaign aggregate (squad, order, region, or
/// player-character wrapper) crosses this boundary. The selected IDs are frozen for the duration
/// of this engagement and are re-evaluated by Operations before the next one.
/// </summary>
public sealed record EngagementParticipant(
    int TacticalId,
    string Name,
    EngagementFactionFacts Faction,
    IReadOnlyList<ISoldier> Members,
    IReadOnlyList<int> FrozenParticipantIds = null,
    bool IsPlayerSquad = false,
    EngagementElementTraits Traits = null,
    IEngagementState State = null)
{
    public IReadOnlyList<ISoldier> EffectiveMembers => Members ?? Array.Empty<ISoldier>();

    public IReadOnlyList<int> EffectiveFrozenParticipantIds =>
        FrozenParticipantIds ?? EffectiveMembers.Select(member => member.Id).ToArray();
}

/// <summary>
/// A complete tactical request. Location is a detached reference; the Application composition
/// layer resolves it to the live Region immediately before invoking the Battles implementation.
/// </summary>
public sealed record EngagementInput(
    IReadOnlyList<EngagementParticipant> FirstSide,
    IReadOnlyList<EngagementParticipant> SecondSide,
    EngagementLocation Location,
    ushort OpeningRange,
    EngagementSideProfile FirstProfile,
    EngagementSideProfile SecondProfile,
    EngagementPlacement Placement = EngagementPlacement.Meeting,
    EngagementBurrowSide BurrowSide = EngagementBurrowSide.None,
    bool ReallocateFirstSideEquipment = true,
    bool ReallocateSecondSideEquipment = false)
{
    public IReadOnlyList<EngagementParticipant> EffectiveFirstSide =>
        FirstSide ?? Array.Empty<EngagementParticipant>();

    public IReadOnlyList<EngagementParticipant> EffectiveSecondSide =>
        SecondSide ?? Array.Empty<EngagementParticipant>();

    public void Validate()
    {
        if (Location == null) throw new ArgumentNullException(nameof(Location));
        if (Location.RegionId <= 0) throw new ArgumentException(
            "An engagement location must identify a region.", nameof(Location));
        if (FirstProfile == null) throw new ArgumentNullException(nameof(FirstProfile));
        if (SecondProfile == null) throw new ArgumentNullException(nameof(SecondProfile));
        if (EffectiveFirstSide.Any(participant => participant == null)
            || EffectiveSecondSide.Any(participant => participant == null))
        {
            throw new ArgumentException("Engagement sides cannot contain null elements.");
        }
    }
}

/// <summary>
/// Detached tactical facts consumed by mission policy. Lists are copied so the result cannot expose
/// the mutable resolver collections.
/// </summary>
public sealed record EngagementOutcome(
    EngagementEndReason EndReason,
    EngagementSide? SideHoldingField,
    IReadOnlyList<int> DisengagedParticipantIds = null,
    IReadOnlyList<int> EliminatedParticipantIds = null,
    IReadOnlyList<int> RoutingParticipantIds = null,
    IReadOnlyList<int> RearGuardParticipantIds = null)
{
    public IReadOnlyList<int> DisengagedIds { get; } = Copy(DisengagedParticipantIds);
    public IReadOnlyList<int> EliminatedIds { get; } = Copy(EliminatedParticipantIds);
    public IReadOnlyList<int> RoutingIds { get; } = Copy(RoutingParticipantIds);
    public IReadOnlyList<int> RearGuardIds { get; } = Copy(RearGuardParticipantIds);

    private static IReadOnlyList<int> Copy(IEnumerable<int> ids) =>
        (ids ?? Enumerable.Empty<int>()).Distinct().OrderBy(id => id).ToArray();
}

/// <summary>
/// Result crossing from the tactical adapter to mission policy. Replay is deliberately opaque: the
/// current host still knows how to render the concrete replay, while Operations only consumes facts.
/// </summary>
public sealed record EngagementResult(
    EngagementOutcome Outcome,
    BattleDebriefReport Report,
    string Summary,
    IBattleReplay Replay,
    int EnemiesKilled,
    int FirstSideEnemiesKilled,
    int FirstSideEnemyDeaths,
    int SecondSideEnemyDeaths,
    IReadOnlyList<int> KilledSoldierIds,
    IReadOnlyList<int> IncapacitatedSoldierIds,
    IReadOnlyList<int> DamagedSoldierIds,
    IReadOnlyList<string> ClosingSummary)
{
    public IReadOnlyList<int> KilledIds { get; } = Copy(KilledSoldierIds);
    public IReadOnlyList<int> IncapacitatedIds { get; } = Copy(IncapacitatedSoldierIds);
    public IReadOnlyList<int> DamagedIds { get; } = Copy(DamagedSoldierIds);
    public IReadOnlyList<string> ClosingLines { get; } =
        (ClosingSummary ?? Array.Empty<string>()).ToArray();

    private static IReadOnlyList<int> Copy(IEnumerable<int> ids) =>
        (ids ?? Enumerable.Empty<int>()).Distinct().OrderBy(id => id).ToArray();
}

/// <summary>Operations' only tactical dependency.</summary>
public interface IEngagementResolver
{
    EngagementResult Resolve(EngagementInput input);

    /// <summary>Returns the tactical opening-range preference without exposing tactical state.</summary>
    int GetPreferredOpeningRange(
        EngagementParticipant element,
        IReadOnlyList<EngagementParticipant> opposingElements);
}
