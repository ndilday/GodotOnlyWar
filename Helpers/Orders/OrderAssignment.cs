using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.UI;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Orders
{
    // Pure-logic extraction of OrderDialogController.OnOrdersConfirmed's Mission-construction and
    // Order-creation logic, generalized to accept more than one squad at once (for a future
    // multi-squad operations board). Depends on GameDataSingleton.Instance.Sector for the same
    // AddNewOrder/RemoveOrder bookkeeping the dialog used to do inline, but has no Godot/UI
    // dependency of its own.
    public static class OrderAssignment
    {
        // Reuses the existing player order for the same effective mission whenever one exists.
        // "Same" means mission type + target region, plus target faction for Attack/Diversion,
        // construction type for building orders, or the exact persisted mission id for a special
        // mission. This keeps one authoritative order (and one aggression setting) per operation.
        // Otherwise builds and registers one new Order after detaching the squads from prior tasking.
        // Returns null (and creates nothing) if the mission descriptor can't be resolved.
        //
        // targetFactionId remains the selector input for Diversion and for legacy callers that
        // construct a generic Attack descriptor. New Attack buttons carry their RegionFaction
        // directly on AvailableMission. A negative value means no separate selection was supplied.
        public static Order AssignSquadsToMission(
            IReadOnlyList<Squad> squads,
            Region targetRegion,
            AvailableMission mission,
            int targetFactionId,
            Aggression aggression,
            IReadOnlyList<PlayerSoldier> attachedSoldiers = null,
            ChapterOperationalDoctrine doctrine = null)
        {
            if (squads == null || squads.Count == 0
                || squads.Any(squad => squad?.CanAcceptSquadOrder != true
                    || squad.SquadTemplate?.PermitsIndividualDetachment == true
                    || squad.CurrentOrders == null
                        && !SquadReadinessService.CanBeginNewDeployment(squad, null, doctrine)))
            {
                return null;
            }
            // A formation that may give up individuals never deploys as a unit
            // (Design/Reference/SpecialistAttachment.md §3.3). HQ squads and the four chapter
            // offices are personnel pools: their people reach the field only by attachment.
            // Note this is enforced here rather than by marking them Administrative -- their
            // IsOperational must stay true, since surgery staffing and recruitment gate on it.
            List<Squad> distinctSquads = squads
                .GroupBy(squad => squad.Id)
                .Select(group => group.First())
                .ToList();
            RecruitmentProgram program =
                GameDataSingleton.Instance.Sector?.PlayerForce?.RecruitmentProgram;
            if (distinctSquads.Any(squad => squad.Members.Any(member =>
                    RecruitmentPromotionService.IsSoldierInBlackCarapaceProcedure(
                        program, member.Id))))
            {
                return null;
            }

            // Individual specialists lent to this operation. Validated before any mutation:
            // one bad specialist rejects the whole issue and creates nothing, matching the
            // existing all-or-nothing contract of this method.
            List<PlayerSoldier> distinctSpecialists = (attachedSoldiers ?? [])
                .Where(soldier => soldier != null)
                .GroupBy(soldier => soldier.Id)
                .Select(group => group.First())
                .ToList();
            if (distinctSpecialists.Any(soldier =>
                    RecruitmentPromotionService.IsSoldierInBlackCarapaceProcedure(
                        program, soldier.Id)))
            {
                return null;
            }

            Sector sector = GameDataSingleton.Instance.Sector;
            List<Order> equivalentOrders = sector.Orders.Values
                .Where(order => IsPlayerOrder(order)
                    && RepresentsEffectiveMission(
                        order, targetRegion, mission, targetFactionId))
                .ToList();
            if (equivalentOrders.Count > 0)
            {
                Order existingOrder = equivalentOrders[0];
                foreach (Order duplicateOrder in equivalentOrders.Skip(1))
                {
                    MoveSquadsToOrder(
                        duplicateOrder.AssignedSquads.ToList(), existingOrder, sector);
                    sector.RemoveOrder(duplicateOrder);
                }
                List<Squad> existingStaging = existingOrder.AssignedSquads
                    .Concat(distinctSquads)
                    .ToList();
                if (!CanAttachAll(distinctSpecialists, existingOrder, existingStaging, doctrine))
                {
                    return null;
                }
                existingOrder.SetAggression(aggression);
                MoveSquadsToOrder(distinctSquads, existingOrder, sector);
                foreach (PlayerSoldier specialist in distinctSpecialists)
                {
                    OrderAttachment.Attach(specialist, existingOrder, doctrine);
                }
                return existingOrder;
            }

            if (!CanAttachAll(distinctSpecialists, null, distinctSquads, doctrine))
            {
                return null;
            }

            Mission builtMission = BuildMission(targetRegion, mission, targetFactionId);
            if (builtMission == null)
            {
                return null;
            }

            foreach (Squad squad in distinctSquads)
            {
                DetachFromCurrentOrder(squad, sector);
            }

            // The Order constructor sets squad.CurrentOrders = this for every squad passed in,
            // so assigning CurrentOrders separately afterwards is unnecessary.
            Order newOrder = new Order(
                distinctSquads,
                true,
                false,
                aggression,
                builtMission,
                sector.PlayerForce?.Faction);
            sector.AddNewOrder(newOrder);
            foreach (PlayerSoldier specialist in distinctSpecialists)
            {
                OrderAttachment.Attach(specialist, newOrder, doctrine);
            }
            return newOrder;
        }

        /// <summary>
        /// Creates or updates a mission from a mixed movement force. Characters are first-class
        /// order participants here, but a new order still requires at least one manoeuvre squad;
        /// characters may be added to an existing squad-backed order independently.
        /// </summary>
        public static Order AssignParticipantsToMission(
            IReadOnlyList<Squad> squads,
            IReadOnlyList<PlayerSoldier> characters,
            Region targetRegion,
            AvailableMission mission,
            int targetFactionId,
            Aggression aggression,
            ChapterOperationalDoctrine doctrine = null)
        {
            List<Squad> distinctSquads = (squads ?? [])
                .Where(squad => squad != null)
                .GroupBy(squad => squad.Id)
                .Select(group => group.First())
                .ToList();
            List<PlayerSoldier> distinctCharacters = (characters ?? [])
                .Where(character => character != null)
                .GroupBy(character => character.Id)
                .Select(group => group.First())
                .ToList();
            if (distinctSquads.Count == 0 && distinctCharacters.Count == 0
                || distinctSquads.Any(squad => squad.CanAcceptSquadOrder != true))
            {
                return null;
            }
            if (distinctSquads.Any(squad => squad.CurrentOrders == null
                && !SquadReadinessService.CanBeginNewDeployment(squad, null, doctrine)))
            {
                return null;
            }

            Sector sector = GameDataSingleton.Instance.Sector;
            if (sector == null) return null;
            RecruitmentProgram program = sector.PlayerForce?.RecruitmentProgram;
            if (distinctSquads.SelectMany(squad => squad.Members)
                    .Concat(distinctCharacters)
                    .Any(soldier => RecruitmentPromotionService.IsSoldierInBlackCarapaceProcedure(
                        program, soldier.Id)))
            {
                return null;
            }

            Mission builtMission = BuildMission(targetRegion, mission, targetFactionId);
            if (builtMission == null) return null;

            List<Order> equivalentOrders = sector.Orders.Values
                .Where(order => IsPlayerOrder(order)
                    && RepresentsEffectiveMission(order, targetRegion, mission, targetFactionId))
                .ToList();
            Order targetOrder = equivalentOrders.FirstOrDefault();
            if (targetOrder == null)
            {
                // An attached character supplements an operation; it cannot be the operation's
                // only force. Keep this invariant at the mutation boundary as well as in the
                // ordinary AssignSquadsToMission API.
                if (distinctSquads.Count == 0)
                {
                    return null;
                }
                Faction ownerFaction = distinctSquads.Select(squad => squad.Faction)
                    .Concat(distinctCharacters.Select(character => character.AssignedSquad?.Faction))
                    .FirstOrDefault(faction => faction != null)
                    ?? sector.PlayerForce?.Faction;
                targetOrder = new Order(
                    [],
                    isQuiet: true,
                    isActivelyEngaging: false,
                    aggression,
                    builtMission,
                    ownerFaction);
                IReadOnlyList<Squad> staging = distinctSquads;
                Region explicitOrigin = staging.Count == 0 ? targetRegion : null;
                CharacterAvailabilityService availability = new();
                if (distinctCharacters.Any(character =>
                    !availability.EvaluateOrderAssignment(
                        character, targetOrder, explicitOrigin, staging, doctrine).IsAllowed))
                {
                    return null;
                }
                foreach (Squad squad in distinctSquads)
                {
                    if (!OrderForceService.AssignSquad(targetOrder, squad, doctrine))
                    {
                        OrderForceService.ReleaseOrder(targetOrder);
                        return null;
                    }
                }
                if (distinctCharacters.Any(character =>
                    !OrderForceService.AssignCharacter(targetOrder, character, doctrine)))
                {
                    OrderForceService.ReleaseOrder(targetOrder);
                    return null;
                }
                sector.AddNewOrder(targetOrder);
                return targetOrder;
            }

            if (distinctSquads.Any(squad => squad.CurrentOrders != null
                    && !ReferenceEquals(squad.CurrentOrders, targetOrder)))
            {
                return null;
            }

            foreach (Order duplicateOrder in equivalentOrders.Skip(1))
            {
                foreach (Squad duplicateSquad in duplicateOrder.AssignedSquads.ToList())
                {
                    OrderForceService.RemoveSquad(duplicateOrder, duplicateSquad);
                    OrderForceService.AssignSquad(targetOrder, duplicateSquad, doctrine);
                }
                foreach (PlayerSoldier duplicateCharacter in duplicateOrder.AssignedCharacters.ToList())
                {
                    OrderForceService.RemoveCharacter(duplicateOrder, duplicateCharacter);
                    OrderForceService.AssignCharacter(targetOrder, duplicateCharacter, doctrine);
                }
                sector.RemoveOrder(duplicateOrder);
            }

            CharacterAvailabilityService targetAvailability = new();
            List<Squad> stagingSquads = targetOrder.AssignedSquads
                .Concat(distinctSquads)
                .ToList();
            Region origin = stagingSquads.Count == 0 ? targetRegion : null;
            if (distinctCharacters.Any(character =>
                !targetAvailability.EvaluateOrderAssignment(
                    character, targetOrder, origin, stagingSquads, doctrine).IsAllowed))
            {
                return null;
            }
            foreach (Squad squad in distinctSquads)
            {
                if (!OrderForceService.AssignSquad(targetOrder, squad, doctrine))
                {
                    return null;
                }
            }
            foreach (PlayerSoldier character in distinctCharacters)
            {
                if (!OrderForceService.AssignCharacter(targetOrder, character, doctrine))
                {
                    return null;
                }
            }
            targetOrder.SetAggression(aggression);
            return targetOrder;
        }

        // Every specialist must clear OrderAttachment.CanAttach against the force actually
        // being committed; the staging list is passed explicitly because for a brand-new order
        // the Order object does not exist yet. targetOrder is null in that case, which makes
        // the "already attached elsewhere" guard reject anyone already committed.
        private static bool CanAttachAll(
            IReadOnlyList<PlayerSoldier> specialists,
            Order targetOrder,
            IReadOnlyList<Squad> stagingSquads,
            ChapterOperationalDoctrine doctrine)
        {
            return specialists.All(soldier => OrderAttachment.CanAttach(
                soldier, targetOrder, stagingSquads, null, out _, doctrine));
        }

        public static bool UnassignSquads(IReadOnlyList<Squad> squads)
        {
            if (squads == null || squads.Count == 0)
            {
                return false;
            }

            Sector sector = GameDataSingleton.Instance.Sector;
            bool changed = false;
            foreach (Squad squad in squads
                .Where(squad => squad?.CurrentOrders != null)
                .GroupBy(squad => squad.Id)
                .Select(group => group.First()))
            {
                DetachFromCurrentOrder(squad, sector);
                changed = true;
            }
            return changed;
        }

        // Recalls individual specialists from whatever operation they are attached to. The
        // order itself survives -- it still has its squads, and it is the squads that decide
        // whether the operation exists at all.
        public static bool UnassignSpecialists(IReadOnlyList<PlayerSoldier> soldiers)
        {
            if (soldiers == null || soldiers.Count == 0)
            {
                return false;
            }
            bool changed = false;
            foreach (PlayerSoldier soldier in soldiers
                .Where(soldier => soldier?.CurrentOrder != null)
                .GroupBy(soldier => soldier.Id)
                .Select(group => group.First()))
            {
                OrderForceService.RemoveCharacter(soldier);
                changed = true;
            }
            return changed;
        }

        private static bool IsPlayerOrder(Order order)
        {
            return order?.Force?.OwnerFaction?.IsPlayerFaction == true
                || (order?.Force?.OwnerFaction == null
                    && order?.Force?.AllPlayerSoldiers?.Any() == true);
        }

        private static bool RepresentsEffectiveMission(
            Order order,
            Region targetRegion,
            AvailableMission availableMission,
            int targetFactionId)
        {
            Mission existingMission = order?.Mission;
            if (existingMission?.RegionFaction?.Region != targetRegion)
            {
                return false;
            }

            if (availableMission.Kind == MissionAvailabilityKind.Special)
            {
                return availableMission.SpecialMission != null
                    && existingMission.Id == availableMission.SpecialMission.Id;
            }

            int effectiveTargetFactionId = availableMission.TargetFaction?
                .PlanetFaction?.Faction?.Id ?? targetFactionId;

            return availableMission.Kind switch
            {
                MissionAvailabilityKind.Recon =>
                    existingMission.MissionType == MissionType.Recon,
                MissionAvailabilityKind.Attack =>
                    existingMission.MissionType == MissionType.Advance
                    && existingMission.RegionFaction.PlanetFaction.Faction.Id == effectiveTargetFactionId,
                MissionAvailabilityKind.Move =>
                    existingMission.MissionType == MissionType.Advance
                    && existingMission.RegionFaction.PlanetFaction.Faction.IsPlayerFaction,
                MissionAvailabilityKind.Defend =>
                    existingMission.MissionType == MissionType.DefenseInDepth,
                MissionAvailabilityKind.Patrol =>
                    existingMission.MissionType == MissionType.Patrol,
                MissionAvailabilityKind.FortifyEntrenchment =>
                    existingMission is ConstructionMission construction
                    && construction.ConstructionType == DefenseType.Entrenchment,
                MissionAvailabilityKind.BuildListeningPost =>
                    existingMission is ConstructionMission construction
                    && construction.ConstructionType == DefenseType.ListeningPost,
                MissionAvailabilityKind.BuildAntiAir =>
                    existingMission is ConstructionMission construction
                    && construction.ConstructionType == DefenseType.AntiAir,
                MissionAvailabilityKind.Diversion =>
                    existingMission.MissionType == MissionType.Diversion
                    && existingMission.RegionFaction.PlanetFaction.Faction.Id == targetFactionId,
                _ => false
            };
        }

        private static void MoveSquadsToOrder(
            IReadOnlyList<Squad> squads,
            Order targetOrder,
            Sector sector)
        {
            foreach (Squad squad in squads)
            {
                if (ReferenceEquals(squad.CurrentOrders, targetOrder))
                {
                    if (!targetOrder.AssignedSquads.Contains(squad))
                    {
                        targetOrder.AssignedSquads.Add(squad);
                    }
                    continue;
                }

                DetachFromCurrentOrder(squad, sector);
                if (!targetOrder.AssignedSquads.Contains(squad))
                {
                    targetOrder.AssignedSquads.Add(squad);
                }
                squad.CurrentOrders = targetOrder;
            }
        }

        private static void DetachFromCurrentOrder(Squad squad, Sector sector)
        {
            if (squad.CurrentOrders == null) return;

            Order oldOrder = squad.CurrentOrders;
            OrderForceService.RemoveSquad(oldOrder, squad);
            // Format-13's attachment façade required at least one squad to keep an order alive.
            // Preserve that behavior only for its operational-posting projection; format-14
            // character-only orders remain valid and are governed by OrderForceService.
            if (oldOrder.AssignedSquads.Count == 0)
            {
                foreach (PlayerSoldier character in oldOrder.AssignedCharacters.ToList())
                {
                    if (character.IndividualPosting?.Kind == IndividualPostingKind.OperationalAttachment)
                    {
                        OrderAttachment.Detach(character);
                    }
                }
            }
            if (oldOrder.Force.IsEmpty)
            {
                sector.RemoveOrder(oldOrder);
            }
        }

        private static Mission BuildMission(Region selectedRegion, AvailableMission mission, int targetFactionId)
        {
            switch (mission.Kind)
            {
                case MissionAvailabilityKind.Recon:
                    {
                        // use the first non-player, non-default region faction in this region
                        RegionFaction enemyRegionFaction = GetEnemyRegionFaction(selectedRegion)
                            ?? GetDefaultRegionFaction(selectedRegion)
                            ?? GetOrCreatePlayerRegionFaction(selectedRegion);
                        if (enemyRegionFaction == null)
                        {
                            return null;
                        }
                        return new Mission(MissionType.Recon, enemyRegionFaction, 0);
                    }
                case MissionAvailabilityKind.Attack:
                    {
                        int effectiveTargetFactionId = mission.TargetFaction?
                            .PlanetFaction?.Faction?.Id ?? targetFactionId;
                        RegionFaction enemyRegionFaction = GetSelectedTargetRegionFaction(
                            selectedRegion, effectiveTargetFactionId);
                        // An attack whose selected faction vanished is invalid. In particular, never
                        // turn it into a Move by silently targeting the player's own presence.
                        if (enemyRegionFaction == null
                            || !enemyRegionFaction.IsPublic
                            || FactionRelationshipService.IsImperial(
                                enemyRegionFaction.PlanetFaction.Faction))
                        {
                            return null;
                        }
                        return new Mission(MissionType.Advance, enemyRegionFaction, 0);
                    }
                case MissionAvailabilityKind.Move:
                    return new Mission(
                        MissionType.Advance,
                        GetOrCreatePlayerRegionFaction(selectedRegion),
                        0);
                case MissionAvailabilityKind.Defend:
                    return new Mission(MissionType.DefenseInDepth, GetOrCreatePlayerRegionFaction(selectedRegion), 0);
                case MissionAvailabilityKind.Patrol:
                    return new Mission(MissionType.Patrol, GetOrCreatePlayerRegionFaction(selectedRegion), 0);
                case MissionAvailabilityKind.FortifyEntrenchment:
                    return new ConstructionMission(DefenseType.Entrenchment, 0, GetOrCreatePlayerRegionFaction(selectedRegion));
                case MissionAvailabilityKind.BuildListeningPost:
                    return new ConstructionMission(DefenseType.ListeningPost, 0, GetOrCreatePlayerRegionFaction(selectedRegion));
                case MissionAvailabilityKind.BuildAntiAir:
                    return new ConstructionMission(DefenseType.AntiAir, 0, GetOrCreatePlayerRegionFaction(selectedRegion));
                case MissionAvailabilityKind.Diversion:
                    {
                        // Diversion: feint against an enemy-held region while the squad stays in
                        // its own region (it demonstrates from adjacent territory rather than
                        // entering the target).
                        RegionFaction enemyRegionFaction = GetSelectedTargetRegionFaction(selectedRegion, targetFactionId);
                        if (enemyRegionFaction == null)
                        {
                            return null;
                        }
                        return new Mission(MissionType.Diversion, enemyRegionFaction, 0);
                    }
                case MissionAvailabilityKind.Special:
                    return mission.SpecialMission;
                default:
                    return null;
            }
        }

        // Returns the player's RegionFaction in the given region, creating (and registering) one
        // if the player does not yet have a presence there. Player-built fortifications are stored
        // on this region faction. Mirrors the on-demand creation used for Advance orders.
        private static RegionFaction GetOrCreatePlayerRegionFaction(Region region)
        {
            Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
            if (!region.RegionFactionMap.TryGetValue(playerFaction.Id, out RegionFaction playerRegionFaction))
            {
                playerRegionFaction = new RegionFaction(region.Planet.PlanetFactionMap[playerFaction.Id], region);
                region.RegionFactionMap[playerFaction.Id] = playerRegionFaction;
            }
            return playerRegionFaction;
        }

        private static RegionFaction GetEnemyRegionFaction(Region region)
        {
            return region.RegionFactionMap.Values.FirstOrDefault(rf =>
                !FactionRelationshipService.IsImperial(rf.PlanetFaction.Faction));
        }

        // Looks up the enemy RegionFaction the player picked in the Target Faction dropdown by
        // faction id. Returns null if no target was selected (negative id: dropdown not
        // applicable/populated) or the region faction map no longer contains that faction (e.g.
        // it was wiped out this turn).
        private static RegionFaction GetSelectedTargetRegionFaction(Region region, int targetFactionId)
        {
            if (targetFactionId < 0)
            {
                return null;
            }
            return region.RegionFactionMap.TryGetValue(targetFactionId, out RegionFaction targetRegionFaction)
                ? targetRegionFaction
                : null;
        }

        private static RegionFaction GetDefaultRegionFaction(Region region)
        {
            return region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
        }
    }
}
