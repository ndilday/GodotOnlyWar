using System;
using System.Collections.Generic;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Application;

public sealed class LoadoutScreenApplication : CampaignScreenApplication,
    ILoadoutScreenApplication
{
    private const string NoCampaignMessage = "No campaign is active.";
    private const string StaleSessionMessage = "This campaign is no longer active.";

    private LoadoutScreenContext Screen => Context.Loadout;

    public LoadoutScreenApplication(CampaignApplicationContext context) : base(context) { }

    public SquadLoadoutView QuerySquadLoadout(int squadId) =>
        Screen?.QuerySquadLoadout(squadId) ?? SquadLoadoutView.Missing;

    public LoadoutCommandResult SetSquadLoadout(
        Guid sessionToken, int squadId, IReadOnlyList<WeaponSet> loadout) =>
        RunCommand(sessionToken, () => Screen?.SetSquadLoadout(sessionToken, squadId, loadout));

    public LoadoutCommandResult ReturnSquadToDoctrine(Guid sessionToken, int squadId) =>
        RunCommand(sessionToken, () => Screen?.ReturnSquadToDoctrine(sessionToken, squadId));

    public LoadoutCommandResult SetSquadCharacterWeaponSet(
        Guid sessionToken, int squadId, int soldierId, WeaponSet weaponSet) =>
        RunCommand(sessionToken, () => Screen?.SetSquadCharacterWeaponSet(
            sessionToken, squadId, soldierId, weaponSet));

    public LoadoutCommandResult ResetSquadCharacterLoadout(
        Guid sessionToken, int squadId, int soldierId) =>
        RunCommand(sessionToken, () => Screen?.ResetSquadCharacterLoadout(
            sessionToken, squadId, soldierId));

    public EquipmentEditorView QuerySoldierEquipmentEditor(int squadId, int soldierId) =>
        Screen?.QuerySoldierEquipmentEditor(squadId, soldierId)
        ?? EquipmentEditorView.Unavailable;

    public LoadoutCommandResult SaveSoldierEquipment(
        Guid sessionToken, int squadId, int soldierId, EquipmentLoadout loadout) =>
        RunCommand(sessionToken, () => Screen?.SaveSoldierEquipment(
            sessionToken, squadId, soldierId, loadout));

    public LoadoutDoctrineScopeView QueryDoctrineScope(int? planetId) =>
        Screen?.QueryDoctrineScope(planetId);

    public LoadoutTemplateDetailView QueryTemplateLoadout(int? planetId, int templateId) =>
        Screen?.QueryTemplateLoadout(planetId, templateId)
        ?? LoadoutTemplateDetailView.Missing;

    public LoadoutCommandResult SaveTemplateLoadout(
        Guid sessionToken, int? planetId, int templateId, IReadOnlyList<WeaponSet> loadout) =>
        RunCommand(sessionToken, () => Screen?.SaveTemplateLoadout(
            sessionToken, planetId, templateId, loadout));

    public LoadoutCommandResult InheritTemplateLoadout(
        Guid sessionToken, int planetId, int templateId) =>
        RunCommand(sessionToken, () => Screen?.InheritTemplateLoadout(
            sessionToken, planetId, templateId));

    public IReadOnlyList<CharacterLoadoutRowData> QueryCharacterRoles() =>
        Screen?.QueryCharacterRoles() ?? [];

    public LoadoutCommandResult SetCharacterRoleWeaponSet(
        Guid sessionToken, int roleId, WeaponSet weaponSet) =>
        RunCommand(sessionToken, () => Screen?.SetCharacterRoleWeaponSet(
            sessionToken, roleId, weaponSet));

    public LoadoutCommandResult ResetCharacterRole(Guid sessionToken, int roleId) =>
        RunCommand(sessionToken, () => Screen?.ResetCharacterRole(sessionToken, roleId));

    public EquipmentEditorView QueryRoleEquipmentEditor(int roleId) =>
        Screen?.QueryRoleEquipmentEditor(roleId) ?? EquipmentEditorView.Unavailable;

    public LoadoutCommandResult SaveRoleEquipment(
        Guid sessionToken, int roleId, EquipmentLoadout loadout) =>
        RunCommand(sessionToken, () => Screen?.SaveRoleEquipment(
            sessionToken, roleId, loadout));

    public OperationalDoctrineView QueryOperationalDoctrine() =>
        Screen?.QueryOperationalDoctrine() ?? LoadoutScreenContext.EmptyOperationalDoctrine();

    public string DescribeOperationalDoctrineConsequence(
        int injuryThresholdIndex, bool requireDutyReadySquadLeader, int minimumStrength) =>
        Screen?.DescribeOperationalDoctrineConsequence(
            injuryThresholdIndex, requireDutyReadySquadLeader, minimumStrength)
        ?? string.Empty;

    public LoadoutCommandResult SaveOperationalDoctrine(
        Guid sessionToken,
        int injuryThresholdIndex,
        bool requireDutyReadySquadLeader,
        int minimumStrength) =>
        RunCommand(sessionToken, () => Screen?.SaveOperationalDoctrine(
            sessionToken,
            injuryThresholdIndex,
            requireDutyReadySquadLeader,
            minimumStrength));

    private LoadoutCommandResult RunCommand(
        Guid sessionToken, Func<LoadoutCommandResult> command)
    {
        if (!Context.HasSession) return LoadoutCommandResult.Failed(NoCampaignMessage);
        if (sessionToken != SessionToken) return LoadoutCommandResult.Failed(StaleSessionMessage);
        return command() ?? LoadoutCommandResult.Failed(NoCampaignMessage);
    }
}
