using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Missions;

/// <summary>
/// Operations' mutable mission element. It keeps campaign members and mission policy together,
/// while the Battles boundary receives only an <see cref="EngagementParticipant"/> projection.
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

    public int TacticalId { get; }
    public int Id => TacticalId;
    public string Name { get; }
    public Faction Faction { get; }
    public bool IsPlayerSquad { get; }
    public bool IsPlayerAligned => IsPlayerSquad || Faction?.IsDefaultFaction == true;
    public EngagementElementTraits Traits { get; }
    public Squad CampaignSquad { get; }
    public Squad Squad => CampaignSquad;
    public PlayerSoldier CampaignCharacter { get; }
    public IEngagementState State { get; }
    public IReadOnlyList<ISoldier> Members => _members;

    public IReadOnlyList<int> FrozenParticipantIds =>
        _frozenParticipantIds?.OrderBy(id => id).ToArray();

    public IReadOnlyList<ISoldier> AbleMembers => _members
        .Where(member => member.IsCombatEffective
            && (_frozenParticipantIds == null || _frozenParticipantIds.Contains(member.Id)))
        .ToArray();

    public int StartingAbleMemberCount { get; }

    public ISoldier SquadLeader => AbleMembers
        .FirstOrDefault(member => member.Template?.IsSquadLeader == true);

    public long AbleBattleValue => AbleMembers
        .Sum(member => (long)(member.Template?.BattleValue ?? 0));

    public long FallenBattleValue => Members
        .Where(member => !member.IsCombatEffective)
        .Sum(member => (long)(member.Template?.BattleValue ?? 0));

    public float GetSquadMove() => AbleMembers
        .Select(member => member.MoveSpeed * member.MotiveSpeedMultiplier)
        .DefaultIfEmpty(float.MaxValue)
        .Min();

    public void RefreshEngagementParticipants(IEnumerable<ISoldier> participants)
    {
        _frozenParticipantIds = (participants ?? Enumerable.Empty<ISoldier>())
            .Where(member => member != null)
            .Select(member => member.Id)
            .ToHashSet();
    }

    public ISoldier GetRandomAbleMember(IRNG random)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        IReadOnlyList<ISoldier> able = AbleMembers;
        return able[random.GetIntBelowMax(0, able.Count)];
    }

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
        Faction == null
            ? null
            : new EngagementFactionFacts(
                Faction.Id,
                Faction.Name,
                Faction.IsPlayerFaction,
                Faction.IsDefaultFaction),
        Members,
        FrozenParticipantIds,
        IsPlayerSquad,
        Traits,
        State);
}
