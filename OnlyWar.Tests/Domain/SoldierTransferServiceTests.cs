using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System.Drawing;
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
    public void GetTransferOptions_OnlyOffersSlotsOfTheSoldiersSpecialistType()
    {
        // Apothecary and Chaplain are distinct specialist tracks; a line/command slot has
        // SpecialistType 0. An Apothecary may only transfer into another Apothecary slot.
        const byte apothecaryType = 1, chaplainType = 4;
        SoldierTemplate apothecary = CreateTemplate(30, "Apothecary", 5, false, apothecaryType);
        SoldierTemplate seniorApothecary = CreateTemplate(31, "Senior Apothecary", 6, false, apothecaryType);
        SoldierTemplate chaplain = CreateTemplate(32, "Chaplain", 5, false, chaplainType);
        SquadTemplate hqTemplate = CreateSquadTemplate(
            "Command Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4),
            (seniorApothecary, 0, 1),
            (chaplain, 0, 1));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", hqTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, apothecary, "Brother Medicae");
        Squad target = AddSquad(chapter, "Target Squad", hqTemplate);
        AddPlayerSoldier(target, TestModelFactory.SergeantTemplate, "Sergeant Titus");

        List<SoldierTransferOption> options = _service.GetTransferOptions(chapter, soldier)
            .Where(option => option.SquadId == target.Id)
            .ToList();

        // The matching-type Apothecary slot is offered...
        Assert.Contains(options, option => option.SoldierTemplate == seniorApothecary);
        // ...but line, command, and other-specialist (Chaplain) slots are not.
        Assert.DoesNotContain(options, option => option.SoldierTemplate == TestModelFactory.MarineTemplate);
        Assert.DoesNotContain(options, option => option.SoldierTemplate == TestModelFactory.SergeantTemplate);
        Assert.DoesNotContain(options, option => option.SoldierTemplate == chaplain);
    }

    [Fact]
    public void GetTransferOptions_OffersSpecialistSlotsToLineBrothers()
    {
        // Becoming a specialist is a one-way door in: a regular marine (SpecialistType 0)
        // may still be drawn into a specialist track (e.g. promoted into a Chaplain slot).
        const byte chaplainType = 4;
        SoldierTemplate chaplain = CreateTemplate(33, "Chaplain", 5, false, chaplainType);
        SquadTemplate hqTemplate = CreateSquadTemplate(
            "Command Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4),
            (chaplain, 0, 1));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", hqTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        Squad target = AddSquad(chapter, "Target Squad", hqTemplate);
        AddPlayerSoldier(target, TestModelFactory.SergeantTemplate, "Sergeant Titus");

        List<SoldierTransferOption> options = _service.GetTransferOptions(chapter, soldier)
            .Where(option => option.SquadId == target.Id)
            .ToList();

        Assert.Contains(options, option => option.SoldierTemplate == chaplain);
    }

    [Fact]
    public void GetTransferOptions_RequiresDestinationTemplateStatAndRatingRequirements()
    {
        SoldierTemplate librarian = CreateTemplate(
            34,
            "Lexicanium",
            5,
            false,
            specialistType: 3,
            requirements:
            [
                new(
                    SoldierTemplateRequirementType.SoldierStat,
                    SoldierTemplateRequirementKeys.PsychicPower,
                    SoldierTemplateRequirementComparison.GreaterThan,
                    0),
                new(
                    SoldierTemplateRequirementType.Rating,
                    RatingKeys.Leadership,
                    SoldierTemplateRequirementComparison.GreaterThan,
                    60)
            ]);
        SoldierTemplate librarianLeader = CreateTemplate(35, "Librarian Leader", 6, true, 3);
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Line Squad", (TestModelFactory.MarineTemplate, 0, 4));
        SquadTemplate librariusTemplate = CreateSquadTemplate(
            "Librarius", (librarianLeader, 0, 1), (librarian, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", lineTemplate);
        PlayerSoldier ordinaryMarine =
            AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        ordinaryMarine.AddEvaluation(new SoldierEvaluation(
            _date, new Dictionary<string, float> { [RatingKeys.Leadership] = 80 }));
        Squad target = AddSquad(chapter, "Librarius", librariusTemplate);
        AddPlayerSoldier(target, librarianLeader, "Master Varro");

        List<SoldierTransferOption> ordinaryOptions =
            _service.GetTransferOptions(chapter, ordinaryMarine);

        Assert.DoesNotContain(ordinaryOptions, option => option.SoldierTemplate == librarian);

        Soldier rawPsyker = TestModelFactory.CreateSoldier(
            TestModelFactory.MarineTemplate, "Brother Psyker");
        rawPsyker.PsychicPower = 1;
        PlayerSoldier qualifiedPsyker = new(rawPsyker, "Brother Psyker");
        qualifiedPsyker.AddEvaluation(new SoldierEvaluation(
            _date, new Dictionary<string, float> { [RatingKeys.Leadership] = 80 }));
        source.AddSquadMember(qualifiedPsyker);

        List<SoldierTransferOption> psykerOptions =
            _service.GetTransferOptions(chapter, qualifiedPsyker);

        Assert.Contains(psykerOptions, option => option.SoldierTemplate == librarian);
    }

    [Fact]
    public void GetTransferOptions_RequiresExistingTrackForSeniorSpecialistTemplate()
    {
        const byte chaplainType = 4;
        SoldierTemplate judiciar = CreateTemplate(
            36,
            "Judiciar",
            4,
            false,
            chaplainType,
            [
                new(
                    SoldierTemplateRequirementType.Rating,
                    RatingKeys.Piety,
                    SoldierTemplateRequirementComparison.GreaterThan,
                    90)
            ]);
        SoldierTemplate chaplain = CreateTemplate(
            37,
            "Chaplain",
            5,
            false,
            chaplainType,
            [
                new(
                    SoldierTemplateRequirementType.Rating,
                    RatingKeys.Piety,
                    SoldierTemplateRequirementComparison.GreaterThan,
                    90),
                new(
                    SoldierTemplateRequirementType.CurrentSpecialistType,
                    SoldierTemplateRequirementKeys.SpecialistType,
                    SoldierTemplateRequirementComparison.Equal,
                    chaplainType)
            ]);
        SoldierTemplate leader = CreateTemplate(38, "Reclusiarch", 6, true, chaplainType);
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Line Squad", (TestModelFactory.MarineTemplate, 0, 4));
        SquadTemplate reclusiumTemplate = CreateSquadTemplate(
            "Reclusium", (leader, 0, 1), (judiciar, 0, 2), (chaplain, 0, 2));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", lineTemplate);
        PlayerSoldier devoutMarine =
            AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        devoutMarine.AddEvaluation(new SoldierEvaluation(
            _date, new Dictionary<string, float> { [RatingKeys.Piety] = 100 }));
        Squad target = AddSquad(chapter, "Reclusium", reclusiumTemplate);
        AddPlayerSoldier(target, leader, "Reclusiarch Varro");

        List<SoldierTransferOption> options =
            _service.GetTransferOptions(chapter, devoutMarine);

        Assert.Contains(options, option => option.SoldierTemplate == judiciar);
        Assert.DoesNotContain(options, option => option.SoldierTemplate == chaplain);
    }

    [Fact]
    public void ApplyTransfer_RechecksDestinationTemplateRequirements()
    {
        SoldierTemplate librarian = CreateTemplate(
            39,
            "Lexicanium",
            5,
            false,
            specialistType: 3,
            requirements:
            [
                new(
                    SoldierTemplateRequirementType.SoldierStat,
                    SoldierTemplateRequirementKeys.PsychicPower,
                    SoldierTemplateRequirementComparison.GreaterThan,
                    0)
            ]);
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Line Squad", (TestModelFactory.MarineTemplate, 0, 4));
        SquadTemplate librariusTemplate = CreateSquadTemplate(
            "Librarius", (librarian, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", lineTemplate);
        PlayerSoldier ordinaryMarine =
            AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        Squad target = AddSquad(chapter, "Librarius", librariusTemplate);
        SoldierTransferOption forgedOption = new(
            target.Id, librarian, "Lexicanium, Librarius, Chapter");

        bool transferred = _service.ApplyTransfer(
            ordinaryMarine,
            forgedOption,
            chapter.GetAllSquads().ToDictionary(squad => squad.Id),
            _date);

        Assert.False(transferred);
        Assert.Same(source, ordinaryMarine.AssignedSquad);
        Assert.Equal(TestModelFactory.MarineTemplate, ordinaryMarine.Template);
    }

    [Fact]
    public void GetTransferOptions_SortsSameRankByCompanyTypeThenSquadName()
    {
        SquadTemplate tacticalTemplate = CreateSquadTemplate(
            "Tactical Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        SquadTemplate scoutTemplate = CreateSquadTemplate(
            "Scout Squad",
            SquadTypes.Scout,
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Unit ninthCompany = CreateUnit("Ninth Company");
        Unit tenthCompany = CreateUnit("Tenth Company");
        AddChildUnit(chapter, ninthCompany);
        AddChildUnit(chapter, tenthCompany);

        Squad source = AddSquad(chapter, "Source Squad", tacticalTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        AddLedSquad(ninthCompany, "Zulu Squad", scoutTemplate);
        AddLedSquad(tenthCompany, "Tactical Squad", tacticalTemplate);
        AddLedSquad(tenthCompany, "Castor Squad", scoutTemplate);
        AddLedSquad(tenthCompany, "Aquila Squad", scoutTemplate);

        List<string> squadNames = _service.GetTransferOptions(chapter, soldier)
            .Where(option => option.SoldierTemplate == TestModelFactory.MarineTemplate)
            .Select(option => chapter.GetAllSquads().Single(squad => squad.Id == option.SquadId).Name)
            .ToList();

        Assert.Equal(
            ["Zulu Squad", "Tactical Squad", "Aquila Squad", "Castor Squad"],
            squadNames);
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
        Assert.Collection(
            soldier.SoldierEvents,
            soldierEvent =>
            {
                Assert.Equal(SoldierEventType.Promotion, soldierEvent.Type);
                Assert.Equal("promoted to Test Sergeant", soldierEvent.Detail);
            },
            soldierEvent =>
            {
                Assert.Equal(SoldierEventType.Transfer, soldierEvent.Type);
                Assert.Equal("transferred to Test Sergeant, Target Squad, Chapter", soldierEvent.Detail);
            });
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
        ship.LoadSquad(source);
        Squad target = AddSquad(chapter, "Empty Target Squad", template);
        Dictionary<int, Squad> squadMap = chapter.GetAllSquads().ToDictionary(squad => squad.Id);
        SoldierTransferOption option = new(target.Id, TestModelFactory.MarineTemplate, "Test Marine, Empty Target Squad, Chapter");

        _service.ApplyTransfer(soldier, option, squadMap, _date);

        Assert.Null(source.BoardedLocation);
        Assert.Equal(ship, target.BoardedLocation);
        Assert.DoesNotContain(source, ship.LoadedSquads);
        Assert.Contains(target, ship.LoadedSquads);
    }

    [Fact]
    public void ApplyTransfer_MovesLandedRosterAndOrderFromEmptiedSourceToDestination()
    {
        SquadTemplate template = CreateSquadTemplate(
            "Line Squad",
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", template);
        PlayerSoldier first = AddPlayerSoldier(
            source, TestModelFactory.MarineTemplate, "Brother Marius");
        PlayerSoldier last = AddPlayerSoldier(
            source, TestModelFactory.MarineTemplate, "Brother Lucian");
        Squad target = AddSquad(chapter, "Empty Target Squad", template);

        Planet planet = new(1, "Macragge", new Coordinate(1, 2), 1, null, 1, 0);
        Region region = new(
            1, planet, 0, "Fortress Region", new RegionCoordinate(0, 0), 0f);
        planet.Regions[0] = region;
        Faction presenceFaction = new(
            1,
            "Planetary Presence",
            Color.Red,
            false,
            false,
            FactionBehavior.None,
            GrowthType.None,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        RegionFaction presence = new(new PlanetFaction(presenceFaction), region);
        region.RegionFactionMap[1] = presence;
        source.CurrentRegion = region;
        presence.LandedSquads.Add(source);

        Order order = new(
            [source], false, true, Aggression.Normal, mission: null);
        Dictionary<int, Squad> squadMap = chapter.GetAllSquads()
            .ToDictionary(squad => squad.Id);
        SoldierTransferOption option = new(
            target.Id,
            TestModelFactory.MarineTemplate,
            "Test Marine, Empty Target Squad, Chapter");

        _service.ApplyTransfer(first, option, squadMap, _date);

        Assert.Contains(source, presence.LandedSquads);
        Assert.Contains(target, presence.LandedSquads);
        Assert.Contains(source, order.AssignedSquads);
        Assert.Contains(target, order.AssignedSquads);
        Assert.Equal(region, target.CurrentRegion);
        Assert.Equal(order, target.CurrentOrders);

        _service.ApplyTransfer(last, option, squadMap, _date);

        Assert.DoesNotContain(source, presence.LandedSquads);
        Assert.Contains(target, presence.LandedSquads);
        Assert.DoesNotContain(source, order.AssignedSquads);
        Assert.Contains(target, order.AssignedSquads);
        Assert.Null(source.CurrentRegion);
        Assert.Null(source.CurrentOrders);
        Assert.Equal(region, target.CurrentRegion);
        Assert.Equal(order, target.CurrentOrders);
    }

    [Fact]
    public void GetTransferOptions_AllowsUnlocatedSourceToReachLocatedOpenSlots()
    {
        SoldierTemplate scout = CreateTemplate(40, "Scout Marine", 1, false);
        SoldierTemplate tacticalMarine = CreateTemplate(41, "Tactical Marine", 2, false);
        SquadTemplate scoutTemplate = CreateSquadTemplate("Scout Squad", (scout, 0, 9));
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Tactical Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (tacticalMarine, 0, 9));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Unlocated Scout Squad", scoutTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, scout, "Scout Marius");
        Squad target = AddSquad(chapter, "Located Tactical Squad", lineTemplate);
        AddPlayerSoldier(target, TestModelFactory.SergeantTemplate, "Sergeant Titus");
        target.BoardedLocation = new Ship(1, "Test Ship", new ShipTemplate(1, "Test Ship Class", 20, 0, 0));

        List<SoldierTransferOption> options = _service.GetTransferOptions(chapter, soldier);

        Assert.Contains(options, option =>
            option.SquadId == target.Id && option.SoldierTemplate == tacticalMarine);
    }

    [Fact]
    public void FormatBlockedTransferTarget_AppendsShipAndLocationForBoardedTarget()
    {
        SquadTemplate template = CreateSquadTemplate("Line Squad", (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad target = AddSquad(chapter, "Target Squad", template);
        Planet planet = new(1, "Macragge", new Coordinate(1, 2), 1, null, 1, 0);
        Ship ship = new(1, "Glory of Hera", new ShipTemplate(1, "Strike Cruiser", 20, 0, 0));
        _ = new TaskForce(1, null, planet.Position, planet, null, [ship]);
        target.BoardedLocation = ship;
        SoldierTransferOption option = new(target.Id, TestModelFactory.MarineTemplate, "Test Marine, Target Squad, Chapter");
        Dictionary<int, Squad> squadMap = chapter.GetAllSquads().ToDictionary(squad => squad.Id);

        string targetDescription = _service.FormatBlockedTransferTarget(option, squadMap);

        Assert.Equal("Test Marine, Target Squad, Chapter (Glory of Hera, orbiting Macragge)", targetDescription);
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
    public void GetTransferOptions_DoesNotOfferNewCompanySquadBeforeHqIsFounded()
    {
        SquadTemplate headquartersTemplate = CreateSquadTemplate(
            "Company HQ",
            SquadTypes.HQ,
            (TestModelFactory.CaptainTemplate, 1, 1));
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Line Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        UnitTemplate companyTemplate = new(
            1,
            "Company Template",
            true,
            headquartersTemplate,
            [new SquadTemplateSlot(lineTemplate, 0, 2)]);
        Unit company = new("Company", companyTemplate);
        Squad source = AddSquad(company, "Source Squad", lineTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");

        Assert.DoesNotContain(_service.GetTransferOptions(company, soldier), option => option.IsNewSquad);

        company.HQSquad.AddSquadMember(
            TestModelFactory.CreateSoldier(TestModelFactory.CaptainTemplate, "Captain Aurelius"));

        Assert.Contains(_service.GetTransferOptions(company, soldier), option => option.IsNewSquad);
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
    public void GetTransferOptions_DoesNotRepeatCompanyForNumberedSquadDisplay()
    {
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Assault Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit company = CreateUnit("Second Company");
        Squad source = AddSquad(company, "Source Squad", lineTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(
            source, TestModelFactory.MarineTemplate, "Brother Marius");
        Squad target = AddSquad(company, "II Assault Squad, 2 Co.", lineTemplate);
        target.FormationOrdinal = 2;
        AddPlayerSoldier(target, TestModelFactory.SergeantTemplate, "Sergeant Titus");

        SoldierTransferOption option = _service
            .GetTransferOptions(company, soldier)
            .Single(candidate => candidate.SquadId == target.Id
                && candidate.SoldierTemplate == TestModelFactory.MarineTemplate);

        Assert.Equal("Test Marine, II Assault Squad, 2 Co.", option.DisplayName);
    }

    [Fact]
    public void ApplyTransfer_CreatesNewSquadAndRetainsEmptiedLineage()
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
        // Stable non-Scout lineages remain available for later reconstitution.
        Assert.Contains(source, company.Squads);
        Assert.True(squadMap.ContainsKey(source.Id));
        Assert.Empty(source.Members);
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

    private static void AddChildUnit(Unit parent, Unit child)
    {
        parent.ChildUnits.Add(child);
        child.ParentUnit = parent;
    }

    private static Squad AddLedSquad(Unit unit, string name, SquadTemplate template)
    {
        Squad squad = AddSquad(unit, name, template);
        AddPlayerSoldier(squad, TestModelFactory.SergeantTemplate, $"{name} Sergeant");
        return squad;
    }

    [Fact]
    public void ApplyTransfer_DoesNotRenameLineSquadAfterItsNewSergeant()
    {
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Tactical Squad",
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", lineTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Quintus");
        Squad target = AddSquad(chapter, "Tactical Squad", lineTemplate);
        Dictionary<int, Squad> squadMap = chapter.GetAllSquads().ToDictionary(squad => squad.Id);
        SoldierTransferOption option = new(target.Id, TestModelFactory.SergeantTemplate,
            "Sergeant, Tactical Squad, Chapter");

        _service.ApplyTransfer(soldier, option, squadMap, _date);

        Assert.Equal("Tactical Squad", target.Name);
    }

    [Fact]
    public void ApplyTransfer_KeepsSpecialistSquadNameWhenSpecialistLeaderArrives()
    {
        // The chapter offices (Apothecarion, Librarius, etc.) are led by a specialist
        // leader template; unlike line squads, they never take their leader's name.
        const byte apothecaryType = 1;
        SoldierTemplate master = CreateTemplate(50, "Master of the Apothecarion", 6, true, apothecaryType);
        SoldierTemplate apothecary = CreateTemplate(51, "Apothecary", 5, false, apothecaryType);
        SquadTemplate apothecarionTemplate = CreateSquadTemplate(
            "Apothecarion",
            (master, 1, 1),
            (apothecary, 0, 50));
        SquadTemplate lineTemplate = CreateSquadTemplate("Line Squad", (TestModelFactory.MarineTemplate, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", lineTemplate);
        PlayerSoldier soldier = AddPlayerSoldier(source, TestModelFactory.MarineTemplate, "Brother Marius");
        Squad apothecarion = AddSquad(chapter, "Apothecarion", apothecarionTemplate);
        Dictionary<int, Squad> squadMap = chapter.GetAllSquads().ToDictionary(squad => squad.Id);
        SoldierTransferOption option = new(apothecarion.Id, master,
            "Master of the Apothecarion, Apothecarion, Chapter");

        _service.ApplyTransfer(soldier, option, squadMap, _date);

        Assert.Equal("Apothecarion", apothecarion.Name);
        Assert.Equal(master, soldier.Template);
    }

    [Fact]
    public void ApplyTransfer_BlocksCompatibilityBearingScoutRoleChange()
    {
        SoldierTemplate scout = CreateTemplate(60, "Scout Marine", 1, false);
        SoldierTemplate devastator = CreateTemplate(61, "Devastator Marine", 2, false);
        SquadTemplate scoutTemplate = CreateSquadTemplate(
            "Scout Squad", SquadTypes.Scout, (scout, 0, 4));
        SquadTemplate devastatorTemplate = CreateSquadTemplate(
            "Devastator Squad", SquadTypes.Heavy, (devastator, 0, 4));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Scout Squad", scoutTemplate);
        Squad target = AddSquad(chapter, "Devastator Squad", devastatorTemplate);
        PlayerSoldier neophyte = AddPlayerSoldier(source, scout, "Neophyte Marius");
        neophyte.GeneticCompatibility = 0.92f;
        SoldierTransferOption option = new(
            target.Id,
            devastator,
            "Devastator Marine, Devastator Squad, Chapter");

        bool transferred = _service.ApplyTransfer(
            neophyte,
            option,
            chapter.GetAllSquads().ToDictionary(squad => squad.Id),
            _date);

        Assert.False(transferred);
        Assert.Same(source, neophyte.AssignedSquad);
        Assert.Equal(scout, neophyte.Template);
    }

    [Fact]
    public void RequiresBlackCarapace_FoundingScoutCanPromoteImmediately()
    {
        SoldierTemplate scout = CreateTemplate(60, "Scout Marine", 1, false);
        SoldierTemplate devastator = CreateTemplate(61, "Devastator Marine", 2, false);
        PlayerSoldier foundingScout = new(
            TestModelFactory.CreateSoldier(scout, "Scout Marius"),
            "Scout Marius");
        SoldierTransferOption option = new(
            42,
            devastator,
            "Devastator Marine, Devastator Squad, Chapter");

        Assert.Null(foundingScout.GeneticCompatibility);
        Assert.False(SoldierTransferService.RequiresBlackCarapace(
            foundingScout, option));
    }

    [Fact]
    public void RequiresBlackCarapace_DoesNotBlockLaterBattleBrotherPromotion()
    {
        SoldierTemplate devastator = CreateTemplate(61, "Devastator Marine", 2, false);
        SoldierTemplate sergeant = CreateTemplate(62, "Sergeant", 3, true);
        SquadTemplate devastatorTemplate = CreateSquadTemplate(
            "Devastator Squad", SquadTypes.Heavy, (devastator, 0, 4), (sergeant, 0, 1));
        Unit chapter = CreateUnit("Chapter");
        Squad squad = AddSquad(chapter, "Devastator Squad", devastatorTemplate);
        PlayerSoldier battleBrother = AddPlayerSoldier(
            squad, devastator, "Brother Marius");
        battleBrother.GeneticCompatibility = 0.92f;
        SoldierTransferOption option = new(
            squad.Id,
            sergeant,
            "Sergeant, Devastator Squad, Chapter");

        Assert.False(SoldierTransferService.RequiresBlackCarapace(
            battleBrother, option));
    }

    [Fact]
    public void GetTransferOptions_AdministrativeHqOffersStaffLeaderSlotsUnderItsCaptain()
    {
        SoldierTemplate captain = CreateTemplate(50, "Captain", 6, true);
        SoldierTemplate scoutSergeant = CreateTemplate(51, "Scout Sergeant", 5, true);
        SquadTemplate administrativeTemplate = CreateSquadTemplate(
            "Scout HQ Squad",
            SquadTypes.HQ | SquadTypes.Scout | SquadTypes.Administrative,
            (captain, 1, 1),
            (scoutSergeant, 0, 50));
        SquadTemplate sourceTemplate = CreateSquadTemplate(
            "Scout Squad",
            (scoutSergeant, 1, 1));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", sourceTemplate);
        PlayerSoldier sergeant = AddPlayerSoldier(source, scoutSergeant, "Sergeant Marius");
        Squad headquarters = AddSquad(chapter, "10th Company HQ", administrativeTemplate);
        headquarters.AddSquadMember(TestModelFactory.CreateSoldier(captain, "Captain Aurelius"));

        List<SoldierTransferOption> options = _service.GetTransferOptions(chapter, sergeant);

        Assert.Contains(options, option =>
            option.SquadId == headquarters.Id
            && option.SoldierTemplate == scoutSergeant);
    }

    [Fact]
    public void GetTransferOptions_EmptyHqSquadOffersOnlyItsCommandSeat()
    {
        SoldierTemplate captain = CreateTemplate(50, "Captain", 6, true);
        SoldierTemplate ancient = CreateTemplate(52, "Ancient", 5, false);
        SoldierTemplate champion = CreateTemplate(53, "Champion", 5, false);
        SoldierTemplate veteranSergeant = CreateTemplate(54, "Veteran Sergeant", 5, true);
        SquadTemplate hqTemplate = CreateSquadTemplate(
            "HQ Squad",
            SquadTypes.HQ | SquadTypes.Administrative,
            (captain, 1, 1),
            (ancient, 1, 1),
            (champion, 1, 1));
        SquadTemplate sourceTemplate = CreateSquadTemplate(
            "Veteran Squad",
            (veteranSergeant, 1, 1));
        Unit chapter = CreateUnit("Chapter");
        Squad source = AddSquad(chapter, "Source Squad", sourceTemplate);
        PlayerSoldier sergeant = AddPlayerSoldier(source, veteranSergeant, "Sergeant Marius");
        Squad headquarters = AddSquad(chapter, "1st Company HQ", hqTemplate);

        List<SoldierTransferOption> options = _service.GetTransferOptions(chapter, sergeant)
            .Where(option => option.SquadId == headquarters.Id)
            .ToList();

        // A leaderless HQ is founded by its Captain; the staff seats stay closed until he
        // is in place. Offering all three at once listed the same empty squad three times
        // in the muster's EMPTY FORMATIONS group.
        SoldierTransferOption only = Assert.Single(options);
        Assert.Equal(captain, only.SoldierTemplate);
    }

    private static PlayerSoldier AddPlayerSoldier(Squad squad, SoldierTemplate template, string name)
    {
        PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(template, name), name);
        squad.AddSquadMember(soldier);
        return soldier;
    }

    private static SoldierTemplate CreateTemplate(int id, string name, byte rank, bool isSquadLeader,
                                                  byte specialistType = 0,
                                                  IReadOnlyList<SoldierTemplateRequirement> requirements = null)
    {
        return new SoldierTemplate(
            id,
            TestModelFactory.HumanSpecies,
            name,
            rank,
            1,
            isSquadLeader,
            specialistType,
            [],
            promotionRequirements: requirements);
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
            squadTypes);
    }
}
