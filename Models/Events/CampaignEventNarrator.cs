using System;
using System.Linq;
using OnlyWar.Models.Missions;

namespace OnlyWar.Models.Events
{
    public static class CampaignEventNarrator
    {
        private static readonly string[] FirstBloodVariants =
        [
            "{soldier} drew First Blood with a confirmed takedown.",
            "The first confirmed takedown of the campaign belongs to {soldier}.",
            "{soldier} opened the tally: First Blood for the Chapter."
        ];

        private static readonly string[] MilestoneVariants =
        [
            "{soldier} has reached {threshold} confirmed kills.",
            "The tally records {soldier} at the {threshold}-kill milestone.",
            "{soldier} crosses another mark: {threshold} confirmed kills."
        ];

        public static string RenderServiceRecord(
            CampaignEvent @event,
            CampaignIdentity identity = null)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            string soldier = SubjectName(@event);
            return @event.Payload switch
            {
                FirstBloodPayload first => Select(FirstBloodVariants, @event, identity)
                    .Replace("{soldier}", soldier, StringComparison.Ordinal)
                    .Replace("{total}", first.NewCumulativeTotal.ToString(), StringComparison.Ordinal),
                KillMilestonePayload milestone => Select(MilestoneVariants, @event, identity)
                    .Replace("{soldier}", soldier, StringComparison.Ordinal)
                    .Replace("{threshold}", milestone.Threshold.ToString(), StringComparison.Ordinal),
                BattleParticipationPayload participation =>
                    $"{soldier} took down {participation.EnemiesTakenDown} enemies and received "
                    + $"{participation.WoundsReceived} wounds in {Location(participation.BattleContext)}.",
                IncapacitatedPayload incapacitated =>
                    $"{soldier} was incapacitated in {Location(incapacitated.BattleContext)}"
                    + (string.IsNullOrWhiteSpace(incapacitated.CausingWeaponName)
                        ? ""
                        : $" by {incapacitated.CausingWeaponName}")
                    + (incapacitated.QualifiesAsNearDeath
                        ? $"; the crippled vital location was {incapacitated.DefiningHitLocationName}."
                        : "."),
                DeathPayload death => RenderDeath(soldier, death),
                GeneseedRecoveryPayload geneseed => RenderGeneseed(geneseed),
                LastSurvivorPayload survivor =>
                    $"{soldier} was the only brother still able to fight in "
                    + $"{Location(survivor.BattleContext)}; {survivor.KilledCount} were killed and "
                    + $"{survivor.IncapacitatedCount} were incapacitated.",
                SquadHeldAgainstOddsPayload held =>
                    $"The {SubjectName(@event)} held the field under "
                    + $"{DefensiveCommitment(held.DefensiveMissionType)} "
                    + $"after {held.KilledCount + held.IncapacitatedCount} of "
                    + $"{held.StartingSquadParticipantCount} participants became casualties.",
                MentorAssignedPayload mentor =>
                    $"{soldier} was assigned {mentor.MentorDisplayName} as his Scout mentor "
                    + $"in {mentor.ScoutSquadName}.",
                NearDeathRecoveryPayload recovery =>
                    $"{soldier} returned to deployability after a crippled "
                    + $"vital location ({recovery.DefiningVitalLocationName ?? "vital"}) in "
                    + $"{Location(recovery.BattleContext)} after {recovery.RecoveryDurationWeeks} weeks"
                    + (recovery.LesserWoundsRemain ? "; lesser wounds remain." : "."),
                BodyPartReplacementPayload replacement =>
                    $"{soldier} received a {Humanize(replacement.ReplacementMethod.ToString())} "
                    + $"replacement for {replacement.PrimaryHitLocationName}.",
                BattleResolvedPayload battle => battle.Summary,
                ChapterFoundedPayload founding =>
                    $"The {founding.ChapterName} was founded with {founding.InitialActiveStrength:N0} "
                    + $"active battle brothers. {founding.OpeningDirective}",
                LegacyChapterHistoryPayload chapter => string.Join(" ", chapter.SubEvents ?? []),
                LegacySoldierEventPayload legacy => legacy.Detail,
                _ => @event.Type.ToString()
            };
        }

        public static string RenderTurnReport(
            CampaignEvent @event,
            CampaignIdentity identity = null)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            return @event.Payload switch
            {
                FirstBloodPayload => $"FIRST BLOOD — {RenderServiceRecord(@event, identity)}",
                KillMilestonePayload => $"KILL MILESTONE — {RenderServiceRecord(@event, identity)}",
                BattleResolvedPayload battle => battle.Summary,
                ChapterFoundedPayload founding =>
                    $"The {founding.ChapterName} was founded with {founding.InitialActiveStrength:N0} "
                    + "active battle brothers.",
                LastSurvivorPayload or SquadHeldAgainstOddsPayload => RenderServiceRecord(@event, identity),
                _ => RenderServiceRecord(@event, identity)
            };
        }

        private static string RenderDeath(string soldier, DeathPayload death)
        {
            if (!string.IsNullOrWhiteSpace(death.Detail)) return death.Detail;
            if (death.Disposition == DeathDisposition.NonBattleProcedural)
                return $"{soldier} died during a medical procedure.";
            string location = Location(death.BattleContext);
            return death.Disposition == DeathDisposition.BodyLeftPresumedDead
                ? $"{soldier} fell in battle at {location} and was left on the field; presumed dead, body not recovered."
                : string.IsNullOrWhiteSpace(death.CausingWeaponName)
                    ? $"{soldier} was killed in battle at {location}."
                    : $"{soldier} was killed in battle at {location} by a {death.CausingWeaponName}.";
        }

        private static string RenderGeneseed(GeneseedRecoveryPayload geneseed) =>
            geneseed.Result switch
            {
                GeneseedRecoveryOutcome.Recovered =>
                    $"Gene-seed recovered (purity {(geneseed.Purity ?? 0):P0}).",
                GeneseedRecoveryOutcome.Destroyed => "Gene-seed destroyed with the body.",
                GeneseedRecoveryOutcome.Lost => "Gene-seed lost with the body.",
                _ => "Gene-seed immature and unrecoverable."
            };

        private static string Location(BattleEventContextSnapshot context) =>
            string.IsNullOrWhiteSpace(context?.LocationName) ? "the battle" : context.LocationName;

        private static string Humanize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return value switch
            {
                "DefenseInDepth" => "defensive commitment",
                "ShowOfForce" => "show of force",
                "LastStand" => "last stand",
                "VatGrown" => "vat-grown",
                "Cybernetic" => "cybernetic",
                _ => string.Join(" ", value
                    .Replace('_', ' ')
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            };
        }

        private static string DefensiveCommitment(MissionType? missionType) => missionType switch
        {
            MissionType.DefenseInDepth => "a defensive commitment",
            MissionType.Fortify => "a fortify order",
            MissionType.LastStand => "a last-stand order",
            MissionType.ShowOfForce => "a show-of-force order",
            _ => "a defensive commitment"
        };

        private static string Select(
            string[] variants,
            CampaignEvent @event,
            CampaignIdentity identity)
        {
            int index = NarrativeVariantSelector.SelectVariant(
                identity ?? CampaignIdentity.Empty,
                @event.Id,
                @event.Type == CampaignEventType.FirstBlood ? "service/first-blood" : "service/kill-milestone",
                1,
                variants.Length);
            return variants[index];
        }

        private static string SubjectName(CampaignEvent @event) =>
            @event.Entities.FirstOrDefault(entity => entity.Role == CampaignEventEntityRole.Subject)
                ?.DisplayNameSnapshot ?? "A battle-brother";
    }
}
