using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class SoldierDossierServiceTests
{
    private readonly SoldierDossierService _service = new();

    [Fact]
    public void BuildDossier_FormatsDataHistoryAwardsAndReports()
    {
        Squad squad = CreateAssignedSquad("Tactical Squad");
        PlayerSoldier soldier = AddPlayerSoldier(squad, "Brother Marius");
        Date date = new(41, 999, 12);
        soldier.AddEvent(new SoldierEvent(date, SoldierEventType.Transfer, "joined Tactical Squad"));
        soldier.AddAward(new SoldierAward(date, "Bronze Sword of the Emperor", "Sword", 1));
        soldier.AddEvaluation(new SoldierEvaluation(date, melee: 100, ranged: 112, lead: 60, med: 0, tech: 0, piety: 0, ancient: 0));

        SoldierDossier dossier = _service.BuildDossier(soldier, richTextInjury: false);

        Assert.Contains($"{date}: joined Tactical Squad", dossier.History);
        Assert.Contains($"{date}: Bronze Sword of the Emperor", dossier.Awards);
        Assert.Contains("candidate for sergeant", dossier.SergeantReport);
        Assert.Contains("Fully fit", dossier.InjuryReport);
    }

    [Fact]
    public void BuildSoldierData_TracksTimeInRankAndSquadFromLatestMilestones()
    {
        Squad squad = CreateAssignedSquad("Tactical Squad");
        PlayerSoldier soldier = AddPlayerSoldier(squad, "Brother Marius");
        // Enlisted in year 990, promoted in 995, and transferred to his current squad in 998.
        soldier.AddEvent(new SoldierEvent(new Date(41, 990, 1), SoldierEventType.AcceptedToTraining, "accepted into training"));
        soldier.AddEvent(new SoldierEvent(new Date(41, 995, 1), SoldierEventType.Promotion, "promoted to Battle-Brother"));
        soldier.AddEvent(new SoldierEvent(new Date(41, 998, 1), SoldierEventType.Transfer, "transferred to Tactical Squad"));
        Date currentDate = new(41, 1000, 1);

        var data = _service.BuildSoldierData(soldier, currentDate);

        Assert.Contains(data, pair => pair.Item1 == "Time in Service" && pair.Item2 == "10 years (since 1.990.M41)");
        Assert.Contains(data, pair => pair.Item1 == "Time in Rank" && pair.Item2 == "5 years (since 1.995.M41)");
        Assert.Contains(data, pair => pair.Item1 == "Time in Squad" && pair.Item2 == "2 years (since 1.998.M41)");
    }

    [Fact]
    public void BuildSoldierData_FallsBackToEnlistmentWhenNeverPromotedOrTransferred()
    {
        Squad squad = CreateAssignedSquad("Tactical Squad");
        PlayerSoldier soldier = AddPlayerSoldier(squad, "Brother Marius");
        soldier.AddEvent(new SoldierEvent(new Date(41, 995, 1), SoldierEventType.AcceptedToTraining, "accepted into training"));
        Date currentDate = new(41, 1000, 1);

        var data = _service.BuildSoldierData(soldier, currentDate);

        // With no promotion or transfer on record, rank and squad tenure anchor to enlistment.
        Assert.Contains(data, pair => pair.Item1 == "Time in Rank" && pair.Item2 == "5 years (since 1.995.M41)");
        Assert.Contains(data, pair => pair.Item1 == "Time in Squad" && pair.Item2 == "5 years (since 1.995.M41)");
    }

    [Fact]
    public void BuildAwardLines_ShowsOnlyHighestTierPerAwardType()
    {
        Squad squad = CreateAssignedSquad("Tactical Squad");
        PlayerSoldier soldier = AddPlayerSoldier(squad, "Brother Marius");
        Date bronzeDate = new(41, 998, 5);
        Date silverDate = new(41, 999, 20);
        Date marksmanDate = new(41, 999, 30);
        soldier.AddAward(new SoldierAward(bronzeDate, "Bronze Sword of the Emperor", "Sword", 1));
        soldier.AddAward(new SoldierAward(silverDate, "Silver Sword of the Emperor", "Sword", 2));
        soldier.AddAward(new SoldierAward(marksmanDate, "Marksman's Honour", "Marksman", 1));

        var awards = _service.BuildAwardLines(soldier);

        // The Sword type collapses to its highest tier (Silver); other types are unaffected.
        Assert.Equal(2, awards.Count);
        Assert.Contains($"{silverDate}: Silver Sword of the Emperor", awards);
        Assert.Contains($"{marksmanDate}: Marksman's Honour", awards);
        Assert.DoesNotContain($"{bronzeDate}: Bronze Sword of the Emperor", awards);
    }

    [Fact]
    public void BuildSergeantReport_ReturnsFallbackWhenNoEvaluationExists()
    {
        Squad squad = CreateAssignedSquad("Tactical Squad");
        PlayerSoldier soldier = AddPlayerSoldier(squad, "Brother Marius");

        string report = _service.BuildSergeantReport(soldier);

        Assert.Equal("No sergeant evaluation is available for this battle brother.", report);
    }

    [Fact]
    public void GenerateSoldierInjurySummary_CanStripRichTextTagsForChapterCards()
    {
        Squad squad = CreateAssignedSquad("Tactical Squad");
        PlayerSoldier soldier = AddPlayerSoldier(squad, "Brother Marius");
        HitLocation arm = soldier.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm");
        arm.Wounds.AddWound(WoundLevel.Major);

        string plainSummary = _service.GenerateSoldierInjurySummary(soldier, richText: false);

        Assert.Contains("Left Arm: Major", plainSummary);
        Assert.DoesNotContain("<color", plainSummary);
        Assert.Contains("Requires", plainSummary);
    }

    private static Squad CreateAssignedSquad(string squadTemplateName)
    {
        SquadTemplate template = new(
            1,
            squadTemplateName,
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)],
            SquadTypes.None,
            10);
        UnitTemplate unitTemplate = new(1, "Test Unit", true, [template], []);
        Unit unit = new(1, "Test Unit", unitTemplate, []);
        Squad squad = new("Test Squad", unit, template);
        unit.AddSquad(squad);
        return squad;
    }

    private static PlayerSoldier AddPlayerSoldier(Squad squad, string name)
    {
        PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, name), name);
        squad.AddSquadMember(soldier);
        return soldier;
    }
}
