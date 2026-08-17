using System;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

// The relationship ledger is the authority for alliance. This file keeps the small domain-level
// contract pinned: absent pairs are hostile, explicit alliances are symmetric, and same-faction
// identity is allied without a persisted row.
public class FactionRelationshipServiceTests
{
    private static Faction Player() => SectorSimulationFixture
        .CreateDetached().Sector.PlayerForce.Faction;

    private static Faction Default() => SectorSimulationFixture.CreateDetached().Default;

    private static Faction Xenos(int id, string name = "Tyranids") =>
        SectorSimulationFixture.BuildTestFaction(id, name, isPlayer: false, isDefault: false);

    [Fact]
    public void Ledger_PlayerAndDefaultFaction_CanBeExplicitlyAllied()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Faction player = fixture.Sector.PlayerForce.Faction;
        Faction imperium = fixture.Default;
        FactionRelationshipLedger ledger = new();
        ledger.SetStance(player, imperium, FactionStance.Allied);

        Assert.Equal(FactionStance.Allied, ledger.GetStance(player, imperium));
        Assert.Equal(FactionStance.Allied, ledger.GetStance(imperium, player));
    }

    [Fact]
    public void Ledger_TwoNonImperialFactions_DefaultToHostile()
    {
        // They are not modelled as fighting each other either, but that is not alliance: a Tyranid
        // swarm does not shelter behind a cult's earthworks.
        Faction first = Xenos(10);
        Faction second = Xenos(11, "Genestealer Cult");
        Assert.Equal(FactionStance.Hostile, new FactionRelationshipLedger().GetStance(first, second));
    }

    [Fact]
    public void Ledger_ImperialAndXenos_DefaultToHostile()
    {
        FactionRelationshipLedger ledger = new();
        Assert.Equal(FactionStance.Hostile, ledger.GetStance(Player(), Xenos(10)));
        Assert.Equal(FactionStance.Hostile, ledger.GetStance(Default(), Xenos(10)));
    }

    [Fact]
    public void Ledger_AFactionWithItself_IsAlwaysAllied()
    {
        // Identity, not diplomacy. Callers sweeping a region for "everyone on this side" rely on
        // this to include the faction they started from - without it a faction would be left out
        // of its own defence (PrepareAssaultMissionStep.AssembleDefendingForce).
        Faction xenos = Xenos(10);
        FactionRelationshipLedger ledger = new();
        Assert.Equal(FactionStance.Allied, ledger.GetStance(xenos, xenos));
        Assert.Equal(FactionStance.Allied, ledger.GetStance(Player(), Player()));
    }

    [Fact]
    public void Ledger_NullFaction_IsRejected()
    {
        FactionRelationshipLedger ledger = new();
        Assert.Throws<ArgumentNullException>(() => ledger.GetStance(null, Player()));
        Assert.Throws<ArgumentNullException>(() => ledger.GetStance(Player(), null));
        Assert.Throws<ArgumentNullException>(() => ledger.GetStance(null, null));
    }

    [Fact]
    public void IsImperial_CoversPlayerAndDefaultOnly()
    {
        Assert.True(FactionRelationshipService.IsImperial(Player()));
        Assert.True(FactionRelationshipService.IsImperial(Default()));
        Assert.False(FactionRelationshipService.IsImperial(Xenos(10)));
        Assert.False(FactionRelationshipService.IsImperial(null));
    }
}
