using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles;

/// <summary>
/// Builds both sides of the immutable Layer-1 engagement frame from the same turn-start state.
/// Every calculation here is deterministic and RNG-free.  Future rollout consumes capability
/// groups only; per-soldier target selection belongs exclusively to current-turn action parity.
/// </summary>
internal static class BattleEngagementFrameBuilder
{
    private const float ScreeningForceCommitmentCap = 0.4f;
    // Units: cells/turn per cell of distance (a RATE, not a 0-1 discount -- see `closingRate`
    // below in AssignScreens). A threat closing at this rate covers a 418-cell gap in ~100 turns;
    // slower threats are not worth screening against. Distinct from every "imminence" family renamed in
    // Design/Reference/EngagementScoringOverhaul.md Phase 0 -- this is the one site whose quantity is
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
            profiles[squad.Id] = BuildProfile(squad, secondActive);
        }
        foreach (BattleSquad squad in secondActive)
        {
            profiles[squad.Id] = BuildProfile(squad, firstActive);
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
    internal static BattleSquadCapabilityProfile BuildProfile(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> opponents = null)
    {
        List<BattleSoldier> able = squad?.AbleSoldiers
            .Where(soldier => soldier.TopLeft.HasValue)
            .OrderBy(soldier => soldier.Soldier.Id)
            .ToList() ?? [];
        if (able.Count == 0)
        {
            return new BattleSquadCapabilityProfile(
                squad?.Id ?? 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<int, float>());
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
            float bv = Math.Max(1, soldier.Soldier.Template.BattleValue);
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
            squad, opponents, upper, out float peakRemovalFraction);
        return new BattleSquadCapabilityProfile(
            squad.Id,
            total,
            ranged,
            melee,
            effectiveMelee,
            lower,
            upper,
            band,
            peakRemovalFraction,
            squad.GetSquadMove(),
            perimeter,
            groups);
    }

    /// <summary>
    /// PHASE 6 (Design/Reference/EngagementScoringOverhaul.md). The single place the
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
    /// - a melee arrival term, <c>meleeBV * MeleeContactRemovalFraction / (1 + turnsUntilContact)</c>,
    ///   matching <c>BattleSquadPlanner.MeleeRemovalRate</c> exactly at contact and decaying with
    ///   approach time. This is the ONE place a turns-based discount is legitimate (Phase 3): a
    ///   charge really does pay off later, unlike a bolt, which lands now.
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
            squad, opponents, reach, out _, requirePlacement);

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
    {
        peakRemovalFraction = 0;
        EngagementCurves curves = BuildEngagementCurves(
            squad, opponents, reach, requirePlacement, out float degenerateRange);
        if (curves == null) return degenerateRange;

        float band = curves.Band;
        float meleeThreat = curves.MeleeThreat;
        float speed = curves.ClosingSpeed;
        float chosen = Math.Clamp(
            RangedEffectivenessCurve.Argmax(
                band,
                range => curves.Outgoing.RemovalAt(range)
                    - curves.Incoming.RemovalAt(range)
                    - (meleeThreat
                        / (1f + (Math.Max(0f, range - ContactGap) / speed)))),
            0,
            band);
        // Sampled at the curve's PEAK, not at `chosen`. `chosen` is the argmax of
        // removal - incoming, which degenerates to maximum range whenever the outgoing curve is
        // flat -- exactly the useless-gun case -- so reading removal there reports 0 for a good
        // rifle and a bad pistol alike. The peak asks the question the option mask actually needs
        // answered: at its very best, anywhere it could stand, is this squad's shooting worth
        // anything against this enemy?
        //
        // RemovalAt is a squad total in battle value; divide out both the shooter count and the
        // representative opponent's worth to land on "share of one enemy, per shooter, per turn"
        // -- the scale NegligibleRemovalFraction is quoted on.
        float peak = RangedEffectivenessCurve.Argmax(band, curves.Outgoing.RemovalAt);
        peakRemovalFraction = curves.Outgoing.RemovalAt(peak)
            / (Math.Max(1, curves.OurSoldierCount) * Math.Max(1f, curves.Target.BattleValue));
        return chosen;
    }

    /// <summary>
    /// Everything both range questions are asked of: this squad's outgoing curve, the opposing
    /// force's return fire and melee threat, and how fast that force closes. Built once so the
    /// mid-fight snapshot (<see cref="CalculateEffectiveEngagementRange"/>) and the opening-range
    /// integral (<see cref="CalculatePreferredOpeningRange"/>) cannot drift apart in WHAT they
    /// model while deliberately differing in WHICH QUESTION they ask of it.
    /// </summary>
    private sealed record EngagementCurves(
        RangedEffectivenessCurve Outgoing,
        RangedEffectivenessCurve Incoming,
        EngagementTargetProfile Target,
        float Band,
        float MeleeThreat,
        float ClosingSpeed,
        float OpposingBattleValue,
        float OurBattleValue,
        int OurSoldierCount);

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
        float meleeBattleValue = 0;
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
                float bv = Math.Max(1, soldier.Soldier.Template.BattleValue);
                battleValue += bv;
                meleeBattleValue += bv * SoldierCombatShares(soldier).Melee;
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
        if (outgoing.SaturationRange(RangedEffectivenessCurve.SaturationFraction) <= 0)
        {
            return null;
        }

        EngagementTargetProfile us = RepresentativeProfile(ourSoldiers);
        RangedEffectivenessCurve incomingRanged =
            RangedEffectivenessCurve.Build(opposingSoldiers, us);
        return new EngagementCurves(
            outgoing,
            incomingRanged,
            them,
            band,
            meleeBattleValue * BattleModifiersUtil.MeleeContactRemovalFraction,
            Math.Max(0.1f, closingSpeed),
            battleValue,
            ourSoldiers.Sum(soldier => Math.Max(1, soldier.Soldier.Template.BattleValue)),
            ourSoldiers.Count);
    }

    // How close the two forces are when the shooting phase ends and the melee begins. Shared with
    // the mid-fight melee arrival term so "turns until contact" means one thing in this file.
    private const float ContactGap = 1.5f;
    // Ceiling on the approach integral below, so a force that closes at a crawl (or not at all)
    // cannot make the opening-range sweep unbounded. 200 turns at any realistic closing speed
    // covers more ground than any weapon in the game reaches.
    private const int MaximumApproachTurns = 200;
    // TUNABLE. How much more than the opposing force's whole battle value the approach must be
    // expected to remove before extra opening range stops being worth anything. Expected removal
    // is a mean; a plan that expects to remove exactly 100% of the enemy before contact coin-flips
    // on whether it actually does. 1.5 buys roughly one extra turn of fire against a fast melee
    // force -- enough that ordinary variance does not put the enemy into contact at full strength.
    private const float ApproachRemovalMargin = 1.5f;
    // Tie-break, not a trade-off. Past the range that destroys the enemy before contact the
    // approach exchange is exactly flat, and a flat score leaves the answer to whichever coarse
    // sample the sweep happened to reach first -- quantizing the opening range to reach/32 (31
    // yards at a bolter's 1000) and hiding real differences between targets underneath it. Tilting
    // the plateau by at most this share of its own height across the whole band makes the SHORTEST
    // sufficient range the true maximum, so the refinement pass resolves it properly. Shorter also
    // matters in its own right: an opening range twice what the fight needs is a battle twice as
    // many turns long for the same outcome.
    private const float ApproachRangeParsimony = 0.01f;
    // TUNABLE. Turns of enemy movement held back from the saturation floor below, so a force does
    // not open the engagement at the very outer edge of its own useful band.
    //
    // The logic is that the attacker wants time to shoot at RETREATING enemies. Opening at the
    // saturation range spends the whole band up front: it buys turns of fire while the enemy closes,
    // but the moment that enemy turns and withdraws it is immediately outside the band and every
    // remaining shot is wasted -- which is the Xibarrus Nu shape (2026-08-05), where the withdrawal
    // triggered at 42.9% battle value and the pursuers had no useful range left to punish it with.
    // Holding back ten bounds of the enemy's own speed reserves roughly ten turns of fire against a
    // quarry running flat out, in the quantity that decides how fast that band is spent.
    //
    // It is a floor adjustment only. An enemy tough enough that `sufficient` exceeds the reduced
    // floor still pushes the opening range back out, so this never overrides the sufficiency branch.
    private const float RetreatFireTurns = 10f;

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
    /// <para>PHASE 7 ARGUED THE APPROACH COST WAS ZERO AT <c>r = band</c>, and it is -- WHEN YOU ARE
    /// THE ONE CLOSING. Opening far then buys turns of walking at reduced effect. When the ENEMY
    /// closes on you, opening far buys turns of unanswered fire and costs nothing, and the per-turn
    /// snapshot cannot see the difference because it never counts turns. Phase 7 claimed the free
    /// shots "are already inside the band, as the melee arrival term"; they are not. That term
    /// discounts a single turn's arrival value. It never credits the fifty turns of shooting that
    /// opening at 400 yards actually buys.</para>
    ///
    /// <para>SO COUNT THE TURNS. For a candidate opening range the enemy closes at its own speed and
    /// this squad collects <c>outgoing - incoming</c> each turn until contact; the score is that
    /// running total, saturated at <see cref="ApproachRemovalMargin"/> times the opposing force's
    /// battle value, because an enemy cannot be killed twice and range bought past the point of
    /// destroying them buys nothing. <see cref="RangedEffectivenessCurve.Argmax"/> breaks ties
    /// toward the SMALLER range, so that half of the answer is the shortest opening range that still
    /// expects to destroy the enemy before they arrive -- not the longest range on the board. It is
    /// then floored by the range at which this force's own fire is still worth using, because an
    /// expected-value plan is a poor thing to stand on at knife range; see the body.</para>
    ///
    /// <para>The high-constitution case then handles itself, in the half of the space where it is
    /// the binding constraint: an enemy too tough to destroy inside the useful band needs more turns
    /// of fire, more turns is more distance, and the opening range moves outward on its own with no
    /// toughness term anywhere in the formula. Measured against five Carnifexes (2026-08-05), the
    /// answer holds at the floor -- 75 and 67 yards -- through constitution 120 and 224, then climbs
    /// 94 / 242 / 687 / 996 as constitution goes 400 / 800 / 1600 / 3200. Below the crossover the
    /// floor decides and toughness does not move the answer at all; that is the intended shape, not
    /// a gap in it.</para>
    /// </summary>
    /// <remarks>
    /// A force with no usable ranged weapon, or one that cannot penetrate the opposition at any
    /// range, gets 0 here -- open in contact -- exactly as the same degenerate cases resolve
    /// mid-fight. The mid-fight band keeps the snapshot argmax, which is the right question there:
    /// "where do I want to stand THIS turn" genuinely is a per-turn quantity.
    /// </remarks>
    internal static float CalculatePreferredOpeningRange(
        BattleSquad squad,
        IReadOnlyCollection<BattleSquad> opposingSquads)
    {
        if (squad == null) return 0;
        List<BattleSoldier> able = squad.AbleSoldiers.ToList();
        if (able.Count == 0) return 0;
        EngagementCurves curves = BuildEngagementCurves(
            squad,
            opposingSquads,
            WeaponReach(able),
            requirePlacement: false,
            out float degenerateRange);
        if (curves == null) return degenerateRange;

        float parsimony = curves.OpposingBattleValue
            * ApproachRemovalMargin
            * ApproachRangeParsimony
            / Math.Max(1f, curves.Band);
        float sufficient = RangedEffectivenessCurve.Argmax(
            curves.Band,
            range => CalculateApproachExchange(curves, range) - (parsimony * range));
        // FLOOR: never open inside the range our own fire is still worth using, less the headroom
        // below.
        //
        // `sufficient` is the shortest range whose EXPECTED removal destroys them before contact,
        // and expectation is a poor thing to stand on at knife range: against lightly armoured
        // monsters it lands at 12 yards, where the plan works on average and one cold streak puts a
        // Broodlord into the line at full strength. The saturation range is the authored answer to
        // "how far out is this force still doing real work", it costs nothing to take when the
        // enemy is the one closing, and it is the quantity Phase 6 used before Phase 7 replaced it
        // for a reason that only holds when WE close.
        //
        // The two branches divide the space cleanly. Ordinary enemies are destroyed well inside the
        // useful band, so the floor decides and the opening range is stable. An enemy tough enough
        // that the useful band does not buy enough turns pushes `sufficient` outside it, and then
        // sufficiency decides -- which is exactly the high-constitution case moving the answer
        // outward with no toughness term in the formula.
        float useful = Math.Max(
            0,
            curves.Outgoing.SaturationRange(RangedEffectivenessCurve.SaturationFraction)
                - (RetreatFireTurns * curves.ClosingSpeed));
        return Math.Clamp(Math.Max(sufficient, useful), 0, curves.Band);
    }

    /// <summary>
    /// Net battle value this squad expects to trade over the whole approach if the engagement opens
    /// at <paramref name="openingRange"/>: what it removes from the closing force, less what that
    /// force removes from it, each saturated at the point where there is nobody left to remove.
    /// </summary>
    private static float CalculateApproachExchange(
        EngagementCurves curves,
        float openingRange)
    {
        float enemyCeiling = curves.OpposingBattleValue * ApproachRemovalMargin;
        float gap = openingRange;
        float removedEnemy = 0;
        float removedUs = 0;
        for (int turn = 0; turn < MaximumApproachTurns && gap > ContactGap; turn++)
        {
            removedEnemy += curves.Outgoing.RemovalAt(gap);
            removedUs += curves.Incoming.RemovalAt(gap);
            // Once either side is spent the approach is over; further turns of it are fiction.
            if (removedEnemy >= enemyCeiling || removedUs >= curves.OurBattleValue)
            {
                break;
            }
            gap -= curves.ClosingSpeed;
        }
        return Math.Min(removedEnemy, enemyCeiling)
            - Math.Min(removedUs, curves.OurBattleValue);
    }

    /// <summary>
    /// Battle-value-weighted mean of the able soldiers' effective weapon reach: the same quantity
    /// <see cref="BuildProfile"/> stores as <c>PreferredBandUpper</c>, computed without requiring
    /// the soldiers to be on a grid. Kept separate rather than factored out of that method, whose
    /// single pass also produces the lower band edge and the capability groups.
    /// </summary>
    private static float WeaponReach(IReadOnlyList<BattleSoldier> soldiers)
    {
        float weighted = 0;
        float weight = 0;
        foreach (BattleSoldier soldier in soldiers)
        {
            if (soldier.FunctioningHands <= 0
                || !soldier.RangedWeapons.Any(weapon => weapon.LoadedAmmo > 0)
                || !soldier.EquippedRangedWeapons.Any())
            {
                continue;
            }
            float bv = Math.Max(1, soldier.Soldier.Template.BattleValue);
            weighted += bv * soldier.EquippedRangedWeapons
                .Where(weapon => weapon.LoadedAmmo > 0)
                .Select(weapon => EffectiveMaximumRange(soldier, weapon))
                .DefaultIfEmpty(0)
                .Max();
            weight += bv;
        }
        return weight > 0 ? weighted / weight : 1f;
    }

    /// <summary>
    /// A representative member of a force, for the OTHER side's curve to shoot at. Able-soldier
    /// mean of the same four scalars, plus mean battle value so the two curves are commensurable.
    /// </summary>
    private static EngagementTargetProfile RepresentativeProfile(
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
            constitution += soldier.Soldier.Constitution;
            evasion += soldier.Soldier.Template.Species.RangedEvasion;
            battleValue += Math.Max(1, soldier.Soldier.Template.BattleValue);
        }
        return new EngagementTargetProfile(
            size / soldiers.Count,
            armor / soldiers.Count,
            constitution / soldiers.Count,
            evasion / soldiers.Count,
            battleValue / soldiers.Count);
    }

    /// <summary>
    /// The ranged/melee split of one soldier's battle value. Extracted verbatim from
    /// <see cref="BuildProfile"/> so <see cref="CalculateEffectiveEngagementRange"/> can price an
    /// opposing force's melee threat before that force's own profiles have been built.
    /// </summary>
    private static (float Ranged, float Melee) SoldierCombatShares(BattleSoldier soldier)
    {
        bool usableRanged = soldier.FunctioningHands > 0
            && soldier.RangedWeapons.Any(weapon => weapon.LoadedAmmo > 0)
            && soldier.EquippedRangedWeapons.Any();
        bool usableMelee = soldier.FunctioningHands > 0
            && (soldier.MeleeWeapons.Count > 0 || soldier.RangedWeapons.Count == 0);
        float authoredMelee = soldier.Soldier.Template.MeleeFraction;
        if (!soldier.Soldier.Template.HasAuthoredMeleeFraction)
        {
            float bestRangedReach = usableRanged
                ? soldier.EquippedRangedWeapons
                    .Where(weapon => weapon.LoadedAmmo > 0)
                    .Select(weapon => EffectiveMaximumRange(soldier, weapon))
                    .DefaultIfEmpty(0)
                    .Max()
                : 0;
            authoredMelee = usableMelee && !usableRanged ? 0.9f
                : usableMelee && bestRangedReach <= 50 ? 0.7f
                : usableMelee && usableRanged ? 0.25f
                : 0.05f;
        }
        return (
            usableRanged ? 1f - authoredMelee : 0,
            usableMelee ? authoredMelee : 0);
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
                // Design/Reference/EngagementScoringOverhaul.md Phase 0.
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
                    is EngagementSquadRole.Pursuit or EngagementSquadRole.Standoff)
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
                    is EngagementSquadRole.Pursuit or EngagementSquadRole.Standoff)
                && primary.HasValue
                    ? primaryRole switch
                    {
                        EngagementSquadRole.Bound or EngagementSquadRole.Routing =>
                            profiles[primary.Value].MoveSpeed,
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
    /// carry the derivation and then have the lookahead score movement relative to it, double
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
        if (distance > profile.PreferredBandUpper + 1) return EngagementOptionKind.JogToward;
        if (distance < profile.PreferredBandLower - 1) return EngagementOptionKind.StepBack;
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
            // family in BattleSquadPlanner (see Design/Reference/EngagementScoringOverhaul.md Phase
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
