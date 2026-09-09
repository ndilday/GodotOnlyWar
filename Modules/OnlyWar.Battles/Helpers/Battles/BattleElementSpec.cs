using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System.Collections.Generic;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Mission-local tactical metadata. It deliberately has no campaign-organizational
    /// identity requirement, allowing an assigned character to be a normal battle element while
    /// retaining its PlayerSoldier as the casualty/recovery settlement target.
    /// </summary>
    public sealed record BattleElementSpec(
        int TacticalId,
        string Name,
        Faction Faction,
        IReadOnlyList<ISoldier> Members,
        BattleElementTraits Traits,
        Squad CampaignSquad = null,
        PlayerSoldier CampaignCharacter = null);

    public sealed record BattleElementTraits(
        bool ProvidesCommandAura = false,
        bool ProvidesSynapse = false,
        bool IsHeadquarters = false);
}
