using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain;
using OnlyWar.Domain.Formatting;
using OnlyWar.Domain.Identity;

namespace OnlyWar.Domain.Units
{
    public class Unit
    {
        private readonly List<Squad> _squads;
        public int Id { get; private set; }
        public string Name { get; set; }
        public Faction Faction { get; }
        public UnitTemplate UnitTemplate { get; private set; }
        public IReadOnlyCollection<Squad> Squads { get => _squads; }
        // if Loadout count < Member count, assume the rest are using the default loadout in the template
        public List<int> AssignedVehicles;
        public List<Unit> ChildUnits;
        public Unit ParentUnit;
        
        public Squad HQSquad
        {
            get
            {
                return _squads.FirstOrDefault(s => (s.SquadTemplate.SquadType & SquadTypes.HQ) > 0);
            }
        }

        public int BattleValue
        {
            get
            {
                return _squads
                    .Where(squad => squad.IsPresentOperationalForce)
                    .Sum(squad => squad.SquadTemplate.BattleValue)
                    + ChildUnits.Sum(u => u.BattleValue);
            }
        }

        public Unit(int id, string name, UnitTemplate template, List<Squad> squads)
        {
            Id = id;
            Name = name;
            UnitTemplate = template;
            Faction = template.Faction;
            _squads = squads;
            ChildUnits = [];
        }
        public Unit(string name, UnitTemplate template, IPersistentIdAllocator identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            Id = identity.GetNextUnitId();
            Name = name;
            Faction = template.Faction;
            UnitTemplate = template;
            AssignedVehicles = [];
            ChildUnits = [];
            
            int i = 1;
            
            _squads = [];
            if (template.HQSquad != null)
            {
                AddSquad(new Squad(name + " HQ Squad", this, template.HQSquad, identity));
                i++;
            }
            // Only the always-present squads (MinCount, e.g. the chapter's command
            // squads) are created up front. Line squads have MinCount 0 and are
            // created on demand as soldiers are assigned or transferred in.
            foreach (SquadTemplateSlot slot in template.GetChildSquadSlots())
            {
                for (int n = 0; n < slot.MinCount; n++)
                {
                    AddSquad(new Squad(slot.Template.Name, this, slot.Template, identity));
                    i++;
                }
            }
        }

        /// <summary>
        /// Compatibility overload for fixture and extension callers that have not yet adopted
        /// session identity injection. It owns only a short-lived local allocator.
        /// </summary>
        public Unit(string name, UnitTemplate template)
            : this(name, template, new CompatibilityPersistentIdAllocator())
        {
        }
        public IEnumerable<ISoldier> GetAllMembers()
        {
            IEnumerable<ISoldier> soldiers = null;
            if(Squads != null)
            {
                soldiers = Squads.SelectMany(s => s.Members);
            }
            if(ChildUnits != null)
            {
                soldiers = soldiers.Union(ChildUnits.SelectMany(u => u.GetAllMembers()));
            }
            return soldiers;
        }

        public IEnumerable<Squad> GetAllSquads()
        {
            IEnumerable<Squad> squads = null;
            if (Squads != null)
            {
                squads = Squads;
            }
            if (ChildUnits != null)
            {
                squads = squads.Union(ChildUnits.SelectMany(u => u.GetAllSquads()));
            }
            return squads;
        }

        public void AddSquad(Squad squad)
        {
            if (squad == null) throw new ArgumentNullException(nameof(squad));
            squad.ParentUnit = this;
            if (SquadDesignationFormatter.IsNumberedLineFormation(squad))
            {
                squad.FormationOrdinal ??= FormationOrdinalAllocator.GetNextOrdinal(this, squad.SquadTemplate);
                squad.Name = SquadDesignationFormatter.Format(squad);
            }
            _squads.Add(squad);
        }

        public void RemoveSquad(Squad squad)
        {
            if(squad.Members.Count != 0)
            {
                throw new InvalidOperationException("Deleted squad still has members!");
            }
            _squads.Remove(squad);
        }

        public override string ToString()
        {
            return Name + ParentUnit == null ? "" : ", " + ParentUnit.Name;
        }
    }
}
