using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

public sealed class ChapterMusterViewModelBuilderTests
{
    [Fact]
    public void BuildCandidates_OrdersHonorsByTypeInsteadOfAwardHistory()
    {
        Unit unit = new("Test Chapter", new UnitTemplate(
            1,
            "Test Chapter",
            true,
            new List<SquadTemplate> { TestModelFactory.SquadTemplate },
            new List<UnitTemplate>()));
        Squad source = new("Source", null, TestModelFactory.SquadTemplate);
        Squad target = new("Target", null, TestModelFactory.SquadTemplate);
        unit.AddSquad(source);
        unit.AddSquad(target);

        PlayerSoldier candidate = new(
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Brother Candidate"),
            "Brother Candidate");
        PlayerSoldier leader = new(
            TestModelFactory.CreateSoldier(TestModelFactory.SergeantTemplate, "Sergeant Target"),
            "Sergeant Target");
        source.AddSquadMember(candidate);
        target.AddSquadMember(leader);

        Date date = new(41, 999, 1);
        candidate.AddAward(new SoldierAward(date, "Bronze Banner", AwardTypes.Banner, 1));
        candidate.AddAward(new SoldierAward(date, "Gold Voice", AwardTypes.Voice, 3));
        candidate.AddAward(new SoldierAward(date, "Silver Sword", AwardTypes.Sword, 2));
        candidate.AddAward(new SoldierAward(date, "Bronze Gun", AwardTypes.Gun, 1));

        Army army = new("Test Army", null, "Test Chapter", unit, [candidate, leader]);
        PlayerForce force = new(null, army, null);

        MusterCandidateViewModel result = Assert.Single(
            new ChapterMusterViewModelBuilder()
                .BuildCandidates(force, new MusterPlanService(), MusterPopulationMode.AnyLegalMove),
            viewModel => viewModel.SoldierId == candidate.Id);

        Assert.Equal(
            [AwardTypes.Gun, AwardTypes.Sword, AwardTypes.Voice, AwardTypes.Banner],
            result.Honors.Select(honor => honor.Type));
    }

    [Fact]
    public void BuildFormations_HidesNewCompanySquadsUntilHqHasLeader()
    {
        SquadTemplate headquartersTemplate = CreateSquadTemplate(
            "Company HQ",
            SquadTypes.HQ,
            (TestModelFactory.CaptainTemplate, 1, 1));
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Tactical Squad",
            SquadTypes.None,
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit company = new(
            "6th Company",
            new UnitTemplate(
                2,
                "Company Template",
                true,
                headquartersTemplate,
                [new SquadTemplateSlot(lineTemplate, 0, 2)]));
        Squad source = new("Source Squad", company, lineTemplate);
        company.AddSquad(source);
        PlayerSoldier candidate = new(
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Brother Candidate"),
            "Brother Candidate");
        source.AddSquadMember(candidate);
        PlayerForce force = new(
            null,
            new Army("Test Army", null, "Test Chapter", company, [candidate]),
            null);
        ChapterMusterViewModelBuilder builder = new();

        IReadOnlyList<FormationVacancyViewModel> beforeFounding = builder.BuildFormations(
            force, candidate, new MusterPlanService());

        Assert.Contains(beforeFounding, row =>
            row.Group == FormationVacancyGroup.EmptyFormations
            && row.FormationName == "6th Company HQ Squad");
        Assert.DoesNotContain(beforeFounding, row =>
            row.Group == FormationVacancyGroup.AvailableNewFormations);

        company.HQSquad.AddSquadMember(
            TestModelFactory.CreateSoldier(TestModelFactory.CaptainTemplate, "Captain Aurelius"));

        Assert.Contains(builder.BuildFormations(force, candidate, new MusterPlanService()), row =>
            row.Group == FormationVacancyGroup.AvailableNewFormations);
    }

    [Fact]
    public void BuildFormations_ShowsEveryStagedNewFormationAsUnderstrength()
    {
        SquadTemplate headquartersTemplate = CreateSquadTemplate(
            "Company HQ",
            SquadTypes.HQ,
            (TestModelFactory.CaptainTemplate, 1, 1));
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Tactical Squad",
            SquadTypes.None,
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 4));
        Unit company = new(
            "3rd Company",
            new UnitTemplate(
                3,
                "Company Template",
                true,
                headquartersTemplate,
                [new SquadTemplateSlot(lineTemplate, 0, 10)]));
        company.HQSquad.AddSquadMember(
            TestModelFactory.CreateSoldier(TestModelFactory.CaptainTemplate, "Captain Aurelius"));

        List<PlayerSoldier> candidates = [];
        for (int index = 0; index < 6; index++)
        {
            Squad source = new($"Source {index + 1}", company, lineTemplate);
            company.AddSquad(source);
            PlayerSoldier soldier = new(
                TestModelFactory.CreateSoldier(
                    TestModelFactory.MarineTemplate, $"Brother {index + 1}"),
                $"Brother {index + 1}");
            source.AddSquadMember(soldier);
            candidates.Add(soldier);
        }

        PlayerForce force = new(
            null,
            new Army("Test Army", null, "Test Chapter", company, candidates),
            null);
        MusterPlanService plan = new();
        SoldierTransferContext context = SoldierTransferContext.Build(company);
        foreach (PlayerSoldier soldier in candidates.Take(5))
        {
            SoldierTransferOption option = Assert.Single(
                new SoldierTransferService().GetTransferOptions(context, soldier),
                transfer => transfer.IsNewSquad);
            plan.Stage(soldier, option);
        }

        IReadOnlyList<FormationVacancyViewModel> rows =
            new ChapterMusterViewModelBuilder().BuildFormations(
                force, candidates[5], plan, context);
        IReadOnlyList<FormationVacancyViewModel> staged = rows
            .Where(row => row.IsPlanProjection)
            .ToList();

        Assert.Equal(5, staged.Count);
        Assert.All(staged, row =>
        {
            Assert.Equal(FormationVacancyGroup.Understrength, row.Group);
            Assert.Equal("0 +1 / 5", row.RosterText);
            Assert.NotNull(row.Option);
            Assert.True(row.Option.IsProvisionalSquad);
            Assert.NotNull(row.SelectionKey);
        });
        Assert.Equal(5, staged.Select(row => row.FormationOrdinal).Distinct().Count());
        Assert.Equal(5, staged.Select(row => row.SelectionKey).Distinct().Count());

        FormationVacancyViewModel provisionalDestination = staged.First();
        plan.Stage(candidates[5], provisionalDestination.Option);
        Assert.True(plan.Validate(force, context).IsValid);
        Assert.Contains(
            new ChapterMusterViewModelBuilder().BuildFormations(force, candidates[5], plan, context),
            row => row.IsPlanProjection && row.RosterText == "0 +2 / 5");

        MusterCommitResult commit = plan.Commit(force, new Date(41, 999, 1));
        Assert.True(commit.Succeeded);
        Assert.Contains(company.Squads, squad => squad.Members.Count == 2);
    }

    [Fact]
    public void BuildFormations_MarksProvisionalFormationFullAfterStagedRosterReachesCapacity()
    {
        SquadTemplate headquartersTemplate = CreateSquadTemplate(
            "Company HQ",
            SquadTypes.HQ,
            (TestModelFactory.CaptainTemplate, 1, 1));
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Tactical Squad",
            SquadTypes.None,
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 1));
        Unit company = new(
            "3rd Company",
            new UnitTemplate(
                3,
                "Company Template",
                true,
                headquartersTemplate,
                [new SquadTemplateSlot(lineTemplate, 0, 6)]));
        company.HQSquad.AddSquadMember(
            TestModelFactory.CreateSoldier(TestModelFactory.CaptainTemplate, "Captain Aurelius"));

        List<PlayerSoldier> candidates = [];
        for (int index = 0; index < 3; index++)
        {
            Squad source = new($"Source {index + 1}", company, lineTemplate);
            company.AddSquad(source);
            PlayerSoldier soldier = new(
                TestModelFactory.CreateSoldier(
                    TestModelFactory.MarineTemplate, $"Brother {index + 1}"),
                $"Brother {index + 1}");
            source.AddSquadMember(soldier);
            candidates.Add(soldier);
        }

        PlayerForce force = new(
            null,
            new Army("Test Army", null, "Test Chapter", company, candidates),
            null);
        SoldierTransferService transfers = new();
        SoldierTransferContext context = SoldierTransferContext.Build(company);
        MusterPlanService plan = new();
        plan.Stage(
            candidates[0],
            Assert.Single(
                transfers.GetTransferOptions(context, candidates[0]),
                option => option.IsNewSquad));

        FormationVacancyViewModel provisional = Assert.Single(
            new ChapterMusterViewModelBuilder().BuildFormations(
                force, candidates[1], plan, context),
            row => row.IsPlanProjection);
        plan.Stage(candidates[1], provisional.Option);

        FormationVacancyViewModel full = Assert.Single(
            new ChapterMusterViewModelBuilder().BuildFormations(
                force, candidates[2], plan, context),
            row => row.IsPlanProjection);
        Assert.True(full.IsFull);
        Assert.Null(full.Option);
        Assert.Equal("0 +2 / 2", full.RosterText);
        Assert.Equal(FormationVacancyGroup.AtStrength, full.Group);
        Assert.Equal("AT STRENGTH", full.StateLabel);
    }

    [Fact]
    public void BuildFormations_StopsCallingALiveSquadUnderstrengthOnceStagingFillsIt()
    {
        SquadTemplate headquartersTemplate = CreateSquadTemplate(
            "Company HQ",
            SquadTypes.HQ,
            (TestModelFactory.CaptainTemplate, 1, 1));
        SquadTemplate lineTemplate = CreateSquadTemplate(
            "Tactical Squad",
            SquadTypes.None,
            (TestModelFactory.SergeantTemplate, 0, 1),
            (TestModelFactory.MarineTemplate, 0, 3));
        Unit company = new(
            "2nd Company",
            new UnitTemplate(
                2,
                "Company Template",
                true,
                headquartersTemplate,
                [new SquadTemplateSlot(lineTemplate, 0, 6)]));
        company.HQSquad.AddSquadMember(
            TestModelFactory.CreateSoldier(TestModelFactory.CaptainTemplate, "Captain Aurelius"));

        // The destination is one short of capacity, so a single staged transfer fills it.
        Squad destination = new("Destination", company, lineTemplate);
        company.AddSquad(destination);
        List<PlayerSoldier> roster = [];
        PlayerSoldier sergeant = new(
            TestModelFactory.CreateSoldier(TestModelFactory.SergeantTemplate, "Sergeant Kaeso"),
            "Sergeant Kaeso");
        destination.AddSquadMember(sergeant);
        roster.Add(sergeant);
        for (int index = 0; index < 2; index++)
        {
            PlayerSoldier held = new(
                TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, $"Held {index + 1}"),
                $"Held {index + 1}");
            destination.AddSquadMember(held);
            roster.Add(held);
        }

        List<PlayerSoldier> candidates = [];
        for (int index = 0; index < 2; index++)
        {
            Squad source = new($"Source {index + 1}", company, lineTemplate);
            company.AddSquad(source);
            PlayerSoldier soldier = new(
                TestModelFactory.CreateSoldier(
                    TestModelFactory.MarineTemplate, $"Brother {index + 1}"),
                $"Brother {index + 1}");
            source.AddSquadMember(soldier);
            candidates.Add(soldier);
            roster.Add(soldier);
        }

        PlayerForce force = new(
            null, new Army("Test Army", null, "Test Chapter", company, roster), null);
        ChapterMusterViewModelBuilder builder = new();
        SoldierTransferContext context = SoldierTransferContext.Build(company);
        MusterPlanService plan = new();

        FormationVacancyViewModel before = Assert.Single(
            builder.BuildFormations(force, candidates[0], plan, context),
            row => row.FormationName == destination.Name);
        Assert.Equal(FormationVacancyGroup.Understrength, before.Group);
        Assert.False(before.IsFull);

        plan.Stage(candidates[0], before.Option);

        FormationVacancyViewModel after = Assert.Single(
            builder.BuildFormations(force, candidates[1], plan, context),
            row => row.FormationName == destination.Name);
        Assert.True(after.IsFull);
        Assert.Equal(FormationVacancyGroup.AtStrength, after.Group);
        Assert.Equal("AT STRENGTH", after.StateLabel);
        Assert.Equal("AT STRENGTH", after.GroupLabel);
        Assert.Equal("3 +1 / 4", after.RosterText);
    }

    private static SquadTemplate CreateSquadTemplate(
        string name,
        SquadTypes squadTypes,
        params (SoldierTemplate Template, byte Min, byte Max)[] elements)
    {
        return new SquadTemplate(
            10,
            name,
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            elements.Select(element => new SquadTemplateElement(
                element.Template, element.Min, element.Max)).ToList(),
            squadTypes);
    }
}
