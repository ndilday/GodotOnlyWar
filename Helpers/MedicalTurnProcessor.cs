using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    // Weekly medical resolution run during turn processing (PRD 4.8 / 5.3 Apothecary second
    // pass). For now this is the natural-healing pass that makes the Apothecarium recovery
    // countdowns real; medical-procedure resolution joins it in a later pass.
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
                ApplyWeeklyHealing(soldier?.Body);
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
            foreach (HitLocation location in soldier.Body.HitLocations)
            {
                // A severed location is gone, and an augmetic location requires specialist
                // repair; neither closes a wound on its own.
                if (location.IsSevered || location.IsCybernetic)
                {
                    continue;
                }
                location.Wounds.ClearNegligibleWounds();
            }
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
            foreach (HitLocation location in body.HitLocations)
            {
                // A week passes for every wounded location, but natural healing never restores a
                // location that needs surgical intervention: a severed non-vital location (gone)
                // or a cybernetic location requiring specialist repair. Locations under an
                // active procedure are inherently in that excluded set. Crippled locations do
                // not require replacement for now and therefore remain on this healing path.
                if (location.Wounds.WoundTotal > 0
                    && !location.IsCybernetic
                    && !location.IsSevered
                    && !location.IsCoveredBySeveredParent
                    && !location.IsReplacementEligible)
                {
                    location.Wounds.ApplyWeekOfHealing();
                }
            }
        }

        // Advances each in-progress procedure by a week and, on completion, applies its
        // result to the hit location and removes it (PRD 4.8 / 5.3). Cybernetic completion
        // marks the location augmetic; vat-grown restores it organically. Both clear the
        // location's wounds, returning it to full capability.
        public static void ResolveProcedures(IList<MedicalProcedure> procedures,
                                             IReadOnlyDictionary<int, PlayerSoldier> soldierMap)
        {
            if (procedures == null)
            {
                return;
            }
            for (int i = procedures.Count - 1; i >= 0; i--)
            {
                MedicalProcedure procedure = procedures[i];
                procedure.WeeksRemaining--;
                if (procedure.WeeksRemaining > 0)
                {
                    continue;
                }
                CompleteProcedure(procedure, soldierMap);
                procedures.RemoveAt(i);
            }
            MedicalProcedureService.SynchronizeProcedureReservations(
                soldierMap?.Values,
                procedures);
        }

        private static void CompleteProcedure(MedicalProcedure procedure,
                                              IReadOnlyDictionary<int, PlayerSoldier> soldierMap)
        {
            if (soldierMap == null
                || !soldierMap.TryGetValue(procedure.SoldierId, out PlayerSoldier soldier)
                || soldier?.Body == null)
            {
                return;
            }
            HitLocation location = soldier.Body.HitLocations
                .FirstOrDefault(hl => hl.Template.Id == procedure.HitLocationTemplateId);
            if (location == null)
            {
                return;
            }
            IEnumerable<HitLocation> restoredLocations = soldier.Body.HitLocations
                .Where(candidate => candidate == location
                    || soldier.Body.GetReplacementParent(candidate) == location);
            foreach (HitLocation restoredLocation in restoredLocations)
            {
                restoredLocation.Wounds.HealWounds();
                if (procedure.ProcedureType == MedicalProcedureType.Cybernetic)
                {
                    restoredLocation.IsCybernetic = true;
                }
                else
                {
                    // A destroyed augmetic is replaced like any other destroyed location; vat
                    // growth restores an organic part rather than carrying the old cybernetic
                    // state forward.
                    restoredLocation.IsCybernetic = false;
                }
            }
        }
    }
}
