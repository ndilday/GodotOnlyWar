using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class SoldierTransferServiceTests
{
    private readonly SoldierTransferService _service = new();
    private readonly Date _date = new(41, 999, 12);

    [Fact]
    public void GetTransferOptions_FiltersByOpenTemplateSlotsAndRank()
    {
        SoldierTemplate initiate = CreateTemplate(10, "Initiate", 0, false);
        SoldierTemplate veteran = CreateTemplate(11, "Veteran", 3, false);
        SquadTemplate mixedTemplate = CreateSquadTemplate(
            "Mixed Squad",
            (TestModelFactory.MarineTemplate, 0, 2),
            (TestModelFactory.SergeantTemplate, 0, 1),
            (initiate, 0, 1),
            (veteran, 0, 1));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", mixedTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");

        Squad target = AddSquad(chapter, "Target Squad", mixedTemplate);
        AddPlayerSoldier(target, TestModelFactory.SergeantTemplate, "Sergeant Titus");

        Squad full = AddSquad(chapter, "Full Squad", CreateSquadTemplate(
            "Full Squad Template",
            (TestModelFactory.MarineTemplate, 0, 1)));
        AddPlayerSoldier(full, TestModelFactory.MarineTemplate, "Brother Full");

        List<SoldierTransferOption> options = _service.GetTransferOptions(chapter, soldier);

        Assert.Contains(options, option => option.SquadId == target.Id && option.SoldierTemplate == TestModelFactory.MarineTemplate);
        // The target squad already has a leader, so its Sergeant slot is not offered.
        Assert.DoesNotContain(options, option => option.SquadId == target.Id && option.SoldierTemplate == TestModelFactory.SergeantTemplate);
        // Initiate is a lower rank than the Marine; transfers never demote, so it is excluded.
        Assert.DoesNotContain(options, option => option.SquadId == target.Id && option.SoldierTemplate == initiate);
        // Veteran is several ranks above the Marine; promotions may span any number of levels.
        Assert.Contains(options, option => option.SquadId == target.Id && option.SoldierTemplate == veteran);
        Assert.DoesNotContain(options, option => option.SquadId == full.Id);
    }

    [Fact]
    public void GetTransferOptions_OnlyOffersLeaderSlotsWhenTargetHasNoLeader()
    {
        SquadTemplate template = CreateSquadTemplate(
            "Commandable Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", template);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        Squad target = AddSquad(chapter, "Leaderless Squad", template);

        List<SoldierTransferOption> targetOptions = _service
            .GetTransferOptions(chapter, soldier)
            .Where(option => option.SquadId == target.Id)
            .ToList();

        Assert.Single(targetOptions);
        Assert.Equal(TestModelFactory.SergeantTemplate, targetOptions[0].SoldierTemplate);
    }

    [Fact]
    public void GetTransferOptions_DoesNotOfferLeaderSlotWhenSquadAlreadyHasADifferentLeader()
    {
        // The slot's defined leader is a Sergeant, but the squad is actually led by a
        // different leader template (mirrors an HQ squad whose template names a
        // "Recruitment Captain" slot while a plain Captain fills it). The leader slot is
        // conceptually filled and must not be offered, even though no member matches the
        // slot's exact template.
        SoldierTemplate altLeader = CreateTemplate(20, "Alternate Leader", 2, isSquadLeader: true);
        SquadTemplate template = CreateSquadTemplate(
            "Command Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", template);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        Squad target = AddSquad(chapter, "Led Squad", template);
        AddPlayerSoldier(target, altLeader, "Captain Varro");

        List<SoldierTransferOption> targetOptions = _service
            .GetTransferOptions(chapter, soldier)
            .Where(option => option.SquadId == target.Id)
            .ToList();

        Assert.DoesNotContain(targetOptions, option => option.SoldierTemplate == TestModelFactory.SergeantTemplate);
        Assert.All(targetOptions, option => Assert.False(option.SoldierTemplate.IsSquadLeader));
    }

    [Fact]
    public void PreviewHistory_DoesNotMutateSoldierHistory()
    {
        SquadTemplate template = CreateSquadTemplate(
            "Line Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", template);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        Squad target = AddSquad(chapter, "Target Squad", template);
        SoldierTransferOption option = new(target.Id, TestModelFactory.SergeantTemplate, "Test Sergeant, Target Squad, Chapter");

        IReadOnlyList<string> preview = _service.PreviewHistory(soldier, option, _date);

        Assert.Equal(2, preview.Count);
        Assert.Empty(soldier.SoldierHistory);
    }

    [Fact]
    public void ApplyTransfer_MovesSoldierPromotesAndRecordsHistoryOnce()
    {
        SquadTemplate template = CreateSquadTemplate(
            "Line Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", template);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        Squad target = AddSquad(chapter, "Target Squad", template);
        Dictionary<int, Squad> squadMap = chapter.GetAllSquads().ToDictionary(squad => squad.Id);
        SoldierTransferOption option = new(target.Id, TestModelFactory.SergeantTemplate, "Test Sergeant, Target Squad, Chapter");

        bool didTransfer = _service.ApplyTransfer(soldier, option, squadMap, _date);

        Assert.True(didTransfer);
        Assert.DoesNotContain(soldier, source.Members);
        Assert.Contains(soldier, target.Members);
        Assert.Equal(target, soldier.AssignedSquad);
        Assert.Equal(TestModelFactory.SergeantTemplate, soldier.Template);
        Assert.Equal(2, soldier.SoldierHistory.Count);
        Assert.Contains($"{_date}: promoted to Test Sergeant", soldier.SoldierHistory);
        Assert.Contains($"{_date}: transferred to Test Sergeant, Target Squad, Chapter", soldier.SoldierHistory);
    }

    [Fact]
    public void ApplyTransfer_CarriesLocationToEmptyDestinationAndClearsEmptySource()
    {
        SquadTemplate template = CreateSquadTemplate("Line Squad", (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", template);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        Ship ship = new(1, "Test Ship", new ShipTemplate(1, "Test Ship Class", 20, 0, 0));
        source.BoardedLocation = ship;
        Squad target = AddSquad(chapter, "Empty Target Squad", template);
        Dictionary<int, Squad> squadMap = chapter.GetAllSquads().ToDictionary(squad => squad.Id);
        SoldierTransferOption option = new(target.Id, TestModelFactory.MarineTemplate, "Test Marine, Empty Target Squad, Chapter");

        _service.ApplyTransfer(soldier, option, squadMap, _date);

        Assert.Null(source.BoardedLocation);
        Assert.Equal(ship, target.BoardedLocation);
    }

    [Fact]
    public void ApplyTransfer_RemovesEmptyScoutSquad()
    {
        SquadTemplate scoutTemplate = CreateSquadTemplate(
            "Scout Squad",
            SquadTypes.Scout,
            (TestModelFactory.MarineTemplate, 0, 4));
        SquadTemplate lineTemplate = CreateSquadTemplate("Line Squad", (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad scoutSquad = AddSquad(chapter, "Scout Squad", scoutTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(scoutSquad, TestModelFactory.MarineTemplate, "Brother Marius");
        Squad target = AddSquad(chapter, "Line Squad", lineTemplate);
        Dictionary<int, Squad> squadMap = chapter.GetAllSquads().ToDictionary(squad => squad.Id);
        SoldierTransferOption option = new(target.Id, TestModelFactory.MarineTemplate, "Test Marine, Line Squad, Chapter");

        _service.ApplyTransfer(soldier, option, squadMap, _date);

        Assert.DoesNotContain(scoutSquad, chapter.Squads);
        Assert.False(squadMap.ContainsKey(scoutSquad.Id));
    }

    [Fact]
    public void GetTransferOptions_OffersNewSquadWhenUnitHasCapacity()
    {
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Line Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit company = CreateUnitWithSlots("Company", new SquadTemplateSlot(lineTemplate, 0, 2));
        Squad source = AddSquad(company, "Source Squad", lineTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");

        List<SoldierTransferOption> options = _service.GetTransferOptions(company, soldier);

        SoldierTransferOption newSquadOption = options.SingleOrDefault(option => option.IsNewSquad);
        Assert.NotNull(newSquadOption);
        // A new squad is empty, so the only opening is its leader slot.
        Assert.Equal(TestModelFactory.SergeantTemplate, newSquadOption.SoldierTemplate);
        Assert.Equal(company, newSquadOption.TargetUnit);
        Assert.Equal(lineTemplate, newSquadOption.TargetSquadTemplate);
    }

    [Fact]
    public void GetTransferOptions_DoesNotOfferNewSquadWhenAtCap()
    {
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Line Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit company = CreateUnitWithSlots("Company", new SquadTemplateSlot(lineTemplate, 0, 1));
        Squad source = AddSquad(company, "Source Squad", lineTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");

        List<SoldierTransferOption> options = _service.GetTransferOptions(company, soldier);

        Assert.DoesNotContain(options, option => option.IsNewSquad);
    }

    [Fact]
    public void ApplyTransfer_CreatesNewSquadAndRemovesEmptiedSource()
    {
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Line Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit company = CreateUnitWithSlots("Company", new SquadTemplateSlot(lineTemplate, 0, 2));
        Squad source = AddSquad(company, "Source Squad", lineTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        SoldierTransferOption newSquadOption = _service
            .GetTransferOptions(company, soldier)
            .Single(option => option.IsNewSquad);
        Dictionary<int, Squad> squadMap = company.GetAllSquads().ToDictionary(squad => squad.Id);

        bool didTransfer = _service.ApplyTransfer(soldier, newSquadOption, squadMap, _date);

        Assert.True(didTransfer);
        Assert.Equal(TestModelFactory.SergeantTemplate, soldier.Template);
        Squad newSquad = soldier.AssignedSquad;
        Assert.NotEqual(source.Id, newSquad.Id);
        Assert.Equal(lineTemplate, newSquad.SquadTemplate);
        Assert.Contains(newSquad, company.Squads);
        Assert.True(squadMap.ContainsKey(newSquad.Id));
        // Moving the only member out empties the source line squad (MinCount 0), so it is removed.
        Assert.DoesNotContain(source, company.Squads);
        Assert.False(squadMap.ContainsKey(source.Id));
    }

    [Fact]
    public void ApplyTransfer_KeepsEmptiedRequiredSquad()
    {
        SquadTemplate commandTemplate = CreateSquadTemplate("Command Squad", (TestModelFactory.MarineTemplate, 0, 4));
        SquadTemplate lineTemplate = CreateSquadTemplate("Line Squad", (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnitWithSlots(
            "Chapter",
            new SquadTemplateSlot(commandTemplate, 1, 1),
            new SquadTemplateSlot(lineTemplate, 0, 2));
        Squad command = AddSquad(chapter, "Command Squad", commandTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(command, TestModelFactory.MarineTemplate, "Brother Marius");
        Squad line = AddSquad(chapter, "Line Squad", lineTemplate);
        Dictionary<int, Squad> squadMap = chapter.GetAllSquads().ToDictionary(squad => squad.Id);
        SoldierTransferOption option = new(line.Id, TestModelFactory.MarineTemplate, "Test Marine, Line Squad, Chapter");

        _service.ApplyTransfer(soldier, option, squadMap, _date);

        // The command squad is now empty but its slot requires one (MinCount 1), so it is kept.
        Assert.Contains(command, chapter.Squads);
        Assert.True(squadMap.ContainsKey(command.Id));
    }

    private static Unit CreateUnit(string name)
    {
        UnitTemplate template = new(1, $"{name} Template", true, [], []);
        return new Unit(1, name, template, []);
    }

    private static Unit CreateUnitWithSlots(string name, params SquadTemplateSlot[] slots)
    {
        UnitTemplate template = new(1, $"{name} Template", true, (SquadTemplate)null, slots.ToList());
        return new Unit(1, name, template, []);
    }

    private static Squad AddSquad(Unit unit, string name, SquadTemplate template)
    {
        Squad squad = new(name, unit, template);
        unit.AddSquad(squad);
        return squad;
    }

    private static PlayerSoldier AddPlayerSoldier(Squad squad, SoldierTemplate template, string name)
    {
        PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(template, name), name);
        squad.AddSquadMember(soldier);
        return soldier;
    }

    private static SoldierTemplate CreateTemplate(int id, string name, byte rank, bool isSquadLeader)
    {
        return new SoldierTemplate(
            id,
            TestModelFactory.HumanSpecies,
            name,
            rank,
            1,
            isSquadLeader,
            0,
            []);
    }

    private static SquadTemplate CreateSquadTemplate(
        string name,
        params (SoldierTemplate Template, byte Min, byte Max)[] elements)
    {
        return CreateSquadTemplate(name, SquadTypes.None, elements);
    }

    private static SquadTemplate CreateSquadTemplate(
        string name,
        SquadTypes squadTypes,
        params (SoldierTemplate Template, byte Min, byte Max)[] elements)
    {
        return new SquadTemplate(
            1,
            name,
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            elements.Select(element => new SquadTemplateElement(element.Template, element.Min, element.Max)).ToList(),
            squadTypes,
            10);
    }
}
