using Godot;
using OnlyWar.Helpers.UI;

public partial class PlanStatusBadge : Label
{
    public enum Status { Valid, Warning, Blocked, Staged }
    public void SetStatus(Status status, string text)
    {
        Text = text;
        AddThemeColorOverride("font_color", status switch
        {
            Status.Valid => OnlyWarStyle.MedicalStable,
            Status.Warning => OnlyWarStyle.MedicalWarning,
            Status.Blocked => OnlyWarStyle.Critical,
            _ => OnlyWarStyle.Gold
        });
    }
}
