using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Soldiers
{
    /// <summary>
    /// Resolves the small set of code-owned skill roles into stable, typed accessors.
    /// Resolution happens once at rules-database load and fails fast with a targeted
    /// error if a role is missing, duplicated, or points at an unknown skill key.
    /// </summary>
    internal sealed class NamedSkillRegistry
    {
        private static readonly SkillRole[] RequiredRoles =
            Enum.GetValues<SkillRole>();

        private readonly IReadOnlyDictionary<SkillRole, BaseSkill> _skills;

        public BaseSkill Stealth => this[SkillRole.Stealth];
        public BaseSkill Tactics => this[SkillRole.Tactics];
        public BaseSkill EngineeringFortification => this[SkillRole.EngineeringFortification];
        public BaseSkill PowerArmor => this[SkillRole.PowerArmor];
        public BaseSkill Teaching => this[SkillRole.Teaching];

        // Compatibility overload for callers that do not have a separate assignment table.
        // It still resolves by SkillKey; it never falls back to display names.
        public NamedSkillRegistry(IReadOnlyDictionary<int, BaseSkill> baseSkillMap)
            : this(baseSkillMap, null)
        {
        }

        public NamedSkillRegistry(
            IReadOnlyDictionary<int, BaseSkill> baseSkillMap,
            IEnumerable<SkillRoleAssignment> assignments)
        {
            if (baseSkillMap == null) throw new ArgumentNullException(nameof(baseSkillMap));

            Dictionary<string, BaseSkill> skillsByKey = [];
            foreach (BaseSkill skill in baseSkillMap.Values)
            {
                if (string.IsNullOrWhiteSpace(skill.SkillKey)) continue;
                if (!skillsByKey.TryAdd(skill.SkillKey, skill))
                {
                    throw new InvalidOperationException(
                        $"Rules database contains duplicate base skill key '{skill.SkillKey}'.");
                }
            }

            IReadOnlyList<SkillRoleAssignment> effectiveAssignments = assignments?.ToList()
                ?? CreateDefaultAssignments();
            Dictionary<SkillRole, string> skillKeysByRole = [];
            foreach (SkillRoleAssignment assignment in effectiveAssignments)
            {
                if (!SkillRoleKeys.TryParse(assignment.RoleKey, out SkillRole role))
                {
                    throw new InvalidOperationException(
                        $"Unknown skill role '{assignment.RoleKey}'.");
                }
                if (string.IsNullOrWhiteSpace(assignment.SkillKey))
                {
                    throw new InvalidOperationException(
                        $"Skill role '{assignment.RoleKey}' has no skill key.");
                }
                if (!skillKeysByRole.TryAdd(role, assignment.SkillKey))
                {
                    throw new InvalidOperationException(
                        $"Skill role '{assignment.RoleKey}' is assigned more than once.");
                }
            }

            Dictionary<SkillRole, BaseSkill> resolved = [];
            foreach (SkillRole role in RequiredRoles)
            {
                if (!skillKeysByRole.TryGetValue(role, out string skillKey))
                {
                    throw new InvalidOperationException(
                        $"Required skill role '{SkillRoleKeys.For(role)}' "
                        + $"({SkillRoleKeys.DisplayName(role)}) is not assigned.");
                }
                if (!skillsByKey.TryGetValue(skillKey, out BaseSkill skill))
                {
                    throw new InvalidOperationException(
                        $"Required skill role '{SkillRoleKeys.For(role)}' "
                        + $"({SkillRoleKeys.DisplayName(role)}) references missing "
                        + $"skill key '{skillKey}'.");
                }
                resolved[role] = skill;
            }
            _skills = resolved;
        }

        public BaseSkill this[SkillRole role] =>
            _skills.TryGetValue(role, out BaseSkill skill)
                ? skill
                : throw new InvalidOperationException(
                    $"Skill role '{SkillRoleKeys.For(role)}' is not assigned.");

        public static IReadOnlyList<SkillRoleAssignment> CreateDefaultAssignments() =>
            RequiredRoles
                .Select(role => new SkillRoleAssignment(
                    SkillRoleKeys.For(role), SkillRoleKeys.For(role)))
                .ToList();
    }
}
