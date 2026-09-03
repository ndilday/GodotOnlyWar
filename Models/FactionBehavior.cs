using System;

namespace OnlyWar.Models
{
    /// <summary>
    /// Mechanical faction traits authored by the rules database. Identity flags such as
    /// <see cref="Faction.IsPlayerFaction"/> remain campaign role data, and scalar values such as
    /// <see cref="Faction.FireDiscipline"/> remain separate from this bit field.
    /// </summary>
    [Flags]
    public enum FactionBehavior
    {
        None = 0,
        CanInfiltrate = 1 << 0,
        PopulationIsMilitary = 1 << 1,
        InvadesOnVictory = 1 << 2,
        DefendsHostWhileHidden = 1 << 3,
        OffersExternalEnemyTruce = 1 << 4,
        UniversallyHostile = 1 << 5,
        Indelible = 1 << 6,

        // Reusable campaign and battle capabilities. These flags intentionally describe one
        // policy each; no consumer should infer a faction identity from a combination of them.
        HasGhostPlanets = 1 << 7,
        HasDormantPopulations = 1 << 8,
        GeneratesInvasions = 1 << 9,
        MobMentality = 1 << 10
    }
}
