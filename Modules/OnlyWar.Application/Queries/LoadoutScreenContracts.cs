using System;
using System.Collections.Generic;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Application;

/// <summary>
/// The loadout surfaces exchange authored rules templates — <see cref="WeaponSet"/>,
/// <see cref="EquipmentLoadout"/>, <see cref="EquipmentRulesCatalog"/> and the validation context
/// built from them. Those are immutable rules data, not the campaign graph: no live squad,
/// soldier, doctrine or force crosses this boundary, and every write is a command below.
/// </summary>
public sealed record LoadoutCommandResult(bool Succeeded, string Message = null)
{
    public static LoadoutCommandResult Ok() => new(true);
    public static LoadoutCommandResult Failed(string message) => new(false, message);
}

/// <summary>Everything the squad loadout screen prints for one squad.</summary>
public sealed record SquadLoadoutView(
    bool Exists,
    string Title,
    string Subtitle,
    string SourceText,
    IReadOnlyList<WeaponSet> Loadout,
    bool IsCustom,
    IReadOnlyList<CharacterLoadoutRowData> CharacterRows,
    IReadOnlyList<ElementCountSectionData> CountSections)
{
    public static readonly SquadLoadoutView Missing =
        new(false, null, null, null, [], false, [], []);
}

/// <summary>The itemized equipment editor's opening state for one soldier or one chapter role.</summary>
public sealed record EquipmentEditorView(
    bool IsAvailable,
    string Title,
    string Subtitle,
    EquipmentRulesCatalog Catalog,
    EquipmentLoadout Loadout,
    EquipmentValidationContext Context)
{
    public static readonly EquipmentEditorView Unavailable =
        new(false, null, null, null, null, null);
}

/// <summary>One squad type in the doctrine dialog's list.</summary>
public sealed record LoadoutTemplateOption(int TemplateId, string Label, bool IsSelectedByDefault);

public sealed record LoadoutDoctrineScopeView(
    bool IsAvailable,
    string Title,
    string SquadModeSubtitle,
    string CharacterModeSubtitle,
    string DoctrineModeSubtitle,
    string SaveButtonText,
    IReadOnlyList<LoadoutTemplateOption> Templates);

public sealed record LoadoutTemplateDetailView(
    bool Exists,
    string Name,
    string SourceText,
    IReadOnlyList<WeaponSet> Loadout,
    IReadOnlyList<ElementCountSectionData> CountSections,
    bool CanInherit)
{
    public static readonly LoadoutTemplateDetailView Missing =
        new(false, "No operational squad types", "", [], [], false);
}

/// <summary>
/// The Chapter operational doctrine as editable values plus the threshold vocabulary. The dialog
/// stages these three numbers and asks for the roster consequence; it never holds the doctrine.
/// </summary>
public sealed record OperationalDoctrineView(
    IReadOnlyList<string> InjuryThresholdLabels,
    int InjuryThresholdIndex,
    bool RequireDutyReadySquadLeader,
    int MinimumDutyReadySquadStrength);

/// <summary>
/// The squad loadout screen and the shared chapter/theater doctrine dialog. Squads, soldiers,
/// roles and worlds are all named by id.
/// </summary>
public interface ILoadoutScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    SquadLoadoutView QuerySquadLoadout(int squadId);

    LoadoutCommandResult SetSquadLoadout(
        Guid sessionToken, int squadId, IReadOnlyList<WeaponSet> loadout);

    LoadoutCommandResult ReturnSquadToDoctrine(Guid sessionToken, int squadId);

    LoadoutCommandResult SetSquadCharacterWeaponSet(
        Guid sessionToken, int squadId, int soldierId, WeaponSet weaponSet);

    LoadoutCommandResult ResetSquadCharacterLoadout(
        Guid sessionToken, int squadId, int soldierId);

    EquipmentEditorView QuerySoldierEquipmentEditor(int squadId, int soldierId);

    LoadoutCommandResult SaveSoldierEquipment(
        Guid sessionToken, int squadId, int soldierId, EquipmentLoadout loadout);

    /// <summary>The doctrine dialog for chapter scope (null world) or one theater override.</summary>
    LoadoutDoctrineScopeView QueryDoctrineScope(int? planetId);

    LoadoutTemplateDetailView QueryTemplateLoadout(int? planetId, int templateId);

    LoadoutCommandResult SaveTemplateLoadout(
        Guid sessionToken, int? planetId, int templateId, IReadOnlyList<WeaponSet> loadout);

    LoadoutCommandResult InheritTemplateLoadout(
        Guid sessionToken, int planetId, int templateId);

    /// <summary>Every personal-equipment role the chapter actually fields, as editor rows.</summary>
    IReadOnlyList<CharacterLoadoutRowData> QueryCharacterRoles();

    LoadoutCommandResult SetCharacterRoleWeaponSet(
        Guid sessionToken, int roleId, WeaponSet weaponSet);

    LoadoutCommandResult ResetCharacterRole(Guid sessionToken, int roleId);

    EquipmentEditorView QueryRoleEquipmentEditor(int roleId);

    LoadoutCommandResult SaveRoleEquipment(
        Guid sessionToken, int roleId, EquipmentLoadout loadout);

    OperationalDoctrineView QueryOperationalDoctrine();

    /// <summary>How many soldiers and squads the staged doctrine would hold back right now.</summary>
    string DescribeOperationalDoctrineConsequence(
        int injuryThresholdIndex, bool requireDutyReadySquadLeader, int minimumStrength);

    LoadoutCommandResult SaveOperationalDoctrine(
        Guid sessionToken,
        int injuryThresholdIndex,
        bool requireDutyReadySquadLeader,
        int minimumStrength);
}
