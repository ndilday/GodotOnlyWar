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
    private const float MinimumScreenImminence = 0.015f;

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
        Dictionary<int, BattleSquadCapabilityProfile> profiles = firstActive
            .Concat(secondActive)
            .ToDictionary(squad => squad.Id, BuildProfile);
        Dictionary<int, SquadEngagementFrame> frames = [];

        BuildSide(firstActive, secondActive, profiles, constraints, frames);
        BuildSide(secondActive, firstActive, profiles, constraints, frames);
        AssignScreens(firstActive, secondActive, profiles, frames);
        AssignScreens(secondActive, firstActive, profiles, frames);
        return new PairedFrame(frames, profiles);
    }

    internal static BattleSquadCapabilityProfile BuildProfile(BattleSquad squad)
    {
        List<BattleSoldier> able = squad?.AbleSoldiers
            .Where(soldier => soldier.TopLeft.HasValue)
            .OrderBy(soldier => soldier.Soldier.Id)
            .ToList() ?? [];
        if (able.Count == 0)
        {
            return new BattleSquadCapabilityProfile(
                squad?.Id ?? 0, 0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<int, float>());
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
            float meleeShare = usableMelee ? authoredMelee : 0;
            float rangedShare = usableRanged ? 1f - authoredMelee : 0;
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
        return new BattleSquadCapabilityProfile(
            squad.Id,
            total,
            ranged,
            melee,
            effectiveMelee,
            lower,
            upper,
            squad.GetSquadMove(),
            perimeter,
            groups);
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
                float imminence = 1f / Math.Max(1, distance);
                raw[target.Id] = imminence * (float)Math.Sqrt(Math.Max(1, targetBv));
            }
            float totalWeight = raw.Values.Sum();
            Dictionary<int, float> weights = raw
                .OrderBy(entry => entry.Key)
                .ToDictionary(
                    entry => entry.Key,
                    entry => totalWeight > 0 ? entry.Value / totalWeight : 0);
            // During an organized withdrawal, Bound squads cannot return fire while the Cover or
            // RearGuard can. Preserve the pursuit policy of closing on the formation that is
            // actively protecting the withdrawal; otherwise use the normal highest-imminence
            // counterpart. Read the paired current-turn role constraints rather than mutable
            // BattleSquad role state so both sides use the same frozen declaration snapshot.
            HashSet<int> coveringTargetIds = constraint.Role == EngagementSquadRole.Pursuit
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
            float quarryRunSpeed = constraint.Role == EngagementSquadRole.Pursuit
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
        IDictionary<int, SquadEngagementFrame> frames)
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
            let imminence = threatProfile.MoveSpeed / Math.Max(1, threatDistance)
            where imminence >= MinimumScreenImminence
            from screener in friendly
            let screenerProfile = profiles[screener.Id]
            where screener.Id != principal.Id
                && screenerProfile.EffectiveMeleeFraction > protectedProfile.EffectiveMeleeFraction
            let preventable = Math.Min(threatProfile.UsableMeleeBattleValue,
                protectedProfile.TotalAbleBattleValue) * imminence
            let incumbent = screener.LastScreenThreatSquadId == threat.Id
                && screener.LastProtectedSquadId == principal.Id
            orderby preventable descending, imminence descending, incumbent descending,
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
        (squads ?? Array.Empty<BattleSquad>())
            .Where(squad => squad != null
                && squad.Status == BattleSquadStatus.Active
                && squad.AbleSoldiers.Count > 0)
            .OrderBy(squad => squad.Id)
            .ToList();
}
