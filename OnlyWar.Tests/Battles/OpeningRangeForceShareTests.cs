using System.Collections.Generic;
using System.Linq;

using OnlyWar.Battles;
using OnlyWar.Domain;
using OnlyWar.Domain.Squads;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// The opening range is asked once per squad against the WHOLE opposing force. Until 2026-09-22
/// each squad then planned as if it alone had to remove the enemy's entire kill target, under the
/// enemy's entire fire; in Grist Nine Epsilon (33 marine squads against 453 orks) no single squad
/// could reach that target, so it never bound, and the battle opened at ~740 instead of ~480. A
/// squad now plans its proportional share of both.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class OpeningRangeForceShareTests
{
    private const int SpaceMarineFactionId = 1;
    private const int TacticalSquadTemplateId = 1;
    private const int OrkFactionId = 4;
    private const int ShootaBoyzTemplateId = 39;
    private const int SluggaBoyzTemplateId = 40;

    [Fact]
    public void SquadInALargerForce_OpensNearerThanTheSameSquadAlone()
    {
        (List<BattleSquad> marines, List<BattleSquad> boyz) = CreateForces(squadsPerSide: 3);
        BattleSquad squad = marines[0];

        float alone = BattleEngagementFrameBuilder.CalculatePreferredOpeningRange(squad, boyz);
        float inForce = BattleEngagementFrameBuilder.CalculatePreferredOpeningRange(
            squad, boyz, marines);

        Assert.True(
            inForce < alone - 50,
            $"one of three squads opened at {inForce}, against {alone} when it planned the whole "
                + "kill alone -- the squad is still planning the whole force's share");
    }

    [Fact]
    public void SquadThatIsItsWholeForce_OpensWhereItWouldAlone()
    {
        (List<BattleSquad> marines, List<BattleSquad> boyz) = CreateForces(squadsPerSide: 1);
        BattleSquad squad = marines[0];

        Assert.Equal(
            BattleEngagementFrameBuilder.CalculatePreferredOpeningRange(squad, boyz),
            BattleEngagementFrameBuilder.CalculatePreferredOpeningRange(squad, boyz, marines));
    }

    // Grist Nine Epsilon (2026-09-22): with the kill share met early, extra opening range looked
    // free and tactical squads opened at ~1050, near weapon reach. The orks broke at ~500 and the
    // marines, one cell a turn faster, never brought them back into range. The expected break must
    // now land inside the squad's useful band, so the opening sits inside it too.
    [Fact]
    public void SquadInALargerForce_OpensInsideItsUsefulBand()
    {
        (List<BattleSquad> marines, List<BattleSquad> boyz) = CreateForces(squadsPerSide: 3);
        BattleSquad squad = marines[0];
        RangedEffectivenessCurve curve = RangedEffectivenessCurve.Build(
            squad.AbleSoldiers.ToList(),
            new EngagementTargetProfile(
                boyz.Average(s => s.GetAverageSize()),
                boyz.Average(s => s.GetAverageArmor()),
                boyz.Average(s => s.GetAverageConstitution()),
                boyz.Average(s => s.GetAverageRangedEvasion()),
                boyz.SelectMany(s => s.AbleSoldiers)
                    .Average(s => (float)System.Math.Max(1, s.EffectiveBattleValue))));
        float usefulBand = curve.SaturationRange(RangedEffectivenessCurve.SaturationFraction);

        float opening = BattleEngagementFrameBuilder.CalculatePreferredOpeningRange(
            squad, boyz, marines);

        Assert.True(usefulBand > 0, "fixture must give the tactical squad a useful band");
        Assert.True(
            opening <= usefulBand,
            $"opened at {opening}, outside its {usefulBand:F0}-yard useful band -- the enemy "
                + "would break where this squad can no longer punish the retreat");
    }

    private static (List<BattleSquad> Marines, List<BattleSquad> Boyz) CreateForces(
        int squadsPerSide)
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction marines = blob.Factions.Single(faction => faction.Id == SpaceMarineFactionId);
        Faction orks = blob.Factions.Single(faction => faction.Id == OrkFactionId);
        RNG.Reset(4243);
        OnlyWar.Abstractions.IEntityIdAllocator ids =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        List<BattleSquad> boyz = Enumerable.Range(0, squadsPerSide)
            .Select(i => new BattleSquad(false, SquadFactory.GenerateSquad(
                orks.SquadTemplates[i % 2 == 0 ? SluggaBoyzTemplateId : ShootaBoyzTemplateId],
                new StaticRNG(), ids, $"Boyz {i}")))
            .ToList();
        List<BattleSquad> tacticals = Enumerable.Range(0, squadsPerSide)
            .Select(i => new BattleSquad(false, SquadFactory.GenerateSquad(
                marines.SquadTemplates[TacticalSquadTemplateId],
                new StaticRNG(), ids, $"Tactical {i}")))
            .ToList();
        return (tacticals, boyz);
    }
}
