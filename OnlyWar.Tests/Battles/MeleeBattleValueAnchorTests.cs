using System.Linq;
using OnlyWar.Helpers.Database.GameRules;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class MeleeBattleValueAnchorTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public void RecalculatedRules_MeleeBalanceAnchorsLandInExpectedBands()
    {
        GameRulesBlob rules = RulesDatabaseFixture.LoadRules();

        int Value(string soldierName) => rules.Factions
            .SelectMany(faction => faction.SoldierTemplates.Values)
            .Single(template => template.Name == soldierName)
            .BattleValue;

        int hormagaunt = Value("Hormagaunt");
        int tacticalMarine = Value("Tactical Marine");
        int genestealer = Value("Genestealer");
        int meleeCarnifex = Value("Melee Carnifex");
        int pdfTrooper = Value("PDF Trooper");

        Assert.True(hormagaunt > 0);
        Assert.True(tacticalMarine > 0);
        Assert.True(genestealer > 0);
        Assert.True(meleeCarnifex > 0);
        Assert.True(pdfTrooper > 0);

        // Strategic BV includes each template's best available weapon, so the tactical marine's
        // bolter makes this a strategic-tier check rather than a pure melee duel probability.
        Assert.True(hormagaunt < tacticalMarine * 0.10,
            $"Hormagaunt BV {hormagaunt} should be far below Tactical Marine BV {tacticalMarine}.");

        double genestealerRatio = (double)genestealer / tacticalMarine;
        Assert.InRange(genestealerRatio, 0.20, 0.40);

        Assert.True(meleeCarnifex >= pdfTrooper * 8,
            $"Melee Carnifex BV {meleeCarnifex} should dominate an 8-PDF group ({pdfTrooper * 8}).");
        Assert.True(meleeCarnifex > tacticalMarine * 2);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void TakeOutAllocation_UsesTheSeventyFivePercentPlannerThreshold()
    {
        int trials = OnlyWar.Helpers.Battles.MeleeMath.CalculateTrialsForCumulativeSuccess(0.25f);

        Assert.Equal(5, trials);
        Assert.True(1 - System.Math.Pow(0.75, trials)
            >= OnlyWar.Helpers.Battles.MeleeMath.TakeOutConfidenceTarget);
    }
}
