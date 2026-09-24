using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Battles;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Generation.World;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// The melee-versus-ranged engagement choice, driven with squads raised from the rules database
/// exactly as the campaign raises them. <see cref="SquadEngagementPlanningTests"/> covers the same
/// decision with one-soldier synthetic squads; these cases exist because the bug that motivated
/// the engagement value model (2026-07) was only ever seen with real content: low-odds ranged
/// Genestealer Cult squads charging Space Marine scouts they could not beat in melee.
///
/// <para>The weak side is always the one planning. A charge by light infantry into a squad of
/// Astartes is the failure; walking or jogging to a range where the gun works is not, so the
/// assertion is against the charge options only (see <see cref="IsCharge"/>), and each case first
/// confirms that a charge was actually offered.</para>
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class RealTemplateEngagementTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public RealTemplateEngagementTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    private const int SpaceMarineFactionId = 1;
    private const int GenestealerCultFactionId = 3;
    // 1 Scout Sergeant + 9 Scout Marines, Bolter + Bolt Pistol, scout armour.
    private const int ScoutSquadTemplateId = 4;
    // 1 leader + 15 Brood Brothers with lasguns and up to two grenade launchers.
    private const int BroodBrotherSquadTemplateId = 24;
    // Two Brood Brothers crewing one autocannon.
    private const int BroodBrotherWeaponSquadTemplateId = 25;
    // Neophyte Hybrids with autogun + autopistol.
    private const int NeophyteHybridSquadTemplateId = 26;
    private const int OrkFactionId = 4;
    private const int EavyNobzSquadTemplateId = 46;
    private const int HeavyBolterWeaponSetId = 5;
    private const int SniperRifleWeaponSetId = 11;

    // Meeting-engagement distances, from long rifle range down to within one move of contact.
    private static readonly int[] Separations = [300, 150, 60, 20, 6];

    /// <summary>
    /// At 150 yards a cult infantry squad can hurt scouts -- an autogun penetrates scout armour
    /// on about half its hits there -- while the scouts' bolters can hurt it far more. Running
    /// in gives up this turn's fire and crosses ground under that fire, so the squad should use
    /// its guns (aim or fire) rather than run at them.
    ///
    /// <para>Not "at its own engagement range": for Neophyte Hybrids against scouts that range
    /// is contact (1 yard), because their best EXCHANGE is point blank, which is not the same as
    /// being unable to hurt them from farther out.</para>
    /// </summary>
    [Theory]
    [InlineData(NeophyteHybridSquadTemplateId)]
    [InlineData(BroodBrotherSquadTemplateId)]
    public void CultInfantry_At150Yards_ShootsRatherThanChargingScouts(int cultSquadTemplateId)
    {
        const int separation = 150;
        (BattleSquad cult, BattleSquad scouts, SquadEngagementDecision decision) =
            Decide(cultSquadTemplateId, separation, seed: 76_100);
        _output.WriteLine($"chose {decision.Chosen.Kind} at {separation} yards. " + Describe(decision));

        Assert.False(
            IsCharge(decision.Chosen.Kind),
            $"{cult.Name} ({cult.Soldiers.Count} soldiers) chose {decision.Chosen.Kind} against "
                + $"{scouts.Name} ({scouts.Soldiers.Count} soldiers) at {separation} yards. "
                + Describe(decision));
        Assert.True(
            (decision.Chosen.RootActions ?? []).Any(action =>
                action.Kind is PlannedSoldierActionKind.Shoot or PlannedSoldierActionKind.Aim),
            $"{cult.Name} chose {decision.Chosen.Kind} at {separation} yards without aiming "
                + "or firing. " + Describe(decision));
    }

    [Fact]
    public void CultHeavyWeaponTeam_DoesNotChargeAScoutSquad()
    {
        foreach (int separation in Separations)
        {
            (BattleSquad team, BattleSquad scouts, SquadEngagementDecision decision) =
                Decide(BroodBrotherWeaponSquadTemplateId, separation, seed: 76_200 + separation);

            Assert.Contains(
                team.Soldiers.SelectMany(soldier => soldier.RangedWeapons),
                weapon => weapon.Template.Name.Contains("Autocannon", StringComparison.Ordinal));
            Assert.False(
                IsCharge(decision.Chosen.Kind),
                $"a two-man autocannon team chose {decision.Chosen.Kind} against {scouts.Name} "
                    + $"({scouts.Soldiers.Count} soldiers) at {separation} yards. "
                    + Describe(decision));
        }
    }

    /// <summary>
    /// Observed 2026-09-23 (Grist Nine Epsilon): two sniper scout squads ambushed 'Eavy Nobz at
    /// ~975 yards, fired the pre-aimed opening volley, then ran at the Nobz for 39 turns without a
    /// shot. Once the ambush aim is spent, Hold only buys aim, and the access term priced the
    /// squad as helpless from the un-aimed rate -- but a sniper rifle's value is almost entirely
    /// its aim. A squad that can aim-and-fire where it stands is not waiting for access.
    /// </summary>
    [Fact]
    public void SniperScouts_WithSpentAim_DoNotCloseOnEavyNobzFromLongRange()
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction marines = blob.Factions.Single(faction => faction.Id == SpaceMarineFactionId);
        Faction orks = blob.Factions.Single(faction => faction.Id == OrkFactionId);

        RNG.Reset(76_300);
        OnlyWar.Abstractions.IEntityIdAllocator entityIds =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        WeaponSet sniper = blob.WeaponSets[SniperRifleWeaponSetId];
        WeaponSet heavyBolter = blob.WeaponSets[HeavyBolterWeaponSetId];
        BattleSquad scouts = CreateSquad(
            marines,
            ScoutSquadTemplateId,
            entityIds,
            [.. Enumerable.Repeat(sniper, 8), heavyBolter]);
        BattleSquad nobz = CreateSquad(orks, EavyNobzSquadTemplateId, entityIds);
        Assert.Contains(
            scouts.Soldiers.SelectMany(soldier => soldier.RangedWeapons),
            weapon => weapon.Template.Id == sniper.PrimaryRangedWeapon.Id);

        BattleGridManager grid = new();
        PlaceLine(grid, scouts, side: true, x: 0);
        PlaceLine(grid, nobz, side: false, x: 975);

        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([scouts], [nobz]);
        SquadEngagementDecision decision = Planner(grid, scouts, nobz).ChooseEngagementOption(
            scouts,
            paired.Frames[scouts.Id],
            paired.Profiles,
            paired.Frames,
            [nobz]);

        Assert.True(
            decision.Candidates.Any(candidate => candidate.Kind == EngagementOptionKind.Hold),
            "Hold was not offered. " + Describe(decision));
        Assert.True(
            decision.Chosen.Kind is not (EngagementOptionKind.RunToward
                or EngagementOptionKind.JogToward
                or EngagementOptionKind.CloseToContact),
            $"sniper scouts chose {decision.Chosen.Kind} toward 'Eavy Nobz at 975 yards. "
                + Describe(decision));
    }

    private static (BattleSquad Cult, BattleSquad Scouts, SquadEngagementDecision Decision) Decide(
        int cultSquadTemplateId,
        int separation,
        int seed)
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction cultFaction = blob.Factions.Single(faction => faction.Id == GenestealerCultFactionId);
        Faction marines = blob.Factions.Single(faction => faction.Id == SpaceMarineFactionId);

        RNG.Reset(seed);
        OnlyWar.Abstractions.IEntityIdAllocator entityIds =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        BattleSquad cult = CreateSquad(cultFaction, cultSquadTemplateId, entityIds);
        BattleSquad scouts = CreateSquad(marines, ScoutSquadTemplateId, entityIds);

        BattleGridManager grid = new();
        PlaceLine(grid, cult, side: true, x: 0);
        PlaceLine(grid, scouts, side: false, x: separation);

        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([cult], [scouts]);
        BattleSquadPlanner planner = Planner(grid, cult, scouts);
        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            cult,
            paired.Frames[cult.Id],
            paired.Profiles,
            paired.Frames,
            [scouts]);
        LastHorizon = planner.ExpectedExchangeTurnsFor(cult.Id);
        LastProfile = paired.Profiles[cult.Id];
        LastHorizonDiagnostics = planner.EngagementHorizonDiagnosticsFor(cult.Id);
        LastSquadSummary = $"{cult.Name}: {cult.AbleSoldiers.Count} soldiers, "
            + $"BV {paired.Profiles[cult.Id].TotalAbleBattleValue:F1}; "
            + $"{scouts.Name}: {scouts.AbleSoldiers.Count} soldiers, "
            + $"BV {paired.Profiles[scouts.Id].TotalAbleBattleValue:F1}";
        // The charge must have been on the table and lost, or these tests prove nothing.
        Assert.True(
            decision.Candidates.Any(candidate => IsCharge(candidate.Kind)),
            $"no charge was offered at {separation} yards. " + Describe(decision));
        return (cult, scouts, decision);
    }

    /// <summary>
    /// Raised the way the campaign raises a squad: SquadFactory fills the template's elements with
    /// soldiers carrying their MOS training, and BattleSquad allocates the template's weapon sets
    /// and armour. See BattleMoraleResolverTests.CreateNpcSquad.
    /// </summary>
    private static BattleSquad CreateSquad(
        Faction faction,
        int squadTemplateId,
        OnlyWar.Abstractions.IEntityIdAllocator entityIds,
        List<WeaponSet> loadout = null)
    {
        SquadTemplate template = faction.SquadTemplates[squadTemplateId];
        Squad squad = SquadFactory.GenerateSquad(template, new StaticRNG(), entityIds, template.Name);
        if (loadout != null) squad.Loadout = loadout;
        return new BattleSquad(false, squad);
    }

    /// <summary>A two-deep line abreast, so the squads face each other across the x axis.</summary>
    private static void PlaceLine(BattleGridManager grid, BattleSquad squad, bool side, int x)
    {
        for (int i = 0; i < squad.Soldiers.Count; i++)
        {
            BattleSoldier soldier = squad.Soldiers[i];
            soldier.TopLeft = (x + (i % 2), i / 2);
            grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
        }
    }

    private static BattleSquadPlanner Planner(BattleGridManager grid, params BattleSquad[] squads)
    {
        Dictionary<int, BattleSoldier> soldiers = squads
            .SelectMany(squad => squad.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        Dictionary<int, MeleeWeaponTemplate> melee = soldiers.Values
            .SelectMany(soldier => soldier.MeleeWeapons
                .Select(weapon => weapon.Template)
                .Append(soldier.Soldier.Template.Species.DefaultUnarmedWeapon))
            .GroupBy(template => template.Id)
            .ToDictionary(group => group.Key, group => group.First());
        return new BattleSquadPlanner(
            grid,
            soldiers,
            new List<IAction>(),
            new List<IAction>(),
            new List<IAction>(),
            null,
            melee,
            new SeededRNG(76_000));
    }

    /// <summary>
    /// A charge is CloseToContact when contact is one move away. From farther out, a squad that
    /// does not seek contact is offered RunToward in its place (SquadEngagementPolicy's option
    /// mask), so a charge from range is a run, turn after turn, until contact is in reach.
    /// </summary>
    private static bool IsCharge(EngagementOptionKind kind) =>
        kind is EngagementOptionKind.CloseToContact or EngagementOptionKind.RunToward;

    // The expected-exchange horizon the last Decide call planned under. Diagnostic only.
    private static float LastHorizon;
    private static BattleSquadCapabilityProfile LastProfile;
    private static EngagementHorizonDiagnostics LastHorizonDiagnostics;
    private static string LastSquadSummary;

    private static string Describe(SquadEngagementDecision decision) =>
        $"{LastSquadSummary}. "
        + $"horizon={LastHorizon:F1} turns "
        + $"(enemy clock {LastHorizonDiagnostics.EnemyWithdrawalTurns:F1}: "
        + $"{LastHorizonDiagnostics.EnemyBattleValueBeforeWithdrawal:F1} BV at "
        + $"{LastHorizonDiagnostics.OutgoingRemovalRate:F3}/turn; "
        + $"own clock {LastHorizonDiagnostics.OwnWithdrawalTurns:F1}: "
        + $"{LastHorizonDiagnostics.OwnBattleValueBeforeWithdrawal:F1} BV at "
        + $"{LastHorizonDiagnostics.IncomingRemovalRate:F3}/turn), "
        + $"effectiveRange={LastProfile?.EffectiveEngagementRange:F1}, "
        + $"usefulFireRange={LastProfile?.UsefulFireRange:F1}, "
        + $"band=[{LastProfile?.PreferredBandLower:F1}, {LastProfile?.PreferredBandUpper:F1}], "
        + $"move={LastProfile?.MoveSpeed:F1}, contactSeeking={LastProfile?.IsContactSeeking}. "
        + "candidates: " + string.Join(" | ", decision.Candidates
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => $"{candidate.Kind} score={candidate.Score:F3} "
                + $"outgoing={candidate.ImmediateEnemyRemoval:F3} "
                + $"readiness={candidate.ReadinessValue:F3} "
                + $"fireWindow={candidate.FireWindowValue:F3} "
                + $"incoming={candidate.IncomingNow:F3} "
                + $"arrival={candidate.ArrivalTimeValue:F3} "
                + $"future=[{string.Join(",", candidate.FutureExchange.Select(value => value.ToString("F2")))}] "
                + $"role={candidate.RoleTerm:F3} "
                + $"morale={candidate.MoralePotentialValue:F3} "
                + $"command={candidate.CommandPotentialValue:F3} "
                + $"access={candidate.AccessPotentialValue:F3} "
                + $"commitment={candidate.ContactCommitmentCost:F3} "
                + $"actions=[{string.Join(",", (candidate.RootActions ?? [])
                    .GroupBy(action => action.Kind)
                    .Select(group => $"{group.Key}x{group.Count()}"))}]"));
}
