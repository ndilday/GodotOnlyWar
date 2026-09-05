using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Contracts.Battles;

/// <summary>Campaign-resolved equipment access, scoped to one mission's participant set.</summary>
public interface IBattleEquipmentSource
{
    System.Collections.Generic.IReadOnlyList<WeaponSet> GetSquadLoadout(Squad squad);
    bool UsesItemizedAttribution { get; }
    WeaponSet GetCharacterWeapons(ISoldier soldier);
    ResolvedEquipmentLoadout Resolve(ISoldier soldier, Squad squad, int functioningHands);
}
