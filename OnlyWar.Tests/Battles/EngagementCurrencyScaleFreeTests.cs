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
/// The engagement option a squad chooses must not depend on the SCALE of battle value: multiplying
/// every soldier on both sides by the same factor changes no decision. Battle value is one
/// currency across factions (battle value parity, EngagementCurrency, 2026-10-12), so the RATIO
/// between the sides does decide -- a cheap force trades with an elite one at the calibrated
/// rate -- but its unit must not.
///
/// <para>Until 2026-10-12 this asserted the opposite, stronger property: that ONE side's values at
/// ten times could not change a decision (headcount parity, when cross-faction values were not
/// yet calibrated). See <see cref="ForceShareScaleFreeTests"/> for withdrawal and morale, which
/// still stand on headcount shares.</para>
///
/// <para>Each frame is a real squad pair from the rules database, planned once at the database's
/// values and again with both sides' soldier templates at ten times their battle value. These
/// fixtures load rules without a sector, so a soldier's effective battle value is exactly its
/// template's and the rescale is exact.</para>
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class EngagementCurrencyScaleFreeTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public EngagementCurrencyScaleFreeTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    private const int ImperialFactionId = 0;
    private const int SpaceMarineFactionId = 1;
    private const int TyranidFactionId = 2;
    private const int GenestealerCultFactionId = 3;
    private const int OrkFactionId = 4;

    private const int TacticalSquad = 1;
    private const int ScoutSquad = 4;
    private const int GenestealerSquad = 16;
    private const int BroodBrotherSquad = 24;
    private const int BroodBrotherWeaponSquad = 25;
    private const int NeophyteHybridSquad = 26;
    private const int PdfInfantrySquad = 34;
    private const int ShootaBoyz = 39;
    private const int EavyNobz = 46;

    private const int Scale = 10;

    private sealed record Outcome(
        EngagementOptionKind Chosen,
        int CandidateCount,
        string Description);

    [Theory]
    // Ranged against ranged.
    [InlineData(ImperialFactionId, PdfInfantrySquad, GenestealerCultFactionId, NeophyteHybridSquad, 150)]
    [InlineData(SpaceMarineFactionId, TacticalSquad, GenestealerCultFactionId, BroodBrotherSquad, 150)]
    [InlineData(OrkFactionId, ShootaBoyz, SpaceMarineFactionId, TacticalSquad, 100)]
    // Ranged against melee, from both ends.
    [InlineData(SpaceMarineFactionId, TacticalSquad, TyranidFactionId, GenestealerSquad, 60)]
    // The melee end was Genestealers at 20 yards until 2026-10-09. A contact seeker with no ranged
    // weapon that must close is now offered the charge alone, so it has no choice to test; 'Eavy
    // Nobz carry guns and keep a choice of approach inside one move.
    [InlineData(OrkFactionId, EavyNobz, SpaceMarineFactionId, TacticalSquad, 5)]
    // The cult-against-scouts frames that RealTemplateEngagementTests guards.
    [InlineData(GenestealerCultFactionId, NeophyteHybridSquad, SpaceMarineFactionId, ScoutSquad, 150)]
    [InlineData(GenestealerCultFactionId, BroodBrotherWeaponSquad, SpaceMarineFactionId, ScoutSquad, 300)]
    public void ChosenOption_DoesNotChangeWhenBothSidesBattleValueIsRescaledAlike(
        int plannerFactionId,
        int plannerTemplateId,
        int enemyFactionId,
        int enemyTemplateId,
        int separation)
    {
        Outcome baseline = Decide(
            plannerFactionId, plannerTemplateId, enemyFactionId, enemyTemplateId, separation,
            ownScale: 1, enemyScale: 1);
        _output.WriteLine($"chose {baseline.Chosen}. {baseline.Description}");
        // A frame with one legal option proves nothing about the scorer.
        Assert.True(
            baseline.CandidateCount >= 2,
            "only one engagement option was offered. " + baseline.Description);

        foreach ((int ownScale, int enemyScale) in new[] { (Scale, Scale) })
        {
            Outcome rescaled = Decide(
                plannerFactionId, plannerTemplateId, enemyFactionId, enemyTemplateId, separation,
                ownScale, enemyScale);
            Assert.True(
                baseline.Chosen == rescaled.Chosen,
                $"own BV x{ownScale}, enemy BV x{enemyScale}: chose {rescaled.Chosen} where the "
                    + $"database values chose {baseline.Chosen}.\nbaseline: {baseline.Description}"
                    + $"\nrescaled: {rescaled.Description}");
        }
    }

    private static Outcome Decide(
        int plannerFactionId,
        int plannerTemplateId,
        int enemyFactionId,
        int enemyTemplateId,
        int separation,
        int ownScale,
        int enemyScale)
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction plannerFaction = blob.Factions.Single(faction => faction.Id == plannerFactionId);
        Faction enemyFaction = blob.Factions.Single(faction => faction.Id == enemyFactionId);

        RNG.Reset(77_000 + separation);
        OnlyWar.Abstractions.IEntityIdAllocator entityIds =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        BattleSquad planner = CreateSquad(plannerFaction, plannerTemplateId, entityIds, ownScale);
        BattleSquad enemy = CreateSquad(enemyFaction, enemyTemplateId, entityIds, enemyScale);

        BattleGridManager grid = new();
        PlaceLine(grid, planner, side: true, x: 0);
        PlaceLine(grid, enemy, side: false, x: separation);

        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([planner], [enemy]);
        SquadEngagementDecision decision = Planner(grid, planner, enemy).ChooseEngagementOption(
            planner,
            paired.Frames[planner.Id],
            paired.Profiles,
            paired.Frames,
            [enemy]);
        BattleSquadCapabilityProfile profile = paired.Profiles[planner.Id];
        string description = $"{planner.Name} ({planner.Soldiers.Count}, BV "
            + $"{profile.TotalAbleBattleValue:F0}) vs {enemy.Name} ({enemy.Soldiers.Count}, BV "
            + $"{paired.Profiles[enemy.Id].TotalAbleBattleValue:F0}) at {separation}; "
            + $"effectiveRange={profile.EffectiveEngagementRange:F1} "
            + $"usefulFireRange={profile.UsefulFireRange:F1}; candidates: "
            + string.Join(" | ", decision.Candidates
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => $"{candidate.Kind} {candidate.Score:F3}"));
        return new Outcome(decision.Chosen.Kind, decision.Candidates.Count, description);
    }

    private static BattleSquad CreateSquad(
        Faction faction,
        int squadTemplateId,
        OnlyWar.Abstractions.IEntityIdAllocator entityIds,
        int battleValueScale)
    {
        SquadTemplate template = faction.SquadTemplates[squadTemplateId];
        Squad squad = SquadFactory.GenerateSquad(template, new StaticRNG(), entityIds, template.Name);
        BattleSquad battleSquad = new(false, squad);
        if (battleValueScale != 1)
        {
            // After BattleSquad has issued the template's weapons, so the rescale touches nothing
            // but battle value. Fresh template objects, so the shared rules blob is not mutated.
            Dictionary<int, SoldierTemplate> rescaled = [];
            foreach (Soldier soldier in squad.Members.Cast<Soldier>())
            {
                if (!rescaled.TryGetValue(soldier.Template.Id, out SoldierTemplate scaled))
                {
                    scaled = Rescale(soldier.Template, battleValueScale);
                    rescaled[soldier.Template.Id] = scaled;
                }
                soldier.Template = scaled;
            }
        }
        return battleSquad;
    }

    private static SoldierTemplate Rescale(SoldierTemplate template, int scale) => new(
        template.Id,
        template.Species,
        template.Name,
        template.Rank,
        template.Subrank,
        template.IsSquadLeader,
        template.SpecialistType,
        template.MosTraining,
        template.WorkExperienceTrainingProfile,
        template.BattleValue * scale,
        template.PromotionRequirements,
        template.HasAuthoredMeleeFraction ? template.MeleeFraction : null,
        template.WeaponOptionsByGroup,
        template.EquipmentKitOptionsByGroup);

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
            new SeededRNG(77_000));
    }
}
