using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.FactionBehaviors;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>Applies ordinary faction population growth, conversion, and garrison changes.</summary>
    internal sealed class PlanetDemographicsProcessor
    {
        private const float LogisticGrowthRate = 0.0006f;
        private const float BaselineGrowthRate = 0.0004f;
        private const float GarrisonAttritionRate = 0.001f;
        private const float GarrisonDraftRate = 0.025f;
        private const float EmergencyGarrisonDraftRate = 0.05f;
        private const float ActiveAssaultGarrisonDraftRate = 0.15f;
        private const float OverrunRemnantGarrisonArmingRate = 1.0f;

        private readonly GameSession _session;
        private readonly OrganicPopulationGrowthLedger _growthLedger;

        internal PlanetDemographicsProcessor(
            GameSession session,
            OrganicPopulationGrowthLedger growthLedger)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _growthLedger = growthLedger ?? throw new ArgumentNullException(nameof(growthLedger));
        }

        internal void ProcessRegionFaction(RegionFaction regionFaction, float pdfRatio)
        {
            Planet planet = regionFaction.Region.Planet;
            Faction controllingFaction = planet.GetControllingFaction();
            float newPop = 0;
            bool isOverrunRemnant = regionFaction.PlanetFaction.Faction.IsDefaultFaction && !regionFaction.IsPublic;
            bool isInvasionOccupiedCivilian = regionFaction.PlanetFaction.Faction.IsDefaultFaction
                && FactionCapabilities.GeneratesInvasions(controllingFaction);

            if (isInvasionOccupiedCivilian)
            {
                // Invasion-held civilians are enslaved rather than allowed to reproduce normally. The
                // decline is deliberately population-only; it does not create a new military pool.
                newPop = (float)(-regionFaction.Population
                    * _session.Rules.FactionBehaviorRules.OccupiedCivilianDeclineRate);
            }
            else switch (regionFaction.PlanetFaction.Faction.GrowthType)
            {
                case GrowthType.Logistic:
                    float factionGrowthMultiplier = regionFaction.GrowthMultiplier;
                    if (FactionCapabilities.HasDormantPopulations(
                        regionFaction.PlanetFaction.Faction))
                    {
                        factionGrowthMultiplier *= (float)DormantPopulationRules.GrowthEfficiency(
                            _session.Rules.FactionBehaviorRules,
                            regionFaction.IsPublic);
                    }
                    newPop = ApplyCarryingCapacity(
                        regionFaction.Population * LogisticGrowthRate * factionGrowthMultiplier,
                        regionFaction.Region);
                    break;
                case GrowthType.Conversion:
                    if (!regionFaction.IsPublic)
                    {
                        newPop = ConvertPopulation(regionFaction.Region, regionFaction, newPop);
                        if (controllingFaction != null
                            && regionFaction.PlanetFaction.Faction.Id != controllingFaction.Id
                            && planet.PlanetFactionMap[controllingFaction.Id].Leader != null)
                        {
                            // Governor detection of converted population remains future work.
                        }
                    }
                    break;
                case GrowthType.Consumption:
                    newPop = 0;
                    break;
                case GrowthType.Unrest:
                    // Secular allegiance, embedded-PDF recruitment, and civilian arming are
                    // processed together by CivilUnrestTurnProcessor after ordinary growth.
                    return;
                default:
                    newPop = ApplyCarryingCapacity(
                        regionFaction.Population * BaselineGrowthRate * regionFaction.GrowthMultiplier,
                        regionFaction.Region);
                    break;
            }

            float whole = (float)Math.Truncate(newPop);
            float fraction = newPop - whole;
            if (_session.Random.GetLinearDouble() < Math.Abs(fraction))
            {
                whole += Math.Sign(fraction);
            }
            long populationBeforeGrowth = regionFaction.Population;
            regionFaction.Population += (long)whole;
            if (regionFaction.Population < 0)
            {
                regionFaction.Population = 0;
            }
            long grown = regionFaction.Population - populationBeforeGrowth;
            _growthLedger.Record(
                regionFaction.Region.Planet.Id,
                regionFaction.PlanetFaction.Faction.Id,
                grown);
            ScenarioMetricsCollector.RecordScenarioNaturalPopulationChange(regionFaction, grown);
            if (isOverrunRemnant && grown > 0)
            {
                long garrisonBefore = regionFaction.Garrison;
                regionFaction.Garrison += (long)(grown * OverrunRemnantGarrisonArmingRate);
                ScenarioMetricsCollector.RecordScenarioPdfDrafted(
                    regionFaction,
                    regionFaction.Garrison - garrisonBefore);
            }
            UpdateRegionFactionForces(regionFaction, pdfRatio, newPop);
        }

        private static float ApplyCarryingCapacity(float baseGrowth, Region region)
        {
            long capacity = region.CarryingCapacity;
            if (capacity <= 0) return baseGrowth;
            float crowding = 1f - (region.NonConsumerPopulation / (float)capacity);
            return baseGrowth * crowding;
        }

        private void UpdateRegionFactionForces(RegionFaction regionFaction, float pdfRatio, float newPop)
        {
            bool isDefaultFaction = regionFaction.PlanetFaction.Faction.IsDefaultFaction;
            bool isPlayerFaction = regionFaction.PlanetFaction.Faction.IsPlayerFaction;
            bool isOverrunRemnant = isDefaultFaction && !regionFaction.IsPublic;

            if ((isDefaultFaction || isPlayerFaction || !regionFaction.IsPublic) && !isOverrunRemnant)
            {
                regionFaction.Garrison -= (long)(regionFaction.Garrison * GarrisonAttritionRate);

                float draftRate = GarrisonDraftRate;
                if (isDefaultFaction && regionFaction.IsPublic && HasPublicNpcFactionInRegion(regionFaction))
                {
                    draftRate = ActiveAssaultGarrisonDraftRate;
                }
                else if (pdfRatio < 0.03f || !regionFaction.IsPublic)
                {
                    draftRate = EmergencyGarrisonDraftRate;
                }

                long garrisonBeforeDraft = regionFaction.Garrison;
                regionFaction.Garrison += (long)(newPop * draftRate);
                ScenarioMetricsCollector.RecordScenarioPdfDrafted(
                    regionFaction,
                    Math.Max(0, regionFaction.Garrison - garrisonBeforeDraft));
            }
        }

        private static bool HasPublicNpcFactionInRegion(RegionFaction regionFaction)
        {
            return regionFaction.Region.RegionFactionMap.Values.Any(other =>
                other != regionFaction
                && other.IsPublic
                && !FactionRelationshipService.IsImperial(other.PlanetFaction.Faction));
        }

        private float ConvertPopulation(Region region, RegionFaction regionFaction, float newPop)
        {
            RegionFaction defaultFaction = region.RegionFactionMap.Values
                .First(pf => pf.PlanetFaction.Faction.IsDefaultFaction);
            if (defaultFaction?.Population > 0)
            {
                defaultFaction.Population--;
                regionFaction.Population++;
                float pdfChance = (float)defaultFaction.Garrison / defaultFaction.Population;
                if (_session.Random.GetLinearDouble() < pdfChance)
                {
                    defaultFaction.Garrison--;
                    regionFaction.Garrison++;
                }
                if (regionFaction.Population > 100)
                {
                    newPop = regionFaction.Population * 0.002f;
                }
            }
            return newPop;
        }
    }
}
