using System;
using System.Collections.Generic;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles
{
    /// <summary>
    /// The removal fraction per hit (<see cref="RemovalMath.EvaluateRemovalFraction"/>: take-out
    /// chance plus graded wound fill) of one weapon against one target location vector, solved on
    /// range nodes <see cref="NodeSpacing"/> yards apart and interpolated linearly between them
    /// (Design/Reference/ForecastCalibration.md section 7, step 1; product owner 2026-11-10: 50-yard nodes).
    ///
    /// <para>WHY. The fraction walks every hit location with up to nine normal CDFs (one take-out
    /// tail and up to eight wound-level tails), and the forecast asked for it about 166,000 times
    /// a turn in an eight-squad battle -- 89 million times in 536 turns, more than half of all
    /// planning CPU. It depends only on the location vector and the weapon's damage at range, and
    /// damage falls linearly and slowly with range, so a few nodes per weapon carry it.</para>
    ///
    /// <para>LINEAR INTERPOLATION, NOT STEPS. A step table would make the engagement potential flat
    /// inside a band and jump at its edge, while Close and Stay differ by one move (often 10-30
    /// yards). The to-hit side is NOT tabulated: it changes fast at short range, and it costs one
    /// logarithm.</para>
    ///
    /// <para>Nodes are solved lazily, the first time a range next to them is asked, and stored as
    /// floats (atomic writes), so planning threads can share a table without a lock: two threads
    /// that race on one node compute the same value.</para>
    /// </summary>
    internal sealed class RemovalFractionTable
    {
        /// <summary>
        /// Yards between nodes. 0 solves at the exact range every time, as before 2026-11-10.
        /// Process-wide so the diagnostics can compare (DUEL_REMOVAL_NODES=0); set it before any
        /// battle, because a table keeps the nodes it has solved.
        /// </summary>
        internal static float NodeSpacing = 50f;

        /// <summary>
        /// Whether <see cref="For"/> hands out one shared table per weapon and location vector
        /// (true since 2026-11-11), or a new table every time, as before. Process-wide so the
        /// diagnostics can compare (DUEL_SHARE_REMOVAL_TABLES=0).
        /// </summary>
        internal static bool ShareTables = true;

        private const int MaximumSharedTables = 4096;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<SharedKey, RemovalFractionTable>
            SharedTables = new();

        /// <summary>
        /// The table for this weapon against this location vector, shared by every caller that asks
        /// for the same pair (2026-11-11, with step 2 of Design/Reference/ForecastCalibration.md section 7).
        ///
        /// <para>WHY. The removal-rate terms and the range curves are built again every turn and for
        /// every soldier, and each built its own table, so the same nodes were solved again and
        /// again: on an eight-squad duel about 20,000 removal-fraction solves a turn remained after
        /// the nodes, against about 80 terms a turn. A table depends only on the weapon and the
        /// location vector (armor, natural armor, wound thresholds and wounds already carried), so
        /// every unhurt soldier of a template under fire from one weapon reads the same table, this
        /// turn and every later one. The key compares the vector field by field; a soldier with new
        /// wounds has a new vector and a new table. The cache is cleared when it reaches
        /// <see cref="MaximumSharedTables"/> entries, which only costs solving nodes again.</para>
        /// </summary>
        internal static RemovalFractionTable For(
            IReadOnlyList<TakeOutLocationTerm> locations,
            RangedWeaponTemplate weapon) =>
            For(locations, weapon, NodeSpacing);

        private static RemovalFractionTable For(
            IReadOnlyList<TakeOutLocationTerm> locations,
            RangedWeaponTemplate weapon,
            float spacing)
        {
            if (!ShareTables || locations == null || weapon == null)
            {
                return new RemovalFractionTable(locations, weapon, spacing);
            }
            SharedKey key = new(weapon, locations, spacing);
            if (SharedTables.TryGetValue(key, out RemovalFractionTable table))
            {
                return table;
            }
            table = new RemovalFractionTable(key.Locations, weapon, spacing);
            return AddBounded(SharedTables, key, table, ref _sharedTableCount, MaximumSharedTables);
        }

        private static int _sharedTableCount;
        private static int _targetTableCount;

        /// <summary>
        /// Adds to a bounded cache, clearing it when the count reaches the bound. The count is kept
        /// here, not read from <c>ConcurrentDictionary.Count</c>, which takes every lock in the
        /// dictionary: on a cache miss in the shot evaluator it cost 5.9% of all CPU in a profile of
        /// the Governance test (2026-11-11). Approximate under races, which only moves the moment of
        /// the next clear.
        /// </summary>
        private static RemovalFractionTable AddBounded<TKey>(
            System.Collections.Concurrent.ConcurrentDictionary<TKey, RemovalFractionTable> cache,
            TKey key,
            RemovalFractionTable table,
            ref int count,
            int maximum)
        {
            if (System.Threading.Volatile.Read(ref count) >= maximum)
            {
                cache.Clear();
                System.Threading.Volatile.Write(ref count, 0);
            }
            if (cache.TryAdd(key, table))
            {
                System.Threading.Interlocked.Increment(ref count);
                return table;
            }
            return cache.TryGetValue(key, out RemovalFractionTable existing) ? existing : table;
        }

        /// <summary>
        /// Yards between the nodes of the tables the live shot evaluator reads
        /// (<see cref="ForTarget"/>; 2026-11-11). Finer than <see cref="NodeSpacing"/>: the shot
        /// planner reads the take-out chance on its own to count shots, and take-out is a sum of
        /// normal tails, one per hit location, that drop out at different ranges, so 50-yard nodes
        /// put it 12-13% high at 80 yards for a degrading rifle under every interpolation tried
        /// (straight, log, logit, probit in 1/damage). 0 solves every range. Process-wide so the
        /// diagnostics can compare (DUEL_SHOT_REMOVAL_NODES).
        /// </summary>
        internal static float ShotNodeSpacing = 10f;

        private const int MaximumTargetTables = 16384;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<TargetKey, RemovalFractionTable>
            TargetTables = new();

        /// <summary>
        /// The shared table for <paramref name="weapon"/> against <paramref name="target"/> in worn
        /// armor <paramref name="wornArmor"/>, for the live shot evaluator (2026-11-11).
        ///
        /// <para>WHY. A CPU profile of an 8 v 8 squad battle, after the forecast moved to these
        /// tables, put 17.8% of planning CPU in the shot evaluator's own per-hit removal: a full
        /// hit-location walk with up to nine normal CDFs per location, for every shooter, weapon,
        /// target and exact range it scores. The value is the one these tables hold, so the shot
        /// evaluator reads them too, and the planner and the forecast price a hit the same way.</para>
        ///
        /// <para>The lookup is keyed by the target's unit location vector by REFERENCE
        /// (<see cref="BattleSoldier.UnitTakeOutTerms"/> is rebuilt when his injuries or stance
        /// change, so a new reference means a new state), the weapon and the worn armor, so the
        /// location walk to build the rescaled vector runs once per such state. The table behind it
        /// comes from <see cref="For"/>, so unhurt soldiers of one template share it.</para>
        /// </summary>
        internal static RemovalFractionTable ForTarget(
            BattleSoldier target,
            float wornArmor,
            RangedWeaponTemplate weapon)
        {
            TargetKey key = new(target.UnitTakeOutTerms, weapon, wornArmor);
            if (TargetTables.TryGetValue(key, out RemovalFractionTable table))
            {
                return table;
            }
            table = For(
                RemovalMath.BuildTakeOutLocationTerms(
                    target, wornArmor, weapon.ArmorMultiplier, weapon.WoundMultiplier),
                weapon,
                ShotNodeSpacing);
            return AddBounded(TargetTables, key, table, ref _targetTableCount, MaximumTargetTables);
        }

        /// <summary>The unit location vector compares by reference (record struct over object).</summary>
        private readonly record struct TargetKey(object UnitTerms, RangedWeaponTemplate Weapon, float WornArmor);

        /// <summary>A weapon (by reference), a copy of a location vector, and the node spacing.</summary>
        private sealed class SharedKey : IEquatable<SharedKey>
        {
            private readonly RangedWeaponTemplate _weapon;
            private readonly float _spacing;
            private readonly int _hash;

            internal TakeOutLocationTerm[] Locations { get; }

            internal SharedKey(
                RangedWeaponTemplate weapon,
                IReadOnlyList<TakeOutLocationTerm> locations,
                float spacing)
            {
                _weapon = weapon;
                _spacing = spacing;
                Locations = new TakeOutLocationTerm[locations.Count];
                HashCode hash = new();
                hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(weapon));
                hash.Add(spacing);
                for (int index = 0; index < locations.Count; index++)
                {
                    TakeOutLocationTerm term = locations[index];
                    Locations[index] = term;
                    hash.Add(term.Weight);
                    hash.Add(term.PenetrationThreshold);
                    hash.Add(term.RequiredRatio);
                    hash.Add(term.ZeroProgressThreshold);
                    hash.Add(term.EffectiveArmor);
                    hash.Add(term.DamagePerRatio);
                    hash.Add(term.Gap);
                    hash.Add(term.NaturalArmor);
                }
                _hash = hash.ToHashCode();
            }

            public bool Equals(SharedKey other)
            {
                if (other is null
                    || _hash != other._hash
                    || !ReferenceEquals(_weapon, other._weapon)
                    || !_spacing.Equals(other._spacing)
                    || Locations.Length != other.Locations.Length)
                {
                    return false;
                }
                for (int index = 0; index < Locations.Length; index++)
                {
                    TakeOutLocationTerm mine = Locations[index];
                    TakeOutLocationTerm theirs = other.Locations[index];
                    if (!mine.Weight.Equals(theirs.Weight)
                        || !mine.PenetrationThreshold.Equals(theirs.PenetrationThreshold)
                        || !mine.RequiredRatio.Equals(theirs.RequiredRatio)
                        || !mine.ZeroProgressThreshold.Equals(theirs.ZeroProgressThreshold)
                        || !mine.EffectiveArmor.Equals(theirs.EffectiveArmor)
                        || !mine.DamagePerRatio.Equals(theirs.DamagePerRatio)
                        || !mine.Gap.Equals(theirs.Gap)
                        || !mine.NaturalArmor.Equals(theirs.NaturalArmor))
                    {
                        return false;
                    }
                }
                return true;
            }

            public override bool Equals(object obj) => Equals(obj as SharedKey);

            public override int GetHashCode() => _hash;
        }

        private readonly IReadOnlyList<TakeOutLocationTerm> _locations;
        private readonly RangedWeaponTemplate _weapon;
        private readonly Node _flat;
        private readonly float _spacing;
        private Node[] _nodes;

        internal RemovalFractionTable(
            IReadOnlyList<TakeOutLocationTerm> locations,
            RangedWeaponTemplate weapon)
            : this(locations, weapon, NodeSpacing)
        {
        }

        internal RemovalFractionTable(
            IReadOnlyList<TakeOutLocationTerm> locations,
            RangedWeaponTemplate weapon,
            float spacing)
        {
            _locations = locations;
            _weapon = weapon;
            _spacing = spacing;
            // A weapon whose damage does not fall with range has one value at every range.
            _flat = weapon.DoesDamageDegradeWithRange
                ? null
                : Solve(0f);
        }

        /// <summary>The removal fraction per hit (take-out plus wound progress, combined).</summary>
        internal float At(float range)
        {
            if (_flat != null)
            {
                return _flat.Fraction;
            }
            if (!TryBracket(range, out Node lower, out Node upper, out float t))
            {
                return Solve(range).Fraction;
            }
            return lower.Fraction + (t * (upper.Fraction - lower.Fraction));
        }

        /// <summary>
        /// The two halves of <see cref="At"/>, each interpolated on the same nodes: the take-out
        /// chance (the shot planner counts shots by it) and the wound progress beyond it. The live
        /// shot evaluator reads this (<see cref="RangedShotEvaluator.CalculateRangedHitRemoval"/>).
        /// </summary>
        internal (float TakeOut, float WoundProgress) TermsAt(float range)
        {
            if (_flat != null)
            {
                return (_flat.TakeOut, _flat.Progress);
            }
            if (!TryBracket(range, out Node lower, out Node upper, out float t))
            {
                Node exact = Solve(range);
                return (exact.TakeOut, exact.Progress);
            }
            return (
                lower.TakeOut + (t * (upper.TakeOut - lower.TakeOut)),
                lower.Progress + (t * (upper.Progress - lower.Progress)));
        }

        private bool TryBracket(float range, out Node lower, out Node upper, out float t)
        {
            lower = upper = null;
            t = 0f;
            float spacing = _spacing;
            if (spacing <= 0f)
            {
                return false;
            }
            float position = Math.Max(0f, range) / spacing;
            int index = (int)position;
            Node[] nodes = _nodes ??= CreateNodes(spacing);
            if (index + 1 >= nodes.Length)
            {
                // Past the last node pair: beyond the weapon's range, where the damage
                // coefficient is zero or negative and the fraction is zero.
                return false;
            }
            lower = NodeAt(nodes, index, spacing);
            upper = NodeAt(nodes, index + 1, spacing);
            t = position - index;
            return true;
        }

        private Node[] CreateNodes(float spacing)
        {
            // One node at or past the maximum range, so every in-range position has two nodes.
            int count = (int)Math.Ceiling(Math.Max(0f, _weapon.MaximumRange) / spacing) + 2;
            return new Node[count];
        }

        private Node NodeAt(Node[] nodes, int index, float spacing)
        {
            Node node = System.Threading.Volatile.Read(ref nodes[index]);
            if (node == null)
            {
                node = Solve(index * spacing);
                System.Threading.Volatile.Write(ref nodes[index], node);
            }
            return node;
        }

        private Node Solve(float range)
        {
            (float takeOut, float progress) = RemovalMath.EvaluateRemovalTerms(
                _locations,
                BattleModifiersUtil.CalculateDamageAtRange(_weapon, range),
                RemovalMath.LocationFillCombineExponent);
            return new Node(takeOut, progress, RemovalMath.CombineRemovalFraction(takeOut, progress));
        }

        /// <summary>One solved node. Immutable, so a reference to it is published without a lock.</summary>
        private sealed record Node(float TakeOut, float Progress, float Fraction);
    }
}
