using OnlyWar.Models;
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
                    HasCoLocatedStaff(force, squad, ApothecaryTemplates)),
                new ProcedureRequisite("Techmarine co-located",
                    HasCoLocatedStaff(force, squad, TechmarineTemplates)),
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

        private static bool HasCoLocatedStaff(PlayerForce force, Squad woundedSquad, HashSet<string> templateNames)
        {
            if (force?.Army?.OrderOfBattle == null || woundedSquad == null)
            {
                return false;
            }
            return force.Army.OrderOfBattle.GetAllMembers().Any(member =>
                member.CanFight
                && member.Template != null
                && templateNames.Contains(member.Template.Name)
                && SameLocation(member.AssignedSquad, woundedSquad));
        }

        private static bool SameLocation(Squad a, Squad b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            if (a.BoardedLocation != null && b.BoardedLocation != null)
            {
                return a.BoardedLocation.Id == b.BoardedLocation.Id;
            }
            if (a.CurrentRegion != null && b.CurrentRegion != null)
            {
                return a.CurrentRegion.Id == b.CurrentRegion.Id;
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
                && (rf.PlanetFaction.Faction.IsPlayerFaction || rf.PlanetFaction.Faction.IsDefaultFaction));
            return developed && imperialControlled;
        }
    }
}
