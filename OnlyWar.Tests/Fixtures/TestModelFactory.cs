using System;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Tests.Fixtures;

// Every SoldierTemplate here must pass a non-zero battleValue. BattleValue defaults to 0, and a
// side whose total BattleValue is 0 degenerates the force math in BattleTurnResolver: bvShare
// comes out 0, so BattleMoraleEvaluator.ComputeForceDisadvantage reports maximum disadvantage and
// the morale context multiplier pins at its ceiling. Harmless in the pure-function tests that
// pass their own inputs, but silently wrong in any test that runs a full battle through
// BattleTurnResolver. A test that WANTS a valueless side should declare its own battleValue: 0
// template locally, the way BattleTurnResolverWithdrawalTests does, so the intent is explicit.
internal static class TestModelFactory
{
    public static MeleeWeaponTemplate DefaultUnarmedWeapon { get; } = new(
        0,
        "Test Fist",
        EquipLocation.OneHand,
        TestSkills.Melee,
        0,
        1,
        1,
        0,
        0.5f,
        -1,
        1);

    public static Species HumanSpecies { get; } = new(
        1,
        "Test Human",
        Value(10),
        Value(10),
        Value(10),
        Value(10),
        Value(10),
        Value(10),
        Value(10),
        Value(0),
        Value(10),
        Value(6),
        Value(1),
        1,
        1,
        0f,
        0f,
        SpeciesAbilities.None,
        HumanBodyTemplate.Instance,
        DefaultUnarmedWeapon);

    public static SoldierTemplate MarineTemplate { get; } = new(
        1,
        HumanSpecies,
        "Test Marine",
        1,
        1,
        false,
        0,
        Array.Empty<ValueTuple<BaseSkill, float>>(),
        battleValue: 2);

    // A 1x1 species that tunnels — used to exercise burrow-arrival placement.
    public static Species BurrowerSpecies { get; } = new(
        2,
        "Test Burrower",
        Value(10), Value(10), Value(10), Value(10), Value(10), Value(10), Value(10),
        Value(0), Value(10), Value(6), Value(1),
        1,
        1,
        0f,
        0f,
        SpeciesAbilities.Burrow,
        HumanBodyTemplate.Instance,
        DefaultUnarmedWeapon);

    public static SoldierTemplate BurrowerTemplate { get; } = new(
        3,
        BurrowerSpecies,
        "Test Burrower",
        1,
        1,
        false,
        0,
        Array.Empty<ValueTuple<BaseSkill, float>>(),
        battleValue: 2);

    // A synapse-providing species (OnlyWar_TDD.md §6.6) — used to exercise
    // squad-to-squad synapse coverage. Radius is well inside the small integer distances
    // BattleGridManager tests place soldiers at, so "in range" / "out of range" cases stay
    // legible.
    public static Species SynapseProviderSpecies { get; } = new(
        3,
        "Test Synapse Provider",
        Value(10), Value(10), Value(10), Value(10), Value(10), Value(10), Value(10),
        Value(0), Value(10), Value(6), Value(1),
        1,
        1,
        0f,
        0f,
        SpeciesAbilities.Synapse,
        HumanBodyTemplate.Instance,
        DefaultUnarmedWeapon,
        synapseRadius: 10f);

    public static SoldierTemplate SynapseProviderTemplate { get; } = new(
        4,
        SynapseProviderSpecies,
        "Test Synapse Provider",
        1,
        1,
        false,
        0,
        Array.Empty<ValueTuple<BaseSkill, float>>(),
        battleValue: 2);

    public static SoldierTemplate SergeantTemplate { get; } = new(
        2,
        HumanSpecies,
        "Test Sergeant",
        2,
        1,
        true,
        0,
        Array.Empty<ValueTuple<BaseSkill, float>>(),
        battleValue: 2);

    // Two more squad-leader templates so command-seniority ordering can be exercised on both of
    // its keys: the veteran sergeant outranks the sergeant on subrank alone (same Rank), and the
    // captain outranks both on rank. Mirrors the shape of the real chapter, where Rank 5 holds
    // Sergeant/Scout Sergeant/Veteran Sergeant and Rank 6 holds Captain.
    public static SoldierTemplate VeteranSergeantTemplate { get; } = new(
        5,
        HumanSpecies,
        "Test Veteran Sergeant",
        2,
        2,
        true,
        0,
        Array.Empty<ValueTuple<BaseSkill, float>>(),
        battleValue: 2);

    public static SoldierTemplate CaptainTemplate { get; } = new(
        6,
        HumanSpecies,
        "Test Captain",
        3,
        1,
        true,
        0,
        Array.Empty<ValueTuple<BaseSkill, float>>(),
        battleValue: 2);

    public static ArmorTemplate TestArmor { get; } = new(1, "Test Armor", 5, 0);

    public static WeaponSet DefaultWeapons { get; } = new(
        1,
        "Test Weapons",
        primaryRanged: new RangedWeaponTemplate(
            1,
            "Test Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            0,
            1,
            1,
            0,
            5,
            100,
            1,
            10,
            0,
            1,
            true,
            1,
            0,
            0,
            0),
        primaryMelee: new MeleeWeaponTemplate(
            2,
            "Test Knife",
            EquipLocation.OneHand,
            TestSkills.Melee,
            0,
            1,
            1,
            0,
            1,
            0,
            1));

    // Thrown blast weapon (TemplateType 3): MaximumRange holds meters-per-Strength-point.
    public static RangedWeaponTemplate FragGrenadeTemplate { get; } = new(
        3,
        "Test Frag Grenade",
        EquipLocation.OneHand,
        TestSkills.Ranged,
        0,
        1,
        1,
        4,
        5,
        3,
        1,
        1,
        0,
        0,
        false,
        1,
        3,
        6,
        0);

    public static WeaponSet GrenadierWeapons { get; } = new(
        2,
        "Test Grenadier Weapons",
        primaryRanged: DefaultWeapons.PrimaryRangedWeapon,
        primaryMelee: DefaultWeapons.PrimaryMeleeWeapon,
        grenadeWeapon: FragGrenadeTemplate);

    // NEVER hand this instance to a Faction constructor. Faction re-owns every SquadTemplate in
    // the dictionary it is given (`template.Faction = this`, Models/Faction.cs), and this object is
    // a process-wide static shared by every fixture-built squad. Binding it to a non-player faction
    // therefore makes Squad.Faction.IsPlayerFaction false for every subsequently built test squad,
    // for the rest of the run — which defeats OrderAssignment.IsPlayerOrder and breaks order reuse
    // in whatever tests xUnit happens to schedule afterwards. It presents as an intermittent,
    // ordering-dependent failure a long way from the test that caused it.
    // Build a private copy instead (see FactionStrategyControllerTests for the pattern).
    public static SquadTemplate SquadTemplate { get; } = new(
        1,
        "Test Squad",
        DefaultWeapons,
        [],
        TestArmor,
        [new SquadTemplateElement(SergeantTemplate, 0, 1), new SquadTemplateElement(MarineTemplate, 0, 4)],
        SquadTypes.None);

    // Test soldiers used to leave Id at its default 0, because nothing in the fixtures cared. That stops
    // being harmless the moment two fixture-built squads meet on a battle grid: BattleGridManager keys
    // placement by soldier id, so the second squad throws "Soldier 0 is already placed". Production never
    // had this problem - SoldierFactory assigns from a static positive counter when no
    // IEntityIdAllocator is supplied, seeded on load by GameStateDataAccess - so this only ever bit
    // fixture-built forces. Interlocked because xUnit runs test classes in parallel.
    private static int _nextTestSoldierId;

    public static Soldier CreateSoldier(
        SoldierTemplate template = null,
        string name = "Test Marine",
        float dexterity = 10,
        float strength = 10,
        float charisma = 10,
        params Skill[] skills)
    {
        Soldier soldier = new(HumanBodyTemplate.Instance)
        {
            Id = System.Threading.Interlocked.Increment(ref _nextTestSoldierId),
            Name = name,
            Template = template ?? MarineTemplate,
            Dexterity = dexterity,
            Strength = strength,
            Charisma = charisma,
            Constitution = 10,
            Intelligence = 10,
            Ego = 10,
            Perception = 10,
            AttackSpeed = 10,
            MoveSpeed = 6,
            Size = 1
        };

        foreach (Skill skill in skills)
        {
            soldier.AddSkillPoints(skill.BaseSkill, skill.PointsInvested);
        }

        return soldier;
    }

    public static Squad CreateSquad(string name, params Soldier[] soldiers)
    {
        Squad squad = new(name, null, SquadTemplate);
        foreach (Soldier soldier in soldiers)
        {
            squad.AddSquadMember(soldier);
        }

        return squad;
    }

    private static NormalizedValueTemplate Value(float value)
    {
        return new NormalizedValueTemplate
        {
            BaseValue = value,
            StandardDeviation = 0
        };
    }
}
