using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

public static class SquadExtensions
{
    public static Squad DeepCopy(this Squad originalSquad)
    {
        // 1. Create a new Squad with the same template and basic properties
        Squad newSquad = new Squad(originalSquad.Id, originalSquad.Name, null, originalSquad.SquadTemplate);

        // 2. Deep copy the Loadout (WeaponSets)
        newSquad.Loadout = new List<WeaponSet>();
        foreach (WeaponSet originalSet in originalSquad.Loadout)
        {
            newSquad.Loadout.Add(originalSet.DeepCopy());
        }
        //Squad currentOrders should not be copied

        // 3. Deep copy each soldier in the squad
        Dictionary<int, ISoldier> soldierIdMap = new Dictionary<int, ISoldier>();

        foreach (ISoldier originalSoldier in originalSquad.Members)
        {
            // Assuming you have a Copy method in your Soldier class
            ISoldier newSoldier = originalSoldier.DeepCopy();

            // Add the new soldier to the new squad
            newSquad.AddSquadMember(newSoldier);

            // Keep track of the old and new soldier IDs for PlayerSoldier
            soldierIdMap[originalSoldier.Id] = newSoldier;
        }

        return newSquad;
    }
    public static ISoldier DeepCopy(this ISoldier originalSoldier)
    {
        // Create a new Soldier or PlayerSoldier object, copying basic properties
        Soldier newSoldier = new Soldier(originalSoldier.Template.Species.BodyTemplate)
        {
            Id = originalSoldier.Id,
            Name = originalSoldier.Name,
            Template = originalSoldier.Template,
            Strength = originalSoldier.Strength,
            Dexterity = originalSoldier.Dexterity,
            Constitution = originalSoldier.Constitution,
            Intelligence = originalSoldier.Intelligence,
            Perception = originalSoldier.Perception,
            Ego = originalSoldier.Ego,
            Charisma = originalSoldier.Charisma,
            PsychicPower = originalSoldier.PsychicPower,
            AttackSpeed = originalSoldier.AttackSpeed,
            Size = originalSoldier.Size,
            MoveSpeed = originalSoldier.MoveSpeed,
            // Add any other properties that need to be copied
        };

        // Deep copy the Body, including HitLocations and their Wounds
        newSoldier.Body = originalSoldier.Body.DeepCopy();

        // Deep copy Skills
        foreach (Skill skill in originalSoldier.Skills)
        {
            newSoldier.AddSkillPoints(skill.BaseSkill, skill.PointsInvested);
        }

        return newSoldier;
    }

    public static Body DeepCopy(this Body originalBody)
    {
        // Deep copy each HitLocation
        List<HitLocation> newHitLocations = new List<HitLocation>();
        foreach (HitLocation originalHitLocation in originalBody.HitLocations)
        {
            newHitLocations.Add(originalHitLocation.DeepCopy());
        }

        return new Body(newHitLocations);
    }

    public static HitLocation DeepCopy(this HitLocation originalHitLocation)
    {
        // Create a new HitLocation, copying basic properties
        HitLocation newHitLocation = new HitLocation(originalHitLocation.Template)
        {
            Armor = originalHitLocation.Armor,
            IsCybernetic = originalHitLocation.IsCybernetic,
        };

        // Deep copy Wounds
        newHitLocation.Wounds = new Wounds(originalHitLocation.Wounds.WoundTotal, originalHitLocation.Wounds.WeeksOfHealing);

        return newHitLocation;
    }

    public static WeaponSet DeepCopy(this WeaponSet originalSet)
    {
        return new WeaponSet(originalSet.Id, originalSet.Name, originalSet.PrimaryRangedWeapon,
            originalSet.SecondaryRangedWeapon, originalSet.PrimaryMeleeWeapon, originalSet.SecondaryMeleeWeapon);
    }
}