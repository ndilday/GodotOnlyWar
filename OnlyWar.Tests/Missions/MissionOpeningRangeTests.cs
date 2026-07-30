using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

// The margin of a mission's controlling check decides WHOSE FIGHT the engagement is, not how close
// it is. Those are different questions, and only the first can be answered without knowing what the
// two forces are carrying.
//
// This matters most in the case ambush exists for. PerformAmbushMissionStep used to compute
// `70 - marginOfSuccess * 20`, floored at 20 - two constants that read neither force's weapons and
// always trended toward contact. Marines ambushing Tyranids do not want to be as close as possible;
// they want bolter range. Under the old formula a perfectly executed marine ambush was dragged to
// 20 yards, which is precisely the fight the gribblies wanted.
public class MissionOpeningRangeTests
{
    // A well-set ambush by a force that wants to shoot opens FAR. This is the exact inversion of the
    // old formula's behaviour, so it is the assertion that would have caught the bug.
    [Fact]
    public void Interpolate_WellSetRangedAmbush_OpensFartherThanABlownOne()
    {
        BattleSquad shooters = CreateRangedSquad();
        BattleSquad brawlers = CreateMeleeSquad();

        ushort wellSet = MissionOpeningRange.Interpolate(
            [shooters], [brawlers], 3.0f, new FixedRNG());
        ushort blown = MissionOpeningRange.Interpolate(
            [shooters], [brawlers], -3.0f, new FixedRNG());

        Assert.True(
            wellSet > blown,
            $"a well-set ranged ambush opened at {wellSet}, no farther than the {blown} a blown one "
            + "opened at - the margin is not pushing toward the ambusher's preference");
    }

    // ...and the same rule run the other way: a force that wants to be in contact is rewarded with
    // contact, not with distance. The rule is "toward the mission force's preference", not "far".
    [Fact]
    public void Interpolate_WellSetMeleeAmbush_OpensCloserThanABlownOne()
    {
        BattleSquad brawlers = CreateMeleeSquad();
        BattleSquad shooters = CreateRangedSquad();

        ushort wellSet = MissionOpeningRange.Interpolate(
            [brawlers], [shooters], 3.0f, new FixedRNG());
        ushort blown = MissionOpeningRange.Interpolate(
            [brawlers], [shooters], -3.0f, new FixedRNG());

        Assert.True(
            wellSet < blown,
            $"a well-set melee ambush opened at {wellSet}, no nearer than the {blown} a blown one "
            + "opened at - the margin is not pushing toward the ambusher's preference");
    }

    // Interpolation, never extrapolation: the worst a mission force can do is fight at exactly the
    // range its enemy wanted, and the best is its own preference. A formula that ran past either end
    // would let a lucky roll place a battle at a range neither side can use.
    [Fact]
    public void Interpolate_StaysBetweenTheTwoSidesPreferences()
    {
        BattleSquad shooters = CreateRangedSquad();
        BattleSquad brawlers = CreateMeleeSquad();
        int shooterPreference = shooters.GetPreferredOpeningRange(1, 0, 10);
        int brawlerPreference = brawlers.GetPreferredOpeningRange(1, 0, 10);

        Assert.True(
            shooterPreference > brawlerPreference,
            "fixture must give the two forces different preferences to interpolate between");

        foreach (float margin in new[] { -10f, -1f, 0f, 1f, 10f })
        {
            ushort range = MissionOpeningRange.Interpolate(
                [shooters], [brawlers], margin, new FixedRNG());
            Assert.InRange(range, brawlerPreference, shooterPreference);
        }
    }

    // --- fixtures ---

    // Carries the standard test rifle (100 yard maximum), so it has a real standoff preference.
    private static BattleSquad CreateRangedSquad() =>
        CreateSquad("Shooters", TestModelFactory.SquadTemplate);

    // Melee only, so CalculateOpeningDistance finds no ranged weapon to stand off with and the squad
    // prefers contact.
    private static BattleSquad CreateMeleeSquad() =>
        CreateSquad("Brawlers", MeleeSquadTemplate);

    private static readonly WeaponSet MeleeOnlyWeapons = new(
        99,
        "Test Melee Only",
        primaryMelee: TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon);

    private static readonly SquadTemplate MeleeSquadTemplate = new(
        99,
        "Test Melee Squad",
        MeleeOnlyWeapons,
        [],
        TestModelFactory.TestArmor,
        [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)],
        SquadTypes.None);

    private static BattleSquad CreateSquad(string name, SquadTemplate template)
    {
        Squad squad = new(name, null, template);
        for (int i = 0; i < 4; i++)
        {
            squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"{name} {i}"));
        }
        return new BattleSquad(false, squad);
    }
}
