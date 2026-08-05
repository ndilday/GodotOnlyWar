using OnlyWar.Models.Missions;

namespace OnlyWar.Helpers.Fortifications
{
    /// <summary>
    /// Player-facing names for the defensive works a <see cref="DefenseType"/> stands for.
    /// </summary>
    /// <remarks>
    /// Two registers, one source: <see cref="Label"/> is the title-case form used where the works
    /// name a row or a mission ("Anti-Air"), <see cref="Prose"/> the lowercase noun phrase that
    /// reads correctly mid-sentence in a report ("... sabotaged enemy anti-air batteries"). Kept
    /// together so a rename of the works can't leave the two renderings disagreeing.
    /// </remarks>
    public static class DefenseTypeNames
    {
        public static string Label(DefenseType defenseType) => defenseType switch
        {
            DefenseType.Entrenchment => "Entrenchments",
            DefenseType.ListeningPost => "Listening Post",
            DefenseType.AntiAir => "Anti-Air",
            DefenseType.Organization => "Organization",
            _ => defenseType.ToString()
        };

        public static string Prose(DefenseType defenseType) => defenseType switch
        {
            DefenseType.Entrenchment => "entrenchments",
            DefenseType.ListeningPost => "listening posts",
            DefenseType.AntiAir => "anti-air batteries",
            DefenseType.Organization => "command organization",
            _ => defenseType.ToString().ToLowerInvariant()
        };
    }
}
