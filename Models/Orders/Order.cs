using OnlyWar.Builders;
using OnlyWar.Models.Missions;
using System;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Orders
{
    // A squad order has two main descriptors:
    // 1) Whether the squad is actively engaging, or focused on observing; and
    // 2) Whether the squad is attempting to stay hidden or not
    // Level of Aggression impacts how favorite the circumstances must be for the squad to choose to engage
    //
    // A third descriptor - Disposition (DugIn / Mobile / Raiding) - was removed 2026-07-30. It was
    // supplied at every construction site and persisted to the save file, but no decision anywhere in
    // the game ever read it. OnlyWar_TDD.md §6.4 opened on exactly this
    // ("Disposition.DugIn is stored and never read"), and the defender advantage the enum was meant to
    // express was ultimately delivered by the region's Entrenchment plus a contested preparation roll
    // (§5) - which routed around the enum rather than wiring it up. What a force is doing with the
    // ground is now carried by MissionType and MissionReturnPolicy (Hold / Return / Static), which is
    // the same distinction expressed where something actually consumes it.

    public class Order
    {
        public int Id { get; }
        public List<Squad> AssignedSquads { get; }
        public List<PlayerSoldier> AssignedCharacters { get; } = [];
        public bool IsQuiet { get; }
        public bool IsActivelyEngaging { get; }
        public Aggression LevelOfAggression { get; private set; }
        public Mission Mission { get; }
        // Compatibility projection for the retired format-13 name. It aliases the first-class
        // character participant collection; new code must use AssignedCharacters.
        [System.Obsolete("Use AssignedCharacters.")]
        public List<Soldiers.PlayerSoldier> AttachedSoldiers => AssignedCharacters;
        public Faction OwnerFaction { get; }
        // Runtime identity used to carry a Waaagh! through planning and result processing. It is
        // deliberately not serialized on an Order because orders are weekly work items.
        public long? StrategicInvasionForceId { get; set; }

        [Obsolete("Use StrategicInvasionForceId.")]
        public long? OrkWaaaghId
        {
            get => StrategicInvasionForceId;
            set => StrategicInvasionForceId = value;
        }
        public OrderForce Force => new(this);

        public Order(List<Squad> orderedSquads, bool isQuiet, bool isActivelyEngaging, Aggression levelOfAggression, Mission mission)
            : this(IdGenerator.GetNextOrderId(), orderedSquads, isQuiet, isActivelyEngaging,
                   levelOfAggression, mission, orderedSquads?.Select(s => s?.Faction).FirstOrDefault(f => f != null)) {}

        public Order(
            List<Squad> orderedSquads,
            bool isQuiet,
            bool isActivelyEngaging,
            Aggression levelOfAggression,
            Mission mission,
            Faction ownerFaction,
            IEnumerable<PlayerSoldier> assignedCharacters = null)
            : this(IdGenerator.GetNextOrderId(), orderedSquads, isQuiet, isActivelyEngaging,
                   levelOfAggression, mission, ownerFaction, assignedCharacters)
        {
        }

        public Order(int id, List<Squad> orderedSquads, bool isQuiet, bool isActivelyEngaging, Aggression levelOfAggression, Mission mission)
            : this(id, orderedSquads, isQuiet, isActivelyEngaging, levelOfAggression, mission,
                   orderedSquads?.Select(s => s?.Faction).FirstOrDefault(f => f != null))
        {
        }

        public Order(
            int id,
            List<Squad> orderedSquads,
            bool isQuiet,
            bool isActivelyEngaging,
            Aggression levelOfAggression,
            Mission mission,
            Faction ownerFaction,
            IEnumerable<PlayerSoldier> assignedCharacters = null)
        {
            Id = id;
            AssignedSquads = orderedSquads ?? [];
            IsQuiet = isQuiet;
            IsActivelyEngaging = isActivelyEngaging;
            LevelOfAggression = levelOfAggression;
            Mission = mission;
            OwnerFaction = ownerFaction
                ?? AssignedSquads.Select(s => s?.Faction).FirstOrDefault(f => f != null)
                ?? mission?.RegionFaction?.PlanetFaction?.Faction;
            if (assignedCharacters != null)
            {
                foreach (PlayerSoldier character in assignedCharacters.Where(c => c != null).Distinct())
                {
                    if (!AssignedCharacters.Contains(character))
                    {
                        AssignedCharacters.Add(character);
                    }
                    character.CurrentOrder = this;
                }
            }
            // A squad in an order's AssignedSquads must point back at that order via
            // CurrentOrders; turn processing (BattleSquad.ShouldContinueMission)
            // reads CurrentOrders for every squad it runs. Establishing the invariant here means
            // no order-creation site (NPC strategy, player UI, save/load) can forget to set it.
            if (orderedSquads != null)
            {
                foreach (Squad squad in AssignedSquads)
                {
                    squad.CurrentOrders = this;
                }
            }
        }

        public void SetAggression(Aggression aggression)
        {
            LevelOfAggression = aggression;
        }
    }

    public enum Aggression
    {
        Avoid = 0,
        Cautious = 1,
        Normal = 2,
        Attritional = 3,
        Aggressive = 4
    }
}
