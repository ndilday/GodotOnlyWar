using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;

namespace OnlyWar.Models
{
    /// <summary>
    /// Resolves the soldier and squad templates that chapter generation references
    /// by name into stable, typed accessors. Resolution happens once at rules-database
    /// load (via <see cref="GameRulesData"/>) and fails fast with a clear error if a
    /// required template is missing or ambiguous, instead of producing a runtime null
    /// or wrong-template assignment deep inside <c>NewChapterBuilder</c>.
    ///
    /// This is the chapter-generation sibling of <see cref="NamedSkillRegistry"/>;
    /// see TDD §8.3 / §4.1 for the validated-registry pattern these are migrating toward.
    /// </summary>
    internal sealed class ChapterGenerationTemplates
    {
        // Command / HQ
        public SoldierTemplate ChapterMaster { get; }
        public SoldierTemplate Captain { get; }

        // Honor specialists
        public SoldierTemplate Champion { get; }
        public SoldierTemplate Ancient { get; }

        // Librarius
        public SoldierTemplate MasterOfTheLibrarium { get; }
        public SoldierTemplate Codicier { get; }
        public SoldierTemplate Lexicanium { get; }

        // Armory
        public SoldierTemplate MasterOfTheForge { get; }
        public SoldierTemplate Techmarine { get; }

        // Apothecarion
        public SoldierTemplate MasterOfTheApothecarion { get; }
        public SoldierTemplate Apothecary { get; }

        // Reclusium
        public SoldierTemplate MasterOfSanctity { get; }
        public SoldierTemplate Chaplain { get; }

        // Line marines and their sergeants
        public SoldierTemplate Veteran { get; }
        public SoldierTemplate VeteranSergeant { get; }
        public SoldierTemplate TacticalMarine { get; }
        public SoldierTemplate TacticalSergeant { get; }
        public SoldierTemplate AssaultMarine { get; }
        public SoldierTemplate AssaultSergeant { get; }
        public SoldierTemplate DevastatorMarine { get; }
        public SoldierTemplate DevastatorSergeant { get; }
        public SoldierTemplate ScoutMarine { get; }
        public SoldierTemplate ScoutSergeant { get; }

        // Squad templates
        public SquadTemplate TacticalSquad { get; }
        public SquadTemplate AssaultSquad { get; }
        public SquadTemplate DevastatorSquad { get; }
        public SquadTemplate ScoutSquad { get; }

        // Chapter-level specialist squad templates (used to locate the generated
        // chapter HQ squads by template identity rather than display name).
        public SquadTemplate Librarius { get; }
        public SquadTemplate Armory { get; }
        public SquadTemplate Apothecarion { get; }
        public SquadTemplate Reclusium { get; }

        // Company unit templates with a distinct identity in the order of battle
        // (the veteran "First Company" and the scout "Tenth Company"), used to locate
        // the generated companies by template identity rather than display name.
        public UnitTemplate VeteranCompany { get; }
        public UnitTemplate ScoutCompany { get; }

        public ChapterGenerationTemplates(Faction faction)
        {
            ChapterMaster = ResolveSoldier(faction, "Chapter Master");
            Captain = ResolveSoldier(faction, "Captain");

            Champion = ResolveSoldier(faction, "Champion");
            Ancient = ResolveSoldier(faction, "Ancient");

            MasterOfTheLibrarium = ResolveSoldier(faction, "Master of the Librarium");
            Codicier = ResolveSoldier(faction, "Codiciers");
            Lexicanium = ResolveSoldier(faction, "Lexicanium");

            MasterOfTheForge = ResolveSoldier(faction, "Master of the Forge");
            Techmarine = ResolveSoldier(faction, "Techmarine");

            MasterOfTheApothecarion = ResolveSoldier(faction, "Master of the Apothecarion");
            Apothecary = ResolveSoldier(faction, "Apothecary");

            MasterOfSanctity = ResolveSoldier(faction, "Master of Sanctity");
            Chaplain = ResolveSoldier(faction, "Chaplain");

            Veteran = ResolveSoldier(faction, "Veteran");
            VeteranSergeant = ResolveSoldier(faction, "Veteran Sergeant");
            TacticalMarine = ResolveSoldier(faction, "Tactical Marine");
            TacticalSergeant = ResolveSoldier(faction, "Sergeant");
            AssaultMarine = ResolveSoldier(faction, "Assault Marine");
            AssaultSergeant = ResolveSoldier(faction, "Sergeant (A)");
            DevastatorMarine = ResolveSoldier(faction, "Devastator Marine");
            DevastatorSergeant = ResolveSoldier(faction, "Sergeant (D)");
            ScoutMarine = ResolveSoldier(faction, "Scout Marine");
            ScoutSergeant = ResolveSoldier(faction, "Scout Sergeant");

            TacticalSquad = ResolveSquad(faction, "Tactical Squad");
            AssaultSquad = ResolveSquad(faction, "Assault Squad");
            DevastatorSquad = ResolveSquad(faction, "Devastator Squad");
            ScoutSquad = ResolveSquad(faction, "Scout Squad");

            Librarius = ResolveSquad(faction, "Librarius");
            Armory = ResolveSquad(faction, "Armory");
            Apothecarion = ResolveSquad(faction, "Apothecarion");
            Reclusium = ResolveSquad(faction, "Reclusium");

            VeteranCompany = ResolveUnit(faction, "Veteran Company");
            ScoutCompany = ResolveUnit(faction, "Scout Company");
        }

        private static SoldierTemplate ResolveSoldier(Faction faction, string name)
        {
            return Resolve(faction.SoldierTemplates?.Values, name, "soldier", faction);
        }

        private static SquadTemplate ResolveSquad(Faction faction, string name)
        {
            return Resolve(faction.SquadTemplates?.Values, name, "squad", faction);
        }

        private static UnitTemplate ResolveUnit(Faction faction, string name)
        {
            return Resolve(faction.UnitTemplates?.Values, name, "unit", faction);
        }

        private static T Resolve<T>(IEnumerable<T> candidates, string name, string kind, Faction faction)
            where T : class
        {
            List<T> matches = candidates?
                .Where(t => NameOf(t) == name)
                .ToList() ?? new List<T>();
            if (matches.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Required {kind} template '{name}' was not found for faction '{faction.Name}'.");
            }
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Required {kind} template '{name}' is ambiguous for faction '{faction.Name}'; " +
                    $"{matches.Count} entries share that name.");
            }
            return matches[0];
        }

        private static string NameOf<T>(T template)
        {
            return template switch
            {
                SoldierTemplate st => st.Name,
                SquadTemplate sq => sq.Name,
                UnitTemplate ut => ut.Name,
                _ => null
            };
        }
    }
}
