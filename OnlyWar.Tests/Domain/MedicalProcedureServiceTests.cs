using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class MedicalProcedureServiceTests
{
    private static readonly SoldierTemplate ApothecaryTemplate =
        new(10, TestModelFactory.HumanSpecies, "Apothecary", 1, 1, false, 0, Array.Empty<Tuple<BaseSkill, float>>());
    private static readonly SoldierTemplate TechmarineTemplate =
        new(11, TestModelFactory.HumanSpecies, "Techmarine", 1, 1, false, 0, Array.Empty<Tuple<BaseSkill, float>>());

    private static ReplacementOption CyberneticLeftArm(int cost = 40) =>
        new(4, MedicalProcedureType.Cybernetic, "Left Arm", "Cybernetic Left Arm", "desc", 6, cost, true);

    [Fact]
    public void EvaluateRequisites_AllMet_WhenStaffCoLocatedSiteValidAndAffordable()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: true, requisition: 100, developedWorld: true);
        MedicalProcedureService service = new();

        IReadOnlyList<ProcedureRequisite> requisites =
            service.EvaluateRequisites(force, wounded, CyberneticLeftArm());

        Assert.All(requisites, r => Assert.True(r.IsMet, $"requisite not met: {r.Label}"));
        Assert.True(service.CanAssign(force, wounded, CyberneticLeftArm()));
    }

    [Fact]
    public void TryAssign_Succeeds_DeductsRequisitionAndCreatesProcedure()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: true, requisition: 100, developedWorld: true);
        MedicalProcedureService service = new();

        bool assigned = service.TryAssign(force, wounded, CyberneticLeftArm(40));

        Assert.True(assigned);
        Assert.Equal(60, force.Army.Requisition);
        MedicalProcedure procedure = Assert.Single(force.Army.MedicalProcedures);
        Assert.Equal(wounded.Id, procedure.SoldierId);
        Assert.Equal(4, procedure.HitLocationTemplateId);
        Assert.Equal(MedicalProcedureType.Cybernetic, procedure.ProcedureType);
        Assert.Equal(6, procedure.WeeksRemaining);
        Assert.Equal(40, procedure.RequisitionCost);
    }

    [Fact]
    public void TryAssign_Fails_WhenInsufficientRequisition()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: true, requisition: 10, developedWorld: true);
        MedicalProcedureService service = new();

        bool assigned = service.TryAssign(force, wounded, CyberneticLeftArm(40));

        Assert.False(assigned);
        Assert.Equal(10, force.Army.Requisition);
        Assert.Empty(force.Army.MedicalProcedures);
    }

    [Fact]
    public void EvaluateRequisites_MarksApothecaryUnmet_WhenNoneCoLocated()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: false, techmarinePresent: true, requisition: 100, developedWorld: true);
        MedicalProcedureService service = new();

        IReadOnlyList<ProcedureRequisite> requisites =
            service.EvaluateRequisites(force, wounded, CyberneticLeftArm());

        Assert.False(requisites.First(r => r.Label.StartsWith("Apothecary")).IsMet);
        Assert.True(requisites.First(r => r.Label.StartsWith("Techmarine")).IsMet);
        Assert.False(service.CanAssign(force, wounded, CyberneticLeftArm()));
    }

    [Fact]
    public void EvaluateRequisites_MarksSiteUnmet_OnUndevelopedWorld()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: true, requisition: 100, developedWorld: false);
        MedicalProcedureService service = new();

        IReadOnlyList<ProcedureRequisite> requisites =
            service.EvaluateRequisites(force, wounded, CyberneticLeftArm());

        Assert.False(requisites.First(r => r.Label == "Valid surgery site").IsMet);
    }

    private static (PlayerForce, PlayerSoldier) BuildScenario(
        bool apothecaryPresent, bool techmarinePresent, int requisition, bool developedWorld)
    {
        Faction player = BuildPlayerFaction();
        Region region = BuildImperialRegion(player, developedWorld);

        UnitTemplate chapterTemplate = new(100, "Chapter", true, new List<SquadTemplate>(), new List<UnitTemplate>());
        UnitTemplate companyTemplate = new(101, "Company", false, new List<SquadTemplate>(), new List<UnitTemplate>());
        Unit chapter = new("Test Chapter", chapterTemplate);
        Unit company = new("1st Company", companyTemplate) { ParentUnit = chapter };
        chapter.ChildUnits.Add(company);
        Squad squad = new("Test Squad", company, TestModelFactory.SquadTemplate) { CurrentRegion = region };
        company.AddSquad(squad);

        List<PlayerSoldier> soldiers = [];
        PlayerSoldier wounded = MakeSoldier(1, "Wounded", TestModelFactory.MarineTemplate);
        wounded.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm").Wounds.AddWound(WoundLevel.Critical);
        wounded.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm").Wounds.AddWound(WoundLevel.Critical);
        wounded.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm").Wounds.AddWound(WoundLevel.Critical);
        squad.AddSquadMember(wounded);
        soldiers.Add(wounded);

        if (apothecaryPresent)
        {
            PlayerSoldier apothecary = MakeSoldier(2, "Brother Medic", ApothecaryTemplate);
            squad.AddSquadMember(apothecary);
            soldiers.Add(apothecary);
        }
        if (techmarinePresent)
        {
            PlayerSoldier techmarine = MakeSoldier(3, "Brother Forge", TechmarineTemplate);
            squad.AddSquadMember(techmarine);
            soldiers.Add(techmarine);
        }

        Army army = new("Test Army", null, "Commander", chapter, soldiers) { Requisition = requisition };
        PlayerForce force = new(player, army, null);
        return (force, wounded);
    }

    private static PlayerSoldier MakeSoldier(int id, string name, SoldierTemplate template)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(template: template, name: name);
        soldier.Id = id;
        return new PlayerSoldier(soldier, name);
    }

    private static Region BuildImperialRegion(Faction player, bool developedWorld)
    {
        PlanetTemplate template = new(
            1,
            developedWorld ? "Hive" : "Agri",
            1,
            new LogNormalValueTemplate { Floor = 1000, Scale = 0 },
            new LogNormalValueTemplate { Floor = 2000, Scale = 0 },
            new NormalizedValueTemplate { BaseValue = 1, StandardDeviation = 0 },
            new LinearValueTemplate { MinValue = 0, MaxValue = 0 });
        Planet planet = new(1, "Test World", new Coordinate(1, 1), 1, template, 1, 0);
        Region region = new(0, planet, 0, "Region 0",
            RegionExtensions.GetCoordinatesFromRegionNumber(0), 0);
        planet.Regions[0] = region;
        PlanetFaction planetFaction = new(player) { IsPublic = true };
        planet.PlanetFactionMap[player.Id] = planetFaction;
        RegionFaction regionFaction = new(planetFaction, region) { IsPublic = true };
        region.RegionFactionMap[player.Id] = regionFaction;
        return region;
    }

    private static Faction BuildPlayerFaction()
    {
        return new Faction(
            2, "Test Chapter", Color.Red, true, false, false, GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }
}
