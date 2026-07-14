using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class WoundResolverTests
{
    // A soldier with Constitution 10 makes the wound ratio == totalDamage / 10,
    // so damage values map cleanly onto the severity thresholds.
    private static BattleSoldier CreateSufferer(params HitLocation[] bodyLocations)
    {
        Soldier soldier = bodyLocations.Length == 0
            ? TestModelFactory.CreateSoldier(name: "Wounded Marine")
            : new Soldier(bodyLocations.ToList(), [])
            {
                Name = "Wounded Marine",
                Constitution = 10
            };
        soldier.Id = 1;
        return new BattleSoldier(soldier, null);
    }

    private static HitLocationTemplate Template(
        float naturalArmor = 0,
        float woundMultiplier = 1,
        uint crippleWound = uint.MaxValue,
        uint severWound = uint.MaxValue,
        bool isVital = false,
        bool isMotive = false,
        bool isRangedWeaponHolder = false,
        bool isMeleeWeaponHolder = false)
    {
        return new HitLocationTemplate
        {
            Id = 1,
            Name = "Test Location",
            NaturalArmor = naturalArmor,
            WoundMultiplier = woundMultiplier,
            CrippleWound = crippleWound,
            SeverWound = severWound,
            HitProbabilityMap = [1, 1, 1],
            IsVital = isVital,
            IsMotive = isMotive,
            IsRangedWeaponHolder = isRangedWeaponHolder,
            IsMeleeWeaponHolder = isMeleeWeaponHolder
        };
    }

    private static WoundResolution Enqueue(
        WoundResolver resolver,
        HitLocation hitLocation,
        float damage,
        BattleSoldier sufferer = null)
    {
        WoundResolution wound = new(null, null, sufferer ?? CreateSufferer(), damage, hitLocation);
        resolver.WoundQueue.Add(wound);
        return wound;
    }

    [Theory]
    [InlineData(1f, WoundLevel.Negligible)]
    [InlineData(2f, WoundLevel.Minor)]
    [InlineData(3f, WoundLevel.Moderate)]
    [InlineData(6f, WoundLevel.Major)]
    [InlineData(10f, WoundLevel.Critical)]
    [InlineData(20f, WoundLevel.Massive)]
    public void Resolve_AssignsWoundLevelFromDamageRatio(float damage, WoundLevel expected)
    {
        WoundResolver resolver = new();
        HitLocation location = new(Template());
        Enqueue(resolver, location, damage);

        resolver.Resolve();

        Assert.Equal((uint)expected, location.Wounds.WoundTotal);
    }

    [Fact]
    public void Resolve_SubtractsNaturalArmorBeforeScoringWound()
    {
        WoundResolver resolver = new();
        HitLocation location = new(Template(naturalArmor: 5));
        // 6 damage - 5 armor = 1 effective => ratio 0.1 => Negligible
        Enqueue(resolver, location, 6f);

        resolver.Resolve();

        Assert.Equal(1, location.Wounds.NegligibleWounds);
    }

    [Fact]
    public void Resolve_AppliesWoundMultiplier()
    {
        WoundResolver resolver = new();
        HitLocation location = new(Template(woundMultiplier: 4));
        // 5 damage * 4 = 20 effective => ratio 2.0 => Massive
        Enqueue(resolver, location, 5f);

        resolver.Resolve();

        Assert.Equal((uint)WoundLevel.Massive, location.Wounds.WoundTotal);
    }

    [Fact]
    public void Resolve_IgnoresAlreadySeveredLocation()
    {
        WoundResolver resolver = new();
        // pre-severed: woundTotal already at/above the sever threshold
        HitLocation location = new(
            Template(severWound: (uint)WoundLevel.Critical),
            isCybernetic: false,
            armor: 0,
            woundTotal: (uint)WoundLevel.Critical,
            weeksOfHealing: 0);
        uint before = location.Wounds.WoundTotal;
        Enqueue(resolver, location, 20f);

        resolver.Resolve();

        Assert.Equal(before, location.Wounds.WoundTotal);
    }

    [Fact]
    public void Resolve_RaisesSoldierDeathWhenVitalLocationCrippled()
    {
        WoundResolver resolver = new();
        bool died = false;
        resolver.OnSoldierDeath += (_, _) => died = true;
        resolver.OnSoldierFall += (_, _) => { };
        HitLocation location = new(Template(crippleWound: (uint)WoundLevel.Critical, isVital: true));
        // 10 damage => ratio 1.0 => Critical, which meets the cripple threshold
        Enqueue(resolver, location, 10f);

        resolver.Resolve();

        Assert.True(died);
    }

    [Fact]
    public void Resolve_RaisesSoldierFallWhenMotiveLocationCrippled()
    {
        WoundResolver resolver = new();
        bool fell = false;
        resolver.OnSoldierFall += (_, _) => fell = true;
        resolver.OnSoldierDeath += (_, _) => { };
        HitLocation location = new(Template(crippleWound: (uint)WoundLevel.Critical, isMotive: true));
        Enqueue(resolver, location, 10f);

        resolver.Resolve();

        Assert.True(fell);
    }

    [Fact]
    public void Resolve_ReportsUnableToWalkOnlyForWoundThatCripplesMotiveLocation()
    {
        WoundResolver resolver = new();
        resolver.OnSoldierFall += (_, _) => { };
        HitLocation location = new(Template(crippleWound: (uint)WoundLevel.Critical, isMotive: true));
        WoundResolution first = Enqueue(resolver, location, 10f);
        WoundResolution second = Enqueue(resolver, location, 10f);

        resolver.Resolve();

        int impactLineCount = (first.Description?.Contains("can no longer walk") == true ? 1 : 0)
            + (second.Description?.Contains("can no longer walk") == true ? 1 : 0);
        Assert.Equal(1, impactLineCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_DoesNotAddStatusSentenceForWoundThatDisablesLastWeaponHolder(bool rangedHolder)
    {
        WoundResolver resolver = new();
        resolver.OnSoldierFall += (_, _) => { };
        resolver.OnSoldierDeath += (_, _) => { };

        HitLocation weaponHolder = new(Template(
            crippleWound: (uint)WoundLevel.Critical,
            isRangedWeaponHolder: rangedHolder,
            isMeleeWeaponHolder: !rangedHolder));
        HitLocation otherWeaponHolder = new(Template(
            crippleWound: (uint)WoundLevel.Critical,
            isRangedWeaponHolder: !rangedHolder,
            isMeleeWeaponHolder: rangedHolder));
        BattleSoldier sufferer = CreateSufferer(weaponHolder, otherWeaponHolder);
        WoundResolution wound = Enqueue(resolver, weaponHolder, 10f, sufferer);

        resolver.Resolve();

        Assert.DoesNotContain("can no longer fight", wound.Description);
        Assert.DoesNotContain("can no longer walk", wound.Description);
    }

    [Fact]
    public void Resolve_ReportsDeathOnlyForWoundThatCripplesVitalLocation()
    {
        WoundResolver resolver = new();
        resolver.OnSoldierDeath += (_, _) => { };
        HitLocation location = new(Template(crippleWound: (uint)WoundLevel.Critical, isVital: true));
        WoundResolution first = Enqueue(resolver, location, 10f);
        WoundResolution second = Enqueue(resolver, location, 10f);

        resolver.Resolve();

        int deathLineCount = (first.Description?.Contains("has died") == true ? 1 : 0)
            + (second.Description?.Contains("has died") == true ? 1 : 0);
        Assert.Equal(1, deathLineCount);
    }

    [Fact]
    public void Resolve_DrainsTheWoundQueue()
    {
        WoundResolver resolver = new();
        Enqueue(resolver, new HitLocation(Template()), 3f);
        Enqueue(resolver, new HitLocation(Template()), 3f);

        resolver.Resolve();

        Assert.True(resolver.WoundQueue.IsEmpty);
    }

    [Fact]
    public void WoundQueue_TakesMostRecentlyAddedWoundFirst()
    {
        WoundResolver resolver = new();
        WoundResolution first = new(null, null, CreateSufferer(), 1, new HitLocation(Template()));
        WoundResolution second = new(null, null, CreateSufferer(), 2, new HitLocation(Template()));
        resolver.WoundQueue.Add(first);
        resolver.WoundQueue.Add(second);

        Assert.True(resolver.WoundQueue.TryTake(out WoundResolution taken));
        Assert.Same(second, taken);
    }

    [Fact]
    public void Resolve_InvalidatesCachedAbleSoldiersWhenWoundDisablesMember()
    {
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(
            "Test Squad",
            TestModelFactory.CreateSoldier(name: "Wounded Marine")));
        BattleSoldier sufferer = squad.Soldiers[0];
        HitLocation vitalLocation = sufferer.Soldier.Body.HitLocations
            .First(location => location.Template.IsVital);
        List<BattleSoldier> cachedAbleSoldiers = squad.AbleSoldiers;
        Assert.Same(cachedAbleSoldiers, squad.AbleSoldiers);

        WoundResolver resolver = new();
        resolver.OnSoldierDeath += (_, _) => { };
        resolver.OnSoldierFall += (_, _) => { };
        resolver.WoundQueue.Add(new WoundResolution(
            null,
            null,
            sufferer,
            float.MaxValue,
            vitalLocation));

        resolver.Resolve();

        Assert.Empty(squad.AbleSoldiers);
        Assert.NotSame(cachedAbleSoldiers, squad.AbleSoldiers);
    }
}
