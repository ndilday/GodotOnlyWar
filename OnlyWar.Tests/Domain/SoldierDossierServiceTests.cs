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

        Assert.Contains(dossier.Data, pair => pair.Item1 == "Name" && pair.Item2 == "Brother Marius");
        Assert.Contains($"{date}: joined Tactical Squad", dossier.History);
        Assert.Contains($"{date}: Bronze Sword of the Emperor", dossier.Awards);
        Assert.Contains("candidate for sergeant", dossier.SergeantReport);
        Assert.Contains("fully fit", dossier.InjuryReport);
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
        Assert.Contains("requires", plainSummary);
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
