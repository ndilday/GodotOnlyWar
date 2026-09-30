using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign
{
    public enum ArmoryPromotionReasonCode
    {
        None = 0,
        MissingSoldier,
        NoArmory,
        NotInTheArmory,
        NotInTheBranch,
        OnMars,
        NotAnArmoryRole,
        NotAPromotion,
        NoOpenPlace,
        BelowRequirement,
        Unavailable
    }

    public sealed record ArmoryPromotionEvaluation(
        bool IsAllowed,
        ArmoryPromotionReasonCode ReasonCode,
        string Reason)
    {
        public static ArmoryPromotionEvaluation Allowed { get; } =
            new(true, ArmoryPromotionReasonCode.None, null);
    }

    /// <summary>
    /// Promotions inside the Techmarine branch (TDD §6.14): Techmarine to Master
    /// Techmarine, and to Master of the Forge. These are the player's decisions on the Armory
    /// screen, never automatic, and never an ordinary Chapter screen transfer: a specialist branch
    /// promotes on its own screen.
    ///
    /// A target is any Techmarine-branch slot of the Armory's squad template that ranks above the
    /// brother's current template (rank, then subrank), so the rules data decides which ranks
    /// exist. The brother serves in the Armory, is home from Mars, meets the target's template
    /// requirements, and is free to be reorganized; the target must have an open place (the Master
    /// of the Forge has one). He stays in the Armory and keeps any posting he has; only his
    /// template changes.
    /// </summary>
    public sealed class ArmoryPromotionService
    {
        private readonly SoldierTemplateEligibilityService _eligibility = new();
        private readonly CharacterAvailabilityService _availability = new();
        private readonly byte _branch;

        /// <param name="techmarineTemplate">Any template of the branch; its specialist type names it.</param>
        public ArmoryPromotionService(SoldierTemplate techmarineTemplate)
        {
            ArgumentNullException.ThrowIfNull(techmarineTemplate);
            if (techmarineTemplate.SpecialistType == 0)
            {
                throw new ArgumentException("The Techmarine template must name a specialist branch.",
                    nameof(techmarineTemplate));
            }
            _branch = techmarineTemplate.SpecialistType;
        }

        public bool IsInBranch(SoldierTemplate template) => template?.SpecialistType == _branch;

        /// <summary>The Armory's ranks above the entry rank, most senior first.</summary>
        public IReadOnlyList<SoldierTemplate> PromotionTargets(Squad armory) =>
            armory?.SquadTemplate?.Elements
                .Select(element => element.SoldierTemplate)
                .Where(IsInBranch)
                .Distinct()
                .OrderByDescending(template => template.Rank)
                .ThenByDescending(template => template.Subrank)
                .ToList()
            ?? [];

        /// <summary>The targets that rank above this brother's current template.</summary>
        public IReadOnlyList<SoldierTemplate> PromotionTargets(Squad armory, PlayerSoldier soldier) =>
            PromotionTargets(armory)
                .Where(target => soldier?.Template != null && RanksAbove(target, soldier.Template))
                .ToList();

        /// <summary>The Armory's seated leader (its Master of the Forge), or null while vacant.</summary>
        public PlayerSoldier FindSeatedLeader(Squad armory) =>
            armory?.Members
                .OfType<PlayerSoldier>()
                .FirstOrDefault(member => member.Template?.IsSquadLeader == true && IsInBranch(member.Template));

        public ArmoryPromotionEvaluation Evaluate(PlayerSoldier soldier, Squad armory, SoldierTemplate target)
        {
            if (soldier == null)
            {
                return Reject(ArmoryPromotionReasonCode.MissingSoldier, "No soldier selected.");
            }
            if (armory == null)
            {
                return Reject(ArmoryPromotionReasonCode.NoArmory, "The chapter has no Armory.");
            }
            if (!ReferenceEquals(soldier.AssignedSquad, armory))
            {
                return Reject(ArmoryPromotionReasonCode.NotInTheArmory,
                    $"{soldier.Name} does not serve in the Armory.");
            }
            if (!IsInBranch(soldier.Template))
            {
                return Reject(ArmoryPromotionReasonCode.NotInTheBranch,
                    $"{soldier.Name} is not a Techmarine.");
            }
            if (MechanicusTrainingService.IsOnMars(soldier))
            {
                return Reject(ArmoryPromotionReasonCode.OnMars,
                    $"{soldier.Name} is still training on Mars.");
            }
            SquadTemplateElement element = armory.SquadTemplate?.Elements
                .FirstOrDefault(candidate => candidate.SoldierTemplate?.Id == target?.Id);
            if (element == null || !IsInBranch(target))
            {
                return Reject(ArmoryPromotionReasonCode.NotAnArmoryRole,
                    $"{target?.Name ?? "That role"} is not a rank of the Armory.");
            }
            if (!RanksAbove(target, soldier.Template))
            {
                return Reject(ArmoryPromotionReasonCode.NotAPromotion,
                    $"{soldier.Name} already ranks as high as {target.Name}.");
            }
            int holders = armory.Members.Count(member => member.Template?.Id == target.Id);
            if (holders >= element.MaximumNumber)
            {
                PlayerSoldier holder = armory.Members.OfType<PlayerSoldier>()
                    .FirstOrDefault(member => member.Template?.Id == target.Id);
                return Reject(ArmoryPromotionReasonCode.NoOpenPlace,
                    element.MaximumNumber == 1 && holder != null
                        ? $"{holder.Name} is already {target.Name}."
                        : $"The Armory has no open {target.Name} place.");
            }
            if (!_eligibility.IsEligible(soldier, target))
            {
                return Reject(ArmoryPromotionReasonCode.BelowRequirement,
                    $"{soldier.Name} does not meet the {target.Name} requirements.");
            }
            CharacterAvailabilityEvaluation availability =
                _availability.EvaluateOrganizationalTransfer(soldier);
            if (!availability.IsAllowed)
            {
                return Reject(ArmoryPromotionReasonCode.Unavailable, availability.Reason);
            }
            return ArmoryPromotionEvaluation.Allowed;
        }

        public void Promote(PlayerSoldier soldier, Squad armory, SoldierTemplate target, Date date)
        {
            ArgumentNullException.ThrowIfNull(date);
            ArmoryPromotionEvaluation evaluation = Evaluate(soldier, armory, target);
            if (!evaluation.IsAllowed)
            {
                throw new InvalidOperationException(evaluation.Reason);
            }
            soldier.Template = target;
            soldier.AddEvent(new SoldierEvent(date, SoldierEventType.Promotion,
                $"promoted to {target.Name}"));
        }

        private static bool RanksAbove(SoldierTemplate target, SoldierTemplate current) =>
            target.Rank > current.Rank
            || (target.Rank == current.Rank && target.Subrank > current.Subrank);

        private static ArmoryPromotionEvaluation Reject(
            ArmoryPromotionReasonCode code, string reason) => new(false, code, reason);
    }
}
