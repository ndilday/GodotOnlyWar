using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Operations.Orders;
using OnlyWar.Persistence.Database.GameState;
using OnlyWar.Runtime.Factories;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class OrkSaveLoadRegressionTests
{
    [Trait("Category", "Slow")]
    [Fact]
    public void SaveGeneratedOrkOpening_SkipsTransientNpcOrdersAndPreservesInvasionForce()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        GameRulesData rules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(
            RulesDatabaseFixture.DatabasePath);
        Date date = new(39, 500, 1);
        Faction orks = rules.Factions.Single(faction => faction.Name == "Orks");
        Sector sector = TestGeneration.GenerateSector(
            1,
            rules,
            date,
            "Ork Save Regression",
            ScenarioFactionSelection.ForFaction(orks.Id));

        GameStateRoundTripFixture roundTrip = new(rules, date);
        roundTrip.RegisterPlayerArmy(sector);
        List<Unit> units = rules.Factions.SelectMany(faction => faction.Units).ToList();
        List<Squad> savedSquads = units.SelectMany(unit => unit.GetAllSquads()).ToList();
        StrategicInvasionForce invasionForce = Assert.Single(sector.StrategicInvasionForces);
        Assert.Contains(invasionForce.CommandSquad, savedSquads);

        // Model the transient order shape produced by the opening warm-up: the persistent
        // strategic command squad is attached to a tactical NPC order alongside a generated
        // assault squad that is deliberately absent from the save graph.
        Squad transientAssaultSquad = SquadFactory.GenerateSquad(
            orks.SquadTemplates.Values.First(),
            new FixedRNG(),
            name: "Transient Ork Assault");
        Order transientNpcOrder = new(
            [invasionForce.CommandSquad, transientAssaultSquad],
            false,
            true,
            Aggression.Normal,
            new Mission(MissionType.Advance, invasionForce.CurrentRegion, rules.PlayerFaction, 0),
            orks);
        Assert.Contains(transientAssaultSquad, transientNpcOrder.AssignedSquads);
        Assert.DoesNotContain(transientAssaultSquad, savedSquads);

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("ork_save_regression");
        try
        {
            roundTrip.Save(sector, dbPath, units);
            GameStateDataBlob blob = roundTrip.Load(dbPath);

            Assert.DoesNotContain(blob.Orders, order => order.OwnerFaction?.Id == orks.Id);
            StrategicInvasionForceSaveData savedForce = Assert.Single(blob.StrategicInvasionForces);
            Assert.Equal(invasionForce.Id, savedForce.Id);
            Assert.Equal(invasionForce.CommandSquad.Id, savedForce.CommandSquadId);

            GameRulesData loadedRules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(
                RulesDatabaseFixture.DatabasePath);
            Sector loadedSector = SavedGameLoader.BuildSectorFromBlob(
                blob,
                loadedRules,
                new OrderCommitmentSurface());
            StrategicInvasionForce loadedForce = Assert.Single(loadedSector.StrategicInvasionForces);
            Assert.Equal(invasionForce.Id, loadedForce.Id);
            Assert.Equal(invasionForce.CommandSquad.Id, loadedForce.CommandSquad.Id);
            Assert.Equal(invasionForce.CommandSquad.ParentUnit.Id, loadedForce.CommandSquad.ParentUnit.Id);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }
}
