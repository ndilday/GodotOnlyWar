using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using System.Linq;

namespace OnlyWar.Campaign
{
    /// <summary>
    /// The one answer to "is Techmarine support available here?" for replacement surgery
    /// (TDD §6.14). Surgery gating (<see cref="MedicalProcedureService"/>),
    /// care-destination evaluation (<see cref="CareDestinationService"/>) and the recovery planner
    /// (<see cref="RecoveryPlanService"/>) all ask this, so the Mechanicus loan satisfies the
    /// requirement everywhere at once and the planner never tries to move a Techmarine the
    /// chapter does not have.
    ///
    /// The loan satisfies it at every location. The surgery-site rule, which is unchanged, is what
    /// gives the lent adepts a place to work.
    /// </summary>
    public static class TechmarineSupport
    {
        public const string LoanLabel = "Mechanicus adepts (on loan)";
        public const string CoLocatedLabel = "Techmarine co-located";

        public static bool IsOnLoan(PlayerForce force) => force?.IsMechanicusLoanActive == true;

        public static bool IsAvailableAt(PlayerForce force, CampaignLocation location) =>
            IsOnLoan(force) || FindTechmarineAt(force, location) != null;

        /// <summary>
        /// A fit, unreserved Techmarine of the chapter at this location, or null. A brother on
        /// Mars is off-sector, which is the same place as nothing, so he is never found here.
        /// </summary>
        public static PlayerSoldier FindTechmarineAt(PlayerForce force, CampaignLocation location) =>
            location == null
                ? null
                : force?.Army?.PlayerSoldierMap?.Values.FirstOrDefault(member =>
                    CareDestinationService.IsAvailableStaff(force, member, MedicalProcedureService.IsTechmarine)
                    && CampaignLocationService.ForSoldier(member)?.IsSamePlace(location) == true);
    }
}
