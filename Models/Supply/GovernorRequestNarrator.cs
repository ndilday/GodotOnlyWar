using System;

namespace OnlyWar.Models.Supply
{
    /// <summary>Voiced petition kept separate from the authoritative mechanical terms.</summary>
    public sealed record GovernorRequestNarrative(string Flavor, string MechanicalSummary);

    public static class GovernorRequestNarrator
    {
        private static readonly string[] PetitionForms =
        [
            "{address}, {danger}. I ask that {chapterAction}",
            "{danger}. By my authority, I petition the Chapter: {chapterAction}",
            "Let this request stand before the Chapter. {danger}; {chapterAction}",
            "I will speak plainly, {address}: {danger}. {chapterAction}",
            "The duty of my office permits no silence. {danger}. I ask that {chapterAction}"
        ];

        public static GovernorRequestNarrative Compose(IRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string governor = request.Requester?.Name ?? "the planetary governor";
            string world = request.TargetPlanet?.Name ?? "this world";
            string address = request.Requester?.OpinionOfPlayerForce >= 0.7f
                ? "honoured brothers"
                : request.Requester?.OpinionOfPlayerForce < 0.3f
                    ? "lords of the Chapter"
                    : "my lords";
            string danger = request.ThreatFaction == null
                ? request.Severity >= RequestSeverity.Desperate
                    ? $"grave reports trouble {world}, though their source remains unconfirmed"
                    : $"reports of danger on {world} remain unconfirmed"
                : request.Severity >= RequestSeverity.Desperate
                    ? $"{world} stands in grave peril from {request.ThreatFaction.Name}"
                    : $"{request.ThreatFaction.Name} threaten {world}";
            string action = request.FulfillmentKind == RequestFulfillmentKind.ThreatSuppressed
                ? "break the threat before the appointed deadline."
                : "make your strength visible in our capital until order is restored.";
            string form = PetitionForms[Math.Abs(request.Id % PetitionForms.Length)]
                .Replace("{address}", address, StringComparison.Ordinal)
                .Replace("{danger}", danger, StringComparison.Ordinal)
                .Replace("{chapterAction}", action, StringComparison.Ordinal);
            if (request.Requester?.Severity >= 0.75f || request.Hazard == RequestHazard.Extreme)
                form += " Delay will be measured in lives.";
            else if (request.Requester?.Patience >= 0.7f)
                form += " I trust the Chapter to judge the hour wisely.";
            form = $"Governor {governor}: “{form}”";

            decimal completed = request.Commitment.ReferenceBattleValuePerPackage <= 0
                ? 0
                : (decimal)request.ProgressBattleValueTime
                    / request.Commitment.ReferenceBattleValuePerPackage;
            decimal required = request.Commitment.PackageCount * request.Commitment.ServiceWeeks;
            string deadline = request.Deadline == null
                ? "unstated"
                : $"{request.Deadline.Year:000}.M{request.Deadline.Millenium} (week {request.Deadline.Week})";
            string mechanics = $"Commitment: {request.Commitment.PackageCount} "
                + request.Commitment.DisplayUnitName
                + (request.Commitment.PackageCount == 1 ? string.Empty : "s")
                + $" for {request.Commitment.ServiceWeeks} weeks. Progress: {completed:0.#}/{required:0.#} "
                + $"squad-weeks. Deadline: {deadline}. Reward: {request.OfferedRequisition:N0} Requisition.";
            return new GovernorRequestNarrative(form, mechanics);
        }
    }
}
