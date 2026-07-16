using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Coordinates complementary squad roles for withdrawal and pursuit. Geometry and role
    /// selection are deterministic; action construction is delegated to BattleSquadPlanner.
    /// </summary>
    public sealed class BattleForcePlanner
    {
        private const double DistanceTieTolerance = 0.0001;
        private readonly BattleSquadPlanner _squadPlanner;

        public sealed record CoverCandidate(
            int SquadId,
            double NearestEnemyDistance,
            bool RangedCoverEligible);

        public sealed record CoverAssignment(
            int? SquadId,
            bool Rotated,
            string Reason,
            IReadOnlyList<CoverCandidate> Candidates);

        public BattleForcePlanner(BattleSquadPlanner squadPlanner)
        {
            _squadPlanner = squadPlanner
                ?? throw new ArgumentNullException(nameof(squadPlanner));
        }

        public CoverAssignment PrepareFightingWithdrawal(
            BattleSideState sideState,
            IReadOnlyCollection<BattleSquad> friendlySquads,
            IReadOnlyCollection<BattleSquad> enemySquads)
        {
            ArgumentNullException.ThrowIfNull(sideState);
            List<BattleSquad> friendly = ActiveSquads(friendlySquads);
            List<BattleSquad> enemies = ActiveSquads(enemySquads);

            sideState.WithdrawalHeading ??= SelectWithdrawalHeading(friendly, enemies);
            CoverAssignment assignment = SelectCover(
                BuildCoverCandidates(friendly, enemies),
                sideState.CoveringSquadId);
            sideState.CoveringSquadId = assignment.SquadId;

            foreach (BattleSquad squad in friendly)
            {
                if (assignment.SquadId == squad.Id)
                {
                    _squadPlanner.PrepareCoverActions(squad);
                }
                else
                {
                    _squadPlanner.PrepareBoundActions(squad, sideState.WithdrawalHeading.Value);
                }
            }

            return assignment;
        }

        public void PrepareRearGuardWithdrawal(
            BattleSideState sideState,
            IReadOnlyCollection<BattleSquad> friendlySquads,
            IReadOnlyCollection<BattleSquad> enemySquads)
        {
            ArgumentNullException.ThrowIfNull(sideState);
            List<BattleSquad> friendly = ActiveSquads(friendlySquads);
            sideState.WithdrawalHeading ??= SelectWithdrawalHeading(
                friendly,
                ActiveSquads(enemySquads));
            sideState.CoveringSquadId = null;

            foreach (BattleSquad squad in friendly)
            {
                if (sideState.RearGuardSquadId == squad.Id)
                {
                    _squadPlanner.PrepareRearGuardActions(squad);
                }
                else
                {
                    _squadPlanner.PrepareBoundActions(squad, sideState.WithdrawalHeading.Value);
                }
            }
        }

        public void PreparePursuit(
            IReadOnlyCollection<BattleSquad> pursuingSquads,
            IReadOnlyCollection<BattleSquad> withdrawingSquads,
            PursuitPosture posture)
        {
            List<BattleSquad> targets = ActiveSquads(withdrawingSquads);
            foreach (BattleSquad squad in ActiveSquads(pursuingSquads))
            {
                _squadPlanner.PreparePursuitActions(squad, posture, targets);
            }
        }

        /// <summary>
        /// Selects and fixes the eight-way heading away from the enemy centroid. If centroids
        /// overlap, evaluates all headings after one full Run and breaks exact ties by heading.
        /// </summary>
        public static ushort SelectWithdrawalHeading(
            IReadOnlyCollection<BattleSquad> friendlySquads,
            IReadOnlyCollection<BattleSquad> enemySquads)
        {
            List<BattleSoldier> friendly = ActiveSoldiers(friendlySquads);
            List<BattleSoldier> enemies = ActiveSoldiers(enemySquads);
            if (friendly.Count == 0 || enemies.Count == 0)
            {
                return 0;
            }

            (double X, double Y) friendlyCentroid = Centroid(friendly);
            (double X, double Y) enemyCentroid = Centroid(enemies);
            double awayX = friendlyCentroid.X - enemyCentroid.X;
            double awayY = friendlyCentroid.Y - enemyCentroid.Y;

            if ((awayX * awayX) + (awayY * awayY) > DistanceTieTolerance)
            {
                return Enumerable.Range(0, BattleOrientation.HeadingCount)
                    .Select(heading => new
                    {
                        Heading = (ushort)heading,
                        Score = HeadingDot((ushort)heading, awayX, awayY)
                    })
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Heading)
                    .First()
                    .Heading;
            }

            return Enumerable.Range(0, BattleOrientation.HeadingCount)
                .Select(heading => new
                {
                    Heading = (ushort)heading,
                    Score = MinimumProjectedEnemyDistance(
                        friendly,
                        enemies,
                        (ushort)heading)
                })
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Heading)
                .First()
                .Heading;
        }

        public static Tuple<int, int> GetHeadingVector(ushort heading)
        {
            return (heading % BattleOrientation.HeadingCount) switch
            {
                0 => new Tuple<int, int>(0, 1),
                1 => new Tuple<int, int>(1, 1),
                2 => new Tuple<int, int>(1, 0),
                3 => new Tuple<int, int>(1, -1),
                4 => new Tuple<int, int>(0, -1),
                5 => new Tuple<int, int>(-1, -1),
                6 => new Tuple<int, int>(-1, 0),
                _ => new Tuple<int, int>(-1, 1)
            };
        }

        public static IReadOnlyList<CoverCandidate> BuildCoverCandidates(
            IReadOnlyCollection<BattleSquad> friendlySquads,
            IReadOnlyCollection<BattleSquad> enemySquads)
        {
            List<BattleSoldier> enemies = ActiveSoldiers(enemySquads);
            return ActiveSquads(friendlySquads)
                .Select(squad =>
                {
                    double distance = squad.AbleSoldiers
                        .SelectMany(friendly => enemies.Select(enemy =>
                            Distance(friendly.TopLeft, enemy.TopLeft)))
                        .DefaultIfEmpty(double.PositiveInfinity)
                        .Min();
                    bool eligible = !squad.IsInMelee
                        && double.IsFinite(distance)
                        && squad.AbleSoldiers.Any(soldier =>
                            soldier.EquippedRangedWeapons.Any(weapon =>
                                weapon.LoadedAmmo > 0
                                && weapon.Template.MaximumRange >= distance));
                    return new CoverCandidate(squad.Id, distance, eligible);
                })
                .OrderBy(candidate => candidate.SquadId)
                .ToArray();
        }

        public static CoverAssignment SelectCover(
            IReadOnlyList<CoverCandidate> candidates,
            int? incumbentSquadId)
        {
            IReadOnlyList<CoverCandidate> ordered = (candidates ?? [])
                .OrderBy(candidate => candidate.SquadId)
                .ToArray();
            if (ordered.Count == 0)
            {
                return new CoverAssignment(null, incumbentSquadId.HasValue,
                    "no_active_squads", ordered);
            }

            CoverCandidate incumbent = incumbentSquadId.HasValue
                ? ordered.FirstOrDefault(candidate => candidate.SquadId == incumbentSquadId.Value)
                : null;
            double closestDistance = ordered.Min(candidate => candidate.NearestEnemyDistance);
            bool incumbentIsClosest = incumbent != null
                && incumbent.NearestEnemyDistance <= closestDistance + DistanceTieTolerance;

            if (incumbent != null && incumbent.RangedCoverEligible && !incumbentIsClosest)
            {
                return new CoverAssignment(
                    incumbent.SquadId,
                    false,
                    "incumbent_not_closest",
                    ordered);
            }

            CoverCandidate selected = ordered
                .Where(candidate => candidate.RangedCoverEligible
                    && (incumbent == null
                        || candidate.SquadId != incumbent.SquadId
                        || ordered.Count(candidate => candidate.RangedCoverEligible) == 1))
                .OrderByDescending(candidate => candidate.NearestEnemyDistance)
                .ThenBy(candidate => candidate.SquadId)
                .FirstOrDefault();

            if (selected == null)
            {
                return new CoverAssignment(null, incumbentSquadId.HasValue,
                    "no_ranged_cover", ordered);
            }

            bool rotated = incumbentSquadId.HasValue && selected.SquadId != incumbentSquadId.Value;
            string reason = incumbent == null
                ? "farthest_eligible"
                : incumbent.RangedCoverEligible
                    ? rotated ? "incumbent_became_closest" : "only_eligible_cover"
                    : "incumbent_ineligible";
            return new CoverAssignment(selected.SquadId, rotated, reason, ordered);
        }

        private static List<BattleSquad> ActiveSquads(
            IReadOnlyCollection<BattleSquad> squads)
        {
            return (squads ?? Array.Empty<BattleSquad>())
                .Where(squad => squad != null
                    && squad.Status == BattleSquadStatus.Active
                    && squad.AbleSoldiers.Count > 0)
                .OrderBy(squad => squad.Id)
                .ToList();
        }

        private static List<BattleSoldier> ActiveSoldiers(
            IReadOnlyCollection<BattleSquad> squads)
        {
            return ActiveSquads(squads)
                .SelectMany(squad => squad.AbleSoldiers)
                .OrderBy(soldier => soldier.Soldier.Id)
                .ToList();
        }

        private static (double X, double Y) Centroid(IReadOnlyCollection<BattleSoldier> soldiers)
        {
            return (soldiers.Average(soldier => soldier.TopLeft.Item1),
                soldiers.Average(soldier => soldier.TopLeft.Item2));
        }

        private static double HeadingDot(ushort heading, double x, double y)
        {
            Tuple<int, int> vector = GetHeadingVector(heading);
            double length = Math.Sqrt((vector.Item1 * vector.Item1) + (vector.Item2 * vector.Item2));
            return ((vector.Item1 * x) + (vector.Item2 * y)) / length;
        }

        private static double MinimumProjectedEnemyDistance(
            IReadOnlyCollection<BattleSoldier> friendly,
            IReadOnlyCollection<BattleSoldier> enemies,
            ushort heading)
        {
            Tuple<int, int> vector = GetHeadingVector(heading);
            double vectorLength = Math.Sqrt(
                (vector.Item1 * vector.Item1) + (vector.Item2 * vector.Item2));
            return friendly
                .SelectMany(soldier =>
                {
                    double speed = soldier.GetMoveSpeed();
                    double projectedX = soldier.TopLeft.Item1 + (vector.Item1 * speed / vectorLength);
                    double projectedY = soldier.TopLeft.Item2 + (vector.Item2 * speed / vectorLength);
                    return enemies.Select(enemy =>
                    {
                        double dx = projectedX - enemy.TopLeft.Item1;
                        double dy = projectedY - enemy.TopLeft.Item2;
                        return Math.Sqrt((dx * dx) + (dy * dy));
                    });
                })
                .DefaultIfEmpty(0)
                .Min();
        }

        private static double Distance(Tuple<int, int> first, Tuple<int, int> second)
        {
            int dx = first.Item1 - second.Item1;
            int dy = first.Item2 - second.Item2;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }
    }
}
