using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Ambush;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Settles planet intelligence, beliefs, observations, and intelligence-generated missions.
    /// Awareness settlement and mission refresh are separate because invalid missions are pruned
    /// between those phases by the turn controller.
    /// </summary>
    internal sealed class PlanetIntelligenceProcessor
    {
        private const float IntelPerListeningPostLevel = 0.2f;

        private readonly GameSession _session;
        private readonly List<Mission> _specialMissions;
        private readonly TurnIntelligenceLedger _ledger;

        internal PlanetIntelligenceProcessor(
            GameSession session,
            List<Mission> specialMissions,
            TurnIntelligenceLedger ledger = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _specialMissions = specialMissions ?? throw new ArgumentNullException(nameof(specialMissions));
            _ledger = ledger ?? new TurnIntelligenceLedger();
        }

        internal void ClearTurnGains() => _ledger.Clear();

        internal void RecordIntelGain(PlanetFaction planetFaction, Region region, float gain)
        {
            _ledger.RecordGain(planetFaction, region, gain);
        }

        internal void RecordReconEvidence(PlanetFaction planetFaction, Region region, float evidence)
        {
            _ledger.RecordReconEvidence(planetFaction, region, evidence);
        }

        internal void RecordTargetObservation(IntelObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            _ledger.RecordObservation(observation);
        }

        internal bool HasPendingEntries(PlanetFaction planetFaction, Planet planet)
        {
            return _ledger.HasPendingEntries(planetFaction, planet);
        }

        internal void ApplyAwareness(Planet planet)
        {
            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
            {
                planetFaction.DecayTargetBeliefs();
                foreach (Region region in planetFaction.RegionAwareness.Keys.ToList())
                {
                    planetFaction.SetRegionAwareness(
                        region,
                        planetFaction.GetRegionAwareness(region)
                            * FactionIntelligenceRules.WeeklyDecayMultiplier);
                }
            }

            foreach (Region region in planet.Regions)
            {
                foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                {
                    // Listening posts are shared structures. Apply their pooled sensor network to
                    // each ally directly rather than recording it once per ally in the ledger.
                    float sensorGain = (float)(
                        RegionDefenses.GetShared(regionFaction, DefenseType.ListeningPost)
                        * IntelPerListeningPostLevel);
                    if (sensorGain > 0f)
                    {
                        regionFaction.PlanetFaction.AddRegionAwareness(region, sensorGain);
                    }
                }
            }

            QueuePublicActivityObservations(planet);
            _ledger.Apply(planet);
        }

        internal void RefreshSpecialMissions(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                RefreshSpecialMissions(planet);
            }
        }

        internal void RefreshSpecialMissions(Planet planet)
        {
            foreach (Region region in planet.Regions)
            {
                float visibleIntel = region.GetPlayerVisibleIntel();
                Faction playerFaction = planet.PlanetFactionMap.Values
                    .Select(planetFaction => planetFaction.Faction)
                    .FirstOrDefault(faction => faction.IsPlayerFaction)
                    ?? planet.PlanetFactionMap.Values
                        .Select(planetFaction => planetFaction.Faction)
                        .FirstOrDefault(faction => faction.IsDefaultFaction);
                List<FactionIntelBelief> playerBeliefs = IntelligenceTargetService
                    .GetPlayerVisibleBeliefs(region)
                    .Where(belief => belief.Level >= IntelLevel.Confirmed
                        && (playerFaction == null
                            || FactionRelationshipService.GetEffectiveStance(
                                playerFaction,
                                belief.TargetFaction,
                                planet) == FactionStance.Hostile))
                    .ToList();

                // Show of Force is a standing governor petition rather than an intelligence find.
                if (visibleIntel < 1f && playerBeliefs.Count == 0)
                {
                    region.SpecialMissions.RemoveAll(
                        mission => mission.MissionType != MissionType.ShowOfForce);
                    continue;
                }

                foreach (Mission mission in region.SpecialMissions.ToList())
                {
                    if (mission.MissionType == MissionType.ShowOfForce) continue;
                    if (_session.Random.GetIntBelowMax(0, 4) == 0)
                    {
                        region.SpecialMissions.Remove(mission);
                    }
                }
                if (visibleIntel <= 0 && playerBeliefs.Count == 0) continue;

                float beliefEvidence = playerBeliefs
                    .Select(belief => belief.Evidence)
                    .DefaultIfEmpty(0f)
                    .Max();
                float regionSpecMissionBudget =
                    (float)Math.Log(Math.Max(1f, Math.Max(visibleIntel, beliefEvidence)), 2) + 1;
                double totalBelievedStrength = playerBeliefs
                    .Select(item => (double)Math.Max(1L, item.EstimatedMilitaryStrength ?? 1L))
                    .Sum();
                foreach (FactionIntelBelief belief in playerBeliefs)
                {
                    double beliefWeight = Math.Max(
                        1L,
                        belief.EstimatedMilitaryStrength ?? 1L) / totalBelievedStrength;
                    HandleBeliefIntelligence(
                        belief,
                        (float)(regionSpecMissionBudget * beliefWeight));
                }
            }
        }

        private void QueuePublicActivityObservations(Planet planet)
        {
            int evidenceWeek = _session.CurrentDate.GetTotalWeeks();
            foreach (Region region in planet.Regions
                .Where(region => region != null)
                .OrderBy(region => region.Id))
            {
                foreach (RegionFaction target in region.RegionFactionMap.Values
                    .Where(regionFaction => regionFaction.IsPublic)
                    .OrderBy(regionFaction => regionFaction.PlanetFaction.Faction.Id))
                {
                    Faction targetFaction = target.PlanetFaction.Faction;
                    foreach (PlanetFaction observer in planet.PlanetFactionMap.Values
                        .Where(planetFaction => planetFaction.Faction.Id != targetFaction.Id)
                        .OrderBy(planetFaction => planetFaction.Faction.Id))
                    {
                        bool hasPlanetaryPresence = planet.Regions.Any(candidateRegion =>
                            candidateRegion.RegionFactionMap.ContainsKey(observer.Faction.Id));
                        bool hasRegionalAwareness = observer.GetRegionAwareness(region) > 0f;
                        if (!hasPlanetaryPresence && !hasRegionalAwareness) continue;

                        FactionIntelBelief previous = observer.GetTargetBelief(region, targetFaction);
                        float evidenceDelta = FactionIntelligenceRules.ConfirmedThreshold
                            - (previous?.Evidence ?? 0f);
                        if (evidenceDelta <= 0f) continue;

                        _ledger.RecordObservation(new IntelObservation(
                            observer,
                            region,
                            targetFaction,
                            evidenceDelta,
                            EstimatePublicActivity(target.Population, observer.GetRegionAwareness(region)),
                            EstimatePublicActivity(target.GetDeployedStrength(), observer.GetRegionAwareness(region)),
                            IntelObservationSource.PublicActivity,
                            evidenceWeek));
                    }
                }
            }
        }

        private static long? EstimatePublicActivity(long value, float awareness)
        {
            if (value < 0) return null;
            if (awareness >= FactionIntelligenceRules.LocatedThreshold) return value;
            long divisor = (long)Math.Pow(
                10,
                Math.Max(0, 3 - (int)Math.Floor(Math.Max(0f, awareness))));
            if (divisor <= 1) return value;
            return value <= 0 ? 0 : Math.Max(1, value / divisor * divisor);
        }

        private void HandleBeliefIntelligence(FactionIntelBelief belief, float specMissionBudget)
        {
            Region region = belief.Region;
            int existing = region.SpecialMissions.Count(mission =>
                mission.Region == region
                && mission.TargetFaction?.Id == belief.TargetFaction.Id);
            float remaining = specMissionBudget - existing;
            for (int i = 0; i < remaining; i++)
            {
                double chance = _session.Random.NextRandomZValue();
                RegionFaction current = region.RegionFactionMap.GetValueOrDefault(belief.TargetFaction.Id);
                if (current == null)
                {
                    int size = Math.Max(1, (int)Math.Ceiling(belief.Evidence));
                    region.SpecialMissions.Add(new Mission(
                        MissionType.Extermination,
                        region,
                        belief.TargetFaction,
                        size));
                }
                else if (chance >= 2)
                {
                    GenerateAssassinationMission(current);
                }
                else if (chance >= 1)
                {
                    double defenseTotal = RegionDefenses.GetShared(current, DefenseType.Entrenchment)
                        + RegionDefenses.GetShared(current, DefenseType.ListeningPost)
                        + RegionDefenses.GetShared(current, DefenseType.AntiAir);
                    if (defenseTotal <= 0) GenerateAmbushMission(current);
                    else GenerateSabotageMission(current, defenseTotal);
                }
                else
                {
                    GenerateAmbushMission(current);
                }
            }
        }

        internal void HandlePublicFactionIntelligence(
            RegionFaction enemyRegionFaction,
            float specMissionBudget)
        {
            float specMissionChance = specMissionBudget;
            specMissionChance -= enemyRegionFaction.Region.SpecialMissions
                .Count(mission => mission.RegionFaction == enemyRegionFaction);
            for (int i = 0; i < specMissionChance; i++)
            {
                double chance = _session.Random.NextRandomZValue();
                if (chance >= 2)
                {
                    GenerateAssassinationMission(enemyRegionFaction);
                }
                else if (chance >= 1)
                {
                    double defenseTotal = RegionDefenses.GetShared(
                            enemyRegionFaction,
                            DefenseType.Entrenchment)
                        + RegionDefenses.GetShared(enemyRegionFaction, DefenseType.ListeningPost)
                        + RegionDefenses.GetShared(enemyRegionFaction, DefenseType.AntiAir);
                    if (defenseTotal <= 0) GenerateAmbushMission(enemyRegionFaction);
                    else GenerateSabotageMission(enemyRegionFaction, defenseTotal);
                }
                else if (chance >= 0)
                {
                    GenerateAmbushMission(enemyRegionFaction);
                }
            }
        }

        internal void HandleHiddenFactionIntelligence(RegionFaction enemyRegionFaction)
        {
            long regionPopulation = Math.Max(1, enemyRegionFaction.Region.Population);
            float popRatio = Math.Clamp(
                (float)enemyRegionFaction.Population / regionPopulation,
                0.0001f,
                0.9999f);
            float zScore = GaussianCalculator.ApproximateInverseNormalCDF(popRatio);
            zScore += enemyRegionFaction.Region.GetPlayerVisibleIntel() / 10.0f;
            double chance = _session.Random.NextRandomZValue();
            if (chance < zScore)
            {
                int size = Math.Max((int)(zScore - chance), 1);
                enemyRegionFaction.Region.SpecialMissions.Add(
                    new Mission(MissionType.Extermination, enemyRegionFaction, size));
            }
        }

        private void GenerateAmbushMission(RegionFaction enemyRegionFaction)
        {
            int maxSize = (int)MissionStealthDifficulty.TroopMagnitude(
                enemyRegionFaction.MilitaryStrength);
            int size = ClampMissionSize((int)_session.Random.NextRandomZValue() + 1, maxSize);
            long targetBattleValue = AmbushMissionSizing.RollTargetBattleValue(size, _session.Random);
            Mission ambush = new Mission(
                MissionType.Ambush,
                enemyRegionFaction,
                size,
                targetBattleValue);
            enemyRegionFaction.Region.SpecialMissions.Add(ambush);
            _specialMissions.Add(ambush);
        }

        private void GenerateSabotageMission(
            RegionFaction enemyRegionFaction,
            double defenseTotal)
        {
            double entrenchment = RegionDefenses.GetShared(
                enemyRegionFaction,
                DefenseType.Entrenchment);
            double listeningPost = RegionDefenses.GetShared(
                enemyRegionFaction,
                DefenseType.ListeningPost);
            double roll = _session.Random.GetLinearDouble() * defenseTotal;
            if (roll <= entrenchment)
            {
                AddSabotageMission(enemyRegionFaction, DefenseType.Entrenchment, entrenchment);
            }
            else if (roll - entrenchment <= listeningPost)
            {
                AddSabotageMission(enemyRegionFaction, DefenseType.ListeningPost, listeningPost);
            }
            else
            {
                AddSabotageMission(
                    enemyRegionFaction,
                    DefenseType.AntiAir,
                    RegionDefenses.GetShared(enemyRegionFaction, DefenseType.AntiAir));
            }
        }

        private void AddSabotageMission(
            RegionFaction enemyRegionFaction,
            DefenseType defenseType,
            double defenseLevel)
        {
            int size = ClampMissionSize(
                (int)_session.Random.NextRandomZValue() + 1,
                (int)Math.Ceiling(defenseLevel));
            SabotageMission sabotage = new SabotageMission(defenseType, size, enemyRegionFaction);
            enemyRegionFaction.Region.SpecialMissions.Add(sabotage);
            _specialMissions.Add(sabotage);
        }

        private void GenerateAssassinationMission(RegionFaction enemyRegionFaction)
        {
            int maximum = (int)MissionStealthDifficulty.TroopMagnitude(enemyRegionFaction.Population);
            int size = ClampMissionSize((int)_session.Random.NextRandomZValue() + 1, maximum);
            Mission assassination = new Mission(MissionType.Assassination, enemyRegionFaction, size);
            enemyRegionFaction.Region.SpecialMissions.Add(assassination);
            _specialMissions.Add(assassination);
        }

        private static int ClampMissionSize(int rolled, int maximum) =>
            Math.Clamp(rolled, 1, Math.Max(1, maximum));
    }
}
