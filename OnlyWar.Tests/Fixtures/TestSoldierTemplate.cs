using System;
using System.Collections.Generic;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Tests.Fixtures;

/// <summary>
/// A SoldierTemplate that remembers a battle value for test fixtures. In the game the value is
/// the squad element's StrategicValuePerSoldier (a soldier template can stand in squads of very different
/// worth); here <see cref="TestModelFactory.SquadTemplateFor"/> copies it onto the element it
/// builds for the template, so tests can still say "a soldier worth N". Production code never
/// reads <see cref="StrategicValue"/>.
/// </summary>
internal sealed class TestSoldierTemplate : SoldierTemplate
{
    public int StrategicValue { get; }

    public TestSoldierTemplate(int id, Species species, string name, byte rank, byte subrank,
                               bool isSquadLeader, byte specialistType,
                               IReadOnlyCollection<ValueTuple<BaseSkill, float>> mosTraining,
                               TrainingProfile workExperienceTrainingProfile = null,
                               int strategicValue = 0,
                               IReadOnlyList<SoldierTemplateRequirement> promotionRequirements = null,
                               IReadOnlyDictionary<string, IReadOnlyList<WeaponSet>> weaponOptionsByGroup = null,
                               IReadOnlyDictionary<string, IReadOnlyList<EquipmentKitTemplate>> equipmentKitOptionsByGroup = null)
        : base(id, species, name, rank, subrank, isSquadLeader, specialistType, mosTraining,
               workExperienceTrainingProfile, promotionRequirements,
               weaponOptionsByGroup, equipmentKitOptionsByGroup)
    {
        StrategicValue = strategicValue;
    }
}
