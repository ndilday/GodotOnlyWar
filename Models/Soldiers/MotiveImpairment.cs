using System.Collections.Generic;

namespace OnlyWar.Models.Soldiers
{
    /// <summary>
    /// Tunables for graded casualty effects (Design/Reference/CasualtyRealism.md §2.1, §3.3).
    /// These live in code, never in the rules database, following the morale precedent
    /// (<c>MoraleConstants</c>): only the leg cripple threshold is genuinely *location* data and
    /// therefore genuinely belongs in <c>HitLocationTemplate</c>. The shape of the impairment
    /// curve is a balance knob, and calibrating it means editing this file.
    ///
    /// THE CURVE, per motive location, by the worst wound band that location is carrying:
    ///
    ///   below Major   1.00   a bruised or gashed leg does not slow a transhuman
    ///   Major         0.85   a limp
    ///   Critical      0.60   a bad limp; he is still in the fight, he is not keeping up
    ///   crippled      0.00   for a principal location -- he cannot walk
    ///   severed       0.00   for a principal location
    ///
    /// Legs cripple at Massive since Database/RulesMigration_LegCrippleThreshold.sql, so in
    /// practice a leg runs 1.0 / 0.85 / 0.6 / 0.
    ///
    /// The per-location multipliers COMPOUND MULTIPLICATIVELY across the body's motive
    /// locations, so two Critical legs give 0.6 x 0.6 = 0.36 -- markedly worse than one, without
    /// either leg alone felling him. Immobility is not a separate concept: it is simply the
    /// product reaching zero, which is what <see cref="ISoldier.CanMove"/> now tests.
    ///
    /// FEET NEVER ZERO LOCOMOTION. An extremity's contribution is clamped to
    /// <see cref="ExtremitySpeedFloor"/>, so a shattered or missing foot is a severe, permanent
    /// slow and never a zero. Physical battlefield eligibility is separate: an untreated
    /// severed foot still incapacitates the soldier, even though the motive multiplier remains
    /// positive. This preserves the distinction between movement math and the immediate loss of
    /// a limb.
    /// </summary>
    public static class CasualtyConstants
    {
        /// <summary>Motive location carrying a Major wound but nothing worse.</summary>
        public const float MajorMotiveSpeedMultiplier = 0.85f;

        /// <summary>Motive location carrying a Critical wound but nothing worse.</summary>
        public const float CriticalMotiveSpeedMultiplier = 0.6f;

        /// <summary>
        /// The worst an extremity (a foot) can ever cost. Applied as a floor rather than as a
        /// fixed value, so a lightly wounded foot still reads off the normal curve; it only bites
        /// once the foot is crippled or severed, where the curve would otherwise say zero.
        /// </summary>
        public const float ExtremitySpeedFloor = 0.4f;
    }

    /// <summary>
    /// Turns a body's motive wounds into a single speed multiplier
    /// (Design/Reference/CasualtyRealism.md §2.1, Phase 3). Replaces the old binary question
    /// "is any motive location crippled?" -- which answered total immobility and so let one
    /// Critical leg wound end a marine's battle -- with a graded product.
    ///
    /// Shared by <see cref="Soldier.CanMove"/>, <see cref="Soldier.MotiveSpeedMultiplier"/> and
    /// <c>BattleSoldier.GetMoveSpeed()</c>, so the domain and the battle layer cannot disagree
    /// about whether a man is still walking.
    ///
    /// PRINCIPAL VERSUS EXTREMITY. Only some motive locations can stop a soldier outright, and
    /// nothing in the data says which. The rule used here is RELATIVE TO THE BODY: a motive
    /// location is principal when no other motive location on that body cripples at a strictly
    /// higher threshold. On both authored bodies this makes the legs (Massive) principal and the
    /// feet (Major) extremities, which is exactly the design's intent, and it stays correct for a
    /// body authored with different absolute numbers.
    ///
    /// The rejected alternative was an absolute cut-off ("principal iff it cripples at Massive or
    /// worse"). It gives the same answer for the shipped bodies but silently demotes the sole
    /// motive location of any body whose limbs are authored below Massive to an extremity that can
    /// never fall -- which is the wrong answer for a body that has nothing else to stand on.
    /// </summary>
    public static class MotiveImpairment
    {
        /// <summary>
        /// The product of every motive location's multiplier: 1.0 for an unhurt body, 0.0 for one
        /// that cannot walk at all. Non-motive locations are ignored entirely.
        /// </summary>
        public static float CalculateSpeedMultiplier(Body body)
        {
            if (body == null) return 1f;

            uint principalThreshold = GetPrincipalCrippleThreshold(body);
            float multiplier = 1f;
            foreach (HitLocation location in body.HitLocations)
            {
                if (!location.Template.IsMotive) continue;
                float locationMultiplier = CalculateLocationMultiplier(location, principalThreshold);
                if (locationMultiplier <= 0f)
                {
                    // Nothing below can bring it back up, and an extremity can never produce a
                    // zero, so this is genuinely "cannot walk".
                    return 0f;
                }
                multiplier *= locationMultiplier;
            }

            return multiplier;
        }

        /// <summary>
        /// One motive location's contribution, classified against the body it belongs to.
        /// </summary>
        public static float CalculateLocationMultiplier(Body body, HitLocation location) =>
            CalculateLocationMultiplier(location, GetPrincipalCrippleThreshold(body));

        /// <summary>
        /// True when losing this location stops locomotion outright: no other motive location on
        /// the body has a strictly higher cripple threshold.
        /// </summary>
        public static bool IsPrincipal(Body body, HitLocationTemplate template) =>
            template != null
            && template.IsMotive
            && template.CrippleWound >= GetPrincipalCrippleThreshold(body);

        /// <summary>
        /// The highest cripple threshold among the body's motive locations. Locations at this
        /// threshold are principal; anything below it is an extremity and floors.
        /// </summary>
        public static uint GetPrincipalCrippleThreshold(Body body)
        {
            uint highest = 0;
            if (body == null) return highest;
            foreach (HitLocation location in body.HitLocations)
            {
                if (!location.Template.IsMotive) continue;
                if (location.Template.CrippleWound > highest)
                {
                    highest = location.Template.CrippleWound;
                }
            }
            return highest;
        }

        private static float CalculateLocationMultiplier(HitLocation location, uint principalThreshold)
        {
            if (location == null || !location.Template.IsMotive) return 1f;

            float banded = location.IsCrippled || location.IsSevered
                ? 0f
                : GetBandMultiplier(location.Wounds.WoundTotal);

            return location.Template.CrippleWound >= principalThreshold
                ? banded
                : System.Math.Max(banded, CasualtyConstants.ExtremitySpeedFloor);
        }

        private static float GetBandMultiplier(uint woundTotal)
        {
            if (woundTotal >= (uint)WoundLevel.Massive) return 0f;
            if (woundTotal >= (uint)WoundLevel.Critical)
            {
                return CasualtyConstants.CriticalMotiveSpeedMultiplier;
            }
            if (woundTotal >= (uint)WoundLevel.Major)
            {
                return CasualtyConstants.MajorMotiveSpeedMultiplier;
            }
            return 1f;
        }

        /// <summary>
        /// Diagnostic helper: every motive location paired with what it currently costs. Not used
        /// by the engine; it exists so tests and any future UI can explain a speed number.
        /// </summary>
        public static IEnumerable<KeyValuePair<HitLocation, float>> DescribeLocations(Body body)
        {
            if (body == null) yield break;
            uint principalThreshold = GetPrincipalCrippleThreshold(body);
            foreach (HitLocation location in body.HitLocations)
            {
                if (!location.Template.IsMotive) continue;
                yield return new KeyValuePair<HitLocation, float>(
                    location, CalculateLocationMultiplier(location, principalThreshold));
            }
        }
    }
}
