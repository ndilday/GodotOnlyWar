using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class RoleSuitabilityServiceTests
{
    private static readonly Date EvalDate = new(39, 500, 1);

    private static PlayerSoldier CreateSoldier(float melee = 0, float ranged = 0, float lead = 0,
                                               float med = 0, float tech = 0, float piety = 0,
                                               float ancient = 0, float psychic = 0,
                                               string name = "Brother Test")
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: name);
        soldier.PsychicPower = psychic;
        PlayerSoldier playerSoldier = new(soldier, name);
        playerSoldier.AddEvaluation(new SoldierEvaluation(EvalDate, melee, ranged, lead,
            med, tech, piety, ancient));
        return playerSoldier;
    }

    private static List<PlayerSoldier> Candidates(FoundingRole role, params PlayerSoldier[] soldiers)
    {
        return new RoleSuitabilityService(soldiers).CreateCandidateList(role);
    }

    [Fact]
    public void Psykers_AppearInNoRoleList()
    {
        // A psyker with top-tier ratings for every role still belongs to the Librarius
        // and nothing else; every role list must omit him.
        PlayerSoldier psyker = CreateSoldier(melee: 120, ranged: 125, lead: 99, med: 120,
            tech: 110, piety: 110, ancient: 120, psychic: 5);
        RoleSuitabilityService service = new(new[] { psyker });

        foreach (FoundingRole role in System.Enum.GetValues<FoundingRole>())
        {
            Assert.Empty(service.CreateCandidateList(role));
        }
    }

    [Theory]
    [InlineData(75f, false)]
    [InlineData(76f, true)]
    public void Techmarine_RequiresTechAboveThreshold(float techRating, bool eligible)
    {
        PlayerSoldier soldier = CreateSoldier(tech: techRating);
        Assert.Equal(eligible, Candidates(FoundingRole.Techmarine, soldier).Contains(soldier));
    }

    [Fact]
    public void MasterOfTheForge_RequiresTechAndLeadership()
    {
        PlayerSoldier techOnly = CreateSoldier(tech: 110, lead: 60);
        PlayerSoldier leaderOnly = CreateSoldier(tech: 100, lead: 80);
        PlayerSoldier both = CreateSoldier(tech: 101, lead: 61);

        List<PlayerSoldier> masters =
            Candidates(FoundingRole.MasterOfTheForge, techOnly, leaderOnly, both);

        Assert.Equal(new[] { both }, masters);
    }

    [Fact]
    public void ChaplainAndMasterOfSanctity_GateOnPietyTiers()
    {
        PlayerSoldier devout = CreateSoldier(piety: 95, lead: 70);
        PlayerSoldier saintly = CreateSoldier(piety: 105, lead: 70);
        PlayerSoldier saintlyFollower = CreateSoldier(piety: 110, lead: 50);
        PlayerSoldier faithless = CreateSoldier(piety: 90, lead: 90);

        List<PlayerSoldier> chaplains =
            Candidates(FoundingRole.Chaplain, devout, saintly, saintlyFollower, faithless);
        List<PlayerSoldier> masters =
            Candidates(FoundingRole.MasterOfSanctity, devout, saintly, saintlyFollower, faithless);

        // Chaplains: piety > 90, sorted most-pious-first.
        Assert.Equal(new[] { saintlyFollower, saintly, devout }, chaplains);
        // Master of Sanctity: piety > 100 and leadership > 60.
        Assert.Equal(new[] { saintly }, masters);
    }

    [Fact]
    public void VeteranRoles_SplitOnLeadershipAndRequireCombatSpike()
    {
        // Baseline (melee > 90, ranged > 105) + spike (melee > 115 or ranged > 120).
        PlayerSoldier meleeSpikeFollower = CreateSoldier(melee: 116, ranged: 106, lead: 60);
        PlayerSoldier rangedSpikeLeader = CreateSoldier(melee: 91, ranged: 121, lead: 61);
        PlayerSoldier baselineNoSpike = CreateSoldier(melee: 110, ranged: 110, lead: 70);
        PlayerSoldier spikeNoBaseline = CreateSoldier(melee: 116, ranged: 100, lead: 70);

        List<PlayerSoldier> veterans = Candidates(FoundingRole.Veteran,
            meleeSpikeFollower, rangedSpikeLeader, baselineNoSpike, spikeNoBaseline);
        List<PlayerSoldier> sergeants = Candidates(FoundingRole.VeteranSergeant,
            meleeSpikeFollower, rangedSpikeLeader, baselineNoSpike, spikeNoBaseline);

        // Leadership 60 stays rank-and-file; 61 ranks as a sergeant instead — the two
        // lists partition the veteran candidates.
        Assert.Equal(new[] { meleeSpikeFollower }, veterans);
        Assert.Equal(new[] { rangedSpikeLeader }, sergeants);
    }

    [Fact]
    public void VeteranCaptain_RequiresLeadershipAndBothCombatRatings()
    {
        PlayerSoldier qualified = CreateSoldier(melee: 106, ranged: 111, lead: 76);
        PlayerSoldier weakLeader = CreateSoldier(melee: 106, ranged: 111, lead: 75);
        PlayerSoldier weakRanged = CreateSoldier(melee: 106, ranged: 110, lead: 80);

        List<PlayerSoldier> captains =
            Candidates(FoundingRole.VeteranCaptain, qualified, weakLeader, weakRanged);

        Assert.Equal(new[] { qualified }, captains);
    }

    [Fact]
    public void LineRoles_PartitionByCombatProfileAndLeadership()
    {
        PlayerSoldier tactical = CreateSoldier(melee: 95, ranged: 110, lead: 40);
        PlayerSoldier tacticalSergeant = CreateSoldier(melee: 95, ranged: 110, lead: 55);
        PlayerSoldier assault = CreateSoldier(melee: 95, ranged: 100, lead: 40);
        PlayerSoldier devastator = CreateSoldier(melee: 85, ranged: 100, lead: 40);
        // Leadership of exactly 50 is in neither the member (< 50) nor sergeant (> 50)
        // list, matching the original filters.
        PlayerSoldier borderline = CreateSoldier(melee: 95, ranged: 110, lead: 50);
        PlayerSoldier[] all = { tactical, tacticalSergeant, assault, devastator, borderline };

        Assert.Equal(new[] { tactical }, Candidates(FoundingRole.TacticalMarine, all));
        Assert.Equal(new[] { tacticalSergeant }, Candidates(FoundingRole.TacticalSergeant, all));
        Assert.Equal(new[] { assault }, Candidates(FoundingRole.AssaultMarine, all));
        Assert.Equal(new[] { devastator }, Candidates(FoundingRole.DevastatorMarine, all));
    }

    [Fact]
    public void Captain_RanksEveryoneByLeadership()
    {
        PlayerSoldier middling = CreateSoldier(lead: 50, name: "Middling");
        PlayerSoldier best = CreateSoldier(lead: 90, name: "Best");
        PlayerSoldier worst = CreateSoldier(lead: 10, name: "Worst");

        List<PlayerSoldier> captains = Candidates(FoundingRole.Captain, middling, best, worst);

        Assert.Equal(new[] { best, middling, worst }, captains);
    }

    [Fact]
    public void CreateCandidateList_ReturnsIndependentCopies()
    {
        PlayerSoldier soldier = CreateSoldier(lead: 60);
        RoleSuitabilityService service = new(new[] { soldier });

        List<PlayerSoldier> first = service.CreateCandidateList(FoundingRole.Captain);
        first.Clear();

        Assert.Single(service.CreateCandidateList(FoundingRole.Captain));
    }
}
