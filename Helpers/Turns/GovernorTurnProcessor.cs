using OnlyWar.Builders;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Supply;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Supply;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Owns the weekly lifecycle of planetary governors and their requests. The planet
    /// processor remains responsible for deciding when this phase runs so the established
    /// simulation and random-draw ordering stays stable.
    /// </summary>
    internal sealed class GovernorTurnProcessor
    {
        private readonly GameSession _session;
        private readonly ICollection<GovernorRequestReport> _requestReports;

        internal GovernorTurnProcessor(
            GameSession session,
            ICollection<GovernorRequestReport> requestReports = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _requestReports = requestReports;
        }

        internal void ProcessGovernor(Planet planet, PlanetFaction planetFaction)
        {
            if (AgeAndCheckForDeath(planet, planetFaction))
            {
                return;
            }

            Character governor = planetFaction.Leader;
            if (governor.ActiveRequest != null)
            {
                IRequest request = governor.ActiveRequest;
                request.ProcessTurn(_session.CurrentDate);
                if (request.Status == RequestStatus.Fulfilled)
                {
                    CreatePledge(request);
                    RetireShowOfForceMission(request);
                    Report(GovernorRequestReportKind.Fulfilled, request);
                    governor.ActiveRequest = null;
                    governor.OpinionOfPlayerForce +=
                        governor.Appreciation * (1 - governor.OpinionOfPlayerForce);
                    governor.NextRequestEligibleDate = AddWeeks(
                        _session.CurrentDate, _session.Rules.SupplyEconomyRules.RequestCooldownWeeks);
                }
                else if (request.Status == RequestStatus.Failed)
                {
                    RetireShowOfForceMission(request);
                    Report(
                        GovernorRequestReportKind.Failed,
                        request,
                        DescribeFailure(request));
                    governor.ActiveRequest = null;
                    governor.OpinionOfPlayerForce -= 0.05f / Math.Max(0.1f, governor.Patience);
                    governor.NextRequestEligibleDate = AddWeeks(
                        _session.CurrentDate, _session.Rules.SupplyEconomyRules.RequestCooldownWeeks);
                }
                else
                {
                    governor.OpinionOfPlayerForce -= 0.005f / governor.Patience;
                    SyncShowOfForceMission(request);
                }
            }
            else if (governor.OpinionOfPlayerForce > 0
                     && (governor.NextRequestEligibleDate == null
                         || _session.CurrentDate.IsAfterOrEqual(governor.NextRequestEligibleDate)))
            {
                GenerateRequest(planet, planetFaction);
            }
        }

        private bool AgeAndCheckForDeath(Planet planet, PlanetFaction planetFaction)
        {
            Character leader = planetFaction.Leader;
            if (_session.CurrentDate.Week == 1)
            {
                leader.Age++;
            }

            float ageFactor = Math.Max(0, leader.Age - 50) / 50f;
            float importanceFactor = 1f - (Math.Min(planet.Importance, 6000) / 12000f);
            float weeklyDeathChance = ageFactor * 0.002f * importanceFactor;
            if (_session.Random.GetLinearDouble() >= weeklyDeathChance)
            {
                return false;
            }

            if (leader.ActiveRequest != null)
            {
                IRequest request = leader.ActiveRequest;
                request.Fail(_session.CurrentDate);
                RetireShowOfForceMission(request);
                Report(
                    GovernorRequestReportKind.Failed,
                    request,
                    $"{leader.Name} died before the commitment was met; the petition dies with them.");
                leader.ActiveRequest = null;
            }
            List<Character> characters = _session.Sector.Characters;
            // Retain the former governor as a historical character. Resolved requests and
            // institutional pledge attribution can continue to reference them after succession.
            int newId = characters.Count == 0 ? 0 : characters.Max(c => c.Id) + 1;
            Character successor = CharacterBuilder.GenerateCharacter(newId, planetFaction.Faction);
            characters.Add(successor);
            planetFaction.Leader = successor;
            return true;
        }

        private void GenerateRequest(Planet planet, PlanetFaction planetFaction)
        {
            Faction threatFaction = FindPublicHostileFaction(planet, planetFaction);
            bool generate = threatFaction != null
                ? _session.Random.GetLinearDouble() < planetFaction.Leader.Investigation
                : _session.Random.GetLinearDouble() < planetFaction.Leader.Paranoia;

            if (!generate)
            {
                return;
            }

            SupplyEconomyRules supplyRules = _session.Rules.SupplyEconomyRules;
            // RequestGenerationRate throttles the whole petition economy. Both gates are linear in
            // the governor's traits, so scaling here changes only how often worlds petition, not
            // which governors do it (see SupplyEconomyRules.RequestGenerationRate).
            float chance = (float)supplyRules.RequestGenerationRate
                * planetFaction.Leader.Neediness
                * planetFaction.Leader.OpinionOfPlayerForce;
            if (_session.Random.GetLinearDouble() >= chance)
            {
                return;
            }

            ForceCommitmentPackage commitment = BuildCommitmentPackage(planet, threatFaction);
            (RequestSeverity severity, RequestHazard hazard) = ClassifyRequest(planet, threatFaction);
            int nominalOffer = CalculateOffer(
                planet, planetFaction.Leader, commitment, severity, hazard);
            PledgeScheduleKind scheduleKind = nominalOffer >= supplyRules.StandingMinimumOffer
                && planetFaction.Leader.OpinionOfPlayerForce >= 0.75f
                    ? PledgeScheduleKind.Standing
                    : PledgeScheduleKind.OneOff;
            int offeredAmount = scheduleKind == PledgeScheduleKind.Standing
                ? Math.Max(1, (int)Math.Round(
                    nominalOffer * supplyRules.StandingDeliveryFraction,
                    MidpointRounding.AwayFromZero))
                : nominalOffer;
            int cadenceWeeks = scheduleKind == PledgeScheduleKind.Standing
                ? supplyRules.StandingCadenceWeeks
                : 0;
            int deliveryDelayWeeks = scheduleKind == PledgeScheduleKind.Standing
                ? supplyRules.StandingCadenceWeeks
                : supplyRules.DefaultDeliveryWeeks;
            IRequest request = RequestFactory.Instance.GenerateNewRequest(
                planet,
                planetFaction.Leader,
                threatFaction,
                _session.CurrentDate,
                AddWeeks(_session.CurrentDate, _session.Rules.SupplyEconomyRules.DefaultDeadlineWeeks),
                commitment,
                offeredAmount,
                scheduleKind,
                cadenceWeeks,
                deliveryDelayWeeks,
                severity,
                hazard);
            planetFaction.Leader.ActiveRequest = request;
            _session.Sector.PlayerForce.Requests.Add(request);
            SyncShowOfForceMission(request);
            Report(GovernorRequestReportKind.Arrived, request);
        }

        /// <summary>
        /// Reconciles the Show of Force order that fulfils an effort-based request onto the target
        /// planet's capital region, so it appears in the order dialog the same way any other
        /// special mission does. Outcome-based (threat suppression) requests are fulfilled by
        /// destroying the threat and need no posted order.
        ///
        /// This runs every turn the request is open rather than once at generation, and is
        /// idempotent. Reconciling rather than posting means the mission survives anything that
        /// legitimately drops it - PruneInvalidSpecialMissions culling a stale anchor after the
        /// player's RegionFaction is rebuilt, or a save written before Show of Force existed - and
        /// re-anchors it to the live player RegionFaction instead of leaving the order dialog
        /// holding a detached one.
        /// </summary>
        private void SyncShowOfForceMission(IRequest request)
        {
            if (request.FulfillmentKind != RequestFulfillmentKind.ForceCommitment) return;
            Region capital = GetCapitalRegion(request.TargetPlanet);
            if (capital == null) return;

            RegionFaction playerRegionFaction = GetOrCreatePlayerRegionFaction(capital);
            if (playerRegionFaction == null) return;

            // A squad already holding the order keeps the mission it was assigned; re-anchoring
            // underneath it would orphan that order's target.
            bool alreadyPosted = capital.SpecialMissions.Any(mission =>
                mission.MissionType == MissionType.ShowOfForce
                && ReferenceEquals(mission.RegionFaction, playerRegionFaction));
            if (alreadyPosted) return;

            capital.SpecialMissions.RemoveAll(
                mission => mission.MissionType == MissionType.ShowOfForce);
            capital.SpecialMissions.Add(new Mission(
                MissionType.ShowOfForce,
                playerRegionFaction,
                request.Commitment.PackageCount));
        }

        /// <summary>
        /// Clears the posted Show of Force mission once the request leaves the open state, so a
        /// resolved petition stops advertising an order the player can no longer make progress on.
        /// Squads already holding the order are released by the normal end-of-turn cleanup.
        /// </summary>
        private void RetireShowOfForceMission(IRequest request)
        {
            if (request.FulfillmentKind != RequestFulfillmentKind.ForceCommitment) return;
            Region capital = GetCapitalRegion(request.TargetPlanet);
            capital?.SpecialMissions.RemoveAll(
                mission => mission.MissionType == MissionType.ShowOfForce);
        }

        internal static Region GetCapitalRegion(Planet planet) =>
            planet?.Regions?.FirstOrDefault(region => region.Id == planet.CapitalRegionId)
            ?? planet?.Regions?.FirstOrDefault();

        // Mirrors OrderAssignment.GetOrCreatePlayerRegionFaction: the Chapter can be petitioned to
        // a world it has no presence on yet, and the posted mission has to hang off a player
        // RegionFaction that exists.
        private RegionFaction GetOrCreatePlayerRegionFaction(Region region)
        {
            Faction playerFaction = _session.Sector.PlayerForce.Faction;
            if (region.RegionFactionMap.TryGetValue(
                playerFaction.Id, out RegionFaction existing))
            {
                return existing;
            }
            if (!region.Planet.PlanetFactionMap.TryGetValue(
                playerFaction.Id, out PlanetFaction playerPlanetFaction))
            {
                return null;
            }
            RegionFaction created = new(playerPlanetFaction, region);
            region.RegionFactionMap[playerFaction.Id] = created;
            return created;
        }

        private static string DescribeFailure(IRequest request)
        {
            if (request.FulfillmentKind == RequestFulfillmentKind.ThreatSuppressed
                && !request.HasPlayerResponded)
            {
                return "The danger was resolved without the Chapter; the petition is moot.";
            }
            return "The deadline passed with the commitment unmet.";
        }

        private void Report(
            GovernorRequestReportKind kind,
            IRequest request,
            string failureReason = null)
        {
            _requestReports?.Add(new GovernorRequestReport(kind, request, failureReason));
        }

        private ForceCommitmentPackage BuildCommitmentPackage(Planet planet, Faction threatFaction)
        {
            SquadTemplate reference = _session.Rules.ChapterTemplates.TacticalSquad;
            SupplyEconomyRules rules = _session.Rules.SupplyEconomyRules;
            long hostileStrength = threatFaction == null ? 0 : SumMilitaryStrength(planet, threatFaction);
            int packageCount = hostileStrength <= 0
                ? 1
                : (int)Math.Clamp(
                    (hostileStrength + reference.BattleValue - 1) / reference.BattleValue,
                    1,
                    5);
            return new ForceCommitmentPackage(
                "astartes-squad-presence",
                threatFaction == null ? "Astartes presence" : "Threat suppression force",
                "squad",
                packageCount,
                rules.DefaultServiceWeeks,
                rules.DefaultDeadlineWeeks,
                reference.BattleValue,
                ["Astartes"],
                maximumEffectivePackageCount: Math.Min(10, packageCount * 2));
        }

        private int CalculateOffer(
            Planet planet,
            Character governor,
            ForceCommitmentPackage commitment,
            RequestSeverity severity,
            RequestHazard hazard)
        {
            SupplyEconomyRules rules = _session.Rules.SupplyEconomyRules;
            decimal hazardMultiplier = rules.HazardMultipliers[hazard.ToString()];
            RequestValuationResult value = RequestValueCalculator.Calculate(
                commitment,
                rules.RequestValuation,
                rules.QualificationPremiums.Where(premium =>
                    commitment.QualificationTags.Contains(
                        premium.RequirementKey, StringComparer.OrdinalIgnoreCase)),
                hazardMultiplier);
            decimal worldMultiplier = rules.WorldRequisitionMultipliers.TryGetValue(
                planet.Template.Id, out decimal authoredWorldMultiplier)
                    ? authoredWorldMultiplier
                    : 1m;
            int worldAdjustedValue = RequestValueCalculator.RoundAndClamp(
                value.RequisitionValue * worldMultiplier,
                rules.RequestValuation.MinimumRequestValue,
                rules.RequestValuation.MaximumRequestValue);
            decimal authority = rules.AuthorityMultipliers[planet.GovernanceTier.ToString()];
            GovernorWillingness willingness = new(
                rules.DesperationMultipliers[severity.ToString()],
                rules.RelationshipBaseMultiplier
                    + rules.RelationshipOpinionScale
                    * (decimal)Math.Clamp(governor.OpinionOfPlayerForce, 0f, 1f),
                authority);
            return GovernorOfferCalculator.Calculate(
                worldAdjustedValue, willingness, rules.GovernorOffers);
        }

        private (RequestSeverity Severity, RequestHazard Hazard) ClassifyRequest(
            Planet planet,
            Faction threatFaction)
        {
            if (threatFaction == null)
                return (RequestSeverity.Concerned, RequestHazard.Routine);
            decimal ratio = CalculateThreatRatio(planet, threatFaction);
            if (ratio > 2m)
                return (RequestSeverity.Existential, RequestHazard.Extreme);
            if (ratio > 1m)
                return (RequestSeverity.Desperate, RequestHazard.Dangerous);
            return (RequestSeverity.Serious, RequestHazard.Dangerous);
        }

        private decimal CalculateThreatRatio(Planet planet, Faction threatFaction)
        {
            if (threatFaction == null) return 0m;
            long hostile = SumMilitaryStrength(planet, threatFaction);
            long defenders = SumMilitaryStrength(planet, _session.Rules.DefaultFaction);
            return hostile / (decimal)Math.Max(1, defenders);
        }

        private static long SumMilitaryStrength(Planet planet, Faction faction) =>
            planet.Regions.Sum(region =>
                region.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction presence)
                    ? presence.MilitaryStrength
                    : 0);

        private void CreatePledge(IRequest request)
        {
            int nextId = _session.Sector.PlayerForce.Pledges.Count == 0
                ? 0
                : _session.Sector.PlayerForce.Pledges.Max(pledge => pledge.Id) + 1;
            Pledge pledge = new(
                nextId,
                request.TargetPlanet.Id,
                request.Requester.Id,
                PledgePayload.Requisition(request.OfferedRequisition),
                request.OfferedScheduleKind,
                AddWeeks(_session.CurrentDate, request.OfferedDeliveryDelayWeeks),
                request.OfferedCadenceWeeks);
            _session.Sector.PlayerForce.Pledges.Add(pledge);
        }

        private static Date AddWeeks(Date date, int weeks)
        {
            Date result = new(date.Millenium, date.Year, date.Week);
            for (int i = 0; i < weeks; i++) result.IncrementWeek();
            return result;
        }

        private static Faction FindPublicHostileFaction(Planet planet, PlanetFaction planetFaction)
        {
            return planet.PlanetFactionMap.Values
                .Select(other => other.Faction)
                .FirstOrDefault(other => other.Id != planetFaction.Faction.Id
                    && planet.PlanetFactionMap[other.Id].IsPublic
                    && !other.IsDefaultFaction
                    && !other.IsPlayerFaction);
        }
    }
}
