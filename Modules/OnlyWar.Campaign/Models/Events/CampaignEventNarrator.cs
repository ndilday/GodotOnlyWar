using System;
using System.Linq;
using OnlyWar.Domain.Missions;

namespace OnlyWar.Campaign.Events
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
        // Sector dispatches (PRD §4.19): one voice, an unnamed aide who reports to the Chapter
        // Master. He reports what happened and does not advise.
        public const string ChapterMasterAideNarratorKey = "chapter-master-aide";
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
            "The {chapter} entered the rolls of war with {strength} active battle brothers and scouts under Chapter Master {master}. {authority} charged the Chapter: {directive}",
            "With {strength} active battle brothers, the {chapter} was entered into the Imperial record under {master}. Its first charge came from {authority}: {directive}"
        ];
        private static readonly string[] CultChronicleVariants =
        [
            "Beneath {planet}, the Chapter exposed {faction}. What had worn the face of obedience was named, and the hidden war began in the light.",
            "The concealed hand of {faction} was uncovered upon {planet}. Its false obedience ended when the Chapter brought its hidden war to light."
        ];

        private static readonly string[] DispatchWorldSavedVariants =
        [
            "My lord, word from {planet}: Imperial forces have restored the Emperor's rule there.",
            "My lord, {planet} is again held in the Emperor's name. Its own defenders retook it."
        ];

        private static readonly string[] DispatchWorldLostVariants =
        [
            "My lord, {planet} has fallen. {faction} now hold it.",
            "My lord, I must report the loss of {planet}. The world is in the hands of {faction}."
        ];

        private static readonly string[] DispatchSecessionVariants =
        [
            "My lord, {planet} has seceded from the Imperium. {faction} now rule it.",
            "My lord, {planet} has broken with the Imperium. The rebels hold the world."
        ];

        private static readonly string[] DispatchWorldInDangerVariants =
        [
            "My lord, {planet} is in danger. The enemy holds ground openly in {hostile} of its {regions} regions.",
            "My lord, reports from {planet} show open hostile presence in {hostile} of {regions} regions. The world is in danger."
        ];

        private static readonly string[] DispatchRevoltCrushedVariants =
        [
            "My lord, the revolt on {planet} has been put down. No rebels stand openly there now.",
            "My lord, {planet} is quiet again. The rebellion there has ended."
        ];

        private static readonly string[] DispatchInvasionForceFormedVariants =
        [
            "My lord, {faction} on {planet} have gathered into one host under a single warlord.",
            "My lord, a warlord of {faction} has risen on {planet}, and a great host follows him."
        ];

        private static readonly string[] DispatchSwarmReachedWorldVariants =
        [
            "My lord, the swarm has reached {planet}. {faction} now show themselves openly there.",
            "My lord, {faction} have appeared on {planet}. Until this week the world was free of them."
        ];

        private static readonly string[] DispatchSectorLordSuccessionVariants =
        [
            "My lord, {lord} now holds the seat of Sector Lord on {planet}, in succession to {previous}.",
            "My lord, the seat of Sector Lord on {planet} has passed from {previous} to {lord}."
        ];

        private static readonly string[] DispatchSectorLordMovedVariants =
        [
            "My lord, the seat of Sector Lord is now on {planet}. {lord} holds it.",
            "My lord, the sector is now governed from {planet}. {lord} is Sector Lord."
        ];

        /// <summary>The Turn Report title of a dispatch.</summary>
        public static string RenderDispatchTitle(SectorDispatchPayload dispatch) =>
            dispatch?.Kind switch
            {
                SectorDispatchKind.WorldSavedWithoutChapter => "World Restored",
                SectorDispatchKind.WorldLost => dispatch.IsSecession ? "World Seceded" : "World Lost",
                SectorDispatchKind.WorldInDanger => "World in Danger",
                SectorDispatchKind.RevoltCrushed => "Revolt Crushed",
                SectorDispatchKind.InvasionForceFormed => "Warlord Risen",
                SectorDispatchKind.SwarmReachedWorld => "Swarm Sighted",
                SectorDispatchKind.NewSectorLord => "New Sector Lord",
                null => throw new ArgumentNullException(nameof(dispatch)),
                _ => "Dispatch"
            };

        /// <summary>
        /// The aide's report: the fact, then the Chapter's part. The variant is selected from
        /// the event id and the campaign identity, so the text is the same every time it is shown.
        /// </summary>
        public static string RenderDispatch(
            CampaignEvent @event,
            SectorDispatchPayload dispatch,
            CampaignIdentity identity = null)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            if (dispatch == null) throw new ArgumentNullException(nameof(dispatch));
            (string[] variants, string family) = dispatch.Kind switch
            {
                SectorDispatchKind.WorldSavedWithoutChapter =>
                    (DispatchWorldSavedVariants, "dispatch/world-saved"),
                SectorDispatchKind.WorldLost when dispatch.IsSecession =>
                    (DispatchSecessionVariants, "dispatch/world-seceded"),
                SectorDispatchKind.WorldLost => (DispatchWorldLostVariants, "dispatch/world-lost"),
                SectorDispatchKind.WorldInDanger =>
                    (DispatchWorldInDangerVariants, "dispatch/world-in-danger"),
                SectorDispatchKind.RevoltCrushed =>
                    (DispatchRevoltCrushedVariants, "dispatch/revolt-crushed"),
                SectorDispatchKind.InvasionForceFormed =>
                    (DispatchInvasionForceFormedVariants, "dispatch/invasion-force-formed"),
                SectorDispatchKind.SwarmReachedWorld =>
                    (DispatchSwarmReachedWorldVariants, "dispatch/swarm-reached-world"),
                SectorDispatchKind.NewSectorLord when dispatch.CapitalMoved
                    || string.IsNullOrWhiteSpace(dispatch.PreviousSectorLordName) =>
                    (DispatchSectorLordMovedVariants, "dispatch/sector-lord-moved"),
                SectorDispatchKind.NewSectorLord =>
                    (DispatchSectorLordSuccessionVariants, "dispatch/sector-lord-succession"),
                _ => throw new NotSupportedException($"No dispatch renderer exists for {dispatch.Kind}.")
            };
            string text = SelectFamily(variants, @event, identity, family)
                .Replace("{planet}", dispatch.PlanetName ?? "an unnamed world", StringComparison.Ordinal)
                .Replace("{faction}", dispatch.FactionName ?? "the enemy", StringComparison.Ordinal)
                .Replace("{hostile}", dispatch.HostileRegionCount.ToString(), StringComparison.Ordinal)
                .Replace("{regions}", dispatch.RegionCount.ToString(), StringComparison.Ordinal)
                .Replace("{lord}", dispatch.SectorLordName ?? "a new governor", StringComparison.Ordinal)
                .Replace("{previous}", dispatch.PreviousSectorLordName ?? "the former Sector Lord",
                    StringComparison.Ordinal);
            string part = RenderChapterPart(dispatch);
            return string.IsNullOrEmpty(part) ? text : $"{text} {part}";
        }

        private static string RenderChapterPart(SectorDispatchPayload dispatch)
        {
            string subsector = string.IsNullOrWhiteSpace(dispatch.SubsectorName)
                ? "that part of the sector"
                : $"the {dispatch.SubsectorName}";
            return dispatch.ChapterPart switch
            {
                SectorDispatchChapterPart.TookPart => "Brothers of the Chapter fought there.",
                SectorDispatchChapterPart.PresentNotEngaged =>
                    $"Brothers of {JoinFormations(dispatch.PresentFormationNames)} stood in {subsector}; "
                    + "they were not committed.",
                SectorDispatchChapterPart.Absent => $"No brothers of the Chapter were in {subsector}.",
                _ => string.Empty
            };
        }

        private static string JoinFormations(System.Collections.Generic.IReadOnlyList<string> names)
        {
            string[] formations = (names ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => $"the {name}")
                .ToArray();
            return formations.Length switch
            {
                0 => "the Chapter",
                1 => formations[0],
                _ => $"{string.Join(", ", formations[..^1])} and {formations[^1]}"
            };
        }

        public static string RenderServiceRecord(
            CampaignEvent @event,
            CampaignIdentity identity = null)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            string soldier = SubjectName(@event);
            return @event.Payload switch
            {
                SectorDispatchPayload dispatch => RenderDispatch(@event, dispatch, identity),
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
                    + (!incapacitated.QualifiesAsNearDeath
                        ? "."
                        : incapacitated.DefiningLocationRequiresOrganReplacement
                            ? $"; the {incapacitated.DefiningHitLocationName} needs an organ replacement."
                            : $"; the crippled vital location was {incapacitated.DefiningHitLocationName}."),
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
                SectorDispatchPayload dispatch => RenderDispatch(@event, dispatch, identity),
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
                    or SquadLeaderUnavailablePayload or SectorDispatchPayload =>
                    RenderServiceRecord(@event, identity),
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

        private static string SubjectName(CampaignEvent @event) =>
            @event.Entities.FirstOrDefault(entity => entity.Role == CampaignEventEntityRole.Subject)
                ?.DisplayNameSnapshot ?? "A battle-brother";
    }
}
