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

        public static void ApplyWeeklyHealing(Body body)
        {
            if (body == null)
            {
                return;
            }
            foreach (HitLocation location in body.HitLocations)
            {
                // A week passes for every wounded location, but natural healing never
                // restores a location that needs surgical intervention: a severed location
                // (gone) or a crippled functional/vital location (replacement-eligible) stays
                // frozen until a cybernetic/vat-grown procedure treats it. Locations under an
                // active procedure are inherently in that excluded set.
                if (location.Wounds.WoundTotal > 0
                    && !location.IsSevered
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
            location.Wounds.HealWounds();
            if (procedure.ProcedureType == MedicalProcedureType.Cybernetic)
            {
                location.IsCybernetic = true;
            }
        }
    }
}
