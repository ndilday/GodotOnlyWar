using System;
using System.Linq;
using OnlyWar.Models.Missions;

namespace OnlyWar.Models.Events
{
    public static class CampaignEventNarrator
    {
        public const string ChapterInternalNarratorKey = "chapter-internal";
        public const string OperationalNarratorKey = "operational-turn-report";
        public const string GovernorNarratorKey = "governor";
        public const string InquisitionNarratorKey = "inquisition";
        public const string BattlefleetNarratorKey = "battlefleet";
        public const string AstartesChapterNarratorKey = "astartes-chapter";
        public const string ArchivalAnnotationNarratorKey = "archival-annotation";
        public const int CurrentVersion = 2;

        private static readonly string[] FirstBloodVariants =
        [
            "{soldier} drew First Blood with a confirmed takedown.",
            "The first confirmed takedown of the campaign belongs to {soldier}.",
            "{soldier} opened the tally: First Blood for the Chapter.",
            "Campaign records credit {soldier} with the Chapter's first confirmed kill.",
            "First Blood was entered under {soldier}'s name."
        ];

        private static readonly string[] MilestoneVariants =
        [
            "{soldier} has reached {threshold} confirmed kills.",
            "The tally records {soldier} at the {threshold}-kill milestone.",
            "{soldier} crosses another mark: {threshold} confirmed kills.",
            "Service tallies place {soldier} at {threshold} confirmed kills.",
            "The {threshold}th confirmed kill was added to {soldier}'s record."
        ];

        private static readonly string[] WorldSavedChronicleVariants =
        [
            "{planet} returned to Imperial dominion. {credit}",
            "Imperial rule was restored upon {planet}. {credit}"
        ];

        private static readonly string[] WorldLostChronicleVariants =
        [
            "{planet} passed from Imperial dominion. Its name was entered among the worlds whose loss remains a debt upon the Imperium.",
            "Imperial dominion ended upon {planet}. The world remains in the annals as a loss not yet answered."
        ];
        private static readonly string[] FoundingChronicleVariants =
        [
            "The {chapter} entered the rolls of war with {strength} active battle brothers under {master}. {authority} charged the Chapter: {directive}",
            "With {strength} active battle brothers, the {chapter} was entered into the Imperial record under {master}. Its first charge came from {authority}: {directive}"
        ];
        private static readonly string[] CultChronicleVariants =
        [
            "Beneath {planet}, the Chapter exposed {faction}. What had worn the face of obedience was named, and the hidden war began in the light.",
            "The concealed hand of {faction} was uncovered upon {planet}. Its false obedience ended when the Chapter brought its hidden war to light."
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
                SquadLeaderUnavailablePayload unavailable =>
                    $"{soldier}, leader of {unavailable.SquadName}, became unavailable for deployment.",
                WorldControlChangedPayload world => world.EventType == CampaignEventType.WorldSaved
                    ? $"{world.PlanetName} returned to Imperial control."
                    : $"{world.PlanetName} passed from Imperial control.",
                HiddenCultRevealedPayload cult =>
                    $"The hidden presence of {cult.FactionName} was exposed on {cult.PlanetName}.",
                FactionIntelEventPayload intel => RenderFactionIntel(@event, intel),
                FactionRelationshipEventPayload relationship =>
                    $"Relations changed from {Humanize(relationship.PreviousStance.ToString())} to "
                    + $"{Humanize(relationship.CurrentStance.ToString())}.",
                BattleResolvedPayload battle => battle.Summary,
                ChapterFoundedPayload founding =>
                    $"The {founding.ChapterName} was founded with {founding.InitialActiveStrength:N0} "
                    + $"active battle brothers. {founding.OpeningDirective}",
                LegacyChapterHistoryPayload chapter => string.Join(" ", chapter.SubEvents ?? []),
                LegacySoldierEventPayload legacy => legacy.Detail,
                _ => throw new NotSupportedException(
                    $"No service-record renderer exists for {@event.Type} payload {@event.Payload.GetType().Name}.")
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
                SquadLeaderUnavailablePayload unavailable =>
                    $"COMMAND DISRUPTION — {SubjectName(@event)} cannot deploy; "
                    + $"{unavailable.SquadName} requires an available leader before its next commitment.",
                WorldControlChangedPayload world => world.EventType == CampaignEventType.WorldSaved
                    ? $"WORLD RESTORED — {world.PlanetName} returned to Imperial control after an episode "
                        + $"beginning in week {world.EpisodeStartedWeek}."
                    : $"WORLD LOST — {world.PlanetName} is now controlled by faction "
                        + $"{world.CurrentControllingFactionId}; the episode began in week {world.EpisodeStartedWeek}.",
                HiddenCultRevealedPayload cult =>
                    $"CULT REVEALED — {cult.FactionName} has been publicly exposed on {cult.PlanetName}.",
                FactionIntelEventPayload or FactionRelationshipEventPayload => RenderServiceRecord(@event, identity),
                BattleParticipationPayload or IncapacitatedPayload or DeathPayload
                    or GeneseedRecoveryPayload or NearDeathRecoveryPayload
                    or BodyPartReplacementPayload or MentorAssignedPayload
                    or LegacySoldierEventPayload or LegacyChapterHistoryPayload =>
                    RenderServiceRecord(@event, identity),
                _ => throw new NotSupportedException(
                    $"No Turn Report renderer exists for {@event.Type} payload {@event.Payload.GetType().Name}.")
            };
        }

        public static string RenderCommandBrief(CampaignEvent @event, CampaignIdentity identity = null) =>
            @event?.Payload switch
            {
                SquadLeaderUnavailablePayload unavailable =>
                    $"Replace {SubjectName(@event)} or revise the orders for {unavailable.SquadName}; "
                    + "its leader is not deployable.",
                WorldControlChangedPayload or HiddenCultRevealedPayload or FactionIntelEventPayload
                    or FactionRelationshipEventPayload => RenderTurnReport(@event, identity),
                null => throw new ArgumentNullException(nameof(@event)),
                _ => RenderTurnReport(@event, identity)
            };

        public static string RenderChronicle(CampaignEvent @event, CampaignIdentity identity = null)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            return @event.Payload switch
            {
                ChapterFoundedPayload founding =>
                    SelectFamily(FoundingChronicleVariants, @event, identity, "chronicle/founding")
                        .Replace("{chapter}", founding.ChapterName, StringComparison.Ordinal)
                        .Replace("{strength}", founding.InitialActiveStrength.ToString("N0"), StringComparison.Ordinal)
                        .Replace("{master}", founding.ChapterMasterName, StringComparison.Ordinal)
                        .Replace("{authority}", founding.OpeningAuthorityName, StringComparison.Ordinal)
                        .Replace("{directive}", founding.OpeningDirective, StringComparison.Ordinal),
                WorldControlChangedPayload world when world.EventType == CampaignEventType.WorldSaved =>
                    SelectFamily(WorldSavedChronicleVariants, @event, identity, "chronicle/world-saved")
                        .Replace("{planet}", world.PlanetName, StringComparison.Ordinal)
                        .Replace("{credit}", world.ChapterParticipated
                            ? "The Chapter stood among the forces that restored it, and its service there was entered in the annals."
                            : "Imperial forces achieved its restoration; the annals claim no greater part for the Chapter.",
                            StringComparison.Ordinal),
                WorldControlChangedPayload world =>
                    SelectFamily(WorldLostChronicleVariants, @event, identity, "chronicle/world-lost")
                        .Replace("{planet}", world.PlanetName, StringComparison.Ordinal),
                HiddenCultRevealedPayload cult =>
                    SelectFamily(CultChronicleVariants, @event, identity, "chronicle/cult-revealed")
                        .Replace("{planet}", cult.PlanetName, StringComparison.Ordinal)
                        .Replace("{faction}", cult.FactionName, StringComparison.Ordinal),
                FactionIntelEventPayload intel when intel.EventType == CampaignEventType.FactionFirstContact =>
                    $"The Chapter first learned the measure of {EntityName(@event, CampaignEntityKind.Faction, "the enemy")}. "
                    + "Their presence was entered among the threats to the sector.",
                FactionIntelEventPayload intel =>
                    RenderFactionIntel(@event, intel) + " The finding was entered in the Chapter's record.",
                FactionRelationshipEventPayload relationship =>
                    $"The Chapter recorded the change from {Humanize(relationship.PreviousStance.ToString())} "
                    + $"to {Humanize(relationship.CurrentStance.ToString())}; its duties would be judged accordingly.",
                FirstBloodPayload or KillMilestonePayload or LastSurvivorPayload
                    or SquadHeldAgainstOddsPayload or BattleResolvedPayload
                    or LegacyChapterHistoryPayload or LegacySoldierEventPayload
                    or BattleParticipationPayload or IncapacitatedPayload
                    or GeneseedRecoveryPayload or MentorAssignedPayload
                    or NearDeathRecoveryPayload or BodyPartReplacementPayload
                    or SquadLeaderUnavailablePayload => RenderServiceRecord(@event, identity),
                DeathPayload death => RenderDeath(SubjectName(@event), death),
                _ => throw new NotSupportedException(
                    $"No Chronicle renderer exists for {@event.Type} payload {@event.Payload.GetType().Name}.")
            };
        }

        public static string RenderEulogy(CampaignEvent deathEvent, CampaignEvent geneseedEvent,
            CampaignIdentity identity = null)
        {
            if (deathEvent?.Payload is not DeathPayload death)
                throw new ArgumentException("A typed death event is required.", nameof(deathEvent));
            if (geneseedEvent?.Payload is not GeneseedRecoveryPayload geneseed
                || geneseed.SourceDeathEventId != deathEvent.Id)
                throw new ArgumentException("A correlated gene-seed outcome is required.", nameof(geneseedEvent));
            string soldier = SubjectName(deathEvent);
            if (!string.IsNullOrWhiteSpace(death.SoldierTemplateName)
                && soldier.IndexOf(death.SoldierTemplateName, StringComparison.OrdinalIgnoreCase) < 0)
                soldier = $"{death.SoldierTemplateName} {soldier}";
            int serviceWeeks = Math.Max(0, deathEvent.OccurredWeek - death.ServiceStartWeek);
            int years = serviceWeeks / 52;
            string manner = RenderDeath(soldier, death).TrimEnd('.');
            string duty = !string.IsNullOrWhiteSpace(death.BattleContext?.OrderName)
                ? $" He fell while carrying out the order {death.BattleContext.OrderName}."
                : death.BattleContext?.MissionType != null
                    ? $" He fell during {Humanize(death.BattleContext.MissionType.Value.ToString())}."
                    : string.Empty;
            string service = $" He had served for {years} {(years == 1 ? "year" : "years")} and carried "
                + $"{death.FinalConfirmedKillCount} confirmed kills upon his record.";
            string seed = geneseed.Result switch
            {
                GeneseedRecoveryOutcome.Recovered => " His gene-seed was recovered, and his service continues in those who follow.",
                GeneseedRecoveryOutcome.Destroyed => " His gene-seed was destroyed with him, compounding the Chapter's loss.",
                GeneseedRecoveryOutcome.Lost => " His gene-seed was lost with his body, compounding the Chapter's loss.",
                _ => " His gene-seed was immature and could not be recovered."
            };
            bool serviceFirst = NarrativeVariantSelector.SelectVariant(
                identity ?? CampaignIdentity.Empty,
                deathEvent.Id,
                "chronicle/eulogy",
                CurrentVersion,
                2) == 1;
            return serviceFirst
                ? $"{soldier} had served for {years} {(years == 1 ? "year" : "years")} and carried "
                    + $"{death.FinalConfirmedKillCount} confirmed kills upon his record. {manner}." + duty + seed
                : manner + "." + duty + service + seed;
        }

        public static string RenderContinuityCallback(CampaignEvent callback, CampaignEvent anchor)
        {
            if (callback == null || anchor == null) throw new ArgumentNullException();
            string subject = SubjectName(anchor);
            return callback.Payload switch
            {
                MentorAssignedPayload mentor =>
                    $"{subject} had once been entrusted to {mentor.MentorDisplayName} during his service in {mentor.ScoutSquadName}.",
                NearDeathRecoveryPayload recovery =>
                    $"He had returned from near death {recovery.RecoveryDurationWeeks} weeks after an earlier wound.",
                KillMilestonePayload milestone =>
                    $"His earlier service had already carried him beyond {milestone.Threshold} confirmed kills.",
                FirstBloodPayload => $"He had drawn the Chapter's first blood earlier in the campaign.",
                _ => $"This service recalled an earlier entry concerning {SubjectName(callback)}."
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
                    ? $"{soldier} was killed in battle at {location}"
                        + (string.IsNullOrWhiteSpace(death.OpposingFactionName)
                            ? "."
                            : $" by {death.OpposingFactionName}.")
                    : $"{soldier} was killed in battle at {location}"
                        + (string.IsNullOrWhiteSpace(death.OpposingFactionName)
                            ? $" by a {death.CausingWeaponName}."
                            : $" by {death.OpposingFactionName}, struck with a {death.CausingWeaponName}.");
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

        private static string RenderFactionIntel(CampaignEvent @event, FactionIntelEventPayload intel)
        {
            string faction = EntityName(@event, CampaignEntityKind.Faction, $"Faction {intel.TargetFactionId}");
            string planet = EntityName(@event, CampaignEntityKind.Planet, $"Planet {intel.PlanetId}");
            return intel.EventType switch
            {
                CampaignEventType.FactionPresenceConfirmed => $"The presence of {faction} was confirmed on {planet}.",
                CampaignEventType.FactionPresenceLocated => $"The position of {faction} was located on {planet}.",
                CampaignEventType.FactionPresenceDisproven => $"The reported presence of {faction} on {planet} was disproven.",
                CampaignEventType.FactionFirstContact => $"The Chapter made first contact with {faction} on {planet}.",
                _ => throw new NotSupportedException($"No faction-intelligence renderer exists for {intel.EventType}.")
            };
        }

        private static string EntityName(CampaignEvent @event, CampaignEntityKind kind, string fallback) =>
            @event.Entities.FirstOrDefault(entity => entity.Kind == kind
                && entity.Role is CampaignEventEntityRole.Subject or CampaignEventEntityRole.Location)
                ?.DisplayNameSnapshot ?? fallback;

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

        private static string SelectFamily(string[] variants, CampaignEvent @event,
            CampaignIdentity identity, string family)
        {
            int index = NarrativeVariantSelector.SelectVariant(identity ?? CampaignIdentity.Empty,
                @event.Id, family, CurrentVersion, variants.Length);
            return variants[index];
        }

        public static int GetAuthoredVariantCount(CampaignEventType type, string surface) =>
            (type, surface) switch
            {
                (CampaignEventType.FirstBlood, "service") => FirstBloodVariants.Length,
                (CampaignEventType.KillMilestone, "service") => MilestoneVariants.Length,
                (CampaignEventType.WorldSaved, "chronicle") => WorldSavedChronicleVariants.Length,
                (CampaignEventType.WorldLost, "chronicle") => WorldLostChronicleVariants.Length,
                (CampaignEventType.ChapterFounded, "chronicle") => FoundingChronicleVariants.Length,
                (CampaignEventType.HiddenCultRevealed, "chronicle") => CultChronicleVariants.Length,
                (CampaignEventType.Death, "eulogy") => 2,
                _ => 1
            };

        private static string SubjectName(CampaignEvent @event) =>
            @event.Entities.FirstOrDefault(entity => entity.Role == CampaignEventEntityRole.Subject)
                ?.DisplayNameSnapshot ?? "A battle-brother";
    }
}
