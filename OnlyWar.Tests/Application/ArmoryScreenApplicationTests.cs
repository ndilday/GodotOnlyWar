using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OnlyWar.Application;
using OnlyWar.Campaign;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Operations.Orders;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Application;

/// <summary>
/// Mars pipeline (TDD §6.14): the Armory screen's read model
/// and its three actions: send a brother to Mars, promote within the Techmarine branch (Master
/// Techmarine, Master of the Forge), and set where returnees report. Templates come from the real
/// rules data.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class ArmoryScreenApplicationTests
{
    private static readonly Date Today = new(42, 10, 1);

    private readonly SectorSimulationFixture _fixture = SectorSimulationFixture.Create();
    private readonly Unit _chapter;
    private readonly Squad _armory;
    private readonly Squad _tactical;
    private readonly Ship _flagship;
    private readonly PlayerForce _force;
    private readonly List<PlayerSoldier> _roster = [];
    private int _nextSoldierId = 5000;

    public ArmoryScreenApplicationTests()
    {
        _chapter = new Unit("Chapter", new UnitTemplate(100, "Chapter", true, [], []));
        _armory = new Squad("Armory", _chapter, Doctrine.Armory);
        _chapter.AddSquad(_armory);
        _armory.DutyStation = CampaignLocation.Landed(_fixture.Planet.Regions[9]);
        _tactical = new Squad("First Tactical", _chapter, Doctrine.TacticalSquad);
        _chapter.AddSquad(_tactical);

        Faction faction = _fixture.Sector.PlayerForce.Faction;
        Fleet fleet = new("Fleet", null, null);
        TaskForce taskForce = new(faction) { Planet = _fixture.Planet, Position = _fixture.Planet.Position };
        _flagship = new Ship(77, "Emperor's Wrath", new ShipTemplate(1, "Strike Cruiser", 200, 0, 0))
        {
            IsFlagship = true
        };
        taskForce.Ships.Add(_flagship);
        _flagship.Fleet = taskForce;
        fleet.TaskForces.Add(taskForce);
        _force = new PlayerForce(faction, new Army("Army", null, "Commander", _chapter, []), fleet)
        {
            IsMechanicusLoanActive = true,
            HomeWorldPlanetId = _fixture.Planet.Id
        };
    }

    private ChapterGenerationDoctrine Doctrine => _fixture.Rules.ChapterDoctrine;

    // The Armory's rank between Techmarine and Master of the Forge. It has no doctrine role, so
    // find it the way the Armory screen does: from the Armory squad template's slots.
    private SoldierTemplate MasterTechmarine => Doctrine.Armory.Elements
        .Select(element => element.SoldierTemplate)
        .Single(template => template.SpecialistType == Doctrine.Techmarine.SpecialistType
            && !template.IsSquadLeader
            && template != Doctrine.Techmarine);

    private ArmoryPromotionOption MasterOfTheForgeOption(ArmoryTechmarineRow row) =>
        row.Promotions.SingleOrDefault(option => option.TemplateId == Doctrine.MasterOfTheForge.Id);

    private static ArmoryPromotionOption Option(ArmoryOverview view, PlayerSoldier soldier, SoldierTemplate target) =>
        view.AtHome.Single(row => row.SoldierId == soldier.Id)
            .Promotions.Single(option => option.TemplateId == target.Id);

    [Fact]
    public void Query_ListsTechmarinesAtHome_BrothersOnMars_AndWhoCouldGo()
    {
        PlayerSoldier home = Brother(Doctrine.Techmarine, _armory, tech: 80f);
        PlayerSoldier away = Brother(Doctrine.TacticalMarine, _tactical, tech: 75f);
        PlayerSoldier gifted = Brother(Doctrine.TacticalMarine, _tactical, tech: 90f);
        PlayerSoldier sergeant = Brother(Doctrine.Sergeant, _tactical, tech: 95f);
        Brother(Doctrine.TacticalMarine, _tactical, tech: 50f);
        CampaignApplication application = Install();
        Date departed = Date.FromTotalWeeks(Today.GetTotalWeeks() - 40);
        Departures().Depart(away, _armory, departed);

        ArmoryOverview view = application.QueryArmory();

        Assert.True(view.HasArmory);
        Assert.Equal("Armory", view.Title);
        Assert.Null(view.MasterOfTheForge);
        ArmoryTechmarineRow homeRow = Assert.Single(view.AtHome);
        Assert.Equal(home.Id, homeRow.SoldierId);
        Assert.Equal(Doctrine.Techmarine.Name, homeRow.Role);
        ArmoryMarsRow marsRow = Assert.Single(view.OnMars);
        Assert.Equal(away.Id, marsRow.SoldierId);
        Assert.Equal(MechanicusDepartureService.ExpectedReturnDate(departed).ToString(), marsRow.Returns);
        Assert.Equal(MechanicusDepartureService.TrainingWeeks - 40, marsRow.WeeksRemaining);
        // Only the brother who may go now is listed: the sergeant is held back by the departure
        // rules, and the brother below the Techmarine requirement does not qualify.
        ArmoryMarsCandidateRow candidate = Assert.Single(view.MarsCandidates);
        Assert.Equal(gifted.Id, candidate.SoldierId);
        Assert.Equal(90, candidate.Tech);
        Assert.False(application.DescribeSendToMars(sergeant.Id).CanProceed);
        Assert.True(view.Loan.IsActive);
        Assert.Contains(marsRow.Returns, view.Loan.Summary);
    }

    [Fact]
    public void SendToMars_PostsTheBrother_AndHeMovesToTheMarsList()
    {
        PlayerSoldier gifted = Brother(Doctrine.TacticalMarine, _tactical, tech: 90f);
        Brother(Doctrine.TacticalMarine, _tactical, tech: 40f);
        CampaignApplication application = Install();

        ArmoryPrompt prompt = application.DescribeSendToMars(gifted.Id);
        ArmoryCommandResult result = application.SendToMars(application.SessionToken, gifted.Id);

        Assert.True(prompt.CanProceed);
        Assert.Contains("cannot be recalled", prompt.Message);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(MechanicusTrainingService.IsOnMars(gifted));
        Assert.Same(_armory, gifted.AssignedSquad);
        Assert.Same(Doctrine.Techmarine, gifted.Template);
        Assert.Equal(Today, gifted.IndividualPosting.StartedDate);
        ArmoryOverview view = application.QueryArmory();
        Assert.Equal(gifted.Id, Assert.Single(view.OnMars).SoldierId);
        Assert.Empty(view.MarsCandidates);
    }

    [Fact]
    public void SendToMars_RefusesAnIneligibleBrother_AndChangesNothing()
    {
        PlayerSoldier sergeant = Brother(Doctrine.Sergeant, _tactical, tech: 95f);
        CampaignApplication application = Install();

        ArmoryPrompt prompt = application.DescribeSendToMars(sergeant.Id);
        ArmoryCommandResult result = application.SendToMars(application.SessionToken, sergeant.Id);

        Assert.False(prompt.CanProceed);
        Assert.False(result.Succeeded);
        Assert.Same(_tactical, sergeant.AssignedSquad);
        Assert.Same(Doctrine.Sergeant, sergeant.Template);
        Assert.Null(sergeant.IndividualPosting);
    }

    [Fact]
    public void Commands_FromAReplacedSession_AreRefused()
    {
        PlayerSoldier gifted = Brother(Doctrine.TacticalMarine, _tactical, tech: 90f);
        CampaignApplication application = Install();
        Guid stale = application.SessionToken;
        application.Install(new GameSession(_fixture.Rules, SectorWithForce(), Today, new SeededRNG(3)));

        Assert.False(application.SendToMars(stale, gifted.Id).Succeeded);
        Assert.False(application.Promote(stale, gifted.Id, Doctrine.MasterOfTheForge.Id).Succeeded);
        Assert.False(application.SetMarsReturnDestination(stale, ArmoryDestinationKeys.Ship(_flagship.Id)).Succeeded);
        Assert.Null(gifted.IndividualPosting);
        Assert.Null(_force.MarsReturnDestination);
    }

    [Fact]
    public void MasterOfTheForge_IsPromotedFromTheArmory_AndTheSeatThenCloses()
    {
        PlayerSoldier master = Brother(Doctrine.Techmarine, _armory, tech: 110f, lead: 70f);
        PlayerSoldier other = Brother(Doctrine.Techmarine, _armory, tech: 115f, lead: 75f);
        CampaignApplication application = Install();
        ArmoryOverview before = application.QueryArmory();
        Assert.All(before.AtHome, row => Assert.True(MasterOfTheForgeOption(row).CanPromote));

        Assert.True(application.DescribePromotion(master.Id, Doctrine.MasterOfTheForge.Id).CanProceed);
        ArmoryCommandResult result = application.Promote(
            application.SessionToken, master.Id, Doctrine.MasterOfTheForge.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Same(Doctrine.MasterOfTheForge, master.Template);
        Assert.Same(_armory, master.AssignedSquad);
        Assert.Contains(master.SoldierEvents, entry => entry.Detail.Contains(Doctrine.MasterOfTheForge.Name));
        ArmoryOverview after = application.QueryArmory();
        Assert.Equal(master.Name, after.MasterOfTheForge);
        // With the seat filled, no row offers it, the master himself has nowhere higher to go,
        // and the command is refused.
        Assert.All(after.AtHome, row => Assert.Null(MasterOfTheForgeOption(row)));
        Assert.Empty(after.AtHome.Single(row => row.SoldierId == master.Id).Promotions);
        Assert.False(application.Promote(
            application.SessionToken, other.Id, Doctrine.MasterOfTheForge.Id).Succeeded);
        Assert.Same(Doctrine.Techmarine, other.Template);
    }

    [Fact]
    public void MasterOfTheForge_BelowTheRequirement_IsShownWhyAndRefused()
    {
        PlayerSoldier techmarine = Brother(Doctrine.Techmarine, _armory, tech: 80f, lead: 40f);
        CampaignApplication application = Install();

        ArmoryPromotionOption option = MasterOfTheForgeOption(Assert.Single(application.QueryArmory().AtHome));
        ArmoryCommandResult result = application.Promote(
            application.SessionToken, techmarine.Id, Doctrine.MasterOfTheForge.Id);

        Assert.False(option.CanPromote);
        Assert.Contains("requirements", option.BlockedReason);
        Assert.False(result.Succeeded);
        Assert.Same(Doctrine.Techmarine, techmarine.Template);
    }

    [Fact]
    public void Promotions_AreNotMadeWhileOnMars()
    {
        PlayerSoldier brother = Brother(Doctrine.TacticalMarine, _tactical, tech: 120f, lead: 80f);
        CampaignApplication application = Install();
        Departures().Depart(brother, _armory, Today);

        Assert.Empty(application.QueryArmory().AtHome);
        Assert.False(application.Promote(
            application.SessionToken, brother.Id, Doctrine.MasterOfTheForge.Id).Succeeded);
        Assert.False(application.Promote(
            application.SessionToken, brother.Id, MasterTechmarine.Id).Succeeded);
        Assert.Same(Doctrine.Techmarine, brother.Template);
    }

    // The Armory's middle rank (TDD §6.14): a Techmarine with
    // Tech above the Master Techmarine bar is promoted on the Armory screen, and from there his
    // only rank left is Master of the Forge.
    [Fact]
    public void MasterTechmarine_IsPromotedFromTheArmory()
    {
        PlayerSoldier able = Brother(Doctrine.Techmarine, _armory, tech: 80f, lead: 40f);
        PlayerSoldier short_ = Brother(Doctrine.Techmarine, _armory, tech: 70f, lead: 40f);
        CampaignApplication application = Install();
        ArmoryOverview before = application.QueryArmory();

        Assert.True(Option(before, able, MasterTechmarine).CanPromote);
        ArmoryPromotionOption blocked = Option(before, short_, MasterTechmarine);
        Assert.False(blocked.CanPromote);
        Assert.Contains("requirements", blocked.BlockedReason);

        ArmoryPrompt prompt = application.DescribePromotion(able.Id, MasterTechmarine.Id);
        ArmoryCommandResult result = application.Promote(application.SessionToken, able.Id, MasterTechmarine.Id);

        Assert.True(prompt.CanProceed);
        Assert.True(result.Succeeded, result.Message);
        Assert.Same(MasterTechmarine, able.Template);
        Assert.Same(_armory, able.AssignedSquad);
        Assert.Contains(able.SoldierEvents, entry => entry.Detail.Contains(MasterTechmarine.Name));
        ArmoryTechmarineRow promoted = application.QueryArmory().AtHome.Single(row => row.SoldierId == able.Id);
        Assert.Equal(MasterTechmarine.Name, promoted.Role);
        Assert.Equal([Doctrine.MasterOfTheForge.Id], promoted.Promotions.Select(option => option.TemplateId));
        // A promotion is never sideways or down.
        Assert.False(application.Promote(application.SessionToken, able.Id, MasterTechmarine.Id).Succeeded);
        Assert.False(application.Promote(application.SessionToken, short_.Id, MasterTechmarine.Id).Succeeded);
        Assert.False(application.Promote(application.SessionToken, able.Id, Doctrine.Techmarine.Id).Succeeded);
        Assert.Same(Doctrine.Techmarine, short_.Template);
    }

    [Fact]
    public void ReturnDestination_OffersShipsAndHomeWorldRegions_AndSetsTheOverride()
    {
        Brother(Doctrine.Techmarine, _armory);
        CampaignApplication application = Install();

        ArmoryReturnDestinationView before = application.QueryArmory().ReturnDestination;
        string shipKey = ArmoryDestinationKeys.Ship(_flagship.Id);
        string regionKey = ArmoryDestinationKeys.Region(_fixture.Planet.Regions[3].Id);

        Assert.Equal(ArmoryDestinationKeys.Default, before.SelectedKey);
        Assert.Contains(before.Options, option => option.Key == shipKey);
        Assert.Contains(before.Options, option => option.Key == regionKey);
        // The Home World is not held by the chapter, so the default resolves to the flagship.
        Assert.Contains(_flagship.Name, before.Resolved);

        Assert.True(application.SetMarsReturnDestination(application.SessionToken, regionKey).Succeeded);
        Assert.True(_force.MarsReturnDestination.IsSamePlace(CampaignLocation.Landed(_fixture.Planet.Regions[3])));
        ArmoryReturnDestinationView chosen = application.QueryArmory().ReturnDestination;
        Assert.Equal(regionKey, chosen.SelectedKey);
        Assert.Contains(_fixture.Planet.Regions[3].Name, chosen.Resolved);

        Assert.False(application.SetMarsReturnDestination(application.SessionToken, "ship:99999").Succeeded);
        Assert.True(_force.MarsReturnDestination.IsSamePlace(CampaignLocation.Landed(_fixture.Planet.Regions[3])));

        Assert.True(application.SetMarsReturnDestination(application.SessionToken, ArmoryDestinationKeys.Default).Succeeded);
        Assert.Null(_force.MarsReturnDestination);
    }

    [Fact]
    public void AnOverrideThatNoLongerExists_StaysVisible_AndResolvesToTheDutyStation()
    {
        Brother(Doctrine.Techmarine, _armory);
        _force.MarsReturnDestination = CampaignLocation.Aboard(
            new Ship(88, "Lost Vessel", new ShipTemplate(1, "Strike Cruiser", 200, 0, 0)));
        CampaignApplication application = Install();

        ArmoryReturnDestinationView view = application.QueryArmory().ReturnDestination;

        Assert.Equal(ArmoryDestinationKeys.Ship(88), view.SelectedKey);
        Assert.Contains(view.Options, option => option.Key == view.SelectedKey && option.Label.Contains("unavailable"));
        Assert.Contains("duty station", view.Resolved);
    }

    [Fact]
    public void AChapterWithNoArmory_ShowsAnEmptyScreen()
    {
        CampaignApplication application = TestPersonnelComposition.CreateCampaign(new SeededRNG(7)).CreateApplication();
        Unit bare = new("Chapter", new UnitTemplate(100, "Chapter", true, [], []));
        PlayerForce force = new(_fixture.Sector.PlayerForce.Faction, new Army("Army", null, "Commander", bare, []),
            new Fleet("Fleet", null, null));
        application.Install(new GameSession(_fixture.Rules, new Sector(force, [], [], []), Today, new SeededRNG(8)));

        ArmoryOverview view = application.QueryArmory();

        Assert.False(view.HasArmory);
        Assert.False(application.SendToMars(application.SessionToken, 1).Succeeded);
        application.Close();
        Assert.False(application.QueryArmory().HasArmory);
    }

    // Ordinary promotions happen on the Chapter screen; the Techmarine branch is entered on Mars
    // and promoted on the Armory screen, so the Chapter screen offers neither.
    [Fact]
    public void ChapterScreen_OffersNoTechmarineBranchSlot()
    {
        PlayerSoldier master = Brother(Doctrine.MasterOfTheForge, _armory, tech: 120f, lead: 80f);
        PlayerSoldier techmarine = Brother(Doctrine.Techmarine, _armory, tech: 120f, lead: 80f);
        PlayerSoldier gifted = Brother(Doctrine.TacticalMarine, _tactical, tech: 90f, lead: 80f);
        CampaignApplication application = Install();
        byte branch = Doctrine.Techmarine.SpecialistType;

        // The unrestricted rule would offer them, which is what the restriction removes.
        Assert.Contains(new SoldierTransferService().GetTransferOptions(_chapter, gifted),
            option => option.SoldierTemplate.SpecialistType == branch);

        foreach (PlayerSoldier soldier in new[] { gifted, techmarine })
        {
            ChapterBrowserView view = application.QueryChapterBrowser(
                new ChapterBrowserQuery(null, null, soldier.Id, null, null, null, null));
            Assert.DoesNotContain(view.TransferOptions, option =>
                option.StartsWith(Doctrine.Techmarine.Name) || option.StartsWith(Doctrine.MasterOfTheForge.Name));
        }
        Assert.Same(Doctrine.MasterOfTheForge, master.Template);
    }

    [Fact]
    public void ArmoryBoundaryContainsNoLiveDomainGraph()
    {
        Assert.Empty(FindDomainGraph(typeof(ArmoryOverview)));
        Assert.Empty(FindDomainGraph(typeof(ArmoryPrompt)));
        Assert.Empty(FindDomainGraph(typeof(ArmoryCommandResult)));
    }

    [Fact]
    public void ArmoryControllerUsesOnlyTheApplicationBoundary()
    {
        string controller = System.IO.File.ReadAllText(System.IO.Path.Combine(
            RulesDatabaseFixture.RepositoryRoot, "Scenes", "ArmoryScreen", "ArmoryScreenController.cs"));
        foreach (string bypass in new[] { "PlayerForce", "PlayerSoldier", "CampaignLocation",
            "MechanicusDepartureService", "ArmoryPromotionService", "Squad" })
        {
            Assert.DoesNotContain(bypass, controller);
        }
        Assert.Contains("IArmoryScreenApplication", controller);
        Assert.Contains("SessionChanged -=", controller);
    }

    private CampaignApplication Install()
    {
        CampaignApplication application =
            TestPersonnelComposition.CreateCampaign(new SeededRNG(7)).CreateApplication();
        application.Install(new GameSession(_fixture.Rules, SectorWithForce(), Today, new SeededRNG(8)));
        return application;
    }

    private Sector SectorWithForce() =>
        new(_force, [], [_fixture.Planet], _force.Fleet.TaskForces);

    private MechanicusDepartureService Departures() =>
        new(new OrderCommitmentSurface(), Doctrine.Techmarine, Doctrine.ScoutMarine);

    private PlayerSoldier Brother(SoldierTemplate template, Squad squad, float tech = 70f, float lead = 50f)
    {
        string name = $"Brother {template.Name} {_nextSoldierId}";
        Soldier source = TestModelFactory.CreateSoldier(template, name);
        source.Id = _nextSoldierId++;
        PlayerSoldier soldier = new(source, name);
        soldier.AddEvaluation(new SoldierEvaluation(
            Today, melee: 50, ranged: 50, lead: lead, med: 50, tech: tech, piety: 50, ancient: 50));
        squad.AddSquadMember(soldier);
        _force.Army.PlayerSoldierMap[soldier.Id] = soldier;
        _roster.Add(soldier);
        return soldier;
    }

    private static IReadOnlyList<Type> FindDomainGraph(Type root)
    {
        HashSet<Type> visited = [];
        List<Type> forbidden = [];
        void Inspect(Type type)
        {
            if (!visited.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string)
                || type == typeof(Guid) || type == typeof(decimal)) return;
            if (type.IsArray) { Inspect(type.GetElementType()); return; }
            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments()) Inspect(argument);
                return;
            }
            if (type.Assembly == typeof(Sector).Assembly) { forbidden.Add(type); return; }
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Inspect(property.PropertyType);
        }
        Inspect(root);
        return forbidden;
    }
}
