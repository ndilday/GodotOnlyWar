using OnlyWar.Operations.Abstractions;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Recruitment;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers.Orders
{
    /// <summary>
    /// The single mutation boundary for order participants. It keeps both sides of every
    /// squad/character pointer pair synchronized and tears down an order only when the complete
    /// participant set becomes empty.
    /// </summary>
    public static class OrderForceService
    {
        // The force whose doctrine and reservations govern this order's participants. An order
        // records the sector that registered it, so the readiness inputs come from the campaign the
        // order actually belongs to rather than from whichever one is installed (SB-05a). A
        // not-yet-registered order has none, and the caller's explicit values stand alone — which is
        // why the command owners resolve and pass them.
        private static PlayerForce ForceFor(Order order) => order?.RegisteredSector?.PlayerForce;

        /// <summary>
        /// Commit a synchronously validated restore without selecting a current campaign or
        /// repeating policy checks between writes. The command owner validates the whole set.
        /// </summary>
        internal static void CommitParticipantRestore(
            Order order,
            IReadOnlyList<Squad> squads,
            IReadOnlyList<PlayerSoldier> characters)
        {
            foreach (Squad squad in squads)
            {
                if (!order.AssignedSquads.Contains(squad)) order.AssignedSquads.Add(squad);
                squad.CurrentOrders = order;
            }
            foreach (PlayerSoldier character in characters)
            {
                if (!order.AssignedCharacters.Contains(character)) order.AssignedCharacters.Add(character);
                character.CurrentOrder = order;
            }
        }

        public static bool AssignCharacter(
            Order order,
            PlayerSoldier character,
            IReadinessDecisions readiness,
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram program = null)
        {
            if (order == null || character == null
                || character.AssignedSquad?.PermitsIndividualDeployment != true)
            {
                return false;
            }

            if (ReferenceEquals(character.CurrentOrder, order))
            {
                if (!order.AssignedCharacters.Contains(character))
                {
                    order.AssignedCharacters.Add(character);
                }
                return true;
            }

            if (character.CurrentOrder != null)
            {
                return false;
            }

            PlayerForce force = ForceFor(order);
            DutyReadinessEvaluation duty = readiness.EvaluateSoldier(character,
                ForceReadinessInputs.DoctrineFor(force, character.AssignedSquad, doctrine),
                ForceReadinessInputs.ProgramFor(force, character.AssignedSquad, program));
            if (!duty.IsDutyReady)
            {
                return false;
            }

            if (!order.AssignedCharacters.Contains(character))
            {
                order.AssignedCharacters.Add(character);
            }
            character.CurrentOrder = order;
            return true;
        }

        public static bool RemoveCharacter(PlayerSoldier character) =>
            character?.CurrentOrder != null
                && RemoveCharacter(character.CurrentOrder, character);

        public static bool RemoveCharacter(Order order, PlayerSoldier character)
        {
            if (order == null || character == null) return false;
            bool changed = order.AssignedCharacters.Remove(character);
            if (ReferenceEquals(character.CurrentOrder, order))
            {
                character.CurrentOrder = null;
                changed = true;
            }
            RemoveIfEmpty(order);
            return changed;
        }

        public static bool AssignSquad(
            Order order,
            Squad squad,
            IReadinessDecisions readiness,
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram program = null)
        {
            if (order == null || squad?.CanAcceptSquadOrder != true) return false;
            if (ReferenceEquals(squad.CurrentOrders, order))
            {
                if (!order.AssignedSquads.Contains(squad))
                {
                    order.AssignedSquads.Add(squad);
                }
                return true;
            }

            // A leaderless required-leader formation may remain on an existing order, be
            // recalled, transferred between ships, or be repaired through Muster. It may not be
            // newly committed to an order. Keeping this check at the participant mutation
            // boundary prevents callers from bypassing the UI's disabled row.
            PlayerForce force = ForceFor(order);
            if (squad.CurrentOrders == null
                && !readiness.CanBeginNewDeployment(squad,
                    ForceReadinessInputs.ProgramFor(force, squad, program),
                    ForceReadinessInputs.DoctrineFor(force, squad, doctrine)))
            {
                return false;
            }

            if (squad.CurrentOrders != null)
            {
                return false;
            }
            if (!order.AssignedSquads.Contains(squad))
            {
                order.AssignedSquads.Add(squad);
            }
            squad.CurrentOrders = order;
            return true;
        }

        public static bool RemoveSquad(Squad squad) =>
            squad?.CurrentOrders != null
                && RemoveSquad(squad.CurrentOrders, squad);

        public static bool RemoveSquad(Order order, Squad squad)
        {
            if (order == null || squad == null) return false;
            bool changed = order.AssignedSquads.Remove(squad);
            if (ReferenceEquals(squad.CurrentOrders, order))
            {
                squad.CurrentOrders = null;
                changed = true;
            }
            RemoveIfEmpty(order);
            return changed;
        }

        /// <summary>Used by the save loader after both order and PlayerSoldier objects exist.</summary>
        public static bool BindLoadedCharacter(Order order, PlayerSoldier character)
        {
            if (order == null || character == null) return false;
            if (character.CurrentOrder != null && !ReferenceEquals(character.CurrentOrder, order))
            {
                throw new InvalidDataException(
                    $"Soldier {character.Id} is linked to more than one order.");
            }
            if (!order.AssignedCharacters.Contains(character))
            {
                order.AssignedCharacters.Add(character);
            }
            character.CurrentOrder = order;
            return true;
        }

        public static void ReleaseOrder(Order order)
        {
            if (order == null) return;
            foreach (PlayerSoldier character in order.AssignedCharacters.ToList())
            {
                RemoveCharacter(order, character);
            }
            foreach (Squad squad in order.AssignedSquads.ToList())
            {
                if (ReferenceEquals(squad.CurrentOrders, order))
                {
                    squad.CurrentOrders = null;
                }
                order.AssignedSquads.Remove(squad);
            }
            RemoveIfEmpty(order);
        }

        public static void RemoveIfEmpty(Order order)
        {
            if (order?.Force.IsEmpty != true
                || order.Mission?.MissionType == Models.Missions.MissionType.Recruitment)
            {
                return;
            }
            order.RegisteredSector?.RemoveOrder(order);
        }
    }
}
