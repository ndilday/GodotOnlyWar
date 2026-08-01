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
        private bool _isAdministrative;
        public int Id { get; }
        public string Name { get; set; }
        public SquadTemplate SquadTemplate { get; }
        public bool IsAdministrative
        {
            get => _isAdministrative;
            set
            {
                if (_isAdministrative == value)
                {
                    return;
                }

                _isAdministrative = value;
                if (!value)
                {
                    return;
                }

                // Administrative duty is mutually exclusive with an operational
                // posting. The 10th Company HQ may be aboard ship, landed, or under
                // orders when the Home World is won, so activating it must detach all
                // three pieces of operational state in one place.
                if (CurrentOrders != null)
                {
                    CurrentOrders.AssignedSquads.Remove(this);
                    CurrentOrders = null;
                }
                BoardedLocation?.RemoveSquad(this);
                BoardedLocation = null;
                if (CurrentRegion != null
                    && Faction != null
                    && CurrentRegion.RegionFactionMap.TryGetValue(
                        Faction.Id, out RegionFaction regionFaction))
                {
                    regionFaction.LandedSquads.Remove(this);
                }
                CurrentRegion = null;
            }
        }
        public bool IsOperational =>
            !_isAdministrative && SquadTemplate?.IsOperational == true;
        public ISoldier SquadLeader { get => Members.FirstOrDefault(m => m.Template.IsSquadLeader); }
        public IReadOnlyCollection<ISoldier> Members { get => _members; }
        public Faction Faction
        {
            get
            {
                if (SquadTemplate == null) return null;
                return SquadTemplate.Faction;
            }
        }

        public Unit ParentUnit { get; set; }
        // if Loadout count < Member count, assume the rest are using the default loadout in the template
        public List<WeaponSet> Loadout { get; set; }
        // Player squads follow the chapter/planet doctrine hierarchy unless the player explicitly
        // customizes them. NPC squads ignore doctrine and continue using their generated loadout.
        public bool UsesLoadoutDoctrine { get; set; }
        public Region CurrentRegion { get; set; }
        public Ship BoardedLocation { get; set; }
        public Order CurrentOrders { get; set; }
        public TrainingFocuses TrainingFocus { get; set; }
        //public List<int> AssignedVehicles;
        public Squad(string name, Unit parentUnit, SquadTemplate template)
        {
            Id = _nextId++;
            Name = name;
            ParentUnit = parentUnit;
            SquadTemplate = template;
            UsesLoadoutDoctrine = template?.Faction?.IsPlayerFaction == true;
            _isAdministrative = template?.IsOperational == false;
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
            UsesLoadoutDoctrine = template?.Faction?.IsPlayerFaction == true;
            _isAdministrative = template?.IsOperational == false;
            _members = [];
            //AssignedVehicles = new List<int>();
            Loadout = [];
        }

        public object Clone()
        {
            Squad clone = new Squad(Id, Name, ParentUnit, SquadTemplate);
            clone.IsAdministrative = IsAdministrative;
            foreach (ISoldier soldier in Members)
            {
                clone.AddSquadMember((ISoldier)soldier.Clone());
            }
            // loadout doesn't need a deep copy
            clone.Loadout = Loadout;
            clone.UsesLoadoutDoctrine = UsesLoadoutDoctrine;
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
