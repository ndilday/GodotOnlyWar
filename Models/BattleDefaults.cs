using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Models
{
    /// <summary>
    /// Resolves the default battle resources that combat logic references by name
    /// into stable, typed accessors. Resolution happens once at rules-database load
    /// (via <see cref="GameRulesData"/>) and fails fast with a clear error if a
    /// required template is missing or ambiguous, instead of producing a runtime null
    /// inside <c>BattleTurnResolver</c>.
    ///
    /// The unarmed-melee defaults are the reason this registry exists: the rules DB
    /// intentionally carries two melee-weapon templates both named "Fist" — one keyed
    /// to the Imperial "Fist" skill (used by Space Marines) and one keyed to the
    /// catch-all "Generic Melee" skill (used by non-Imperial creatures). The name
    /// alone is therefore not a stable key; these are disambiguated by their related
    /// base skill. See TDD §8.3 / §4.1.1.
    /// </summary>
    internal sealed class BattleDefaults
    {
        /// <summary>Unarmed-melee weapon for soldiers using the Imperial "Fist" skill (Space Marines).</summary>
        public MeleeWeaponTemplate ImperialUnarmedWeapon { get; }

        /// <summary>Unarmed-melee weapon for soldiers using the catch-all "Generic Melee" skill (non-Imperial factions).</summary>
        public MeleeWeaponTemplate GenericUnarmedWeapon { get; }

        public BattleDefaults(IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeaponTemplates,
                              NamedSkillRegistry skills)
        {
            ImperialUnarmedWeapon = ResolveUnarmed(meleeWeaponTemplates, "Fist", skills.Fist);
            GenericUnarmedWeapon = ResolveUnarmed(meleeWeaponTemplates, "Fist", skills.GenericMelee);
        }

        private static MeleeWeaponTemplate ResolveUnarmed(
            IReadOnlyDictionary<int, MeleeWeaponTemplate> templates, string name, BaseSkill relatedSkill)
        {
            List<MeleeWeaponTemplate> matches = templates?.Values
                .Where(t => t.Name == name && t.RelatedSkill == relatedSkill)
                .ToList() ?? new List<MeleeWeaponTemplate>();
            if (matches.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Required melee-weapon template '{name}' using the '{relatedSkill?.Name}' skill " +
                    $"was not found in the rules database.");
            }
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Required melee-weapon template '{name}' using the '{relatedSkill?.Name}' skill " +
                    $"is ambiguous; {matches.Count} entries share that name and skill.");
            }
            return matches[0];
        }
    }
}
