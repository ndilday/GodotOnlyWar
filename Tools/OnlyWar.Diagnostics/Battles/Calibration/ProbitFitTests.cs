using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace OnlyWar.Diagnostics.Battles.Calibration;

/// <summary>
/// Always-on checks of the probit fitter behind the gauntlet report. They fight no battles: the data
/// are synthetic, drawn from a seeded stream, so each test is deterministic and takes milliseconds.
/// </summary>
public class ProbitFitTests
{
    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(1.0, 0.8413447460685429)]
    [InlineData(-1.0, 0.15865525393145707)]
    [InlineData(1.959963984540054, 0.975)]
    [InlineData(-3.0, 0.0013498980316301)]
    [InlineData(6.0, 0.9999999990134123)]
    [InlineData(-8.0, 6.22096057427178e-16)]
    public void NormalCdf_MatchesKnownValues(double x, double expected)
    {
        double actual = ProbitMath.NormalCdf(x);
        double tolerance = expected * 1e-9;
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }

    [Theory]
    [InlineData(3.841458820694124, 1, 0.05)]
    [InlineData(6.634896601021213, 1, 0.01)]
    [InlineData(5.991464547107979, 2, 0.05)]
    [InlineData(18.307038053275146, 10, 0.05)]
    [InlineData(124.3421134, 100, 0.05)]
    [InlineData(0.0, 4, 1.0)]
    public void ChiSquareSurvival_MatchesTableValues(double x, double degrees, double expected)
    {
        Assert.Equal(expected, ProbitMath.ChiSquareSurvival(x, degrees), 6);
        Assert.Equal(1.0, ProbitMath.ChiSquareSurvival(x, degrees) + ProbitMath.ChiSquareCdf(x, degrees), 12);
    }

    [Fact]
    public void LogGamma_MatchesFactorials()
    {
        Assert.Equal(System.Math.Log(24.0), ProbitMath.LogGamma(5.0), 10);
        Assert.Equal(0.5 * System.Math.Log(System.Math.PI), ProbitMath.LogGamma(0.5), 10);
    }

    [Fact]
    public void CellFit_WithExactFractionalResponses_RecoversTheParameters()
    {
        // Responses are the model's own probabilities, so the fit must land on the truth exactly. Some
        // are fractions, as an average of wins, losses and undecided battles would be.
        double alpha = 0.4;
        double beta = -1.7;
        List<FitPoint> points = Grid(-1.5, 1.5, 7)
            .Select(x => new FitPoint([1.0, x], ProbitMath.NormalCdf(alpha + beta * x), 25))
            .ToList();

        ProbitFitResult fit = ProbitFit.Fit(points, 2);

        Assert.True(fit.Converged);
        Assert.False(fit.Separated);
        Assert.Equal(alpha, fit.Coefficients[0], 4);
        Assert.Equal(beta, fit.Coefficients[1], 4);
        Assert.True(fit.Deviance < 1e-4);
    }

    [Fact]
    public void CellFit_FromSimulatedBattles_RecoversTheParametersWithSaneErrors()
    {
        double alpha = 0.4;
        double beta = -1.7;
        int battles = 60;
        SeededRNG random = new(2024);
        List<double> xs = Grid(-1.5, 1.5, 9).ToList();
        List<FitPoint> points = xs
            .Select(x => new FitPoint([1.0, x], Simulate(random, ProbitMath.NormalCdf(alpha + beta * x), battles), battles))
            .ToList();

        ProbitFitResult fit = ProbitFit.Fit(points, 2);

        Assert.True(fit.Converged);
        Assert.InRange(fit.Coefficients[0], alpha - 4 * fit.StandardErrors[0], alpha + 4 * fit.StandardErrors[0]);
        Assert.InRange(fit.Coefficients[1], beta - 4 * fit.StandardErrors[1], beta + 4 * fit.StandardErrors[1]);

        // The standard errors should match the Fisher information at the true parameters.
        double a = 0, b = 0, d = 0;
        foreach (double x in xs)
        {
            double eta = alpha + beta * x;
            double mu = ProbitMath.NormalCdf(eta);
            double weight = battles * ProbitMath.NormalPdf(eta) * ProbitMath.NormalPdf(eta) / (mu * (1 - mu));
            a += weight;
            b += weight * x;
            d += weight * x * x;
        }
        double determinant = a * d - b * b;
        Assert.InRange(fit.StandardErrors[0] / System.Math.Sqrt(d / determinant), 0.85, 1.15);
        Assert.InRange(fit.StandardErrors[1] / System.Math.Sqrt(a / determinant), 0.85, 1.15);
        Assert.Equal(1.0, fit.TotalWeight / (battles * xs.Count), 12);
    }

    [Fact]
    public void CellFit_BreakEvenAndItsInterval_CoverTheTruth()
    {
        // Break-even x0 = -alpha / beta; the 95% interval from the delta method should contain it.
        double alpha = -0.6;
        double beta = -2.2;
        SeededRNG random = new(77);
        List<BattleRow> rows = [];
        foreach (double x in Grid(-1.2, 1.2, 8))
        {
            int modelsB = (int)System.Math.Round(30 * System.Math.Exp(x));
            double p = ProbitMath.NormalCdf(alpha + beta * System.Math.Log(modelsB / 30.0));
            for (int seed = 1; seed <= 40; seed++)
            {
                rows.Add(Row("a", "b", 30, modelsB, seed, random.GetLinearDouble() < p ? 1.0 : 0.0));
            }
        }

        CellFit cell = GauntletAnalysis.FitCell("a", "b", rows);

        double truth = System.Math.Exp(-alpha / beta);
        Assert.Equal("ok", cell.Status);
        Assert.InRange(truth, cell.RatioLow, cell.RatioHigh);
        Assert.InRange(cell.RatioHigh / cell.RatioLow, 1.01, 1.6);
    }

    [Fact]
    public void GlobalFit_RecoversValuesAndNoise_AndSeesNoMatchupStructure()
    {
        double kappa = 2.5;
        Dictionary<string, double> truth = new() { ["pdf"] = 0.0, ["hyb"] = 0.8, ["tac"] = 2.1, ["ork"] = -0.2 };
        List<BattleRow> rows = SyntheticRows(truth, kappa, new SeededRNG(5150), battles: 40, matchup: (_, _) => 0.0);

        GauntletFit fit = GauntletAnalysis.Analyze(rows);

        Assert.Equal("pdf", fit.Anchor);
        Assert.InRange(fit.Kappa, kappa - 4 * fit.SeKappa, kappa + 4 * fit.SeKappa);
        Assert.InRange(fit.SeKappa, 0.01, 0.6);
        foreach (EntryValue value in fit.Values)
        {
            if (value.IsAnchor)
            {
                Assert.Equal(5.0, value.Value, 12);
                continue;
            }
            Assert.InRange(value.U, truth[value.Key] - 4 * value.SeU, truth[value.Key] + 4 * value.SeU);
            Assert.InRange(value.SeU, 0.002, 0.2);
            Assert.True(value.ValueLow < value.Value && value.Value < value.ValueHigh);
        }
        Assert.Equal(1.0 / (fit.Kappa * System.Math.Sqrt(2.0)), fit.Sigma, 12);
        Assert.True(fit.DegreesOfFreedom > 0);
        Assert.True(fit.PValue > 0.01, $"p = {fit.PValue}");
    }

    [Fact]
    public void GlobalFit_RoleTerm_RecoversAKnownAttackerBias()
    {
        double kappa = 2.5;
        double rho = 0.45;
        Dictionary<string, double> truth = new() { ["pdf"] = 0.0, ["hyb"] = 0.8, ["tac"] = 2.1, ["ork"] = -0.2 };
        List<BattleRow> rows = SyntheticRows(
            truth, kappa, new SeededRNG(8675), battles: 60, matchup: (_, _) => 0.0, roleBias: rho);

        GauntletFit fit = GauntletAnalysis.Analyze(rows);

        Assert.True(fit.HasRoleTerm);
        Assert.Equal(rows.Count(row => row.AAttacks), fit.AttackerBattles);
        Assert.Equal(rows.Count(row => !row.AAttacks), fit.DefenderBattles);
        Assert.InRange(fit.Role, rho - 4 * fit.SeRole, rho + 4 * fit.SeRole);
        Assert.InRange(fit.SeRole, 0.005, 0.3);
        Assert.InRange(fit.Role / fit.SeRole, 3.0, double.MaxValue);
        Assert.InRange(fit.Kappa, kappa - 4 * fit.SeKappa, kappa + 4 * fit.SeKappa);
        foreach (EntryValue value in fit.Values.Where(value => !value.IsAnchor))
        {
            Assert.InRange(value.U, truth[value.Key] - 4 * value.SeU, truth[value.Key] + 4 * value.SeU);
        }

        // One attacking model is worth exp(rho / kappa) neutral models and exp(2 rho / kappa) defenders.
        double expected = System.Math.Exp(rho / kappa);
        Assert.InRange(expected, fit.RoleFactorLow, fit.RoleFactorHigh);
        Assert.InRange(System.Math.Exp(2 * rho / kappa), fit.AttackerOverDefenderLow, fit.AttackerOverDefenderHigh);
        Assert.Equal(fit.RoleFactor * fit.RoleFactor, fit.AttackerOverDefenderFactor, 9);
        Assert.True(fit.PValue > 0.01, $"p = {fit.PValue}");
    }

    [Fact]
    public void GlobalFit_RoleTerm_WithNoBias_IsConsistentWithZero()
    {
        Dictionary<string, double> truth = new() { ["pdf"] = 0.0, ["hyb"] = 0.8, ["tac"] = 2.1 };
        List<BattleRow> rows = SyntheticRows(
            truth, 2.5, new SeededRNG(31337), battles: 60, matchup: (_, _) => 0.0, roleBias: 0.0);

        GauntletFit fit = GauntletAnalysis.Analyze(rows);

        Assert.True(fit.HasRoleTerm);
        Assert.InRange(fit.Role, -4 * fit.SeRole, 4 * fit.SeRole);
        Assert.InRange(1.0, fit.RoleFactorLow, fit.RoleFactorHigh);
    }

    [Fact]
    public void GlobalFit_WithOneRoleOnly_HasNoRoleTerm()
    {
        Dictionary<string, double> truth = new() { ["pdf"] = 0.0, ["hyb"] = 0.8 };
        List<BattleRow> rows = SyntheticRows(
                truth, 2.5, new SeededRNG(4), battles: 30, matchup: (_, _) => 0.0)
            .Select(row => row with { ARole = BattleRow.Attacker })
            .ToList();

        GauntletFit fit = GauntletAnalysis.Analyze(rows);

        Assert.False(fit.HasRoleTerm);
        Assert.True(double.IsNaN(fit.Role));
        Assert.Equal(rows.Count, fit.AttackerBattles);
        Assert.Equal(0, fit.DefenderBattles);
    }

    [Fact]
    public void RoleModes_AssignTheAttackerBySeed()
    {
        Assert.All(Enumerable.Range(1, 10), seed => Assert.True(RoleMode.AAttacks.AAttacksOn(seed)));
        Assert.All(Enumerable.Range(1, 10), seed => Assert.False(RoleMode.BAttacks.AAttacksOn(seed)));
        Assert.True(RoleMode.Alternate.AAttacksOn(1));
        Assert.False(RoleMode.Alternate.AAttacksOn(2));
        Assert.Equal(8, Enumerable.Range(1, 16).Count(seed => RoleMode.Alternate.AAttacksOn(seed)));
    }

    [Fact]
    public void GlobalFit_DetectsRockPaperScissors()
    {
        // Each of three entries beats the next by a factor of e^1 per model, which no single value per
        // entry can reproduce.
        Dictionary<string, double> truth = new() { ["pdf"] = 0.0, ["hyb"] = 0.0, ["tac"] = 0.0 };
        Dictionary<(string, string), double> cycle = new()
        {
            [("pdf", "hyb")] = 1.0, [("hyb", "tac")] = 1.0, [("pdf", "tac")] = -1.0
        };
        List<BattleRow> rows = SyntheticRows(
            truth, 2.5, new SeededRNG(99), battles: 40, matchup: (a, b) => cycle[(a, b)]);

        GauntletFit fit = GauntletAnalysis.Analyze(rows);

        Assert.True(fit.PValue < 1e-6, $"p = {fit.PValue}");
        Assert.True(fit.DevianceDifference > 50);
        Assert.Equal(3, fit.Cells.Count);
        Assert.All(fit.Cells, cell => Assert.Equal("ok", cell.Status));
        // Per-cell break-evens recover the cycle: each cell's ratio is e or 1/e.
        Assert.InRange(fit.Cells.First(c => c.EntryA == "pdf" && c.EntryB == "hyb").Ratio, 2.4, 3.1);
        Assert.InRange(fit.Cells.First(c => c.EntryA == "pdf" && c.EntryB == "tac").Ratio, 0.32, 0.42);
    }

    [Fact]
    public void OneSidedData_IsFlaggedAndDoesNotThrow()
    {
        List<BattleRow> rows = [];
        foreach (int modelsB in new[] { 5, 10, 20, 40 })
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                rows.Add(Row("a", "b", 20, modelsB, seed, 1.0));
            }
        }

        CellFit cell = GauntletAnalysis.FitCell("a", "b", rows);

        Assert.Equal("separated", cell.Status);
        Assert.True(cell.Fit.Separated);
    }

    [Fact]
    public void SingleSize_IsReportedAsInsufficient()
    {
        List<BattleRow> rows = Enumerable.Range(1, 8).Select(seed => Row("a", "b", 20, 20, seed, seed % 2)).ToList();

        CellFit cell = GauntletAnalysis.FitCell("a", "b", rows);

        Assert.Equal("insufficient", cell.Status);
        Assert.Equal(1, cell.Parameters);
    }

    [Fact]
    public void BattleRow_RoundTripsThroughTheTsvLine()
    {
        BattleRow row = new("k|1", "pdf", "hyb", "34,n=4", "26,n=4", 2, 3, 10, 15, 7,
            0.5, 0.25, 0.125, 31, "Rout", "AB12", "CD34", 200, "Aggressive");

        Assert.True(BattleRow.TryParse(row.ToLine(), out BattleRow parsed));
        Assert.Equal(row, parsed);
        Assert.True(parsed.AAttacks);
        Assert.False(BattleRow.TryParse(BattleRow.Header, out _));

        BattleRow defending = row with { ARole = BattleRow.Defender };
        Assert.True(BattleRow.TryParse(defending.ToLine(), out BattleRow parsedDefending));
        Assert.Equal(defending, parsedDefending);
        Assert.False(parsedDefending.AAttacks);
        Assert.EndsWith("\tDefender", defending.ToLine());
    }

    [Fact]
    public void BattleRow_WithoutTheRoleColumn_ParsesAsAAttacking()
    {
        BattleRow row = new("k|1", "pdf", "hyb", "34,n=4", "26,n=4", 2, 3, 10, 15, 7,
            0.5, 0.25, 0.125, 31, "Rout", "AB12", "CD34", 200, "Aggressive");
        string legacy = string.Join('\t', row.ToLine().Split('\t').Take(BattleRow.LegacyColumnCount));

        Assert.True(BattleRow.TryParse(legacy, out BattleRow parsed));
        Assert.True(parsed.AAttacks);
        Assert.Equal(row, parsed);
        Assert.False(BattleRow.TryParse(legacy + "\tNonsense", out _));
    }

    private static IEnumerable<double> Grid(double from, double to, int count) =>
        Enumerable.Range(0, count).Select(i => from + (to - from) * i / (count - 1));

    /// <summary>The mean score of simulated battles, each a win with probability p.</summary>
    private static double Simulate(SeededRNG random, double p, int battles) =>
        Enumerable.Range(0, battles).Count(_ => random.GetLinearDouble() < p) / (double)battles;

    private static BattleRow Row(
        string a, string b, int modelsA, int modelsB, int seed, double score, bool aAttacks = true) =>
        new($"{a}|{b}|{modelsA}|{modelsB}|{seed}", a, b, a, b, 1, 1, modelsA, modelsB, seed,
            score, 0.5, 0.5, 10, "Test", "db", "code", 200, "Aggressive",
            aAttacks ? BattleRow.Attacker : BattleRow.Defender);

    /// <summary>
    /// Battles drawn from score = Phi(kappa (u_A - u_B + matchup(A, B) - x)) for every pair of entries in
    /// declaration order, at force ratios from a quarter to four times the break-even.
    /// </summary>
    private static List<BattleRow> SyntheticRows(
        Dictionary<string, double> utilities,
        double kappa,
        SeededRNG random,
        int battles,
        Func<string, string, double> matchup,
        double roleBias = 0.0)
    {
        // roleBias is rho: the probit-scale shift when A attacks (+) or defends (-). Odd seeds have A
        // attack, as the gauntlet's "alternate" mode does.
        List<string> keys = utilities.Keys.ToList();
        List<BattleRow> rows = [];
        for (int i = 0; i < keys.Count; i++)
        {
            for (int j = i + 1; j < keys.Count; j++)
            {
                double breakEven = utilities[keys[i]] - utilities[keys[j]] + matchup(keys[i], keys[j]);
                foreach (double multiplier in new[] { 0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 4.0 })
                {
                    int modelsB = (int)System.Math.Round(30 * System.Math.Exp(breakEven) * multiplier);
                    double x = System.Math.Log(modelsB / 30.0);
                    for (int seed = 1; seed <= battles; seed++)
                    {
                        bool aAttacks = seed % 2 != 0;
                        double p = ProbitMath.NormalCdf(kappa * (breakEven - x) + (aAttacks ? roleBias : -roleBias));
                        rows.Add(Row(keys[i], keys[j], 30, modelsB, seed, random.GetLinearDouble() < p ? 1.0 : 0.0, aAttacks));
                    }
                }
            }
        }
        return rows;
    }
}
