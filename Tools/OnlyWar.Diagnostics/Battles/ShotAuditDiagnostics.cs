using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using OnlyWar.Battles;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Resolutions;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Generation.World;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Diagnostics.Battles;

/// <summary>
/// Opt-in audit of the planner's removal model against the engine (opening-range refactor,
/// calibration item 3: the forecast now agrees with the planner's rate table, and both remove more
/// than the battles do). Two halves:
///
/// HITS (RUN_SHOT_AUDIT): fights SHOT_AUDIT_SCENARIO (duel syntax, "A vs B @ range", assault:N
/// allowed) for SHOT_AUDIT_SEEDS battles on this thread with ShootAction.Observer set, and per side
/// and range band compares the hits the engine rolled with the hits its own to-hit formula expects
/// at the total it used, and at the totals the planner's sustained-aim model assumes: a full aim,
/// with the target's real speed and with it standing still.
///
/// REMOVAL (RUN_HITS_TO_KILL): fires HITS_TO_KILL_TRIALS strings of hits from the shooter squad's
/// most common ranged weapon at fresh soldiers of the target squad, through the engine's own hit
/// location, damage and wound rules (ShootAction.HandleHit, WoundResolver), and counts hits until
/// the target is no longer combat effective; against the planner's removal per hit
/// (RemovalMath.EvaluateRemovalTerms on the target's real locations). HITS_TO_KILL_SCENARIO is
/// "shooterTemplate vs targetTemplate @ range".
///
/// Output: SHOT_AUDIT_OUT folder (default %TEMP%\GodotOnlyWar\shot-audit), shot-audit.txt and
/// hits-to-kill.txt.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ShotAuditDiagnostics
{
    private static readonly float[] Bands = [2, 10, 25, 50, 100, 200, float.PositiveInfinity];
    // As HardnessDiagnostics' default cap.
    private const int HitsToKillCap = 20_000;

    [Fact]
    public void ShotAudit_HitsAgainstThePlannersAssumptions()
    {
        string scenario = Environment.GetEnvironmentVariable("SHOT_AUDIT_SCENARIO");
        if (Environment.GetEnvironmentVariable("RUN_SHOT_AUDIT") == null || string.IsNullOrWhiteSpace(scenario))
        {
            return;
        }
        int seeds = SquadDuelDiagnostics.ReadInt("SHOT_AUDIT_SEEDS", 8);
        string[] parts = scenario.Split('@');
        string[] sides = parts[0].Split(" vs ", StringSplitOptions.TrimEntries);
        int range = SquadDuelDiagnostics.ParseRange(parts.Length > 1 ? parts[1].Trim() : "200");
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();

        Dictionary<(string Side, int Band), Tally> tallies = [];
        foreach (int seed in Enumerable.Range(1, seeds))
        {
            bool aAttacks = seed % 2 == 1;
            List<ShootAction.ShotObservation> shots = [];
            ShootAction.Observer = shots.Add;
            try
            {
                SquadDuelDiagnostics.RunBattle(
                    blob,
                    SquadDuelDiagnostics.ParseSide(sides[0]),
                    SquadDuelDiagnostics.ParseSide(sides[1]),
                    range,
                    seed,
                    SquadDuelDiagnostics.ReadAggression("DUEL_AGGR"),
                    planningWorkers: 1,
                    aAttacks: aAttacks);
            }
            finally
            {
                ShootAction.Observer = null;
            }
            foreach (ShootAction.ShotObservation shot in shots)
            {
                // The duel names every squad after its side ("A ...", "B ...").
                string side = shot.Shooter.BattleSquad?.Name.StartsWith("A ", StringComparison.Ordinal) == true
                    ? "A"
                    : "B";
                int band = Array.FindIndex(Bands, edge => shot.Range < edge);
                Tally tally = tallies.GetValueOrDefault((side, band)) ?? new Tally();
                tally.Add(shot);
                tallies[(side, band)] = tally;
            }
        }

        StringBuilder report = new();
        report.AppendLine($"Shot audit: {scenario}, {seeds} battles. Per side and range band. hits/rd: actual, then the "
            + "engine's own expectation at the total it rolled against (should match), at a full aim with the target's real "
            + "speed, and at a full aim on a still target (the planner's sustained-aim assumption for a stander).");
        report.AppendLine($"  {"side",4} {"band",9} {"volleys",7} {"rounds",7} {"aimed",6} {"full",6} {"mean aim",8} {"speed",6} "
            + $"{"melee",6} {"shaken",6} | {"actual",7} {"expected",8} {"fullAim",7} {"still",7}");
        foreach (((string side, int band), Tally tally) in tallies.OrderBy(entry => entry.Key.Side).ThenBy(entry => entry.Key.Band))
        {
            string label = band == 0 ? "<2" : $"{Bands[band - 1]:0}-{(float.IsInfinity(Bands[band]) ? "" : Bands[band].ToString("0"))}";
            report.AppendLine($"  {side,4} {label,9} {tally.Volleys,7} {tally.Rounds,7} {P(tally.Aimed, tally.Volleys),6} "
                + $"{P(tally.FullAim, tally.Volleys),6} {tally.AimBonusSum / Math.Max(1, tally.Aimed),8:0.00} "
                + $"{tally.SpeedSum / Math.Max(1, tally.Volleys),6:0.0} {P(tally.IntoMelee, tally.Volleys),6} {P(tally.Shaken, tally.Volleys),6} | "
                + $"{R(tally.Hits, tally.Rounds),7} {R(tally.Expected, tally.Rounds),8} {R(tally.ExpectedFullAim, tally.Rounds),7} "
                + $"{R(tally.ExpectedStill, tally.Rounds),7}");
        }
        Write("shot-audit.txt", report.ToString());
    }

    [Fact]
    public void HitsToKill_EngineAgainstThePlannersRemovalPerHit()
    {
        string scenario = Environment.GetEnvironmentVariable("HITS_TO_KILL_SCENARIO");
        if (Environment.GetEnvironmentVariable("RUN_HITS_TO_KILL") == null || string.IsNullOrWhiteSpace(scenario))
        {
            return;
        }
        int trials = SquadDuelDiagnostics.ReadInt("HITS_TO_KILL_TRIALS", 2000);
        string[] parts = scenario.Split('@');
        string[] sides = parts[0].Split(" vs ", StringSplitOptions.TrimEntries);
        float range = parts.Length > 1 ? float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture) : 50f;
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        OnlyWar.Abstractions.IEntityIdAllocator ids = new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        SeededRNG random = new(4243);

        BattleSquad shooters = SquadDuelDiagnostics.BuildSquads(blob, SquadDuelDiagnostics.ParseSide(sides[0]), ids, "S", random)[0];
        BattleSoldier shooter = shooters.AbleSoldiers.First();
        // HITS_TO_KILL_MELEE: the attacker's primary melee weapon (MeleeAttackAction.HandleHit) instead.
        bool melee = Environment.GetEnvironmentVariable("HITS_TO_KILL_MELEE") != null;
        WeaponTemplate template;
        float armorMultiplier;
        float woundMultiplier;
        float damageCoefficient;
        if (melee)
        {
            MeleeWeapon meleeWeapon = shooter.GetPrimaryMeleeWeapon(MeleeAttackAction.GetUnarmedWeapon(shooter));
            template = meleeWeapon.Template;
            armorMultiplier = meleeWeapon.Template.ArmorMultiplier;
            woundMultiplier = meleeWeapon.Template.WoundMultiplier;
            damageCoefficient = shooter.Soldier.Strength * meleeWeapon.Template.StrengthMultiplier;
        }
        else
        {
            RangedWeapon weapon = shooters.AbleSoldiers
                .SelectMany(soldier => soldier.EquippedRangedWeapons)
                .GroupBy(candidate => candidate.Template.Id)
                .OrderByDescending(group => group.Count())
                .First()
                .First();
            template = weapon.Template;
            armorMultiplier = weapon.Template.ArmorMultiplier;
            woundMultiplier = weapon.Template.WoundMultiplier;
            damageCoefficient = BattleModifiersUtil.CalculateDamageAtRange(weapon.Template, range);
        }

        StringBuilder report = new();
        report.AppendLine($"Hits to take out: {template.Name} {(melee ? "in melee" : $"at {range:0} yd")} against {sides[1]}, {trials} targets.");
        int totalHits = 0;
        int takenOut = 0;
        float plannerTakeOut = 0;
        float plannerRemoval = 0;
        int measured = 0;
        List<BattleSoldier> pool = [];
        for (int trial = 0; trial < trials; trial++)
        {
            if (pool.Count == 0)
            {
                pool = SquadDuelDiagnostics.BuildSquads(blob, SquadDuelDiagnostics.ParseSide(sides[1]), ids, "T", random)
                    .SelectMany(squad => squad.AbleSoldiers)
                    .ToList();
            }
            BattleSoldier target = pool[^1];
            pool.RemoveAt(pool.Count - 1);
            if (measured < 50)
            {
                IReadOnlyList<TakeOutLocationTerm> terms = RemovalMath.BuildTakeOutLocationTerms(
                    target, target.Armor?.Template.ArmorProvided ?? 0, armorMultiplier, woundMultiplier);
                (float takeOut, float progress) = RemovalMath.EvaluateRemovalTerms(
                    terms, damageCoefficient, RemovalMath.LocationFillCombineExponent);
                plannerTakeOut += takeOut;
                plannerRemoval += RemovalMath.CombineRemovalFraction(takeOut, progress);
                measured++;
            }

            WoundResolver resolver = new(random);
            // The resolver raises these unconditionally.
            resolver.OnSoldierFall += (_, _) => { };
            resolver.OnSoldierDeath += (_, _) => { };
            resolver.OnSoldierWounded += (_, _) => { };
            int hits = 0;
            // Each target runs to the end: hits get deadlier as wounds build up, so a string cut
            // short overstates hits per take-out. At 500 (until 2026-10-26) an autogun against a
            // Tyranid Warrior at 250 yards read 963 against the 608 of HardnessDiagnostics.
            while (target.IsCombatEffective && hits < HitsToKillCap)
            {
                hits++;
                HitLocation location = HitLocationCalculator.DetermineHitLocation(target, random);
                if (location.IsSevered)
                {
                    continue;
                }
                // As ShootAction.HandleHit.
                float damage = damageCoefficient * (3.5f + ((float)random.NextRandomZValue() * 1.75f));
                float penetrating = damage - RemovalMath.EffectiveArmor(
                    target.Armor?.Template.ArmorProvided ?? 0, location.NaturalArmor, armorMultiplier);
                if (penetrating > 0)
                {
                    resolver.WoundQueue.Add(new WoundResolution(
                        shooter, template, target, penetrating * woundMultiplier, location));
                    resolver.Resolve();
                }
            }
            totalHits += hits;
            if (!target.IsCombatEffective)
            {
                takenOut++;
            }
        }
        report.AppendLine($"  engine: {totalHits / (double)Math.Max(1, takenOut):0.0} hits per take-out ({takenOut} of {trials} taken out)");
        report.AppendLine($"  planner, fresh target: take-out per hit {plannerTakeOut / measured:0.0000} "
            + $"({measured / plannerTakeOut:0.0} hits per take-out), removal per hit with softening "
            + $"{plannerRemoval / measured:0.0000} ({measured / plannerRemoval:0.0} hits per take-out)");
        Write("hits-to-kill.txt", report.ToString());
    }

    private sealed class Tally
    {
        public int Volleys;
        public int Rounds;
        public int Hits;
        public int Aimed;
        public int FullAim;
        public float AimBonusSum;
        public float SpeedSum;
        public int IntoMelee;
        public int Shaken;
        public double Expected;
        public double ExpectedFullAim;
        public double ExpectedStill;

        public void Add(ShootAction.ShotObservation shot)
        {
            Volleys++;
            Rounds += shot.Shots;
            Hits += shot.Hits;
            if (shot.AimBonus.HasValue)
            {
                Aimed++;
                AimBonusSum += shot.AimBonus.Value;
                if (shot.AimBonus.Value >= RangedTargetSelector.FullAimBonusTurns)
                {
                    FullAim++;
                }
            }
            SpeedSum += shot.TargetSpeed;
            IntoMelee += shot.IntoMelee ? 1 : 0;
            Shaken += shot.Shaken ? 1 : 0;

            float total = shot.Skill + shot.Modifier;
            float accuracy = BattleModifiersUtil.CalculateAimAccuracy(
                shot.Weapon.Template.Accuracy, shot.Skill);
            float aimed = shot.AimBonus.HasValue ? shot.AimBonus.Value + accuracy + 1 : 0;
            float fullAim = RangedTargetSelector.FullAimBonusTurns + accuracy + 1;
            float speedTerm = BattleModifiersUtil.CalculateRangeModifier(shot.Range, shot.TargetSpeed)
                - BattleModifiersUtil.CalculateRangeModifier(shot.Range, 0f);
            float recoil = shot.Weapon.Template.Recoil;
            Expected += ExpectedHits(total, shot.Shots, recoil);
            ExpectedFullAim += ExpectedHits(total - aimed + fullAim, shot.Shots, recoil);
            ExpectedStill += ExpectedHits(total - aimed + fullAim - speedTerm, shot.Shots, recoil);
        }
    }

    /// <summary>
    /// The engine's hits for a pre-roll total (ShootAction.Execute): the roll is 10.5 + 3z; a
    /// margin m above it hits once, and each further shot hits while m less the recoil so far
    /// stays above 1. Integrated over z on a fine grid.
    /// </summary>
    private static double ExpectedHits(float total, int shots, float recoil)
    {
        const int steps = 400;
        double sum = 0;
        double weight = 0;
        for (int step = 0; step <= steps; step++)
        {
            double z = -5 + (10.0 * step / steps);
            double density = Math.Exp(-0.5 * z * z);
            weight += density;
            double margin = total - (10.5 + (3 * z));
            if (margin <= 0)
            {
                continue;
            }
            int hits = 1;
            double left = margin;
            while (hits < shots)
            {
                left -= recoil;
                if (left <= 1)
                {
                    break;
                }
                hits++;
            }
            sum += density * hits;
        }
        return sum / weight;
    }

    private static void Write(string name, string text)
    {
        string directory = Environment.GetEnvironmentVariable("SHOT_AUDIT_OUT")
            ?? Path.Combine(Path.GetTempPath(), "GodotOnlyWar", "shot-audit");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), text);
    }

    private static string P(int part, int whole) => whole > 0 ? $"{100.0 * part / whole:0}%" : "n/a";

    private static string R(double part, int rounds) => rounds > 0 ? (part / rounds).ToString("0.000", CultureInfo.InvariantCulture) : "n/a";
}
