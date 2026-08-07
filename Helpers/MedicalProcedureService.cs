using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    // Owns the gating and assignment of Apothecarium medical procedures (PRD 4.8 / 5.3).
    // The Apothecarium view renders the requisite breakdown this produces (green = met,
    // red = unmet); the controller calls TryAssign on the assign action.
    public class MedicalProcedureService
    {
        private static readonly HashSet<string> ApothecaryTemplates =
            new() { "Apothecary", "Master of the Apothecarion" };
        private static readonly HashSet<string> TechmarineTemplates =
            new() { "Techmarine", "Master of the Forge" };
        // Worlds developed enough to support augmetic surgery (PRD 4.8). The biome descriptor
        // is the planet template name; agri/feudal/feral/death worlds lack the infrastructure.
        private static readonly HashSet<string> SurgeryCapableWorlds =
            new() { "Hive", "Forge", "Civilised" };

        public IReadOnlyList<ProcedureRequisite> EvaluateRequisites(
            PlayerForce force, ISoldier soldier, ReplacementOption option)
        {
            Squad squad = soldier?.AssignedSquad;
            int balance = force?.Army?.Requisition ?? 0;

            return
            [
                new ProcedureRequisite("Apothecary co-located",
                    HasCoLocatedStaff(force, soldier, ApothecaryTemplates)),
                new ProcedureRequisite("Techmarine co-located",
                    HasCoLocatedStaff(force, soldier, TechmarineTemplates)),
                new ProcedureRequisite("Valid surgery site", IsValidSurgerySite(squad)),
                new ProcedureRequisite(
                    $"Requisition {option.RequisitionCost} (have {balance})",
                    balance >= option.RequisitionCost),
            ];
        }

        public bool CanAssign(PlayerForce force, ISoldier soldier, ReplacementOption option)
        {
            return EvaluateRequisites(force, soldier, option).All(r => r.IsMet);
        }

        public bool TryAssign(PlayerForce force, ISoldier soldier, ReplacementOption option)
        {
            if (force?.Army == null || soldier == null || option == null)
            {
                return false;
            }
            if (!CanAssign(force, soldier, option))
            {
                return false;
            }
            // pay up front, so a mid-procedure save needs no reconciliation
            force.Army.Requisition -= option.RequisitionCost;
            force.Army.MedicalProcedures.Add(new MedicalProcedure(
                soldier.Id, option.HitLocationId, option.Type, option.Weeks, option.RequisitionCost));
            return true;
        }

        public bool HasProcedureInProgress(PlayerForce force, int soldierId, int hitLocationTemplateId)
        {
            return force?.Army?.MedicalProcedures?.Any(
                p => p.SoldierId == soldierId && p.HitLocationTemplateId == hitLocationTemplateId) == true;
        }

        /// <summary>
        /// Is this brother an Apothecary? Identified by template name, which is how the Chapter's
        /// medical roles have always been recognised here. Exposed so field care
        /// (<c>Helpers/Medical/FieldCareService</c>) shares this definition rather than growing a
        /// second one that could drift.
        /// </summary>
        public static bool IsApothecary(ISoldier soldier) =>
            soldier?.Template != null && ApothecaryTemplates.Contains(soldier.Template.Name);

        private static bool HasCoLocatedStaff(PlayerForce force, ISoldier wounded, HashSet<string> templateNames)
        {
            if (force?.Army?.OrderOfBattle == null || wounded?.AssignedSquad == null)
            {
                return false;
            }
            (Ship woundedShip, Region woundedRegion) = ResolveLocation(wounded);
            return force.Army.OrderOfBattle.GetAllMembers().Any(member =>
                // Fit for duty: the staff member must be neither downed nor immobilized.
                member.IsCombatEffective
                && member.AssignedSquad?.IsOperational == true
                && member.Template != null
                && templateNames.Contains(member.Template.Name)
                && SameLocation(ResolveLocation(member), (woundedShip, woundedRegion)));
        }

        /// <summary>
        /// Where a soldier physically is, as a (ship, region) pair.
        ///
        /// Design/Active/SpecialistAttachment.md §8 trap 2: this used to read
        /// <c>AssignedSquad</c> alone, but an Apothecary attached to an order is FORWARD while his
        /// home squad may still sit aboard ship -- so surgery gating would have accepted him at a
        /// site he had left. An attached specialist is therefore resolved through
        /// <see cref="PlayerSoldier.EffectiveRegion"/> and is aboard nothing. Everyone else resolves
        /// exactly as before, so this changes no existing behaviour.
        /// </summary>
        private static (Ship Ship, Region Region) ResolveLocation(ISoldier soldier)
        {
            if (soldier is PlayerSoldier player && player.AttachedOrder != null)
            {
                return (null, player.EffectiveRegion);
            }
            Squad squad = soldier?.AssignedSquad;
            return (squad?.BoardedLocation, squad?.CurrentRegion);
        }

        private static bool SameLocation((Ship Ship, Region Region) a, (Ship Ship, Region Region) b)
        {
            if (a.Ship != null && b.Ship != null)
            {
                return a.Ship.Id == b.Ship.Id;
            }
            if (a.Region != null && b.Region != null)
            {
                return a.Region.Id == b.Region.Id;
            }
            return false;
        }

        private static bool IsValidSurgerySite(Squad squad)
        {
            if (squad == null)
            {
                return false;
            }
            // Aboard a ship: the fleet carries an apothecarion.
            if (squad.BoardedLocation != null)
            {
                return true;
            }
            Region region = squad.CurrentRegion;
            Planet planet = region?.Planet;
            if (region == null || planet?.Template == null)
            {
                return false;
            }
            // On the ground: the region must be held by the chapter or the wider Imperium,
            // on a world developed enough to host augmetic surgery.
            bool developed = SurgeryCapableWorlds.Contains(planet.Template.Name);
            bool imperialControlled = region.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic
                && FactionDispositionService.IsImperial(rf.PlanetFaction.Faction));
            return developed && imperialControlled;
        }
    }
}
