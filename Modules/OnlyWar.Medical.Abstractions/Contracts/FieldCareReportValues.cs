using OnlyWar.Domain.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Medical.Abstractions
{
    /// <summary>One brother's wound stepped down one band by an Apothecary.</summary>
    public sealed record FieldCareTreatment(
        int SoldierId,
        string SoldierName,
        string LocationName,
        WoundLevel FromBand,
        int WoundsMoved,
        float Cost,
        int Day = 0);

    /// <summary>
    /// What field care did for one order (or one garrison location) over the days it ran. Carried
    /// into the mission report so the player can see the Apothecary he sent forward doing something
    /// -- see §7 trap 3 of Design/Reference/SpecialistAttachment.md: the attached specialist's
    /// field-care work must remain visible whether or not he is duty-ready for the engagement.
    ///
    /// A boundary value rather than a treatment-service detail (SB-05b-1, §3.5): mission execution
    /// stores it on its own context and the end-of-turn report reads it, so neither may depend on
    /// the field-care implementation that produces it. <see cref="RecordTreatment"/> is public for
    /// that reason -- the producer no longer shares an assembly with the value.
    /// </summary>
    public sealed class FieldCareReport
    {
        public List<string> ApothecaryNames { get; } = [];
        public List<int> ApothecaryIds { get; } = [];
        public List<FieldCareTreatment> Treatments { get; } = [];
        public float CapacitySpent { get; private set; }
        public HashSet<int> TreatedSoldierIds { get; } = [];

        public int TreatmentCount => Treatments.Count;
        public int TreatedSoldierCount => TreatedSoldierIds.Count;
        public bool HasApothecary => ApothecaryIds.Count > 0;

        public void RecordTreatment(FieldCareTreatment treatment)
        {
            Treatments.Add(treatment);
            TreatedSoldierIds.Add(treatment.SoldierId);
            CapacitySpent += treatment.Cost;
        }
    }
}
