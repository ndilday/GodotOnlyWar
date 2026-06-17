using Xunit;

namespace OnlyWar.Tests.UI;

public class ChapterBrowserNavigatorTests
{
    [Fact]
    public void NewNavigator_StartsAtChapterLevel()
    {
        ChapterBrowserNavigator navigator = new();

        Assert.Equal(ChapterBrowserLevel.Chapter, navigator.Path.Level);
        Assert.Null(navigator.SelectedItem);
    }

    [Fact]
    public void Select_UpdatesSelectedItemWithoutChangingPath()
    {
        ChapterBrowserNavigator navigator = new();
        ChapterBrowserItemEvent company = new(ChapterBrowserLevel.Company, 12);

        navigator.Select(company);

        Assert.Equal(ChapterBrowserLevel.Chapter, navigator.Path.Level);
        Assert.Equal(company, navigator.SelectedItem);
    }

    [Fact]
    public void DrillIntoCompany_MovesToCompanyAndClearsLowerPath()
    {
        ChapterBrowserNavigator navigator = new();
        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Company, 12));
        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Squad, 34));

        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Company, 56));

        Assert.Equal(ChapterBrowserLevel.Company, navigator.Path.Level);
        Assert.Equal(56, navigator.Path.CompanyId);
        Assert.Null(navigator.Path.SquadId);
        Assert.Null(navigator.Path.SoldierId);
        Assert.Null(navigator.SelectedItem);
    }

    [Fact]
    public void DrillIntoSquad_MovesToSquadWithinCurrentCompany()
    {
        ChapterBrowserNavigator navigator = new();
        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Company, 12));

        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Squad, 34));

        Assert.Equal(ChapterBrowserLevel.Squad, navigator.Path.Level);
        Assert.Equal(12, navigator.Path.CompanyId);
        Assert.Equal(34, navigator.Path.SquadId);
        Assert.Null(navigator.Path.SoldierId);
        Assert.Null(navigator.SelectedItem);
    }

    [Fact]
    public void DrillIntoSoldier_MovesToSoldierAndSelectsIt()
    {
        ChapterBrowserNavigator navigator = new();
        ChapterBrowserItemEvent soldier = new(ChapterBrowserLevel.Soldier, 78);

        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Company, 12));
        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Squad, 34));
        navigator.DrillInto(soldier);

        Assert.Equal(ChapterBrowserLevel.Soldier, navigator.Path.Level);
        Assert.Equal(78, navigator.Path.SoldierId);
        Assert.Equal(soldier, navigator.SelectedItem);
    }

    [Fact]
    public void MoveToBreadcrumb_TrimsLowerPathAndClearsSelection()
    {
        ChapterBrowserNavigator navigator = new();
        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Company, 12));
        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Squad, 34));
        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Soldier, 78));

        navigator.MoveToBreadcrumb(ChapterBrowserLevel.Company);

        Assert.Equal(ChapterBrowserLevel.Company, navigator.Path.Level);
        Assert.Equal(12, navigator.Path.CompanyId);
        Assert.Null(navigator.Path.SquadId);
        Assert.Null(navigator.Path.SoldierId);
        Assert.Null(navigator.SelectedItem);
    }

    [Fact]
    public void ResetToChapter_ClearsPathAndSelection()
    {
        ChapterBrowserNavigator navigator = new();
        navigator.DrillInto(new ChapterBrowserItemEvent(ChapterBrowserLevel.Company, 12));
        navigator.Select(new ChapterBrowserItemEvent(ChapterBrowserLevel.Squad, 34));

        navigator.ResetToChapter();

        Assert.Equal(ChapterBrowserLevel.Chapter, navigator.Path.Level);
        Assert.Null(navigator.Path.CompanyId);
        Assert.Null(navigator.SelectedItem);
    }
}
