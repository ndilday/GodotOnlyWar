using OnlyWar.Models.Soldiers;
using OnlyWar.Contracts.Medical;
using OnlyWar.Medical.Treatment;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    // Campaign cadence adapter for the weekly medical resolution (PRD 4.8 / 5.3 Apothecary
    // second pass). The headless Medical module owns body/procedure transitions; this adapter
    // preserves the existing turn order and applies campaign-side recovery bookkeeping.
    public static class MedicalTurnProcessor
    {
        public static void ApplyWeeklyHealing(IEnumerable<ISoldier> soldiers)
        {
            if (soldiers == null)
            {
                return;
            }
            foreach (ISoldier soldier in soldiers)
            {
                MedicalHealthPolicy.ApplyWeeklyHealing(soldier?.Body);
                MarkAwaitingReunionWhenRecovered(soldier as PlayerSoldier);
            }
        }

        /// <summary>
        /// The end-of-campaign-day pass (Design/Reference/CasualtyRealism.md §2.5): species with
        /// <see cref="SpeciesAbilities.AcceleratedHealing"/> shed their Negligible wounds
        /// overnight. Everyone else is untouched and stays entirely on the weekly cascade.
        ///
        /// Idempotent by construction -- clearing a band that is already clear does nothing -- so
        /// running it more often than once a day is harmless. That is what lets it be hung off the
        /// mission day loop and the weekly upkeep pass without either having to know about the
        /// other.
        /// </summary>
        public static void ApplyDailyHealing(IEnumerable<ISoldier> soldiers)
        {
            if (soldiers == null)
            {
                return;
            }
            foreach (ISoldier soldier in soldiers)
            {
                ApplyDailyHealing(soldier);
            }
        }

        public static void ApplyDailyHealing(ISoldier soldier)
        {
            if (soldier?.Body == null || !HasAcceleratedHealing(soldier))
            {
                return;
            }
            MedicalHealthPolicy.ApplyDailyHealing(soldier.Body, HasAcceleratedHealing(soldier));
        }

        private static bool HasAcceleratedHealing(ISoldier soldier) =>
            soldier.Template?.Species?.Abilities.HasFlag(SpeciesAbilities.AcceleratedHealing)
            ?? false;

        public static void ApplyWeeklyHealing(Body body)
        {
            if (body == null)
            {
                return;
            }
            MedicalHealthPolicy.ApplyWeeklyHealing(body);
        }

        // Advances each in-progress procedure by a week and, on completion, applies its
        // result to the hit location and removes it (PRD 4.8 / 5.3). Cybernetic completion
        // marks the location augmetic; vat-grown restores it organically. Both clear the
        // location's wounds, returning it to full capability.
        public static IReadOnlyList<CompletedMedicalProcedure> ResolveProcedures(
            IList<MedicalProcedure> procedures,
            IReadOnlyDictionary<int, PlayerSoldier> soldierMap)
        {
            List<CompletedMedicalProcedure> completed = new();
            if (procedures == null)
            {
                return completed;
            }
            Dictionary<int, Body> bodies = soldierMap?.Values
                .Where(soldier => soldier?.Body != null)
                .ToDictionary(soldier => soldier.Id, soldier => soldier.Body)
                ?? new Dictionary<int, Body>();
            for (int i = procedures.Count - 1; i >= 0; i--)
            {
                MedicalProcedure procedure = procedures[i];
                MedicalProcedureCompletion transition = MedicalHealthPolicy.AdvanceProcedure(
                    procedure, bodies);
                if (procedure.WeeksRemaining > 0) continue;
                if (transition != null
                    && soldierMap?.TryGetValue(transition.SoldierId, out PlayerSoldier soldier) == true)
                {
                    completed.Add(new CompletedMedicalProcedure(
                        soldier,
                        transition.PrimaryHitLocationTemplateId,
                        transition.PrimaryHitLocationName,
                        transition.ProcedureType,
                        transition.WasAlreadyCybernetic,
                        transition.ProcedureDurationWeeks,
                        transition.RequisitionCost));
                }
                procedures.RemoveAt(i);
            }
            MedicalProcedureService.SynchronizeProcedureReservations(
                soldierMap?.Values,
                procedures);
            foreach (PlayerSoldier soldier in completed.Select(item => item.Soldier).Distinct())
            {
                MarkAwaitingReunionWhenRecovered(soldier);
            }
            return completed;
        }

        private static void MarkAwaitingReunionWhenRecovered(PlayerSoldier soldier)
        {
            if (soldier?.IndividualPosting?.Kind != IndividualPostingKind.MedicalDetachment
                || soldier.IsUndergoingMedicalProcedure
                || soldier.Body?.HitLocations.Any(location =>
                    location.Wounds.WoundTotal > 0 || location.IsSevered) == true)
            {
                return;
            }
            new IndividualPostingService().MarkAwaitingReunion(soldier);
        }

    }
}
