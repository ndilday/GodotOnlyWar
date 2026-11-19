using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace OnlyWar.Diagnostics.Battles.Calibration;

/// <summary>
/// Always-on checks of what the gauntlet fit makes of cells with no usable break-even (2026-10-25):
/// one-sided and separated cells become bounds or brackets, and the single-scale fit holds a bound
/// only when its values would break it. Synthetic data; no battles.
/// </summary>
public class GauntletBoundsTests
{
    private static BattleRow Row(int modelsA, int modelsB, double score, int seed) =>
        new($"k{modelsA}-{modelsB}-{seed}", "a", "b", "1", "2", 1, 1, modelsA, modelsB, seed,
            score, 0, 0, 10, "Rout", "db", "code", 0, "Aggressive");

    private static List<BattleRow> Point(int modelsA, int modelsB, params double[] scores) =>
        scores.Select((score, seed) => Row(modelsA, modelsB, score, seed)).ToList();

    [Fact]
    public void ACellThatAWonEverywhere_IsALowerBoundAtItsLargestRatio()
    {
        List<BattleRow> rows = [.. Point(10, 10, 1, 1, 1, 1), .. Point(10, 40, 1, 1, 1, 1)];

        CellFit cell = GauntletAnalysis.FitCell("a", "b", rows);

        Assert.True(cell.IsBound);
        Assert.Equal(System.Math.Log(4.0), cell.LowerX, 9);
        Assert.True(double.IsNaN(cell.UpperX));
    }

    [Fact]
    public void ACellThatALostEverywhere_IsAnUpperBoundAtItsSmallestRatio()
    {
        List<BattleRow> rows = [.. Point(10, 10, 0, 0, 0, 0), .. Point(40, 10, 0, 0, 0, 0)];

        CellFit cell = GauntletAnalysis.FitCell("a", "b", rows);

        Assert.Equal(System.Math.Log(0.25), cell.UpperX, 9);
        Assert.True(double.IsNaN(cell.LowerX));
    }

    [Fact]
    public void ASeparatedCellWithWinsAndLosses_IsBracketedInTheGap()
    {
        List<BattleRow> rows = [.. Point(10, 10, 1, 1, 1, 1), .. Point(10, 40, 0, 0, 0, 0)];

        CellFit cell = GauntletAnalysis.FitCell("a", "b", rows);

        Assert.Equal("bracketed", cell.Status);
        Assert.Equal(0.5 * System.Math.Log(4.0), cell.X0, 9);
        Assert.False(cell.IsBound);
    }

    private static CellFit Observed(string a, string b, double x0) =>
        new() { EntryA = a, EntryB = b, Points = [], Status = "ok", X0 = x0, SeX0 = 0.1 };

    private static CellFit LowerBound(string a, string b, double lowerX) =>
        new() { EntryA = a, EntryB = b, Points = [], Status = "separated", LowerX = lowerX };

    [Fact]
    public void ABoundTheValuesBreak_IsHeldAtItsBound()
    {
        // a is worth e^1 PDF and b e^0 by their PDF cells, so the fit alone says a-b = 1. a beat b at
        // every size up to e^2 b per a, so the break-even is above 2.
        LeastSquaresFit fit = GauntletAnalysis.FitLeastSquares(
            [Observed("a", "pdf", 1.0), Observed("b", "pdf", 0.0), LowerBound("a", "b", 2.0)],
            "pdf", ["pdf", "a", "b"]);
        double a = fit.Values.Single(value => value.Key == "a").U;
        double b = fit.Values.Single(value => value.Key == "b").U;

        Assert.Single(fit.ActiveBounds);
        Assert.True(a - b > 1.2, $"a - b = {a - b}: the bound should pull the two apart");
    }

    [Fact]
    public void ABoundTheValuesKeep_ChangesNothing()
    {
        LeastSquaresFit without = GauntletAnalysis.FitLeastSquares(
            [Observed("a", "pdf", 1.0), Observed("b", "pdf", 0.0)], "pdf", ["pdf", "a", "b"]);
        LeastSquaresFit with = GauntletAnalysis.FitLeastSquares(
            [Observed("a", "pdf", 1.0), Observed("b", "pdf", 0.0), LowerBound("a", "b", 0.5)],
            "pdf", ["pdf", "a", "b"]);

        Assert.Empty(with.ActiveBounds);
        Assert.Equal(1, with.Bounds);
        Assert.Equal(
            without.Values.Single(value => value.Key == "a").U,
            with.Values.Single(value => value.Key == "a").U, 9);
    }

    [Theory]
    [InlineData("Rout", 1.0, 0.2, 0.9, 1.0)]
    [InlineData("TurnCap", 0.5, 1.0, 0.45, 0.6 / 0.65)]
    [InlineData("TurnCap", 0.5, 0.67, 0.89, 0.16 / 0.54)]
    [InlineData("TurnCap", 0.5, 1.0, 1.0, 0.5)]
    public void ATurnCapBattle_IsScoredByCasualties(string reason, double holderScore, double aLeft, double bLeft, double expected) =>
        Assert.Equal(expected, BattleRow.ScoreOf(reason, holderScore, aLeft, bLeft), 9);

    [Fact]
    public void AStoredTurnCapRow_IsRescoredWhenRead()
    {
        string line = Row(10, 10, 0.5, 1).ToLine().Replace("\tRout\t", "\tTurnCap\t");

        Assert.True(BattleRow.TryParse(line, out BattleRow row));
        // Both sides lost everything: an even share.
        Assert.Equal(0.5, row.Score, 9);
        Assert.True(BattleRow.TryParse(line.Replace("\t0.5\t0\t0\t", "\t0.5\t1\t0.5\t"), out BattleRow ahead));
        Assert.Equal(0.55 / 0.6, ahead.Score, 9);
    }

    [Fact]
    public void AnEntryKnownOnlyByBounds_HasNoValue()
    {
        LeastSquaresFit fit = GauntletAnalysis.FitLeastSquares(
            [Observed("a", "pdf", 1.0), LowerBound("c", "a", 2.0)], "pdf", ["pdf", "a", "c"]);

        Assert.Contains("c", fit.Unconnected);
        Assert.DoesNotContain(fit.Values, value => value.Key == "c");
    }
}
