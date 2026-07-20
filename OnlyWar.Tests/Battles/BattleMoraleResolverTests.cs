using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 3 resolver wiring for Design/Active/MoraleAndRout.md: the morale check runs at
/// resolver stage 6, a rout sets the existing WithdrawalRole.Routing seam, emits
/// SquadRouted, stays sticky, and flows into BattleSideIntent.Rout / BattleEndReason.Rout
/// through the existing withdrawal machinery. Consumes the seeded static RNG.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class BattleMoraleResolverTests
{
    [Fact]
    public void LowEgoSquadUnderSustainedFire_RoutsAndBattleEndsInRout()
    {
        // Ten unarmed low-Ego (8) troopers advance across open ground into withering
        // heavy-bolter-grade fire from four crack marines. The troopers take heavy
        // casualties while losing the exchange; morale must break them (Routing,
        // SquadRouted event) rather than fighting to the last man. The marines' Avoid
        // aggression then declines pursuit, so the routed side disengages and the battle
        // records the typed Rout outcome.
        BattleSquad victims = CreateSquad(
            "Gaunt Analog", 74_101, TestModelFactory.MarineTemplate, count: 10, ego: 8f);
        BattleSquad marines = CreateSquad(
            "Marines", 74_201, TestModelFactory.MarineTemplate, count: 4, ego: 14f,
            dexterity: 20f);
        RangedWeaponTemplate heavyRifle = new(
            99_500,
            "Test Heavy Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 10,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 1,
            baseDamage: 20,
            maxDistance: 200,
            rof: 3,
            ammo: 30,
            recoil: 0,
            bulk: 0,
            doesDamageDegradeWithRange: false,
            reloadTime: 1,
            templateType: 0,
            areaRadius: 0,
            fuelPerBurst: 0);
        BattleGridManager grid = new();
        for (int i = 0; i < victims.Soldiers.Count; i++)
        {
            BattleSoldier soldier = victims.Soldiers[i];
            soldier.RangedWeapons.Clear();
            soldier.EquippedRangedWeapons.Clear();
            Place(grid, soldier, side: true, x: 60 + (i % 5), y: i / 5);
        }
        for (int i = 0; i < marines.Soldiers.Count; i++)
        {
            BattleSoldier soldier = marines.Soldiers[i];
            RangedWeapon weapon = new(heavyRifle);
            soldier.RangedWeapons.Clear();
            soldier.EquippedRangedWeapons.Clear();
            soldier.RangedWeapons.Add(weapon);
            soldier.EquippedRangedWeapons.Add(weapon);
            Place(grid, soldier, side: false, x: i, y: 0);
        }

        BattleTurnResolver resolver = CreateResolver(
            grid,
            [victims],
            [marines],
            attackerAggression: Aggression.Aggressive,
            defenderAggression: Aggression.Avoid);
        bool completed = false;
        resolver.OnBattleComplete += (_, _) => completed = true;
        for (int turn = 0; turn < 1000 && !completed; turn++)
        {
            resolver.ProcessNextTurn();
        }

        Assert.True(completed);
        BattleOutcome outcome = Assert.IsType<BattleOutcome>(resolver.BattleHistory.Outcome);
        List<BattleEvent> events = resolver.BattleHistory.Turns
            .SelectMany(turn => turn.Events)
            .ToList();
        BattleEvent routed = events.FirstOrDefault(e => e.Type == BattleEventType.SquadRouted);
        Assert.True(routed != null,
            $"end={outcome.EndReason} turns={resolver.BattleHistory.Turns.Count} "
            + $"events=[{string.Join("; ", events.Select(e => $"{e.TurnNumber}:{e.Type}:{e.PrimarySquadId}"))}] "
            + $"victimsAble={victims.AbleSoldiers.Count} marinesAble={marines.AbleSoldiers.Count}");
        Assert.Equal(victims.Id, routed.PrimarySquadId);
        Assert.Contains(victims.Id, outcome.RoutingSquadIds);
        Assert.Equal(BattleEndReason.Rout, outcome.EndReason);
        Assert.Equal(BattleSide.Opposing, outcome.SideHoldingField);

        // Rout is sticky (§6): after the SquadRouted turn, every later snapshot in which
        // the squad is still active shows it Routing — it never reverts to a fighting role.
        foreach (BattleTurn turn in resolver.BattleHistory.Turns
            .Where(t => t.TurnNumber > routed.TurnNumber))
        {
            if (turn.State.AttackerSquads.TryGetValue(victims.Id, out BattleSquadSnapshot snapshot)
                && snapshot.Status == BattleSquadStatus.Active)
            {
                Assert.Equal(WithdrawalRole.Routing, snapshot.WithdrawalRole);
            }
        }
    }

    [Fact]
    public void MarineSquadsExchangingFire_NeverRout()
    {
        // Marines vs marines, both taking real casualties: high Ego (14) must hold both
        // sides Steady to the end — no SquadRouted event on either side (§2, §10 Phase 7).
        BattleSquad first = CreateSquad(
            "First Marines", 74_301, TestModelFactory.MarineTemplate, count: 5, ego: 14f);
        BattleSquad second = CreateSquad(
            "Second Marines", 74_401, TestModelFactory.MarineTemplate, count: 5, ego: 14f);
        BattleGridManager grid = new();
        for (int i = 0; i < 5; i++)
        {
            Place(grid, first.Soldiers[i], side: true, x: i, y: 0);
            Place(grid, second.Soldiers[i], side: false, x: i, y: 30);
        }

        BattleTurnResolver resolver = CreateResolver(
            grid,
            [first],
            [second],
            attackerAggression: Aggression.Aggressive,
            defenderAggression: Aggression.Aggressive);
        bool completed = false;
        resolver.OnBattleComplete += (_, _) => completed = true;
        for (int turn = 0; turn < 1000 && !completed; turn++)
        {
            resolver.ProcessNextTurn();
        }

        Assert.True(completed);
        Assert.DoesNotContain(
            resolver.BattleHistory.Turns.SelectMany(turn => turn.Events),
            e => e.Type == BattleEventType.SquadRouted);
        Assert.Empty(resolver.BattleHistory.Outcome.RoutingSquadIds);
    }

    private static BattleTurnResolver CreateResolver(
        BattleGridManager grid,
        IList<BattleSquad> attackers,
        IList<BattleSquad> defenders,
        Aggression attackerAggression,
        Aggression defenderAggression)
    {
        GameRulesData rules = new();
        Date date = new(1, 1, 1);
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
            GameDataSingleton.Instance.LoadGameDataFromBlob(rules, date, null);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }

        RNG.Reset(74_000);
        BattleAftermathDependencies aftermath = new(
            date,
            StaticRNG.Instance,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, StaticRNG.Instance, aftermath);
        return new BattleTurnResolver(
            grid,
            attackers,
            defenders,
            region: null,
            execution,
            new BattleSideProfile(attackerAggression, BattleRole.Attacker),
            new BattleSideProfile(defenderAggression, BattleRole.Defender));
    }

    private static BattleSquad CreateSquad(
        string name,
        int firstSoldierId,
        SoldierTemplate soldierTemplate,
        int count,
        float ego,
        float dexterity = 10f)
    {
        Faction faction = CreateFaction(firstSoldierId + 10_000, name, soldierTemplate);
        SquadTemplate squadTemplate = new(
            firstSoldierId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(soldierTemplate, 0, (byte)count)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Squad squad = new(name, null, squadTemplate);
        for (int i = 0; i < count; i++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(soldierTemplate, $"{name} {i + 1}");
            soldier.Id = firstSoldierId + i;
            soldier.Ego = ego;
            soldier.Dexterity = dexterity;
            squad.AddSquadMember(soldier);
        }
        return new BattleSquad(false, squad);
    }

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        int x,
        int y)
    {
        soldier.TopLeft = new ValueTuple<int, int>(x, y);
        grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
    }

    private static Faction CreateFaction(int id, string name, SoldierTemplate template)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: false,
            canInfiltrate: false,
            GrowthType.None,
            new Dictionary<int, Species> { [template.Species.Id] = template.Species },
            new Dictionary<int, SoldierTemplate> { [template.Id] = template },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, Models.Units.UnitTemplate>(),
            new Dictionary<int, Models.Fleets.BoatTemplate>(),
            new Dictionary<int, Models.Fleets.ShipTemplate>(),
            new Dictionary<int, Models.Fleets.FleetTemplate>());
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
