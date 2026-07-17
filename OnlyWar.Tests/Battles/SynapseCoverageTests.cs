using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 1 of Design/Active/MoraleAndRout.md: SpeciesAbilities.Synapse, derived
/// BattleSquad.SquadProvidesSynapse, and the per-turn SynapseCoverageEvaluator predicate.
/// No morale check exists yet (Phase 3) — these tests assert coverage only.
/// </summary>
public class SynapseCoverageTests
{
    private static BattleSoldier Place(BattleGridManager grid, BattleSquad squad, int index,
                                       bool side, int x, int y)
    {
        BattleSoldier soldier = squad.AbleSoldiers[index];
        Tuple<int, int> cell = new(x, y);
        grid.PlaceSoldier(soldier, side, new List<Tuple<int, int>> { cell });
        soldier.TopLeft = cell;
        return soldier;
    }

    private static BattleSquad CreateSquad(string name, SoldierTemplate template, params int[] ids)
    {
        Soldier[] soldiers = new Soldier[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(template: template, name: $"{name}-{ids[i]}");
            soldier.Id = ids[i];
            soldiers[i] = soldier;
        }
        Squad squad = TestModelFactory.CreateSquad(name, soldiers);
        return new BattleSquad(false, squad);
    }

    // --- SquadProvidesSynapse derivation ---

    [Fact]
    public void SquadProvidesSynapse_TrueForSquadBuiltFromSynapseSpecies()
    {
        // Stands in for a Tyranid Warrior Squad (Design §4.1: Warriors carry the flag even
        // though their SquadType is Elite, not HQ — see §3.2).
        BattleSquad warriorLike = CreateSquad("Warriors", TestModelFactory.SynapseProviderTemplate, 1, 2, 3);

        Assert.True(warriorLike.SquadProvidesSynapse);
        Assert.True(warriorLike.SynapseRadius > 0f);
    }

    [Fact]
    public void SquadProvidesSynapse_FalseForOrdinarySpecies()
    {
        // Stands in for a Termagaunt Squad — no synapse ability of its own.
        BattleSquad gauntLike = CreateSquad("Gaunts", TestModelFactory.MarineTemplate, 1, 2, 3);

        Assert.False(gauntLike.SquadProvidesSynapse);
        Assert.Equal(0f, gauntLike.SynapseRadius);
    }

    [Fact]
    public void SquadProvidesSynapse_FalseForIndependentWilledSpecies()
    {
        // Stands in for a Genestealer Squad: high Ego, deliberately not a synapse provider
        // (Design §4.1 — "Not Genestealer... Ego 20 already covers their independence").
        BattleSquad genestealerLike = CreateSquad("Genestealers", TestModelFactory.SergeantTemplate, 1);

        Assert.False(genestealerLike.SquadProvidesSynapse);
    }

    // --- IsSynapseCovered ---

    [Fact]
    public void IsSynapseCovered_TrueWhenProviderWithinRadius()
    {
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad("Provider", TestModelFactory.SynapseProviderTemplate, 1);
        BattleSquad covered = CreateSquad("Covered", TestModelFactory.MarineTemplate, 2);

        Place(grid, provider, 0, side: true, x: 0, y: 0);
        // SynapseProviderSpecies radius is 10; place the covered squad 5 units away.
        Place(grid, covered, 0, side: true, x: 5, y: 0);

        bool result = SynapseCoverageEvaluator.IsSynapseCovered(
            covered, new[] { provider, covered }, grid);

        Assert.True(result);
    }

    [Fact]
    public void IsSynapseCovered_FalseWhenProviderOutsideRadius()
    {
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad("Provider", TestModelFactory.SynapseProviderTemplate, 1);
        BattleSquad covered = CreateSquad("Covered", TestModelFactory.MarineTemplate, 2);

        Place(grid, provider, 0, side: true, x: 0, y: 0);
        // Radius is 10; place the covered squad 50 units away — well outside.
        Place(grid, covered, 0, side: true, x: 50, y: 0);

        bool result = SynapseCoverageEvaluator.IsSynapseCovered(
            covered, new[] { provider, covered }, grid);

        Assert.False(result);
    }

    [Fact]
    public void IsSynapseCovered_FalseWhenNoProviderPresent()
    {
        BattleGridManager grid = new();
        BattleSquad coveredA = CreateSquad("SquadA", TestModelFactory.MarineTemplate, 1);
        BattleSquad coveredB = CreateSquad("SquadB", TestModelFactory.MarineTemplate, 2);

        Place(grid, coveredA, 0, side: true, x: 0, y: 0);
        Place(grid, coveredB, 0, side: true, x: 1, y: 0);

        bool result = SynapseCoverageEvaluator.IsSynapseCovered(
            coveredA, new[] { coveredA, coveredB }, grid);

        Assert.False(result);
    }

    [Fact]
    public void IsSynapseCovered_ProviderKilledMidBattleSeversCoverageSameTurn()
    {
        // Design §5.1: coverage reads post-round physical state. A provider wiped this
        // round severs its dependents' coverage immediately — no one-turn grace period.
        // Simulated here by removing every provider soldier (as the resolver's casualty
        // pipeline does via BattleSquad.RemoveSoldier) and re-evaluating with the same
        // candidate list in the same call: there is no separate "prior turn" state to fall
        // back on, matching the stateless, recomputed-every-turn evaluator (§5.2).
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad("Provider", TestModelFactory.SynapseProviderTemplate, 1);
        BattleSquad covered = CreateSquad("Covered", TestModelFactory.MarineTemplate, 2);

        BattleSoldier providerSoldier = Place(grid, provider, 0, side: true, x: 0, y: 0);
        Place(grid, covered, 0, side: true, x: 5, y: 0);

        Assert.True(SynapseCoverageEvaluator.IsSynapseCovered(
            covered, new[] { provider, covered }, grid));

        // Provider is wiped this round.
        provider.RemoveSoldier(providerSoldier);

        bool result = SynapseCoverageEvaluator.IsSynapseCovered(
            covered, new[] { provider, covered }, grid);

        Assert.False(result);
    }

    [Fact]
    public void IsSynapseCovered_FalseWhenCoveredSquadHasNoAbleSoldiers()
    {
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad("Provider", TestModelFactory.SynapseProviderTemplate, 1);
        BattleSquad covered = CreateSquad("Covered", TestModelFactory.MarineTemplate, 2);

        Place(grid, provider, 0, side: true, x: 0, y: 0);
        BattleSoldier coveredSoldier = Place(grid, covered, 0, side: true, x: 5, y: 0);
        covered.RemoveSoldier(coveredSoldier);

        bool result = SynapseCoverageEvaluator.IsSynapseCovered(
            covered, new[] { provider, covered }, grid);

        Assert.False(result);
    }

    [Fact]
    public void IsSynapseCovered_SynapseProvidersDoNotSelfCover()
    {
        // A lone provider squad checked against only itself should not report coverage —
        // Design §4.2: "Synapse creatures themselves never check" (the morale skip is
        // driven by their own high Ego in Phase 3, but the coverage predicate itself
        // should not treat a squad as covering itself).
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad("Provider", TestModelFactory.SynapseProviderTemplate, 1);
        Place(grid, provider, 0, side: true, x: 0, y: 0);

        bool result = SynapseCoverageEvaluator.IsSynapseCovered(
            provider, new[] { provider }, grid);

        Assert.False(result);
    }
}
