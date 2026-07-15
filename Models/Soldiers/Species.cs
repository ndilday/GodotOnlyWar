using System.Collections.Generic;

using OnlyWar.Models.Equippables;

namespace OnlyWar.Models.Soldiers
{
    public class SkillTemplate : NormalizedValueTemplate
    {
        public BaseSkill BaseSkill;
    }

    public class Species
    {
        public int Id { get; }
        public string Name { get; }

        // attributes
        public NormalizedValueTemplate Strength { get; }
        public NormalizedValueTemplate Dexterity { get; }
        public NormalizedValueTemplate Perception { get; }
        public NormalizedValueTemplate Intelligence { get; }
        public NormalizedValueTemplate Ego { get; }
        public NormalizedValueTemplate Charisma { get; }
        public NormalizedValueTemplate Constitution { get; }
        public NormalizedValueTemplate PsychicPower { get; }
        
        public NormalizedValueTemplate AttackSpeed { get; }
        public NormalizedValueTemplate MoveSpeed { get; }
        public NormalizedValueTemplate Size { get; }
        public ushort Width { get; }
        public ushort Depth { get; }

        // Defensive "harder to hit" levers. Melee evasion is subtracted from the
        // attacker's total in the contested melee roll; ranged evasion is a flat
        // subtraction in shooting (and the AI's range-seeking). See
        // Design/EvasionBurrowAndAmbush.md.
        public float MeleeEvasion { get; }
        public float RangedEvasion { get; }
        public SpeciesAbilities Abilities { get; }
        public BodyTemplate BodyTemplate { get; }
        public int DefaultUnarmedWeaponTemplateId { get; }
        public MeleeWeaponTemplate DefaultUnarmedWeapon { get; }

        public Species(int id, string name, NormalizedValueTemplate strength,
                       NormalizedValueTemplate dex, NormalizedValueTemplate con,
                       NormalizedValueTemplate intl, NormalizedValueTemplate per,
                       NormalizedValueTemplate ego, NormalizedValueTemplate cha,
                       NormalizedValueTemplate psy, NormalizedValueTemplate atk,
                       NormalizedValueTemplate mov, NormalizedValueTemplate siz,
                       ushort width, ushort depth, float meleeEvasion, float rangedEvasion,
                       SpeciesAbilities abilities, BodyTemplate bodyTemplate,
                       MeleeWeaponTemplate defaultUnarmedWeapon)
        {
            Id = id;
            Name = name;
            Strength = strength;
            Dexterity = dex;
            Perception = per;
            Intelligence = intl;
            Ego = ego;
            Charisma = cha;
            Constitution = con;
            PsychicPower = psy;
            AttackSpeed = atk;
            MoveSpeed = mov;
            Size = siz;
            Width = width;
            Depth = depth;
            MeleeEvasion = meleeEvasion;
            RangedEvasion = rangedEvasion;
            Abilities = abilities;
            BodyTemplate = bodyTemplate;
            DefaultUnarmedWeapon = defaultUnarmedWeapon
                ?? throw new System.ArgumentNullException(nameof(defaultUnarmedWeapon));
            DefaultUnarmedWeaponTemplateId = defaultUnarmedWeapon.Id;
        }
    }
}
