using OnlyWar.Operations.Abstractions;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Operations.Personnel;
using OnlyWar.Runtime.Allocators;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Orders
{
    // Pure-logic extraction of OrderDialogController.OnOrdersConfirmed's Mission-construction and
    // Order-creation logic, generalized to accept more than one squad at once (for a future
    // multi-squad operations board). The campaign it mutates arrives as an explicit
    // OrderCommandContext (SB-05a): the same AddNewOrder/RemoveOrder bookkeeping the dialog used to
    // do inline, against a named sector rather than whichever campaign happens to be current.
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
            OrderCommandContext campaign,
            IReadOnlyList<Squad> squads,
            Region targetRegion,
            AvailableMission mission,
            int targetFactionId,
            Aggression aggression,
            IReadOnlyList<PlayerSoldier> attachedSoldiers = null,
            ChapterOperationalDoctrine doctrine = null)
        {
            if (campaign?.Sector == null) return null;
            if (squads == null || squads.Count == 0
                || squads.Any(squad => squad?.CanAcceptSquadOrder != true
                    || squad.PermitsIndividualDeployment
                    || squad.CurrentOrders == null
                        && !campaign.RequireReadiness().CanBeginNewDeployment(
                            squad,
                            program: ForceReadinessInputs.ProgramFor(campaign.Force, squad),
                            doctrine: ForceReadinessInputs.DoctrineFor(campaign.Force, squad, doctrine))))
            {
                return null;
            }
            // A formation that may give up individuals never deploys as a unit
            // (Design/Reference/SpecialistAttachment.md §3.3). HQ squads and the four chapter
            // offices are personnel pools: their people reach the field only by attachment.
            // Administrative formations are member-only personnel pools and never deploy as
            // whole formations; their capabilities are authored by the template.
            List<Squad> distinctSquads = squads
                .GroupBy(squad => squad.Id)
                .Select(group => group.First())
                .ToList();
            RecruitmentProgram program = campaign.Recruitment;
            if (distinctSquads.Any(squad => squad.Members.Any(member =>
                    RecruitmentProcedureRules.IsSoldierInBlackCarapaceProcedure(
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
                    RecruitmentProcedureRules.IsSoldierInBlackCarapaceProcedure(
                        program, soldier.Id)))
            {
                return null;
            }

            Sector sector = campaign.Sector;
            IPersonnelAvailabilityQueries personnel = campaign.Personnel
                ?? throw new System.ArgumentException(
                    "Order command context must include personnel availability queries.",
                    nameof(campaign));
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
                        duplicateOrder.AssignedSquads.ToList(), existingOrder);
                    sector.RemoveOrder(duplicateOrder);
                }
                List<Squad> existingStaging = existingOrder.AssignedSquads
                    .Concat(distinctSquads)
                    .ToList();
                if (!CanAttachAll(campaign, distinctSpecialists, existingOrder, existingStaging, doctrine))
                {
                    return null;
                }
                existingOrder.SetAggression(aggression);
                MoveSquadsToOrder(distinctSquads, existingOrder);
                foreach (PlayerSoldier specialist in distinctSpecialists)
                {
                    OrderAttachment.Attach(
                        specialist,
                        existingOrder,
                        campaign.RequireReadiness(),
                        doctrine);
                }
                return existingOrder;
            }

            if (!CanAttachAll(campaign, distinctSpecialists, null, distinctSquads, doctrine))
            {
                return null;
            }

            IPersistentIdAllocator identity = campaign.Identity ?? CreateTransientIdentity();
            Mission builtMission = BuildMission(
                targetRegion,
                mission,
                targetFactionId,
                campaign.PlayerFaction,
                identity);
            if (builtMission == null)
            {
                return null;
            }

            foreach (Squad squad in distinctSquads)
            {
                DetachFromCurrentOrder(squad);
            }

            // The Order constructor sets squad.CurrentOrders = this for every squad passed in,
            // so assigning CurrentOrders separately afterwards is unnecessary.
            Order newOrder = new Order(
                identity.GetNextOrderId(),
                distinctSquads,
                true,
                false,
                aggression,
                builtMission,
                sector.PlayerForce?.Faction);
            sector.AddNewOrder(newOrder);
            foreach (PlayerSoldier specialist in distinctSpecialists)
            {
                OrderAttachment.Attach(
                    specialist,
                    newOrder,
                    campaign.RequireReadiness(),
                    doctrine);
            }
            return newOrder;
        }

        /// <summary>
        /// Creates or updates a mission from a mixed movement force. Characters are first-class
        /// order participants here, but a new order still requires at least one manoeuvre squad;
        /// characters may be added to an existing squad-backed order independently.
        /// </summary>
        public static Order AssignParticipantsToMission(
            OrderCommandContext campaign,
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
                && !campaign.RequireReadiness().CanBeginNewDeployment(
                    squad,
                    program: ForceReadinessInputs.ProgramFor(campaign?.Force, squad),
                    doctrine: ForceReadinessInputs.DoctrineFor(campaign?.Force, squad, doctrine))))
            {
                return null;
            }

            Sector sector = campaign?.Sector;
            if (sector == null) return null;
            IPersonnelAvailabilityQueries personnel = campaign.Personnel
                ?? throw new System.ArgumentException(
                    "Order command context must include personnel availability queries.",
                    nameof(campaign));
            RecruitmentProgram program = campaign.Recruitment;
            if (distinctSquads.SelectMany(squad => squad.Members)
                    .Concat(distinctCharacters)
                    .Any(soldier => RecruitmentProcedureRules.IsSoldierInBlackCarapaceProcedure(
                        program, soldier.Id)))
            {
                return null;
            }

            IPersistentIdAllocator identity = campaign.Identity ?? CreateTransientIdentity();
            Mission builtMission = BuildMission(
                targetRegion,
                mission,
                targetFactionId,
                campaign.PlayerFaction,
                identity);
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
                    identity.GetNextOrderId(),
                    [],
                    isQuiet: true,
                    isActivelyEngaging: false,
                    aggression,
                    builtMission,
                    ownerFaction);
                IReadOnlyList<Squad> staging = distinctSquads;
                Region explicitOrigin = staging.Count == 0 ? targetRegion : null;
                if (distinctCharacters.Any(character =>
                    !personnel.EvaluateOrderAssignment(
                        PersonnelAvailabilityProjection.ForOrderAssignment(
                            character,
                            targetOrder,
                            explicitOrigin,
                            staging,
                            doctrine,
                            program)).IsAllowed))
                {
                    return null;
                }
                foreach (Squad squad in distinctSquads)
                {
                    if (!AssignSquadFor(campaign, targetOrder, squad, doctrine))
                    {
                        OrderForceService.ReleaseOrder(targetOrder);
                        return null;
                    }
                }
                if (distinctCharacters.Any(character =>
                    !AssignCharacterFor(campaign, targetOrder, character, doctrine)))
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
                    AssignSquadFor(campaign, targetOrder, duplicateSquad, doctrine);
                }
                foreach (PlayerSoldier duplicateCharacter in duplicateOrder.AssignedCharacters.ToList())
                {
                    OrderForceService.RemoveCharacter(duplicateOrder, duplicateCharacter);
                    AssignCharacterFor(campaign, targetOrder, duplicateCharacter, doctrine);
                }
                sector.RemoveOrder(duplicateOrder);
            }

            List<Squad> stagingSquads = targetOrder.AssignedSquads
                .Concat(distinctSquads)
                .ToList();
            Region origin = stagingSquads.Count == 0 ? targetRegion : null;
            if (distinctCharacters.Any(character =>
                !personnel.EvaluateOrderAssignment(
                    PersonnelAvailabilityProjection.ForOrderAssignment(
                        character,
                        targetOrder,
                        origin,
                        stagingSquads,
                        doctrine,
                        program)).IsAllowed))
            {
                return null;
            }
            foreach (Squad squad in distinctSquads)
            {
                if (!AssignSquadFor(campaign, targetOrder, squad, doctrine))
                {
                    return null;
                }
            }
            foreach (PlayerSoldier character in distinctCharacters)
            {
                if (!AssignCharacterFor(campaign, targetOrder, character, doctrine))
                {
                    return null;
                }
            }
            targetOrder.SetAggression(aggression);
            return targetOrder;
        }

        // A brand-new order is not registered on the sector yet, so OrderForceService cannot derive
        // the owning force from it. The command owner knows the campaign, so it resolves the
        // readiness inputs here and passes them in rather than letting anything fall back to the
        // installed campaign (SB-05a).
        private static bool AssignSquadFor(
            OrderCommandContext campaign, Order order, Squad squad,
            ChapterOperationalDoctrine doctrine) =>
            OrderForceService.AssignSquad(
                order, squad,
                campaign.RequireReadiness(),
                ForceReadinessInputs.DoctrineFor(campaign.Force, squad, doctrine),
                ForceReadinessInputs.ProgramFor(campaign.Force, squad));

        private static bool AssignCharacterFor(
            OrderCommandContext campaign, Order order, PlayerSoldier character,
            ChapterOperationalDoctrine doctrine) =>
            OrderForceService.AssignCharacter(
                order, character,
                campaign.RequireReadiness(),
                ForceReadinessInputs.DoctrineFor(campaign.Force, character?.AssignedSquad, doctrine),
                ForceReadinessInputs.ProgramFor(campaign.Force, character?.AssignedSquad));

        // Every specialist must clear OrderAttachment.CanAttach against the force actually
        // being committed; the staging list is passed explicitly because for a brand-new order
        // the Order object does not exist yet. targetOrder is null in that case, which makes
        // the "already attached elsewhere" guard reject anyone already committed.
        private static bool CanAttachAll(
            OrderCommandContext campaign,
            IReadOnlyList<PlayerSoldier> specialists,
            Order targetOrder,
            IReadOnlyList<Squad> stagingSquads,
            ChapterOperationalDoctrine doctrine)
        {
            return specialists.All(soldier => OrderAttachment.CanAttach(
                soldier, targetOrder, stagingSquads, null, campaign.RequireReadiness(), out _,
                ForceReadinessInputs.DoctrineFor(campaign.Force, soldier?.AssignedSquad, doctrine),
                ForceReadinessInputs.ProgramFor(campaign.Force, soldier?.AssignedSquad)));
        }

        public static bool UnassignSquads(IReadOnlyList<Squad> squads)
        {
            if (squads == null || squads.Count == 0)
            {
                return false;
            }

            bool changed = false;
            foreach (Squad squad in squads
                .Where(squad => squad?.CurrentOrders != null)
                .GroupBy(squad => squad.Id)
                .Select(group => group.First()))
            {
                DetachFromCurrentOrder(squad);
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

        private static IPersistentIdAllocator CreateTransientIdentity() =>
            new PersistentIdAllocator(
                System.Guid.NewGuid().GetHashCode(),
                System.Guid.NewGuid().GetHashCode(),
                System.Guid.NewGuid().GetHashCode(),
                System.Guid.NewGuid().GetHashCode());

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
            Order targetOrder)
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

                DetachFromCurrentOrder(squad);
                if (!targetOrder.AssignedSquads.Contains(squad))
                {
                    targetOrder.AssignedSquads.Add(squad);
                }
                squad.CurrentOrders = targetOrder;
            }
        }

        // An order that empties out is retired from the sector that registered it, which the order
        // itself records. Nothing here consults the active campaign (SB-05a).
        private static void DetachFromCurrentOrder(Squad squad)
        {
            if (squad.CurrentOrders == null) return;

            Order oldOrder = squad.CurrentOrders;
            OrderForceService.RemoveSquad(oldOrder, squad);
            if (oldOrder.AssignedSquads.Count == 0)
            {
                foreach (PlayerSoldier character in oldOrder.AssignedCharacters.ToList())
                {
                    OrderForceService.RemoveCharacter(oldOrder, character);
                }
            }
            if (oldOrder.Force.IsEmpty)
            {
                oldOrder.RegisteredSector?.RemoveOrder(oldOrder);
            }
        }

        private static Mission BuildMission(
            Region selectedRegion,
            AvailableMission mission,
            int targetFactionId,
            Faction playerFaction,
            IPersistentIdAllocator identity)
        {
            switch (mission.Kind)
            {
                case MissionAvailabilityKind.Recon:
                    {
                        // use the first non-player, non-default region faction in this region
                        RegionFaction enemyRegionFaction = GetEnemyRegionFaction(selectedRegion)
                            ?? GetDefaultRegionFaction(selectedRegion)
                            ?? GetOrCreatePlayerRegionFaction(selectedRegion, playerFaction);
                        if (enemyRegionFaction == null)
                        {
                            return null;
                        }
                        return new Mission(
                            identity.GetNextMissionId(), MissionType.Recon, enemyRegionFaction, 0);
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
                        return new Mission(
                            identity.GetNextMissionId(), MissionType.Advance, enemyRegionFaction, 0);
                    }
                case MissionAvailabilityKind.Move:
                    return new Mission(
                        identity.GetNextMissionId(),
                        MissionType.Advance,
                        GetOrCreatePlayerRegionFaction(selectedRegion, playerFaction),
                        0);
                case MissionAvailabilityKind.Defend:
                    return new Mission(
                        identity.GetNextMissionId(),
                        MissionType.DefenseInDepth,
                        GetOrCreatePlayerRegionFaction(selectedRegion, playerFaction),
                        0);
                case MissionAvailabilityKind.Patrol:
                    return new Mission(
                        identity.GetNextMissionId(),
                        MissionType.Patrol,
                        GetOrCreatePlayerRegionFaction(selectedRegion, playerFaction),
                        0);
                case MissionAvailabilityKind.FortifyEntrenchment:
                    return new ConstructionMission(
                        identity.GetNextMissionId(),
                        DefenseType.Entrenchment,
                        0,
                        GetOrCreatePlayerRegionFaction(selectedRegion, playerFaction));
                case MissionAvailabilityKind.BuildListeningPost:
                    return new ConstructionMission(
                        identity.GetNextMissionId(),
                        DefenseType.ListeningPost,
                        0,
                        GetOrCreatePlayerRegionFaction(selectedRegion, playerFaction));
                case MissionAvailabilityKind.BuildAntiAir:
                    return new ConstructionMission(
                        identity.GetNextMissionId(),
                        DefenseType.AntiAir,
                        0,
                        GetOrCreatePlayerRegionFaction(selectedRegion, playerFaction));
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
                        return new Mission(
                            identity.GetNextMissionId(), MissionType.Diversion, enemyRegionFaction, 0);
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
        private static RegionFaction GetOrCreatePlayerRegionFaction(Region region, Faction playerFaction)
        {
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
