using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace OnlyWar.Diagnostics.Battles.Calibration;

/// <summary>
/// Always-on checks of the single-scale least-squares fit (GauntletAnalysis.FitLeastSquares). The
/// cells are synthetic break-evens, so each test takes milliseconds and fights no battles.
/// </summary>
public class LeastSquaresFitTests
{
    private static CellFit Cell(string a, string b, double lnBreakEven, double se = 0.05, string status = "ok") =>
        new() { EntryA = a, EntryB = b, X0 = lnBreakEven, SeX0 = se, Status = status, Points = [] };

    private static double Value(LeastSquaresFit fit, string key) => fit.Values.Single(value => value.Key == key).Value;

    [Fact]
    public void ConsistentCells_RecoverTheValuesExactly()
    {
        // pdf = 5, tac = 90, ork = 10: each cell's ln b is u_A - u_B.
        Dictionary<string, double> u = new() { ["pdf"] = 0, ["tac"] = System.Math.Log(18), ["ork"] = System.Math.Log(2) };
        List<CellFit> cells =
        [
            Cell("tac", "pdf", u["tac"] - u["pdf"]),
            Cell("pdf", "tac", u["pdf"] - u["tac"]),
            Cell("ork", "pdf", u["ork"] - u["pdf"]),
            Cell("ork", "tac", u["ork"] - u["tac"])
        ];

        LeastSquaresFit fit = GauntletAnalysis.FitLeastSquares(cells, "pdf", ["pdf", "tac", "ork"]);

        Assert.Equal(5.0, Value(fit, "pdf"), 9);
        Assert.Equal(90.0, Value(fit, "tac"), 6);
        Assert.Equal(10.0, Value(fit, "ork"), 6);
        Assert.Equal(0.0, fit.ResidualSd, 9);
        Assert.Empty(fit.Unconnected);
    }

    [Fact]
    public void ANonTransitiveTriangle_SpreadsItsResidualOverEveryCell()
    {
        // The *9 references: a marine is worth 18.4 PDF, an Ork 1 PDF, and a marine 8 Orks. No three
        // values fit all three, so no single cell should absorb the whole disagreement.
        List<CellFit> cells =
        [
            Cell("tac", "pdf", System.Math.Log(18.4)),
            Cell("ork", "pdf", System.Math.Log(1.0)),
            Cell("tac", "ork", System.Math.Log(8.0))
        ];

        LeastSquaresFit fit = GauntletAnalysis.FitLeastSquares(cells, "pdf", ["pdf", "tac", "ork"]);

        double[] residuals = fit.Residuals.Values.Select(System.Math.Abs).ToArray();
        Assert.All(residuals, e => Assert.True(e > 0.1, $"residual {e} should carry part of the disagreement"));
        Assert.True(residuals.Max() / residuals.Min() < 1.01, "equal weights should share the disagreement equally");
        // The Ork lands between its two readings (5 directly, 18.4 / 8 * 5 = 11.5 via the marine).
        Assert.InRange(Value(fit, "ork"), 5.0, 11.5);
    }

    [Fact]
    public void OnlyOkCells_EnterTheFit()
    {
        List<CellFit> cells =
        [
            Cell("tac", "pdf", System.Math.Log(18)),
            Cell("carn", "pdf", System.Math.Log(1000), status: "separated"),
            Cell("carn", "tac", System.Math.Log(6))
        ];

        LeastSquaresFit fit = GauntletAnalysis.FitLeastSquares(cells, "pdf", ["pdf", "tac", "carn"]);

        Assert.Equal(2, fit.Used.Count);
        Assert.Equal(90.0 * 6, Value(fit, "carn"), 6);
    }

    [Fact]
    public void AnEntryWithNoChainToTheAnchor_IsReportedAndGetsNoValue()
    {
        List<CellFit> cells =
        [
            Cell("tac", "pdf", System.Math.Log(18)),
            Cell("horm", "gaunt", System.Math.Log(0.5))
        ];

        LeastSquaresFit fit = GauntletAnalysis.FitLeastSquares(cells, "pdf", ["pdf", "tac", "horm", "gaunt"]);

        Assert.Equal(["horm", "gaunt"], fit.Unconnected);
        Assert.DoesNotContain(fit.Values, value => value.Key is "horm" or "gaunt");
        Assert.Single(fit.Used);
    }

    [Fact]
    public void CellsThatAgreeWithinTheirNoise_AreWeightedByPrecision()
    {
        // Two readings of one ratio, 2 tightly and 2.2 loosely: the gap is inside the loose cell's
        // noise, so tau is 0 and the tight reading dominates.
        List<CellFit> cells =
        [
            Cell("ork", "pdf", System.Math.Log(2), se: 0.02),
            Cell("pdf", "ork", -System.Math.Log(2.2), se: 0.2)
        ];

        LeastSquaresFit fit = GauntletAnalysis.FitLeastSquares(cells, "pdf", ["pdf", "ork"]);

        Assert.Equal(0.0, fit.Tau, 9);
        Assert.InRange(Value(fit, "ork"), 10.0, 10.05);
    }

    [Fact]
    public void CellsThatDisagreeBeyondTheirNoise_CountAboutEqually()
    {
        // 2 and 4, each read far more precisely than the gap between them: matchups, not battles, set
        // the spread, so a sharp cell must not win just for being sharp (the *9 refit's dispersion
        // was 168 with precision weights alone).
        List<CellFit> cells =
        [
            Cell("ork", "pdf", System.Math.Log(2), se: 0.02),
            Cell("pdf", "ork", -System.Math.Log(4), se: 0.06)
        ];

        LeastSquaresFit fit = GauntletAnalysis.FitLeastSquares(cells, "pdf", ["pdf", "ork"]);

        Assert.True(fit.Tau > 0.3, $"tau {fit.Tau} should carry the disagreement");
        // Near the geometric mean of 10 and 20, not near the sharper cell's 10.
        Assert.InRange(Value(fit, "ork"), 13.0, 15.5);
    }
}
