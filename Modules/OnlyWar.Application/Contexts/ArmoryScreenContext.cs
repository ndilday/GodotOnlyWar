using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Medical.Readiness;
using OnlyWar.Operations.Abstractions;

namespace OnlyWar.Application;

/// <summary>
/// The Armory screen's reads and commands (TDD §6.14): the
/// Techmarines at home, the brothers on Mars, who may be sent next, the Master of the Forge
/// promotion, and where returnees report. The rules themselves live in the Campaign services;
/// this context only finds the Armory, projects rows, and validates what the player picked.
/// </summary>
internal sealed class ArmoryScreenContext
{
    private readonly Sector _sector;
    private readonly GameRulesData _rules;
    private readonly Date _currentDate;
    private readonly IOrderCommitmentSurface _commitments;
    private readonly SoldierTemplateEligibilityService _eligibility = new();
    private MechanicusDepartureService _departures;
    private ArmoryPromotionService _promotions;

    internal ArmoryScreenContext(
        Sector sector,
        GameRulesData rules,
        Date currentDate,
        IOrderCommitmentSurface commitments)
    {
        _sector = sector;
        _rules = rules;
        _currentDate = currentDate;
        _commitments = commitments ?? throw new ArgumentNullException(nameof(commitments));
    }

    private MechanicusDepartureService Departures =>
        _departures ??= new MechanicusDepartureService(
            _commitments, Doctrine.Techmarine, Doctrine.ScoutMarine);

    private ArmoryPromotionService Promotions =>
        _promotions ??= new ArmoryPromotionService(Doctrine.Techmarine);

    /// <summary>
    /// The specialist branches managed on their own screen rather than by ordinary transfer:
    /// today, the Techmarine branch. Shared with the Chapter screen and the muster so neither
    /// offers a way round Mars or round the Armory's promotion.
    /// </summary>
    internal static IReadOnlyList<byte> BranchScreenSpecialistTypes(GameRulesData rules)
    {
        byte? techmarine = rules?.ChapterDoctrine?.Techmarine?.SpecialistType;
        return techmarine is > 0 ? [techmarine.Value] : [];
    }

    private ChapterGenerationDoctrine Doctrine => _rules?.ChapterDoctrine;
    private PlayerForce Force => _sector?.PlayerForce;

    internal ArmoryOverview Query(Guid sessionToken)
    {
        Squad armory = FindArmory();
        if (armory == null)
        {
            return ArmoryOverview.Empty(sessionToken) with
            {
                Loan = BuildLoan([])
            };
        }

        List<PlayerSoldier> roster = Roster().ToList();
        List<PlayerSoldier> onMars = roster.Where(MechanicusTrainingService.IsOnMars).ToList();
        PlayerSoldier seated = Promotions.FindSeatedLeader(armory);

        List<ArmoryTechmarineRow> atHome = roster
            .Where(soldier => IsTechmarineBranch(soldier.Template)
                && !MechanicusTrainingService.IsOnMars(soldier))
            .OrderByDescending(soldier => soldier.Template.Rank)
            .ThenByDescending(soldier => soldier.Template.Subrank)
            .ThenBy(soldier => soldier.Name, StringComparer.OrdinalIgnoreCase)
            .Select(soldier => BuildTechmarineRow(soldier, armory))
            .ToList();

        List<ArmoryMarsRow> marsRows = onMars
            .OrderBy(soldier => soldier.IndividualPosting.ExpectedReturnDate?.GetTotalWeeks() ?? int.MaxValue)
            .ThenBy(soldier => soldier.Name, StringComparer.OrdinalIgnoreCase)
            .Select(BuildMarsRow)
            .ToList();

        return new ArmoryOverview(
            sessionToken,
            true,
            armory.Name,
            Describe(CampaignLocationService.ForSquad(armory)),
            seated?.Name,
            BuildLoan(onMars),
            atHome,
            marsRows,
            BuildCandidates(roster),
            BuildReturnDestination());
    }

    internal ArmoryPrompt DescribeSendToMars(int soldierId)
    {
        Squad armory = FindArmory();
        if (armory == null) return ArmoryPrompt.Blocked("Send to Mars", "The chapter has no Armory.");
        if (FindSoldier(soldierId) is not PlayerSoldier soldier)
        {
            return ArmoryPrompt.Blocked("Send to Mars", "That brother is no longer on the rolls.");
        }
        if (IsTechmarineBranch(soldier.Template) || MechanicusTrainingService.IsOnMars(soldier))
        {
            return ArmoryPrompt.Blocked("Cannot Send to Mars",
                $"{soldier.Name} is already a {soldier.Template.Name}.");
        }
        MechanicusDepartureEvaluation evaluation =
            Departures.Evaluate(soldier, Force.RecruitmentProgram);
        if (!evaluation.IsAllowed) return ArmoryPrompt.Blocked("Cannot Send to Mars", evaluation.Reason);

        Date returns = MechanicusDepartureService.ExpectedReturnDate(_currentDate);
        return new ArmoryPrompt(
            true,
            "Send to Mars",
            $"Send {soldier.Template.Name} {soldier.Name} to Mars for training by the Adeptus "
            + $"Mechanicus? He leaves {soldier.AssignedSquad?.Name} now, joins the Armory as a "
            + $"Techmarine, and is lost to the chapter until he returns in {returns}, "
            + $"{MechanicusDepartureService.TrainingWeeks / 52} years from now. He cannot be recalled.");
    }

    internal ArmoryCommandResult SendToMars(int soldierId)
    {
        ArmoryPrompt prompt = DescribeSendToMars(soldierId);
        if (!prompt.CanProceed) return new ArmoryCommandResult(false, prompt.Message);

        PlayerSoldier soldier = FindSoldier(soldierId);
        Force.Army.PopulateSquadMap();
        IndividualPosting posting = Departures.Depart(
            soldier,
            FindArmory(),
            _currentDate,
            Force.RecruitmentProgram,
            squadMap: Force.Army.SquadMap);
        return new ArmoryCommandResult(
            true, $"{soldier.Name} has left for Mars. He returns in {posting.ExpectedReturnDate}.");
    }

    internal ArmoryPrompt DescribePromotion(int soldierId, int templateId)
    {
        Squad armory = FindArmory();
        if (armory == null) return ArmoryPrompt.Blocked("Promotion Blocked", "The chapter has no Armory.");
        if (FindSoldier(soldierId) is not PlayerSoldier soldier)
        {
            return ArmoryPrompt.Blocked("Promotion Blocked", "That brother is no longer on the rolls.");
        }
        SoldierTemplate target = FindArmoryRank(armory, templateId);
        ArmoryPromotionEvaluation evaluation = Promotions.Evaluate(soldier, armory, target);
        if (!evaluation.IsAllowed) return ArmoryPrompt.Blocked("Promotion Blocked", evaluation.Reason);
        string role = target.IsSquadLeader ? ", master of the chapter's Armory" : string.Empty;
        return new ArmoryPrompt(
            true,
            $"Promote to {target.Name}",
            $"Promote {soldier.Template.Name} {soldier.Name} to {target.Name}{role}?");
    }

    internal ArmoryCommandResult Promote(int soldierId, int templateId)
    {
        ArmoryPrompt prompt = DescribePromotion(soldierId, templateId);
        if (!prompt.CanProceed) return new ArmoryCommandResult(false, prompt.Message);

        Squad armory = FindArmory();
        PlayerSoldier soldier = FindSoldier(soldierId);
        SoldierTemplate target = FindArmoryRank(armory, templateId);
        Promotions.Promote(soldier, armory, target, _currentDate);
        return new ArmoryCommandResult(true, $"{soldier.Name} is now {target.Name}.");
    }

    private SoldierTemplate FindArmoryRank(Squad armory, int templateId) =>
        Promotions.PromotionTargets(armory).FirstOrDefault(template => template.Id == templateId);

    internal ArmoryCommandResult SetMarsReturnDestination(string destinationKey)
    {
        if (FindArmory() == null) return new ArmoryCommandResult(false, "The chapter has no Armory.");
        string key = string.IsNullOrWhiteSpace(destinationKey)
            ? ArmoryDestinationKeys.Default
            : destinationKey;
        if (key == ArmoryDestinationKeys.Default)
        {
            Force.MarsReturnDestination = null;
            return new ArmoryCommandResult(true, "Returnees will report to the default destination.");
        }
        if (!DestinationChoices().TryGetValue(key, out (CampaignLocation Location, string Label) choice))
        {
            return new ArmoryCommandResult(false, "That destination is not available.");
        }
        Force.MarsReturnDestination = choice.Location;
        return new ArmoryCommandResult(true, $"Returnees will report to {choice.Label}.");
    }

    // Every Armory rank above his own, each with whether he can take it now. A single-place rank
    // that someone already holds (the Master of the Forge's seat) is left out: it is not a choice.
    private ArmoryTechmarineRow BuildTechmarineRow(PlayerSoldier soldier, Squad armory)
    {
        List<ArmoryPromotionOption> promotions = Promotions.PromotionTargets(armory, soldier)
            .Select(target => (Target: target, Evaluation: Promotions.Evaluate(soldier, armory, target)))
            .Where(item => !(item.Evaluation.ReasonCode == ArmoryPromotionReasonCode.NoOpenPlace
                && item.Target.IsSquadLeader))
            .Select(item => new ArmoryPromotionOption(
                item.Target.Id,
                item.Target.Name,
                item.Evaluation.IsAllowed,
                item.Evaluation.IsAllowed ? null : item.Evaluation.Reason))
            .ToList();
        return new ArmoryTechmarineRow(
            soldier.Id,
            soldier.Name,
            soldier.Template.Name,
            Describe(CampaignLocationService.ForSoldier(soldier)),
            DutyStatus(soldier),
            promotions);
    }

    private ArmoryMarsRow BuildMarsRow(PlayerSoldier soldier)
    {
        IndividualPosting posting = soldier.IndividualPosting;
        int weeks = posting.ExpectedReturnDate == null
            ? 0
            : Math.Max(0, posting.ExpectedReturnDate.GetTotalWeeks() - _currentDate.GetTotalWeeks());
        return new ArmoryMarsRow(
            soldier.Id,
            soldier.Name,
            posting.StartedDate?.ToString(),
            posting.ExpectedReturnDate?.ToString() ?? "Unknown",
            weeks);
    }

    // Everyone who may be sent to Mars now: brothers who meet the Techmarine requirement and
    // pass the departure rules, best Tech first. A brother already in the Techmarine branch is
    // on this screen's other lists instead.
    private IReadOnlyList<ArmoryMarsCandidateRow> BuildCandidates(IEnumerable<PlayerSoldier> roster) =>
        roster
            .Where(soldier => soldier.Template != null
                && !IsTechmarineBranch(soldier.Template)
                && !MechanicusTrainingService.IsOnMars(soldier)
                && _eligibility.IsEligible(soldier, Doctrine.Techmarine)
                && Departures.Evaluate(soldier, Force.RecruitmentProgram).IsAllowed)
            .OrderByDescending(Tech)
            .ThenBy(soldier => soldier.Name, StringComparer.OrdinalIgnoreCase)
            .Select(soldier => new ArmoryMarsCandidateRow(
                soldier.Id,
                soldier.Name,
                soldier.Template.Name,
                soldier.AssignedSquad?.Name ?? "Unassigned",
                Describe(CampaignLocationService.ForSoldier(soldier)),
                (int)Math.Round(Tech(soldier))))
            .ToList();

    private ArmoryLoanView BuildLoan(IReadOnlyList<PlayerSoldier> onMars)
    {
        if (Force?.IsMechanicusLoanActive != true)
        {
            return new ArmoryLoanView(false,
                "No adepts are on loan. Replacement surgery needs one of the chapter's own "
                + "Techmarines at the surgery site.");
        }
        Date firstReturn = onMars
            .Select(soldier => soldier.IndividualPosting.ExpectedReturnDate)
            .Where(date => date != null)
            .OrderBy(date => date.GetTotalWeeks())
            .FirstOrDefault();
        string until = firstReturn == null
            ? "until the first brother sent to Mars comes home"
            : $"until the chapter's first Techmarines come home in {firstReturn}";
        return new ArmoryLoanView(true,
            "The Adeptus Mechanicus lends the chapter tech-priests, who meet the Techmarine "
            + $"requirement for replacement surgery {until}.");
    }

    private ArmoryReturnDestinationView BuildReturnDestination()
    {
        Dictionary<string, (CampaignLocation Location, string Label)> choices = DestinationChoices();
        List<ArmoryDestinationOption> options =
        [
            new ArmoryDestinationOption(
                ArmoryDestinationKeys.Default,
                "Default: the Home World's capital, else the flagship")
        ];
        options.AddRange(choices.Select(choice =>
            new ArmoryDestinationOption(choice.Key, choice.Value.Label)));

        string selected = ArmoryDestinationKeys.Default;
        CampaignLocation chosen = Force.MarsReturnDestination;
        if (chosen != null)
        {
            selected = KeyFor(chosen);
            if (!choices.ContainsKey(selected))
            {
                // A standing choice that no longer exists stays visible, so the player sees why
                // returnees go to the duty station instead.
                options.Add(new ArmoryDestinationOption(selected, $"{Describe(chosen)} (unavailable)"));
            }
        }

        CampaignLocation resolved = MechanicusReturnService.ResolveDestination(_sector);
        string resolvedText = resolved == null
            ? $"the Armory's duty station ({Describe(CampaignLocationService.ForSquad(FindArmory()))})"
            : Describe(resolved);
        return new ArmoryReturnDestinationView(
            selected, $"The next brothers home from Mars report to {resolvedText}.", options);
    }

    // The places the player may choose: every chapter ship, and every region of the Home World.
    private Dictionary<string, (CampaignLocation Location, string Label)> DestinationChoices()
    {
        Dictionary<string, (CampaignLocation, string)> choices = [];
        foreach (Ship ship in PlayerShips().OrderByDescending(ship => ship.IsFlagship).ThenBy(ship => ship.Name))
        {
            choices[ArmoryDestinationKeys.Ship(ship.Id)] =
                (CampaignLocation.Aboard(ship), ship.IsFlagship ? $"{ship.Name} (flagship)" : ship.Name);
        }
        if (Force.HomeWorldPlanetId is int homeWorldId
            && _sector.Planets.TryGetValue(homeWorldId, out Planet homeWorld))
        {
            foreach (Region region in homeWorld.Regions.Where(region => region != null))
            {
                choices[ArmoryDestinationKeys.Region(region.Id)] =
                    (CampaignLocation.Landed(region), $"{region.Name}, {homeWorld.Name}");
            }
        }
        return choices;
    }

    private static string KeyFor(CampaignLocation location) =>
        location.Ship != null
            ? ArmoryDestinationKeys.Ship(location.Ship.Id)
            : location.Region != null
                ? ArmoryDestinationKeys.Region(location.Region.Id)
                : ArmoryDestinationKeys.Default;

    private IEnumerable<Ship> PlayerShips() =>
        Force.Fleet?.TaskForces.SelectMany(taskForce => taskForce.Ships) ?? [];

    private string DutyStatus(PlayerSoldier soldier)
    {
        if (soldier.CurrentOrder != null) return "Assigned to an operation";
        DutyReadinessEvaluation readiness = DutyReadinessService.Evaluate(
            soldier,
            doctrine: Force.Army?.ChapterOperationalDoctrine,
            recruitmentProgram: Force.RecruitmentProgram);
        return readiness.IsDutyReady
            ? "Duty-ready"
            : readiness.ReasonCode == DutyReadinessReasonCode.ChapterInjuryThreshold
                ? "Withheld by doctrine"
                : "Physically unavailable";
    }

    private static string Describe(CampaignLocation location)
    {
        if (location?.Region != null)
        {
            return location.Region.Planet == null
                ? location.Region.Name
                : $"{location.Region.Name}, {location.Region.Planet.Name}";
        }
        return CampaignLocationService.Format(location);
    }

    private static float Tech(PlayerSoldier soldier) =>
        soldier.SoldierEvaluationHistory.LastOrDefault()?["tech"] ?? 0f;

    private bool IsTechmarineBranch(SoldierTemplate template) =>
        template != null
        && template.SpecialistType != 0
        && template.SpecialistType == Doctrine.Techmarine.SpecialistType;

    private IEnumerable<PlayerSoldier> Roster() =>
        Force?.Army?.PlayerSoldierMap?.Values.Where(soldier => soldier.AssignedSquad != null) ?? [];

    private PlayerSoldier FindSoldier(int soldierId) =>
        Force?.Army?.PlayerSoldierMap?.GetValueOrDefault(soldierId) is PlayerSoldier soldier
            && soldier.AssignedSquad != null
            ? soldier
            : null;

    // Every action and row below this is reached only once an Armory has been found, so the
    // rules, the doctrine, the date and the player force are all present from here on.
    private Squad FindArmory()
    {
        if (Doctrine == null || _currentDate == null) return null;
        int armoryTemplateId = Doctrine.Armory.Id;
        return Force?.Army?.OrderOfBattle?.GetAllSquads()
            .FirstOrDefault(squad => squad.SquadTemplate?.Id == armoryTemplateId);
    }
}
