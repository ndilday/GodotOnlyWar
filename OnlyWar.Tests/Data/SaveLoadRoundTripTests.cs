using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Orders;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Models.Supply;
using OnlyWar.Models.Equippables;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

// End-to-end save/load coverage (TDD 9.2.1 #1). Generates a real new-game sector,
// writes it through GameStateDataAccess.SaveData, reads it back through GetData, and
// asserts the high-level state survives the round trip. This is the regression guard
// for schema drift: any future change to the save schema that is not propagated to
// both SaveData and GetData will fail here.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class SaveLoadRoundTripTests
{
    private readonly GameRulesData _data;
    private readonly Date _date = new(39, 500, 1);
    private readonly GameStateRoundTripFixture _roundTrip;

    public SaveLoadRoundTripTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        // These tests exercise the save/load schema, not sector generation at scale: a
        // handful of planets stresses every persisted feature just as well as the full
        // 200x200 production sector and generates far faster. The 20x20 grid stays within
        // one subsector (MaxSubsectorCellDiameter = 20), so only one planet becomes a
        // governance capital and the promised-world selection always has eligible worlds.
        _data.OverrideSectorGeometryForTesting(new Coordinate(20, 20), 0.02f);
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, null);
        _roundTrip = new GameStateRoundTripFixture(_data, _date);
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void SaveThenLoad_MutatedGeneratedSector_PreservesRoundTripFeatures()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        _roundTrip.RegisterPlayerArmy(sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;

        sector.PlayerForce.Army.Requisition = 777;
        sector.PlayerForce.GeneseedStockpile = 13;
        sector.PlayerForce.GeneseedPurity = 0.83f;

        Character pledgeAuthority = sector.Characters.First(character => character.Id >= 0);
        Planet pledgeSource = sector.Planets.Values.First(planet => planet.Id >= 0);
        pledgeAuthority.Competence = 0.37f;
        pledgeAuthority.Severity = 0.82f;
        RegionFaction civilState = pledgeSource.Regions
            .SelectMany(region => region.RegionFactionMap.Values)
            .First(regionFaction => regionFaction.Population - regionFaction.Garrison >= 1_234);
        civilState.Contentment = 27.5f;
        civilState.ArmedCivilians = 1_234;
        civilState.HasEmergenceAdvantage = true;
        int capitalRegionId = pledgeSource.CapitalRegionId;
        Pledge originalPledge = new(
            0,
            pledgeSource.Id,
            pledgeAuthority.Id,
            PledgePayload.Requisition(75),
            PledgeScheduleKind.Standing,
            new Date(39, 501, 1),
            cadenceWeeks: 52);
        sector.PlayerForce.Pledges.Add(originalPledge);
        Pledge successionPledge = new(
            1,
            pledgeSource.Id,
            grantingAuthorityId: 999999,
            PledgePayload.Requisition(40),
            PledgeScheduleKind.OneOff,
            new Date(39, 500, 10));
        sector.PlayerForce.Pledges.Add(successionPledge);

        int requestId = sector.PlayerForce.Requests.Count == 0
            ? 0
            : sector.PlayerForce.Requests.Max(request => request.Id) + 1;
        ForceCommitmentPackage savedCommitment = new(
            "scout-investigation",
            "Reconnaissance investigation",
            "Scout squad",
            1,
            4,
            6,
            _data.ChapterTemplates.ScoutSquad.BattleValue,
            ["Scout", "Covert"]);
        PresenceRequest savedRequest = new(
            requestId,
            pledgeSource,
            pledgeAuthority,
            null,
            _date,
            new Date(39, 500, 7),
            savedCommitment,
            offeredRequisition: 145,
            progressBattleValueTime: savedCommitment.ReferenceBattleValuePerPackage,
            status: RequestStatus.InProgress);
        sector.PlayerForce.Requests.Add(savedRequest);

        CampaignScenario originalScenario = sector.Scenario;
        Assert.NotNull(originalScenario);
        originalScenario.State = ObjectiveState.Won;
        originalScenario.BriefingAcknowledged = true;

        Faction tyranids = _data.Factions.Single(f => f.Name == "Tyranids");
        Planet promised = sector.GetPlanet(originalScenario.PromisedPlanetId);
        const float testMultiplier = 0.4f;
        List<RegionFaction> promisedTyranidFactions = promised.Regions
            .SelectMany(r => r.RegionFactionMap.Values)
            .Where(rf => rf.PlanetFaction.Faction.Id == tyranids.Id)
            .ToList();
        Assert.NotEmpty(promisedTyranidFactions);
        Assert.All(promisedTyranidFactions, rf => rf.GrowthMultiplier = testMultiplier);

        PlayerSoldier procedureSubject = armyRoot.GetAllMembers().OfType<PlayerSoldier>().First();
        MedicalProcedure procedure = new(procedureSubject.Id, 4, MedicalProcedureType.Cybernetic, 5, 40);
        sector.PlayerForce.Army.MedicalProcedures.Add(procedure);

        Planet intelPlanet = sector.Planets.Values
            .First(p => p.PlanetFactionMap.Count > 0 && p.Regions.Any(r => r != null));
        PlanetFaction intelFaction = intelPlanet.PlanetFactionMap.Values.First();
        Region intelRegionA = intelPlanet.Regions.First(r => r != null);
        Region intelRegionB = intelPlanet.Regions.Last(r => r != null);
        float baseIntelA = intelFaction.GetRegionIntel(intelRegionA);
        float baseIntelB = intelFaction.GetRegionIntel(intelRegionB);
        intelFaction.AddRegionIntel(intelRegionA, 2.25f);
        intelFaction.AddRegionIntel(intelRegionB, 0.5f);

        Squad orderedSquad = armyRoot.GetAllSquads().First(s => s.Members.Count > 0);
        Region orderRegion = sector.Planets.Values
            .SelectMany(p => p.Regions)
            .First(r => r.RegionFactionMap.Count > 0);
        RegionFaction orderTarget = orderRegion.RegionFactionMap.Values.First();
        Mission orderMission = new(MissionType.Recon, orderTarget, 0);
        Order order = new([orderedSquad], true, false, Aggression.Normal, orderMission);
        orderedSquad.CurrentOrders = order;
        sector.AddNewOrder(order);

        Squad landedSquad = armyRoot.GetAllSquads()
            .First(s => s.Members.Count > 0 && s.Id != orderedSquad.Id);
        Region landedRegion = sector.Planets.Values
            .SelectMany(p => p.Regions)
            .First(r => r != null);
        if (!landedRegion.Planet.PlanetFactionMap.TryGetValue(_data.PlayerFaction.Id, out PlanetFaction playerPlanetFaction))
        {
            playerPlanetFaction = new PlanetFaction(_data.PlayerFaction) { IsPublic = true };
            landedRegion.Planet.PlanetFactionMap[_data.PlayerFaction.Id] = playerPlanetFaction;
        }
        if (!landedRegion.RegionFactionMap.TryGetValue(_data.PlayerFaction.Id, out RegionFaction playerRegionFaction))
        {
            playerRegionFaction = new RegionFaction(playerPlanetFaction, landedRegion) { IsPublic = true };
            landedRegion.RegionFactionMap[_data.PlayerFaction.Id] = playerRegionFaction;
        }
        landedSquad.BoardedLocation?.RemoveSquad(landedSquad);
        landedSquad.BoardedLocation = null;
        landedSquad.CurrentRegion = landedRegion;
        playerRegionFaction.LandedSquads.Add(landedSquad);

        Squad administrativeSquad = armyRoot.GetAllSquads()
            .First(s => s.Id != orderedSquad.Id && s.Id != landedSquad.Id);
        administrativeSquad.IsAdministrative = true;

        // Order-level specialist attachment (Design/Reference/SpecialistAttachment.md). The man
        // stays on his home squad's roll -- his only persisted marker of being forward is the
        // OrderSoldier row -- so the round trip has to restore BOTH halves of the pointer pair
        // AND land on the same object the squad holds. See the reference-equality assertions
        // below: PlayerSoldier's constructor swaps the wrapper into the squad in place of the
        // base Soldier during load, so an id-only assertion would pass while the game held two
        // divergent objects.
        Squad detachableSquad = armyRoot.GetAllSquads().First(s =>
            s.SquadTemplate.PermitsIndividualDetachment
            && s.Members.Count > 0
            && s.Id != administrativeSquad.Id);
        PlayerSoldier attachedSpecialist = detachableSquad.Members.OfType<PlayerSoldier>().First();
        OrderAttachment.Attach(attachedSpecialist, order);
        int attachedSpecialistId = attachedSpecialist.Id;
        int detachableSquadId = detachableSquad.Id;

        // The squad-level weapon-option menu (SquadTemplateWeaponOption) is gone: a pooled
        // special-weapon menu now lives on the trooper element's SoldierTemplate, gated by a
        // quota keyed by any group other than Command Weapon (that one belongs to the character
        // system exercised below). Find a squad with one to exercise the same doctrine paths the
        // old squad-level menu did.
        (Squad Squad, SquadTemplateElement Element, string Group) doctrineSource = armyRoot.GetAllSquads()
            .SelectMany(squad => squad.SquadTemplate.Elements
                .SelectMany(element => element.Quotas
                    .Where(quota => quota.OptionGroup != CharacterLoadoutService.CommandWeaponGroup)
                    .Select(quota => (Squad: squad, Element: element, Group: quota.OptionGroup))))
            .First();
        Squad doctrineSquad = doctrineSource.Squad;
        WeaponSet doctrineWeaponSet = doctrineSource.Element.GetMenu(doctrineSource.Group).First();
        sector.PlayerForce.Army.LoadoutDoctrine.SetLoadout(
            doctrineSquad.SquadTemplate.Id, [doctrineWeaponSet]);
        landedRegion.Planet.LoadoutDoctrine.SetLoadout(
            doctrineSquad.SquadTemplate.Id, [doctrineWeaponSet, doctrineWeaponSet]);
        orderedSquad.UsesLoadoutDoctrine = false;
        orderedSquad.Loadout = [doctrineWeaponSet];

        // Characters persist on their own two layers: a chapter default for the role, and a
        // personal override for the individual. "Character" is now a property of the soldier's
        // element (a Command Weapon quota), not his template, so this has to go through
        // CharacterLoadoutService rather than a template flag.
        ISoldier characterSoldier = armyRoot.GetAllSquads()
            .SelectMany(squad => squad.Members)
            .First(CharacterLoadoutService.IsCharacter);
        IReadOnlyList<WeaponSet> characterMenu =
            characterSoldier.Template.GetWeaponOptions(CharacterLoadoutService.CommandWeaponGroup);
        WeaponSet roleDefaultSet = characterMenu[0];
        WeaponSet personalSet = characterMenu.Last();
        sector.PlayerForce.Army.CharacterLoadoutDoctrine.SetRoleDefault(
            characterSoldier.Template.Id, roleDefaultSet);
        sector.PlayerForce.Army.CharacterLoadoutDoctrine.SetPersonalLoadout(
            characterSoldier.Id, personalSet);

        PlayerSoldier eventSoldier = armyRoot.GetAllSquads()
            .SelectMany(s => s.Members)
            .OfType<PlayerSoldier>()
            .First();
        eventSoldier.GeneticCompatibility = 0.93f;
        eventSoldier.RecruitmentBirthDate = new Date(39, 486, 12);
        SoldierEvent battleEvent = new(_date, SoldierEventType.BattleParticipation,
            "Skirmish in the Northern Waste, Test World. Felled 4 Tyranids.",
            factionId: 7, magnitude: 4, locationName: "Northern Waste, Test World");
        eventSoldier.AddEvent(battleEvent);

        // Incapacitation (Design/Reference/CasualtyRealism.md §2.3) adds NO new persisted column: the
        // state is derived from the wounds the HitLocation table already carries, and an
        // incapacitated brother keeps his squad, so he is never mistaken for a fallen brother
        // (GameStateDataAccess treats a squad-less player soldier as dead). Both halves are pinned
        // here - the derived state after a round trip, and the untouched brother still reading
        // healthy, which is what "absent state = healthy" means when there is no migration.
        PlayerSoldier incapacitated = sector.PlayerForce.Army.PlayerSoldierMap.Values
            .First(s => s.AssignedSquad != null && s.Id != eventSoldier.Id);
        int incapacitatedId = incapacitated.Id;
        int incapacitatedSquadId = incapacitated.AssignedSquad.Id;
        HitLocation crippledVital = incapacitated.Body.HitLocations
            .First(location => location.Template.IsVital && !location.Template.HoldsProgenoid);
        // Crippled, deliberately short of severed: down and alive, which is the whole point.
        crippledVital.Wounds.AddWound(WoundLevel.Critical);
        Assert.True(CasualtyStateEvaluator.IsIncapacitated(incapacitated));
        Assert.False(CasualtyStateEvaluator.HasSeveredVitalLocation(incapacitated));
        uint incapacitatedWoundTotal = crippledVital.Wounds.WoundTotal;
        int crippledVitalTemplateId = crippledVital.Template.Id;
        incapacitated.AddEvent(new SoldierEvent(_date, SoldierEventType.Incapacitated,
            "Incapacitated in battle with the Tyranids and recovered from the field.",
            factionId: 7));

        Army army = sector.PlayerForce.Army;
        PlayerSoldier doomed = army.PlayerSoldierMap.Values
            .First(s => s.AssignedSquad != null
                && s.Id != eventSoldier.Id
                && s.Id != incapacitatedId);
        int doomedId = doomed.Id;
        string doomedName = doomed.Name;
        doomed.AddEvent(new SoldierEvent(_date, SoldierEventType.Death,
            "Killed in battle with the Tyranids by a Scything Talon",
            factionId: 7, weaponTemplateId: 13));
        doomed.AssignedSquad.RemoveSquadMember(doomed);
        doomed.AssignedSquad = null;
        army.PlayerSoldierMap.Remove(doomedId);
        army.FallenBrothers[doomedId] = doomed;

        List<Unit> originalUnits = _roundTrip.CurrentUnits;
        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_roundtrip");
        try
        {
            _roundTrip.Save(sector, dbPath, originalUnits);
            GameStateDataBlob loaded = _roundTrip.Load(dbPath);

            Assert.Equal(_date, loaded.CurrentDate);
            Assert.Equal(777, loaded.Requisition);
            Assert.Equal(13, loaded.GeneseedStockpile);
            Assert.Equal(0.83f, loaded.GeneseedPurity, 3);
            Assert.Equal(sector.Planets.Count, loaded.Planets.Count);
            Assert.Equal(sector.Characters.Count(), loaded.Characters.Count);
            Assert.True(loaded.ChapterLoadoutDoctrine.TryGetLoadout(
                doctrineSquad.SquadTemplate.Id, out IReadOnlyList<WeaponSet> loadedChapterLoadout));
            Assert.Equal(doctrineWeaponSet.Id, Assert.Single(loadedChapterLoadout).Id);
            Assert.True(loaded.CharacterLoadoutDoctrine.TryGetRoleDefault(
                characterSoldier.Template.Id, out WeaponSet loadedRoleDefault));
            Assert.Equal(roleDefaultSet.Id, loadedRoleDefault.Id);
            Assert.True(loaded.CharacterLoadoutDoctrine.TryGetPersonalLoadout(
                characterSoldier.Id, out WeaponSet loadedPersonalSet));
            Assert.Equal(personalSet.Id, loadedPersonalSet.Id);
            Character loadedAuthority = loaded.Characters.Single(character => character.Id == pledgeAuthority.Id);
            Assert.Equal(0.37f, loadedAuthority.Competence, 3);
            Assert.Equal(0.82f, loadedAuthority.Severity, 3);
            Planet loadedPledgeSource = loaded.Planets.Single(planet => planet.Id == pledgeSource.Id);
            Assert.Equal(capitalRegionId, loadedPledgeSource.CapitalRegionId);
            RegionFaction loadedCivilState = loadedPledgeSource.Regions
                .SelectMany(region => region.RegionFactionMap.Values)
                .Single(regionFaction => regionFaction.Region.Id == civilState.Region.Id
                    && regionFaction.PlanetFaction.Faction.Id == civilState.PlanetFaction.Faction.Id);
            Assert.Equal(27.5f, loadedCivilState.Contentment, 3);
            Assert.Equal(1_234, loadedCivilState.ArmedCivilians);
            Assert.True(loadedCivilState.HasEmergenceAdvantage);
            Planet loadedDoctrinePlanet = loaded.Planets.Single(planet => planet.Id == landedRegion.Planet.Id);
            Assert.True(loadedDoctrinePlanet.LoadoutDoctrine.TryGetLoadout(
                doctrineSquad.SquadTemplate.Id, out IReadOnlyList<WeaponSet> loadedPlanetLoadout));
            Assert.Equal(2, loadedPlanetLoadout.Count);
            Assert.Equal(sector.PlayerForce.Requests.Count, loaded.Requests.Count);
            IRequest loadedRequest = loaded.Requests.Single(request => request.Id == requestId);
            Assert.Equal(RequestStatus.InProgress, loadedRequest.Status);
            Assert.Equal("Reconnaissance investigation", loadedRequest.Commitment.DisplayName);
            Assert.Equal(new[] { "Scout", "Covert" }, loadedRequest.Commitment.QualificationTags);
            Assert.Equal(savedCommitment.ReferenceBattleValuePerPackage,
                loadedRequest.ProgressBattleValueTime);
            Assert.Equal(145, loadedRequest.OfferedRequisition);
            Assert.Equal(new Date(39, 500, 7), loadedRequest.Deadline);
            Assert.Equal(2, loaded.Pledges.Count);
            Pledge loadedPledge = loaded.Pledges.Single(pledge => pledge.Id == originalPledge.Id);
            Assert.Equal(originalPledge.SourcePlanetId, loadedPledge.SourcePlanetId);
            Assert.Equal(originalPledge.GrantingAuthorityId, loadedPledge.GrantingAuthorityId);
            Assert.Equal(75, loadedPledge.Payload.Amount);
            Assert.Equal(PledgeScheduleKind.Standing, loadedPledge.ScheduleKind);
            Assert.Equal(new Date(39, 501, 1), loadedPledge.NextDeliveryDate);
            Assert.Equal(999999,
                loaded.Pledges.Single(pledge => pledge.Id == successionPledge.Id).GrantingAuthorityId);

            int originalShips = sector.Fleets.Values.SelectMany(tf => tf.Ships).Count();
            int loadedShips = loaded.Fleets.SelectMany(tf => tf.Ships).Count();
            Assert.Equal(originalShips, loadedShips);
            Assert.Equal(
                sector.Fleets.Values.SelectMany(tf => tf.Ships).Sum(ship => ship.LoadedSquads.Count),
                loaded.Fleets.SelectMany(tf => tf.Ships).Sum(ship => ship.LoadedSquads.Count));
            Assert.Equal(
                sector.Fleets.Values.SelectMany(tf => tf.Ships).Sum(ship => ship.LoadedSoldierCount),
                loaded.Fleets.SelectMany(tf => tf.Ships).Sum(ship => ship.LoadedSoldierCount));

            Assert.Equal(CountSoldiers(originalUnits), CountSoldiers(loaded.Units));
            Assert.Equal(CountSquads(originalUnits), CountSquads(loaded.Units));
            Assert.Equal(TotalPopulation(sector.Planets.Values), TotalPopulation(loaded.Planets));
            Assert.Equal(TotalCarryingCapacity(sector.Planets.Values), TotalCarryingCapacity(loaded.Planets));
            Assert.Equal(TotalMaximumCarryingCapacity(sector.Planets.Values), TotalMaximumCarryingCapacity(loaded.Planets));
            Assert.True(sector.Planets.Values.Sum(p => p.Regions.Length) > 0);

            int? promisedId = sector.Scenario?.PromisedPlanetId;
            Assert.All(
                sector.Planets.Values.SelectMany(p => p.Regions),
                r =>
                {
                    Assert.True(r.CarryingCapacity <= r.MaximumCarryingCapacity,
                        $"Region {r.Id} capacity {r.CarryingCapacity} exceeds max {r.MaximumCarryingCapacity}");
                    if (r.Planet.Id != promisedId)
                    {
                        Assert.Equal(r.CarryingCapacity, r.MaximumCarryingCapacity);
                        Assert.True(r.Population <= r.CarryingCapacity,
                            $"Region {r.Id} population {r.Population} exceeds capacity {r.CarryingCapacity}");
                    }
                });

            Assert.NotNull(loaded.Scenario);
            Assert.Equal(ScenarioType.PromisedWorld, loaded.Scenario.Type);
            Assert.Equal(originalScenario.PromisedPlanetId, loaded.Scenario.PromisedPlanetId);
            Assert.Equal(ObjectiveState.Won, loaded.Scenario.State);
            Assert.True(loaded.Scenario.BriefingAcknowledged);
            Assert.Equal(originalScenario.BriefingText, loaded.Scenario.BriefingText);
            Assert.Equal(originalScenario.OriginalAuthorityCharacterId,
                         loaded.Scenario.OriginalAuthorityCharacterId);

            Planet loadedPromised = loaded.Planets.Single(p => p.Id == originalScenario.PromisedPlanetId);
            List<RegionFaction> loadedTyranidFactions = loadedPromised.Regions
                .SelectMany(r => r.RegionFactionMap.Values)
                .Where(rf => rf.PlanetFaction.Faction.Id == tyranids.Id)
                .ToList();
            Assert.NotEmpty(loadedTyranidFactions);
            Assert.All(loadedTyranidFactions,
                rf => Assert.Equal(testMultiplier, rf.GrowthMultiplier));
            IEnumerable<RegionFaction> others = loaded.Planets
                .SelectMany(p => p.Regions)
                .SelectMany(r => r.RegionFactionMap.Values)
                .Where(rf => rf.PlanetFaction.Faction.Id != tyranids.Id
                             || rf.Region.Planet.Id != originalScenario.PromisedPlanetId);
            Assert.All(others, rf => Assert.Equal(1.0f, rf.GrowthMultiplier));

            MedicalProcedure loadedProcedure = Assert.Single(loaded.MedicalProcedures);
            Assert.Equal(procedureSubject.Id, loadedProcedure.SoldierId);
            Assert.Equal(4, loadedProcedure.HitLocationTemplateId);
            Assert.Equal(MedicalProcedureType.Cybernetic, loadedProcedure.ProcedureType);
            Assert.Equal(5, loadedProcedure.WeeksRemaining);
            Assert.Equal(40, loadedProcedure.RequisitionCost);

            Planet loadedIntelPlanet = loaded.Planets.Single(p => p.Id == intelPlanet.Id);
            PlanetFaction loadedIntelFaction = loadedIntelPlanet.PlanetFactionMap[intelFaction.Faction.Id];
            Region loadedIntelA = loadedIntelPlanet.Regions.Single(r => r != null && r.Id == intelRegionA.Id);
            Region loadedIntelB = loadedIntelPlanet.Regions.Single(r => r != null && r.Id == intelRegionB.Id);
            Assert.Equal(baseIntelA + 2.25f, loadedIntelFaction.GetRegionIntel(loadedIntelA), 3);
            Assert.Equal(baseIntelB + 0.5f, loadedIntelFaction.GetRegionIntel(loadedIntelB), 3);

            Squad loadedSquad = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .Single(s => s.Id == orderedSquad.Id);
            Assert.NotNull(loadedSquad.CurrentOrders);
            Assert.False(loadedSquad.UsesLoadoutDoctrine);
            Assert.Equal(doctrineWeaponSet.Id, Assert.Single(loadedSquad.Loadout).Id);
            Assert.Equal(MissionType.Recon, loadedSquad.CurrentOrders.Mission.MissionType);
            Assert.DoesNotContain(
                loaded.Planets.SelectMany(p => p.Regions).SelectMany(r => r.SpecialMissions),
                m => m.Id == orderMission.Id);

            // 1. The attachment survived.
            Order loadedOrder = loadedSquad.CurrentOrders;
            PlayerSoldier loadedSpecialist = Assert.Single(loadedOrder.AttachedSoldiers);
            Assert.Equal(attachedSpecialistId, loadedSpecialist.Id);
            // 2. And it points at the SAME instance the home squad holds -- the assertion that
            //    catches the PlayerSoldier-wrapper trap. He is still on his squad's roll, so he
            //    must not have loaded as a fallen brother either.
            Squad loadedHomeSquad = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .Single(s => s.Id == detachableSquadId);
            ISoldier rosterInstance = loadedHomeSquad.Members
                .Single(m => m.Id == attachedSpecialistId);
            Assert.Same(rosterInstance, loadedSpecialist);
            Assert.DoesNotContain(loaded.FallenBrothers, f => f.Id == attachedSpecialistId);
            // 3. The soldier-side backpointer resolves to the same order object.
            Assert.Same(loadedOrder, loadedSpecialist.AttachedOrder);

            Squad loadedLandedSquad = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .Single(s => s.Id == landedSquad.Id);
            Region loadedLandedRegion = loaded.Planets
                .SelectMany(p => p.Regions)
                .Single(r => r != null && r.Id == landedRegion.Id);
            RegionFaction loadedPlayerRegionFaction = loadedLandedRegion.RegionFactionMap[_data.PlayerFaction.Id];
            Assert.Equal(loadedLandedRegion, loadedLandedSquad.CurrentRegion);
            Assert.Null(loadedLandedSquad.BoardedLocation);
            Assert.Contains(loadedLandedSquad, loadedPlayerRegionFaction.LandedSquads);

            Squad loadedAdministrativeSquad = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .Single(s => s.Id == administrativeSquad.Id);
            Assert.True(loadedAdministrativeSquad.IsAdministrative);
            Assert.False(loadedAdministrativeSquad.IsOperational);
            Assert.Null(loadedAdministrativeSquad.BoardedLocation);
            Assert.Null(loadedAdministrativeSquad.CurrentRegion);
            Assert.Null(loadedAdministrativeSquad.CurrentOrders);

            PlayerSoldier loadedEventSoldier = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .SelectMany(s => s.Members)
                .OfType<PlayerSoldier>()
                .Single(ps => ps.Id == eventSoldier.Id);
            Assert.Equal(0.93f, loadedEventSoldier.GeneticCompatibility.Value, 3);
            Assert.Equal(
                eventSoldier.RecruitmentBirthDate,
                loadedEventSoldier.RecruitmentBirthDate);
            SoldierEvent loadedEvent = loadedEventSoldier.SoldierEvents
                .Single(e => e.Type == SoldierEventType.BattleParticipation);
            Assert.Equal(battleEvent.Detail, loadedEvent.Detail);
            Assert.Equal(_date, loadedEvent.Date);
            Assert.Equal(7, loadedEvent.FactionId);
            Assert.Equal(4, loadedEvent.Magnitude);
            Assert.Equal("Northern Waste, Test World", loadedEvent.LocationName);
            Assert.Equal(battleEvent.Render(), loadedEvent.Render());
            Assert.True(loadedEventSoldier.SoldierEvents.Count > 1);

            PlayerSoldier loadedIncapacitated = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .SelectMany(s => s.Members)
                .OfType<PlayerSoldier>()
                .Single(ps => ps.Id == incapacitatedId);
            Assert.Equal(incapacitatedSquadId, loadedIncapacitated.AssignedSquad.Id);
            Assert.Equal(
                incapacitatedWoundTotal,
                loadedIncapacitated.Body.HitLocations
                    .Single(location => location.Template.Id == crippledVitalTemplateId)
                    .Wounds.WoundTotal);
            Assert.True(CasualtyStateEvaluator.IsIncapacitated(loadedIncapacitated));
            Assert.False(CasualtyStateEvaluator.HasSeveredVitalLocation(loadedIncapacitated));
            Assert.Equal(
                CasualtyState.Incapacitated,
                CasualtyStateEvaluator.Classify(loadedIncapacitated, bodyRecovered: true));
            Assert.Single(
                loadedIncapacitated.SoldierEvents,
                e => e.Type == SoldierEventType.Incapacitated);
            // No stored flag means an untouched brother comes back healthy, with nothing to migrate.
            Assert.Equal(
                CasualtyState.Unharmed,
                CasualtyStateEvaluator.Classify(loadedEventSoldier, bodyRecovered: true));

            PlayerSoldier loadedFallen = Assert.Single(loaded.FallenBrothers);
            Assert.Equal(doomedId, loadedFallen.Id);
            Assert.Equal(doomedName, loadedFallen.Name);
            Assert.Null(loadedFallen.AssignedSquad);
            SoldierEvent death = loadedFallen.SoldierEvents
                .Single(e => e.Type == SoldierEventType.Death);
            Assert.Equal(7, death.FactionId);
            Assert.Equal(13, death.WeaponTemplateId);
            Assert.Equal("Killed in battle with the Tyranids by a Scything Talon", death.Render());
            Assert.DoesNotContain(
                loaded.Units.SelectMany(u => u.GetAllSquads()).SelectMany(s => s.Members),
                m => m.Id == doomedId);

            List<SoldierEvaluation> loadedEvaluations = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .SelectMany(s => s.Members)
                .OfType<PlayerSoldier>()
                .SelectMany(ps => ps.SoldierEvaluationHistory)
                .ToList();
            Assert.NotEmpty(loadedEvaluations);
            Assert.All(loadedEvaluations, e => Assert.Contains(RatingKeys.Melee, e.Ratings.Keys));
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void Load_ReconstructsArmy_WithoutPreSeedingFactionUnits()
    {
        // Regression for the load crash: the real load path (StartMenu -> SavedGameLoader)
        // rebuilds the player's Army from a freshly constructed GameRulesData whose
        // PlayerFaction.Units is empty, relying on the loaded blob to supply the order of
        // battle. Unlike the other round-trip tests, this deliberately does NOT pre-seed the
        // reconstruction faction, reproducing the real load path.
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Load Reconstruct Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        _roundTrip.RegisterPlayerArmy(sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        int expectedSoldierCount = armyRoot.GetAllMembers().Count();

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_load_reconstruct");
        try
        {
            _roundTrip.Save(sector, dbPath, _roundTrip.CurrentUnits);
            GameStateDataBlob loaded = _roundTrip.Load(dbPath);

            // A fresh rules-data instance, exactly as the real load path constructs. Its
            // player faction starts with no units; the loader must populate them from the blob.
            GameRulesData freshRules = new();
            Assert.Empty(freshRules.PlayerFaction.Units);

            Sector rebuilt = SavedGameLoader.BuildSectorFromBlob(loaded, freshRules);

            // The loader registered the loaded order of battle on the previously empty faction,
            // so both the reconstructed army and any subsequent save work.
            Assert.NotEmpty(freshRules.PlayerFaction.Units);
            Assert.Equal(expectedSoldierCount,
                rebuilt.PlayerForce.Army.OrderOfBattle.GetAllMembers().Count());
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void Load_RepopulatesSectorOrders_FromLoadedPlayerSquads()
    {
        // Regression: the data-access layer restores orders only onto squads (Squad.CurrentOrders),
        // so BuildSectorFromBlob must re-register them with Sector.Orders - the authoritative list
        // that turn processing (TurnController) and the region/planet inbound-orders views read.
        // Without the rebuild a reloaded game processes and displays no standing orders.
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Load Orders Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        _roundTrip.RegisterPlayerArmy(sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;

        Squad orderedSquad = armyRoot.GetAllSquads().First(s => s.Members.Count > 0);
        Region orderRegion = sector.Planets.Values
            .SelectMany(p => p.Regions)
            .First(r => r.RegionFactionMap.Count > 0);
        RegionFaction orderTarget = orderRegion.RegionFactionMap.Values.First();
        Mission orderMission = new(MissionType.Recon, orderTarget, 0);
        Order order = new([orderedSquad], true, false, Aggression.Normal, orderMission);
        orderedSquad.CurrentOrders = order;
        sector.AddNewOrder(order);

        // An attached specialist must ride through the full StartMenu load path too:
        // SavedGameLoader re-registers the same Order instances, so the attachment restored by
        // PopulateOrderAttachments has to still be on them after the sector rebuild.
        Squad detachableSquad = armyRoot.GetAllSquads().First(s =>
            s.SquadTemplate.PermitsIndividualDetachment && s.Members.Count > 0);
        PlayerSoldier specialist = detachableSquad.Members.OfType<PlayerSoldier>().First();
        OrderAttachment.Attach(specialist, order);
        int specialistId = specialist.Id;

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_load_orders");
        try
        {
            _roundTrip.Save(sector, dbPath, _roundTrip.CurrentUnits);
            GameStateDataBlob loaded = _roundTrip.Load(dbPath);

            // A fresh rules-data instance, exactly as the real StartMenu load path constructs.
            GameRulesData freshRules = new();
            Sector rebuilt = SavedGameLoader.BuildSectorFromBlob(loaded, freshRules);

            Squad rebuiltSquad = rebuilt.PlayerForce.Army.OrderOfBattle.GetAllSquads()
                .Single(s => s.Id == orderedSquad.Id);
            Assert.NotNull(rebuiltSquad.CurrentOrders);
            Assert.Equal(MissionType.Recon, rebuiltSquad.CurrentOrders.Mission.MissionType);
            // The squad's restored order is registered with the Sector, not merely dangling off
            // the squad - this is the assertion that would have caught the empty-Orders bug.
            Assert.Contains(rebuiltSquad.CurrentOrders, rebuilt.Orders.Values);

            PlayerSoldier rebuiltSpecialist =
                Assert.Single(rebuiltSquad.CurrentOrders.AttachedSoldiers);
            Assert.Equal(specialistId, rebuiltSpecialist.Id);
            Assert.Same(rebuiltSquad.CurrentOrders, rebuiltSpecialist.AttachedOrder);
            // Reference equality against the live roster: the rebuilt sector must hold one
            // object for this man, not an order-side copy that diverges from his squad entry.
            Assert.Same(
                rebuilt.PlayerForce.Army.OrderOfBattle.GetAllSquads()
                    .SelectMany(s => s.Members)
                    .Single(m => m.Id == specialistId),
                rebuiltSpecialist);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }

    private static int CountSoldiers(IEnumerable<Unit> rootUnits)
    {
        return rootUnits.Sum(u => u.GetAllSquads().Sum(s => s.Members.Count));
    }

    private static int CountSquads(IEnumerable<Unit> rootUnits)
    {
        return rootUnits.Sum(u => u.GetAllSquads().Count());
    }

    private static long TotalCarryingCapacity(IEnumerable<Models.Planets.Planet> planets)
    {
        return planets.Sum(p => p.Regions.Sum(r => r.CarryingCapacity));
    }

    private static long TotalMaximumCarryingCapacity(IEnumerable<Models.Planets.Planet> planets)
    {
        return planets.Sum(p => p.Regions.Sum(r => r.MaximumCarryingCapacity));
    }

    private static long TotalPopulation(IEnumerable<Models.Planets.Planet> planets)
    {
        return planets.Sum(p => p.Population);
    }
}
