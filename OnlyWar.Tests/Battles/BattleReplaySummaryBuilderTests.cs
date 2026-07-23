using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleReplaySummaryBuilderTests
{
    private static int _nextSoldierId = 1;

    [Fact]
    public void Build_DefaultsSelectionToFirstPlayerFormation()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Cult Mob", "Cultist One", "Cultist Two");
        BattleHistory history = CreateHistory(playerSquad, opposingSquad);

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 0);

        Assert.Equal(playerSquad.Id, display.SelectedFormationId);
        Assert.Equal("Alpha", display.SelectedFormation.Name);
        Assert.Equal("Battle Chronicle", display.BattleTitle);
        Assert.Equal("Deployment", display.PhaseLabel);
    }

    [Fact]
    public void Build_NpcAttackAgainstPlayerDefenderGroupsByAffiliationNotResolverSide()
    {
        BattleSquad npcAttacker = CreateBattleSquad(false, "Cult Mob", "Cultist One", "Cultist Two");
        BattleSquad playerDefender = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha");
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(new BattleState(
            new Dictionary<int, BattleSquad> { [npcAttacker.Id] = npcAttacker },
            new Dictionary<int, BattleSquad> { [playerDefender.Id] = playerDefender }), []));

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 0);

        Assert.Equal(playerDefender.Id, display.SelectedFormationId);
        BattleForceHierarchyNode playerRoot = Assert.Single(display.ForceHierarchy, node => node.IsPlayerForce);
        Assert.Contains(playerRoot.Children.SelectMany(node => node.Children), node => node.FormationId == playerDefender.Id);
        BattleForceHierarchyNode opposingRoot = Assert.Single(display.ForceHierarchy, node => !node.IsPlayerForce);
        Assert.Contains(opposingRoot.Children.SelectMany(node => node.Children), node => node.FormationId == npcAttacker.Id);
    }

    [Fact]
    public void Build_DefaultPdfDefenderIsShownOnPlayerAlignedSide()
    {
        BattleSquad chapterDefender = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha");
        BattleSquad pdfDefender = CreateBattleSquad(
            false,
            "PDF Garrison",
            CreateDefaultPdfFaction(),
            "PDF Trooper");
        BattleSquad tyranidAttacker = CreateBattleSquad(false, "Carnifex", "Carnifex");
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(new BattleState(
            new Dictionary<int, BattleSquad> { [tyranidAttacker.Id] = tyranidAttacker },
            new Dictionary<int, BattleSquad>
            {
                [chapterDefender.Id] = chapterDefender,
                [pdfDefender.Id] = pdfDefender
            }), []));

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 0, pdfDefender.Id);

        BattleForceHierarchyNode playerRoot = Assert.Single(display.ForceHierarchy, node => node.IsPlayerForce);
        BattleForceHierarchyNode opposingRoot = Assert.Single(display.ForceHierarchy, node => !node.IsPlayerForce);
        Assert.Contains(playerRoot.Children.SelectMany(node => node.Children), node => node.FormationId == pdfDefender.Id);
        Assert.DoesNotContain(opposingRoot.Children.SelectMany(node => node.Children), node => node.FormationId == pdfDefender.Id);
        Assert.True(display.SelectedFormation.IsPlayerForce);
    }

    [Fact]
    public void Build_ForceHierarchyReportsCurrentStrengthAndLosses()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Cult Mob", "Cultist One", "Cultist Two", "Cultist Three");
        BattleState initialState = CreateState(playerSquad, opposingSquad);
        BattleState currentState = new(initialState);
        currentState.GetSquad(opposingSquad.Id).Soldiers.RemoveAt(0);
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(initialState, []));
        history.Turns.Add(new BattleTurn(currentState, []));

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 1, opposingSquad.Id);

        BattleForceHierarchyNode opposingRoot = Assert.Single(display.ForceHierarchy, node => !node.IsPlayerForce);
        Assert.Equal(3, opposingRoot.StartingStrength);
        Assert.Equal(2, opposingRoot.CurrentStrength);
        Assert.Equal(1, opposingRoot.Losses);
        Assert.Equal(opposingSquad.Id, display.SelectedFormation.FormationId);
        Assert.Equal(1, display.SelectedFormation.Losses);
        Assert.Equal("Pressed", display.SelectedFormation.MoraleLabel);
    }

    [Fact]
    public void Build_CurrentTurnEventsAndCasualtyTimelineUseRequestedTurn()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Cult Mob", "Cultist One", "Cultist Two");
        BattleState initialState = CreateState(playerSquad, opposingSquad);
        BattleState currentState = new(initialState);
        currentState.GetSquad(opposingSquad.Id).Soldiers.RemoveAt(0);
        FakeAction action = new(playerSquad.Soldiers[0].Soldier.Id, "Sergeant Alpha opened fire.");
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(initialState, []));
        history.Turns.Add(new BattleTurn(currentState, [action]));

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 1, playerSquad.Id);

        BattleEventEntry eventEntry = Assert.Single(display.CurrentTurnEvents);
        Assert.Equal("Sergeant Alpha", eventEntry.ActorName);
        Assert.Equal("Alpha", eventEntry.FormationName);
        Assert.Equal("Action", eventEntry.EventType);
        Assert.Contains("opened fire", eventEntry.Text);
        Assert.Equal(2, display.Timeline.Count);
        Assert.True(display.Timeline[1].IsSelected);
        Assert.Equal(1, display.CasualtiesByRound[1].OpposingLossesThisRound);
        Assert.Equal(1, display.CasualtiesByRound[1].OpposingCumulativeLosses);
    }

    [Fact]
    public void Build_ActionByCasualtyRetainsActorAndFormationIdentity()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Cult Mob", "Neophyte Hybrid 15");
        BattleState initialState = CreateState(playerSquad, opposingSquad);
        BattleState currentState = new(initialState);
        BattleSoldier casualty = currentState.GetSquad(opposingSquad.Id).Soldiers[0];
        FakeAction action = new(casualty.Soldier.Id, "Neophyte Hybrid 15 readies Autopistol.");

        currentState.GetSquad(opposingSquad.Id).RemoveSoldier(casualty);
        currentState.RemoveSoldier(casualty.Soldier.Id);
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(initialState, []));
        history.Turns.Add(new BattleTurn(currentState, [action], null, [casualty]));

        BattleEventEntry eventEntry = Assert.Single(
            new BattleReplaySummaryBuilder().Build(history, 1).CurrentTurnEvents);

        Assert.Equal("Neophyte Hybrid 15", eventEntry.ActorName);
        Assert.Equal("Cult Mob", eventEntry.FormationName);
        Assert.Equal("Neophyte Hybrid 15 readies Autopistol.", eventEntry.Text);
    }

    [Fact]
    public void Build_TypedEventsPrecedeActionChronology()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Cult Mob", "Cultist One");
        BattleState initialState = CreateState(playerSquad, opposingSquad);
        BattleState currentState = new(initialState);
        FakeAction action = new(currentState.GetSquad(playerSquad.Id).Soldiers[0].Soldier.Id,
            "Sergeant Alpha opened fire.");
        BattleEvent withdrawal = new(
            BattleEventType.WithdrawalOrdered,
            currentState.TurnNumber,
            BattleSide.Opposing,
            opposingSquad.Id,
            [],
            "Cult Mob began an orderly withdrawal.");
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(initialState, []));
        history.Turns.Add(new BattleTurn(currentState, [action], [withdrawal]));

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 1);

        Assert.Equal(2, display.CurrentTurnEvents.Count);
        Assert.Equal("Withdrawal", display.CurrentTurnEvents[0].EventType);
        Assert.Equal("Cult Mob", display.CurrentTurnEvents[0].FormationName);
        Assert.Equal("01:01", display.CurrentTurnEvents[0].Timestamp);
        Assert.Equal("Action", display.CurrentTurnEvents[1].EventType);
        Assert.Equal("01:02", display.CurrentTurnEvents[1].Timestamp);
        Assert.Equal("1 battle event, 1 action, 0 wounds", display.Timeline[1].Summary);
    }

    [Fact]
    public void Build_DisengagedSoldiersRemainSurvivorsWhileEliminatedRosterCountsAsCasualties()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Cult Mob", "Cultist One", "Cultist Two");
        BattleState initialState = CreateState(playerSquad, opposingSquad);
        BattleState finalState = new(initialState);
        finalState.DisengageSquad(finalState.GetSquad(playerSquad.Id));
        finalState.RemoveSquad(finalState.GetSquad(opposingSquad.Id));
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(initialState, []));
        history.Turns.Add(new BattleTurn(finalState, []));

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 1, playerSquad.Id);

        BattleForceHierarchyNode playerRoot = Assert.Single(display.ForceHierarchy, node => node.IsPlayerForce);
        BattleForceHierarchyNode opposingRoot = Assert.Single(display.ForceHierarchy, node => !node.IsPlayerForce);
        Assert.Equal(2, playerRoot.CurrentStrength);
        Assert.Equal(0, playerRoot.Losses);
        Assert.Equal(0, opposingRoot.CurrentStrength);
        Assert.Equal(2, opposingRoot.Losses);
        Assert.Equal(0, display.CasualtiesByRound[1].PlayerCumulativeLosses);
        Assert.Equal(2, display.CasualtiesByRound[1].OpposingCumulativeLosses);
        Assert.Equal("Disengaged", display.SelectedFormation.MoraleLabel);
        Assert.Contains("Disengaged", display.SelectedFormation.NotableEffects);
    }

    [Theory]
    [InlineData(BattleEndReason.Withdrawal, BattleSide.Attacker, "Opposing force withdrew; Player force held the field")]
    [InlineData(BattleEndReason.Rout, BattleSide.Opposing, "Player force routed; Opposing force held the field")]
    [InlineData(BattleEndReason.MutualDisengagement, null, "Mutual disengagement; field contested")]
    [InlineData(BattleEndReason.TurnCap, null, "Turn cap reached; field contested")]
    [InlineData(BattleEndReason.Annihilation, BattleSide.Attacker, "Player force held the field")]
    public void Build_FinalResultUsesTypedOutcome(
        BattleEndReason reason,
        BattleSide? holder,
        string expected)
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Cult Mob", "Cultist One");
        BattleState initialState = CreateState(playerSquad, opposingSquad);
        BattleHistory history = new()
        {
            Outcome = new BattleOutcome(reason, holder)
        };
        history.Turns.Add(new BattleTurn(initialState, []));
        history.Turns.Add(new BattleTurn(new BattleState(initialState), []));

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 1);

        Assert.Equal(expected, display.ResultLabel);
    }

    [Fact]
    public void Build_UsesSquadTypeIconForOpposingFormation()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Heavy Brood", SquadTypes.Heavy, "Heavy One", "Heavy Two");
        BattleHistory history = CreateHistory(playerSquad, opposingSquad);

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 0, opposingSquad.Id);

        BattleForceHierarchyNode opposingFormation = Assert.Single(
            display.ForceHierarchy,
            node => !node.IsPlayerForce)
            .Children
            .SelectMany(node => node.Children)
            .Single(node => node.FormationId == opposingSquad.Id);
        Assert.Equal("heavy", opposingFormation.IconKey);
    }

    [Fact]
    public void Build_ReportsActiveWeaponSetsIncludingImplicitDefaultMembers()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha", "Brother Beta");
        WeaponSet specialistSet = new(42, "Specialist Weapons");
        playerSquad.Squad.Loadout.Add(specialistSet);
        playerSquad.Squad.Loadout.Add(specialistSet);
        BattleHistory history = CreateHistory(playerSquad, CreateBattleSquad(false, "Cult Mob", "Cultist One"));

        BattleFormationSummary summary = new BattleReplaySummaryBuilder()
            .Build(history, 0, playerSquad.Id)
            .SelectedFormation;

        Assert.Equal(2, summary.ActiveWeaponSets.Count);
        Assert.Equal("Test Weapons", summary.ActiveWeaponSets[0].Name);
        Assert.Equal(1, summary.ActiveWeaponSets[0].Count);
        Assert.Equal("Specialist Weapons", summary.ActiveWeaponSets[1].Name);
        Assert.Equal(2, summary.ActiveWeaponSets[1].Count);
    }

    private static BattleHistory CreateHistory(BattleSquad playerSquad, BattleSquad opposingSquad)
    {
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(CreateState(playerSquad, opposingSquad), []));
        return history;
    }

    private static BattleState CreateState(BattleSquad playerSquad, BattleSquad opposingSquad)
    {
        return new BattleState(
            new Dictionary<int, BattleSquad> { [playerSquad.Id] = playerSquad },
            new Dictionary<int, BattleSquad> { [opposingSquad.Id] = opposingSquad });
    }

    private static BattleSquad CreateBattleSquad(bool isPlayerSquad, string squadName, params string[] soldierNames)
    {
        return CreateBattleSquad(isPlayerSquad, squadName, SquadTypes.None, soldierNames);
    }

    private static BattleSquad CreateBattleSquad(bool isPlayerSquad, string squadName, SquadTypes squadType, params string[] soldierNames)
    {
        return CreateBattleSquad(isPlayerSquad, squadName, squadType, null, soldierNames);
    }

    private static BattleSquad CreateBattleSquad(bool isPlayerSquad, string squadName, Faction faction, params string[] soldierNames)
    {
        return CreateBattleSquad(isPlayerSquad, squadName, SquadTypes.None, faction, soldierNames);
    }

    private static BattleSquad CreateBattleSquad(
        bool isPlayerSquad,
        string squadName,
        SquadTypes squadType,
        Faction faction,
        params string[] soldierNames)
    {
        List<Soldier> soldiers = [];
        for (int i = 0; i < soldierNames.Length; i++)
        {
            SoldierTemplate template = i == 0 ? TestModelFactory.SergeantTemplate : TestModelFactory.MarineTemplate;
            Soldier soldier = TestModelFactory.CreateSoldier(template, soldierNames[i]);
            soldier.Id = _nextSoldierId++;
            soldiers.Add(soldier);
        }

        SquadTemplate squadTemplate = new(
            TestModelFactory.SquadTemplate.Id,
            TestModelFactory.SquadTemplate.Name,
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.SergeantTemplate, 0, 1), new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)],
            squadType);
        squadTemplate.Faction = faction;
        Squad sourceSquad = new(squadName, null, squadTemplate);
        foreach (Soldier soldier in soldiers)
        {
            sourceSquad.AddSquadMember(soldier);
        }

        BattleSquad squad = new(isPlayerSquad, sourceSquad);
        for (int i = 0; i < squad.Soldiers.Count; i++)
        {
            squad.Soldiers[i].TopLeft = (i + 1, 2);
            squad.Soldiers[i].Orientation = 0;
        }

        return squad;
    }

    private static Faction CreateDefaultPdfFaction()
    {
        return new Faction(
            900,
            "PDF",
            Color.White,
            isPlayerFaction: false,
            isDefaultFaction: true,
            canInfiltrate: false,
            growthType: GrowthType.None,
            species: null,
            soldierTemplates: null,
            squadTemplates: null,
            unitTemplates: null,
            boatTemplates: null,
            shipTemplates: null,
            fleetTemplates: null);
    }

    private sealed class FakeAction : IAction
    {
        private readonly string _description;

        public int ActorId { get; }

        public FakeAction(int actorId, string description)
        {
            ActorId = actorId;
            _description = description;
        }

        public void Execute(BattleState state)
        {
        }

        public string Description()
        {
            return _description;
        }
    }
}
