using System;
using OnlyWar.Helpers;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using SoldierAttribute = OnlyWar.Models.Soldiers.Attribute;

namespace OnlyWar.Helpers.Battles;

/// <summary>
/// Deterministic valuation of a representative soldier by replaying the tactical
/// engine's own to-hit and damage math (ShootAction / MeleeAttackAction / WoundResolver)
/// against a weighted reference threat panel: swarm chaff, light human infantry, elite
/// power-armored infantry, and a monstrous creature.
///
/// Offense is the soldier's expected kills per turn against the panel; Durability is the
/// expected turns survived under the panel's return fire. The unit value is the Lanchester
/// square-law geometric mean sqrt(Offense x Durability) — a unit that kills twice as fast,
/// or lives twice as long, is worth sqrt(2) line troopers — scaled by a small command
/// multiplier and anchored so the light-infantry reference profile scores
/// <see cref="AnchorBattleValue"/> (the PDF trooper's strategic garrison unit scale,
/// see StrategicCombatRules.PdfTrooperBattleValue).
///
/// This is intentionally an offline/data-calibration primitive; the resulting value is
/// persisted on SoldierTemplate and consumed by force generation.
/// </summary>
public static class BattleValueCalculator
{
    /// <summary>The light-infantry reference profile is defined to be worth this much.</summary>
    public const float AnchorBattleValue = 5.0f;

    /// <summary>
    /// Effective HP: killing a soldier takes about this many multiples of Constitution in
    /// post-armor wound damage, approximating the hit-location wound ladder in WoundResolver.
    /// </summary>
    public const float KillThresholdConMultiple = 3.0f;

    /// <summary>
    /// Melee output is discounted for the turns spent closing under fire; faster movers
    /// waste less. Factor = MeleeClosingFactor * min(1, MoveSpeed / reference speed).
    /// Burrowers pay no closing tax at all: BurrowPlacer erupts them adjacent to an
    /// enemy squad at battle start and they attack the turn they surface.
    /// </summary>
    public const float MeleeClosingReferenceSpeed = 8.0f;
    public const float MeleeClosingFactor = 0.75f;

    /// <summary>A unit with both a ranged and a melee mode gets this credit for the lesser mode.</summary>
    public const float SecondaryModeCredit = 0.25f;

    /// <summary>
    /// Average of the blast damage falloff (1 - d/R)^2 across the victims of a typical
    /// blast. A uniform spread over the disc would average exactly 1/6
    /// (integral of 2x(1-x)^2 dx from 0 to 1), and a perfectly centered victim gets 1.0;
    /// the planner aims blasts at cluster centers, pulling victims toward the middle,
    /// while scatter (the margin-driven placement check) and multi-cell footprints push
    /// them back out. 0.5 splits that range: placement quality and scatter are folded
    /// into this single tunable rather than modeling the throw's skill roll explicitly,
    /// mirroring how the cone branch treats template hits as skill-free auto-hits.
    /// </summary>
    public const float BlastAverageFalloffFactor = 0.5f;

    /// <summary>Durability cap: nothing is valued as surviving longer than this under panel fire.</summary>
    public const float MaxSurvivalTurns = 400.0f;

    // Engine damage roll: DamageMultiplier (or Str x StrengthMultiplier) x (3.5 + 1.75Z).
    private const float DamageRollMean = 3.5f;
    private const float DamageRollStdDev = 1.75f;

    // Engine ranged hit roll: total = skill + modifiers - (10.5 + 3Z); shots chain while
    // the remaining margin exceeds the weapon's recoil.
    private const float RangedRollTarget = 10.5f;
    private const float RangedRollStdDev = 3.0f;

    public sealed class Input
    {
        public float Strength { get; init; }
        public float Constitution { get; init; }
        public float AttackSpeed { get; init; }
        public float MoveSpeed { get; init; } = 6.0f;
        public float Size { get; init; } = 1.0f;
        /// <summary>Footprint in grid cells; bounds how many enemies a melee attacker can engage.</summary>
        public int WidthCells { get; init; } = 1;
        public int DepthCells { get; init; } = 1;
        public float MeleeSkill { get; init; }
        public float RangedSkill { get; init; }
        /// <summary>
        /// Skill used when defending in melee. Defaults to MeleeSkill. In the engine an
        /// unarmed defender uses the default unarmed weapon's skill (Fist / Generic Melee);
        /// basic unarmed training is part of every soldier's MOS data, so that usually
        /// tracks close to their melee skill — pass this explicitly when it differs.
        /// </summary>
        public float MeleeDefenseSkill { get; init; } = float.NaN;
        public float MeleeEvasion { get; init; }
        public float RangedEvasion { get; init; }
        public float Armor { get; init; }
        /// <summary>Burrow-capable species erupt adjacent to the enemy and strike the same turn.</summary>
        public bool CanBurrow { get; init; }
        public MeleeWeaponTemplate MeleeWeapon { get; init; }
        /// <summary>Off-hand melee weapon: adds one strike per turn and +1 melee defense.</summary>
        public MeleeWeaponTemplate SecondaryMeleeWeapon { get; init; }
        public RangedWeaponTemplate RangedWeapon { get; init; }
        /// <summary>
        /// Grenade sidearm (WeaponSet third ranged slot). A soldier fires the primary OR
        /// throws in a given turn, never both, so this contributes marginal offense only:
        /// against each panel profile the better of the two is used.
        /// </summary>
        public RangedWeaponTemplate GrenadeWeapon { get; init; }
        /// <summary>MOS training points feeding the command multiplier.</summary>
        public float TacticsTrainingPoints { get; init; }
        public float LeadershipTrainingPoints { get; init; }
    }

    public readonly struct Result
    {
        public float Offense { get; }
        public float Durability { get; }
        public float NormalizedOffense { get; }
        public float NormalizedDurability { get; }
        public int BattleValue { get; }

        public Result(float offense, float durability, float normalizedOffense,
                      float normalizedDurability, int battleValue)
        {
            Offense = offense;
            Durability = durability;
            NormalizedOffense = normalizedOffense;
            NormalizedDurability = normalizedDurability;
            BattleValue = battleValue;
        }
    }

    private static readonly BaseSkill ReferenceSkill = new(
        0, SkillCategory.Melee, "Battle Value Reference Skill", SoldierAttribute.Strength, 0);

    // Engine default unarmed weapon (post-rework Fist values).
    private static readonly MeleeWeaponTemplate UnarmedFist = new(
        0, "Battle Value Unarmed Fist", EquipLocation.OneHand, ReferenceSkill,
        accuracy: 0, armorMultiplier: 1, penetrationMultiplier: 1, requiredStrength: 0,
        strengthMultiplier: 0.5f, parryMod: -1, attackSpeedMultiplier: 1);

    // ----- reference threat panel (values mirror the shipped rules database) -----

    private static readonly RangedWeaponTemplate PanelDevourer = new(
        0, "Panel Devourer", EquipLocation.TwoHand, ReferenceSkill,
        accuracy: 0, armorMultiplier: 1, penetrationMultiplier: 1, requiredStrength: 8,
        baseDamage: 6, maxDistance: 750, rof: 15, ammo: 100, recoil: 3, bulk: 2,
        doesDamageDegradeWithRange: true, reloadTime: 5);

    private static readonly RangedWeaponTemplate PanelLasgun = new(
        0, "Panel Lasgun", EquipLocation.TwoHand, ReferenceSkill,
        accuracy: 10, armorMultiplier: 1, penetrationMultiplier: 1, requiredStrength: 7,
        baseDamage: 4.5f, maxDistance: 1000, rof: 10, ammo: 80, recoil: 1, bulk: 4,
        doesDamageDegradeWithRange: true, reloadTime: 3);

    private static readonly RangedWeaponTemplate PanelBoltgun = new(
        0, "Panel Boltgun", EquipLocation.TwoHand, ReferenceSkill,
        accuracy: 3, armorMultiplier: 1, penetrationMultiplier: 2, requiredStrength: 12,
        baseDamage: 6, maxDistance: 1000, rof: 9, ammo: 30, recoil: 2, bulk: 4,
        doesDamageDegradeWithRange: false, reloadTime: 3);

    // Frag grenade (thrown blast): MaximumRange stores meters-per-Strength-point.
    private static readonly RangedWeaponTemplate PanelFragGrenade = new(
        0, "Panel Frag Grenade", EquipLocation.OneHand, ReferenceSkill,
        accuracy: 0, armorMultiplier: 1, penetrationMultiplier: 1, requiredStrength: 0,
        baseDamage: 5, maxDistance: 3, rof: 1, ammo: 1, recoil: 0, bulk: 0,
        doesDamageDegradeWithRange: false, reloadTime: 1,
        templateType: 3, areaRadius: 6);

    private static readonly MeleeWeaponTemplate PanelMonstrousTalons = new(
        0, "Panel Monstrous Scything Talons", EquipLocation.OneHand, ReferenceSkill,
        accuracy: 1, armorMultiplier: 0.2f, penetrationMultiplier: 4.5f, requiredStrength: 16,
        strengthMultiplier: 0.5f, parryMod: 0, attackSpeedMultiplier: 1);

    private static readonly MeleeWeaponTemplate PanelThresherScythe = new(
        0, "Panel Thresher Scythe", EquipLocation.OneHand, ReferenceSkill,
        accuracy: 0, armorMultiplier: 0.75f, penetrationMultiplier: 1.5f, requiredStrength: 16,
        strengthMultiplier: 1.34f, parryMod: 0, attackSpeedMultiplier: 1);

    // Termagaunt-like swarm chaff.
    private static readonly Input SwarmChaffProfile = new()
    {
        Strength = 12, Constitution = 12, AttackSpeed = 10, MoveSpeed = 6, Size = 1,
        MeleeSkill = 14, RangedSkill = 14, Armor = 5,
        RangedWeapon = PanelDevourer
    };

    // PDF-trooper-like light human infantry: the anchor profile.
    private static readonly Input LightInfantryProfile = new()
    {
        Strength = 10, Constitution = 10, AttackSpeed = 10, MoveSpeed = 6, Size = 1.75f,
        MeleeSkill = 11, RangedSkill = 11.6f, Armor = 5,
        RangedWeapon = PanelLasgun, GrenadeWeapon = PanelFragGrenade
    };

    // Tactical-marine-like elite infantry.
    private static readonly Input EliteInfantryProfile = new()
    {
        Strength = 15, Constitution = 30, AttackSpeed = 15, MoveSpeed = 6, Size = 2.4f,
        MeleeSkill = 14, RangedSkill = 15, Armor = 20,
        RangedWeapon = PanelBoltgun, GrenadeWeapon = PanelFragGrenade
    };

    // Carnifex-like monstrous creature.
    private static readonly Input MonsterProfile = new()
    {
        Strength = 24, Constitution = 224, AttackSpeed = 40, MoveSpeed = 7, Size = 8,
        WidthCells = 4, DepthCells = 2,
        MeleeSkill = 12, RangedSkill = 6, Armor = 20,
        MeleeWeapon = PanelMonstrousTalons, SecondaryMeleeWeapon = PanelThresherScythe
    };

    private static readonly (Input Profile, float Weight)[] ThreatPanel =
    [
        (SwarmChaffProfile, 0.30f),
        (LightInfantryProfile, 0.25f),
        (EliteInfantryProfile, 0.30f),
        (MonsterProfile, 0.15f)
    ];

    private static readonly Lazy<(float Offense, float Durability)> ReferenceScores =
        new(() => ScoreAgainstPanel(LightInfantryProfile));

    public static Result Calculate(Input input)
    {
        if (input == null)
        {
            return new Result(0, 0, 0, 0, 0);
        }

        (float offense, float durability) = ScoreAgainstPanel(input);
        (float referenceOffense, float referenceDurability) = ReferenceScores.Value;

        float normalizedOffense = Math.Max(0.0001f, offense / referenceOffense);
        float normalizedDurability = Math.Max(0.0001f, durability / referenceDurability);
        float commandMultiplier = CalculateCommandMultiplier(input);
        float rawValue = AnchorBattleValue * commandMultiplier
            * (float)Math.Sqrt(normalizedOffense * normalizedDurability);
        int battleValue = Math.Max(1, (int)Math.Round(rawValue, MidpointRounding.AwayFromZero));
        return new Result(offense, durability, normalizedOffense, normalizedDurability, battleValue);
    }

    private static (float Offense, float Durability) ScoreAgainstPanel(Input input)
    {
        float offense = 0;
        float incomingKillRate = 0;
        foreach ((Input profile, float weight) in ThreatPanel)
        {
            offense += weight * CalculateKillRate(input, profile);
            incomingKillRate += weight * CalculateKillRate(profile, input);
        }

        float durability = 1.0f / Math.Max(incomingKillRate, 1.0f / MaxSurvivalTurns);
        return (Math.Max(0.0001f, offense), durability);
    }

    private static float CalculateCommandMultiplier(Input input)
    {
        return 1.0f
            + 0.06f * (float)Math.Log(1.0 + Math.Max(0, input.TacticsTrainingPoints), 2.0)
            + 0.04f * (float)Math.Log(1.0 + Math.Max(0, input.LeadershipTrainingPoints), 2.0);
    }

    /// <summary>Expected kills per turn of <paramref name="attacker"/> against <paramref name="defender"/>.</summary>
    private static float CalculateKillRate(Input attacker, Input defender)
    {
        float primaryRangedRate = attacker.RangedWeapon != null
            ? CalculateRangedKillRate(attacker, attacker.RangedWeapon, defender)
            : 0;
        // The grenade is a sidearm, not the main gun: a turn spent throwing forgoes the
        // primary volley, so its value is marginal — per panel profile the soldier uses
        // whichever of the two is better, never both.
        float grenadeRate = attacker.GrenadeWeapon != null
            ? CalculateRangedKillRate(attacker, attacker.GrenadeWeapon, defender)
            : 0;
        float rangedRate = Math.Max(primaryRangedRate, grenadeRate);
        float meleeRate = CalculateMeleeKillRate(attacker, defender);
        return Math.Max(rangedRate, meleeRate)
            + SecondaryModeCredit * Math.Min(rangedRate, meleeRate);
    }

    // ----- ranged model: mirrors ShootAction -----

    private static float CalculateRangedKillRate(Input attacker, RangedWeaponTemplate weapon, Input defender)
    {
        if (weapon.IsBlastWeapon)
        {
            return CalculateBlastWeaponKillRate(attacker, weapon, defender);
        }
        if (weapon.IsTemplateWeapon)
        {
            return CalculateTemplateWeaponKillRate(weapon, defender);
        }

        int rateOfFire = Math.Max(1, (int)weapon.RateOfFire);
        float margin = attacker.RangedSkill
            + BattleModifiersUtil.CalculateRateOfFireModifier(rateOfFire)
            + BattleModifiersUtil.CalculateSizeModifier(Math.Max(0.1f, defender.Size))
            - defender.RangedEvasion
            - RangedRollTarget;

        // Weapon accuracy only applies when aiming, and an aim turn forgoes a volley:
        // take the better of firing every turn vs alternating aim+fire.
        float aimBonus = Math.Min(weapon.Accuracy, attacker.RangedSkill) + 1.0f;
        float hitsUnaimed = ExpectedHitsInVolley(margin, rateOfFire, weapon.Recoil);
        float hitsAimed = ExpectedHitsInVolley(margin + aimBonus, rateOfFire, weapon.Recoil);
        float hitsPerTurn = Math.Max(hitsUnaimed, hitsAimed / 2.0f);

        float killFractionPerHit = CalculateKillFractionPerHit(
            weapon.DamageMultiplier, weapon.ArmorMultiplier, weapon.WoundMultiplier, defender);

        // A volley targets one soldier (ShootAction): surplus hits are overkill.
        float killsPerVolley = Math.Min(1.0f, hitsPerTurn * killFractionPerHit);

        // Reach: longer-ranged weapons shoot while shorter ones close (30m flamer reach = baseline).
        float standoff = Math.Clamp(
            1.0f + 0.12f * (float)Math.Log(Math.Max(1.0f, weapon.MaximumRange) / 30.0f), 0.8f, 1.6f);

        // Sustained fire: fraction of turns actually shooting once reloads are cycled in.
        float volleysPerMagazine = (float)weapon.AmmoCapacity / rateOfFire;
        float sustain = volleysPerMagazine / (volleysPerMagazine + weapon.ReloadTime);

        return killsPerVolley * standoff * sustain;
    }

    private static float CalculateTemplateWeaponKillRate(
        RangedWeaponTemplate weapon,
        Input defender)
    {
        if (weapon.FuelPerBurst == 0 || weapon.AmmoCapacity == 0)
        {
            return 0;
        }

        // A template auto-hits every caught figure. The panel density represents how many
        // separate bodies a typical full-length cone covers; large solitary targets stay at one.
        float expectedVictimsPerBurst = GetPanelTemplateDensity(defender);
        float killFractionPerVictim = CalculateKillFractionPerHit(
            weapon.DamageMultiplier,
            weapon.ArmorMultiplier,
            weapon.WoundMultiplier,
            defender);
        float killsPerBurst = expectedVictimsPerBurst * killFractionPerVictim;

        // Keep the shared 30m standoff baseline; the template branch changes only how a
        // burst hits and how its fuel/reload duty cycle is calculated.
        float standoff = Math.Clamp(
            1.0f + 0.12f * (float)Math.Log(Math.Max(1.0f, weapon.MaximumRange) / 30.0f),
            0.8f,
            1.6f);
        float burstsPerTank = (float)weapon.AmmoCapacity / weapon.FuelPerBurst;
        float sustain = burstsPerTank / (burstsPerTank + weapon.ReloadTime);
        return killsPerBurst * standoff * sustain;
    }

    /// <summary>
    /// Blast (grenade/launcher) kill rate: mirrors BlastAttackAction. Everyone inside the
    /// 6m-radius circle is auto-hit with quadratic damage falloff from the impact center
    /// (averaged into <see cref="BlastAverageFalloffFactor"/>). The panel densities are
    /// shared with the cone branch: a full-reach flamer cone sweeps ~90 m^2 (30m length x
    /// 3m half-width triangle) versus the blast circle's ~113 m^2, and the cone's edge in
    /// raking a firing line is offset by the planner centering blasts on clusters
    /// (SelectBestBlastThrow), so the same bodies-per-template figures apply. Self/friendly
    /// exposure is not modeled: the planner only throws when the net score is positive and
    /// beats the soldier's best conventional action.
    /// </summary>
    private static float CalculateBlastWeaponKillRate(
        Input attacker,
        RangedWeaponTemplate weapon,
        Input defender)
    {
        if (weapon.AmmoCapacity == 0)
        {
            return 0;
        }

        float expectedVictimsPerBlast = GetPanelTemplateDensity(defender);
        float killFractionPerVictim = CalculateKillFractionPerHit(
            weapon.DamageMultiplier * BlastAverageFalloffFactor,
            weapon.ArmorMultiplier,
            weapon.WoundMultiplier,
            defender);
        float killsPerBlast = expectedVictimsPerBlast * killFractionPerVictim;

        // Thrown blasts store meters-per-Strength-point in MaximumRange, so a marine's
        // grenade reaches farther than a PDF trooper's; launched blasts use it raw.
        // Either way the shared 30m standoff baseline applies (flamer parity).
        float effectiveMaxRange = weapon.IsThrown
            ? attacker.Strength * weapon.MaximumRange
            : weapon.MaximumRange;
        float standoff = Math.Clamp(
            1.0f + 0.12f * (float)Math.Log(Math.Max(1.0f, effectiveMaxRange) / 30.0f),
            0.8f,
            1.6f);

        // Duty cycle: shots per magazine then reload. A single grenade with ReloadTime 1
        // throws every other turn at best; the 12-shell launcher reloads for 3 turns.
        int rateOfFire = Math.Max(1, (int)weapon.RateOfFire);
        float shotsPerMagazine = (float)weapon.AmmoCapacity / rateOfFire;
        float sustain = shotsPerMagazine / (shotsPerMagazine + weapon.ReloadTime);
        return killsPerBlast * standoff * sustain;
    }

    /// <summary>
    /// Expected separate bodies caught by one template against each panel profile;
    /// large solitary targets stay at one.
    /// </summary>
    private static float GetPanelTemplateDensity(Input defender)
    {
        return ReferenceEquals(defender, SwarmChaffProfile)
            ? 3.0f
            : ReferenceEquals(defender, LightInfantryProfile)
                ? 1.5f
                : 1.0f;
    }

    private static float ExpectedHitsInVolley(float margin, int rateOfFire, float recoil)
    {
        float hits = 0;
        for (int shot = 0; shot < rateOfFire; shot++)
        {
            hits += GaussianCalculator.ApproximateNormalCDF(
                (margin - shot * recoil) / RangedRollStdDev);
        }
        return hits;
    }

    // ----- melee model: mirrors MeleeAttackAction + BattleSquadPlanner strike planning -----

    private static float CalculateMeleeKillRate(Input attacker, Input defender)
    {
        MeleeWeaponTemplate primary = attacker.MeleeWeapon;
        if (primary == null && attacker.RangedWeapon == null)
        {
            primary = UnarmedFist;
        }
        if (primary == null)
        {
            return 0;
        }

        float defenderSkill = ResolveMeleeDefenseSkill(defender);
        float defenderDefenseModifier = ResolveMeleeDefenseModifier(defender);

        float primarySwings = (attacker.AttackSpeed / 10.0f) * primary.AttackSpeedMultiplier;
        float killRate = ExpectedMeleeKills(attacker, primary, primarySwings,
                                            defender, defenderSkill, defenderDefenseModifier);

        // Dual wielding: the off-hand weapon contributes exactly one extra strike.
        if (attacker.MeleeWeapon != null && attacker.SecondaryMeleeWeapon != null)
        {
            killRate += ExpectedMeleeKills(attacker, attacker.SecondaryMeleeWeapon, 1.0f,
                                           defender, defenderSkill, defenderDefenseModifier);
        }

        // Engagement ceiling: the planner spreads strikes across adjacent enemies, so kills
        // are bounded by how many enemies fit around the attacker's footprint.
        float engagementCap = 1.0f + attacker.WidthCells + attacker.DepthCells;
        killRate = Math.Min(killRate, engagementCap);

        // Melee spends turns closing under fire before it lands anything — unless the
        // attacker burrows, in which case it surfaces adjacent and swings immediately.
        float closing = attacker.CanBurrow
            ? 1.0f
            : MeleeClosingFactor * Math.Min(1.0f, attacker.MoveSpeed / MeleeClosingReferenceSpeed);
        return killRate * closing;
    }

    private static float ExpectedMeleeKills(Input attacker, MeleeWeaponTemplate weapon, float swings,
                                            Input defender, float defenderSkill, float defenderDefenseModifier)
    {
        float hitProbability = MeleeMath.CalculateContestedHitProbability(
            attacker.MeleeSkill,
            weapon.Accuracy,
            didMove: false,
            defenderSkill,
            defender.MeleeEvasion,
            defenderDefenseModifier);
        float killFractionPerHit = CalculateKillFractionPerHit(
            attacker.Strength * weapon.StrengthMultiplier,
            weapon.ArmorMultiplier, weapon.WoundMultiplier, defender);
        return swings * hitProbability * killFractionPerHit;
    }

    private static float ResolveMeleeDefenseSkill(Input defender)
    {
        if (!float.IsNaN(defender.MeleeDefenseSkill))
        {
            return defender.MeleeDefenseSkill;
        }

        // Engine: defenders parry with their best equipped melee weapon's skill; unarmed
        // defenders use the default unarmed weapon's skill, which MOS data keeps trained.
        return defender.MeleeSkill;
    }

    private static float ResolveMeleeDefenseModifier(Input defender)
    {
        if (defender.MeleeWeapon == null)
        {
            return UnarmedFist.ParryModifier;
        }

        // Defensive value comes solely from parry modifiers, summed across weapons;
        // there is no flat dual-wield defense bonus.
        float parry = defender.MeleeWeapon.ParryModifier;
        if (defender.SecondaryMeleeWeapon != null)
        {
            parry += defender.SecondaryMeleeWeapon.ParryModifier;
        }
        return parry;
    }

    // ----- shared damage model: mirrors the damage roll + flat armor + WoundResolver -----

    private static float CalculateKillFractionPerHit(float damageScale, float armorMultiplier,
                                                     float woundMultiplier, Input defender)
    {
        float effectiveArmor = Math.Max(0, defender.Armor) * armorMultiplier;
        float expectedWound = ExpectedPenetratingDamage(damageScale, effectiveArmor) * woundMultiplier;
        float effectiveHitPoints = KillThresholdConMultiple * Math.Max(1.0f, defender.Constitution);
        return Math.Min(1.0f, expectedWound / effectiveHitPoints);
    }

    /// <summary>
    /// E[max(0, damageScale * X - armor)] where X ~ N(3.5, 1.75): the expected damage
    /// that gets past flat armor, integrating over the engine's damage roll.
    /// </summary>
    private static float ExpectedPenetratingDamage(float damageScale, float effectiveArmor)
    {
        if (damageScale <= 0)
        {
            return 0;
        }

        float mean = damageScale * DamageRollMean - effectiveArmor;
        float stdDev = damageScale * DamageRollStdDev;
        float z = mean / stdDev;
        return stdDev * NormalPdf(z) + mean * GaussianCalculator.ApproximateNormalCDF(z);
    }

    private static float NormalPdf(float z)
    {
        return (float)(Math.Exp(-0.5 * z * z) / Math.Sqrt(2.0 * Math.PI));
    }
}
