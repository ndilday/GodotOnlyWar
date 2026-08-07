using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Orders;

// Phase 2a of Design/Reference/CasualtyRealism.md, specified in
// Design/Reference/SpecialistAttachment.md: an individual specialist may be attached to an
// operation without his home squad.
//
// Two rules carry most of the weight here and are asserted repeatedly:
//   1. The pointer pair (Order.AttachedSoldiers / PlayerSoldier.AttachedOrder) is never
//      half-set, because OrderAttachment owns both halves.
//   2. The detachment flag is TWO-SIDED. A formation that may lend individuals is also a
//      formation that may never be assigned to an order as a unit.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class OrderAttachmentTests
{
    // A private copy of the fixture squad template carrying the detachment flag. Built here
    // rather than added to TestModelFactory's shared static, which is process-wide.
    private static SquadTemplate DetachableTemplate() => new(
        901,
        "Test Apothecarion",
        TestModelFactory.DefaultWeapons,
        [],
        TestModelFactory.TestArmor,
        [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)],
        SquadTypes.PermitsIndividualDetachment);

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

    private static Order CreateOrder(SectorSimulationFixture fixture, params Squad[] squads)
    {
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        foreach (Squad squad in squads)
        {
            squad.CurrentRegion = fixture.Planet.Regions[0];
        }
        return OrderAssignment.AssignSquadsToMission(
            squads,
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal);
    }

    // ---- pointer-pair symmetry -------------------------------------------------------

    [Fact]
    public void Attach_SetsBothHalvesOfThePointerPair()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Squad line = TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier());
        Order order = CreateOrder(fixture, line);
        PlayerSoldier specialist = CreateSpecialist();
        CreateDetachableSquad("Apothecarion", specialist);

        OrderAttachment.Attach(specialist, order);

        Assert.Same(order, specialist.AttachedOrder);
        Assert.Contains(specialist, order.AttachedSoldiers);
    }

    [Fact]
    public void Attach_IsIdempotentForTheSameOrder()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier()));
        PlayerSoldier specialist = CreateSpecialist();

        OrderAttachment.Attach(specialist, order);
        OrderAttachment.Attach(specialist, order);

        Assert.Single(order.AttachedSoldiers);
    }

    [Fact]
    public void Attach_ToASecondOrder_MovesHimRatherThanDuplicating()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order first = CreateOrder(
            fixture, TestModelFactory.CreateSquad("First", TestModelFactory.CreateSoldier()));
        RegionFaction cult = fixture.AddPublicCult(region: 6, population: 2000, organization: 100);
        Squad second = TestModelFactory.CreateSquad("Second", TestModelFactory.CreateSoldier());
        second.CurrentRegion = fixture.Planet.Regions[0];
        Order secondOrder = OrderAssignment.AssignSquadsToMission(
            [second],
            fixture.Planet.Regions[6],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            cult.PlanetFaction.Faction.Id,
            Aggression.Normal);
        PlayerSoldier specialist = CreateSpecialist();

        OrderAttachment.Attach(specialist, first);
        OrderAttachment.Attach(specialist, secondOrder);

        Assert.Empty(first.AttachedSoldiers);
        Assert.Same(secondOrder, specialist.AttachedOrder);
        Assert.Contains(specialist, secondOrder.AttachedSoldiers);
    }

    [Fact]
    public void Detach_ClearsBothHalves_AndIsSafeOnAnUnattachedSoldier()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier()));
        PlayerSoldier specialist = CreateSpecialist();
        OrderAttachment.Attach(specialist, order);

        OrderAttachment.Detach(specialist);
        OrderAttachment.Detach(specialist);

        Assert.Null(specialist.AttachedOrder);
        Assert.Empty(order.AttachedSoldiers);
    }

    [Fact]
    public void ReleaseAll_ClearsEveryAttachment()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier()));
        PlayerSoldier first = CreateSpecialist("Apothecary");
        PlayerSoldier second = CreateSpecialist("Techmarine");
        OrderAttachment.Attach(first, order);
        OrderAttachment.Attach(second, order);

        OrderAttachment.ReleaseAll(order);

        Assert.Empty(order.AttachedSoldiers);
        Assert.Null(first.AttachedOrder);
        Assert.Null(second.AttachedOrder);
    }

    // The load-bearing negative: a detached specialist is still on his squad's roll.
    // Removing him from Members would set Soldier.SquadId null, which the loader reads as
    // "fallen brother" -- he would come back from a save dead.
    [Fact]
    public void Attach_LeavesHimOnHisHomeSquadsRoll()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier()));
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);

        OrderAttachment.Attach(specialist, order);

        Assert.Contains(specialist, home.Members);
        Assert.Same(home, specialist.AssignedSquad);
    }

    // ---- CanAttach guards -----------------------------------------------------------

    [Fact]
    public void CanAttach_AcceptsACoLocatedMemberOfADetachableFormation()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region origin = fixture.Planet.Regions[0];
        Squad line = TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier());
        Order order = CreateOrder(fixture, line);
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = origin;

        Assert.True(OrderAttachment.CanAttach(specialist, order, origin, out string reason));
        Assert.Null(reason);
    }

    [Fact]
    public void CanAttach_RejectsAMemberOfALineSquad()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region origin = fixture.Planet.Regions[0];
        Order order = CreateOrder(
            fixture, TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier()));
        PlayerSoldier trooper = CreateSpecialist("Brother Trooper");
        Squad line = TestModelFactory.CreateSquad("Second Line");
        line.AddSquadMember(trooper);
        line.CurrentRegion = origin;

        Assert.False(OrderAttachment.CanAttach(trooper, order, origin, out string reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void CanAttach_RejectsSomeoneAlreadyAttachedToAnotherOperation()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region origin = fixture.Planet.Regions[0];
        Order order = CreateOrder(
            fixture, TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier()));
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = origin;
        Order elsewhere = new([], true, false, Aggression.Normal, order.Mission);
        OrderAttachment.Attach(specialist, elsewhere);

        Assert.False(OrderAttachment.CanAttach(specialist, order, origin, out _));
        // ...but re-offering him to the order he is already on is fine.
        Assert.True(OrderAttachment.CanAttach(specialist, elsewhere, origin, out _));
    }

    [Fact]
    public void CanAttach_RejectsAManWhoIsNotCombatEffective()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region origin = fixture.Planet.Regions[0];
        Order order = CreateOrder(
            fixture, TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier()));
        Soldier wounded = TestModelFactory.CreateSoldier(name: "Brother Casualty");
        HitLocation vital = wounded.Body.HitLocations.First(l => l.Template.IsVital);
        vital.Wounds = new Wounds(vital.Template.CrippleWound, 0);
        PlayerSoldier specialist = new(wounded, "Brother Casualty");
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = origin;

        Assert.False(specialist.IsCombatEffective);
        Assert.False(OrderAttachment.CanAttach(specialist, order, origin, out _));
    }

    [Fact]
    public void CanAttach_RejectsAManWhoIsNotWithTheForce()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier()));
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = fixture.Planet.Regions[9];

        Assert.False(OrderAttachment.CanAttach(
            specialist, order, fixture.Planet.Regions[0], out _));
    }

    // ---- the two-sided flag: these formations never deploy as units ------------------

    [Fact]
    public void AssignSquadsToMission_RejectsASquadWhoseTemplatePermitsDetachment()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Squad apothecarion = CreateDetachableSquad(
            "Apothecarion", TestModelFactory.CreateSoldier());
        apothecarion.CurrentRegion = fixture.Planet.Regions[0];

        Order order = OrderAssignment.AssignSquadsToMission(
            [apothecarion],
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal);

        Assert.Null(order);
        Assert.Null(apothecarion.CurrentOrders);
        // ...and it stays operational, because surgery staffing and recruitment gate on that.
        Assert.True(apothecarion.IsOperational);
        Assert.False(SpecialistAvailability.IsDeployableFormation(apothecarion));
    }

    // ---- order issue with specialists -----------------------------------------------

    [Fact]
    public void AssignSquadsToMission_AttachesTheSuppliedSpecialists()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Region origin = fixture.Planet.Regions[0];
        Squad line = TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier());
        line.CurrentRegion = origin;
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = origin;

        Order order = OrderAssignment.AssignSquadsToMission(
            [line],
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal,
            [specialist]);

        Assert.NotNull(order);
        Assert.Same(order, specialist.AttachedOrder);
        Assert.Contains(specialist, order.AttachedSoldiers);
        Assert.Single(order.AssignedSquads);
    }

    [Fact]
    public void AssignSquadsToMission_AnIneligibleSpecialistRejectsTheWholeIssue()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Region origin = fixture.Planet.Regions[0];
        Squad line = TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier());
        line.CurrentRegion = origin;
        // A line-squad member is not detachable, so this must create nothing at all.
        PlayerSoldier trooper = CreateSpecialist("Brother Trooper");
        Squad otherLine = TestModelFactory.CreateSquad("Second Line");
        otherLine.AddSquadMember(trooper);
        otherLine.CurrentRegion = origin;

        Order order = OrderAssignment.AssignSquadsToMission(
            [line],
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal,
            [trooper]);

        Assert.Null(order);
        Assert.Null(line.CurrentOrders);
        Assert.Null(trooper.AttachedOrder);
        Assert.Empty(fixture.Sector.Orders);
    }

    // The invariant several sites depend on: an order always has at least one squad, so a
    // specialists-only order is never created. TurnController and PlanetForwardSimulator
    // partition orders on AssignedSquads.Any() and would silently drop one.
    [Fact]
    public void AssignSquadsToMission_SpecialistsWithNoSquad_CreatesNoOrder()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        home.CurrentRegion = fixture.Planet.Regions[0];

        Order order = OrderAssignment.AssignSquadsToMission(
            [],
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal,
            [specialist]);

        Assert.Null(order);
        Assert.Null(specialist.AttachedOrder);
        Assert.Empty(fixture.Sector.Orders);
    }

    // ---- release paths ---------------------------------------------------------------

    [Fact]
    public void UnassignSquads_RemovingTheLastSquad_ReleasesTheAttachedSpecialists()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Squad line = TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier());
        Order order = CreateOrder(fixture, line);
        PlayerSoldier specialist = CreateSpecialist();
        CreateDetachableSquad("Apothecarion", specialist);
        OrderAttachment.Attach(specialist, order);

        OrderAssignment.UnassignSquads([line]);

        Assert.Null(specialist.AttachedOrder);
        Assert.Empty(order.AttachedSoldiers);
        Assert.Empty(fixture.Sector.Orders);
    }

    [Fact]
    public void UnassignSpecialists_RecallsTheManButLeavesTheOrderStanding()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Squad line = TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier());
        Order order = CreateOrder(fixture, line);
        PlayerSoldier specialist = CreateSpecialist();
        CreateDetachableSquad("Apothecarion", specialist);
        OrderAttachment.Attach(specialist, order);

        Assert.True(OrderAssignment.UnassignSpecialists([specialist]));

        Assert.Null(specialist.AttachedOrder);
        Assert.Empty(order.AttachedSoldiers);
        Assert.Same(order, line.CurrentOrders);
        Assert.Single(fixture.Sector.Orders);
    }

    [Fact]
    public void AdministrativeSquad_RecallsItsAttachedMembers()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Order order = CreateOrder(
            fixture, TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier()));
        PlayerSoldier specialist = CreateSpecialist();
        Squad home = CreateDetachableSquad("Apothecarion", specialist);
        OrderAttachment.Attach(specialist, order);

        home.IsAdministrative = true;

        Assert.Null(specialist.AttachedOrder);
        Assert.Empty(order.AttachedSoldiers);
    }

    // ---- display and preflight -------------------------------------------------------

    [Fact]
    public void InboundOrderSummary_ReportsTheAttachedCount()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Squad line = TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier());
        Order order = CreateOrder(fixture, line);
        PlayerSoldier specialist = CreateSpecialist();
        CreateDetachableSquad("Apothecarion", specialist);
        OrderAttachment.Attach(specialist, order);

        InboundOrderInfo inbound = Assert.Single(
            InboundOrders.ForRegion(fixture.Planet.Regions[5]));

        Assert.Equal(1, inbound.AttachedCount);
        Assert.Contains("+1 attached", inbound.SummaryLabel);
    }

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
        Squad line = TestModelFactory.CreateSquad("Line", TestModelFactory.CreateSoldier());
        line.CurrentRegion = origin;
        playerRegionFaction.LandedSquads.Add(line);
        Order order = CreateOrder(fixture, line);
        OrderAttachment.Attach(committed, order);

        IReadOnlyList<SpecialistOption> fresh =
            SpecialistAvailability.EnumerateCandidates(playerRegionFaction, origin);
        IReadOnlyList<SpecialistOption> editing =
            SpecialistAvailability.EnumerateCandidates(playerRegionFaction, origin, order);

        // Issuing a new order: the committed man is not on offer for a second one.
        Assert.Equal(["Free Apothecary"], fresh.Select(o => o.Soldier.Name).ToArray());
        // Re-opening the order he is already on: he is selectable so he can be released.
        Assert.Equal(2, editing.Count);
        // Line-squad members never appear at all.
        Assert.DoesNotContain(editing, o => o.HomeSquad == line);
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
