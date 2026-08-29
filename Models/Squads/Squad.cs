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
        // Compatibility-only state for format-13 callers that toggled administration on a live
        // squad. New campaigns derive the identity entirely from SquadTemplate.
        private bool _legacyAdministrativeOverride;
        public int Id { get; }
        public string Name { get; set; }
        /// <summary>
        /// Stable number for a line formation inside its parent company. The number belongs to
        /// the formation, not to its current leader or its position in a sorted roster.
        /// </summary>
        public int? FormationOrdinal { get; set; }
        /// <summary>
        /// Durable, monotonic marker used to retain an empty Scout lineage after deployment.
        /// Detailed history remains in the campaign event ledger.
        /// </summary>
        public bool HasBattleHistory { get; set; }
        public SquadTemplate SquadTemplate { get; }
        [Obsolete("Administration is authored on SquadTemplate; use SquadTemplate.IsAdministrative.")]
        public bool IsAdministrative
        {
            get => SquadTemplate?.IsAdministrative == true || _legacyAdministrativeOverride;
            set
            {
                if (!value)
                {
                    // A rules-authored administrative template cannot be made operational by
                    // a legacy caller. A compatibility-only live override can still be cleared.
                    if (SquadTemplate?.IsAdministrative != true)
                    {
                        _legacyAdministrativeOverride = false;
                    }
                    return;
                }

                _legacyAdministrativeOverride = SquadTemplate?.IsAdministrative != true;

                // Administrative duty is mutually exclusive with an operational
                // posting. The 10th Company HQ may be aboard ship, landed, or under
                // orders when the Home World is won, so activating it must detach all
                // three pieces of operational state in one place.
                if (CurrentOrders != null)
                {
                    CurrentOrders.AssignedSquads.Remove(this);
                    CurrentOrders = null;
                }
                // Members lent out to other operations come home too: an administrative
                // formation has no one in the field (Design/Reference/SpecialistAttachment.md).
                foreach (PlayerSoldier member in _members.OfType<PlayerSoldier>().ToList())
                {
                    OnlyWar.Helpers.Orders.OrderAttachment.Detach(member);
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
        public bool CanMoveAsFormation => SquadTemplate?.CanMoveAsFormation == true
            && !IsAdministrative;
        public bool CanAcceptSquadOrder => SquadTemplate?.CanAcceptSquadOrder == true
            && !IsAdministrative;
        public bool IsPresentOperationalForce => SquadTemplate?.IsPresentOperationalForce == true
            && !IsAdministrative;
        public bool MayProvideLocalSupport => SquadTemplate?.MayProvideLocalSupport == true;
        public bool PermitsIndividualDeployment =>
            SquadTemplate?.PermitsIndividualDeployment == true;

        [Obsolete("Use the capability that matches the operation being evaluated.")]
        public bool IsOperational => !IsAdministrative;
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
        /// <summary>
        /// Physical station for a MembersOnly administrative formation. It is deliberately
        /// separate from CurrentRegion/BoardedLocation because the formation is not itself a
        /// manoeuvre element and its roster must never leak into regional or ship combat lists.
        /// </summary>
        public CampaignLocation DutyStation { get; internal set; }
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
            _members = [];
            //AssignedVehicles = new List<int>();
            Loadout = [];
        }

        public Squad(int id, string name, Unit parentUnit, SquadTemplate template)
        {
            Id = id;
            // >= , not > : _nextId is the *next* id to hand out, so a loaded squad whose id
            // equals it must still push it past itself. With > , loading a save whose highest
            // squad id landed on _nextId left the counter unchanged and the next runtime-created
            // squad reused that id.
            if(id >= _nextId)
            {
                Interlocked.Exchange(ref _nextId, id + 1);
            }
            Name = name;
            ParentUnit = parentUnit;
            SquadTemplate = template;
            UsesLoadoutDoctrine = template?.Faction?.IsPlayerFaction == true;
            _members = [];
            //AssignedVehicles = new List<int>();
            Loadout = [];
        }

        public object Clone()
        {
            Squad clone = new Squad(Id, Name, ParentUnit, SquadTemplate);
            clone._legacyAdministrativeOverride = _legacyAdministrativeOverride;
            clone.DutyStation = DutyStation;
            clone.CurrentRegion = CurrentRegion;
            clone.BoardedLocation = BoardedLocation;
            foreach (ISoldier soldier in Members)
            {
                clone.AddSquadMember((ISoldier)soldier.Clone());
            }
            // loadout doesn't need a deep copy
            clone.Loadout = Loadout;
            clone.UsesLoadoutDoctrine = UsesLoadoutDoctrine;
            clone.FormationOrdinal = FormationOrdinal;
            clone.HasBattleHistory = HasBattleHistory;
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
