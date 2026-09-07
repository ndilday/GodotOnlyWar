using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.UI;

public static class SquadIconKeys
{
    public static string For(SquadTemplate template)
    {
        if (template == null) return "infantry";
        SquadTypes type = template.SquadType;
        if (type.HasFlag(SquadTypes.HQ)) return "hq";
        if (type.HasFlag(SquadTypes.Elite)) return "elite";
        if (type.HasFlag(SquadTypes.Bodyguard)) return "bodyguard";
        if (type.HasFlag(SquadTypes.Heavy)) return "devastator";
        if (type.HasFlag(SquadTypes.Fast)) return "assault";
        if (type.HasFlag(SquadTypes.Scout)) return "scout";
        return "tactical";
    }
}
