using OnlyWar.Builders;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

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
        public bool IsQuiet { get; }
        public bool IsActivelyEngaging { get; }
        public Aggression LevelOfAggression { get; private set; }
        public Mission Mission { get; }
        // Individuals attached to this operation without their home squad
        // (Design/Reference/SpecialistAttachment.md). Purely organizational in Phase 2a: an
        // attached specialist is WITH the force, not IN the engagement -- he gets no
        // BattleSquad binding and cannot become a casualty.
        //
        // Initialized here rather than taken as a constructor parameter so that every
        // existing construction site (player UI, NPC strategy, save load, tests) keeps
        // producing a non-null list, and so that mutation only ever happens through
        // OrderAttachment, which owns both halves of the pointer pair.
        public List<Soldiers.PlayerSoldier> AttachedSoldiers { get; } = [];

        public Order(List<Squad> orderedSquads, bool isQuiet, bool isActivelyEngaging, Aggression levelOfAggression, Mission mission)
            : this(IdGenerator.GetNextOrderId(), orderedSquads, isQuiet, isActivelyEngaging, levelOfAggression, mission) {}

        public Order(int id, List<Squad> orderedSquads, bool isQuiet, bool isActivelyEngaging, Aggression levelOfAggression, Mission mission)
        {
            Id = id;
            AssignedSquads = orderedSquads;
            IsQuiet = isQuiet;
            IsActivelyEngaging = isActivelyEngaging;
            LevelOfAggression = levelOfAggression;
            Mission = mission;
            // A squad in an order's AssignedSquads must point back at that order via
            // CurrentOrders; turn processing (BattleSquad.ShouldContinueMission)
            // reads CurrentOrders for every squad it runs. Establishing the invariant here means
            // no order-creation site (NPC strategy, player UI, save/load) can forget to set it.
            if (orderedSquads != null)
            {
                foreach (Squad squad in orderedSquads)
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
