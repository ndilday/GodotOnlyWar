using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
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
        // PHASE 7: each side's preference is now derived against the other side's whole force, so
        // the endpoints the interpolation must stay between are asked the same way Interpolate
        // asks them.
        int shooterPreference = shooters.GetPreferredOpeningRange([brawlers]);
        int brawlerPreference = brawlers.GetPreferredOpeningRange([shooters]);

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

    // PHASE 7 REGRESSION GUARD (Design/Active/EngagementScoringOverhaul.md).
    //
    // Phase 6 left opening range as the UN-OPPOSED saturation range - "where am I still half as
    // effective as at my best" - which for a force whose weapon outranges the fight runs all the way
    // out to weapon reach. It answered 1000 yards for bolter marines whose derived mid-fight band
    // against the same Tyranids was 173: ~140 turns of walking before the fight the squad actually
    // wants can begin. Phase 7 re-pointed it at the derived band by pricing the approach.
    //
    // SUPERSEDED IN PART (2026-08-05). Phase 7's re-pointing went too far: delegating opening range
    // to the mid-fight band made it a per-turn snapshot, whose every term decreases with range once
    // the enemy has no ranged weapons -- so a melee-only enemy drove the answer to CONTACT for any
    // loadout, and the Xibarrus Zeta ambush was sprung at one yard. CalculatePreferredOpeningRange
    // no longer delegates; it integrates the approach. See its comment for why the two questions
    // differ, and GradedRemovalCalibrationTests for the regression that pins the melee-only case.
    //
    // UPPER BOUND REBASELINED (2026-08-05), and it is a RELAXATION -- record it as one. It was a
    // flat `< 500`, chosen when SaturationFraction was 0.5 and "still half as effective as at my
    // best" sat a long way inside weapon reach. At 0.1 the useful band deliberately runs close to
    // reach, so 500 now encodes the old fraction rather than the property, and the quantity that
    // keeps opening range OFF weapon reach is no longer the fraction at all -- it is the
    // retreat-fire headroom in CalculatePreferredOpeningRange, which holds ten bounds of the
    // enemy's own speed back from the saturation floor.
    //
    // So the bound is stated in that quantity instead. It still brackets from BOTH sides and still
    // fails the Phase 6 defect this guard exists for, which answered weapon reach exactly. Measured
    // 2026-08-05: 694 against a 1000-yard reach.
    [Fact]
    public void PreferredOpeningRange_IsTheDerivedBandNotWeaponReach()
    {
        BattleSquad shooters = CreateRangedSquad();
        BattleSquad brawlers = CreateMeleeSquad();

        int opening = shooters.GetPreferredOpeningRange([brawlers]);
        int headroom = (int)(10 * brawlers.GetSquadMove());

        Assert.True(
            opening > 0,
            "a force that can hurt the enemy at range should still want some standoff, not contact");
        Assert.True(
            opening <= LongRifleReach - headroom,
            $"opening range came back {opening} against a {LongRifleReach}-yard reach, leaving less "
            + $"than the {headroom} yards of retreat-fire headroom the derivation reserves - that is "
            + "weapon reach, not the derived engagement band");
    }

    // --- fixtures ---

    private const int LongRifleReach = 1_000;

    // PHASE 7 FIXTURE RETUNE (Design/Active/EngagementScoringOverhaul.md), same precedent as the
    // Phase 6 retune it replaces and for the same underlying reason: the fixture's shooters have to
    // be able to shoot for "a force that wants to shoot" to mean anything.
    //
    // Phase 6 got there with Dexterity 20 alone, because opening range was then the UN-OPPOSED
    // saturation range and a poor weapon still scores a fraction of its own poor best. Phase 7
    // makes it the OPPOSED band -- argmax of removal(r) minus what the enemy does back -- and the
    // standard test rifle (accuracy 0, rate of fire 1, damage 5 degrading linearly to nothing over
    // 100 yards) cannot beat Test Armor at any distance worth standing at, so its honest answer
    // against these brawlers is 1 yard: contact. That is the model being right, not a regression;
    // a weapon that cannot hurt the enemy has no business choosing the range. So the shooters now
    // carry a rifle with real reach and real damage, which is what the fixture always claimed.
    //
    // The property under test -- the margin slides the opening range toward the MISSION force's own
    // preference, in whichever direction that force's preference lies -- is untouched.
    private static BattleSquad CreateRangedSquad() =>
        CreateSquad("Shooters", RangedSquadTemplate, dexterity: 20f);

    private static readonly WeaponSet LongRifleWeapons = new(
        98,
        "Test Long Rifle",
        primaryRanged: new RangedWeaponTemplate(
            98,
            "Test Long Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 6,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 20,
            maxDistance: 1_000,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 2,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));

    private static readonly SquadTemplate RangedSquadTemplate = new(
        98,
        "Test Ranged Squad",
        LongRifleWeapons,
        [],
        TestModelFactory.TestArmor,
        [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)],
        SquadTypes.None);

    // Melee only, so the effectiveness curve is empty, there is no ranged weapon to stand off with,
    // and the squad prefers contact.
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

    private static BattleSquad CreateSquad(
        string name,
        SquadTemplate template,
        float dexterity = 10f)
    {
        Squad squad = new(name, null, template);
        for (int i = 0; i < 4; i++)
        {
            squad.AddSquadMember(
                TestModelFactory.CreateSoldier(name: $"{name} {i}", dexterity: dexterity));
        }
        return new BattleSquad(false, squad);
    }
}
