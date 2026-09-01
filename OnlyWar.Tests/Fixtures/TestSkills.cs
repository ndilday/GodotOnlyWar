using OnlyWar.Models.Soldiers;

namespace OnlyWar.Tests.Fixtures;

internal static class TestSkills
{
    public static readonly BaseSkill Ranged = new(1, SkillCategory.Ranged, "Test Ranged", Attribute.Dexterity, 0, "test_ranged");
    public static readonly BaseSkill Melee = new(2, SkillCategory.Melee, "Test Melee", Attribute.Strength, 0, "test_melee");
    public static readonly BaseSkill Stealth = new(3, SkillCategory.Espionage, "Test Stealth", Attribute.Dexterity, 1, "test_stealth");
    public static readonly BaseSkill Leadership = new(4, SkillCategory.Military, "Test Leadership", Attribute.Presence, 0, "test_leadership");
    public static readonly BaseSkill Tactics = new(5, SkillCategory.Military, "Test Tactics", Attribute.Intelligence, 0, "test_tactics");
}
