namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Morale and synapse tunables (Design/Active/MoraleAndRout.md). Every morale-related
    /// constant lives here, in code — never in the rules database (§3.5). Calibration
    /// (§10 Phase 7) means editing these values, not editing Ego or any other DB row.
    ///
    /// SANITY HAND-CALCULATION (§10 target: marines effectively never rout; uncovered
    /// gaunts under heavy fire collapse promptly; covered gaunts never check).
    ///
    ///   resolve(Ego) = ResolveEgoCoefficient * Ego^ResolveEgoExponent
    ///     gaunt  Ego  8 -> 0.00525 * 8^2.5  = 0.950
    ///     PDF    Ego 10 -> 0.00525 * 10^2.5 = 1.660
    ///     marine Ego 14 -> 0.00525 * 14^2.5 = 3.850
    ///     warrior/genestealer Ego 20 -> 9.392
    ///   The steep exponent is deliberate: a small marine squad that loses a man in one
    ///   round sees a 50% this-turn casualty fraction, so marine resolve must sit above
    ///   the stress such a round produces, while gaunt resolve stays under the reference
    ///   stress below. A shallower curve (1.5 was tried) breaks marines in ordinary
    ///   marine-vs-marine attrition, which §2 forbids.
    ///
    ///   Reference case — 25% casualties THIS turn, 25% cumulative, force losing:
    ///     shock   = w1*0.25 + w2*0.25 = 2.0*0.25 + 1.0*0.25 = 0.75
    ///     context = 1 + k*forceDisadvantage ~= 1 + 1.0*0.41 = 1.41   (bv 100 vs 180,
    ///               two-round losses 40 vs 10 -> forceDisadvantage ~= 0.41)
    ///     stress  = 0.75 * 1.41 = 1.06
    ///     gaunt  (resolve 0.950): per-soldier fail P(z > (0.950-1.06)/0.25) = P(z>-0.43)
    ///             ~= 0.67 -> failFraction ~0.67 >= RoutThreshold 0.5 -> ROUTING (usually).
    ///     marine (resolve 3.850): P(z > (3.850-1.06)/0.25) = P(z>11) ~= 0 -> STEADY.
    ///
    ///   Heavy fire — 40% this turn, 40% cumulative, losing (stress ~1.68):
    ///     gaunt  P(z>-2.9) ~= 0.998 -> whole squad routs (prompt collapse).
    ///
    ///   Marine worst plausible round — 50% this turn, 50% cumulative, leader dead,
    ///   badly losing (context 1.76): stress = (2*0.5 + 1*0.5 + 0.5) * 1.76 = 3.52 <
    ///   3.850 -> STEADY. Only a catastrophic corner (all of the above at context ~2.0,
    ///   stress 4.0) can break a marine squad — "effectively never," not "immune."
    ///
    ///   Winning (context 1.0) vs losing (context 1.41) on the SAME local shock 0.75:
    ///     gaunt winning: stress 0.75 < 0.950 -> STEADY (fail P ~0.21 per man).
    ///     gaunt losing:  stress 1.06 > 0.950 -> ROUTING (fail P ~0.67 per man).
    ///   That asymmetry (global context MULTIPLIES local shock) is a locked decision (§2).
    /// </summary>
    public static class MoraleConstants
    {
        // §7 rear-guard eligibility / §9 force-generation "needs coverage" gate:
        // WillNotBreakWhileHolding(squad) = squadEgo >= RearGuardEgoThreshold || ...
        // Must clear Space Marine (14) and Genestealer (20); must fail PDF (10), Ork (8),
        // and gaunts (8). 12 sits in the gap between PDF/Ork/gaunt and Marine/Genestealer.
        // Calibration target for Phase 7 — not a decided value, only a decided shape.
        public const float RearGuardEgoThreshold = 12f;

        // §9 force generation ratio: BV of coverage-needing squads (neither a synapse
        // provider nor past the Ego gate) purchased per one synapse-providing squad bought.
        // Illustrative figure from §9 is one Tyranid Warrior Squad (~33 BV, §3.3) per ~150
        // BV of gaunts (Termagaunt BV 6 x 10-30 = 60-180/squad) — i.e. roughly one Warrior
        // squad per Termagaunt squad. Picked as the initial ratio here. Calibration target
        // for Phase 7.
        public const float SynapseRatioBV = 150f;

        // --- §5.2 resolve curve: raw Ego in, resolve out (never linearised via
        // AddAttributePoints; §3.6). A steep convex power curve so the marine/gaunt resolve
        // gap widens with Ego, letting marines shrug off shock that shatters gaunts (see
        // header hand-calculation for why the exponent must be this steep). All Phase 7
        // calibration targets. ---
        public const float ResolveEgoCoefficient = 0.00525f;
        public const float ResolveEgoExponent = 2.5f;

        // --- §5.2 shock weights (local, per-turn). Phase 7 calibration targets. ---
        // w1: fraction of this-turn's starting able strength lost this turn (sudden shock).
        public const float ShockCasualtyThisTurnWeight = 2.0f;
        // w2: fraction of the squad's original strength lost so far (attrition).
        public const float ShockCumulativeCasualtyWeight = 1.0f;
        // w3: squad leader dead (or downed) — command loss.
        public const float ShockLeaderDeadWeight = 0.5f;
        // w4: headcount-weighted fraction of visible friendlies that were Routing at the
        // START of this turn (§5.1 propagation; reads the turn-start snapshot only).
        public const float ShockRoutingVisibleWeight = 0.6f;
        // w5: local outnumbering beyond parity within visual range.
        public const float ShockLocalOutnumberWeight = 0.15f;
        // w6: command-aura term (§4.3, Phase 6). SIGNED: a living same-faction HQ squad
        // within CommandAuraRadius supplies +CommandAuraSupportStrength (support, subtracted
        // from shock); a side whose every HQ squad has been destroyed supplies
        // -CommandLossStress, which ADDS w6 * CommandLossStress of shock ("whose loss
        // supplies a positive one"). Never a check skip — that is synapse-only.
        public const float CommandAuraSupportWeight = 0.5f;

        // --- §4.3 command auras (Phase 6): the weak-coefficient generalisation of synapse.
        // Derived entirely from SquadTypes.HQ plus these code constants — no DB data. All
        // Phase 7 calibration targets. ---
        // Radius an HQ squad projects command over. Deliberately WEAKER AND SHORTER than
        // synapse (Species.SynapseRadius is 1000 for every synapse creature): command is
        // presence and voice, not a hive link.
        public const float CommandAuraRadius = 500f;
        // Support while a living HQ is in radius. NOT stacking: one HQ suffices, and
        // multiple HQs still supply exactly this value (max, never a sum). Net shock
        // reduction = w6 * this = 0.25.
        public const float CommandAuraSupportStrength = 0.5f;
        // Loss term once every HQ squad the side fielded has been destroyed (stateless
        // reading — see CommandAuraEvaluator). Net shock added = w6 * this = 0.25, half the
        // local squad-leader-death shock (w3 = 0.5): the platoon feels the Captain die; the
        // squad feels its own sergeant die harder.
        public const float CommandLossStress = 0.5f;

        // --- §5.2 global context multiplier: context = 1 + k * forceDisadvantage. Phase 7. ---
        public const float ContextDisadvantageCoefficient = 1.0f;
        // forceDisadvantage blends force-wide BV share and two-round loss trend (both from
        // BattleForceEvaluator's existing metrics). Weights sum to 1.
        public const float ForceDisadvantageBvWeight = 0.6f;
        public const float ForceDisadvantageLossWeight = 0.4f;

        // --- §5.2 per-soldier roll noise: fails_i = (stress + N(0, sigma)) > resolve_i. ---
        public const float MoraleRollSigma = 0.25f;

        // --- §5.3 aggregation thresholds over failFraction = fails / ableSoldiers. Phase 7. ---
        public const float RoutThreshold = 0.5f;
        public const float ShakenThreshold = 0.25f;
        // A living, non-Shaken (did-not-fail) squad leader raises both thresholds — he holds
        // them together (§5.3).
        public const float LeaderThresholdBonus = 0.15f;

        // Visual range used by the propagation (w4) and local-outnumber (w5) terms. There is
        // no line-of-sight system yet (Battle Visuals Phase 3); this flat radius is a
        // placeholder that terrain/LOS will later replace. Phase 7 calibration target.
        public const float VisualRange = 60f;

        // Clamp on w5's local-outnumber ratio so a lone squad amid a swarm cannot produce an
        // unbounded shock term. Phase 7 calibration target.
        public const float LocalOutnumberCap = 3.0f;

        // §6 Shaken outcome: degraded ranged fire. Applied as a to-hit penalty when a Shaken
        // squad evaluates its shots (skill-point scale). Phase 7 calibration target.
        public const float ShakenRangedAccuracyPenalty = 3.0f;

        // §8.2 command-collapse pricing: a squad that loses its synapse/command aura in a
        // withdrawal projection and is estimated to rout forfeits the masked-departure delay
        // and is run down at the pursuit rules' HIGHER interception rate. This multiplies the
        // pursuer's one-turn attack reach for such a routing squad only. > 1 so a bolting
        // severed swarm is caught where an orderly one would have slipped away. Phase 7
        // calibration target — shape (a reach multiplier on routed squads) is decided; the
        // value is not.
        public const float RoutInterceptionReachMultiplier = 1.5f;
    }
}
