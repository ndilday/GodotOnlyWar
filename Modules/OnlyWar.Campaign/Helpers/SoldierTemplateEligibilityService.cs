using System;
using System.Linq;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Evaluates the rules-database requirements attached to a destination soldier
    /// template. Rank, open-slot, and location rules remain the responsibility of the
    /// transfer service.
    /// </summary>
    public sealed class SoldierTemplateEligibilityService
    {
        public bool IsEligible(PlayerSoldier soldier, SoldierTemplate destinationTemplate)
        {
            if (soldier == null || destinationTemplate == null)
            {
                return false;
            }

            foreach (SoldierTemplateRequirement requirement in destinationTemplate.PromotionRequirements)
            {
                float actualValue = GetActualValue(soldier, requirement);
                if (!MeetsRequirement(actualValue, requirement))
                {
                    return false;
                }
            }

            return true;
        }

        private static float GetActualValue(
            PlayerSoldier soldier,
            SoldierTemplateRequirement requirement)
        {
            return requirement.RequirementType switch
            {
                SoldierTemplateRequirementType.SoldierStat
                    when requirement.RequirementKey == SoldierTemplateRequirementKeys.PsychicPower
                    => soldier.PsychicPower,
                SoldierTemplateRequirementType.Rating
                    => soldier.SoldierEvaluationHistory.LastOrDefault()?[requirement.RequirementKey] ?? 0f,
                SoldierTemplateRequirementType.CurrentSpecialistType
                    when requirement.RequirementKey == SoldierTemplateRequirementKeys.SpecialistType
                    => soldier.Template.SpecialistType,
                _ => throw new InvalidOperationException(
                    $"Unsupported soldier-template requirement "
                    + $"'{requirement.RequirementType}:{requirement.RequirementKey}'.")
            };
        }

        private static bool MeetsRequirement(
            float actualValue,
            SoldierTemplateRequirement requirement)
        {
            return requirement.Comparison switch
            {
                SoldierTemplateRequirementComparison.Equal
                    => Math.Abs(actualValue - requirement.RequiredValue) < 0.0001f,
                SoldierTemplateRequirementComparison.GreaterThan
                    => actualValue > requirement.RequiredValue,
                SoldierTemplateRequirementComparison.GreaterThanOrEqual
                    => actualValue >= requirement.RequiredValue,
                _ => throw new InvalidOperationException(
                    $"Unsupported soldier-template requirement comparison "
                    + $"'{requirement.Comparison}'.")
            };
        }
    }
}
