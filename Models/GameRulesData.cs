using OnlyWar.Helpers.Database.GameRules;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models
{
    internal sealed class GameRulesData
    {
        public bool DebugMode { get; private set; }
        // Sector Data
        public Tuple<ushort, ushort> SectorSize { get; private set; }
        public Tuple<ushort, ushort> SectorCellSize { get; private set; }
        public ushort MaxSubsectorCellDiameter { get; private set; }
        public float PlanetChance { get; private set; }
        // Battle Data
        public Tuple<ushort, ushort> BattleCellSize { get; private set; }

        // Mod Data
        private readonly IReadOnlyList<Faction> _factions;
        private readonly IReadOnlyDictionary<int, BaseSkill> _baseSkillMap;
        private readonly IReadOnlyList<SkillTemplate> _skillTemplateList;
        private readonly IReadOnlyDictionary<int, List<HitLocationTemplate>> _bodyHitLocationTemplateMap;
        private readonly IReadOnlyDictionary<int, PlanetTemplate> _planetTemplateMap;
        public IReadOnlyList<Faction> Factions { get => _factions; }
        public Faction PlayerFaction { get; }
        public Faction DefaultFaction { get; }
        public IReadOnlyDictionary<int, BaseSkill> BaseSkillMap { get => _baseSkillMap; }
        public IReadOnlyList<SkillTemplate> SkillTemplateList { get => _skillTemplateList; }
        public IReadOnlyDictionary<int, List<HitLocationTemplate>> BodyHitLocationTemplateMap { get => _bodyHitLocationTemplateMap; }
        public IReadOnlyDictionary<int, PlanetTemplate> PlanetTemplateMap { get => _planetTemplateMap; }
        public IReadOnlyDictionary<int, RangedWeaponTemplate> RangedWeaponTemplates { get; }
        public IReadOnlyDictionary<int, MeleeWeaponTemplate> MeleeWeaponTemplates { get; }
        public IReadOnlyDictionary<int, WeaponSet> WeaponSets { get; }

        public GameRulesData()
        {
            var gameBlob = GameRulesDataAccess.Instance.GetData("C:\\Projects\\GodotOnlyWar\\Database\\OnlyWar.s3db");
            
            DebugMode = true;
            SectorSize = new(50, 50);
            SectorCellSize = new(20, 20);
            MaxSubsectorCellDiameter = 10;
            PlanetChance = 0.05f;
            BattleCellSize = new(20, 20);

            _factions = gameBlob.Factions;
            _baseSkillMap = gameBlob.BaseSkills;
            _skillTemplateList = gameBlob.SkillTemplates;
            _bodyHitLocationTemplateMap = gameBlob.BodyTemplates;
            _planetTemplateMap = gameBlob.PlanetTemplates;
            RangedWeaponTemplates = gameBlob.RangedWeaponTemplates;
            MeleeWeaponTemplates = gameBlob.MeleeWeaponTemplates;
            WeaponSets = gameBlob.WeaponSets;
            PlayerFaction = _factions.First(f => f.IsPlayerFaction);
            DefaultFaction = _factions.First(f => f.IsDefaultFaction);
        }

        public IReadOnlyList<Faction> GetNonPlayerFactions()
        {
            return _factions.Where(f => !f.IsPlayerFaction).ToList();
        }
    }
}
