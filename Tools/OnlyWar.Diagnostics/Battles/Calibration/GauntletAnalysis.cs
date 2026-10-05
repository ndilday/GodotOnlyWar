using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OnlyWar.Diagnostics.Battles.Calibration;

/// <summary>One battle of a gauntlet, as stored in battles-TAG.tsv.</summary>
internal sealed record BattleRow(
    string Key, string EntryA, string EntryB, string SpecA, string SpecB,
    int A, int B, int ModelsA, int ModelsB, int Seed,
    double Score, double ALeft, double BLeft, int Turns, string EndReason,
    string Db, string Code, int Range, string Aggression, string ARole = BattleRow.Attacker)
{
    /// <summary>Columns of a current row; rows written before the role column have <see cref="LegacyColumnCount"/>.</summary>
    public const int ColumnCount = 20;

    public const int LegacyColumnCount = 19;

    /// <summary>The <see cref="ARole"/> of a battle in which side A was the battle's Attacker.</summary>
    public const string Attacker = "Attacker";

    /// <summary>The <see cref="ARole"/> of a battle in which side A was the battle's Defender.</summary>
    public const string Defender = "Defender";

    public static string Header => string.Join('\t',
        "key", "entryA", "entryB", "specA", "specB", "a", "b", "modelsA", "modelsB", "seed",
        "score", "aLeftFraction", "bLeftFraction", "turns", "endReason", "dbHash", "codeHash", "range", "aggression",
        "aRole");

    public string ToLine() => string.Join('\t',
        Key, EntryA, EntryB, SpecA, SpecB, I(A), I(B), I(ModelsA), I(ModelsB), I(Seed),
        N(Score), N(ALeft), N(BLeft), I(Turns), EndReason, Db, Code, I(Range), Aggression, ARole);

    /// <summary>True when side A was the battle's Attacker. Rows from before the role column all were.</summary>
    public bool AAttacks => ARole != Defender;

    public static bool TryParse(string line, out BattleRow row)
    {
        row = null;
        string[] p = line.Split('\t');
        if ((p.Length != ColumnCount && p.Length != LegacyColumnCount) || p[0] == "key")
        {
            return false;
        }
        string role = p.Length == ColumnCount ? p[19] : Attacker;
        if (role != Attacker && role != Defender)
        {
            return false;
        }
        try
        {
            row = new BattleRow(
                p[0], p[1], p[2], p[3], p[4],
                int.Parse(p[5], CultureInfo.InvariantCulture), int.Parse(p[6], CultureInfo.InvariantCulture),
                int.Parse(p[7], CultureInfo.InvariantCulture), int.Parse(p[8], CultureInfo.InvariantCulture),
                int.Parse(p[9], CultureInfo.InvariantCulture),
                double.Parse(p[10], CultureInfo.InvariantCulture), double.Parse(p[11], CultureInfo.InvariantCulture),
                double.Parse(p[12], CultureInfo.InvariantCulture), int.Parse(p[13], CultureInfo.InvariantCulture),
                p[14], p[15], p[16], int.Parse(p[17], CultureInfo.InvariantCulture), p[18], role);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Log of B models per A model in this battle.</summary>
    public double X => System.Math.Log((double)ModelsB / ModelsA);

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>The probit of one ordered pair of entries: score ~ Phi(alpha + beta x), x = ln(modelsB / modelsA).</summary>
internal sealed class CellFit
{
    public string EntryA { get; init; }
    public string EntryB { get; init; }
    public int Battles { get; init; }
    public List<CellPoint> Points { get; init; }
    public double MeanScore { get; init; }

    /// <summary>"ok", "separated" (one-sided data), "extrapolated" (break-even outside the sampled sizes) or "insufficient".</summary>
    public string Status { get; set; }

    public ProbitFitResult Fit { get; init; }
    public double Alpha => Fit == null ? double.NaN : Fit.Coefficients[0];
    public double Beta => Fit == null ? double.NaN : Fit.Coefficients[1];
    public double SeAlpha => Fit == null ? double.NaN : Fit.StandardErrors[0];
    public double SeBeta => Fit == null ? double.NaN : Fit.StandardErrors[1];

    /// <summary>Log of the break-even B models per A model, -alpha / beta, and its delta-method standard error.</summary>
    public double X0 { get; init; } = double.NaN;
    public double SeX0 { get; init; } = double.NaN;
    public double Ratio => System.Math.Exp(X0);
    public double RatioLow => System.Math.Exp(X0 - 1.96 * SeX0);
    public double RatioHigh => System.Math.Exp(X0 + 1.96 * SeX0);

    public double MinX => Points.Min(p => p.X);
    public double MaxX => Points.Max(p => p.X);

    /// <summary>The deviance this cell contributes to the "every cell its own line" model.</summary>
    public double Deviance { get; init; }

    /// <summary>Parameters the per-cell model spends on this cell: two, or one when only one size was fought.</summary>
    public int Parameters { get; init; }

    /// <summary>Break-even B per A predicted by the global model, exp(u_A - u_B).</summary>
    public double GlobalRatio { get; set; } = double.NaN;
    public double ObservedOverGlobal => Ratio / GlobalRatio;
}

/// <summary>Battles of one cell at one pair of model counts.</summary>
internal sealed record CellPoint(int ModelsA, int ModelsB, int Battles, double MeanScore)
{
    public double X => System.Math.Log((double)ModelsB / ModelsA);
}

internal sealed class EntryValue
{
    public string Key { get; init; }
    public bool IsAnchor { get; init; }
    public double U { get; init; }
    public double SeU { get; init; }

    /// <summary>Per-model value, with the anchor entry's model worth 5.</summary>
    public double Value => 5.0 * System.Math.Exp(U);
    public double ValueLow => 5.0 * System.Math.Exp(U - 1.96 * SeU);
    public double ValueHigh => 5.0 * System.Math.Exp(U + 1.96 * SeU);
}

/// <summary>Everything the gauntlet fit reports.</summary>
internal sealed class GauntletFit
{
    public List<string> Entries { get; init; }
    public string Anchor { get; init; }
    public List<CellFit> Cells { get; init; }
    public ProbitFitResult Global { get; init; }
    public List<EntryValue> Values { get; init; }
    public double Kappa { get; init; }
    public double SeKappa { get; init; }

    /// <summary>
    /// True when the battles include both roles for A, so the global model carries a role term:
    /// eta = kappa (u_A - u_B - x) + rho r, with r = +1 when A is the battle's Attacker and -1 when it
    /// defends. Values and kappa are then role-neutral, the average over the two roles.
    /// </summary>
    public bool HasRoleTerm { get; init; }

    /// <summary>Battles in which A attacked, and in which A defended.</summary>
    public int AttackerBattles { get; init; }
    public int DefenderBattles { get; init; }

    /// <summary>rho, half the probit-scale gap between attacking and defending, and its standard error.</summary>
    public double Role { get; init; } = double.NaN;
    public double SeRole { get; init; } = double.NaN;

    /// <summary>
    /// What attacking is worth in models: an attacking model counts as exp(rho / kappa) role-neutral
    /// models, and as exp(2 rho / kappa) defending models. The standard error is the delta method.
    /// </summary>
    public double RoleLogFactor => Role / Kappa;
    public double SeRoleLogFactor { get; init; } = double.NaN;
    public double RoleFactor => System.Math.Exp(RoleLogFactor);
    public double RoleFactorLow => System.Math.Exp(RoleLogFactor - 1.96 * SeRoleLogFactor);
    public double RoleFactorHigh => System.Math.Exp(RoleLogFactor + 1.96 * SeRoleLogFactor);
    public double AttackerOverDefenderFactor => System.Math.Exp(2.0 * RoleLogFactor);
    public double AttackerOverDefenderLow => System.Math.Exp(2.0 * (RoleLogFactor - 1.96 * SeRoleLogFactor));
    public double AttackerOverDefenderHigh => System.Math.Exp(2.0 * (RoleLogFactor + 1.96 * SeRoleLogFactor));

    /// <summary>The strategic resolver's noise: it compares strength x exp(sigma Z) on each side.</summary>
    public double Sigma => 1.0 / (Kappa * System.Math.Sqrt(2.0));
    public double SeSigma => Sigma * SeKappa / Kappa;

    public int Battles { get; init; }
    /// <summary>
    /// Deviance of the role-free global model on the per-cell points, the one the matchup test compares
    /// with <see cref="CellDeviance"/> (the role-term model fits points split by role, which the
    /// per-cell lines do not).
    /// </summary>
    public double GlobalDeviance { get; init; }
    public double CellDeviance { get; init; }
    public double DevianceDifference => System.Math.Max(0, GlobalDeviance - CellDeviance);
    public int DegreesOfFreedom { get; init; }
    public double PValue { get; init; } = double.NaN;
    public double GlobalDispersion { get; init; } = double.NaN;
}

/// <summary>Turns stored gauntlet battles into probit fits and the report files.</summary>
internal static class GauntletAnalysis
{
    public static GauntletFit Analyze(IReadOnlyList<BattleRow> rows, IReadOnlyList<string> preferredOrder = null)
    {
        Dictionary<string, string> specs = [];
        void Register(string key, string spec)
        {
            if (specs.TryGetValue(key, out string known) && known != spec)
            {
                throw new InvalidOperationException(
                    $"Entry '{key}' has two specs ('{known}' and '{spec}') in the battles; use distinct entry keys.");
            }
            specs[key] = spec;
        }
        foreach (BattleRow row in rows)
        {
            Register(row.EntryA, row.SpecA);
            Register(row.EntryB, row.SpecB);
        }

        List<string> entries = (preferredOrder ?? [])
            .Where(specs.ContainsKey).Distinct()
            .Concat(specs.Keys.Except(preferredOrder ?? []).OrderBy(key => key, StringComparer.Ordinal))
            .ToList();
        string anchor = entries.Contains("pdf") ? "pdf" : entries[0];

        List<CellFit> cells = rows
            .GroupBy(row => (row.EntryA, row.EntryB))
            .OrderBy(group => entries.IndexOf(group.Key.EntryA))
            .ThenBy(group => entries.IndexOf(group.Key.EntryB))
            .Select(group => FitCell(group.Key.EntryA, group.Key.EntryB, group.ToList()))
            .ToList();

        // Global model: eta = w_A - w_B - kappa x, where w = kappa u and the anchor has u = 0.
        // It is linear in (w for every non-anchor entry, kappa), so it is one more probit fit.
        List<string> free = entries.Where(entry => entry != anchor).ToList();
        int kappaIndex = free.Count;
        int parameters = free.Count + 1;
        double[] EntryRow(string entryA, string entryB, double x, int width)
        {
            double[] row = new double[width];
            int a = free.IndexOf(entryA);
            int b = free.IndexOf(entryB);
            if (a >= 0) row[a] += 1;
            if (b >= 0) row[b] -= 1;
            row[kappaIndex] = -x;
            return row;
        }

        // The role-free model, on the points of each cell. The matchup test below compares it with the
        // per-cell lines, which are fitted to the same points.
        List<FitPoint> globalPoints = [];
        foreach (CellFit cell in cells)
        {
            foreach (CellPoint point in cell.Points)
            {
                globalPoints.Add(new FitPoint(EntryRow(cell.EntryA, cell.EntryB, point.X, parameters), point.MeanScore, point.Battles));
            }
        }
        ProbitFitResult plain = ProbitFit.Fit(globalPoints, parameters);

        // With both roles in the data, the same model plus one coefficient rho for "A is the attacker"
        // (r = +1 when it is, -1 when it defends), on points split by role. Values and kappa are then
        // the role-neutral ones.
        int attackerBattles = rows.Count(row => row.AAttacks);
        int defenderBattles = rows.Count - attackerBattles;
        bool hasRole = attackerBattles > 0 && defenderBattles > 0;
        ProbitFitResult global = plain;
        int roleIndex = parameters;
        if (hasRole)
        {
            int roleParameters = parameters + 1;
            List<FitPoint> rolePoints = rows
                .GroupBy(row => (row.EntryA, row.EntryB, row.ModelsA, row.ModelsB, row.AAttacks))
                .Select(group =>
                {
                    double[] design = EntryRow(group.Key.EntryA, group.Key.EntryB, group.First().X, roleParameters);
                    design[roleIndex] = group.Key.AAttacks ? 1.0 : -1.0;
                    return new FitPoint(design, group.Average(row => row.Score), group.Count());
                })
                .ToList();
            global = ProbitFit.Fit(rolePoints, roleParameters);
        }
        double kappa = global.Coefficients[kappaIndex];
        double seKappa = global.StandardErrors[kappaIndex];
        double Utility(string entry) => entry == anchor ? 0.0 : global.Coefficients[free.IndexOf(entry)] / kappa;

        List<EntryValue> values = [];
        foreach (string entry in entries)
        {
            double se = 0;
            if (entry != anchor)
            {
                int i = free.IndexOf(entry);
                double w = global.Coefficients[i];
                double u = w / kappa;
                double variance = global.Covariance == null
                    ? double.NaN
                    : (global.Covariance[i, i] + u * u * global.Covariance[kappaIndex, kappaIndex]
                        - 2 * u * global.Covariance[i, kappaIndex]) / (kappa * kappa);
                se = System.Math.Sqrt(variance);
            }
            values.Add(new EntryValue { Key = entry, IsAnchor = entry == anchor, U = Utility(entry), SeU = se });
        }

        double role = double.NaN;
        double seRole = double.NaN;
        double seRoleLog = double.NaN;
        if (hasRole)
        {
            role = global.Coefficients[roleIndex];
            seRole = global.StandardErrors[roleIndex];
            if (global.Covariance != null)
            {
                // rho / kappa, by the delta method.
                double ratio = role / kappa;
                double variance = (global.Covariance[roleIndex, roleIndex]
                    + ratio * ratio * global.Covariance[kappaIndex, kappaIndex]
                    - 2 * ratio * global.Covariance[roleIndex, kappaIndex]) / (kappa * kappa);
                seRoleLog = System.Math.Sqrt(System.Math.Max(0, variance));
            }
        }

        foreach (CellFit cell in cells)
        {
            cell.GlobalRatio = System.Math.Exp(Utility(cell.EntryA) - Utility(cell.EntryB));
        }

        double cellDeviance = cells.Sum(cell => cell.Deviance);
        int dof = cells.Sum(cell => cell.Parameters) - parameters;
        double difference = System.Math.Max(0, plain.Deviance - cellDeviance);
        double points = globalPoints.Count;
        return new GauntletFit
        {
            Entries = entries,
            Anchor = anchor,
            Cells = cells,
            Global = global,
            Values = values,
            Kappa = kappa,
            SeKappa = seKappa,
            HasRoleTerm = hasRole,
            AttackerBattles = attackerBattles,
            DefenderBattles = defenderBattles,
            Role = role,
            SeRole = seRole,
            SeRoleLogFactor = seRoleLog,
            Battles = rows.Count,
            GlobalDeviance = plain.Deviance,
            CellDeviance = cellDeviance,
            DegreesOfFreedom = dof,
            PValue = dof > 0 ? ProbitMath.ChiSquareSurvival(difference, dof) : double.NaN,
            GlobalDispersion = points > parameters ? plain.PearsonChiSquare / (points - parameters) : double.NaN
        };
    }

    /// <summary>Fits one cell's probit to its battles, grouped by the model counts they were fought at.</summary>
    internal static CellFit FitCell(string entryA, string entryB, IReadOnlyList<BattleRow> rows)
    {
        List<CellPoint> points = rows
            .GroupBy(row => (row.ModelsA, row.ModelsB))
            .OrderBy(group => group.Key.ModelsB / (double)group.Key.ModelsA)
            .Select(group => new CellPoint(group.Key.ModelsA, group.Key.ModelsB, group.Count(), group.Average(row => row.Score)))
            .ToList();
        double mean = rows.Average(row => row.Score);

        if (points.Count < 2)
        {
            return new CellFit
            {
                EntryA = entryA, EntryB = entryB, Battles = rows.Count, Points = points, MeanScore = mean,
                Status = "insufficient", Deviance = 0, Parameters = 1
            };
        }

        List<FitPoint> fitPoints = points
            .Select(point => new FitPoint([1.0, point.X], point.MeanScore, point.Battles))
            .ToList();
        ProbitFitResult fit = ProbitFit.Fit(fitPoints, 2);
        double alpha = fit.Coefficients[0];
        double beta = fit.Coefficients[1];
        double x0 = double.NaN;
        double seX0 = double.NaN;
        if (beta != 0 && fit.Covariance != null)
        {
            x0 = -alpha / beta;
            double variance = (fit.Covariance[0, 0] + x0 * x0 * fit.Covariance[1, 1] + 2 * x0 * fit.Covariance[0, 1]) / (beta * beta);
            seX0 = System.Math.Sqrt(System.Math.Max(0, variance));
        }

        string status = fit.Separated ? "separated" : beta >= 0 ? "wrong_slope" : "ok";
        CellFit cell = new()
        {
            EntryA = entryA, EntryB = entryB, Battles = rows.Count, Points = points, MeanScore = mean,
            Fit = fit, X0 = x0, SeX0 = seX0, Deviance = fit.Deviance, Parameters = 2, Status = status
        };
        if (status == "ok" && (x0 < cell.MinX || x0 > cell.MaxX))
        {
            cell.Status = "extrapolated";
        }
        return cell;
    }

    // ----- Reading and selecting stored battles -----

    /// <summary>Reads every row of one file, or of every battles-*.tsv in a folder, dropping repeated keys.</summary>
    public static List<BattleRow> ReadRows(string path)
    {
        IEnumerable<string> files = Directory.Exists(path)
            ? Directory.GetFiles(path, "battles-*.tsv").OrderBy(file => file, StringComparer.Ordinal)
            : [path];
        Dictionary<string, BattleRow> byKey = [];
        foreach (string file in files)
        {
            using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new(stream);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (BattleRow.TryParse(line, out BattleRow row))
                {
                    byKey.TryAdd(row.Key, row);
                }
            }
        }
        return byKey.Values.ToList();
    }

    /// <summary>
    /// Keeps the battles of one rules database and code version (GAUNTLET_DB and GAUNTLET_CODE pick
    /// them by prefix; the default is the most common pair), and then of one range and aggression
    /// (GAUNTLET_RANGE and GAUNTLET_AGGR, else the most common). Returns a note on what was dropped.
    /// </summary>
    public static (List<BattleRow> Rows, string Note) SelectDataset(List<BattleRow> all)
    {
        StringBuilder note = new();
        string dbFilter = Environment.GetEnvironmentVariable("GAUNTLET_DB");
        string codeFilter = Environment.GetEnvironmentVariable("GAUNTLET_CODE");
        IEnumerable<BattleRow> chosen = all;
        if (!string.IsNullOrWhiteSpace(dbFilter))
        {
            chosen = chosen.Where(row => row.Db.StartsWith(dbFilter, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(codeFilter))
        {
            chosen = chosen.Where(row => row.Code.StartsWith(codeFilter, StringComparison.OrdinalIgnoreCase));
        }
        List<BattleRow> filtered = chosen.ToList();
        if (filtered.Count == 0)
        {
            return (filtered, "No battles match the GAUNTLET_DB / GAUNTLET_CODE filters.");
        }

        var versions = filtered.GroupBy(row => (row.Db, row.Code))
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key.Db).ThenBy(group => group.Key.Code).ToList();
        var best = versions[0];
        if (versions.Count > 1)
        {
            note.AppendLine($"{versions.Count} rules-database/code versions found; using the most common ({best.Key.Db}, {best.Key.Code}, {best.Count()} battles).");
        }
        filtered = best.ToList();

        string rangeText = Environment.GetEnvironmentVariable("GAUNTLET_RANGE");
        string aggrText = Environment.GetEnvironmentVariable("GAUNTLET_AGGR");
        if (int.TryParse(rangeText, out int range))
        {
            filtered = filtered.Where(row => row.Range == range).ToList();
        }
        if (!string.IsNullOrWhiteSpace(aggrText))
        {
            filtered = filtered.Where(row => string.Equals(row.Aggression, aggrText, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        var conditions = filtered.GroupBy(row => (row.Range, row.Aggression))
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key.Range).ThenBy(group => group.Key.Aggression).ToList();
        if (conditions.Count > 1)
        {
            note.AppendLine($"{conditions.Count} range/aggression combinations found; using the most common ({conditions[0].Key.Range} yd, {conditions[0].Key.Aggression}). Set GAUNTLET_RANGE / GAUNTLET_AGGR to choose.");
            filtered = conditions[0].ToList();
        }
        return (filtered, note.ToString());
    }

    // ----- Report -----

    public static void WriteReport(GauntletFit fit, string directory, string tag, string header)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"fit-{tag}.txt"), Describe(fit, header));

        StringBuilder values = new();
        values.AppendLine(string.Join('\t', "entry", "anchor", "u", "se_u", "value", "value_lo", "value_hi"));
        foreach (EntryValue value in fit.Values)
        {
            values.AppendLine(string.Join('\t', value.Key, value.IsAnchor ? "1" : "0", F(value.U), F(value.SeU),
                F(value.Value), F(value.ValueLow), F(value.ValueHigh)));
        }
        File.WriteAllText(Path.Combine(directory, $"values-{tag}.tsv"), values.ToString());

        StringBuilder cells = new();
        cells.AppendLine(string.Join('\t', "entryA", "entryB", "battles", "points", "min_ratio", "max_ratio", "mean_score",
            "status", "alpha", "se_alpha", "beta", "se_beta", "breakeven_ratio", "ratio_lo", "ratio_hi",
            "global_ratio", "observed_over_global", "deviance"));
        foreach (CellFit cell in fit.Cells)
        {
            cells.AppendLine(string.Join('\t', cell.EntryA, cell.EntryB, cell.Battles, cell.Points.Count,
                F(System.Math.Exp(cell.MinX)), F(System.Math.Exp(cell.MaxX)), F(cell.MeanScore), cell.Status,
                F(cell.Alpha), F(cell.SeAlpha), F(cell.Beta), F(cell.SeBeta),
                F(cell.Ratio), F(cell.RatioLow), F(cell.RatioHigh),
                F(cell.GlobalRatio), F(cell.ObservedOverGlobal), F(cell.Deviance)));
        }
        File.WriteAllText(Path.Combine(directory, $"cells-{tag}.tsv"), cells.ToString());
    }

    public static string Describe(GauntletFit fit, string header)
    {
        StringBuilder text = new();
        text.AppendLine(header.TrimEnd());
        text.AppendLine();
        text.AppendLine($"Battles: {fit.Battles}, cells: {fit.Cells.Count}, entries: {fit.Entries.Count}. "
            + $"Anchor: {fit.Anchor} (u = 0, per-model value 5).");
        text.AppendLine("Score is 1 when A holds the field, 0 when B does, 0.5 when neither does. x = ln(models B / models A).");
        text.AppendLine();

        if (fit.HasRoleTerm)
        {
            text.AppendLine("GLOBAL MODEL  score ~ Phi(kappa * (u_A - u_B - x) + rho r), one u per entry, r = +1 when A is the battle's Attacker, -1 when it defends");
            text.AppendLine($"  battles with A attacking: {fit.AttackerBattles}, with A defending: {fit.DefenderBattles}. Values and kappa below are role-neutral (averaged over the two roles).");
        }
        else
        {
            text.AppendLine("GLOBAL MODEL  score ~ Phi(kappa * (u_A - u_B - x)), one u per entry");
            text.AppendLine(fit.AttackerBattles > 0
                ? "  Every battle had A attacking, so no role term is fitted; values carry any attacker advantage."
                : "  Every battle had A defending, so no role term is fitted; values carry any defender advantage.");
        }
        text.AppendLine($"  kappa = {F(fit.Kappa)} +/- {F(fit.SeKappa)} (1 s.e.)");
        text.AppendLine($"  strategic noise sigma = 1 / (kappa * sqrt 2) = {F(fit.Sigma)} +/- {F(fit.SeSigma)}"
            + " (the strategic resolver compares strength * exp(sigma Z) on each side)");
        if (fit.Global.Separated)
        {
            text.AppendLine("  WARNING: the global fit is not well determined (one-sided data or a disconnected entry); read the values as bounds.");
        }
        text.AppendLine("  per-model value (anchor's model = 5), 95% interval:");
        foreach (EntryValue value in fit.Values)
        {
            text.AppendLine(value.IsAnchor
                ? $"    {value.Key,-12} {F(value.Value),9}   (anchor)"
                : $"    {value.Key,-12} {F(value.Value),9}   [{F(value.ValueLow)}, {F(value.ValueHigh)}]   u = {F(value.U)} +/- {F(value.SeU)}");
        }
        if (fit.HasRoleTerm)
        {
            double z = fit.SeRole > 0 ? fit.Role / fit.SeRole : double.NaN;
            text.AppendLine($"  role: rho = {F(fit.Role)} +/- {F(fit.SeRole)} (z = {F(z)}); positive means attacking helps");
            text.AppendLine($"    one attacking model counts as exp(rho / kappa) = {F(fit.RoleFactor)} role-neutral models "
                + $"[{F(fit.RoleFactorLow)}, {F(fit.RoleFactorHigh)}]");
            text.AppendLine($"    one attacking model counts as exp(2 rho / kappa) = {F(fit.AttackerOverDefenderFactor)} defending models "
                + $"[{F(fit.AttackerOverDefenderLow)}, {F(fit.AttackerOverDefenderHigh)}]");
        }
        text.AppendLine();

        text.AppendLine("IS THERE MATCHUP (ROCK-PAPER-SCISSORS) STRUCTURE?  global model against one line per cell"
            + (fit.HasRoleTerm ? " (role-free global model; the per-cell lines average over the roles)" : ""));
        text.AppendLine($"  deviance, global model:     {F(fit.GlobalDeviance)}");
        text.AppendLine($"  deviance, per-cell lines:   {F(fit.CellDeviance)}");
        text.AppendLine($"  difference:                 {F(fit.DevianceDifference)} on {fit.DegreesOfFreedom} degrees of freedom");
        text.AppendLine($"  chi-square p-value:         {FP(fit.PValue)}");
        text.AppendLine($"  Pearson dispersion (global): {F(fit.GlobalDispersion)} (about 1 if the battles behave like coin flips; the p-value assumes 1)");
        text.AppendLine("  A small p-value means one scalar per entry does not explain the cells: matchup structure is real beyond noise.");
        int undetermined = fit.Cells.Count(cell => cell.Status is "separated" or "insufficient" or "wrong_slope");
        if (undetermined > 0)
        {
            text.AppendLine($"  CAUTION: {undetermined} cell(s) have no well-determined line of their own (separated, insufficient or wrong slope), so this test is unreliable; rerun with GAUNTLET_REFINE or a better GAUNTLET_PRIOR.");
        }
        text.AppendLine();

        text.AppendLine("PER-CELL FITS  score ~ Phi(alpha + beta x); break-even = B models per A model at score 0.5");
        text.AppendLine($"  {"A",-10} {"B",-10} {"battles",7} {"points",6} {"sizes (B per A)",-16} {"mean",5} {"alpha",8} {"beta",8} {"break-even",10} {"95% interval",-17} status");
        foreach (CellFit cell in fit.Cells)
        {
            string sizes = $"{F(System.Math.Exp(cell.MinX))}-{F(System.Math.Exp(cell.MaxX))}";
            string interval = cell.Fit == null || double.IsNaN(cell.SeX0)
                ? "n/a"
                : $"[{F(cell.RatioLow)}, {F(cell.RatioHigh)}]";
            text.AppendLine($"  {cell.EntryA,-10} {cell.EntryB,-10} {cell.Battles,7} {cell.Points.Count,6} {sizes,-16} {F(cell.MeanScore),5} "
                + $"{F(cell.Alpha),8} {F(cell.Beta),8} {F(cell.Ratio),10} {interval,-17} {cell.Status}");
        }
        text.AppendLine("  separated: one-sided data, the break-even is only a bound. extrapolated: the break-even lies outside the sizes fought.");
        text.AppendLine();

        text.AppendLine("RESIDUALS  observed per-cell break-even / break-even the global model predicts, exp(u_A - u_B); largest first");
        text.AppendLine($"  {"A",-10} {"B",-10} {"observed",9} {"global",9} {"obs/global",10}");
        foreach (CellFit cell in fit.Cells.Where(cell => !double.IsNaN(cell.ObservedOverGlobal))
                     .OrderByDescending(cell => System.Math.Abs(System.Math.Log(cell.ObservedOverGlobal))))
        {
            text.AppendLine($"  {cell.EntryA,-10} {cell.EntryB,-10} {F(cell.Ratio),9} {F(cell.GlobalRatio),9} {F(cell.ObservedOverGlobal),10}   {cell.Status}");
        }
        return text.ToString();
    }

    private static string F(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? "n/a" : value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string FP(double value) =>
        double.IsNaN(value) ? "n/a" : value < 1e-4 ? value.ToString("0.###E+0", CultureInfo.InvariantCulture) : value.ToString("0.####", CultureInfo.InvariantCulture);
}
