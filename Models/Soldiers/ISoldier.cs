using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;

namespace OnlyWar.Models.Soldiers
{
    public interface ISoldier : ICloneable
    {
        int Id { get; }
        string Name { get; }
        SoldierTemplate Template { get; }

        float Strength { get; }
        float Dexterity { get; }
        float Constitution { get; }
        float Perception { get; }
        float Intelligence { get; }
        float Ego { get; }
        float Charisma { get; }
        float PsychicPower { get; }
        float AttackSpeed { get; }
        float Size { get; }
        float MoveSpeed { get; }
        Body Body { get; }
        Squad AssignedSquad { get; set; }
        /// <summary>Hands and vital locations only -- excludes motive capability.</summary>
        bool CanFight { get; }
        /// <summary>Motive locations only: <see cref="MotiveSpeedMultiplier"/> is above zero.</summary>
        bool CanMove { get; }
        /// <summary>
        /// How much of his foot speed his motive wounds leave him, 1.0 down to 0.0
        /// (Design/Reference/CasualtyRealism.md §2.1). Zero means he cannot walk at all.
        /// </summary>
        float MotiveSpeedMultiplier { get; }
        /// <summary><see cref="CanFight"/> and <see cref="CanMove"/>, absent untreated severance: still in the fight.</summary>
        bool IsCombatEffective { get; }
        /// <summary>True when an authored motive or hand-group limb is currently severed.</summary>
        bool HasUntreatedSeveredLimb { get; }
        IReadOnlyCollection<Skill> Skills { get; }

        IReadOnlyList<int> FunctioningHandGroupIds { get; }
        int FunctioningHands { get; }
        bool CanUseTwoHandedWeapon { get; }
        void AddSkillPoints(BaseSkill skill, float points);
        void AddAttributePoints(Attribute attribute, float points);
        float GetTotalSkillValue(BaseSkill skill);
        Skill GetBestSkillInCategory(SkillCategory category);
    }
}
