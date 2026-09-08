using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Application;

public enum EndTurnWarningCategory
{
    IdleDeployableSquads,
    LeaderlessSquads,
    ActionableTaskForces,
    SpecialMissionOpportunities,
    RecruitmentProgram
}

public interface IEndTurnWarningPreferencesRepository
{
    EndTurnWarningPreferences Load();
    void Save(EndTurnWarningPreferences preferences);
    string PreferencesFilePath { get; }
}

public sealed class EndTurnWarningPreferences
{
    public bool WarnIdleDeployableSquads { get; set; } = true;
    public bool WarnLeaderlessSquads { get; set; } = true;
    public bool WarnActionableTaskForces { get; set; } = true;
    public bool WarnSpecialMissionOpportunities { get; set; } = true;
    public bool WarnRecruitmentProgram { get; set; } = true;

    public bool IsEnabled(EndTurnWarningCategory category) => category switch
    {
        EndTurnWarningCategory.IdleDeployableSquads => WarnIdleDeployableSquads,
        EndTurnWarningCategory.LeaderlessSquads => WarnLeaderlessSquads,
        EndTurnWarningCategory.ActionableTaskForces => WarnActionableTaskForces,
        EndTurnWarningCategory.SpecialMissionOpportunities => WarnSpecialMissionOpportunities,
        EndTurnWarningCategory.RecruitmentProgram => WarnRecruitmentProgram,
        _ => true
    };

    public void SetEnabled(EndTurnWarningCategory category, bool enabled)
    {
        switch (category)
        {
            case EndTurnWarningCategory.IdleDeployableSquads:
                WarnIdleDeployableSquads = enabled;
                break;
            case EndTurnWarningCategory.LeaderlessSquads:
                WarnLeaderlessSquads = enabled;
                break;
            case EndTurnWarningCategory.ActionableTaskForces:
                WarnActionableTaskForces = enabled;
                break;
            case EndTurnWarningCategory.SpecialMissionOpportunities:
                WarnSpecialMissionOpportunities = enabled;
                break;
            case EndTurnWarningCategory.RecruitmentProgram:
                WarnRecruitmentProgram = enabled;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(category), category, null);
        }
    }

    public EndTurnWarningPreferences Clone() => new()
    {
        WarnIdleDeployableSquads = WarnIdleDeployableSquads,
        WarnLeaderlessSquads = WarnLeaderlessSquads,
        WarnActionableTaskForces = WarnActionableTaskForces,
        WarnSpecialMissionOpportunities = WarnSpecialMissionOpportunities,
        WarnRecruitmentProgram = WarnRecruitmentProgram
    };
}

public sealed class EndTurnAttentionItem
{
    public EndTurnWarningCategory Category { get; }
    public int EntityId { get; }
    public string Title { get; }
    public string Detail { get; }
    public string StableKey { get; }
    public CampaignNavigationTarget NavigationTarget { get; }
    public int? DeadlineWeek { get; }

    public EndTurnAttentionItem(
        EndTurnWarningCategory category,
        int entityId,
        string title,
        string detail)
        : this(category, entityId, title, detail, null, null, null)
    {
    }

    public EndTurnAttentionItem(
        EndTurnWarningCategory category,
        int entityId,
        string title,
        string detail,
        string stableKey,
        CampaignNavigationTarget navigationTarget,
        int? deadlineWeek)
    {
        Category = category;
        EntityId = entityId;
        Title = title ?? string.Empty;
        Detail = detail ?? string.Empty;
        StableKey = stableKey ?? $"{category}/{entityId}";
        NavigationTarget = navigationTarget;
        DeadlineWeek = deadlineWeek;
    }
}

public sealed class EndTurnPreflightReport
{
    private readonly IReadOnlyList<EndTurnAttentionItem> _items;

    public IReadOnlyList<EndTurnAttentionItem> Items => _items;
    public bool RequiresConfirmation => _items.Count > 0;

    public EndTurnPreflightReport(IEnumerable<EndTurnAttentionItem> items)
    {
        _items = (items ?? Enumerable.Empty<EndTurnAttentionItem>()).ToList().AsReadOnly();
    }

    public IReadOnlyList<EndTurnAttentionItem> ForCategory(EndTurnWarningCategory category) =>
        _items.Where(item => item.Category == category).ToList().AsReadOnly();
}
