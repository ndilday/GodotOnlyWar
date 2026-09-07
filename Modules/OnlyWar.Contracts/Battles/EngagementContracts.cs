using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Contracts.Battles;

/// <summary>
/// Opaque state owned by the tactical implementation for one engagement element. Operations may
/// carry the handle across mission stages, but cannot inspect or mutate tactical state through it.
/// </summary>
public interface IEngagementState
{
}

/// <summary>
/// The neutral mission-side representation of one operational element.
///
/// <para>This is deliberately separate from <see cref="IEngagementState"/>. Operations can use
/// the identity, live domain members, and frozen participant set to make mission decisions, while
/// the opaque state is retained by the Application adapter for tactical continuity. In particular,
/// this type is not implemented by <c>BattleSquad</c> and does not expose tactical equipment,
/// placement, or resolver state.</para>
/// </summary>
public sealed class OperationalMissionElement
{
    private readonly IReadOnlyList<ISoldier> _members;
    private HashSet<int> _frozenParticipantIds;

    public OperationalMissionElement(
        int tacticalId,
        string name,
        Faction faction,
        IEnumerable<ISoldier> members,
        bool isPlayerSquad = false,
        EngagementElementTraits traits = null,
        Squad campaignSquad = null,
        PlayerSoldier campaignCharacter = null,
        IEngagementState state = null,
        IEnumerable<int> frozenParticipantIds = null,
        int? startingAbleMemberCount = null)
    {
        TacticalId = tacticalId;
        Name = name;
        Faction = faction;
        IsPlayerSquad = isPlayerSquad;
        Traits = traits ?? new EngagementElementTraits();
        CampaignSquad = campaignSquad;
        CampaignCharacter = campaignCharacter;
        State = state;
        _members = (members ?? Enumerable.Empty<ISoldier>())
            .Where(member => member != null)
            .ToArray();
        _frozenParticipantIds = frozenParticipantIds?.ToHashSet();
        StartingAbleMemberCount = startingAbleMemberCount ?? AbleMembers.Count;
    }

    /// <summary>The stable tactical identity assigned for this mission element.</summary>
    public int TacticalId { get; }

    /// <summary>Compatibility name for callers that describe an element as a squad.</summary>
    public int Id => TacticalId;

    public string Name { get; }
    public Faction Faction { get; }
    public bool IsPlayerSquad { get; }

    /// <summary>
    /// Presentation affiliation used by mission reports. Player control remains distinct from
    /// default-faction affiliation, just as it is at the tactical boundary.
    /// </summary>
    public bool IsPlayerAligned => IsPlayerSquad || Faction?.IsDefaultFaction == true;

    public EngagementElementTraits Traits { get; }
    public Squad CampaignSquad { get; }
    public Squad Squad => CampaignSquad;
    public PlayerSoldier CampaignCharacter { get; }

    /// <summary>
    /// The opaque state retained by the Application adapter. Operations may carry it but cannot
    /// inspect it. A null value is tolerated for pure mission-policy fixtures; production-created
    /// elements are always state-backed before they reach an engagement resolver.
    /// </summary>
    public IEngagementState State { get; }

    /// <summary>
    /// Live domain members shared with the bounded tactical adapter. The collection itself is
    /// detached, while the soldier objects remain the single authoritative wound graph.
    /// </summary>
    public IReadOnlyList<ISoldier> Members => _members;

    /// <summary>
    /// The participant filter frozen for the next engagement, or null for an unrestricted NPC
    /// element. Operations replaces this filter at its explicit stage boundary; the adapter applies
    /// it to the retained tactical state when the next engagement or tactical query begins.
    /// </summary>
    public IReadOnlyList<int> FrozenParticipantIds =>
        _frozenParticipantIds?.OrderBy(id => id).ToArray();

    /// <summary>Members that are both combat-effective and selected for the next engagement.</summary>
    public IReadOnlyList<ISoldier> AbleMembers => _members
        .Where(member => member.IsCombatEffective
            && (_frozenParticipantIds == null || _frozenParticipantIds.Contains(member.Id)))
        .ToArray();

    /// <summary>The initial able count used by the mission-level aggression tolerance.</summary>
    public int StartingAbleMemberCount { get; }

    public ISoldier SquadLeader => AbleMembers
        .FirstOrDefault(member => member.Template?.IsSquadLeader == true);

    public long AbleBattleValue => AbleMembers
        .Sum(member => (long)(member.Template?.BattleValue ?? 0));

    public long FallenBattleValue => Members
        .Where(member => !member.IsCombatEffective)
        .Sum(member => (long)(member.Template?.BattleValue ?? 0));

    /// <summary>Slowest selected able member's current movement speed.</summary>
    public float GetSquadMove() => AbleMembers
        .Select(member => member.MoveSpeed * member.MotiveSpeedMultiplier)
        .DefaultIfEmpty(float.MaxValue)
        .Min();

    /// <summary>
    /// Replaces the participant set for the next engagement. This is an operational readiness
    /// decision, not a tactical mutation; the Application adapter consumes it during Update.
    /// </summary>
    public void RefreshEngagementParticipants(IEnumerable<ISoldier> participants)
    {
        _frozenParticipantIds = (participants ?? Enumerable.Empty<ISoldier>())
            .Where(member => member != null)
            .Select(member => member.Id)
            .ToHashSet();
    }

    /// <summary>Returns one selected able member while preserving the existing RNG draw point.</summary>
    public ISoldier GetRandomAbleMember(IRNG random)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        IReadOnlyList<ISoldier> able = AbleMembers;
        return able[random.GetIntBelowMax(0, able.Count)];
    }

    /// <summary>
    /// Whether this element may continue its mission after taking losses. The threshold is the
    /// mission policy formerly hidden behind the tactical wrapper's continuation helper.
    /// </summary>
    public bool ShouldContinueMission()
    {
        int ableMemberCount = AbleMembers.Count;
        if (ableMemberCount == 0) return false;

        Aggression aggression = (CampaignCharacter?.CurrentOrder ?? CampaignSquad?.CurrentOrders)
            ?.LevelOfAggression ?? Aggression.Normal;
        if (aggression == Aggression.Aggressive) return true;

        float ratio = (float)ableMemberCount / Math.Max(1, StartingAbleMemberCount);
        return aggression switch
        {
            Aggression.Avoid => ratio >= 0.9f,
            Aggression.Cautious => ratio >= 0.75f,
            Aggression.Normal => ratio >= 0.5f,
            Aggression.Attritional => ratio >= 0.25f,
            _ => false
        };
    }

    public EngagementParticipant ToEngagementParticipant() => new(
        TacticalId,
        Name,
        Faction,
        Members,
        FrozenParticipantIds,
        IsPlayerSquad,
        Traits,
        CampaignSquad,
        CampaignCharacter,
        State);
}

/// <summary>
/// Application-owned lifecycle for operational mission elements. Implementations create the
/// tactical wrapper once, update its frozen participant view at stage boundaries, and retain it for
/// every engagement belonging to the same mission.
/// </summary>
public interface IEngagementElementFactory
{
    OperationalMissionElement CreateSquad(
        bool isPlayerSquad,
        Squad squad,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null);

    OperationalMissionElement CreateAttachedCharacter(
        PlayerSoldier character,
        int tacticalId,
        Faction fallbackFaction,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null);

    /// <summary>Applies the neutral element's current participant set to retained tactical state.</summary>
    void Update(OperationalMissionElement element);
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

/// <summary>
/// A frozen participant set for one engagement element. <see cref="Members"/> is the live domain
/// soldier graph that the tactical layer is allowed to wound; the selected IDs are frozen for the
/// duration of this engagement and are re-evaluated by Operations before the next one.
/// </summary>
public sealed record EngagementParticipant(
    int TacticalId,
    string Name,
    Faction Faction,
    IReadOnlyList<ISoldier> Members,
    IReadOnlyList<int> FrozenParticipantIds = null,
    bool IsPlayerSquad = false,
    EngagementElementTraits Traits = null,
    Squad CampaignSquad = null,
    PlayerSoldier CampaignCharacter = null,
    IEngagementState State = null)
{
    public IReadOnlyList<ISoldier> EffectiveMembers => Members ?? Array.Empty<ISoldier>();

    public IReadOnlyList<int> EffectiveFrozenParticipantIds =>
        FrozenParticipantIds ?? EffectiveMembers.Select(member => member.Id).ToArray();
}

/// <summary>
/// The complete request for one tactical engagement. It contains the scoped live domain access and
/// immutable choices needed by Battles, but no mission scheduler, current-session lookup, or
/// tactical implementation object.
/// </summary>
public sealed record EngagementInput(
    IReadOnlyList<EngagementParticipant> FirstSide,
    IReadOnlyList<EngagementParticipant> SecondSide,
    Region Region,
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
        if (Region == null) throw new ArgumentNullException(nameof(Region));
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
        OperationalMissionElement element,
        IReadOnlyList<OperationalMissionElement> opposingElements);
}
