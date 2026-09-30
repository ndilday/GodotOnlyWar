using System.Linq;
using OnlyWar.Campaign;
using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

/// <summary>
/// Mars pipeline (TDD §6.14): a specialist branch managed on its own
/// screen is never an ordinary transfer target. A gifted line brother cannot become a Techmarine
/// without Mars, and a Techmarine becomes Master of the Forge only on the Armory screen.
/// </summary>
public class BranchScreenTransferTests
{
    private readonly SectorSimulationFixture _fixture = SectorSimulationFixture.Create();
    private ChapterGenerationDoctrine Doctrine => _fixture.Rules.ChapterDoctrine;

    [Fact]
    public void TheBranchIsOfferedByTheUnrestrictedRule_AndWithheldByTheRestrictedOne()
    {
        Unit chapter = Chapter(out Squad armory, out Squad tactical);
        armory.AddSquadMember(Brother(Doctrine.MasterOfTheForge));
        PlayerSoldier gifted = Brother(Doctrine.TacticalMarine);
        tactical.AddSquadMember(gifted);
        SoldierTransferService open = new();
        SoldierTransferService restricted = new([Doctrine.Techmarine.SpecialistType]);

        SoldierTransferOption techmarineSlot = open.GetTransferOptions(chapter, gifted)
            .Single(option => option.SoldierTemplate == Doctrine.Techmarine);

        Assert.DoesNotContain(restricted.GetTransferOptions(chapter, gifted),
            option => option.SoldierTemplate.SpecialistType == Doctrine.Techmarine.SpecialistType);
        // The mutation boundary refuses a stale or hand-built option too.
        Assert.False(restricted.ApplyTransfer(
            gifted, techmarineSlot, chapter.GetAllSquads().ToDictionary(squad => squad.Id), new Date(42, 1, 1)));
        Assert.Same(tactical, gifted.AssignedSquad);
        Assert.Same(Doctrine.TacticalMarine, gifted.Template);
    }

    [Fact]
    public void ATechmarineHasNoOrdinaryRouteToMasterOfTheForge()
    {
        Unit chapter = Chapter(out Squad armory, out _);
        PlayerSoldier techmarine = Brother(Doctrine.Techmarine, tech: 120f, lead: 80f);
        armory.AddSquadMember(techmarine);

        SoldierTransferService open = new();
        SoldierTransferService restricted = new([Doctrine.Techmarine.SpecialistType]);

        Assert.Contains(open.GetTransferOptions(chapter, techmarine),
            option => option.SoldierTemplate == Doctrine.MasterOfTheForge);
        Assert.True(open.HasLegalTransferOption(open.CreateContext(chapter), techmarine, promotionOnly: true));
        // The muster asks this cheaper question; it must agree with the option list.
        Assert.Empty(restricted.GetTransferOptions(chapter, techmarine));
        Assert.False(restricted.HasLegalTransferOption(
            restricted.CreateContext(chapter), techmarine, promotionOnly: false));
    }

    [Fact]
    public void ThePromotionService_SeatsOneMaster()
    {
        Chapter(out Squad armory, out _);
        PlayerSoldier first = Brother(Doctrine.Techmarine, tech: 120f, lead: 80f);
        PlayerSoldier second = Brother(Doctrine.Techmarine, tech: 120f, lead: 80f);
        armory.AddSquadMember(first);
        armory.AddSquadMember(second);
        ArmoryPromotionService promotions = new(Doctrine.Techmarine);

        promotions.Promote(first, armory, Doctrine.MasterOfTheForge, new Date(42, 1, 1));

        Assert.Same(first, promotions.FindSeatedLeader(armory));
        Assert.Equal(ArmoryPromotionReasonCode.NoOpenPlace,
            promotions.Evaluate(second, armory, Doctrine.MasterOfTheForge).ReasonCode);
        Assert.Equal(ArmoryPromotionReasonCode.NotAPromotion,
            promotions.Evaluate(first, armory, Doctrine.MasterOfTheForge).ReasonCode);
    }

    // The Armory's ranks come from its squad template, in rank order, and a brother is offered
    // only the ranks above his own.
    [Fact]
    public void ThePromotionTargets_AreTheArmorysRanksAboveTheBrother()
    {
        Chapter(out Squad armory, out _);
        PlayerSoldier techmarine = Brother(Doctrine.Techmarine, tech: 120f, lead: 80f);
        armory.AddSquadMember(techmarine);
        ArmoryPromotionService promotions = new(Doctrine.Techmarine);

        var ranks = promotions.PromotionTargets(armory);
        var above = promotions.PromotionTargets(armory, techmarine);

        Assert.Equal(3, ranks.Count);
        Assert.Same(Doctrine.MasterOfTheForge, ranks[0]);
        Assert.Same(Doctrine.Techmarine, ranks[2]);
        Assert.Equal([ranks[0], ranks[1]], above);
        Assert.Equal(ArmoryPromotionReasonCode.NotAnArmoryRole,
            promotions.Evaluate(techmarine, armory, Doctrine.Captain).ReasonCode);
    }

    private Unit Chapter(out Squad armory, out Squad tactical)
    {
        Unit chapter = new("Chapter", new UnitTemplate(100, "Chapter", true, [], []));
        armory = new Squad("Armory", chapter, Doctrine.Armory);
        chapter.AddSquad(armory);
        tactical = new Squad("First Tactical", chapter, Doctrine.TacticalSquad);
        chapter.AddSquad(tactical);
        return chapter;
    }

    private static PlayerSoldier Brother(SoldierTemplate template, float tech = 90f, float lead = 50f)
    {
        string name = $"Brother {template.Name}";
        PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(template, name), name);
        soldier.AddEvaluation(new SoldierEvaluation(
            new Date(42, 1, 1), melee: 50, ranged: 50, lead: lead, med: 50, tech: tech, piety: 50, ancient: 50));
        return soldier;
    }
}
