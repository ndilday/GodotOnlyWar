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
    private const int ImperialFactionId = 0;
    private const int TyranidFactionId = 2;
    // 20 PDF: Sergeant and Troopers with autoguns and a heavy stubber.
    private const int PdfSquadTemplateId = 34;
    // 30 Hormagaunts: melee only.
    private const int HormagauntSquadTemplateId = 17;
    // 30 Shoota Boyz.
    private const int ShootaBoyzSquadTemplateId = 39;

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
            // A team the Scouts destroy within the turn whatever it does (its own clock <= 1:
            // 20 yards and closer since removal per hit came from the engine's wound counter,
            // 2026-10-06) has no good choice left; every option ends at about -10 of its 14
            // battle value, and running is a hair better only because a moving target is harder
            // to hit this turn and the option set has no fast move AWAY (withdrawal is a separate
            // system). There, the charge must merely not stand out from holding.
            if (LastHorizonDiagnostics.OwnWithdrawalTurns <= 1f)
            {
                EngagementOptionEvaluation hold = decision.Candidates.Single(
                    candidate => candidate.Kind == EngagementOptionKind.Hold);
                float teamValue = team.Soldiers.Sum(
                    soldier => soldier.Soldier.Template.BattleValue);
                Assert.True(
                    decision.Chosen.Score - hold.Score <= 0.02f * teamValue,
                    $"a doomed autocannon team's {decision.Chosen.Kind} stood out from holding at "
                        + $"{separation} yards. " + Describe(decision));
                continue;
            }
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

    /// <summary>
    /// Observed 2026-10-07 in the gauntlet: 30 Hormagaunts ran at 20 PDF for 200 yards and then
    /// slowed to a walk at about 30 yards, inside the PDF's best fire band, and routed without
    /// killing anyone. The forecast had read the melee rate at 1 yard into the ranged rates at
    /// 3-8 yards by interpolation, and priced the turn a charge arrived on at the range the turn
    /// started at, so whether the forecast squad broke depended on where its last step landed. A
    /// melee swarm with nothing to shoot keeps running at every range.
    /// </summary>
    [Fact]
    public void HormagauntSwarm_KeepsRunningThroughPdfFire()
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction tyranids = blob.Factions.Single(faction => faction.Id == TyranidFactionId);
        Faction imperial = blob.Factions.Single(faction => faction.Id == ImperialFactionId);
        // 200 and 120 since 2026-10-09: once the PDF fire was priced as the standing aimed fire it
        // is, every continuation forecast a loss from long range and the swarm walked toward the
        // line for 19 turns. A contact seeker with no fire worth keeping has no walk to choose.
        foreach (int separation in new[] { 200, 120, 60, 40, 30, 20, 10 })
        {
            RNG.Reset(77_000 + separation);
            OnlyWar.Abstractions.IEntityIdAllocator entityIds =
                new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
            BattleSquad gaunts = CreateSquad(tyranids, HormagauntSquadTemplateId, entityIds);
            BattleSquad pdf = CreateSquad(imperial, PdfSquadTemplateId, entityIds);
            BattleGridManager grid = new();
            PlaceLine(grid, gaunts, side: true, x: 0);
            PlaceLine(grid, pdf, side: false, x: separation);

            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build([gaunts], [pdf]);
            BattleSquadPlanner planner = Planner(grid, gaunts, pdf);
            SquadEngagementDecision decision = planner.ChooseEngagementOption(
                gaunts,
                paired.Frames[gaunts.Id],
                paired.Profiles,
                paired.Frames,
                [pdf]);
            LastHorizon = planner.ExpectedExchangeTurnsFor(gaunts.Id);
            LastProfile = paired.Profiles[gaunts.Id];
            LastHorizonDiagnostics = planner.EngagementHorizonDiagnosticsFor(gaunts.Id);
            LastSquadSummary = $"{gaunts.Name} against {pdf.Name}";

            Assert.True(
                decision.Chosen.Kind is EngagementOptionKind.RunToward
                    or EngagementOptionKind.CloseToContact,
                $"Hormagaunts chose {decision.Chosen.Kind} at {separation} yards from PDF. "
                    + Describe(decision));
        }
    }

    /// <summary>
    /// Observed 2026-10-09: Neophyte Hybrids and Shoota Boyz at 260 yards each priced the other's
    /// fire 20 to 30 times below its standing rate, because each read the other's baseline posture
    /// (StepBack: both were inside the other's wide preferred band) as a prediction that it would
    /// keep moving and fire un-aimed. Both forecast a win; the Orks lost the firefight four to
    /// one. The forecast assumes an enemy stands and aims unless it charges to contact or flees.
    /// </summary>
    [Fact]
    public void ExchangeForecast_AssumesAShootingEnemyStandsAndAims_ButAChargeStaysACharge()
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction cult = blob.Factions.Single(faction => faction.Id == GenestealerCultFactionId);
        Faction orks = blob.Factions.Single(faction => faction.Id == OrkFactionId);
        Faction tyranids = blob.Factions.Single(faction => faction.Id == TyranidFactionId);
        RNG.Reset(77_500);
        OnlyWar.Abstractions.IEntityIdAllocator entityIds =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        BattleSquad hybrids = CreateSquad(cult, NeophyteHybridSquadTemplateId, entityIds);
        BattleSquad boyz = CreateSquad(orks, ShootaBoyzSquadTemplateId, entityIds);
        BattleSquad gaunts = CreateSquad(tyranids, HormagauntSquadTemplateId, entityIds);
        BattleGridManager grid = new();
        PlaceLine(grid, hybrids, side: true, x: 0);
        PlaceLine(grid, boyz, side: false, x: 260);
        PlaceLine(grid, gaunts, side: false, x: 120);

        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([hybrids], [boyz, gaunts]);
        SquadEngagementFrame boyzFrame = paired.Frames[boyz.Id];
        SquadEngagementFrame gauntFrame = paired.Frames[gaunts.Id];

        // Until 2026-10-10 the Boyz' baseline here was StepBack, which made this the case the
        // forecast posture had to override. A ranged squad in reach now defaults to Hold, so the
        // forecast posture and the baseline agree; the assertion is that it is Hold either way.
        Assert.Equal(
            EngagementOptionKind.Hold,
            EngagementExchangeModel.ForecastFiringPosture(boyzFrame));
        Assert.Equal(EngagementOptionKind.CloseToContact, gauntFrame.BaselinePosture);
        Assert.Equal(
            EngagementOptionKind.CloseToContact,
            EngagementExchangeModel.ForecastFiringPosture(gauntFrame));
    }

    /// <summary>
    /// Observed 2026-10-09: Neophyte Hybrids 15 yards from Shoota Boyz ran into contact on the
    /// strength of their point-blank fire. Neither squad seeks contact, so the forecast priced the
    /// contact state at the standing rates read at 1 yard and never saw the Orks' melee: 100 to
    /// 130 battle value a turn against the Hybrids' 10 to 20. All three Hybrid squads had routed by
    /// turn six. A state in contact is now priced at the contact rates whoever's doctrine it is,
    /// with contact judged by the squads' fronts, not their centroids (a contact option left the
    /// centroids 4.8 yards apart).
    /// </summary>
    [Fact]
    public void NeophyteHybrids_DoNotCloseToContactWithShootaBoyz()
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction cult = blob.Factions.Single(faction => faction.Id == GenestealerCultFactionId);
        Faction orks = blob.Factions.Single(faction => faction.Id == OrkFactionId);
        int offered = 0;
        foreach (int separation in new[] { 3, 5, 7, 9, 11 })
        {
            RNG.Reset(77_600 + separation);
            OnlyWar.Abstractions.IEntityIdAllocator entityIds =
                new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
            // Three Hybrid squads against two mobs, as in the duel that showed it.
            List<BattleSquad> hybrids =
            [
                CreateSquad(cult, NeophyteHybridSquadTemplateId, entityIds),
                CreateSquad(cult, NeophyteHybridSquadTemplateId, entityIds),
                CreateSquad(cult, NeophyteHybridSquadTemplateId, entityIds),
            ];
            List<BattleSquad> boyz =
            [
                CreateSquad(orks, ShootaBoyzSquadTemplateId, entityIds),
                CreateSquad(orks, ShootaBoyzSquadTemplateId, entityIds),
            ];
            BattleGridManager grid = new();
            for (int index = 0; index < hybrids.Count; index++)
            {
                PlaceBlock(grid, hybrids[index], side: true, x: 0, y: index * 12);
            }
            for (int index = 0; index < boyz.Count; index++)
            {
                PlaceBlock(grid, boyz[index], side: false, x: separation, y: 2 + index * 16);
            }

            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build(hybrids, boyz);
            BattleSquadPlanner planner = Planner(grid, [.. hybrids, .. boyz]);
            foreach (BattleSquad squad in hybrids)
            {
                SquadEngagementDecision decision = planner.ChooseEngagementOption(
                    squad, paired.Frames[squad.Id], paired.Profiles, paired.Frames, boyz);
                LastHorizon = planner.ExpectedExchangeTurnsFor(squad.Id);
                LastProfile = paired.Profiles[squad.Id];
                LastHorizonDiagnostics = planner.EngagementHorizonDiagnosticsFor(squad.Id);
                LastSquadSummary = $"{squad.Name} against two Shoota mobs at {separation} yards";
                if (decision.Candidates.Any(candidate =>
                    candidate.Kind == EngagementOptionKind.CloseToContact))
                {
                    offered++;
                }
                Assert.True(
                    decision.Chosen.Kind != EngagementOptionKind.CloseToContact,
                    $"{squad.Name} closed to contact at {separation} yards. " + Describe(decision));
            }
        }
        Assert.True(offered > 0, "contact was never offered, so the test proves nothing");
    }

    /// <summary>A block four wide, so a squad of twenty or thirty stays compact.</summary>
    private static void PlaceBlock(BattleGridManager grid, BattleSquad squad, bool side, int x, int y)
    {
        for (int i = 0; i < squad.Soldiers.Count; i++)
        {
            BattleSoldier soldier = squad.Soldiers[i];
            soldier.TopLeft = (x + (side ? -(i % 4) : i % 4), y + i / 4);
            grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
        }
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
                + $"commitment={candidate.ContactCommitmentCost:F3} "
                + $"actions=[{string.Join(",", (candidate.RootActions ?? [])
                    .GroupBy(action => action.Kind)
                    .Select(group => $"{group.Key}x{group.Count()}"))}]"));
}
