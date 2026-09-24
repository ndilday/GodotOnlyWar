using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Mob morale support (MobMentality factions). Covers the three rules a turn-one ambush on a
/// boss-less ork warband exposed: a warband that never fielded a boss takes no command-loss
/// penalty, a nearby mob's health is measured against its battle-start strength, and a mob
/// only discourages its neighbours once it was already routing at the start of the turn.
/// </summary>
public class MobMoraleSupportEvaluatorTests
{
    private const float Precision = 0.0001f;

    // The "standard" profile values from the rules database.
    private const double NearbyMobSupport = 0.1;
    private const double LivingLeaderSupport = 0.25;
    private const double CasualtyPenalty = 0.15;
    private const double RoutPenalty = 0.2;
    private const double SeparatedPenalty = 0.15;
    private const double CommandLossPenalty = 0.2;
    private const double MaximumSupport = 0.5;

    private static readonly FactionBehaviorRulesProfile Rules = new(
        "test",
        0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        NearbyMobSupport,
        LivingLeaderSupport,
        CasualtyPenalty,
        RoutPenalty,
        SeparatedPenalty,
        CommandLossPenalty,
        MaximumSupport);

    private static readonly Faction MobFaction = new(
        id: 90_000,
        name: "Mob Test Faction",
        color: Color.Green,
        isPlayerFaction: false,
        isDefaultFaction: false,
        FactionBehavior.MobMentality,
        growthType: GrowthType.None,
        species: new Dictionary<int, Species>(),
        soldierTemplates: new Dictionary<int, SoldierTemplate>(),
        squadTemplates: new Dictionary<int, SquadTemplate>(),
        unitTemplates: new Dictionary<int, UnitTemplate>(),
        boatTemplates: new Dictionary<int, BoatTemplate>(),
        shipTemplates: new Dictionary<int, ShipTemplate>(),
        fleetTemplates: new Dictionary<int, FleetTemplate>());

    private static BattleSquad CreateMob(string name, int templateId, SquadTypes squadType, int soldierCount)
    {
        SquadTemplate squadTemplate = new(
            templateId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, (byte)soldierCount)],
            squadType)
        {
            Faction = MobFaction
        };
        Squad squad = new(templateId, name, null, squadTemplate);
        for (int i = 0; i < soldierCount; i++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(
                template: TestModelFactory.MarineTemplate, name: $"{name} {i + 1}");
            soldier.Id = (templateId * 100) + i;
            soldier.Ego = 8f;
            squad.AddSquadMember(soldier);
        }
        return new BattleSquad(false, squad);
    }

    private static void Place(BattleGridManager grid, BattleSquad squad, int x, int y)
    {
        for (int i = 0; i < squad.Soldiers.Count; i++)
        {
            BattleSoldier soldier = squad.Soldiers[i];
            ValueTuple<int, int> cell = new(x + i, y);
            grid.PlaceSoldier(soldier, true, new List<ValueTuple<int, int>> { cell });
            soldier.TopLeft = cell;
        }
    }

    private static void Kill(BattleGridManager grid, BattleSquad squad, int count)
    {
        foreach (BattleSoldier soldier in squad.Soldiers.Take(count).ToList())
        {
            squad.RemoveSoldier(soldier);
            grid.RemoveSoldier(soldier.Soldier.Id);
        }
    }

    private static float Support(
        BattleSquad squad,
        IReadOnlyList<BattleSquad> roster,
        BattleGridManager grid,
        Dictionary<int, int> startingAble,
        ISet<int> routingAtTurnStart = null,
        float genericCommandAura = 0f)
    {
        List<BattleSquad> active = roster
            .Where(candidate => candidate.Status == BattleSquadStatus.Active
                && candidate.AbleSoldiers.Count > 0)
            .ToList();
        return MobMoraleSupportEvaluator.ComputeSupport(
            squad,
            active,
            roster,
            grid,
            Rules,
            candidate => startingAble[candidate.Id],
            routingAtTurnStart ?? new HashSet<int>(),
            genericCommandAura);
    }

    private static Dictionary<int, int> StartingAble(params BattleSquad[] squads) =>
        squads.ToDictionary(squad => squad.Id, squad => squad.AbleSoldiers.Count);

    [Fact]
    public void WarbandThatNeverFieldedABoss_TakesNoCommandLossPenalty()
    {
        BattleGridManager grid = new();
        BattleSquad boyz = CreateMob("Boyz", 90_001, SquadTypes.None, 10);
        BattleSquad nobz = CreateMob("Nobz", 90_002, SquadTypes.None, 4);
        Place(grid, boyz, 0, 0);
        Place(grid, nobz, 0, 5);
        Dictionary<int, int> starting = StartingAble(boyz, nobz);

        // One healthy mob nearby and no boss ever on the field: plain nearby-mob support.
        Assert.Equal(
            (float)NearbyMobSupport,
            Support(boyz, [boyz, nobz], grid, starting),
            Precision);
    }

    [Fact]
    public void DestroyedBoss_AppliesCommandLossPenalty_LivingBossAnywherePreventsIt()
    {
        BattleGridManager grid = new();
        BattleSquad boyz = CreateMob("Boyz", 90_011, SquadTypes.None, 10);
        BattleSquad otherBoyz = CreateMob("Other Boyz", 90_012, SquadTypes.None, 10);
        BattleSquad boss = CreateMob("Warboss", 90_013, SquadTypes.HQ, 1);
        Place(grid, boyz, 0, 0);
        Place(grid, otherBoyz, 0, 5);
        Place(grid, boss, 500, 0);
        Dictionary<int, int> starting = StartingAble(boyz, otherBoyz, boss);

        // The boss is alive but far out of sight: no living-leader support, and no loss.
        Assert.Equal(
            (float)NearbyMobSupport,
            Support(boyz, [boyz, otherBoyz, boss], grid, starting),
            Precision);

        // Every boss the warband fielded is dead. The mob penalty applies on top of the
        // generic command-loss term, which the caller passes in as a negative aura.
        Kill(grid, boss, 1);
        Assert.Equal(
            (float)(NearbyMobSupport - CommandLossPenalty),
            Support(
                boyz,
                [boyz, otherBoyz, boss],
                grid,
                starting,
                genericCommandAura: -MoraleConstants.CommandLossStress),
            Precision);
    }

    [Fact]
    public void NearbyMobHealth_IsMeasuredAgainstItsStartingStrength()
    {
        BattleGridManager grid = new();
        BattleSquad boyz = CreateMob("Boyz", 90_021, SquadTypes.None, 10);
        BattleSquad otherBoyz = CreateMob("Other Boyz", 90_022, SquadTypes.None, 10);
        Place(grid, boyz, 0, 0);
        Place(grid, otherBoyz, 0, 5);
        Dictionary<int, int> starting = StartingAble(boyz, otherBoyz);

        // Half the neighbouring mob is dead and gone from its roster. It must read as half
        // strength, not as a whole mob of five.
        Kill(grid, otherBoyz, 5);
        Assert.Equal(5, otherBoyz.Soldiers.Count);
        Assert.Equal(
            (float)((NearbyMobSupport * 0.5) - (CasualtyPenalty * 0.5)),
            Support(boyz, [boyz, otherBoyz], grid, starting),
            Precision);
    }

    [Fact]
    public void RoutPenalty_ReadsTurnStartRouting_NotAMobThatBrokeThisTurn()
    {
        BattleGridManager grid = new();
        BattleSquad boyz = CreateMob("Boyz", 90_031, SquadTypes.None, 10);
        BattleSquad nobz = CreateMob("Nobz", 90_032, SquadTypes.None, 4);
        Place(grid, boyz, 0, 0);
        Place(grid, nobz, 0, 5);
        Dictionary<int, int> starting = StartingAble(boyz, nobz);

        // The Boyz broke earlier in this same morale pass. The Nobz must not see it yet.
        boyz.WithdrawalRole = WithdrawalRole.Routing;
        Assert.Equal(
            (float)NearbyMobSupport,
            Support(nobz, [boyz, nobz], grid, starting, new HashSet<int>()),
            Precision);

        // Next turn the Boyz are in the turn-start snapshot, and the penalty applies.
        Assert.Equal(
            (float)(NearbyMobSupport - RoutPenalty),
            Support(nobz, [boyz, nobz], grid, starting, new HashSet<int> { boyz.Id }),
            Precision);
    }
}
