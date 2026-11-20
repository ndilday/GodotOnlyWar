using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.Mandates;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Mandates
{
    /// <summary>
    /// The mandate lifecycle (TDD §6.15 *Lifecycle*): swear, abandon, resolve, and
    /// renounce the Chapter World. Every result writes its Chronicle event and its legacy entry
    /// here, so the two cannot disagree (§6.1).
    /// </summary>
    public sealed class MandateService
    {
        private readonly Sector _sector;
        private readonly GameRulesData _rules;
        private readonly int _currentWeek;

        public MandateService(Sector sector, GameRulesData rules, Date currentDate)
        {
            _sector = sector ?? throw new ArgumentNullException(nameof(sector));
            _rules = rules;
            _currentWeek = currentDate?.GetTotalWeeks() ?? throw new ArgumentNullException(nameof(currentDate));
        }

        private PlayerForce Force => _sector.PlayerForce;
        private ChapterMandates Mandates => Force.Mandates;

        public IReadOnlyList<MandateOption> ListOptions() =>
            Mandates.Active == null ? MandateCatalog.ListEligible(_sector, _rules) : [];

        /// <summary>
        /// Opens the campaign's record at founding: the founding Chapter Master's tenure, and the
        /// Promised World as the first mandate. It needs no sector, because the founding runs
        /// before the sector is assembled.
        /// </summary>
        public static void BeginCampaign(
            PlayerForce force,
            Date foundingDate,
            int? chapterMasterId,
            string chapterMasterName,
            int promisedPlanetId,
            string promisedPlanetName)
        {
            if (force == null) throw new ArgumentNullException(nameof(force));
            if (foundingDate == null) throw new ArgumentNullException(nameof(foundingDate));
            int week = foundingDate.GetTotalWeeks();
            ChapterMandates mandates = force.Mandates;
            if (chapterMasterId is int masterId && mandates.CurrentTenure == null
                && !string.IsNullOrWhiteSpace(chapterMasterName))
            {
                mandates.BeginTenure(masterId, chapterMasterName, week);
            }
            if (mandates.All.Count > 0) return;
            string name = string.IsNullOrWhiteSpace(promisedPlanetName)
                ? $"Planet {promisedPlanetId}"
                : promisedPlanetName;
            Mandate mandate = PromisedWorldMandate(mandates.NextMandateId, promisedPlanetId, name, week);
            mandates.Add(mandate);
            RecordOath(force, mandate, week,
            [
                ChapterEntity(force),
                new CampaignEventEntityRef(CampaignEntityKind.Planet, promisedPlanetId,
                    CampaignEventEntityRole.Location, name)
            ]);
        }

        /// <summary>
        /// A safety net for a sector that skipped <see cref="BeginCampaign"/>: it creates the
        /// Promised World mandate only for a pending scenario with no mandate yet.
        /// </summary>
        public Mandate EnsurePromisedWorld()
        {
            CampaignScenario scenario = _sector.Scenario;
            if (scenario is not { State: ObjectiveState.Pending } || Mandates.All.Count > 0) return null;
            Planet promised = _sector.Planets.GetValueOrDefault(scenario.PromisedPlanetId);
            string name = promised?.Name ?? $"Planet {scenario.PromisedPlanetId}";
            Mandate mandate = PromisedWorldMandate(Mandates.NextMandateId, scenario.PromisedPlanetId, name, _currentWeek);
            Mandates.Add(mandate);
            RecordOath(mandate);
            return mandate;
        }

        private static Mandate PromisedWorldMandate(int id, int planetId, string name, int week) => new(
            id,
            MandateFamily.EstablishChapterWorld,
            $"Liberate {name}, the Promised World",
            name,
            week,
            null,
            MandateConstants.EstablishLegacy,
            planetIds: [planetId],
            isPromisedWorld: true);

        public Mandate Swear(string optionKey)
        {
            if (Mandates.Active != null)
                throw new InvalidOperationException("A mandate is already active.");
            MandateOption option = ListOptions().FirstOrDefault(candidate =>
                string.Equals(candidate.Key, optionKey, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("That mandate is no longer eligible.");
            int baselinePledge = Force.Pledges.Count == 0 ? -1 : Force.Pledges.Max(pledge => pledge.Id);
            Mandate mandate = new(
                Mandates.NextMandateId,
                option.Family,
                option.Title,
                option.SubjectName,
                _currentWeek,
                _currentWeek + option.DeadlineWeeks,
                option.LegacyValue,
                option.PlanetIds,
                option.UnitId,
                option.FactionId,
                option.InvasionForceId,
                option.ThreatKind,
                baselinePledge);
            Mandates.Add(mandate);
            RecordOath(mandate);
            return mandate;
        }

        /// <summary>
        /// The player gives up the active mandate. Abandoning is a failure. Abandoning the
        /// Promised World lapses the scenario with the lapse's opinion penalty.
        /// </summary>
        public Mandate Abandon()
        {
            Mandate active = Mandates.Active
                ?? throw new InvalidOperationException("No mandate is active.");
            if (active.IsPromisedWorld)
            {
                ScenarioTurnProcessor.Abandon(_sector, _rules);
            }
            else if (active.Family == MandateFamily.EstablishChapterWorld)
            {
                ChangeSectorLordOpinion(-MandateConstants.EstablishSectorLordOpinionPenalty);
            }
            Resolve(active, MandateState.Abandoned, "The Chapter Master set the vow aside.");
            return active;
        }

        /// <summary>
        /// The player gives up the Chapter World. The simulation never does this on its own.
        /// An active Secure mandate fails with it.
        /// </summary>
        public bool RenounceChapterWorld()
        {
            if (Force.HomeWorldPlanetId is not int homeId) return false;
            Planet home = _sector.Planets.GetValueOrDefault(homeId);
            string name = home?.Name ?? $"Planet {homeId}";
            Force.HomeWorldPlanetId = null;
            Force.GetCampaignEventRecorder().Record(new CampaignEventCandidate(
                CampaignEventType.ChapterWorldRenounced,
                _currentWeek,
                _currentWeek,
                null,
                $"chapter-world/{homeId}/renounced/{_currentWeek}",
                1,
                new ChapterWorldRenouncedPayload(homeId, name),
                [
                    ChapterEntity(),
                    new CampaignEventEntityRef(CampaignEntityKind.Planet, homeId,
                        CampaignEventEntityRole.Location, name)
                ]));
            Mandate active = Mandates.Active;
            if (active is { Family: MandateFamily.SecureChapterWorld })
            {
                Resolve(active, MandateState.Failed, $"The Chapter renounced {name}.");
            }
            return true;
        }

        /// <summary>Resolves a mandate: its state, its Chronicle event and its legacy entry.</summary>
        public void Resolve(Mandate mandate, MandateState state, string reason)
        {
            if (mandate == null) throw new ArgumentNullException(nameof(mandate));
            mandate.Resolve(state, _currentWeek);
            int amount = state == MandateState.Fulfilled
                ? mandate.LegacyValue
                : MandateConstants.FailureLegacy(mandate.LegacyValue);
            CampaignEvent @event = Force.GetCampaignEventRecorder().Record(new CampaignEventCandidate(
                CampaignEventType.MandateResolved,
                _currentWeek,
                _currentWeek,
                null,
                $"mandate/{mandate.Id}/resolved",
                1,
                new MandateResolvedPayload(
                    mandate.Id,
                    mandate.Family,
                    mandate.Title,
                    mandate.SubjectName,
                    state,
                    amount,
                    reason,
                    mandate.IsPromisedWorld),
                MandateEntities(mandate)));
            Mandates.AddLegacy(
                _currentWeek,
                amount,
                state == MandateState.Fulfilled
                    ? LegacySourceKind.MandateFulfilled
                    : LegacySourceKind.MandateFailed,
                $"mandate/{mandate.Id}",
                @event?.Id,
                state switch
                {
                    MandateState.Fulfilled => $"Vow kept: {mandate.Title}.",
                    MandateState.Abandoned => $"Vow abandoned: {mandate.Title}.",
                    _ => $"Vow broken: {mandate.Title}."
                });
        }

        internal void ChangeSectorLordOpinion(float delta)
        {
            Character lord = _sector.GetSectorLord();
            if (lord != null) lord.OpinionOfPlayerForce += delta;
        }

        private void RecordOath(Mandate mandate) =>
            RecordOath(Force, mandate, _currentWeek, MandateEntities(mandate));

        private static void RecordOath(
            PlayerForce force,
            Mandate mandate,
            int recordedWeek,
            IReadOnlyList<CampaignEventEntityRef> entities)
        {
            force.GetCampaignEventRecorder().Record(new CampaignEventCandidate(
                CampaignEventType.Oath,
                mandate.StartWeek,
                Math.Max(mandate.StartWeek, recordedWeek),
                null,
                $"mandate/{mandate.Id}/oath",
                3,
                new MandateOathPayload(
                    mandate.Id,
                    mandate.Family,
                    mandate.Title,
                    mandate.SubjectName,
                    mandate.DeadlineWeek,
                    mandate.LegacyValue,
                    mandate.IsPromisedWorld,
                    force.Mandates.CurrentTenure?.SoldierName),
                entities));
        }

        private IReadOnlyList<CampaignEventEntityRef> MandateEntities(Mandate mandate)
        {
            List<CampaignEventEntityRef> entities = [ChapterEntity()];
            foreach (int planetId in mandate.PlanetIds)
            {
                Planet planet = _sector.Planets.GetValueOrDefault(planetId);
                if (planet == null) continue;
                entities.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Planet, planet.Id, CampaignEventEntityRole.Location, planet.Name));
            }
            if (mandate.FactionId is int factionId)
            {
                Faction faction = _rules?.Factions?.FirstOrDefault(candidate => candidate.Id == factionId);
                if (faction != null)
                {
                    entities.Add(new CampaignEventEntityRef(
                        CampaignEntityKind.Faction, faction.Id, CampaignEventEntityRole.Opponent, faction.Name));
                }
            }
            return entities;
        }

        private CampaignEventEntityRef ChapterEntity() => ChapterEntity(Force);

        private static CampaignEventEntityRef ChapterEntity(PlayerForce force) => new(
            CampaignEntityKind.Chapter,
            0,
            CampaignEventEntityRole.Subject,
            force.Army?.OrderOfBattle?.Name ?? force.Faction?.Name ?? "Chapter");
    }
}
