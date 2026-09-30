using System;
using System.Collections.Generic;

namespace OnlyWar.Application;

/// <summary>
/// The Armory screen (TDD §6.14). Today it holds only the
/// Techmarine section; the chapter's armour, weapon and vehicle counts (PRD §6.9) join it later.
/// Every value is detached: nothing here refers to the live campaign graph.
/// </summary>
public sealed record ArmoryOverview(
    Guid SessionToken,
    bool HasArmory,
    string Title,
    string DutyStation,
    string MasterOfTheForge,
    ArmoryLoanView Loan,
    IReadOnlyList<ArmoryTechmarineRow> AtHome,
    IReadOnlyList<ArmoryMarsRow> OnMars,
    IReadOnlyList<ArmoryMarsCandidateRow> MarsCandidates,
    ArmoryReturnDestinationView ReturnDestination)
{
    public static ArmoryOverview Empty(Guid sessionToken) => new(
        sessionToken, false, "Armory", null, null,
        new ArmoryLoanView(false, "No campaign is loaded."),
        [], [], [], new ArmoryReturnDestinationView(ArmoryDestinationKeys.Default, null, []));
}

/// <summary>The Mechanicus loan: whether it runs, and what it means in words.</summary>
public sealed record ArmoryLoanView(bool IsActive, string Summary);

/// <summary>A Techmarine at home, with the Armory ranks he could be promoted to.</summary>
public sealed record ArmoryTechmarineRow(
    int SoldierId,
    string Name,
    string Role,
    string Location,
    string Status,
    IReadOnlyList<ArmoryPromotionOption> Promotions);

/// <summary>
/// One Armory rank above the brother's own. <see cref="BlockedReason"/> says why he cannot take it
/// now; it is null when <see cref="CanPromote"/> is set. A filled Master of the Forge seat is not
/// offered at all.
/// </summary>
public sealed record ArmoryPromotionOption(
    int TemplateId,
    string RoleName,
    bool CanPromote,
    string BlockedReason);

public sealed record ArmoryMarsRow(
    int SoldierId,
    string Name,
    string Departed,
    string Returns,
    int WeeksRemaining);

/// <summary>
/// A brother who meets the Techmarine requirement and may be sent to Mars now.
/// </summary>
public sealed record ArmoryMarsCandidateRow(
    int SoldierId,
    string Name,
    string Role,
    string Formation,
    string Location,
    int Tech);

public sealed record ArmoryDestinationOption(string Key, string Label);

/// <summary>
/// The standing destination for brothers returning from Mars. <see cref="Resolved"/> says where
/// the next returnee would actually report today, after the fallbacks.
/// </summary>
public sealed record ArmoryReturnDestinationView(
    string SelectedKey,
    string Resolved,
    IReadOnlyList<ArmoryDestinationOption> Options);

public static class ArmoryDestinationKeys
{
    public const string Default = "default";
    public static string Ship(int shipId) => $"ship:{shipId}";
    public static string Region(int regionId) => $"region:{regionId}";
}

/// <summary>What an Armory action would do, asked before the player commits it.</summary>
public sealed record ArmoryPrompt(bool CanProceed, string Title, string Message)
{
    public static ArmoryPrompt Blocked(string title, string message) => new(false, title, message);
}

public sealed record ArmoryCommandResult(bool Succeeded, string Message);
