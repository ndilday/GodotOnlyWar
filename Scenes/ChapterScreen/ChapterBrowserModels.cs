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

public sealed record ChapterBrowserMenuItem(
    ChapterBrowserLevel Level,
    int Id,
    string IconKey,
    string Title,
    string Subtitle,
    bool CanDrill,
    bool IsSelected);

public sealed record ChapterBrowserMetric(string Value, string Label);

public sealed record ChapterBrowserDetailCard(string IconKey, string Title, string Subtitle, string Body);

public sealed record ChapterBrowserDetail(
    string IconKey,
    string Title,
    string Subtitle,
    IReadOnlyList<ChapterBrowserMetric> Metrics,
    IReadOnlyList<ChapterBrowserDetailCard> Cards);
