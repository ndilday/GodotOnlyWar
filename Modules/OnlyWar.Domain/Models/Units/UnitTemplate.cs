using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Units
{
    public class UnitTemplate
    {
        public int Id { get; }
        public string Name { get; }
        public bool IsTopLevelUnit { get; }
        public Faction Faction { get; set; }

        public SquadTemplate HQSquad { get; private set; }

        //private UnitTemplate _parentUnit;
        private IReadOnlyCollection<UnitTemplate> _childUnits;
        private readonly IReadOnlyCollection<SquadTemplateSlot> _childSquadSlots;

        // Convenience constructor (mainly for tests): each squad template becomes a
        // slot that exists exactly once. An HQ-typed template is pulled out as the
        // HQ squad, matching the loader's separate HQSquadTemplate handling.
        public UnitTemplate(int id, string name, bool isTopLevel,
                            List<SquadTemplate> childSquads,
                            List<UnitTemplate> childUnits)
        {
            Id = id;
            Name = name;
            IsTopLevelUnit = isTopLevel;
            _childUnits = childUnits;
            SquadTemplate hq = childSquads.FirstOrDefault(squad => (squad.SquadType & SquadTypes.HQ) > 0);
            if (hq != null)
            {
                HQSquad = hq;
                childSquads.Remove(hq);
            }
            _childSquadSlots = childSquads
                .Select(squad => new SquadTemplateSlot(squad, 1, 1))
                .ToList();
        }

        public UnitTemplate(int id, string name, bool isTopLevel,
                            SquadTemplate hqSquadTemplate,
                            List<SquadTemplateSlot> childSquadSlots)
        {
            Id = id;
            Name = name;
            IsTopLevelUnit = isTopLevel;
            _childSquadSlots = childSquadSlots;
            HQSquad = hqSquadTemplate;
        }

        public void SetChildUnits(IReadOnlyCollection<UnitTemplate> childUnits)
        {
            _childUnits = childUnits;
        }

        public IReadOnlyCollection<UnitTemplate> GetChildUnits()
        {
            return _childUnits ?? (IReadOnlyCollection<UnitTemplate>)Enumerable.Empty<UnitTemplate>();
        }

        public IReadOnlyCollection<SquadTemplateSlot> GetChildSquadSlots()
        {
            return _childSquadSlots ?? (IReadOnlyCollection<SquadTemplateSlot>)Enumerable.Empty<SquadTemplateSlot>();
        }

        public Unit GenerateUnitFromTemplateWithoutChildren(string name)
        {
            return new Unit(name, this);
        }

        public int GetMaximumSoldierCount()
        {
            int maxSoldierCount = 0;
            foreach(UnitTemplate childUnit in GetChildUnits())
            {
                maxSoldierCount += childUnit.GetMaximumSoldierCount();
            }
            foreach(SquadTemplateSlot slot in GetChildSquadSlots())
            {
                maxSoldierCount += slot.Template.Elements.Sum(e => e.MaximumNumber) * slot.MaxCount;
            }
            return maxSoldierCount;
        }
    }

}
