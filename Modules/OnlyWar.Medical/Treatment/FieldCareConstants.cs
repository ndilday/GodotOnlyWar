using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Medical
{
    /// <summary>
    /// Tunables for Apothecary field care (Design/Reference/CasualtyRealism.md §2.6, §3.2).
    ///
    /// These live in CODE, never in the rules database, following the morale precedent
    /// (<c>MoraleConstants</c>) and the Phase 3 precedent (<c>CasualtyConstants</c>). None of it is
    /// location data or template data -- it is the shape of a balance curve, and calibrating it
    /// means editing this file and re-running the tests that pin the curve.
    /// </summary>
    public static class FieldCareConstants
    {
        // ---- Capacity from the Medical rating -------------------------------------------------
        //
        // MILDLY SUPERLINEAR (§3.2). Ratings sit near 100 for a competent brother; the founding
        // thresholds in RoleSuitabilityService are >95 for an Apothecary and >115 for the Master of
        // the Apothecarion. An exponent of 1.5 makes the Master worth about 1.3x an ordinary
        // Apothecary rather than 1.15x, which is the intended reading: he is better, and he is not
        // worth several men. A linear curve made the office title meaningless; anything steeper made
        // one exceptional roll dominate the Chapter's whole medical throughput.

        /// <summary>Daily wound-treatment capacity of an Apothecary at the reference rating.</summary>
        public const float BaseDailyCapacity = 3.0f;

        /// <summary>The rating <see cref="BaseDailyCapacity"/> is quoted at.</summary>
        public const float ReferenceMedicalRating = 100f;

        /// <summary>
        /// Curvature of rating -> capacity. 1.0 would be linear; above 1.0 rewards excellence
        /// superlinearly. Kept low deliberately -- see the note above.
        /// </summary>
        public const float CapacityExponent = 1.5f;

        /// <summary>
        /// Hard ceiling on one man's day, so a freak rating cannot make a single Apothecary an
        /// entire Apothecarium. Mirrors the clamp RecruitmentRules.GetStaffEffectiveness applies for
        /// the same reason.
        /// </summary>
        public const float MaxDailyCapacityPerApothecary = 8.0f;

        // ---- The demotion cost curve ----------------------------------------------------------
        //
        // FLAT RELATIVE TO BAND VALUES (§3.2, decided). Wound bands are powers of 16, so a cost
        // proportional to the band's VALUE would make an Unsurvivable wound 16 million times the
        // price of a Moderate one and put every severe wound permanently out of reach -- which
        // inverts the entire point of the feature, since severe wounds are exactly the cases that
        // would otherwise be out for months. Cost therefore scales with band INDEX:
        //
        //   Moderate    -> Minor        1.0
        //   Major       -> Moderate     1.5
        //   Critical    -> Major        2.0
        //   Massive     -> Critical     2.5
        //   Mortal      -> Massive      3.0
        //   Unsurvivable-> Mortal       3.5
        //
        // At the reference capacity of 3.0/day one Apothecary can take a Mortal wound down a band
        // every day, or clear three Moderate ones -- so the severe case is genuinely treatable and
        // still costs more than the light one.

        /// <summary>Cost of demoting the cheapest treatable band (Moderate -> Minor).</summary>
        public const float DemotionBaseCost = 1.0f;

        /// <summary>Added cost per band above Moderate. The "flatness" knob.</summary>
        public const float DemotionCostPerBand = 0.5f;

        /// <summary>
        /// Surcharge per EXTRA wound in the band being demoted. A demotion moves the whole band at
        /// once (the healing model's own semantics -- demotion preserves count), so without this a
        /// location carrying five Massive wounds would be treated for the price of one. Sub-linear
        /// on purpose: treating five injuries in one session is more work than one, not five times
        /// the work.
        /// </summary>
        public const float DemotionCountSurcharge = 0.5f;

        // ---- Scheduling -----------------------------------------------------------------------

        /// <summary>
        /// Days of garrison care settled by one turn-processing pass. Field care resolves a day at a
        /// time on the mission day loop; garrison care has no day loop to hang off (§2.6), so the
        /// weekly pass runs the same daily algorithm this many times, re-triaging between each.
        /// Running the identical routine -- rather than a bulk weekly approximation -- is what keeps
        /// the two halves impossible to drift apart.
        /// </summary>
        public const int GarrisonDaysPerTurn = 7;

        // ---- Learn-by-doing -------------------------------------------------------------------

        /// <summary>
        /// Skill points granted per point of treatment capacity actually spent, to each base skill
        /// that composes the Medical rating (PRD §4.12 learn-by-doing).
        ///
        /// §3.2 asked whether an Apothecary earns XP for treating when there is no roll to take a
        /// margin from. DECIDED: yes, and the substitute for a margin is WORK DONE -- capacity spent
        /// is the only signal the system produces about how much medicine was practised, and it is a
        /// better one than a roll would be, since it already scales with how severe the caseload was.
        /// A busy Apothecary spending his full 3.0/day for a week banks about 0.21 points, which is
        /// deliberately in line with ChapterUpkeepProcessor's 0.2 weekly training points: a week in
        /// the field treating brothers is worth roughly a week of drill, and no more.
        /// </summary>
        public const float MedicalExperiencePerCapacitySpent = 0.01f;

        // ---- Derived helpers ------------------------------------------------------------------

        /// <summary>
        /// 1 for Moderate, 2 for Major ... 6 for Unsurvivable. 0 for anything with no treatment
        /// cost defined (Negligible/Minor, which heal on their own and are never treated).
        /// </summary>
        public static int GetBandIndex(WoundLevel band)
        {
            return band switch
            {
                WoundLevel.Moderate => 1,
                WoundLevel.Major => 2,
                WoundLevel.Critical => 3,
                WoundLevel.Massive => 4,
                WoundLevel.Mortal => 5,
                WoundLevel.Unsurvivable => 6,
                _ => 0
            };
        }

        /// <summary>
        /// Capacity a soldier's Medical rating buys in one day. Zero for a rating of zero, so a
        /// brother who is nominally an Apothecary but has never been evaluated contributes nothing
        /// rather than a free baseline.
        /// </summary>
        public static float GetDailyCapacity(float medicalRating)
        {
            if (medicalRating <= 0f) return 0f;
            double scaled = System.Math.Pow(
                medicalRating / ReferenceMedicalRating, CapacityExponent);
            return (float)System.Math.Min(
                BaseDailyCapacity * scaled, MaxDailyCapacityPerApothecary);
        }

        /// <summary>
        /// What one treatment costs: the band's flat index price, plus a sub-linear surcharge for
        /// each additional wound moved with it.
        /// </summary>
        public static float GetDemotionCost(WoundLevel band, int woundCount)
        {
            int index = GetBandIndex(band);
            if (index <= 0 || woundCount <= 0) return float.PositiveInfinity;
            float bandCost = DemotionBaseCost + DemotionCostPerBand * (index - 1);
            return bandCost * (1f + DemotionCountSurcharge * (woundCount - 1));
        }
    }
}
