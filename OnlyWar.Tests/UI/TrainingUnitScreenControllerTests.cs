using OnlyWar.Application;
using OnlyWar.Helpers.UI;
﻿using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

public class TrainingUnitScreenControllerTests
{
    [Fact]
    public void IsDoctrineValid_AcceptsConfiguredRecruitmentThresholds()
    {
        RecruitmentDoctrineDraft doctrine = new(
            RecruitmentPolicy.VoluntaryPresentation,
            0,
            1,
            -1,
            2,
            0,
            0.9f);

        Assert.True(CampaignApplication.IsDoctrineValid(doctrine));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(7)]
    public void IsDoctrineValid_RejectsAttributeFilterOutsideRulesRange(int halfSteps)
    {
        RecruitmentDoctrineDraft doctrine = new(
            RecruitmentPolicy.PlanetaryTithe,
            halfSteps,
            0,
            0,
            0,
            0,
            0.9f);

        Assert.False(CampaignApplication.IsDoctrineValid(doctrine));
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    public void IsDoctrineValid_RejectsGeneticThresholdOutsideUnitInterval(float threshold)
    {
        RecruitmentDoctrineDraft doctrine = new(
            RecruitmentPolicy.PlanetaryTithe,
            0,
            0,
            0,
            0,
            0,
            threshold);

        Assert.False(CampaignApplication.IsDoctrineValid(doctrine));
    }

    [Fact]
    public void IsTrainingSquad_ExcludesScoutCompanyHq()
    {
        SquadTemplate trainingTemplate = CreateScoutTemplate(101, "Scout Squad");
        SquadTemplate hqTemplate = CreateScoutTemplate(
            102,
            "Scout Company HQ",
            SquadTypes.Scout | SquadTypes.HQ);

        Assert.True(CampaignApplication.IsTrainingSquad(
            new Squad(1, "Alpha Scouts", null, trainingTemplate)));
        Assert.False(CampaignApplication.IsTrainingSquad(
            new Squad(2, "10th Company HQ", null, hqTemplate)));
    }

    [Fact]
    public void IsTrainingSquad_ExcludesAdministrativeScoutFormation()
    {
        Squad squad = new(
            1,
            "Recruitment Staff",
            null,
            CreateScoutTemplate(101, "Scout Staff", SquadTypes.Scout | SquadTypes.Administrative));

        Assert.False(CampaignApplication.IsTrainingSquad(squad));
    }

    [Fact]
    public void OrderScoutSquads_SortsByConfiguredTypeThenAlphabetically()
    {
        SquadTemplate scoutTemplate = CreateScoutTemplate(101, "Scout Squad");
        SquadTemplate neophyteTemplate = CreateScoutTemplate(102, "Neophyte Squad");
        UnitTemplate companyTemplate = new(
            10,
            "Scout Company",
            false,
            new List<SquadTemplate> { scoutTemplate, neophyteTemplate },
            []);
        Unit company = new(10, "10th Company", companyTemplate, []);

        Squad alpha = AddSquad(company, 2, "Alpha Scouts", scoutTemplate);
        Squad beta = AddSquad(company, 3, "Beta Scouts", scoutTemplate);
        Squad neophyte = AddSquad(company, 4, "Aquila Neophytes", neophyteTemplate);

        List<Squad> ordered = CampaignApplication
            .OrderScoutSquads([neophyte, beta, alpha])
            .ToList();

        Assert.Equal(
            ["Alpha Scouts", "Beta Scouts", "Aquila Neophytes"],
            ordered.Select(squad => squad.Name));
    }

    [Fact]
    public void GetSquadListLabel_UnassignedSquad_ShowsTrainingOption()
    {
        Squad squad = new(1, "Alpha Scouts", null, CreateScoutTemplate(101, "Scout Squad"))
        {
            TrainingOptionKey = ScoutTrainingOptionKeys.Melee
        };

        Assert.Equal("Alpha Scouts (Melee)", CampaignApplication.DescribeSquadListLabel(squad));
    }

    [Fact]
    public void GetSquadListLabel_SquadAssignedToMission_ShowsOnMission()
    {
        Squad squad = new(1, "Alpha Scouts", null, CreateScoutTemplate(101, "Scout Squad"))
        {
            TrainingOptionKey = ScoutTrainingOptionKeys.Melee
        };
        Mission mission = new(MissionType.Patrol, null, 1);
        _ = new Order([squad], false, true, Aggression.Normal, mission);

        Assert.Equal("Alpha Scouts (On Mission)", CampaignApplication.DescribeSquadListLabel(squad));
    }

    private static SquadTemplate CreateScoutTemplate(
        int id,
        string name,
        SquadTypes type = SquadTypes.Scout)
    {
        return new SquadTemplate(
            id,
            name,
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 10)],
            type);
    }

    private static Squad AddSquad(Unit company, int id, string name, SquadTemplate template)
    {
        Squad squad = new(id, name, company, template);
        company.AddSquad(squad);
        return squad;
    }
}
