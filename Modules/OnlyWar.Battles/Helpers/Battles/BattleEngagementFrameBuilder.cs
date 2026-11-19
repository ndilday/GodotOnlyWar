using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles;

/// <summary>
/// Builds both sides of the immutable Layer-1 engagement frame from the same turn-start state.
/// Every calculation here is deterministic and RNG-free.  The engagement potential that values
/// future position reads these frames and squad-level capability/removal-rate aggregates only;
/// per-soldier target selection belongs exclusively to current-turn action parity.
/// </summary>
internal static class BattleEngagementFrameBuilder
{
    private const float ScreeningForceCommitmentCap = 0.4f;
    // Units: cells/turn per cell of distance (a RATE, not a 0-1 discount -- see `closingRate`
    // below in AssignScreens). A threat closing at this rate covers a 418-cell gap in ~100 turns;
    // slower threats are not worth screening against. Distinct from every "imminence" family renamed in
    // Design/Reference/BattleLogic.md Phase 0 -- this is the one site whose quantity is
    // genuinely a rate, not a turns-based discount.
    // Keep screening active across the long approach distances used by the tactical missions:
    // the reference 418-yard engagement closes at roughly 0.014 cells per yard per turn.
    private const float MinimumScreenClosingRate = 0.01f;

    internal sealed record PairedFrame(
        IReadOnlyDictionary<int, SquadEngagementFrame> Frames,
        IReadOnlyDictionary<int, BattleSquadCapabilityProfile> Profiles);

    internal static PairedFrame Build(
        IReadOnlyCollection<BattleSquad> first,
        IReadOnlyCollection<BattleSquad> second,
        IReadOnlyDictionary<int, EngagementRoleConstraint> constraints = null)
    {
        List<BattleSquad> firstActive = Active(first);
        List<BattleSquad> secondActive = Active(second);
        // Each side's profiles are derived against the OPPOSING side: EffectiveEngagementRange
        // needs a representative target (size/armor/constitution/ranged evasion).
        Dictionary<int, BattleSquadCapabilityProfile> profiles = [];
        foreach (BattleSquad squad in firstActive)
        {
            profiles[squad.Id] = BuildProfile(squad, secondActive, firstActive);
        }
        foreach (BattleSquad squad in secondActive)
        {
            profiles[squad.Id] = BuildProfile(squad, firstActive, secondActive);
        }
        Dictionary<int, SquadEngagementFrame> frames = [];

        BuildSide(firstActive, secondActive, profiles, constraints, frames);
        BuildSide(secondActive, firstActive, profiles, constraints, frames);
        AssignScreens(firstActive, secondActive, profiles, frames);
        AssignScreens(secondActive, firstActive, profiles, frames);
        return new PairedFrame(frames, profiles);
    }

    /// <param name="opponents">
    /// The squads this profile will be scored against. Used only to derive
    /// <see cref="BattleSquadCapabilityProfile.EffectiveEngagementRange"/>; when null or empty that
    /// quantity falls back to weapon reach, which reproduces pre-Phase-2 behaviour exactly.
    /// </param>
    /// <summary>
    /// The squad's pursuit doctrine from the same melee/ranged value shares, and the same bands,
    /// as <see cref="BuildProfile"/>'s <c>IsContactSeeking</c> and <c>IsFireSupport</c> -- without
    /// the engagement-range curves, which the pursuit decision does not need.
    /// </summary>
    internal static PursuitDoctrine ClassifyPursuitDoctrine(BattleSquad squad)
    {
        float ranged = 0;
        float melee = 0;
        foreach (BattleSoldier soldier in squad?.AbleSoldiers
            .Where(soldier => soldier.TopLeft.HasValue) ?? [])
        {
            float bv = Math.Max(TacticalValueTable.MinimumValue, soldier.PlanningValue);
            (float rangedShare, float meleeShare) = SoldierCombatShares(soldier);
            melee += bv * meleeShare;
            ranged += bv * rangedShare;
        }
        if (melee + ranged <= 0) return PursuitDoctrine.Mixed;

        float effectiveMelee = melee / (melee + ranged);
        if (effectiveMelee >= BattleSquadCapabilityProfile.ContactSeekingMeleeFraction)
        {
            return PursuitDoctrine.ContactSeeking;
        }
        return effectiveMelee <= BattleSquadCapabilityProfile.FireSupportMeleeFraction
            && ranged > melee
                ? PursuitDoctrine.FireSupport
                : PursuitDoctrine.Mixed;
    }

    /// <summary>
    /// Whether a squad seeks contact, from the same value shares and band as
    /// <see cref="BattleSquadCapabilityProfile.IsContactSeeking"/>, over all its able soldiers
    /// whether placed or not: the opening range is asked before a grid exists.
    /// </summary>
    internal static bool IsContactSeekingBeforePlacement(BattleSquad squad)
    {
        float ranged = 0;
        float melee = 0;
        foreach (BattleSoldier soldier in squad?.AbleSoldiers ?? [])
        {
            float bv = Math.Max(TacticalValueTable.MinimumValue, soldier.PlanningValue);
            (float rangedShare, float meleeShare) = SoldierCombatShares(soldier);
            melee += bv * meleeShare;
            ranged += bv * rangedShare;
        }
        return melee + ranged > 0
            && melee / (melee + ranged) >= BattleSquadCapabilityProfile.ContactSeekingMeleeFraction;
    }

    /// <param name="friendlySquads">
    /// The squad's own side, itself included or not. The effective engagement range gives the
    /// squad only its share of the enemy's fire (<see cref="ForceShare"/>). Null means it fights
    /// alone.
    /// </param>
    internal static BattleSquadCapabilityProfile BuildProfile(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> opponents = null,
        IReadOnlyCollection<BattleSquad> friendlySquads = null)
    {
        List<BattleSoldier> able = squad?.AbleSoldiers
            .Where(soldier => soldier.TopLeft.HasValue)
            .OrderBy(soldier => soldier.Soldier.Id)
            .ToList() ?? [];
        if (able.Count == 0)
        {
            return new BattleSquadCapabilityProfile(
                squad?.Id ?? 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0,
                new Dictionary<int, float>());
        }

        float total = 0;
        float ranged = 0;
        float melee = 0;
        float preferredWeighted = 0;
        float preferredLowerWeighted = 0;
        float preferredWeight = 0;
        Dictionary<int, float> groups = [];
        foreach (BattleSoldier soldier in able)
        {
            float bv = Math.Max(TacticalValueTable.MinimumValue, soldier.PlanningValue);
            total += bv;
            bool usableRanged = soldier.FunctioningHands > 0
                && soldier.RangedWeapons.Any(weapon => weapon.LoadedAmmo > 0)
                && soldier.EquippedRangedWeapons.Any();
            (float rangedShare, float meleeShare) = SoldierCombatShares(soldier);
            melee += bv * meleeShare;
            ranged += bv * rangedShare;

            if (usableRanged)
            {
                float reach = soldier.EquippedRangedWeapons
                    .Where(weapon => weapon.LoadedAmmo > 0)
                    .Select(weapon => EffectiveMaximumRange(soldier, weapon))
                    .DefaultIfEmpty(0)
                    .Max();
                preferredWeighted += bv * reach;
                bool templateOnly = soldier.EquippedRangedWeapons
                    .Where(weapon => weapon.LoadedAmmo > 0)
                    .All(weapon => weapon.Template.IsTemplateWeapon);
                preferredLowerWeighted += bv * (templateOnly ? 0 : reach * 0.7f);
                preferredWeight += bv;
            }

            int groupId = soldier.Soldier.Template.Id;
            groups[groupId] = groups.GetValueOrDefault(groupId) + bv;
        }

        float effectiveMelee = melee / Math.Max(0.0001f, melee + ranged);
        float upper = preferredWeight > 0 ? preferredWeighted / preferredWeight : 1f;
        // The hit-limited maximum is the preferred standoff for non-degrading weapons. Keeping a
        // broad lower edge gives hysteresis room and avoids a one-cell advance/retreat sawtooth.
        float lower = preferredWeight > 0
            ? preferredLowerWeighted / preferredWeight
            : 0;
        int perimeter = able.Sum(soldier =>
            Math.Max(2, 2 * (soldier.Soldier.Template.Species.Width
                + soldier.Soldier.Template.Species.Depth)));
        float band = CalculateEffectiveEngagementRange(
            squad,
            opponents,
            upper,
            out float usefulFireRange,
            out float peakRemovalFraction,
            friendlySquads: friendlySquads);
        return new BattleSquadCapabilityProfile(
            squad.Id,
            total,
            ranged,
            melee,
            effectiveMelee,
            lower,
            upper,
            band,
            usefulFireRange,
            peakRemovalFraction,
            squad.GetSquadMove(),
            squad.CanRun,
            perimeter,
            groups);
    }

    /// <summary>
    /// PHASE 6 (Design/Reference/BattleLogic.md). The single place the
    /// effectiveness-derived engagement range is computed. Phase 2 built this seam; Phase 6
    /// replaced its body and, as the plan predicted, no call site changed.
    ///
    /// <para>The band is now the argmax of <c>removal(r) - incoming(r)</c>, both halves quoted in
    /// battle value per turn -- the currency Phase 5 put `outgoing` and `future` in. It is no
    /// longer an authored quantity derived from weapon reach.</para>
    /// </summary>
    /// <remarks>
    /// WHAT THE OLD BODY GOT WRONG. It was <c>min(EstimateHitDistance, EstimateKillDistance)</c>
    /// through <see cref="BattleSquad.GetPreferredEngagementRange"/>, and EstimateKillDistance
    /// returned the weapon's MaximumRange outright for a non-degrading weapon. Against a large,
    /// unarmored, non-evasive target that collapsed straight back onto reach, so the quantity was
    /// indistinguishable from PreferredBandUpper for most marine small arms -- precisely the
    /// bolter-vs-Carnifex case in the reference trace.
    ///
    /// WHY THE ANSWER IS SET BY RETURN FIRE. For a non-degrading weapon against a penetrable
    /// target, removal(r) improves monotonically as you close: the damage does not fall off, only
    /// the hit chance, so nothing about the GUN argues for standing off. What argues for standing
    /// off is what closing costs. So the standoff this produces is the range where the marginal
    /// hit-probability gained by another step forward stops paying for the enemy arriving a step
    /// sooner. That is the correct physics, and it is why the incoming half is not optional here.
    ///
    /// INCOMING has two terms, in the same shapes the planner already uses:
    /// - the opposing force's own <see cref="RangedEffectivenessCurve"/> fired at a representative
    ///   member of THIS squad, which is symmetric with our outgoing half; and
    /// - a melee arrival term, the shared contact take-out/removal rate divided by
    ///   <c>1 + turnsUntilContact</c>, matching the planner's melee exchange rate exactly at
    ///   contact and decaying with approach time. This is the ONE place a turns-based discount is
    ///   legitimate (Phase 3): a charge really does pay off later, unlike a bolt, which lands now.
    ///
    /// OUR OWN MELEE IS DELIBERATELY ABSENT from the outgoing half. The question here is where a
    /// squad wants to STAND AND SHOOT; adding a contact spike would drag every mixed squad to
    /// contact and make the band meaningless. A squad that would rather fight in melee is routed by
    /// <c>IsContactSeeking</c> in <see cref="Baseline"/>, which does not read this quantity.
    ///
    /// DEGENERATE CASES, all returning 0 -- "there is nothing to stand off for, close" -- which is
    /// the meaning 0 has always carried here: no usable ranged weapon; no opposing force; and the
    /// standing invariant case of a target that cannot be penetrated at any range, where the peak
    /// of the outgoing curve never clears
    /// <see cref="RangedEffectivenessCurve.NegligibleRemovalFraction"/>.
    /// That last one is checked EXPLICITLY rather than left to the argmax, because against an
    /// impenetrable target the net score is just <c>-incoming(r)</c>, whose argmax is maximum
    /// range -- a "preferred range" that would imply plinking from standoff is worthwhile. It is
    /// not, and the invariant says so.
    /// </remarks>
    internal static float CalculateEffectiveEngagementRange(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> opponents,
        float reach,
        bool requirePlacement = true)
        => CalculateEffectiveEngagementRange(
            squad, opponents, reach, out _, out _, requirePlacement);

    /// <param name="peakRemovalFraction">
    /// Best-case per-shooter share of one representative opponent removed per turn, maximized over
    /// every range this squad could shoot from. 0 in every degenerate case the return value is 0
    /// for. See <see cref="BattleSquadCapabilityProfile.PeakRangedRemovalFraction"/> for why the
    /// caller wants this alongside the range rather than the range alone.
    /// </param>
    internal static float CalculateEffectiveEngagementRange(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> opponents,
        float reach,
        out float peakRemovalFraction,
        bool requirePlacement = true)
        => CalculateEffectiveEngagementRange(
            squad,
            opponents,
            reach,
            out _,
            out peakRemovalFraction,
            requirePlacement);

    /// <param name="usefulFireRange">
    /// Outer range where the outgoing-removal curve clears the separately documented useful-fire
    /// criterion. Derived in the same pass as the exchange optimum and cached on the capability
    /// profile; 0 when no target-relative curve can be built or fire is ineffective at every range.
    /// </param>
    internal static float CalculateEffectiveEngagementRange(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> opponents,
        float reach,
        out float usefulFireRange,
        out float peakRemovalFraction,
        bool requirePlacement = true,
        IReadOnlyCollection<BattleSquad> friendlySquads = null)
    {
        usefulFireRange = 0;
        peakRemovalFraction = 0;
        EngagementCurves curves = BuildEngagementCurves(
            squad, opponents, reach, requirePlacement, out float degenerateRange);
        if (curves == null) return degenerateRange;

        float band = curves.Band;
        // The squad takes its share of the enemy's fire, not all of it (2026-10-11). The curves
        // price the WHOLE opposing force firing at this one squad, so a squad that is one of ten
        // found every range a loss -- least at maximum range -- and the Close continuation that
        // moves to this range never priced an advance: ten Insurrectionist Mobs stood 200 yards from
        // one Tactical squad for 400 turns, out-shot 45 to 1, where at point blank the ten would
        // have out-shot it. The same share the opening range uses (ForceShare), on the ranged fire
        // and the melee threat alike; the exchange forecast still decides whether the walk in pays.
        float incomingShare = ForceShare(squad, friendlySquads);
        float meleeThreat = curves.MeleeThreat * incomingShare;
        float speed = curves.ClosingSpeed;
        // The PEAK of the outgoing curve, not its value at `chosen`. `chosen` is the argmax of
        // removal - incoming, which degenerates to maximum range whenever the outgoing curve is
        // flat -- exactly the useless-gun case -- so reading removal there reports 0 for a good
        // rifle and a bad pistol alike. The peak asks the question the option mask actually needs
        // answered: at its very best, anywhere it could stand, is this squad's shooting worth
        // anything against this enemy?
        //
        // TWO SEPARATE SWEEPS, DELIBERATELY. The second one re-evaluates a curve the first has
        // already sampled at every coarse point, and harvesting the peak from the first sweep's
        // running max instead is an obvious-looking 34% cut in curve evaluations. It was tried on
        // 2026-08-08 and reverted: OnlyWar.Tests.Generation.ScenarioBuilderTests went from 1m17s
        // to 5m6s. One file changed, back to back, on an otherwise idle machine -- that last part
        // matters, because the same experiment run against a machine doing other work reported
        // 2.4x and was not trustworthy enough to act on. The extra sweep costs a third more
        // arithmetic and saves four times the wall clock, because what it buys is a DECISION, not
        // a number.
        //
        // WHY THE TWO SWEEPS ARE NOT INTERCHANGEABLE. They share the 33 coarse samples and differ
        // only in where the 17-point refinement goes -- this one refines around the argmax of
        // removal - incoming, a dedicated sweep around the argmax of removal. That sounds like a
        // rounding difference and is not. The coarse grid spans [1, reach], so at a bolter's 1000
        // yards its spacing is ~31 yards, and a weapon whose removal spikes near contact has its
        // whole useful band inside a cell or two. For those curves refinement is not polishing a
        // peak the sweep already found, it is the only thing that finds it, and the harvested max
        // comes out first-order low rather than O(step^2) low. peakRemovalFraction then feeds
        // BattleSquadPlanner.HasNoViableRangedOption, which is a threshold test, so a first-order
        // error there flips squads between engagement postures.
        // Outgoing and incoming are both tactical value, so they net directly.
        float chosen = Math.Clamp(
            RangedEffectivenessCurve.Argmax(
                band,
                range => curves.Outgoing.RemovalAt(range)
                    - (incomingShare * curves.Incoming.RemovalAt(range))
                    - (meleeThreat
                        / (1f + (Math.Max(0f, range - ContactGap) / speed)))),
            0,
            band);
        float outgoingPeak = curves.Outgoing.RemovalAt(
            RangedEffectivenessCurve.Argmax(band, curves.Outgoing.RemovalAt));
        usefulFireRange = Math.Min(band, curves.Outgoing.UsefulFireRange(outgoingPeak));
        // RemovalAt is a squad total in battle value; divide out both the shooter count and the
        // representative opponent's worth to land on "share of one enemy, per shooter, per turn"
        // -- the scale NegligibleRemovalFraction is quoted on.
        peakRemovalFraction = outgoingPeak
            / (Math.Max(1, curves.OurSoldierCount)
                * Math.Max(TacticalValueTable.MinimumValue, curves.Target.BattleValue));
        return chosen;
    }

    /// <summary>
    /// Everything both range questions are asked of: this squad's outgoing curve, the opposing
    /// force's return fire and melee threat, and how fast that force closes. Built once so the
    /// mid-fight snapshot (<see cref="CalculateEffectiveEngagementRange"/>) and the opening-range
    /// integral (<see cref="CalculatePreferredOpeningRange"/>) cannot drift apart in WHAT they
    /// model while deliberately differing in WHICH QUESTION they ask of it.
    /// </summary>
    /// <param name="OutgoingSaturationRange">
    /// The outer edge of the outgoing curve's useful band, at
    /// <see cref="RangedEffectivenessCurve.SaturationFraction"/>. Carried on the record because
    /// <see cref="BuildEngagementCurves"/> must compute it anyway to apply the standing invariant
    /// guard, and <see cref="CalculatePreferredOpeningRange"/> then wants the same number for its
    /// floor. Recomputing it there cost a second full 50-sample sweep of the same curve for an
    /// answer that cannot have changed -- nothing between the two mutates the curve.
    /// </param>
    /// <summary>
    /// Diagnostics: the terms <see cref="CalculateEffectiveEngagementRange"/> maximizes, sampled
    /// at the given ranges -- our outgoing removal converted to our currency, our share of the
    /// opposing force's return fire, and our share of its melee arrival threat -- plus the band the
    /// argmax runs over. Null in the degenerate cases.
    /// </summary>
    internal static (float Band, IReadOnlyList<(float Range, float Outgoing, float Incoming, float MeleeThreat)> Samples)?
        DescribeEngagementCurve(
            BattleSquad squad,
            IReadOnlyCollection<BattleSquad> opponents,
            float reach,
            IReadOnlyList<float> ranges,
            IReadOnlyCollection<BattleSquad> friendlySquads = null)
    {
        EngagementCurves curves = BuildEngagementCurves(squad, opponents, reach, true, out _);
        if (curves == null) return null;
        float share = ForceShare(squad, friendlySquads);
        List<(float, float, float, float)> samples = [];
        foreach (float range in ranges)
        {
            samples.Add((
                range,
                curves.Outgoing.RemovalAt(range),
                share * curves.Incoming.RemovalAt(range),
                share * curves.MeleeThreat
                    / (1f + (Math.Max(0f, range - ContactGap) / curves.ClosingSpeed))));
        }
        return (curves.Band, samples);
    }

    private sealed record EngagementCurves(
        RangedEffectivenessCurve Outgoing,
        RangedEffectivenessCurve Incoming,
        EngagementTargetProfile Target,
        float Band,
        float MeleeThreat,
        float ClosingSpeed,
        float OpposingBattleValue,
        float OurBattleValue,
        int OurSoldierCount,
        float OutgoingSaturationRange);

    /// <param name="degenerateRange">
    /// What the caller must return when this yields null: <paramref name="reach"/> for "there is
    /// no opposition to derive anything against", 0 for the standing-invariant cases (no usable
    /// ranged weapon, nothing worth shooting at any range) whose established meaning is "there is
    /// nothing to stand off for, close".
    /// </param>
    private static EngagementCurves BuildEngagementCurves(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> opponents,
        float reach,
        bool requirePlacement,
        out float degenerateRange)
    {
        degenerateRange = reach;
        if (squad == null) return null;
        List<BattleSquad> live = (opponents ?? [])
            .Where(opponent => opponent != null && opponent.AbleSoldiers.Count > 0)
            .OrderBy(opponent => opponent.Id)
            .ToList();
        if (live.Count == 0) return null;

        float weight = 0;
        float size = 0;
        float armor = 0;
        float constitution = 0;
        float evasion = 0;
        float battleValue = 0;
        float closingSpeed = 0;
        List<BattleSoldier> opposingSoldiers = [];
        foreach (BattleSquad opponent in live)
        {
            float opponentWeight = opponent.AbleSoldiers.Count;
            weight += opponentWeight;
            size += opponentWeight * opponent.GetAverageSize();
            armor += opponentWeight * opponent.GetAverageArmor();
            constitution += opponentWeight * opponent.GetAverageConstitution();
            evasion += opponentWeight * opponent.GetAverageRangedEvasion();
            closingSpeed = Math.Max(closingSpeed, opponent.GetSquadMove());
            foreach (BattleSoldier soldier in opponent.AbleSoldiers
                .Where(member => !requirePlacement || member.TopLeft.HasValue)
                .OrderBy(member => member.Soldier.Id))
            {
                opposingSoldiers.Add(soldier);
                float bv = Math.Max(TacticalValueTable.MinimumValue, soldier.PlanningValue);
                battleValue += bv;
            }
        }
        if (weight <= 0) return null;

        EngagementTargetProfile them = new(
            size / weight,
            armor / weight,
            constitution / weight,
            evasion / weight,
            opposingSoldiers.Count > 0 ? battleValue / opposingSoldiers.Count : 1f);
        List<BattleSoldier> ourSoldiers = squad.AbleSoldiers
            .Where(member => !requirePlacement || member.TopLeft.HasValue)
            .OrderBy(member => member.Soldier.Id)
            .ToList();
        RangedEffectivenessCurve outgoing = RangedEffectivenessCurve.Build(ourSoldiers, them);
        float band = Math.Max(0, Math.Min(reach, outgoing.Reach));
        degenerateRange = 0;
        if (outgoing.IsEmpty || band <= 0)
        {
            return null;
        }
        // Invariant guard: if nothing on the outgoing curve is worth shooting, no standoff is.
        float saturationRange =
            outgoing.SaturationRange(RangedEffectivenessCurve.SaturationFraction);
        if (saturationRange <= 0)
        {
            return null;
        }

        EngagementTargetProfile us = RepresentativeProfile(ourSoldiers);
        RangedEffectivenessCurve incomingRanged =
            RangedEffectivenessCurve.Build(opposingSoldiers, us);
        float meleeRemovalRate = MeleeStrikeEstimator.EstimateContactRemovalRate(
            opposingSoldiers,
            ourSoldiers);
        return new EngagementCurves(
            outgoing,
            incomingRanged,
            them,
            band,
            meleeRemovalRate,
            Math.Max(0.1f, closingSpeed),
            battleValue,
            ourSoldiers.Sum(soldier => Math.Max(TacticalValueTable.MinimumValue, soldier.PlanningValue)),
            ourSoldiers.Count,
            saturationRange);
    }

    // How close the two forces are when the shooting phase ends and the melee begins. Shared with
    // the mid-fight melee arrival term so "turns until contact" means one thing in this file.
    private const float ContactGap = 1.5f;

    /// <summary>
    /// The range at which a squad wants an engagement to OPEN against a specific opposing force,
    /// asked BEFORE a grid exists.
    ///
    /// <para>THIS IS NOT THE MID-FIGHT BAND, and conflating the two is what produced the Xibarrus
    /// Zeta ambush (2026-08-04): three marine squads sprang an ambush on a Broodlord, three Tyrant
    /// Guard and a Carnifex at ONE YARD and lost 29 of 30 marines.
    /// <see cref="CalculateEffectiveEngagementRange"/> maximizes a per-turn RATE at a single range,
    /// <c>outgoing(r) - incoming(r) - meleeThreat/(1 + turnsToContact(r))</c>. Against a melee-only
    /// enemy the incoming curve is identically zero and every remaining term DECREASES with range:
    /// measured against that force, outgoing ran 75.5 battle value per turn at 1 yard down to 8.2 at
    /// 800, while the melee term peaked at 20.54 and decayed hyperbolically. The net is monotone
    /// decreasing, so the argmax is the lower bound. No loadout changes that -- the shape is the
    /// same for bolters, plasma and heavy weapons alike.</para>
    ///
    /// <para>THE ANSWER IS THE OPENING FORECAST'S (2026-10-24, Design/Reference/ForecastCalibration.md section 1,
    /// phase 5): the opening whose forecast fight this squad likes best, see
    /// <see cref="CalculateForecastOpeningRange"/>. It replaced the shortest-sufficient sweep, which
    /// summed the approach fire up to an assumed break share and floored the answer by the useful
    /// band, a retreat reserve and a minimum of three turns of fire. Against the measured approach
    /// curves the sweep put a PDF line 26-28 yards from Hormagaunts (best measured: 450) and a
    /// Tactical squad 761 yards from PDF (best: 450); the forecast is within one ladder step of the
    /// measured best for every panel side but the flat-curved Carnifex.</para>
    /// </summary>
    /// <remarks>
    /// A squad with no usable ranged weapon has no preference (null from
    /// <see cref="CalculatePreferredOpeningRangeOrNone"/>), read here as 0: open in contact.
    /// </remarks>
    /// <param name="friendlySquads">
    /// The squad's whole force, itself included or not. The squad plans only its own part of the
    /// fight: its proportional share of the enemy, and its proportional share of the enemy's fire
    /// (see <see cref="ForceShare"/>). Null or empty means the squad fights alone.
    /// </param>
    internal static float CalculatePreferredOpeningRange(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> opposingSquads,
        IReadOnlyCollection<BattleSquad> friendlySquads = null)
        => CalculateForecastOpeningRange(squad, opposingSquads, friendlySquads) ?? 0f;

    /// <summary>
    /// The preferred opening range, or null when the squad has no preference: a squad with no
    /// ranged weapon has nothing to stand off for (Design/Reference/ForecastCalibration.md section 1), and a
    /// reciprocal assault must not let it pull the opening (section 3, rule B).
    /// </summary>
    internal static float? CalculatePreferredOpeningRangeOrNone(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> opposingSquads,
        IReadOnlyCollection<BattleSquad> friendlySquads = null)
        => CalculateForecastOpeningRange(squad, opposingSquads, friendlySquads);

    // Candidate openings for the forecast argmax: log-spaced from the shortest to the squad's
    // reach, then one refinement between the best candidate's neighbours.
    private const float ForecastOpeningMinimum = 1.5f;
    private const int ForecastOpeningCandidates = 32;
    private const int ForecastOpeningRefinement = 9;
    // The indifference band of the argmax, as a share of the squad's own battle value: an
    // opening within this of the best net is as good, and the shortest such opening wins, because
    // a longer battle for the same result is worse.
    private const float ForecastOpeningIndifferenceShare = 0.01f;

    /// <summary>
    /// The opening whose forecast this squad likes best (Design/Reference/ForecastCalibration.md section 1,
    /// section 2): the argmax over candidate openings of
    /// <see cref="OpeningExchangeForecast.Net"/>, taking the shortest opening within
    /// <see cref="ForecastOpeningIndifferenceShare"/> of the best, on the expected path AND on the
    /// unlucky one (<see cref="OpeningExchangeForecast.EvaluateUnlucky"/>). Null for a squad with
    /// no usable ranged weapon: it has nothing to stand off for, which is no preference, not a
    /// preference for contact.
    /// </summary>
    /// <remarks>
    /// THE UNLUCKY PATH (2026-10-24). A flat plateau's shortest point is its edge, where a little
    /// bad luck turns "they die before they arrive" into contact. One bolter squad against five
    /// lone monsters (the Xibarrus Zeta fixture) expected to kill them all from 24 yards out or
    /// more and lose 0.5 battle value at 9, so the expected argmax opened at 9 yards: the opening
    /// at which 29 of 30 marines died in the 2026-08-04 battle. On the unlucky path 9 yards loses
    /// heavily and the plateau starts farther out.
    /// </remarks>
    internal static float? CalculateForecastOpeningRange(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> opposingSquads,
        IReadOnlyCollection<BattleSquad> friendlySquads = null,
        OpeningForecastOptions options = null)
    {
        OpeningExchangeForecast forecast = OpeningExchangeForecast.Build(
            squad, opposingSquads, friendlySquads, options);
        if (forecast == null)
        {
            return null;
        }
        if (!forecast.HasRangedWeapon || forecast.Reach <= ForecastOpeningMinimum)
        {
            return null;
        }

        float low = ForecastOpeningMinimum;
        float high = forecast.Reach;
        List<(float Range, float Net)> scored = [];
        for (int index = 0; index < ForecastOpeningCandidates; index++)
        {
            float range = low * MathF.Pow(high / low, index / (float)(ForecastOpeningCandidates - 1));
            scored.Add((range, OpeningExchangeForecast.Net(forecast.Evaluate(range))));
        }
        int best = 0;
        for (int index = 1; index < scored.Count; index++)
        {
            if (scored[index].Net > scored[best].Net)
            {
                best = index;
            }
        }
        float refineLow = scored[Math.Max(0, best - 1)].Range;
        float refineHigh = scored[Math.Min(scored.Count - 1, best + 1)].Range;
        for (int index = 1; index < ForecastOpeningRefinement; index++)
        {
            float range = refineLow + ((refineHigh - refineLow) * index / ForecastOpeningRefinement);
            scored.Add((range, OpeningExchangeForecast.Net(forecast.Evaluate(range))));
        }

        float bestNet = scored.Max(candidate => candidate.Net);
        float tolerance = ForecastOpeningIndifferenceShare * Math.Max(1f, forecast.OwnPool);
        List<(float Range, float Net)> plateau = scored
            .Where(candidate => candidate.Net >= bestNet - tolerance)
            .Select(candidate => (candidate.Range,
                OpeningExchangeForecast.Net(forecast.EvaluateUnlucky(candidate.Range))))
            .ToList();
        float bestUnlucky = plateau.Max(candidate => candidate.Item2);
        return plateau
            .Where(candidate => candidate.Item2 >= bestUnlucky - tolerance)
            .Min(candidate => candidate.Range);
    }

    /// <summary>
    /// This squad's share of its force's battle value, in (0, 1]. The opening-range question is
    /// asked once per squad against the WHOLE opposing force, so without this every squad planned
    /// as if it alone had to remove the enemy's entire target share, under the enemy's entire
    /// fire. Observed 2026-09-22 (Grist Nine Epsilon, 33 marine squads against 453 orks): no one
    /// squad could reach the kill target, so it never bound, and the force opened at ~740. With
    /// the share, a squad plans its proportional part of the kill, and takes its proportional part
    /// of the enemy's fire -- as if the enemy spreads its fire across the force by value.
    /// </summary>
    internal static float ForceShare(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> friendlySquads)
    {
        float own = SquadBattleValue(squad);
        float total = (friendlySquads ?? [])
            .Where(friendly => friendly != null && friendly.Id != squad.Id)
            .DistinctBy(friendly => friendly.Id)
            .Sum(SquadBattleValue)
            + own;
        return total > 0 ? Math.Clamp(own / total, 0f, 1f) : 1f;
    }

    private static float SquadBattleValue(BattleSquad squad) => squad.AbleSoldiers
        .Sum(soldier => (float)Math.Max(TacticalValueTable.MinimumValue, soldier.PlanningValue));


    /// <summary>
    /// A representative member of a force, for the OTHER side's curve to shoot at. Able-soldier
    /// mean of the same four scalars, plus mean battle value so the two curves are commensurable.
    /// </summary>
    internal static EngagementTargetProfile RepresentativeProfile(
        IReadOnlyList<BattleSoldier> soldiers)
    {
        if (soldiers.Count == 0) return new EngagementTargetProfile(1, 0, 1, 0, 1);
        float size = 0;
        float armor = 0;
        float constitution = 0;
        float evasion = 0;
        float battleValue = 0;
        foreach (BattleSoldier soldier in soldiers)
        {
            size += soldier.Soldier.Size;
            armor += soldier.Armor?.Template.ArmorProvided ?? 0;
            constitution += RemovalMath.RepresentativeConstitution(soldier);
            evasion += soldier.Soldier.Template.Species.RangedEvasion;
            battleValue += Math.Max(TacticalValueTable.MinimumValue, soldier.PlanningValue);
        }
        return new EngagementTargetProfile(
            size / soldiers.Count,
            armor / soldiers.Count,
            constitution / soldiers.Count,
            evasion / soldiers.Count,
            battleValue / soldiers.Count);
    }

    /// <summary>
    /// The ranged/melee split of one soldier's value, so
    /// <see cref="CalculateEffectiveEngagementRange"/> can price an opposing force's melee threat
    /// before that force's own profiles have been built.
    ///
    /// <para>FROM ARMAMENT AND TARGET (2026-11-01, Design/Reference/BattleValueCalibration.md
    /// section 3). The split is the soldier's tactical melee share: his melee take-outs at contact
    /// against the enemy on the field over those plus his ranged take-outs across his reach. It
    /// replaced <c>SoldierTemplate.MeleeFraction</c>, one authored number per template, which made
    /// every Ork Boy squad (shoota or choppa) a 0.5 and so Slugga Boyz never sought contact. A
    /// soldier no table has seen (a coordinator-free fixture) is split by armament alone, the old
    /// rule for an unauthored template.</para>
    /// </summary>
    private static (float Ranged, float Melee) SoldierCombatShares(BattleSoldier soldier)
    {
        bool usableRanged = soldier.FunctioningHands > 0
            && soldier.RangedWeapons.Any(weapon => weapon.LoadedAmmo > 0)
            && soldier.EquippedRangedWeapons.Any();
        bool usableMelee = soldier.FunctioningHands > 0
            && (soldier.MeleeWeapons.Count > 0 || soldier.RangedWeapons.Count == 0);
        float meleeShare;
        if (soldier.TacticalMeleeShare is float measured)
        {
            meleeShare = measured;
        }
        else
        {
            float bestRangedReach = usableRanged
                ? soldier.EquippedRangedWeapons
                    .Where(weapon => weapon.LoadedAmmo > 0)
                    .Select(weapon => EffectiveMaximumRange(soldier, weapon))
                    .DefaultIfEmpty(0)
                    .Max()
                : 0;
            meleeShare = usableMelee && !usableRanged ? 0.9f
                : usableMelee && bestRangedReach <= 50 ? 0.7f
                : usableMelee && usableRanged ? 0.25f
                : 0.05f;
        }
        return (
            usableRanged ? 1f - meleeShare : 0,
            usableMelee ? meleeShare : 0);
    }

    private static float EffectiveMaximumRange(BattleSoldier soldier, RangedWeapon weapon)
    {
        if (weapon.Template.IsThrown)
        {
            return soldier.Soldier.Strength * weapon.Template.MaximumRange;
        }
        return weapon.Template.MaximumRange;
    }

    private static void BuildSide(
        IReadOnlyList<BattleSquad> friendly,
        IReadOnlyList<BattleSquad> enemy,
        IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
        IReadOnlyDictionary<int, EngagementRoleConstraint> constraints,
        IDictionary<int, SquadEngagementFrame> frames)
    {
        foreach (BattleSquad squad in friendly)
        {
            EngagementRoleConstraint constraint = constraints?.GetValueOrDefault(squad.Id)
                ?? new EngagementRoleConstraint(EngagementSquadRole.Normal);
            Dictionary<int, float> raw = [];
            foreach (BattleSquad target in enemy)
            {
                float distance = MinimumDistance(squad, target);
                float targetBv = profiles[target.Id].TotalAbleBattleValue;
                // ProximityWeight: pure inverse distance, no speed or preferred-range input at
                // all. Feeds PairWeights (target-selection weighting), not a turns-based discount
                // like the "imminence" family in BattleSquadPlanner -- see
                // Design/Reference/BattleLogic.md Phase 0.
                float proximityWeight = 1f / Math.Max(1, distance);
                raw[target.Id] = proximityWeight * (float)Math.Sqrt(Math.Max(1, targetBv));
            }
            float totalWeight = raw.Values.Sum();
            Dictionary<int, float> weights = raw
                .OrderBy(entry => entry.Key)
                .ToDictionary(
                    entry => entry.Key,
                    entry => totalWeight > 0 ? entry.Value / totalWeight : 0);
            // During an organized withdrawal, Bound squads cannot return fire while the Cover or
            // RearGuard can. Preserve the pursuit policy of closing on the formation that is
            // actively protecting the withdrawal; otherwise use the normal highest-ProximityWeight
            // counterpart. Read the paired current-turn role constraints rather than mutable
            // BattleSquad role state so both sides use the same frozen declaration snapshot.
            HashSet<int> coveringTargetIds = (constraint.Role
                    is EngagementSquadRole.Pursuit
                        or EngagementSquadRole.Follow
                        or EngagementSquadRole.Press
                        or EngagementSquadRole.Standoff)
                ? enemy.Where(target => constraints?.GetValueOrDefault(target.Id)?.Role is
                        EngagementSquadRole.Cover or EngagementSquadRole.RearGuard)
                    .Select(target => target.Id)
                    .ToHashSet()
                : [];
            IEnumerable<KeyValuePair<int, float>> primaryCandidates = coveringTargetIds.Count > 0
                ? weights.Where(entry => coveringTargetIds.Contains(entry.Key))
                : weights;
            int? primary = primaryCandidates
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .Select(entry => (int?)entry.Key)
                .FirstOrDefault();
            EngagementOptionKind baseline = Baseline(
                squad,
                primary.HasValue ? enemy.First(target => target.Id == primary.Value) : null,
                profiles[squad.Id]);
            EngagementSquadRole primaryRole = primary.HasValue
                ? constraints?.GetValueOrDefault(primary.Value)?.Role
                    ?? EngagementSquadRole.Normal
                : EngagementSquadRole.Normal;
            // Pursuit scoring must use the speed of the quarry this squad actually selected. The
            // old force minimum could belong to an entirely different withdrawing squad, making a
            // jog look able to hold a gap that its real target was opening.
            float quarryRunSpeed = (constraint.Role
                    is EngagementSquadRole.Pursuit
                        or EngagementSquadRole.Follow
                        or EngagementSquadRole.Press
                        or EngagementSquadRole.Standoff)
                && primary.HasValue
                    ? primaryRole switch
                    {
                        EngagementSquadRole.Bound or EngagementSquadRole.Routing =>
                            profiles[primary.Value].CanRun
                                ? profiles[primary.Value].MoveSpeed
                                : profiles[primary.Value].MoveSpeed
                                    * SoldierMovementPlanner.JogSpeedMultiplier,
                        EngagementSquadRole.Cover or EngagementSquadRole.RearGuard => 0,
                        _ => constraint.QuarryRunSpeed
                    }
                    : 0;
            frames[squad.Id] = new SquadEngagementFrame(
                squad.Id,
                constraint.Role,
                null,
                null,
                null,
                primary,
                weights,
                baseline,
                constraint.FixedHeading,
                quarryRunSpeed);
        }
    }

    /// <summary>
    /// The default posture a squad falls back to. Reads PreferredBandUpper/Lower, i.e. REACH, and
    /// PHASE 6 RE-CHECKED THAT AND LEFT IT. Baseline answers "am I roughly in the fight", a
    /// containment question with a wide hysteresis band, and it is the fallback the scored options
    /// are compared against -- feeding it the sharply-derived
    /// <see cref="BattleSquadCapabilityProfile.EffectiveEngagementRange"/> would make the FALLBACK
    /// carry the derivation and then have the engagement score rate movement relative to it, double
    /// counting the same judgement. The derived band belongs in the score, not in the default.
    /// </summary>
    private static EngagementOptionKind Baseline(
        BattleSquad squad,
        BattleSquad target,
        BattleSquadCapabilityProfile profile)
    {
        if (target == null) return EngagementOptionKind.Hold;
        float distance = MinimumDistance(squad, target);
        if (profile.IsContactSeeking) return distance <= 1
            ? EngagementOptionKind.Hold
            : EngagementOptionKind.CloseToContact;
        // Beyond its own reach a squad is not in the fight at all, so the containment answer is
        // "close, fast". Jogging was the fallback until 2026-09-20, and it only ever showed when
        // the scored options sat inside the indifference band -- which is exactly the far-away
        // case, where every option is worth about the same and the baseline decides. A jog does
        // not merely close more slowly: against a quarry of equal speed it does not close at all.
        if (distance > profile.PreferredBandUpper + 1)
        {
            return profile.CanRun
                ? EngagementOptionKind.RunToward
                : EngagementOptionKind.JogToward;
        }
        // Inside its reach a squad holds. There was a StepBack branch here, inside the band's lower
        // edge (70% of the maximum weapon range), written when MaximumRange was a realistic reach.
        // Since accuracy falloff limits range and MaximumRange became a 1,000-yard cap, that edge
        // sat near 700 yards, so nearly every rifle squad defaulted to stepping back. The default
        // only breaks ties between options the score cannot separate, and those were the squads
        // losing a firefight at every range: PDF facing marines at 200 yards walked away a yard a
        // turn, unable to shoot, until they routed (2026-10-10). Whether to open the range is a
        // judgement the score makes; the default should not make it for the squad.
        return EngagementOptionKind.Hold;
    }

    private static void AssignScreens(
        IReadOnlyList<BattleSquad> friendly,
        IReadOnlyList<BattleSquad> enemy,
        IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
        Dictionary<int, SquadEngagementFrame> frames)
    {
        if (friendly.Count < 2) return;
        float forceBv = friendly.Sum(squad => profiles[squad.Id].TotalAbleBattleValue);
        float committed = 0;
        HashSet<int> assignedScreeners = [];
        var candidates =
            from threat in enemy
            let threatProfile = profiles[threat.Id]
            where threatProfile.IsContactSeeking
            from principal in friendly
            let protectedProfile = profiles[principal.Id]
            where protectedProfile.IsFireSupport
            let threatDistance = MinimumDistance(threat, principal)
            // ClosingRate: cells/turn per cell of distance -- a RATE, compared directly against
            // the MinimumScreenClosingRate threshold, not a 0-1 discount like the "imminence"
            // family in BattleSquadPlanner (see Design/Reference/BattleLogic.md Phase
            // 0). Uses the threat's own speed.
            let closingRate = threatProfile.MoveSpeed / Math.Max(1, threatDistance)
            where closingRate >= MinimumScreenClosingRate
            from screener in friendly
            let screenerProfile = profiles[screener.Id]
            where screener.Id != principal.Id
                && screenerProfile.EffectiveMeleeFraction > protectedProfile.EffectiveMeleeFraction
            let preventable = Math.Min(threatProfile.UsableMeleeBattleValue,
                protectedProfile.TotalAbleBattleValue) * closingRate
            let incumbent = screener.LastScreenThreatSquadId == threat.Id
                && screener.LastProtectedSquadId == principal.Id
            orderby preventable descending, closingRate descending, incumbent descending,
                threat.Id, screener.Id
            select new { threat, principal, screener, preventable };

        foreach (var candidate in candidates)
        {
            if (assignedScreeners.Contains(candidate.screener.Id)) continue;
            float cost = profiles[candidate.screener.Id].TotalAbleBattleValue;
            if (committed + cost > forceBv * ScreeningForceCommitmentCap) continue;
            SquadEngagementFrame incumbent = frames[candidate.screener.Id];
            // Withdrawal/pursuit constraints own the option mask and are never overwritten by a
            // normal-force screening assignment.
            if (incumbent.Role != EngagementSquadRole.Normal) continue;

            (float protectedX, float protectedY) = Centroid(candidate.principal);
            (float threatX, float threatY) = Centroid(candidate.threat);
            float protectedStep = profiles[candidate.principal.Id].MoveSpeed * 0.5f;
            float dx = protectedX - threatX;
            float dy = protectedY - threatY;
            float length = Math.Max(1f, (float)Math.Sqrt(dx * dx + dy * dy));
            float projectedProtectedX = protectedX + (dx / length * protectedStep);
            float projectedProtectedY = protectedY + (dy / length * protectedStep);
            ValueTuple<float, float> interpose = (
                (projectedProtectedX + threatX) * 0.5f,
                (projectedProtectedY + threatY) * 0.5f);
            frames[candidate.screener.Id] = incumbent with
            {
                ProtectedSquadId = candidate.principal.Id,
                ScreenThreatSquadId = candidate.threat.Id,
                InterposePoint = interpose
            };
            assignedScreeners.Add(candidate.screener.Id);
            committed += cost;
        }
    }

    internal static float MinimumDistance(BattleSquad first, BattleSquad second)
    {
        return first.AbleSoldiers
            .Where(soldier => soldier.TopLeft.HasValue)
            .SelectMany(a => second.AbleSoldiers
                .Where(soldier => soldier.TopLeft.HasValue)
                .Select(b => Distance(a.TopLeft.Value, b.TopLeft.Value)))
            .DefaultIfEmpty(float.MaxValue)
            .Min();
    }

    internal static (float X, float Y) Centroid(BattleSquad squad)
    {
        List<BattleSoldier> placed = squad.AbleSoldiers
            .Where(soldier => soldier.TopLeft.HasValue)
            .ToList();
        return placed.Count == 0
            ? (0, 0)
            : ((float)placed.Average(soldier => soldier.TopLeft.Value.Item1),
                (float)placed.Average(soldier => soldier.TopLeft.Value.Item2));
    }

    private static float Distance(ValueTuple<int, int> a, ValueTuple<int, int> b)
    {
        int dx = a.Item1 - b.Item1;
        int dy = a.Item2 - b.Item2;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private static List<BattleSquad> Active(IReadOnlyCollection<BattleSquad> squads) =>
        (squads ?? [])
            .Where(squad => squad != null
                && squad.Status == BattleSquadStatus.Active
                && squad.AbleSoldiers.Count > 0)
            .OrderBy(squad => squad.Id)
            .ToList();
}
