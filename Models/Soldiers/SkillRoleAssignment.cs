using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Soldiers
{
    /// <summary>
    /// Code-owned meanings that need a particular skill. Rules data maps these roles to
    /// data-owned <see cref="BaseSkill.SkillKey"/> values.
    /// </summary>
    public enum SkillRole
    {
        Stealth = 0,
        Tactics = 1,
        EngineeringFortification = 2,
        PowerArmor = 3,
        Teaching = 4
    }

    public static class SkillRoleKeys
    {
        public const string Stealth = "stealth";
        public const string Tactics = "tactics";
        public const string EngineeringFortification = "engineering_fortification";
        public const string PowerArmor = "power_armor";
        public const string Teaching = "teaching";

        public static string For(SkillRole role) => role switch
        {
            SkillRole.Stealth => Stealth,
            SkillRole.Tactics => Tactics,
            SkillRole.EngineeringFortification => EngineeringFortification,
            SkillRole.PowerArmor => PowerArmor,
            SkillRole.Teaching => Teaching,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

        public static string DisplayName(SkillRole role) => role switch
        {
            SkillRole.Stealth => "Stealth",
            SkillRole.Tactics => "Tactics",
            SkillRole.EngineeringFortification => "Engineering (Fortification)",
            SkillRole.PowerArmor => "Power Armor",
            SkillRole.Teaching => "Teaching",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

        public static bool TryParse(string value, out SkillRole role)
        {
            role = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            foreach (SkillRole candidate in Enum.GetValues<SkillRole>())
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

    /// <summary>
    /// A data-owned binding between a gameplay role and a stable skill key.
    /// </summary>
    public sealed record SkillRoleAssignment(string RoleKey, string SkillKey);
}
