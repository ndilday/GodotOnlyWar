using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Domain.Identity;

namespace OnlyWar.Domain.Squads
{
    public class Squad : ICloneable
    {
        private readonly List<ISoldier> _members;
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
        public bool CanMoveAsFormation => SquadTemplate?.CanMoveAsFormation == true;
        public bool CanAcceptSquadOrder => SquadTemplate?.CanAcceptSquadOrder == true;
        public bool IsPresentOperationalForce => SquadTemplate?.IsPresentOperationalForce == true;
        public bool MayProvideLocalSupport => SquadTemplate?.MayProvideLocalSupport == true;
        public bool PermitsIndividualDeployment =>
            SquadTemplate?.PermitsIndividualDeployment == true;
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
        public CampaignLocation DutyStation { get; set; }
        public Order CurrentOrders { get; set; }
        /// <summary>
        /// Stable key of the selected scout-training option. The key is resolved against the
        /// active rules database; it is not a display name or a numeric enum value.
        /// </summary>
        public string TrainingOptionKey { get; set; } = ScoutTrainingOptionKeys.Balanced;
        //public List<int> AssignedVehicles;
        public Squad(string name, Unit parentUnit, SquadTemplate template,
                     IPersistentIdAllocator identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            Id = identity.GetNextSquadId();
            Name = name;
            ParentUnit = parentUnit;
            SquadTemplate = template;
            UsesLoadoutDoctrine = template?.Faction?.IsPlayerFaction == true;
            _members = [];
            //AssignedVehicles = new List<int>();
            Loadout = [];
        }

        /// <summary>Compatibility overload backed by a fresh, non-global allocator.</summary>
        public Squad(string name, Unit parentUnit, SquadTemplate template)
            : this(name, parentUnit, template, CreateCompatibilityIdentity(parentUnit))
        {
        }

        private static IPersistentIdAllocator CreateCompatibilityIdentity(Unit parentUnit)
        {
            Unit root = parentUnit;
            while (root?.ParentUnit != null)
            {
                root = root.ParentUnit;
            }

            int nextSquadId = (root?.GetAllSquads() ?? [])
                .Select(squad => squad.Id)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            return new CompatibilityPersistentIdAllocator(nextSquadId: nextSquadId);
        }

        public Squad(int id, string name, Unit parentUnit, SquadTemplate template)
        {
            Id = id;
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
            clone.TrainingOptionKey = TrainingOptionKey;
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
