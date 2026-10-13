namespace OnlyWar.Domain.Soldiers;

/// <summary>
/// One tier of installed augmetic part (OnlyWar_TDD.md §5.3). Rules data,
/// from the <c>CyberneticOption</c> table. A part installed from this option has its own natural
/// armor (<see cref="Armor"/>) and its own constitution (<see cref="Constitution"/>) instead of the
/// soldier's, both read by wound resolution only.
/// </summary>
public sealed class CyberneticOption
{
    public int Id { get; }
    public string Name { get; }
    public float Armor { get; }
    public float Constitution { get; }

    public CyberneticOption(int id, string name, float armor, float constitution)
    {
        Id = id;
        Name = name;
        Armor = armor;
        Constitution = constitution;
    }
}

/// <summary>
/// A queued Techmarine repair of one damaged cybernetic part (OnlyWar_TDD.md §6.6.1).
/// Persisted separately from <see cref="MedicalProcedure"/>: a repair is measured in working hours
/// and continues across days, where a procedure counts down whole weeks.
/// </summary>
/// <remarks>
/// The job saves the work DONE, in hours at the reference skill, not a fixed remaining figure. The
/// hours a job needs come from the part's current worst wound band, so a part that takes more
/// damage while it waits needs more work, and the progress already made still counts.
/// Requisition is paid when the job is queued.
/// </remarks>
public sealed class CyberneticRepairJob
{
    public int SoldierId { get; set; }
    public int HitLocationTemplateId { get; set; }
    public float HoursCompleted { get; set; }
    public int RequisitionCost { get; set; }

    public CyberneticRepairJob() { }

    public CyberneticRepairJob(
        int soldierId,
        int hitLocationTemplateId,
        float hoursCompleted,
        int requisitionCost)
    {
        SoldierId = soldierId;
        HitLocationTemplateId = hitLocationTemplateId;
        HoursCompleted = hoursCompleted;
        RequisitionCost = requisitionCost;
    }
}
