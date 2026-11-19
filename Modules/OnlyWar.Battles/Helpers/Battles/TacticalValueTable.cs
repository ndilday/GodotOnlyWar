using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace OnlyWar.Battles
{
    /// <summary>
    /// The tactical value of every able soldier on the field for one turn
    /// (Design/Reference/BattleValueCalibration.md, section 2). Computed from the battle state alone:
    /// no database value goes in, so the planner can be priced in it without the gauntlet measuring
    /// its own assumptions.
    ///
    /// <para>THE QUANTITY. For soldier j: <c>a_j</c>, the share of an enemy soldier he removes a turn,
    /// averaged over the enemy force (weighted by the enemy's tactical value from the previous turn,
    /// see below); <c>d_j</c>, the turns he survives under the fire and blows of one average enemy
    /// soldier; and <c>u_j = sqrt(a_j * d_j)</c>, the Lanchester value the offline
    /// <see cref="BattleValueCalculator"/> uses against its reference panel.</para>
    ///
    /// <para>THE SIDE FACTOR. Measured against the real opponent instead of a common panel, <c>u</c>
    /// is the soldier's worth in units of the enemy's average worth: for two uniform sides
    /// <c>u_m = v_m / v_b</c> and <c>u_b = v_b / v_m</c>, so the ratio of the two sides' <c>u</c> is
    /// the square of the true ratio. The square law says one marine is worth
    /// <c>sqrt(alpha_m / alpha_b)</c> Mob models (alpha: take-outs a turn against the other side),
    /// and that is what <c>v_j = u_j * (s_E / s_X)^(1/4)</c> gives, with <c>s</c> each side's mean
    /// <c>u</c>. Within a side the factor is common, so <c>v</c> keeps the order <c>u</c> gives, and the
    /// result does not depend on how many soldiers each side has.</para>
    ///
    /// <para>THE SELF-REFERENCE. A take-out of a valuable soldier should count more, so <c>a_j</c>
    /// weights each enemy by his value from the previous turn (all equal on the first). The weights
    /// are normalized by the enemy's mean, which keeps <c>a</c> in "average enemy" units and the side
    /// factor valid. It is one step of a fixed-point iteration a turn, carried by the battle.</para>
    ///
    /// <para>RATES. A soldier's rate against an enemy is the better of his ranged and his melee:
    /// ranged is the <see cref="RangedEffectivenessCurve"/> of his weapons against that enemy's real
    /// hit locations at the current separation (at his reach if the enemy is beyond it), the
    /// sustained aimed cycle; melee is one turn of his strikes at contact
    /// (<see cref="MeleeStrikeEstimator.EstimateTurnTakeOutProbability"/>). A melee soldier is valued
    /// as though he gets there, a gunner as though he fires from where he is: judgment call,
    /// 2026-11-01. Enemies are grouped by squad, template and armor; one representative per group,
    /// at the group's centroid.</para>
    ///
    /// <para>NOT YET (judgment call, 2026-11-01): the leader term of section 2.4. A leader is
    /// valued by his own a and d only. Revisit if the planner stops shooting sergeants.</para>
    /// </summary>
    internal sealed class TacticalValueTable
    {
        /// <summary>
        /// Multiplies every value. A pure unit: the geometric mean of the two side means is this
        /// number, so an average soldier is worth about 10 and the planner's floors and tie
        /// tolerances, written when values were tens to hundreds, keep their meaning.
        /// </summary>
        internal const float Scale = 10f;

        /// <summary>The lowest value the planner prices a soldier at, so no one is free.</summary>
        internal const float MinimumValue = 0.1f;

        /// <summary>Turns a soldier nobody can hurt is credited, so u stays finite.</summary>
        internal const float MaximumDurabilityTurns = 1_000f;

        /// <summary>Per-turn rates below this count as nothing (keeps 1/rate finite).</summary>
        private const float MinimumRate = 1e-6f;

        private static long s_rateEvaluations;
        private static long s_buildTicks;

        /// <summary>Rate evaluations across every table built in this process. Diagnostic.</summary>
        internal static long TotalRateEvaluations => Interlocked.Read(ref s_rateEvaluations);

        /// <summary>Stopwatch ticks spent building tables in this process. Diagnostic.</summary>
        internal static long TotalBuildTicks => Interlocked.Read(ref s_buildTicks);

        internal static TacticalValueTable Empty { get; } = new(
            new Dictionary<int, SoldierTacticalValue>(),
            new Dictionary<(int, int), PairTacticalValue>(),
            1f,
            1f,
            0);

        private readonly IReadOnlyDictionary<int, SoldierTacticalValue> _soldiers;
        private readonly IReadOnlyDictionary<(int SoldierId, int EnemySquadId), PairTacticalValue> _pairs;

        private TacticalValueTable(
            IReadOnlyDictionary<int, SoldierTacticalValue> soldiers,
            IReadOnlyDictionary<(int, int), PairTacticalValue> pairs,
            float firstSideMean,
            float secondSideMean,
            int rateEvaluations)
        {
            _soldiers = soldiers;
            _pairs = pairs;
            FirstSideMeanU = firstSideMean;
            SecondSideMeanU = secondSideMean;
            RateEvaluations = rateEvaluations;
        }

        /// <summary>Mean u of the first side's able soldiers.</summary>
        internal float FirstSideMeanU { get; }

        /// <summary>Mean u of the second side's able soldiers.</summary>
        internal float SecondSideMeanU { get; }

        /// <summary>Soldier-against-enemy-group rates this table evaluated.</summary>
        internal int RateEvaluations { get; }

        internal IReadOnlyDictionary<int, SoldierTacticalValue> Soldiers => _soldiers;

        /// <summary>The soldier's tactical value; 0 for a soldier the table does not know.</summary>
        internal float ValueOf(int soldierId) =>
            _soldiers.TryGetValue(soldierId, out SoldierTacticalValue value) ? value.Value : 0f;

        internal bool TryGet(int soldierId, out SoldierTacticalValue value) =>
            _soldiers.TryGetValue(soldierId, out value);

        /// <summary>
        /// The soldier's offense and durability against one enemy squad: the pair terms the
        /// engagement-mode choice reads (section 3). Default when the pair is unknown.
        /// </summary>
        internal PairTacticalValue PairOf(int soldierId, int enemySquadId) =>
            _pairs.GetValueOrDefault((soldierId, enemySquadId));

        /// <summary>
        /// Builds the table for one turn. <paramref name="previousValues"/> is the previous turn's
        /// table (null on the first turn), whose values weight the take-outs.
        /// </summary>
        internal static TacticalValueTable Build(
            IReadOnlyCollection<BattleSquad> firstSide,
            IReadOnlyCollection<BattleSquad> secondSide,
            TacticalValueTable previousValues = null)
        {
            long started = Stopwatch.GetTimestamp();
            List<BattleSoldier> first = AbleSoldiers(firstSide);
            List<BattleSoldier> second = AbleSoldiers(secondSide);
            if (first.Count == 0 || second.Count == 0)
            {
                return Empty;
            }

            List<TargetGroup> firstGroups = Groups(first);
            List<TargetGroup> secondGroups = Groups(second);
            int evaluations = 0;
            // rates[s][g]: soldier s against the representative of enemy group g.
            float[][] firstRates = Rates(
                first, secondGroups, ref evaluations, out float[][] firstMelee, out float[][] firstPeak,
                out float[][] firstContact, out bool[][] firstMeleeBest);
            float[][] secondRates = Rates(
                second, firstGroups, ref evaluations, out float[][] secondMelee, out float[][] secondPeak,
                out float[][] secondContact, out bool[][] secondMeleeBest);
            float[] firstShares = MeleeShares(
                first, secondGroups, firstMelee, firstPeak, GroupWeights(secondGroups, previousValues));
            float[] secondShares = MeleeShares(
                second, firstGroups, secondMelee, secondPeak, GroupWeights(firstGroups, previousValues));

            Dictionary<int, SoldierTacticalValue> soldiers = [];
            Dictionary<(int, int), PairTacticalValue> pairs = [];
            float[] firstU = SideU(
                first, firstGroups, firstRates, firstMeleeBest, secondGroups, second, secondRates, secondContact,
                previousValues, pairs, out float[] firstOffense, out float[] firstDurability);
            float[] secondU = SideU(
                second, secondGroups, secondRates, secondMeleeBest, firstGroups, first, firstRates, firstContact,
                previousValues, pairs, out float[] secondOffense, out float[] secondDurability);

            float firstMean = Mean(firstU);
            float secondMean = Mean(secondU);
            float firstFactor = SideFactor(firstMean, secondMean) * Scale;
            float secondFactor = SideFactor(secondMean, firstMean) * Scale;
            for (int index = 0; index < first.Count; index++)
            {
                soldiers[first[index].Soldier.Id] = new SoldierTacticalValue(
                    firstOffense[index], firstDurability[index], firstU[index],
                    firstU[index] * firstFactor, firstShares[index]);
            }
            for (int index = 0; index < second.Count; index++)
            {
                soldiers[second[index].Soldier.Id] = new SoldierTacticalValue(
                    secondOffense[index], secondDurability[index], secondU[index],
                    secondU[index] * secondFactor, secondShares[index]);
            }

            Interlocked.Add(ref s_rateEvaluations, evaluations);
            Interlocked.Add(ref s_buildTicks, Stopwatch.GetTimestamp() - started);
            return new TacticalValueTable(soldiers, pairs, firstMean, secondMean, evaluations);
        }

        /// <summary>
        /// Writes every value onto its soldier (<see cref="BattleSoldier.TacticalValue"/>), where
        /// the planner reads it. Called serially, before any planning worker starts.
        /// </summary>
        internal void AssignTo(IEnumerable<BattleSquad> squads)
        {
            foreach (BattleSoldier soldier in squads
                .Where(squad => squad != null)
                .SelectMany(squad => squad.Soldiers))
            {
                bool known = _soldiers.TryGetValue(soldier.Soldier.Id, out SoldierTacticalValue value);
                soldier.TacticalValue = known ? Math.Max(MinimumValue, value.Value) : MinimumValue;
                soldier.TacticalMeleeShare = known ? value.MeleeShare : null;
            }
        }

        /// <summary>
        /// Each soldier's melee share against this enemy: his melee take-outs at contact over
        /// those plus his ranged take-outs across his reach, both averaged over the enemy force
        /// with the take-out weights. It replaces <c>SoldierTemplate.MeleeFraction</c>
        /// (section 3): the same Ork Boy template is a melee soldier with a choppa and a gunner
        /// with a shoota, and a soldier can lean to melee against a mob and to his gun against a
        /// monster. A soldier counts melee only with a usable melee weapon, or unarmed when he has
        /// no gun, as <see cref="BattleEngagementFrameBuilder"/> always has.
        /// </summary>
        private static float[] MeleeShares(
            List<BattleSoldier> soldiers,
            List<TargetGroup> enemyGroups,
            float[][] meleeRates,
            float[][] peakRates,
            float[] weights)
        {
            float[] shares = new float[soldiers.Count];
            for (int index = 0; index < soldiers.Count; index++)
            {
                BattleSoldier soldier = soldiers[index];
                bool usableMelee = soldier.FunctioningHands > 0
                    && (soldier.MeleeWeapons.Count > 0 || soldier.RangedWeapons.Count == 0);
                float melee = 0f;
                float ranged = 0f;
                float weightTotal = 0f;
                for (int group = 0; group < enemyGroups.Count; group++)
                {
                    float weight = enemyGroups[group].Members.Count * weights[group];
                    melee += meleeRates[index][group] * weight;
                    ranged += peakRates[index][group] * weight;
                    weightTotal += weight;
                }
                if (!usableMelee)
                {
                    melee = 0f;
                }
                shares[index] = melee + ranged > MinimumRate
                    ? melee / (melee + ranged)
                    : usableMelee ? 1f : 0f;
            }
            return shares;
        }

        /// <summary>
        /// Before the first planning pass (placement and the opening forecast), gives every soldier
        /// of both forces a value if any able one has none. Positions may not exist yet; then every
        /// separation reads 0 (point blank for guns, contact for melee).
        /// </summary>
        internal static void EnsureAssigned(
            IReadOnlyCollection<BattleSquad> firstSide,
            IReadOnlyCollection<BattleSquad> secondSide)
        {
            bool missing = (firstSide ?? [])
                .Concat(secondSide ?? [])
                .Where(squad => squad != null)
                .SelectMany(squad => squad.AbleSoldiers)
                .Any(soldier => soldier.TacticalValue == null);
            if (!missing)
            {
                return;
            }
            Build(firstSide, secondSide).AssignTo((firstSide ?? []).Concat(secondSide ?? []));
        }

        /// <summary>
        /// <c>(s_enemy / s_own)^(1/4)</c>; 1 when either side has no value to compare.
        /// </summary>
        private static float SideFactor(float ownMean, float enemyMean) =>
            ownMean > 0f && enemyMean > 0f
                ? MathF.Pow(enemyMean / ownMean, 0.25f)
                : 1f;

        private static float[] SideU(
            List<BattleSoldier> own,
            List<TargetGroup> ownGroups,
            float[][] ownRates,
            bool[][] ownMeleeBest,
            List<TargetGroup> enemyGroups,
            List<BattleSoldier> enemy,
            float[][] enemyRates,
            float[][] enemyContactRates,
            TacticalValueTable previousValues,
            Dictionary<(int, int), PairTacticalValue> pairs,
            out float[] offense,
            out float[] durability)
        {
            // Previous-turn weight of each enemy group: its members' mean value over the enemy's
            // mean value. Members with no previous value count as average.
            float[] weights = GroupWeights(enemyGroups, previousValues);
            Dictionary<int, int> ownGroupOf = GroupIndexBySoldier(ownGroups);
            Dictionary<int, int> enemyGroupOf = GroupIndexBySoldier(enemyGroups);

            offense = new float[own.Count];
            durability = new float[own.Count];
            float[] u = new float[own.Count];
            for (int index = 0; index < own.Count; index++)
            {
                BattleSoldier soldier = own[index];
                int ownGroup = ownGroupOf[soldier.Soldier.Id];

                // Offense against the whole force and against each enemy squad.
                float weightedRate = 0f;
                float weightTotal = 0f;
                Dictionary<int, (float Rate, float Weight)> bySquad = [];
                for (int group = 0; group < enemyGroups.Count; group++)
                {
                    float weight = enemyGroups[group].Members.Count * weights[group];
                    float rate = ownRates[index][group];
                    weightedRate += rate * weight;
                    weightTotal += weight;
                    int squadId = enemyGroups[group].SquadId;
                    (float squadRate, float squadWeight) = bySquad.GetValueOrDefault(squadId);
                    bySquad[squadId] = (squadRate + (rate * weight), squadWeight + weight);
                }
                offense[index] = weightTotal > 0f ? weightedRate / weightTotal : 0f;

                // Durability: turns under one average enemy soldier, and under each enemy squad's
                // average soldier. Against an enemy he is valued as reaching (his melee is his best
                // against that enemy), at the enemy's rate at contact (MeleeDurabilityAtContact).
                float incoming = 0f;
                Dictionary<int, (float Rate, int Count)> incomingBySquad = [];
                for (int enemyIndex = 0; enemyIndex < enemy.Count; enemyIndex++)
                {
                    bool atContact = MeleeDurabilityAtContact
                        && ownMeleeBest[index][enemyGroupOf[enemy[enemyIndex].Soldier.Id]];
                    float rate = atContact
                        ? enemyContactRates[enemyIndex][ownGroup]
                        : enemyRates[enemyIndex][ownGroup];
                    incoming += rate;
                    int squadId = enemy[enemyIndex].BattleSquad.Id;
                    (float squadRate, int count) = incomingBySquad.GetValueOrDefault(squadId);
                    incomingBySquad[squadId] = (squadRate + rate, count + 1);
                }
                durability[index] = Durability(incoming / enemy.Count);
                u[index] = MathF.Sqrt(offense[index] * durability[index]);

                foreach ((int squadId, (float rate, float weight)) in bySquad)
                {
                    (float squadIncoming, int count) = incomingBySquad.GetValueOrDefault(squadId);
                    pairs[(soldier.Soldier.Id, squadId)] = new PairTacticalValue(
                        weight > 0f ? rate / weight : 0f,
                        Durability(count > 0 ? squadIncoming / count : 0f));
                }
            }
            return u;
        }

        private static float Durability(float incomingRate) =>
            incomingRate > MinimumRate
                ? Math.Min(MaximumDurabilityTurns, 1f / incomingRate)
                : MaximumDurabilityTurns;

        private static float[] GroupWeights(
            List<TargetGroup> groups,
            TacticalValueTable previousValues)
        {
            float[] weights = new float[groups.Count];
            if (previousValues == null || previousValues._soldiers.Count == 0)
            {
                Array.Fill(weights, 1f);
                return weights;
            }

            float known = 0f;
            int knownCount = 0;
            foreach (TargetGroup group in groups)
            {
                foreach (BattleSoldier member in group.Members)
                {
                    if (previousValues._soldiers.TryGetValue(
                        member.Soldier.Id, out SoldierTacticalValue value))
                    {
                        known += value.Value;
                        knownCount++;
                    }
                }
            }
            float mean = knownCount > 0 ? known / knownCount : 0f;
            if (mean <= 0f)
            {
                Array.Fill(weights, 1f);
                return weights;
            }

            for (int index = 0; index < groups.Count; index++)
            {
                float total = 0f;
                foreach (BattleSoldier member in groups[index].Members)
                {
                    total += previousValues._soldiers.TryGetValue(
                        member.Soldier.Id, out SoldierTacticalValue value)
                            ? value.Value
                            : mean;
                }
                weights[index] = total / groups[index].Members.Count / mean;
            }
            return weights;
        }

        private static float[][] Rates(
            List<BattleSoldier> soldiers,
            List<TargetGroup> enemyGroups,
            ref int evaluations,
            out float[][] meleeRates,
            out float[][] peakRangedRates,
            out float[][] contactRates,
            out bool[][] meleeBest)
        {
            float[][] rates = new float[soldiers.Count][];
            meleeRates = new float[soldiers.Count][];
            peakRangedRates = new float[soldiers.Count][];
            contactRates = new float[soldiers.Count][];
            meleeBest = new bool[soldiers.Count][];
            for (int index = 0; index < soldiers.Count; index++)
            {
                rates[index] = new float[enemyGroups.Count];
                meleeRates[index] = new float[enemyGroups.Count];
                peakRangedRates[index] = new float[enemyGroups.Count];
                contactRates[index] = new float[enemyGroups.Count];
                meleeBest[index] = new bool[enemyGroups.Count];
                for (int group = 0; group < enemyGroups.Count; group++)
                {
                    RateTerms terms = RatesAgainst(soldiers[index], enemyGroups[group]);
                    rates[index][group] = terms.Rate;
                    meleeRates[index][group] = terms.Melee;
                    peakRangedRates[index][group] = terms.PeakRanged;
                    contactRates[index][group] = terms.Contact;
                    meleeBest[index][group] = terms.MeleeBest;
                    evaluations++;
                }
            }
            return rates;
        }

        /// <summary>
        /// Samples of the ranged band for the melee share. Read point blank instead (first try,
        /// 2026-11-01) a slugga pistol nearly matched a choppa and Slugga Boyz read 0.49.
        /// </summary>
        private const int BandSamples = 4;

        /// <summary>
        /// One soldier against one enemy group. <c>Rate</c>, the share of one enemy soldier he
        /// removes a turn, is the better of his sustained ranged cycle at the current separation
        /// (at his reach if the enemy is beyond it) and one turn of strikes at contact.
        /// <c>Melee</c> is the latter and <c>PeakRanged</c> the ranged cycle averaged over his
        /// reach, the two the melee share compares.
        /// </summary>
        /// <remarks>
        /// <c>Contact</c> is the better of his ranged cycle point blank and his melee: his rate
        /// against an enemy who is valued as reaching him (<see cref="MeleeDurabilityAtContact"/>).
        /// <c>MeleeBest</c>: his melee is at least his ranged cycle averaged over its band
        /// (<c>PeakRanged</c>), so he is a contact fighter against this enemy at any range and is
        /// valued as though he gets there.
        /// </remarks>
        internal readonly record struct RateTerms(
            float Rate, float Melee, float PeakRanged, float Contact = 0f, bool MeleeBest = false);

        /// <summary>
        /// Offense and durability at one range (2026-11-15, product owner). A soldier whose melee
        /// against an enemy beats his gun across its band is valued as though he gets there; his durability
        /// against that enemy is now read at contact too (the enemy's <see cref="RateTerms.Contact"/>),
        /// not at the present separation. Before, a Warrior's offense was his melee at every range
        /// while his durability was the marines' Boltgun fire at the separation, so at 449 yards
        /// one Warrior squad was priced at 144-160 against a Tactical squad's 58 (93 against 88-106
        /// at 1 yard). In that currency the Warriors' losses were dear and their kills cheap, their
        /// effective engagement range went to their reach, and they never closed
        /// (Design/Reference/BattleValueCalibration.md). One-sided on purpose: a first version read
        /// both soldiers of such a pair at contact, which gave the gunner side point-blank fire and
        /// moved a marine from at least 5 Mob models to 3.2 (the gauntlet measures about 24). A
        /// static field so the diagnostics can compare: DUEL_CONTACT_DURABILITY=0 restores the old rule.
        /// </summary>
        internal static bool MeleeDurabilityAtContact = true;

        /// <summary>
        /// Whether a soldier with a gun and no melee weapon is credited his unarmed melee
        /// (2026-11-15, product owner: no, as <see cref="MeleeShares"/> already rules). Without
        /// this, a marine's fists beat his Boltgun at range, so he too read as a contact fighter
        /// under <see cref="MeleeDurabilityAtContact"/> and Tactical v Warriors stayed a stand-off.
        /// DUEL_UNARMED_WITH_GUN=1 restores the old credit.
        /// </summary>
        internal static bool UnarmedMeleeWithGun = false;        internal static float RateAgainst(BattleSoldier soldier, TargetGroup group) =>
            RatesAgainst(soldier, group).Rate;

        internal static RateTerms RatesAgainst(BattleSoldier soldier, TargetGroup group)
        {
            BattleSoldier target = group.Representative;
            // A soldier with a gun and no melee weapon is credited no melee (UnarmedMeleeWithGun).
            float melee = !UnarmedMeleeWithGun
                    && soldier.EquippedRangedWeapons.Count > 0
                    && soldier.MeleeWeapons.Count == 0
                ? 0f
                : MeleeStrikeEstimator.EstimateTurnTakeOutProbability(soldier, target);
            float ranged = 0f;
            float pointBlank = 0f;
            float peak = 0f;
            if (soldier.EquippedRangedWeapons.Count > 0)
            {
                // Battle value 1: the curve then reads in shares of the target soldier.
                RangedEffectivenessCurve curve = RangedEffectivenessCurve.Build(
                    [soldier],
                    new EngagementTargetProfile(
                        target.Soldier.Size,
                        target.Armor?.Template.ArmorProvided ?? 0,
                        RemovalMath.RepresentativeConstitution(target),
                        target.Soldier.Template.Species.RangedEvasion,
                        1f),
                    target);
                if (!curve.IsEmpty)
                {
                    float range = Math.Min(group.DistanceFrom(soldier), curve.Reach);
                    ranged = curve.SustainedAt(range, CurveFireModel.SustainedAimed).Removal;
                    // Point blank, as an unplaced table reads every separation.
                    pointBlank = range <= 0f
                        ? ranged
                        : curve.SustainedAt(0f, CurveFireModel.SustainedAimed).Removal;
                    // The mean over the band a standing squad can fire from: four midpoints of
                    // [0, reach]. A long, accurate gun keeps its rate across it; a pistol does not.
                    for (int sample = 0; sample < BandSamples; sample++)
                    {
                        float at = curve.Reach * (sample + 0.5f) / BandSamples;
                        peak += curve.SustainedAt(at, CurveFireModel.SustainedAimed).Removal
                            / BandSamples;
                    }
                }
            }
            return new RateTerms(
                Math.Clamp(Math.Max(melee, ranged), 0f, 1f),
                Math.Clamp(melee, 0f, 1f),
                Math.Clamp(peak, 0f, 1f),
                Math.Clamp(Math.Max(melee, pointBlank), 0f, 1f),
                melee > MinimumRate && melee >= ranged);
        }

        private static float Mean(float[] values)
        {
            if (values.Length == 0)
            {
                return 0f;
            }
            float total = 0f;
            foreach (float value in values)
            {
                total += value;
            }
            return total / values.Length;
        }

        private static List<BattleSoldier> AbleSoldiers(IReadOnlyCollection<BattleSquad> squads) =>
            (squads ?? (IReadOnlyCollection<BattleSquad>)Array.Empty<BattleSquad>())
                .Where(squad => squad != null)
                .OrderBy(squad => squad.Id)
                .SelectMany(squad => squad.AbleSoldiers.OrderBy(soldier => soldier.Soldier.Id))
                .ToList();

        private static List<TargetGroup> Groups(List<BattleSoldier> soldiers) =>
            soldiers
                .GroupBy(soldier => (
                    SquadId: soldier.BattleSquad.Id,
                    TemplateId: soldier.Soldier.Template.Id,
                    ArmorId: soldier.Armor?.Template.Id ?? -1))
                .OrderBy(group => group.Key.SquadId)
                .ThenBy(group => group.Key.TemplateId)
                .ThenBy(group => group.Key.ArmorId)
                .Select(group => new TargetGroup(group.Key.SquadId, group.ToList()))
                .ToList();

        private static Dictionary<int, int> GroupIndexBySoldier(List<TargetGroup> groups)
        {
            Dictionary<int, int> index = [];
            for (int group = 0; group < groups.Count; group++)
            {
                foreach (BattleSoldier member in groups[group].Members)
                {
                    index[member.Soldier.Id] = group;
                }
            }
            return index;
        }

        /// <summary>
        /// Enemy soldiers of one squad, template and armor, priced through one representative (the
        /// lowest soldier id) at the group's centroid.
        /// </summary>
        internal sealed class TargetGroup
        {
            internal int SquadId { get; }
            internal IReadOnlyList<BattleSoldier> Members { get; }
            internal BattleSoldier Representative { get; }
            private readonly float? _centroidX;
            private readonly float? _centroidY;

            internal TargetGroup(int squadId, IReadOnlyList<BattleSoldier> members)
            {
                SquadId = squadId;
                Members = members;
                Representative = members.OrderBy(member => member.Soldier.Id).First();
                List<(int X, int Y)> placed = members
                    .Where(member => member.TopLeft.HasValue)
                    .Select(member => member.TopLeft.Value)
                    .ToList();
                if (placed.Count > 0)
                {
                    _centroidX = (float)placed.Average(cell => cell.X);
                    _centroidY = (float)placed.Average(cell => cell.Y);
                }
            }

            /// <summary>Distance to the group's centroid; 0 when either is not placed.</summary>
            internal float DistanceFrom(BattleSoldier soldier)
            {
                if (!_centroidX.HasValue || !soldier.TopLeft.HasValue)
                {
                    return 0f;
                }
                float dx = soldier.TopLeft.Value.Item1 - _centroidX.Value;
                float dy = soldier.TopLeft.Value.Item2 - _centroidY.Value;
                return MathF.Sqrt((dx * dx) + (dy * dy));
            }
        }
    }

    /// <summary>
    /// One soldier's tactical value for a turn: offense (share of an average enemy soldier removed a
    /// turn), durability (turns under one average enemy soldier), u = sqrt(offense x durability),
    /// the value after the side factor, and his melee share against this enemy.
    /// </summary>
    internal readonly record struct SoldierTacticalValue(
        float Offense,
        float Durability,
        float U,
        float Value,
        float MeleeShare = 0f);

    /// <summary>
    /// One soldier against one enemy squad: offense (share of that squad's average soldier removed
    /// a turn) and durability (turns under that squad's average soldier).
    /// </summary>
    internal readonly record struct PairTacticalValue(float Offense, float Durability);
}
