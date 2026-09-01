using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Soldiers.Ratings
{
    /// <summary>
    /// A code-owned capability that a gameplay subsystem needs from a soldier
    /// evaluation. The rules data chooses which open-ended rating supplies it.
    /// </summary>
    public enum RatingConsumerRole
    {
        MeleeCombat = 0,
        RangedCombat = 1,
        CommandLeadership = 2,
        MedicalCapacity = 3,
        TechnicalCapability = 4,
        SpiritualCapability = 5,
        AncientService = 6
    }

    public static class RatingConsumerRoleKeys
    {
        public const string MeleeCombat = "melee_combat";
        public const string RangedCombat = "ranged_combat";
        public const string CommandLeadership = "command_leadership";
        public const string MedicalCapacity = "medical_capacity";
        public const string TechnicalCapability = "technical_capability";
        public const string SpiritualCapability = "spiritual_capability";
        public const string AncientService = "ancient_service";

        public static string For(RatingConsumerRole role) => role switch
        {
            RatingConsumerRole.MeleeCombat => MeleeCombat,
            RatingConsumerRole.RangedCombat => RangedCombat,
            RatingConsumerRole.CommandLeadership => CommandLeadership,
            RatingConsumerRole.MedicalCapacity => MedicalCapacity,
            RatingConsumerRole.TechnicalCapability => TechnicalCapability,
            RatingConsumerRole.SpiritualCapability => SpiritualCapability,
            RatingConsumerRole.AncientService => AncientService,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

        public static bool TryParse(string value, out RatingConsumerRole role)
        {
            role = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            foreach (RatingConsumerRole candidate in Enum.GetValues<RatingConsumerRole>())
            {
                if (string.Equals(value, For(candidate), StringComparison.OrdinalIgnoreCase))
                {
                    role = candidate;
                    return true;
                }
            }
            return false;
        }
    }

    public sealed record RatingConsumerAssignment(string RoleKey, string RatingKey);

    /// <summary>
    /// Resolves code-owned consumer roles to data-owned rating keys. This is the
    /// boundary that lets a mod rename or replace ordinary ratings without making
    /// gameplay code know those names.
    /// </summary>
    public sealed class RatingConsumerBindings
    {
        private static readonly RatingConsumerRole[] RequiredRoles =
            Enum.GetValues<RatingConsumerRole>();

        private readonly IReadOnlyDictionary<RatingConsumerRole, string> _ratingKeys;

        public RatingConsumerBindings(IEnumerable<RatingConsumerAssignment> assignments)
        {
            Dictionary<RatingConsumerRole, string> bindings = [];
            foreach (RatingConsumerAssignment assignment in assignments ?? [])
            {
                if (!RatingConsumerRoleKeys.TryParse(assignment.RoleKey, out RatingConsumerRole role))
                {
                    throw new InvalidOperationException(
                        $"Unknown rating consumer role '{assignment.RoleKey}'.");
                }
                if (string.IsNullOrWhiteSpace(assignment.RatingKey))
                {
                    throw new InvalidOperationException(
                        $"Rating consumer role '{assignment.RoleKey}' has no rating key.");
                }
                if (!bindings.TryAdd(role, assignment.RatingKey))
                {
                    throw new InvalidOperationException(
                        $"Rating consumer role '{assignment.RoleKey}' is assigned more than once.");
                }
            }
            _ratingKeys = bindings;
        }

        public string this[RatingConsumerRole role] => GetRatingKey(role);

        public string GetRatingKey(RatingConsumerRole role) =>
            _ratingKeys.TryGetValue(role, out string key)
                ? key
                : throw new InvalidOperationException(
                    $"Rating consumer role '{RatingConsumerRoleKeys.For(role)}' is not assigned.");

        public bool TryGetRatingKey(RatingConsumerRole role, out string ratingKey) =>
            _ratingKeys.TryGetValue(role, out ratingKey);

        public float Get(SoldierEvaluation evaluation, RatingConsumerRole role) =>
            evaluation?[GetRatingKey(role)] ?? 0f;

        public bool TryGet(SoldierEvaluation evaluation, RatingConsumerRole role, out float value)
        {
            value = 0f;
            if (!TryGetRatingKey(role, out string ratingKey) || evaluation == null)
            {
                return false;
            }
            return evaluation.Ratings.TryGetValue(ratingKey, out value);
        }

        public IReadOnlyDictionary<RatingConsumerRole, string> AsDictionary() => _ratingKeys;

        public static RatingConsumerBindings CreateDefault() => new(
            CreateDefaultAssignments());

        public static IReadOnlyList<RatingConsumerAssignment> CreateDefaultAssignments() =>
            RequiredRoles.Select(role => new RatingConsumerAssignment(
                RatingConsumerRoleKeys.For(role), DefaultRatingKey(role))).ToList();

        private static string DefaultRatingKey(RatingConsumerRole role) => role switch
        {
            RatingConsumerRole.MeleeCombat => RatingKeys.Melee,
            RatingConsumerRole.RangedCombat => RatingKeys.Ranged,
            RatingConsumerRole.CommandLeadership => RatingKeys.Leadership,
            RatingConsumerRole.MedicalCapacity => RatingKeys.Medical,
            RatingConsumerRole.TechnicalCapability => RatingKeys.Tech,
            RatingConsumerRole.SpiritualCapability => RatingKeys.Piety,
            RatingConsumerRole.AncientService => RatingKeys.Ancient,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

        public static IReadOnlyList<RatingConsumerRole> GetRequiredRoles() => RequiredRoles;
    }
}
