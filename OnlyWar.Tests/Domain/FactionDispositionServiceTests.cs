using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

// AreAllied is a deliberate policy, not something derived from the enemy rules, so it is pinned
// here directly. Until there is a diplomacy system the only alliance in the game is the player's
// Chapter and the world's own defence forces; "not currently at war with" must not be mistaken for
// it, or two xenos factions would end up manning each other's fortifications (RegionDefenses).
public class FactionDispositionServiceTests
{
    private static Faction Player() => SectorSimulationFixture
        .CreateDetached().Sector.PlayerForce.Faction;

    private static Faction Default() => SectorSimulationFixture.CreateDetached().Default;

    private static Faction Xenos(int id, string name = "Tyranids") =>
        SectorSimulationFixture.BuildTestFaction(id, name, isPlayer: false, isDefault: false);

    [Fact]
    public void AreAllied_PlayerAndDefaultFaction_AreTheOneAlliance()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Faction player = fixture.Sector.PlayerForce.Faction;
        Faction imperium = fixture.Default;

        Assert.True(FactionDispositionService.AreAllied(player, imperium));
        Assert.True(FactionDispositionService.AreAllied(imperium, player));
    }

    [Fact]
    public void AreAllied_TwoNonImperialFactions_AreNotAllies()
    {
        // They are not modelled as fighting each other either, but that is not alliance: a Tyranid
        // swarm does not shelter behind a cult's earthworks.
        Assert.False(FactionDispositionService.AreAllied(Xenos(10), Xenos(11, "Genestealer Cult")));
    }

    [Fact]
    public void AreAllied_ImperialAndXenos_AreNotAllies()
    {
        Assert.False(FactionDispositionService.AreAllied(Player(), Xenos(10)));
        Assert.False(FactionDispositionService.AreAllied(Default(), Xenos(10)));
    }

    [Fact]
    public void AreAllied_AFactionWithItself_IsAlwaysTrue()
    {
        // Identity, not diplomacy. Callers sweeping a region for "everyone on this side" rely on
        // this to include the faction they started from - without it a faction would be left out
        // of its own defence (PrepareAssaultMissionStep.AssembleDefendingForce).
        Faction xenos = Xenos(10);
        Assert.True(FactionDispositionService.AreAllied(xenos, xenos));
        Assert.True(FactionDispositionService.AreAllied(Player(), Player()));
    }

    [Fact]
    public void AreAllied_NullFaction_IsNotAnAlliance()
    {
        Assert.False(FactionDispositionService.AreAllied(null, Player()));
        Assert.False(FactionDispositionService.AreAllied(Player(), null));
        Assert.False(FactionDispositionService.AreAllied(null, null));
    }

    [Fact]
    public void IsImperial_CoversPlayerAndDefaultOnly()
    {
        Assert.True(FactionDispositionService.IsImperial(Player()));
        Assert.True(FactionDispositionService.IsImperial(Default()));
        Assert.False(FactionDispositionService.IsImperial(Xenos(10)));
        Assert.False(FactionDispositionService.IsImperial(null));
    }
}
