using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using OnlyWar.Battles.Resolutions;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Diagnostics.Battles;

/// <summary>
/// Opt-in: how hard each target is for each attacker's weapons, measured with the engine's own hit
/// pipeline rather than a formula. For every attack an entry's squad can make (each distinct ranged
/// weapon, and each melee weapon or the species' unarmed attack, with the carrier's own Strength) and
/// every target entry, it fires hits at a fresh copy of the target's first non-leader soldier until
/// the soldier is no longer combat-effective, and reports the mean number of HITS that takes. A hit is
/// what ShootAction.HandleHit and MeleeAttackAction.HandleHit do after the to-hit roll: a hit location
/// from HitLocationCalculator, damage base x (3.5 + 1.75 Z), worn armor x the weapon's ArmorMultiplier,
/// the weapon's WoundMultiplier, then WoundResolver for wound levels, the Negligible floor, promotion,
/// crippling, severing and IsCombatEffective. To-hit chance (range, skill, target size) is NOT
/// included: the numbers are hits, not shots.
///
/// Gated by RUN_HARDNESS. Settings: HARDNESS_ENTRIES ("key=spec|..." as the ladder and gauntlet),
/// HARDNESS_TRIALS (targets disabled per cell, default 100), HARDNESS_RANGE (yards, for ranged weapons
/// whose damage falls off with range, default 30), HARDNESS_CAP (hits before a trial gives up, default
/// 20000), HARDNESS_OUT (default %TEMP%\GodotOnlyWar\hardness.txt).
///
/// Reported per entry against each target: the mean hits to disable for each of its attacks, and a
/// squad figure, the rate at which the whole squad disables that target per hit it lands
/// (sum over its attacks of carriers / hits-to-disable). Comparing a squad's figure against a soft
/// target with its figure against a hard one is the "anti-hard share" of
/// Design/Active/BattleValueCalibration.md, section 7.12.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class HardnessDiagnostics
{
    private sealed record Attack(string Kind, string Weapon, float Strength, int Carriers,
        RangedWeaponTemplate Ranged, MeleeWeaponTemplate Melee);

    [Fact]
    public void Hardness_ReportsHitsToDisable()
    {
        string entriesText = Environment.GetEnvironmentVariable("HARDNESS_ENTRIES");
        if (Environment.GetEnvironmentVariable("RUN_HARDNESS") == null || string.IsNullOrWhiteSpace(entriesText))
        {
            return;
        }

        int trials = SquadDuelDiagnostics.ReadInt("HARDNESS_TRIALS", 100);
        int range = SquadDuelDiagnostics.ReadInt("HARDNESS_RANGE", 30);
        int cap = SquadDuelDiagnostics.ReadInt("HARDNESS_CAP", 20_000);
        string output = Environment.GetEnvironmentVariable("HARDNESS_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "hardness.txt");

        List<(string Key, string Spec)> entries = entriesText
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => text.Split('=', 2))
            .Select(parts => (parts[0].Trim(), parts[1].Trim()))
            .ToList();
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();

        Dictionary<string, List<Attack>> attacks = entries.ToDictionary(e => e.Key, e => AttacksOf(blob, e.Spec));
        StringBuilder report = new();
        report.AppendLine($"Hits to disable (mean of {trials} targets; hits, not shots; ranged damage at {range} yd; cap {cap}).");
        report.AppendLine();

        Dictionary<(string Attacker, string Target), double> squadRate = [];
        foreach ((string attackerKey, _) in entries)
        {
            report.AppendLine($"== {attackerKey} ==");
            report.AppendLine("  attack                                carriers  " + string.Join("  ",
                entries.Select(t => t.Key.PadLeft(10))));
            Dictionary<string, double> rateByTarget = entries.ToDictionary(t => t.Key, _ => 0.0);
            foreach (Attack attack in attacks[attackerKey])
            {
                StringBuilder line = new($"  {attack.Kind,-6}{attack.Weapon,-30}{attack.Carriers,9}  ");
                foreach ((string targetKey, string targetSpec) in entries)
                {
                    double hits = MeanHitsToDisable(blob, targetSpec, attack, range, trials, cap);
                    line.Append(hits >= cap ? "      >cap  " : $"{hits,10:F1}  ");
                    rateByTarget[targetKey] += attack.Carriers / hits;
                }
                report.AppendLine(line.ToString());
            }
            report.AppendLine("  squad: targets disabled per 100 hits  " + string.Join("  ",
                entries.Select(t => (100 * rateByTarget[t.Key]
                    / attacks[attackerKey].Sum(a => a.Carriers)).ToString("F3", CultureInfo.InvariantCulture).PadLeft(10))));
            foreach (var pair in rateByTarget)
            {
                squadRate[(attackerKey, pair.Key)] = pair.Value / attacks[attackerKey].Sum(a => a.Carriers);
            }
            report.AppendLine();
        }

        // Anti-hard share against each target, relative to the first entry as the soft reference:
        // how much worse (or better) a squad's hits do against this target than against the reference.
        string reference = entries[0].Key;
        report.AppendLine($"Relative effectiveness per hit: squad rate against the target / squad rate against '{reference}'.");
        report.AppendLine("  attacker  " + string.Join("  ", entries.Select(t => t.Key.PadLeft(10))));
        foreach ((string attackerKey, _) in entries)
        {
            report.AppendLine($"  {attackerKey,-8}  " + string.Join("  ", entries.Select(t =>
                (squadRate[(attackerKey, t.Key)] / squadRate[(attackerKey, reference)])
                    .ToString("G3", CultureInfo.InvariantCulture).PadLeft(10))));
        }

        // The planner's own estimate of the same quantity (RemovalMath: 1 / removal per hit against
        // a fresh target), at each setting of the location-combining exponent, beside the measured
        // value. This is the calibration of RemovalMath.LocationFillCombineExponent.
        report.AppendLine();
        report.AppendLine("Planner estimate of hits to disable (1 / removal per hit, fresh target), "
            + "measured | q=1 | q=2 | q=4 | q=inf; worst ratio estimate/measured per q at the end.");
        float[] exponents = [1f, 2f, 4f, float.PositiveInfinity];
        double[] worstLog = new double[exponents.Length];
        double[] sumSquaredLog = new double[exponents.Length];
        int cells = 0;
        foreach ((string attackerKey, _) in entries)
        {
            foreach (Attack attack in attacks[attackerKey])
            {
                foreach ((string targetKey, string targetSpec) in entries)
                {
                    double measured = MeanHitsToDisable(blob, targetSpec, attack, range, trials, cap);
                    if (measured >= cap)
                    {
                        continue;
                    }
                    double[] model = exponents.Select(q => ModelHitsToDisable(blob, targetSpec, attack, range, q, trials)).ToArray();
                    report.AppendLine(
                        $"  {attackerKey,-6} {attack.Weapon,-22} vs {targetKey,-6} {measured,9:F1} | "
                        + string.Join(" | ", model.Select(value => value.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9))));
                    for (int index = 0; index < exponents.Length; index++)
                    {
                        double logRatio = Math.Log(model[index] / measured);
                        worstLog[index] = Math.Max(worstLog[index], Math.Abs(logRatio));
                        sumSquaredLog[index] += logRatio * logRatio;
                    }
                    cells++;
                }
            }
        }
        for (int index = 0; index < exponents.Length; index++)
        {
            report.AppendLine(
                $"  q={exponents[index]}: worst factor {Math.Exp(worstLog[index]):F2}, "
                + $"rms factor {Math.Exp(Math.Sqrt(sumSquaredLog[index] / Math.Max(1, cells))):F2} over {cells} cells");
        }

        string explain = Environment.GetEnvironmentVariable("HARDNESS_EXPLAIN");
        if (!string.IsNullOrWhiteSpace(explain))
        {
            report.AppendLine();
            report.Append(Explain(blob, attacks, entries, explain, range, trials, cap));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output, report.ToString());
    }

    /// <summary>
    /// HARDNESS_EXPLAIN="attackerKey:Weapon Name:targetKey": for that one cell, why each simulated
    /// target left the fight (which vital crippled, no functioning hand, cannot move, severed
    /// limb) and the planner's per-location fill rate -- the comparison that shows which route
    /// the engine takes and which one the estimate assumes.
    /// </summary>
    private static string Explain(
        GameRulesBlob blob, Dictionary<string, List<Attack>> attacks, List<(string Key, string Spec)> entries,
        string request, int range, int trials, int cap)
    {
        string[] parts = request.Split(':');
        Attack attack = attacks[parts[0]].Single(a => a.Weapon == parts[1]);
        string targetSpec = entries.Single(e => e.Key == parts[2]).Spec;
        StringBuilder text = new($"EXPLAIN {request}{Environment.NewLine}");
        Dictionary<string, int> causes = [];
        SeededRNG random = new(12_345);
        WoundResolver resolver = new();
        resolver.OnSoldierDeath += (_, _) => { };
        resolver.OnSoldierFall += (_, _) => { };
        resolver.OnSoldierWounded += (_, _) => { };
        WeaponTemplate weapon = (WeaponTemplate)attack.Ranged ?? attack.Melee;
        Dictionary<string, (int Hits, double Added)> perLocation = [];
        for (int trial = 0; trial < trials; trial++)
        {
            BattleSoldier target = SquadDuelDiagnostics.BuildSquads(
                    blob, SquadDuelDiagnostics.ParseSide(targetSpec),
                    new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator(), "T", new SeededRNG(90_000 + trial))
                .SelectMany(s => s.Soldiers).OrderBy(s => s.Soldier.Template.IsSquadLeader).First();
            int hits = 0;
            while (target.IsCombatEffective && hits < cap)
            {
                hits++;
                HitLocation location = HitLocationCalculator.DetermineHitLocation(target, random);
                if (location.IsSevered) continue;
                float before = RemovalMath.WoundCounterValue(location.Wounds.WoundTotal);
                var tally = perLocation.GetValueOrDefault(location.Template.Name);
                perLocation[location.Template.Name] = (tally.Hits + 1, tally.Added);
                try
                {
                float roll = 3.5f + ((float)random.NextRandomZValue() * 1.75f);
                float damage = attack.Ranged != null
                    ? BattleModifiersUtil.CalculateDamageAtRange(attack.Ranged, range) * roll
                    : attack.Strength * attack.Melee.StrengthMultiplier * roll;
                float penetrating = damage - (target.Armor?.Template.ArmorProvided ?? 0) * weapon.ArmorMultiplier;
                if (penetrating <= 0) continue;
                resolver.WoundQueue.Add(new WoundResolution(null, weapon, target, penetrating * weapon.WoundMultiplier, location));
                resolver.Resolve();
                }
                finally
                {
                    var after = perLocation[location.Template.Name];
                    perLocation[location.Template.Name] = (after.Hits,
                        after.Added + RemovalMath.WoundCounterValue(location.Wounds.WoundTotal) - before);
                }
            }
            ISoldier soldier = target.Soldier;
            string cause = soldier.HasUntreatedSeveredLimb ? "severed limb: " + string.Join(",",
                    soldier.Body.HitLocations.Where(l => l.IsSevered && (l.Template.IsMotive || l.Template.HandGroupId.HasValue)).Select(l => l.Template.Name))
                : !soldier.CanMove ? "cannot move"
                : soldier.Body.HitLocations.Any(l => l.Template.IsVital && (l.IsCrippled || l.IsSevered))
                    ? "vital: " + string.Join(",", soldier.Body.HitLocations.Where(l => l.Template.IsVital && (l.IsCrippled || l.IsSevered)).Select(l => l.Template.Name))
                : soldier.FunctioningHands == 0 ? "no functioning hand"
                : hits >= cap ? "cap" : "other";
            causes[cause] = causes.GetValueOrDefault(cause) + 1;
        }
        foreach (var cause in causes.OrderByDescending(c => c.Value))
        {
            text.AppendLine($"  {cause.Value,4}  {cause.Key}");
        }
        text.AppendLine("  engine: hits landed and mean counter value added per hit, by location");
        foreach (var entry in perLocation.OrderBy(e => e.Key))
        {
            text.AppendLine($"    {entry.Key,-14} hits {entry.Value.Hits,7}  mean added {entry.Value.Added / Math.Max(1, entry.Value.Hits),8:F2}");
        }
        BattleSoldier fresh = SquadDuelDiagnostics.BuildSquads(
                blob, SquadDuelDiagnostics.ParseSide(targetSpec),
                new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator(), "T", new SeededRNG(90_000))
            .SelectMany(s => s.Soldiers).OrderBy(s => s.Soldier.Template.IsSquadLeader).First();
        float damageCoefficient = attack.Ranged != null
            ? BattleModifiersUtil.CalculateDamageAtRange(attack.Ranged, range)
            : attack.Strength * attack.Melee.StrengthMultiplier;
        IReadOnlyList<TakeOutLocationTerm> terms = RemovalMath.BuildTakeOutLocationTerms(
            fresh, (fresh.Armor?.Template.ArmorProvided ?? 0) * weapon.ArmorMultiplier, weapon.WoundMultiplier);
        text.AppendLine("  planner locations: weight, gap, fill rate per hit (weight x fill / gap)");
        foreach (TakeOutLocationTerm term in terms)
        {
            float rate = RemovalMath.EvaluateRemovalTerms([term], damageCoefficient, 1f) is var pair
                ? pair.TakeOut + pair.Progress
                : 0;
            text.AppendLine($"    w={term.Weight:F3} gap={term.Gap,8:F0} armor={term.EffectiveArmor:F1} dmg/ratio={term.DamagePerRatio:F1} rate={rate:E3} (hits {1 / rate:F0}) mean added per hit here {rate / term.Weight * term.Gap:F2}");
        }
        return text.ToString();
    }

    // Averaged over the same generated targets MeanHitsToDisable fights: soldiers of one template
    // differ in Constitution, and a monster's spread is wide (one seed's Carnifex had 185 against a
    // mean nearer 224), so comparing one target's estimate with the mean of many is not a test.
    private static double ModelHitsToDisable(
        GameRulesBlob blob, string targetSpec, Attack attack, int range, float exponent, int trials)
    {
        WeaponTemplate weapon = (WeaponTemplate)attack.Ranged ?? attack.Melee;
        float damageCoefficient = attack.Ranged != null
            ? BattleModifiersUtil.CalculateDamageAtRange(attack.Ranged, range)
            : attack.Strength * attack.Melee.StrengthMultiplier;
        double total = 0;
        for (int trial = 0; trial < trials; trial++)
        {
            BattleSoldier target = SquadDuelDiagnostics.BuildSquads(
                    blob, SquadDuelDiagnostics.ParseSide(targetSpec),
                    new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator(), "T", new SeededRNG(90_000 + trial))
                .SelectMany(s => s.Soldiers)
                .OrderBy(s => s.Soldier.Template.IsSquadLeader)
                .First();
            IReadOnlyList<TakeOutLocationTerm> terms = RemovalMath.BuildTakeOutLocationTerms(
                target,
                (target.Armor?.Template.ArmorProvided ?? 0) * weapon.ArmorMultiplier,
                weapon.WoundMultiplier);
            (float takeOut, float progress) = RemovalMath.EvaluateRemovalTerms(terms, damageCoefficient, exponent);
            float perHit = Math.Clamp(takeOut + progress, 0f, 1f);
            total += perHit > 0 ? 1.0 / perHit : 1e9;
        }
        return total / trials;
    }

    /// <summary>
    /// The distinct attacks a full squad of this spec can make: each ranged weapon its soldiers carry,
    /// and each melee weapon (or, for a soldier with none, the species' unarmed attack) with that
    /// carrier's Strength, counting how many soldiers carry each.
    /// </summary>
    private static List<Attack> AttacksOf(GameRulesBlob blob, string spec)
    {
        List<BattleSquad> squads = SquadDuelDiagnostics.BuildSquads(
            blob, SquadDuelDiagnostics.ParseSide(spec),
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator(), "H", new SeededRNG(80_000));
        List<Attack> result = [];
        foreach (BattleSoldier soldier in squads.SelectMany(s => s.Soldiers))
        {
            foreach (RangedWeaponTemplate ranged in soldier.RangedWeapons.Select(w => w.Template).Distinct())
            {
                result.Add(new Attack("ranged", ranged.Name, 0, 1, ranged, null));
            }
            List<MeleeWeaponTemplate> melee = soldier.MeleeWeapons.Select(w => w.Template).Distinct().ToList();
            if (melee.Count == 0 && soldier.Soldier.Template.Species.DefaultUnarmedWeapon != null)
            {
                melee.Add(soldier.Soldier.Template.Species.DefaultUnarmedWeapon);
            }
            foreach (MeleeWeaponTemplate weapon in melee)
            {
                result.Add(new Attack("melee", weapon.Name, soldier.Soldier.Strength, 1, null, weapon));
            }
        }
        // One line per distinct attack; melee lines keep the mean Strength of their carriers.
        return result
            .GroupBy(a => (a.Kind, a.Weapon))
            .Select(g => g.First() with { Carriers = g.Count(), Strength = g.Average(a => a.Strength) })
            .ToList();
    }

    private static double MeanHitsToDisable(
        GameRulesBlob blob, string targetSpec, Attack attack, int range, int trials, int cap)
    {
        SeededRNG random = new(12_345);
        WoundResolver resolver = new();
        resolver.OnSoldierDeath += (_, _) => { };
        resolver.OnSoldierFall += (_, _) => { };
        resolver.OnSoldierWounded += (_, _) => { };
        List<SquadSpec> specs = SquadDuelDiagnostics.ParseSide(targetSpec);
        double total = 0;
        for (int trial = 0; trial < trials; trial++)
        {
            BattleSoldier target = SquadDuelDiagnostics.BuildSquads(
                    blob, specs, new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator(), "T", new SeededRNG(90_000 + trial))
                .SelectMany(s => s.Soldiers)
                .OrderBy(s => s.Soldier.Template.IsSquadLeader)
                .First();
            int hits = 0;
            while (target.IsCombatEffective && hits < cap)
            {
                hits++;
                HitLocation location = HitLocationCalculator.DetermineHitLocation(target, random);
                if (location.IsSevered) continue;
                WeaponTemplate weapon = (WeaponTemplate)attack.Ranged ?? attack.Melee;
                float roll = 3.5f + ((float)random.NextRandomZValue() * 1.75f);
                float damage = attack.Ranged != null
                    ? BattleModifiersUtil.CalculateDamageAtRange(attack.Ranged, range) * roll
                    : attack.Strength * attack.Melee.StrengthMultiplier * roll;
                float penetrating = damage - (target.Armor?.Template.ArmorProvided ?? 0) * weapon.ArmorMultiplier;
                if (penetrating <= 0) continue;
                resolver.WoundQueue.Add(new WoundResolution(
                    null, weapon, target, penetrating * weapon.WoundMultiplier, location));
                resolver.Resolve();
            }
            total += hits;
        }
        return total / trials;
    }
}
