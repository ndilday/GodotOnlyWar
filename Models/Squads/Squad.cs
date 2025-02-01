using OnlyWar.Models.Equippables;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OnlyWar.Models.Squads
{
    public class Squad : ICloneable
    {
        private static int _nextId = 0;
        private readonly List<ISoldier> _members;
        public int Id { get; }
        public string Name { get; set; }
        public SquadTemplate SquadTemplate { get; }
        public ISoldier SquadLeader { get => Members.FirstOrDefault(m => m.Template.IsSquadLeader); }
        public IReadOnlyCollection<ISoldier> Members { get => _members; }
        public Faction Faction
        {
            get
            {
                if (ParentUnit == null) return null;
                return ParentUnit.Faction;
            }
        }

        public Unit ParentUnit { get; set; }
        // if Loadout count < Member count, assume the rest are using the default loadout in the template
        public List<WeaponSet> Loadout { get; set; }
        public Region CurrentRegion { get; set; }
        public Ship BoardedLocation { get; set; }
        public Order CurrentOrders { get; set; }
        //public List<int> AssignedVehicles;
        public Squad(string name, Unit parentUnit, SquadTemplate template)
        {
            Id = _nextId++;
            Name = name;
            ParentUnit = parentUnit;
            SquadTemplate = template;
            _members = [];
            //AssignedVehicles = new List<int>();
            Loadout = [];
        }

        public Squad(int id, string name, Unit parentUnit, SquadTemplate template)
        {
            Id = id;
            if(id > _nextId)
            {
                Interlocked.Exchange(ref _nextId, id + 1);
            }
            Name = name;
            ParentUnit = parentUnit;
            SquadTemplate = template;
            _members = [];
            //AssignedVehicles = new List<int>();
            Loadout = [];
        }

        public Squad Clone()
        {
            Squad clone = new Squad(Id, Name, ParentUnit, SquadTemplate);
            foreach (ISoldier soldier in Members)
            {
                clone.AddSquadMember((ISoldier)soldier.Clone());
            }
            // loadout doesn't need a deep copy
            clone.Loadout = Loadout;
            return clone;
        }

        public void AddSquadMember(ISoldier soldier)
        {
            if (!_members.Contains(soldier))
            {
                _members.Add(soldier);
                soldier.AssignedSquad = this;
            }
        }

        public void RemoveSquadMember(ISoldier soldier)
        {
            if(_members.Contains(soldier))
            {
                _members.Remove(soldier);
                soldier.AssignedSquad = null;
            }
        }

        public override string ToString()
        {
            return Name + ", " + ParentUnit.Name;
        }
    }
}
