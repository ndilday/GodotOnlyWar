using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OnlyWar.Diagnostics.Battles.Calibration;

/// <summary>
/// One battle of a gauntlet, as stored in battles-TAG.tsv. <see cref="Range"/> is the configured range:
/// yards, or <see cref="SquadDuelDiagnostics.AssaultRange"/> (0) for the assault opening, in which case
/// <see cref="OpeningRange"/> is the range that battle actually opened at.
/// </summary>
internal sealed record BattleRow(
    string Key, string EntryA, string EntryB, string SpecA, string SpecB,
    int A, int B, int ModelsA, int ModelsB, int Seed,
    double Score, double ALeft, double BLeft, int Turns, string EndReason,
    string Db, string Code, int Range, string Aggression, string ARole = BattleRow.Attacker,
    int OpeningRange = -1)
{
    /// <summary>
    /// Columns of a current row. Rows written before the opening-range column have
    /// <see cref="RoleColumnCount"/>, and rows written before the role column <see cref="LegacyColumnCount"/>;
    /// both opened at their configured range.
    /// </summary>
    public const int ColumnCount = 21;

    public const int RoleColumnCount = 20;

    public const int LegacyColumnCount = 19;

    /// <summary>The <see cref="ARole"/> of a battle in which side A was the battle's Attacker.</summary>
    public const string Attacker = "Attacker";

    /// <summary>The <see cref="ARole"/> of a battle in which side A was the battle's Defender.</summary>
    public const string Defender = "Defender";

    public static string Header => string.Join('\t',
        "key", "entryA", "entryB", "specA", "specB", "a", "b", "modelsA", "modelsB", "seed",
        "score", "aLeftFraction", "bLeftFraction", "turns", "endReason", "dbHash", "codeHash", "range", "aggression",
        "aRole", "openingRange");

    public string ToLine() => string.Join('\t',
        Key, EntryA, EntryB, SpecA, SpecB, I(A), I(B), I(ModelsA), I(ModelsB), I(Seed),
        N(Score), N(ALeft), N(BLeft), I(Turns), EndReason, Db, Code, I(Range), Aggression, ARole, I(OpeningRange));

    /// <summary>True when side A was the battle's Attacker. Rows from before the role column all were.</summary>
    public bool AAttacks => ARole != Defender;

    /// <summary>The end reason of a battle the harness stopped at its turn limit.</summary>
    public const string TurnCap = "TurnCap";

    // Casualty fraction added to both sides of a turn-cap score, so a battle in which little
    // happened scores near 0.5 instead of swinging on one casualty.
    internal const double TurnCapShrink = 0.05;

    /// <summary>
    /// The score of a battle: 1 when A holds the field, 0 when B does, and for a battle the turn
    /// limit stopped, A's share of the casualties by fraction of each side lost (2026-10-25,
    /// Design/Active/GauntletV2.md). Half the assault battles of the v2 Ork run ended at the limit,
    /// most with one side far ahead (Mega Nobz untouched against a Shoota mob 55% down); counted as
    /// 0.5 they told the fit nothing. The side that has lost less of itself is the one a longer
    /// battle would break last.
    /// </summary>
    public static double ScoreOf(string endReason, double holderScore, double aLeft, double bLeft)
    {
        if (endReason != TurnCap)
        {
            return holderScore;
        }
        double aLost = System.Math.Clamp(1 - aLeft, 0, 1);
        double bLost = System.Math.Clamp(1 - bLeft, 0, 1);
        return (bLost + TurnCapShrink) / (aLost + bLost + (2 * TurnCapShrink));
    }

    public static bool TryParse(string line, out BattleRow row)
    {
        row = null;
        string[] p = line.Split('\t');
        if ((p.Length != ColumnCount && p.Length != RoleColumnCount && p.Length != LegacyColumnCount) || p[0] == "key")
        {
            return false;
        }
        string role = p.Length >= RoleColumnCount ? p[19] : Attacker;
        if (role != Attacker && role != Defender)
        {
            return false;
        }
        try
        {
            int range = int.Parse(p[17], CultureInfo.InvariantCulture);
            double aLeft = double.Parse(p[11], CultureInfo.InvariantCulture);
            double bLeft = double.Parse(p[12], CultureInfo.InvariantCulture);
            // Stored turn-cap rows say 0.5; their score is rebuilt from the casualties.
            double score = ScoreOf(p[14], double.Parse(p[10], CultureInfo.InvariantCulture), aLeft, bLeft);
            row = new BattleRow(
                p[0], p[1], p[2], p[3], p[4],
                int.Parse(p[5], CultureInfo.InvariantCulture), int.Parse(p[6], CultureInfo.InvariantCulture),
                int.Parse(p[7], CultureInfo.InvariantCulture), int.Parse(p[8], CultureInfo.InvariantCulture),
                int.Parse(p[9], CultureInfo.InvariantCulture),
                score, aLeft, bLeft, int.Parse(p[13], CultureInfo.InvariantCulture),
                p[14], p[15], p[16], range, p[18], role,
                p.Length == ColumnCount ? int.Parse(p[20], CultureInfo.InvariantCulture) : range);
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

    /// <summary>
    /// "ok", "bracketed" (separated data with the break-even between two fought sizes: X0 is the gap's
    /// midpoint), "separated" (one-sided data), "extrapolated" (break-even outside the sampled sizes),
    /// "wrong_slope" or "insufficient".
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// What a cell with no usable break-even still says (2026-10-25): the break-even lies above
    /// <see cref="LowerX"/> (A held the field at every size up to there) or below <see cref="UpperX"/>
    /// (A lost at every size down to there). NaN when there is no such bound.
    /// </summary>
    public double LowerX { get; init; } = double.NaN;
    public double UpperX { get; init; } = double.NaN;
    public bool IsBound => double.IsFinite(LowerX) || double.IsFinite(UpperX);

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

/// <summary>
/// One value per entry on a single scale, from every well-determined cell at once (Design/Active/
/// GauntletV2.md section 1): ln b(A,B) = u(A) - u(B) + e(A,B), minimizing the sum of w e^2 with the
/// anchor fixed at u = 0 (a per-model value of 5). b is the cell's break-even B models per A model, so
/// a stronger A per model has the larger b.
///
/// <para>WEIGHTS: w = 1 / (se(ln b)^2 + tau^2). tau is the matchup standard deviation, the spread of
/// the residuals that a cell's own sampling noise does not explain, estimated so that the sum of
/// w e^2 equals its degrees of freedom (Paule-Mandel). With precision weights alone (tau = 0) the
/// refit of all the *9 cells had a dispersion of 168: the matchups, not the battles, set the spread,
/// and a few cells with very steep probits outweighed the rest and pulled Tactical to 55 against 93.6
/// from its own cells. With tau in the weights, cells count about equally when matchups dominate,
/// and by their precision when they agree.</para>
/// </summary>
internal sealed class LeastSquaresFit
{
    public string Anchor { get; init; }

    /// <summary>Cells used: status "ok" with a finite, positive standard error.</summary>
    public List<CellFit> Used { get; init; }

    /// <summary>
    /// Bound cells (<see cref="CellFit.IsBound"/>) whose bound the values would otherwise break, held at
    /// their bound in the final solve.
    /// </summary>
    public List<CellFit> ActiveBounds { get; init; } = [];

    /// <summary>Bound cells between two entries on the scale, active or not.</summary>
    public int Bounds { get; init; }

    /// <summary>Entries with a value: connected to the anchor through used cells.</summary>
    public List<EntryValue> Values { get; init; }

    /// <summary>Entries in the data that no chain of used cells connects to the anchor.</summary>
    public List<string> Unconnected { get; init; }

    /// <summary>Residual e(A,B) of each used cell, in ln units.</summary>
    public Dictionary<(string A, string B), double> Residuals { get; init; }

    /// <summary>The matchup standard deviation tau, in ln units (0 when the cells agree within their noise).</summary>
    public double Tau { get; init; }

    /// <summary>Sum of e^2 / se^2 over the used cells (precision weights alone), its degrees of freedom, and their ratio.</summary>
    public double ChiSquare { get; init; }
    public int DegreesOfFreedom { get; init; }
    public double Dispersion => DegreesOfFreedom > 0 ? ChiSquare / DegreesOfFreedom : double.NaN;

    /// <summary>
    /// The spread the fit minimizes, as a weighted standard deviation of the residuals in ln units:
    /// sqrt(sum w e^2 / sum w). exp of it is the typical factor by which one value per squad misses a
    /// matchup.
    /// </summary>
    public double ResidualSd { get; init; }
}

/// <summary>Everything the gauntlet fit reports.</summary>
internal sealed class GauntletFit
{
    /// <summary>The single-scale least-squares fit over all well-determined cells.</summary>
    public LeastSquaresFit LeastSquares { get; init; }

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
    /// <summary>
    /// The standard error, in ln units, a bound carries when it is held: about the spacing of the
    /// sizes fought near it. The matchup sd tau adds to it as to every cell.
    /// </summary>
    internal const double BoundSe = 0.25;

    /// <summary>A mean score at or above this (or at or below 1 minus it) counts as one-sided.</summary>
    internal const double OneSidedScore = 0.85;

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
            LeastSquares = FitLeastSquares(cells, anchor, entries),
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

    /// <summary>
    /// The single-scale fit of <see cref="LeastSquaresFit"/>. "ok" and "bracketed" cells enter as
    /// observations. A separated or extrapolated cell's break-even is not a number but a bound
    /// (<see cref="CellFit.IsBound"/>), and it enters as one (2026-10-25): censored least squares by an
    /// active set, which holds a bound cell at its bound only while the values would otherwise break
    /// it. Until then such cells were dropped, so a squad that lost every battle against one panel
    /// squad gave the fit nothing from that matchup, and the harness fought ever larger battles to
    /// turn it into a number. Bounds do not link entries to the anchor: an entry known only through
    /// bounds has an interval, not a value, and is listed with the unconnected ones.
    /// </summary>
    internal static LeastSquaresFit FitLeastSquares(
        IReadOnlyList<CellFit> cells,
        string anchor,
        IReadOnlyList<string> entries)
    {
        List<CellFit> used = cells
            .Where(cell => (cell.Status == "ok" || cell.Status == "bracketed") && double.IsFinite(cell.X0)
                && double.IsFinite(cell.SeX0) && cell.SeX0 > 0)
            .ToList();

        // Connectivity: everything reachable from the anchor through used cells.
        Dictionary<string, List<string>> links = [];
        foreach (CellFit cell in used)
        {
            links.TryAdd(cell.EntryA, []);
            links.TryAdd(cell.EntryB, []);
            links[cell.EntryA].Add(cell.EntryB);
            links[cell.EntryB].Add(cell.EntryA);
        }
        HashSet<string> connected = [];
        if (links.ContainsKey(anchor))
        {
            Queue<string> queue = new([anchor]);
            connected.Add(anchor);
            while (queue.Count > 0)
            {
                foreach (string next in links[queue.Dequeue()].Where(connected.Add))
                {
                    queue.Enqueue(next);
                }
            }
        }
        List<string> unconnected = entries.Where(entry => !connected.Contains(entry)).ToList();
        used = used.Where(cell => connected.Contains(cell.EntryA) && connected.Contains(cell.EntryB)).ToList();
        List<string> free = entries.Where(entry => connected.Contains(entry) && entry != anchor).ToList();

        int n = free.Count;
        int dof = used.Count - n;
        List<CellFit> bounds = cells
            .Where(cell => cell.IsBound && !used.Contains(cell)
                && connected.Contains(cell.EntryA) && connected.Contains(cell.EntryB))
            .ToList();
        List<(CellFit Cell, double Target)> active = [];

        // One weighted solve at a given tau^2: values, their covariance, and the weighted sum of
        // squared residuals Q over the used cells, which falls as tau^2 rises. Active bounds enter as
        // observations at their bound.
        (double[] U, double[,] Inverse, double Q) Solve(double tau2)
        {
            double[,] normal = new double[n, n];
            double[] rhs = new double[n];
            void Add(string entryA, string entryB, double x, double w)
            {
                int a = free.IndexOf(entryA);
                int b = free.IndexOf(entryB);
                // Design row: +1 for A, -1 for B; the anchor has no column.
                if (a >= 0) { normal[a, a] += w; rhs[a] += w * x; }
                if (b >= 0) { normal[b, b] += w; rhs[b] -= w * x; }
                if (a >= 0 && b >= 0) { normal[a, b] -= w; normal[b, a] -= w; }
            }
            foreach (CellFit cell in used)
            {
                Add(cell.EntryA, cell.EntryB, cell.X0, 1.0 / (cell.SeX0 * cell.SeX0 + tau2));
            }
            foreach ((CellFit cell, double target) in active)
            {
                Add(cell.EntryA, cell.EntryB, target, 1.0 / (BoundSe * BoundSe + tau2));
            }
            double[,] inverse = n > 0 ? ProbitFit.Invert(normal) : new double[0, 0];
            double[] solution = new double[n];
            if (inverse != null)
            {
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        solution[i] += inverse[i, j] * rhs[j];
                    }
                }
            }
            double q = 0;
            foreach (CellFit cell in used)
            {
                double e = cell.X0 - (Value(solution, cell.EntryA) - Value(solution, cell.EntryB));
                q += e * e / (cell.SeX0 * cell.SeX0 + tau2);
            }
            return (solution, inverse, q);
        }
        double Value(double[] solution, string entry) => entry == anchor ? 0.0 : solution[free.IndexOf(entry)];

        // Paule-Mandel: the tau^2 at which Q equals its degrees of freedom, or 0 if the cells already
        // agree within their own noise. Q is monotone in tau^2, so bracket and bisect.
        double tau2 = 0;
        (double[] u, double[,] inverse, double q0) = Solve(0);
        double chiSquare = q0;
        if (dof > 0 && q0 > dof)
        {
            double low = 0;
            double high = 0.01;
            while (Solve(high).Q > dof && high < 100)
            {
                low = high;
                high *= 4;
            }
            for (int step = 0; step < 60; step++)
            {
                double middle = 0.5 * (low + high);
                if (Solve(middle).Q > dof) low = middle; else high = middle;
            }
            tau2 = 0.5 * (low + high);
            (u, inverse, _) = Solve(tau2);
        }

        // Bounds, at the tau the observations set: hold every bound the values break at its bound,
        // and repeat until none is broken. The set only grows, so this ends.
        for (int pass = 0; pass < bounds.Count + 1; pass++)
        {
            List<(CellFit, double)> broken = [];
            foreach (CellFit cell in bounds.Where(cell => active.All(entry => entry.Cell != cell)))
            {
                double predicted = Value(u, cell.EntryA) - Value(u, cell.EntryB);
                if (double.IsFinite(cell.LowerX) && predicted < cell.LowerX)
                {
                    broken.Add((cell, cell.LowerX));
                }
                else if (double.IsFinite(cell.UpperX) && predicted > cell.UpperX)
                {
                    broken.Add((cell, cell.UpperX));
                }
            }
            if (broken.Count == 0)
            {
                break;
            }
            active.AddRange(broken);
            (u, inverse, _) = Solve(tau2);
        }

        Dictionary<(string, string), double> residuals = [];
        double weightedSquares = 0;
        double weightSum = 0;
        foreach (CellFit cell in used)
        {
            double w = 1.0 / (cell.SeX0 * cell.SeX0 + tau2);
            double e = cell.X0 - (Value(u, cell.EntryA) - Value(u, cell.EntryB));
            residuals[(cell.EntryA, cell.EntryB)] = e;
            weightedSquares += w * e * e;
            weightSum += w;
        }

        List<EntryValue> values = [];
        foreach (string entry in entries.Where(connected.Contains))
        {
            int i = free.IndexOf(entry);
            double se = i < 0 || inverse == null ? 0 : System.Math.Sqrt(inverse[i, i]);
            values.Add(new EntryValue { Key = entry, IsAnchor = entry == anchor, U = Value(u, entry), SeU = se });
        }

        return new LeastSquaresFit
        {
            Anchor = anchor,
            Used = used,
            ActiveBounds = active.Select(entry => entry.Cell).ToList(),
            Bounds = bounds.Count,
            Values = values,
            Unconnected = unconnected,
            Residuals = residuals,
            Tau = System.Math.Sqrt(tau2),
            ChiSquare = chiSquare,
            DegreesOfFreedom = dof,
            ResidualSd = weightSum > 0 ? System.Math.Sqrt(weightedSquares / weightSum) : double.NaN
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
                Status = "insufficient", Deviance = 0, Parameters = 1,
                LowerX = points[0].MeanScore >= OneSidedScore ? points[0].X : double.NaN,
                UpperX = points[0].MeanScore <= 1 - OneSidedScore ? points[0].X : double.NaN
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
        double minX = points.Min(point => point.X);
        double maxX = points.Max(point => point.X);
        double lowerX = double.NaN;
        double upperX = double.NaN;
        if (status == "separated")
        {
            // Separated data still place the break-even. A held the field at every size: above the
            // largest B-per-A fought. A lost at every size: below the smallest. Otherwise the wins and
            // the losses are split at a gap between two sizes, and the break-even is in that gap.
            if (points.All(point => point.MeanScore >= 0.5))
            {
                lowerX = maxX;
            }
            else if (points.All(point => point.MeanScore <= 0.5))
            {
                upperX = minX;
            }
            else
            {
                double winsTo = points.Where(point => point.MeanScore > 0.5).Max(point => point.X);
                double lossesFrom = points.Where(point => point.MeanScore < 0.5).Min(point => point.X);
                if (lossesFrom > winsTo)
                {
                    status = "bracketed";
                    x0 = 0.5 * (winsTo + lossesFrom);
                    seX0 = 0.25 * (lossesFrom - winsTo);
                }
            }
        }
        else if (status == "ok" && x0 > maxX)
        {
            status = "extrapolated";
            lowerX = maxX;
        }
        else if (status == "ok" && x0 < minX)
        {
            status = "extrapolated";
            upperX = minX;
        }
        return new CellFit
        {
            EntryA = entryA, EntryB = entryB, Battles = rows.Count, Points = points, MeanScore = mean,
            Fit = fit, X0 = x0, SeX0 = seX0, Deviance = fit.Deviance, Parameters = 2, Status = status,
            LowerX = lowerX, UpperX = upperX
        };
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
        if (!string.IsNullOrWhiteSpace(rangeText))
        {
            int range = SquadDuelDiagnostics.ParseRange(rangeText);
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
            note.AppendLine($"{conditions.Count} range/aggression combinations found; using the most common ({SquadDuelDiagnostics.RangeText(conditions[0].Key.Range)}, {conditions[0].Key.Aggression}). Set GAUNTLET_RANGE / GAUNTLET_AGGR to choose.");
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

        LeastSquaresFit lsq = fit.LeastSquares;
        if (lsq != null)
        {
            StringBuilder table = new();
            table.AppendLine(string.Join('\t', "entry", "anchor", "u", "se_u", "value", "value_lo", "value_hi", "cells"));
            foreach (EntryValue value in lsq.Values)
            {
                int count = lsq.Used.Count(cell => cell.EntryA == value.Key || cell.EntryB == value.Key);
                table.AppendLine(string.Join('\t', value.Key, value.IsAnchor ? "1" : "0", F(value.U), F(value.SeU),
                    F(value.Value), F(value.ValueLow), F(value.ValueHigh), count));
            }
            foreach (string entry in lsq.Unconnected)
            {
                table.AppendLine(string.Join('\t', entry, "0", "n/a", "n/a", "n/a", "n/a", "n/a", "0"));
            }
            File.WriteAllText(Path.Combine(directory, $"lsq-{tag}.tsv"), table.ToString());
        }
    }

    public static string Describe(GauntletFit fit, string header)
    {
        StringBuilder text = new();
        text.AppendLine(header.TrimEnd());
        text.AppendLine();
        text.AppendLine($"Battles: {fit.Battles}, cells: {fit.Cells.Count}, entries: {fit.Entries.Count}. "
            + $"Anchor: {fit.Anchor} (u = 0, per-model value 5).");
        text.AppendLine("Score is 1 when A holds the field, 0 when B does, and A's share of the casualties when the turn limit stops the battle (BattleRow.ScoreOf). x = ln(models B / models A).");
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

        LeastSquaresFit lsq = fit.LeastSquares;
        if (lsq != null)
        {
            text.AppendLine("SINGLE-SCALE VALUES  least squares over every 'ok' cell: ln b(A,B) = u_A - u_B + e, weights 1 / (se(ln b)^2 + tau^2), anchor fixed");
            text.AppendLine($"  cells used: {lsq.Used.Count} of {fit.Cells.Count}; matchup sd tau = {F(lsq.Tau)}"
                + $" (beyond the battles' own noise, one value per entry misses a typical matchup by a factor of {F(System.Math.Exp(lsq.Tau))})");
            text.AppendLine($"  bound cells (one-sided or extrapolated): {lsq.Bounds}, of which {lsq.ActiveBounds.Count} held at their bound"
                + (lsq.ActiveBounds.Count > 0
                    ? ": " + string.Join(", ", lsq.ActiveBounds.Select(cell => $"{cell.EntryA}-{cell.EntryB}"))
                    : ""));
            text.AppendLine($"  precision-weighted chi-square {F(lsq.ChiSquare)} on {lsq.DegreesOfFreedom} degrees of freedom, dispersion {F(lsq.Dispersion)}"
                + $" (about 1 if one value per entry explained the cells up to their own noise); weighted sd of the residuals {F(lsq.ResidualSd)}");
            text.AppendLine("  per-model value (anchor's model = 5), 95% interval:");
            foreach (EntryValue value in lsq.Values)
            {
                int count = lsq.Used.Count(cell => cell.EntryA == value.Key || cell.EntryB == value.Key);
                text.AppendLine(value.IsAnchor
                    ? $"    {value.Key,-12} {F(value.Value),9}   (anchor)"
                    : $"    {value.Key,-12} {F(value.Value),9}   [{F(value.ValueLow)}, {F(value.ValueHigh)}]   {count} cells");
            }
            if (lsq.Unconnected.Count > 0)
            {
                text.AppendLine($"  NOT ON THE SCALE (no chain of 'ok' cells to {lsq.Anchor}): {string.Join(", ", lsq.Unconnected)}");
            }
            text.AppendLine("  largest residuals (observed break-even / single-scale prediction):");
            foreach (((string a, string b), double e) in lsq.Residuals.OrderByDescending(pair => System.Math.Abs(pair.Value)).Take(15))
            {
                text.AppendLine($"    {a,-10} {b,-10} {F(System.Math.Exp(e)),9}");
            }
            text.AppendLine();
        }

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
