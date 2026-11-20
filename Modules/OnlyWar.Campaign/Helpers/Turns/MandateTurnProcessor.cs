using OnlyWar.Campaign.Mandates;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain;
using OnlyWar.Domain.Mandates;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Units;
using OnlyWar.Operations.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Turns
{
    /// <summary>
    /// The weekly mandate and legacy pass (TDD §6.15 *Turn step*). It runs
    /// after ScenarioTurnProcessor, once the week's simulation has settled, in this order:
    /// <list type="number">
    /// <item>bridge the Promised World to its mandate;</item>
    /// <item>write the week's legacy entries, credited to the master who held the office as the
    /// week began;</item>
    /// <item>bring Chapter Master tenure up to date;</item>
    /// <item>note companies that reached the Rebuild threshold;</item>
    /// <item>judge the active mandate.</item>
    /// </list>
    /// </summary>
    internal sealed class MandateTurnProcessor
    {
        private readonly CampaignTurnContext _turn;
        private readonly ChapterWorldGrantService _grant;

        internal MandateTurnProcessor(
            ICampaignSimulationSession session,
            IOperationsPersonnelSurface personnel,
            IOrderCommitmentSurface commitments)
            : this(CampaignTurnContext.From(session), personnel, commitments)
        {
        }

        internal MandateTurnProcessor(
            CampaignTurnContext turn,
            IOperationsPersonnelSurface personnel,
            IOrderCommitmentSurface commitments)
        {
            _turn = turn ?? throw new ArgumentNullException(nameof(turn));
            _grant = new ChapterWorldGrantService(_turn, personnel, commitments);
        }

        internal void Process(Sector sector, IEnumerable<CampaignEvent> weekEvents)
        {
            PlayerForce force = sector?.PlayerForce;
            if (force == null) return;
            int week = _turn.CurrentDate.GetTotalWeeks();
            MandateService service = new(sector, _turn.Rules, _turn.CurrentDate);

            service.EnsurePromisedWorld();
            BridgePromisedWorld(sector, service);
            LegacyService.RecordWeek(force, weekEvents?.ToList(), week);
            ChapterMasterOffice.Synchronize(force, _turn.Rules, week);
            MarkRaisedCompanies(force);

            Mandate active = force.Mandates.Active;
            if (active == null || active.IsPromisedWorld) return;
            MandateEvaluation evaluation = MandateEvaluator.Evaluate(active, sector, _turn.Rules, week, advance: true);
            switch (evaluation.Outcome)
            {
                case MandateOutcome.Fulfilled:
                    if (active.Family == MandateFamily.EstablishChapterWorld)
                    {
                        Planet world = sector.Planets[active.PlanetIds.First()];
                        _grant.Grant(sector, world);
                        service.ChangeSectorLordOpinion(MandateConstants.EstablishSectorLordOpinionReward);
                    }
                    service.Resolve(active, MandateState.Fulfilled, evaluation.Reason);
                    break;
                case MandateOutcome.Failed:
                    if (active.Family == MandateFamily.EstablishChapterWorld)
                    {
                        service.ChangeSectorLordOpinion(-MandateConstants.EstablishSectorLordOpinionPenalty);
                    }
                    service.Resolve(active, MandateState.Failed, evaluation.Reason);
                    break;
            }
        }

        private static void BridgePromisedWorld(Sector sector, MandateService service)
        {
            Mandate active = sector.PlayerForce.Mandates.Active;
            if (active is not { IsPromisedWorld: true } || sector.Scenario == null) return;
            Planet promised = sector.Planets.GetValueOrDefault(sector.Scenario.PromisedPlanetId);
            string name = promised?.Name ?? "the Promised World";
            switch (sector.Scenario.State)
            {
                case ObjectiveState.Won:
                    service.Resolve(active, MandateState.Fulfilled,
                        $"No enemy holds open ground on {name}; it was granted to the Chapter.");
                    break;
                case ObjectiveState.Lapsed:
                    service.Resolve(active, MandateState.Failed,
                        $"No Imperial force held ground on {name}, and the promise was withdrawn.");
                    break;
            }
        }

        private void MarkRaisedCompanies(PlayerForce force)
        {
            foreach (Unit company in MandateCatalog.Companies(force, _turn.Rules))
            {
                if (force.Mandates.WasCompanyRaised(company.Id)) continue;
                if (CompanyStrength.Measure(company, force).Fraction >= MandateConstants.RebuildRaisedAtFraction)
                {
                    force.Mandates.MarkCompanyRaised(company.Id);
                }
            }
        }
    }
}
