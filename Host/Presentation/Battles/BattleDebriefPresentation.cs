using OnlyWar.Application;

namespace OnlyWar.Host.Presentation.Battles;

public static class BattleDebriefPresentation
{
    /// <summary>
    /// Formats the compact casualty banner shown above a battle in the mission debrief.
    /// </summary>
    public static string BuildSummaryLine(BattleDebriefView report)
    {
        if (report == null) return string.Empty;
        string friendly = report.PlayerIncapacitated > 0
            ? $"Friendly dead: {report.PlayerDeaths}    Friendly incapacitated: {report.PlayerIncapacitated}"
            : $"Friendly dead: {report.PlayerDeaths}";
        return $"{friendly}    Opposing dead: {report.OpposingDeaths}";
    }
}
