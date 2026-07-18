using System.Collections.Generic;

public enum ChapterBrowserLevel
{
    Chapter,
    Company,
    Squad,
    Soldier
}

public sealed class ChapterBrowserPath
{
    public int? CompanyId { get; set; }
    public int? SquadId { get; set; }
    public int? SoldierId { get; set; }

    public ChapterBrowserLevel Level
    {
        get
        {
            if (SoldierId.HasValue)
            {
                return ChapterBrowserLevel.Soldier;
            }
            if (SquadId.HasValue)
            {
                return ChapterBrowserLevel.Squad;
            }
            if (CompanyId.HasValue)
            {
                return ChapterBrowserLevel.Company;
            }
            return ChapterBrowserLevel.Chapter;
        }
    }

    public void TrimTo(ChapterBrowserLevel level)
    {
        if (level < ChapterBrowserLevel.Soldier)
        {
            SoldierId = null;
        }
        if (level < ChapterBrowserLevel.Squad)
        {
            SquadId = null;
        }
        if (level < ChapterBrowserLevel.Company)
        {
            CompanyId = null;
        }
    }
}

public sealed record ChapterBreadcrumbItem(ChapterBrowserLevel Level, string Text, string IconKey);

public sealed record ChapterBrowserItemEvent(ChapterBrowserLevel Level, int Id);

public sealed class ChapterBrowserNavigator
{
    public ChapterBrowserPath Path { get; } = new();
    public ChapterBrowserItemEvent SelectedItem { get; private set; }

    public void ResetToChapter()
    {
        Path.TrimTo(ChapterBrowserLevel.Chapter);
        SelectedItem = null;
    }

    public void Select(ChapterBrowserItemEvent item)
    {
        SelectedItem = item;
    }

    public void DrillInto(ChapterBrowserItemEvent item)
    {
        switch (item.Level)
        {
            case ChapterBrowserLevel.Company:
                Path.CompanyId = item.Id;
                Path.SquadId = null;
                Path.SoldierId = null;
                SelectedItem = null;
                break;
            case ChapterBrowserLevel.Squad:
                Path.SquadId = item.Id;
                Path.SoldierId = null;
                SelectedItem = null;
                break;
            case ChapterBrowserLevel.Soldier:
                Path.SoldierId = item.Id;
                SelectedItem = item;
                break;
        }
    }

    public void MoveToBreadcrumb(ChapterBrowserLevel level)
    {
        Path.TrimTo(level);
        SelectedItem = null;
    }
}

public sealed record ChapterBrowserMenuItem(
    ChapterBrowserLevel Level,
    int Id,
    string IconKey,
    string Title,
    string Subtitle,
    bool CanDrill,
    bool IsSelected,
    string DrillText = ">");

public sealed record ChapterBrowserMetric(string Value, string Label);

public sealed record ChapterBrowserDetailCard(
    string IconKey,
    string Title,
    string Subtitle,
    string Body,
    bool Scrollable = false,
    bool FullHeight = false);

public sealed record ChapterBrowserDetail(
    string IconKey,
    string Title,
    string Subtitle,
    IReadOnlyList<ChapterBrowserMetric> Metrics,
    IReadOnlyList<ChapterBrowserDetailCard> Cards,
    string PrimaryActionText = null,
    string PrimaryActionIconKey = null);
