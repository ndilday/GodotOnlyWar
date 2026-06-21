using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Soldiers
{
    /// <summary>
    /// Resolves the small set of base skills that game logic references by name
    /// (rather than by player choice) into stable, typed accessors. Resolution
    /// happens once at rules-database load and fails fast with a clear error if a
    /// required skill is missing or ambiguous, instead of producing a null
    /// reference at runtime deep inside a mission or battle step.
    ///
    /// This is the first validated registry described in TDD §8.3 / §4.1: code that
    /// needs a specific named rules entry should resolve it through a registry like
    /// this one at load time, not via scattered <c>First(s =&gt; s.Name == "...")</c>
    /// lookups.
    /// </summary>
    internal sealed class NamedSkillRegistry
    {
        public BaseSkill Stealth { get; }
        public BaseSkill Tactics { get; }
        public BaseSkill Fist { get; }
        public BaseSkill GenericMelee { get; }
        public BaseSkill EngineeringFortification { get; }

        public NamedSkillRegistry(IReadOnlyDictionary<int, BaseSkill> baseSkillMap)
        {
            Stealth = Resolve(baseSkillMap, "Stealth");
            Tactics = Resolve(baseSkillMap, "Tactics");
            Fist = Resolve(baseSkillMap, "Fist");
            GenericMelee = Resolve(baseSkillMap, "Generic Melee");
            EngineeringFortification = Resolve(baseSkillMap, "Engineering (Fortification)");
        }

        private static BaseSkill Resolve(IReadOnlyDictionary<int, BaseSkill> baseSkillMap, string name)
        {
            List<BaseSkill> matches = baseSkillMap.Values.Where(s => s.Name == name).ToList();
            if (matches.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Required base skill '{name}' was not found in the rules database.");
            }
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Required base skill '{name}' is ambiguous; {matches.Count} entries share that name.");
            }
            return matches[0];
        }
    }
}
