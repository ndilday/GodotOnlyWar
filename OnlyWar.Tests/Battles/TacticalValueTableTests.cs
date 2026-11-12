using System.Collections.Generic;
using System.Linq;

using OnlyWar.Battles;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Squads;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 1 of the strategic/tactical value split (Design/Active/StrategicAndTacticalValue.md): the
/// tactical value is computed from the battle state, with no database value in it.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class TacticalValueTableTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const int TacticalSquadTemplateId = 1;
    private const int InsurrectionistMobTemplateId = 36;
    private const int ShootaBoyzTemplateId = 39;
    private const int SluggaBoyzTemplateId = 40;
    private const int NeophyteSquadTemplateId = 26;
    private const int PdfSquadTemplateId = 34;
    private const int HormagauntSquadTemplateId = 17;

    [Fact]
    public void AMarine_IsWorthManyMobModels()
    {
        // The headcount-parity failure (before 2026-10-12) priced a marine as one Mob model.
        Forces forces = Build([TacticalSquadTemplateId], Repeat(InsurrectionistMobTemplateId, 4), 100);

        TacticalValueTable table = TacticalValueTable.Build(forces.First, forces.Second);

        float ratio = MeanValue(table, forces.First) / MeanValue(table, forces.Second);
        output.WriteLine(
            $"marine {MeanValue(table, forces.First):0.000}, Mob model {MeanValue(table, forces.Second):0.000}, "
            + $"ratio {ratio:0.0}, {table.RateEvaluations} rate evaluations");
        Assert.True(ratio >= 5f,$"a marine was worth {ratio:0.0} Mob models");
    }

    [Fact]
    public void TheMarineToMobRatio_DoesNotDependOnHowManyMobsThereAre()
    {
        Forces few = Build([TacticalSquadTemplateId], Repeat(InsurrectionistMobTemplateId, 1), 100);
        Forces many = Build([TacticalSquadTemplateId], Repeat(InsurrectionistMobTemplateId, 6), 100);

        TacticalValueTable fewTable = TacticalValueTable.Build(few.First, few.Second);
        TacticalValueTable manyTable = TacticalValueTable.Build(many.First, many.Second);

        float fewRatio = MeanValue(fewTable, few.First) / MeanValue(fewTable, few.Second);
        float manyRatio = MeanValue(manyTable, many.First) / MeanValue(manyTable, many.Second);
        Assert.InRange(manyRatio / fewRatio, 0.8f, 1.25f);
    }

    [Fact]
    public void ANob_OutvaluesABoy()
    {
        Forces forces = Build([SluggaBoyzTemplateId], [TacticalSquadTemplateId], 50);

        TacticalValueTable table = TacticalValueTable.Build(forces.First, forces.Second);

        BattleSquad boyz = forces.First.Single();
        List<BattleSoldier> nobs = boyz.AbleSoldiers
            .Where(soldier => soldier.Soldier.Template.Name.Contains("Nob"))
            .ToList();
        List<BattleSoldier> boys = boyz.AbleSoldiers.Except(nobs).ToList();
        Assert.NotEmpty(nobs);
        float nob = nobs.Average(soldier => table.ValueOf(soldier.Soldier.Id));
        float boy = boys.Average(soldier => table.ValueOf(soldier.Soldier.Id));
        Assert.True(nob > boy, $"Nob {nob:0.000} against Boy {boy:0.000}");
    }

    [Fact]
    public void TheSameTemplate_IsValuedByItsArmament()
    {
        // Shoota and Slugga Boyz share the Ork Boy template. The gun's offense falls with range
        // (down to the Shoota's own choppa, so only a little against marines); the choppa's,
        // priced at contact, does not.
        Forces near = Build([ShootaBoyzTemplateId, SluggaBoyzTemplateId], [TacticalSquadTemplateId], 20);
        Forces far = Build([ShootaBoyzTemplateId, SluggaBoyzTemplateId], [TacticalSquadTemplateId], 400);

        TacticalValueTable nearTable = TacticalValueTable.Build(near.First, near.Second);
        TacticalValueTable farTable = TacticalValueTable.Build(far.First, far.Second);

        float shootaNear = MeanOffense(nearTable, near.First[0]);
        float shootaFar = MeanOffense(farTable, far.First[0]);
        float sluggaNear = MeanOffense(nearTable, near.First[1]);
        float sluggaFar = MeanOffense(farTable, far.First[1]);
        Assert.True(shootaFar < shootaNear * 0.95f,$"Shoota offense {shootaNear:0.0000} near, {shootaFar:0.0000} far");
        Assert.InRange(sluggaFar / sluggaNear, 0.95f, 1.05f);
    }

    [Fact]
    public void SluggaBoyz_LeanToMeleeAgainstNeophytes()
    {
        // The Neophyte-Slugga stalemate (GauntletV2.md): every Ork Boy template read
        // MeleeFraction 0.5, so Slugga Boyz never sought contact. Measured instead, Ork guns
        // remove almost nothing across their reach, so Shoota Boyz (who carry choppas too) lean
        // to melee as well: 0.98 and 0.99 on 2026-11-01.
        Forces forces = Build([SluggaBoyzTemplateId, ShootaBoyzTemplateId], [NeophyteSquadTemplateId], 300);

        TacticalValueTable table = TacticalValueTable.Build(forces.First, forces.Second);

        float slugga = MeanMeleeShare(table, forces.First[0]);
        float shoota = MeanMeleeShare(table, forces.First[1]);
        output.WriteLine($"melee share: Slugga {slugga:0.00}, Shoota {shoota:0.00}");
        Assert.True(slugga >= BattleSquadCapabilityProfile.ContactSeekingMeleeFraction, $"Slugga {slugga:0.00}");
    }

    [Fact]
    public void AGunnerWithNoMeleeWeapon_HasNoMeleeShare()
    {
        Forces forces = Build([PdfSquadTemplateId, TacticalSquadTemplateId], [HormagauntSquadTemplateId], 150);

        TacticalValueTable table = TacticalValueTable.Build(forces.First, forces.Second);

        output.WriteLine(
            $"melee share: PDF {MeanMeleeShare(table, forces.First[0]):0.00}, "
            + $"Tactical {MeanMeleeShare(table, forces.First[1]):0.00}, "
            + $"Hormagaunts {MeanMeleeShare(table, forces.Second[0]):0.00}");
        Assert.True(MeanMeleeShare(table, forces.First[0]) < BattleSquadCapabilityProfile.FireSupportMeleeFraction);
        // Tactical marines stand against Hormagaunts (StrategicAndTacticalValue.md, phase 3 check).
        Assert.True(MeanMeleeShare(table, forces.First[1]) < BattleSquadCapabilityProfile.ContactSeekingMeleeFraction);
        Assert.True(MeanMeleeShare(table, forces.Second[0]) >= BattleSquadCapabilityProfile.ContactSeekingMeleeFraction);
    }

    [Fact]
    public void PairTerms_DependOnTheTarget()
    {
        Forces forces = Build(
            [SluggaBoyzTemplateId],
            [TacticalSquadTemplateId, InsurrectionistMobTemplateId],
            50);

        TacticalValueTable table = TacticalValueTable.Build(forces.First, forces.Second);

        BattleSoldier boy = forces.First[0].AbleSoldiers.First(
            soldier => !soldier.Soldier.Template.Name.Contains("Nob"));
        PairTacticalValue againstMarines = table.PairOf(boy.Soldier.Id, forces.Second[0].Id);
        PairTacticalValue againstMob = table.PairOf(boy.Soldier.Id, forces.Second[1].Id);
        Assert.True(againstMob.Offense > againstMarines.Offense);
        Assert.True(againstMob.Durability > againstMarines.Durability);
    }

    [Fact]
    public void ThePreviousTurnsValues_SettleRatherThanSwing()
    {
        Forces forces = Build([TacticalSquadTemplateId], Repeat(InsurrectionistMobTemplateId, 3), 100);

        TacticalValueTable first = TacticalValueTable.Build(forces.First, forces.Second);
        TacticalValueTable second = TacticalValueTable.Build(forces.First, forces.Second, first);
        TacticalValueTable third = TacticalValueTable.Build(forces.First, forces.Second, second);

        float Ratio(TacticalValueTable table) =>
            MeanValue(table, forces.First) / MeanValue(table, forces.Second);
        Assert.InRange(Ratio(third) / Ratio(second), 0.9f, 1.1f);
    }

    private static float MeanValue(TacticalValueTable table, IEnumerable<BattleSquad> squads) =>
        squads.SelectMany(squad => squad.AbleSoldiers)
            .Average(soldier => table.ValueOf(soldier.Soldier.Id));

    private static float MeanOffense(TacticalValueTable table, BattleSquad squad) =>
        squad.AbleSoldiers.Average(soldier =>
            table.TryGet(soldier.Soldier.Id, out SoldierTacticalValue value) ? value.Offense : 0f);

    private static float MeanMeleeShare(TacticalValueTable table, BattleSquad squad) =>
        squad.AbleSoldiers.Average(soldier =>
            table.TryGet(soldier.Soldier.Id, out SoldierTacticalValue value) ? value.MeleeShare : 0f);

    private static int[] Repeat(int templateId, int count) =>
        Enumerable.Repeat(templateId, count).ToArray();

    private sealed record Forces(List<BattleSquad> First, List<BattleSquad> Second);

    /// <summary>
    /// Two forces facing each other across <paramref name="separation"/> yards: each squad in a
    /// column of its own, its soldiers in a line.
    /// </summary>
    private static Forces Build(int[] firstTemplateIds, int[] secondTemplateIds, int separation)
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        RNG.Reset(4243);
        OnlyWar.Abstractions.IEntityIdAllocator ids =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        List<BattleSquad> Side(int[] templateIds, int x, string name)
        {
            List<BattleSquad> squads = [];
            for (int index = 0; index < templateIds.Length; index++)
            {
                BattleSquad squad = new(false, SquadFactory.GenerateSquad(
                    blob.Factions
                        .Where(faction => faction.SquadTemplates.ContainsKey(templateIds[index]))
                        .Select(faction => faction.SquadTemplates[templateIds[index]])
                        .First(),
                    new StaticRNG(), ids, $"{name}{index}"));
                int y = index * 40;
                foreach (BattleSoldier soldier in squad.AbleSoldiers)
                {
                    soldier.TopLeft = (x, y++);
                }
                squads.Add(squad);
            }
            return squads;
        }
        return new Forces(Side(firstTemplateIds, 0, "First"), Side(secondTemplateIds, separation, "Second"));
    }
}
