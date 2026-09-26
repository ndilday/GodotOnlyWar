using OnlyWar.Medical.Readiness;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain.Missions;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Operations.Orders;
using OnlyWar.Operations.Personnel;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Orders;

// Phase 2a of Design/Reference/CasualtyRealism.md, specified in
// Design/Reference/SpecialistAttachment.md: an individual specialist may be attached to an
// operation without his home squad.
//
// Two rules carry most of the weight here and are asserted repeatedly:
//   1. The pointer pair (Order.AssignedCharacters / PlayerSoldier.CurrentOrder) is never
//      half-set, because OrderForceService owns both halves.
//   2. The individual-deployment capability is TWO-SIDED. A formation that may lend individuals is also a
//      formation that may never be assigned to an order as a unit.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class OrderAttachmentTests
{
    // A private copy of the fixture squad template carrying the member-only capability. Built here
    // rather than added to TestModelFactory's shared static, which is process-wide.
    private static SquadTemplate DetachableTemplate() => new(
        901,
        "Test Apothecarion",
        TestModelFactory.DefaultWeapons,
        [],
        TestModelFactory.TestArmor,
        [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)],
        SquadTypes.Administrative,
        FormationMobilityPolicy.MembersOnly);

    private static Squad CreateDetachableSquad(string name, params ISoldier[] soldiers)
    {
        Squad squad = new(name, null, DetachableTemplate());
        foreach (ISoldier soldier in soldiers)
        {
            squad.AddSquadMember(soldier);
        }
        return squad;
    }

    private static PlayerSoldier CreateSpecialist(string name = "Brother Apothecary")
    {
        return new PlayerSoldier(TestModelFactory.CreateSoldier(name: name), name);
    }

    private static Squad CreateLineSquad(string name) =>
        TestModelFactory.CreateSquad(
            name, TestModelFactory.CreateSoldier(TestModelFactory.SergeantTemplate));

    private static Order CreateOrder(SectorSimulationFixture fixture, params Squad[] squads)
    {
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        foreach (Squad squad in squads)
        {
            squad.CurrentRegion = fixture.Planet.Regions[0];
        }
        Order order = OrderAssignment.AssignParticipantsToMission(
            fixture.OrderCommands,
            squads,
            [],
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal);
        Assert.NotNull(order);
        return order;
    }

    // Attaches through the production mutation boundary and fails the test if it refuses.
    private static void Attach(PlayerSoldier soldier, Order order) =>
        Assert.True(OrderForceService.AssignCharacter(
            order, soldier, new MedicalReadinessDecisions()));

    // The availability decision production consults before attaching a character to an order:
    // the Region Ops roster (SpecialistAvailability) and order issue (OrderAssignment) both ask it.
    private static PersonnelAvailabilityDecision EvaluateAssignment(
        PlayerSoldier soldier, Order order, Region origin) =>
        TestPersonnelComposition.CreatePersonnel().EvaluateOrderAssignment(
            PersonnelAvailabilityProjection.ForOrderAssignment(
                soldier, order, origin, order?.AssignedSquads));

    // ---- pointer-pair symmetry -------------------------------------------------------

    [Fact]
    public void AssignCharacter_SetsBothHalvesOfThePointerPair()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Squad line = CreateLineSquad("Line");
        Order order = CreateOrder(fixture, line);
        PlayerSoldier specialist = CreateSpecialist();
        CreateDetachableSquad("Apothecarion", specialist);

        Attach(specialist, order);

        Assert.Same(order, specialist.CurrentOrder);
        Assert.Contains(specialist, order.AssignedCharacters);
    }

    [Fact]
    public void AssignCharacter_IsIdempotentForTheSameOrder()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, CreateLineSquad("Line"));
        PlayerSoldier specialist = CreateSpecialist();
        CreateDetachableSquad("Apothecarion", specialist);

        Attach(specialist, order);
        Attach(specialist, order);

        Assert.Single(order.AssignedCharacters);
    }

    // One man, one operation: he must be recalled before he can join a second one.
    [Fact]
    public void AssignCharacter_ToASecondOrder_IsRefusedAndLeavesHimWhereHeWas()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order first = CreateOrder(
            fixture, CreateLineSquad("First"));
        RegionFaction cult = fixture.AddPublicCult(region: 6, population: 2000, organization: 100);
        Squad second = CreateLineSquad("Second");
        second.CurrentRegion = fixture.Planet.Regions[0];
        Order secondOrder = OrderAssignment.AssignParticipantsToMission(
            fixture.OrderCommands,
            [second],
            [],
            fixture.Planet.Regions[6],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            cult.PlanetFaction.Faction.Id,
            Aggression.Normal);
        Assert.NotNull(secondOrder);
        PlayerSoldier specialist = CreateSpecialist();
        CreateDetachableSquad("Apothecarion", specialist);
        Attach(specialist, first);

        Assert.False(OrderForceService.AssignCharacter(
            secondOrder, specialist, new MedicalReadinessDecisions()));

        Assert.Same(first, specialist.CurrentOrder);
        Assert.Contains(specialist, first.AssignedCharacters);
        Assert.Empty(secondOrder.AssignedCharacters);
    }

    [Fact]
    public void RemoveCharacter_ClearsBothHalves_AndIsSafeOnAnUnattachedSoldier()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, CreateLineSquad("Line"));
        PlayerSoldier specialist = CreateSpecialist();
        CreateDetachableSquad("Apothecarion", specialist);
        Attach(specialist, order);

        Assert.True(OrderForceService.RemoveCharacter(specialist));
        Assert.False(OrderForceService.RemoveCharacter(specialist));

        Assert.Null(specialist.CurrentOrder);
        Assert.Empty(order.AssignedCharacters);
    }

    [Fact]
    public void ReleaseOrder_ClearsEveryAttachment()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Squad line = CreateLineSquad("Line");
        Order order = CreateOrder(fixture, line);
        PlayerSoldier first = CreateSpecialist("Apothecary");
        PlayerSoldier second = CreateSpecialist("Techmarine");
        CreateDetachableSquad("Apothecarion", first, second);
        Attach(first, order);
        Attach(second, order);

        OrderForceService.ReleaseOrder(order);

        Assert.Empty(order.AssignedCharacters);
        Assert.Null(first.CurrentOrder);
        Assert.Null(second.CurrentOrder);
        Assert.Null(line.CurrentOrders);
        Assert.Empty(fixture.Sector.Orders);
    }

    // The load-bearing negative: a detached specialist is still on his squad's roll.
    // Removing him from Members would set Soldier.SquadId null, which the loader reads as
    // "fallen brother" -- he would come back from a save dead.
    [Fact]
    public void AssignCharacter_LeavesHimOnHisHomeSquadsRoll()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, CreateLineSquad("Line"));
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);

        Attach(specialist, order);

        Assert.Contains(specialist, home.Members);
        Assert.Same(home, specialist.AssignedSquad);
    }

    // ---- order-assignment availability guards ----------------------------------------

    [Fact]
    public void OrderAssignment_AcceptsACoLocatedMemberOfADetachableFormation()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region origin = fixture.Planet.Regions[0];
        Squad line = CreateLineSquad("Line");
        Order order = CreateOrder(fixture, line);
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = origin;

        PersonnelAvailabilityDecision decision = EvaluateAssignment(specialist, order, origin);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void OrderAssignment_RejectsAMemberOfALineSquad()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region origin = fixture.Planet.Regions[0];
        Order order = CreateOrder(
            fixture, CreateLineSquad("Line"));
        PlayerSoldier trooper = CreateSpecialist("Brother Trooper");
        Squad line = TestModelFactory.CreateSquad("Second Line");
        line.AddSquadMember(trooper);
        line.CurrentRegion = origin;

        PersonnelAvailabilityDecision decision = EvaluateAssignment(trooper, order, origin);

        Assert.False(decision.IsAllowed);
        Assert.Equal(
            (int)PersonnelAvailabilityReasonCode.NoAdministrativeFormation, decision.ReasonCode);
        Assert.NotNull(decision.Reason);
    }

    [Fact]
    public void OrderAssignment_RejectsSomeoneAlreadyAttachedToAnotherOperation()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region origin = fixture.Planet.Regions[0];
        Order order = CreateOrder(
            fixture, CreateLineSquad("Line"));
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = origin;
        Order elsewhere = new([], true, false, Aggression.Normal, order.Mission);
        Attach(specialist, elsewhere);

        PersonnelAvailabilityDecision decision = EvaluateAssignment(specialist, order, origin);

        Assert.False(decision.IsAllowed);
        Assert.Equal(
            (int)PersonnelAvailabilityReasonCode.AssignedElsewhere, decision.ReasonCode);
        // ...but re-offering him to the order he is already on is fine.
        Assert.True(EvaluateAssignment(specialist, elsewhere, origin).IsAllowed);
    }

    [Fact]
    public void OrderAssignment_RejectsAManWhoIsNotCombatEffective()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region origin = fixture.Planet.Regions[0];
        Order order = CreateOrder(
            fixture, CreateLineSquad("Line"));
        Soldier wounded = TestModelFactory.CreateSoldier(name: "Brother Casualty");
        HitLocation vital = wounded.Body.HitLocations.First(l => l.Template.IsVital);
        vital.Wounds = new Wounds(vital.Template.CrippleWound, 0);
        PlayerSoldier specialist = new(wounded, "Brother Casualty");
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = origin;

        Assert.False(specialist.IsCombatEffective);
        PersonnelAvailabilityDecision decision = EvaluateAssignment(specialist, order, origin);

        Assert.False(decision.IsAllowed);
        Assert.Equal(
            (int)PersonnelAvailabilityReasonCode.NotCombatEffective, decision.ReasonCode);
    }

    [Fact]
    public void OrderAssignment_RejectsAManWhoIsNotWithTheForce()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, CreateLineSquad("Line"));
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        // Region 9 is neither the origin (region 0) nor adjacent to it.
        home.CurrentRegion = fixture.Planet.Regions[9];

        PersonnelAvailabilityDecision decision =
            EvaluateAssignment(specialist, order, fixture.Planet.Regions[0]);

        Assert.False(decision.IsAllowed);
        Assert.Equal(
            (int)PersonnelAvailabilityReasonCode.NotAtOrigin, decision.ReasonCode);
    }

    // ---- the two-sided flag: these formations never deploy as units ------------------

    [Fact]
    public void AssignParticipantsToMission_RejectsASquadWhoseTemplatePermitsDetachment()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Squad apothecarion = CreateDetachableSquad(
            "Apothecarion", TestModelFactory.CreateSoldier());
        apothecarion.CurrentRegion = fixture.Planet.Regions[0];

        Order order = OrderAssignment.AssignParticipantsToMission(
            fixture.OrderCommands,
            [apothecarion],
            [],
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal);

        Assert.Null(order);
        Assert.Null(apothecarion.CurrentOrders);
        // ...and it stays operational, because surgery staffing and recruitment gate on that.
        Assert.True(apothecarion.MayProvideLocalSupport);
        Assert.False(SpecialistAvailability.IsDeployableFormation(apothecarion));
    }

    // ---- order issue with specialists -----------------------------------------------

    [Fact]
    public void AssignParticipantsToMission_AttachesTheSuppliedSpecialists()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Region origin = fixture.Planet.Regions[0];
        Squad line = CreateLineSquad("Line");
        line.CurrentRegion = origin;
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = origin;

        Order order = OrderAssignment.AssignParticipantsToMission(
            fixture.OrderCommands,
            [line],
            [specialist],
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal);

        Assert.NotNull(order);
        Assert.Same(order, specialist.CurrentOrder);
        Assert.Contains(specialist, order.AssignedCharacters);
        Assert.Single(order.AssignedSquads);
    }

    [Fact]
    public void AssignParticipantsToMission_AnIneligibleSpecialistRejectsTheWholeIssue()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Region origin = fixture.Planet.Regions[0];
        Squad line = CreateLineSquad("Line");
        line.CurrentRegion = origin;
        // A line-squad member is not detachable, so this must create nothing at all.
        PlayerSoldier trooper = CreateSpecialist("Brother Trooper");
        Squad otherLine = TestModelFactory.CreateSquad("Second Line");
        otherLine.AddSquadMember(trooper);
        otherLine.CurrentRegion = origin;

        Order order = OrderAssignment.AssignParticipantsToMission(
            fixture.OrderCommands,
            [line],
            [trooper],
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal);

        Assert.Null(order);
        Assert.Null(line.CurrentOrders);
        Assert.Null(trooper.CurrentOrder);
        Assert.Empty(fixture.Sector.Orders);
    }

    // The invariant several sites depend on: an order always has at least one squad, so a
    // specialists-only order is never created. TurnController and PlanetForwardSimulator
    // partition orders on AssignedSquads.Any() and would silently drop one.
    [Fact]
    public void AssignParticipantsToMission_SpecialistsWithNoSquad_CreatesNoOrder()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = fixture.Planet.Regions[0];

        Order order = OrderAssignment.AssignParticipantsToMission(
            fixture.OrderCommands,
            [],
            [specialist],
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal);

        Assert.Null(order);
        Assert.Null(specialist.CurrentOrder);
        Assert.Empty(fixture.Sector.Orders);
    }

    // ---- release paths ---------------------------------------------------------------

    [Fact]
    public void UnassignSquads_RemovingTheLastSquad_ReleasesTheAttachedSpecialists()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Squad line = CreateLineSquad("Line");
        Order order = CreateOrder(fixture, line);
        PlayerSoldier specialist = CreateSpecialist();
        CreateDetachableSquad("Apothecarion", specialist);
        Attach(specialist, order);

        OrderAssignment.UnassignSquads([line]);

        Assert.Null(specialist.CurrentOrder);
        Assert.Empty(order.AssignedCharacters);
        Assert.Empty(fixture.Sector.Orders);
    }

    [Fact]
    public void RemoveCharacter_RecallsTheManButLeavesTheOrderStanding()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Squad line = CreateLineSquad("Line");
        Order order = CreateOrder(fixture, line);
        PlayerSoldier specialist = CreateSpecialist();
        CreateDetachableSquad("Apothecarion", specialist);
        Attach(specialist, order);

        Assert.True(OrderForceService.RemoveCharacter(specialist));

        Assert.Null(specialist.CurrentOrder);
        Assert.Empty(order.AssignedCharacters);
        Assert.Same(order, line.CurrentOrders);
        Assert.Single(fixture.Sector.Orders);
    }

    [Fact]
    public void AdministrativeSquad_RecallsItsAttachedMembers()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, CreateLineSquad("Line"));
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        Attach(specialist, order);

        Assert.True(home.SquadTemplate.IsAdministrative);
        Assert.Same(order, specialist.CurrentOrder);
        Assert.Contains(specialist, order.AssignedCharacters);
    }

    // ---- display ---------------------------------------------------------------------

    [Fact]
    public void SpecialistAvailability_OffersDetachableMembersAndExcludesTheAlreadyCommitted()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region origin = fixture.Planet.Regions[0];
        RegionFaction playerRegionFaction = fixture.Sector.PlayerForce.Faction != null
            ? EnsurePlayerPresence(fixture, origin)
            : null;
        PlayerSoldier free = CreateSpecialist("Free Apothecary");
        PlayerSoldier committed = CreateSpecialist("Committed Apothecary");
        Squad home = CreateDetachableSquad("Apothecarion", free, committed);
        home.CurrentRegion = origin;
        playerRegionFaction.LandedSquads.Add(home);
        Squad line = CreateLineSquad("Line");
        line.CurrentRegion = origin;
        playerRegionFaction.LandedSquads.Add(line);
        Order order = CreateOrder(fixture, line);
        Attach(committed, order);

        List<SpecialistOption> fresh =
            SpecialistAvailability.EnumerateRoster(
                    playerRegionFaction, origin, fixture.ChapterRoster,
                    personnel: TestPersonnelComposition.CreatePersonnel())
                .Where(option => option.IsAvailable)
                .ToList();
        List<SpecialistOption> editing =
            SpecialistAvailability.EnumerateRoster(
                    playerRegionFaction, origin, fixture.ChapterRoster, order,
                    personnel: TestPersonnelComposition.CreatePersonnel())
                .Where(option => option.IsAvailable)
                .ToList();

        IReadOnlyList<SpecialistOption> roster =
            SpecialistAvailability.EnumerateRoster(
                playerRegionFaction, origin, fixture.ChapterRoster,
                personnel: TestPersonnelComposition.CreatePersonnel());

        // Issuing a new order: the committed man is not on offer for a second one.
        Assert.Equal(["Free Apothecary"], fresh.Select(o => o.Soldier.Name).ToArray());
        // Re-opening the order he is already on: he is selectable so he can be released.
        Assert.Equal(2, editing.Count);
        // The region roster still shows him as a selectable row, but he is not available for a
        // second attachment.
        SpecialistOption committedRow = Assert.Single(
            roster, option => option.Soldier == committed);
        Assert.False(committedRow.IsAvailable);
        Assert.True(committedRow.IsSelectable);
        Assert.Equal("Region 5", committedRow.StatusLabel);
        // Line-squad members never appear at all.
        Assert.DoesNotContain(editing, o => o.HomeSquad == line);
    }

    [Fact]
    public void SpecialistAvailability_LabelIncludesRankTitleBeforeHomeSquad()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region origin = fixture.Planet.Regions[0];
        RegionFaction playerRegionFaction = EnsurePlayerPresence(fixture, origin);
        PlayerSoldier specialist = CreateSpecialist("Brother Apothecary");
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = origin;
        playerRegionFaction.LandedSquads.Add(home);

        SpecialistOption option = Assert.Single(
            SpecialistAvailability.EnumerateRoster(
                playerRegionFaction, origin, fixture.ChapterRoster,
                personnel: TestPersonnelComposition.CreatePersonnel()),
            candidate => candidate.IsAvailable);

        Assert.Equal("Brother Apothecary | Test Marine | Apothecarion", option.Label);
    }

    private static RegionFaction EnsurePlayerPresence(
        SectorSimulationFixture fixture, Region region)
    {
        int playerFactionId = fixture.Sector.PlayerForce.Faction.Id;
        if (!region.RegionFactionMap.TryGetValue(playerFactionId, out RegionFaction rf))
        {
            if (!fixture.Planet.PlanetFactionMap.TryGetValue(
                    playerFactionId, out PlanetFaction pf))
            {
                pf = new PlanetFaction(fixture.Sector.PlayerForce.Faction) { IsPublic = true };
                fixture.Planet.PlanetFactionMap[playerFactionId] = pf;
            }
            rf = new RegionFaction(pf, region);
            region.RegionFactionMap[playerFactionId] = rf;
        }
        return rf;
    }
}
