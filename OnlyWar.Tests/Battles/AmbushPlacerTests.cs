using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Ambush placement (see OnlyWar_TDD.md §6.6) lays the ambushed force out as a
/// column and sets the ambushers in an L: a long leg parallel to the column plus one short
/// leg capping an end, with the far corner left open so no element fires across the kill zone
/// into another. These tests pin that shape down and guard against the large-squad
/// degeneration (everything piling onto one flank in a single file) that the box-tiling
/// placer used to produce.
/// </summary>
public class AmbushPlacerTests
{
    private static int _nextId = 1;

    private static BattleSquad CreateSquad(string name, int size, bool isPlayer)
    {
        Soldier[] soldiers = new Soldier[size];
        for (int i = 0; i < size; i++)
        {
            Soldier s = TestModelFactory.CreateSoldier(template: TestModelFactory.MarineTemplate, name: $"{name}-{i}");
            s.Id = _nextId++;
            soldiers[i] = s;
        }
        Squad squad = TestModelFactory.CreateSquad(name, soldiers);
        return new BattleSquad(isPlayer, squad);
    }

    private readonly record struct Bounds(int MinX, int MaxX, int MinY, int MaxY);

    private static Bounds BoundsOf(BattleGridManager grid, BattleSquad squad)
    {
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (BattleSoldier s in squad.AbleSoldiers)
        {
            foreach (ValueTuple<int, int> c in grid.GetSoldierPosition(s.Soldier.Id))
            {
                minX = global::System.Math.Min(minX, c.Item1); maxX = global::System.Math.Max(maxX, c.Item1);
                minY = global::System.Math.Min(minY, c.Item2); maxY = global::System.Math.Max(maxY, c.Item2);
            }
        }
        return new Bounds(minX, maxX, minY, maxY);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(20, 30)]
    [InlineData(50, 50)]
    public void PlaceSquads_LargeSquads_DoNotThrowAndAreFullyMapped(int ambushedSize, int ambusherSize)
    {
        BattleGridManager grid = new();
        List<BattleSquad> ambushed = [CreateSquad("Ambushed", ambushedSize, true)];
        List<BattleSquad> ambushing =
        [
            CreateSquad("A0", ambusherSize, false),
            CreateSquad("A1", ambusherSize, false),
            CreateSquad("A2", ambusherSize, false),
            CreateSquad("A3", ambusherSize, false),
        ];

        AmbushPlacer placer = new(grid, 70);
        Dictionary<BattleSquad, ValueTuple<int, int>> map = placer.PlaceSquads(ambushed, ambushing);

        // Every squad on both sides makes it into the returned position map...
        Assert.Equal(ambushed.Count + ambushing.Count, map.Count);
        // ...and onto the grid, with no overlaps (the grid throws on collision, so reaching
        // here already proves that; assert the soldier count as a belt-and-braces check).
        int placed = ambushed.Concat(ambushing).Sum(s => s.AbleSoldiers.Count(a => grid.GetSoldierPosition(a.Soldier.Id) != null));
        Assert.Equal(ambushed.Concat(ambushing).Sum(s => s.AbleSoldiers.Count), placed);
    }

    /// <summary>
    /// XIBARRUS ZETA REGRESSION (2026-08-04). A derived engagement range of ONE YARD set three
    /// marine squads a single yard off a column containing a Broodlord and a Carnifex, which
    /// reached them on turn 1; 29 of 30 marines died. The derivation is fixed in
    /// <c>BattleEngagementFrameBuilder.CalculatePreferredOpeningRange</c>, but the placer must not
    /// be able to produce that geometry again whatever range it is handed: an ambush sprung inside
    /// one bound of the ambushed force's own movement buys a single volley and then becomes the
    /// melee the ambush existed to avoid.
    /// </summary>
    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData((ushort)3)]
    public void PlaceSquads_RangeInsideOneEnemyBound_StillStandsOffAFullBound(ushort range)
    {
        BattleGridManager grid = new();
        List<BattleSquad> ambushed = [CreateSquad("Column", 10, false)];
        List<BattleSquad> ambushing = [CreateSquad("Ambusher", 10, true)];

        AmbushPlacer placer = new(grid, range);
        placer.PlaceSquads(ambushed, ambushing);

        // One turn of the COLUMN's movement is the quantity that matters: it is how far they get
        // on the turn the ambush is sprung.
        int bound = (int)System.Math.Ceiling(ambushed[0].GetSquadMove());
        Bounds kill = BoundsOf(grid, ambushed[0]);
        Bounds leg = BoundsOf(grid, ambushing[0]);
        int gap = kill.MinX - leg.MaxX;

        Assert.True(
            gap >= bound,
            $"an engagement range of {range} left the ambushers {gap} from the column, inside its "
                + $"{bound}-yard bound");
    }

    [Fact]
    public void PlaceSquads_UsesTwoAdjacentSides_WithOpenCorner()
    {
        BattleGridManager grid = new();
        List<BattleSquad> ambushed = [CreateSquad("Ambushed", 50, true)];
        // enough squads that a single flank cannot swallow them all
        List<BattleSquad> ambushing = Enumerable.Range(0, 6)
            .Select(i => CreateSquad($"A{i}", 40, false))
            .ToList();

        AmbushPlacer placer = new(grid, 70);
        placer.PlaceSquads(ambushed, ambushing);

        Bounds kill = BoundsOf(grid, ambushed[0]);

        int westOfKill = 0;   // long leg: west face
        int northOfKill = 0;  // short leg: north face
        foreach (BattleSquad squad in ambushing)
        {
            Bounds b = BoundsOf(grid, squad);
            bool west = b.MaxX < kill.MinX;
            bool north = b.MinY > kill.MaxY;
            Assert.True(west || north, $"{squad.Name} is neither west nor north of the kill zone");
            // No squad may sit in the open (north-west) corner: west of the kill zone AND
            // north of it at the same time.
            Assert.False(west && north, $"{squad.Name} occupies the open corner");
            if (west) westOfKill++;
            if (north) northOfKill++;
        }

        // Both sides are actually used (an L, not a single firing line)...
        Assert.True(westOfKill > 0, "no squads on the long (west) leg");
        Assert.True(northOfKill > 0, "no squads on the short (north) leg");
        // ...and the long leg carries the majority.
        Assert.True(westOfKill >= northOfKill, "the long leg should hold at least as many squads as the short leg");
    }

    [Fact]
    public void PlaceSquads_SingleAmbusher_IsLinear()
    {
        BattleGridManager grid = new();
        List<BattleSquad> ambushed = [CreateSquad("Ambushed", 50, true)];
        List<BattleSquad> ambushing = [CreateSquad("Lone", 50, false)];

        AmbushPlacer placer = new(grid, 70);
        placer.PlaceSquads(ambushed, ambushing);

        Bounds kill = BoundsOf(grid, ambushed[0]);
        Bounds b = BoundsOf(grid, ambushing[0]);
        // a lone squad forms a plain linear ambush on the long (west) leg
        Assert.True(b.MaxX < kill.MinX, "lone ambusher should be on the west leg");
    }

    [Fact]
    public void PlaceSquads_HugeEngagementRange_DoesNotWrapCoordinates()
    {
        BattleGridManager grid = new();
        List<BattleSquad> ambushed = [CreateSquad("Ambushed", 5, true)];
        List<BattleSquad> ambushing =
        [
            CreateSquad("A0", 5, false),
            CreateSquad("A1", 5, false),
            CreateSquad("A2", 5, false)
        ];

        AmbushPlacer placer = new(grid, ushort.MaxValue);
        Dictionary<BattleSquad, ValueTuple<int, int>> map = placer.PlaceSquads(ambushed, ambushing);

        Assert.Equal(ambushed.Count + ambushing.Count, map.Count);

        // The assertion this test was named for but never made. It was written alongside a ceiling
        // that kept placement away from the wrap, so counting the squads was all it could check.
        // BattleSquadPlacer no longer narrows cell coordinates through (short), so the legs really
        // do stand off 65,535 yards instead of truncating back to cells beside the kill zone -- the
        // failure mode was a collision throw, but a standoff that silently landed at arm's length
        // would pass a count-only check just as well.
        Bounds kill = BoundsOf(grid, ambushed[0]);
        foreach (BattleSquad ambusher in ambushing)
        {
            Bounds b = BoundsOf(grid, ambusher);
            // System.Math, not the OnlyWar.Tests.Math namespace this file sits next to.
            int gap = System.Math.Max(
                System.Math.Max(kill.MinX - b.MaxX, b.MinX - kill.MaxX),
                System.Math.Max(kill.MinY - b.MaxY, b.MinY - kill.MaxY));
            Assert.True(
                gap > 60_000,
                $"{ambusher.Name} stood off only {gap} from the kill zone at a 65,535 engagement "
                + "range - coordinates wrapped");
        }
    }

    // AnnihilationPlacer shared BattleSquadPlacer's truncation and, unlike AmbushPlacer, never had a
    // ceiling of its own to hide behind. Meeting engagements and reciprocal assaults route through
    // it, and Phase 6 made their opening range derived rather than clamped, so the input range is no
    // longer bounded by anything but weapon reach.
    [Fact]
    public void AnnihilationPlacer_HugeRange_DoesNotWrapCoordinates()
    {
        BattleGridManager grid = new();
        List<BattleSquad> bottom = [CreateSquad("Bottom", 5, true)];
        List<BattleSquad> top = [CreateSquad("Top", 5, false)];

        AnnihilationPlacer placer = new(grid, ushort.MaxValue);
        Dictionary<BattleSquad, ValueTuple<int, int>> map = placer.PlaceSquads(bottom, top);

        Assert.Equal(bottom.Count + top.Count, map.Count);

        Bounds bottomBounds = BoundsOf(grid, bottom[0]);
        Bounds topBounds = BoundsOf(grid, top[0]);
        Assert.True(
            topBounds.MinY - bottomBounds.MaxY > 60_000,
            $"the two lines opened only {topBounds.MinY - bottomBounds.MaxY} apart at a 65,535 "
            + "range - coordinates wrapped");
    }
}
