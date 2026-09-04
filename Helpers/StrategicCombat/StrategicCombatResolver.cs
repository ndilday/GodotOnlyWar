using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers.StrategicCombat
{
    public class StrategicCombatResolver
    {
        private readonly IRNG _rng;
        private readonly Action<PlanetFaction, Region, float> _recordIntelGain;
        private readonly Action<IntelObservation> _recordTargetObservation;

        public StrategicCombatResolver(
            IRNG rng = null,
            Action<PlanetFaction, Region, float> recordIntelGain = null,
            Action<IntelObservation> recordTargetObservation = null)
        {
            _rng = rng ?? StaticRNG.Instance;
            _recordIntelGain = recordIntelGain;
            _recordTargetObservation = recordTargetObservation;
        }

        public StrategicCombatResult Resolve(StrategicCombatMission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            RegionFaction target = mission.RegionFaction;
            Faction attacker = mission.Attacker;
            List<RegionFaction> defenders = GetDefendingFactions(target, attacker);
            long committed = Math.Max(0, mission.CommittedBattleValue);
            long defenderBattleValue = CalculateEngagedDefenderBattleValue(mission, target, defenders);

            // Surprise from the attacker/defender awareness differential (StrategicCombatRules): a
            // faction attacking a region it understands better than the defender sees its own ground
            // strikes with an edge — the "attacking from within" advantage of a freshly-risen cult
            // against a blind PDF. It fades as the defender builds awareness (listening posts, patrols,
            // recon). Applied to the attacker's effective strength so it shifts both the win check and
            // the casualty exchange.
            double attackerIntel = target.Region.GetFactionRegionAwareness(attacker);
            double defenderIntel = target.GetOwnRegionAwareness();
            double surprise = StrategicCombatRules.AmbushSurpriseMultiplier(attackerIntel, defenderIntel);

            double attackerEffective = CalculateAttackerEffectiveStrength(mission) * surprise;
            double defenderEffective = CalculateDefenderEffectiveStrength(target, defenderBattleValue);

            double attackerRoll = attackerEffective * Math.Exp(_rng.NextRandomZValue() * StrategicCombatRules.CombatSigma);
            double defenderRoll = defenderEffective * Math.Exp(_rng.NextRandomZValue() * StrategicCombatRules.CombatSigma);
            bool attackerWon = attackerRoll > defenderRoll * StrategicCombatRules.CaptureThreshold;

            double intensity = StrategicCombatRules.BaseIntensity
                * StrategicCombatRules.AggressionCasualtyMultiplier(mission.Aggression);
            double attackerPressure = attackerEffective / Math.Max(defenderEffective, 1.0);
            double defenderPressure = defenderEffective / Math.Max(attackerEffective, 1.0);

            double attackerLossRate = intensity * Math.Pow(defenderPressure, 0.65);
            double defenderLossRate = intensity * Math.Pow(attackerPressure, 0.65)
                * StrategicCombatRules.DefenderProtection(
                    RegionDefenses.GetShared(target, DefenseType.Entrenchment));

            attackerLossRate = Math.Clamp(attackerLossRate, 0.01, 0.60);
            defenderLossRate = Math.Clamp(defenderLossRate, 0.01, 0.75);

            long attackerLosses = ClampLoss((long)Math.Round(committed * attackerLossRate), committed, defenderEffective);
            long mutableDefenderStrength = defenders.Sum(
                defender => defender.OrganizedMilitaryStrength);
            long defenderLosses = ClampLoss((long)Math.Round(defenderBattleValue * defenderLossRate),
                mutableDefenderStrength, attackerEffective);

            ApplyDefenderLosses(defenders, defenderLosses, mutableDefenderStrength);
            long attackerSurvivors = committed - attackerLosses;

            bool controlChanged = false;
            StrategicCombatOutcome outcome;
            if (attackerSurvivors <= 0)
            {
                outcome = StrategicCombatOutcome.AttackerDestroyed;
            }
            else if (attackerWon && mission.InvadesOnVictory)
            {
                InvaderPresenceService.Establish(attacker, target.Region, attackerSurvivors);
                HideBrokenCivilianDefender(target);
                controlChanged = true;
                outcome = StrategicCombatOutcome.InvaderFoothold;
            }
            else
            {
                ReturnSurvivors(mission.Contributions, attackerSurvivors, committed);
                outcome = attackerWon ? StrategicCombatOutcome.Raided : StrategicCombatOutcome.DefenderHeld;
            }

            // Reactive awareness: a defender that survives the assault learns which regions the enemy
            // staged from, so a previously-blind neighbour can be garrisoned next turn even without a
            // deliberate recon (FactionThreatAssessment.CalculateRequiredDefensiveBattleValue).
            if (!controlChanged && mission.Contributions != null)
            {
                foreach (StrategicCombatContribution contribution in mission.Contributions)
                {
                    Region stagingRegion = contribution.StagingFaction?.Region;
                    if (stagingRegion != null)
                    {
                        if (_recordIntelGain != null)
                        {
                            _recordIntelGain(
                                target.PlanetFaction,
                                stagingRegion,
                                StrategicCombatRules.IntelGainedFromBeingAttacked);
                        }
                        else
                        {
                            target.PlanetFaction.AddRegionAwareness(
                                stagingRegion, StrategicCombatRules.IntelGainedFromBeingAttacked);
                        }
                    }
                }
            }

            RecordBattleContact(mission, target, defenders, defenderBattleValue, committed);

            return new StrategicCombatResult(
                target,
                attacker,
                committed,
                defenderBattleValue,
                attackerEffective,
                defenderEffective,
                attackerLosses,
                defenderLosses,
                attackerSurvivors,
                outcome,
                attackerWon,
                controlChanged);
        }

        private void RecordBattleContact(
            StrategicCombatMission mission,
            RegionFaction target,
            IReadOnlyCollection<RegionFaction> defenders,
            long defenderBattleValue,
            long committedBattleValue)
        {
            if (_recordTargetObservation == null
                || target?.Region?.Planet == null
                || mission?.Attacker == null)
            {
                return;
            }

            Planet planet = target.Region.Planet;
            List<(Faction Faction, PlanetFaction Presence, long? Population, long? Military)> participants =
                new();

            PlanetFaction attackerObserver = GetOrMaterializeObserver(planet, mission.Attacker);
            participants.Add((
                mission.Attacker,
                attackerObserver,
                null,
                committedBattleValue > 0 ? committedBattleValue : null));

            foreach (RegionFaction defender in defenders
                .Where(defender => defender?.PlanetFaction?.Faction != null)
                .OrderBy(defender => defender.PlanetFaction.Faction.Id))
            {
                Faction faction = defender.PlanetFaction.Faction;
                if (participants.Any(participant => participant.Faction.Id == faction.Id)) continue;
                participants.Add((
                    faction,
                    defender.PlanetFaction,
                    Math.Max(0L, defender.Population),
                    Math.Max(0L, CalculateDefenderBattleValue(defender))));
            }

            foreach ((Faction Faction, PlanetFaction Presence, long? Population, long? Military) observer in participants)
            {
                foreach ((Faction Faction, PlanetFaction Presence, long? Population, long? Military) subject in participants)
                {
                    if (observer.Faction.Id == subject.Faction.Id) continue;
                    RecordLocatedObservation(
                        observer.Presence,
                        target.Region,
                        subject.Faction,
                        subject.Population,
                        subject.Military);
                }
            }
        }

        private void RecordLocatedObservation(
            PlanetFaction observer,
            Region region,
            Faction targetFaction,
            long? estimatedPopulation,
            long? estimatedMilitaryStrength)
        {
            if (observer == null || region == null || targetFaction == null
                || observer.Faction.Id == targetFaction.Id)
            {
                return;
            }

            FactionIntelBelief previous = observer.GetTargetBelief(region, targetFaction);
            float evidenceDelta = Math.Max(
                0.25f,
                FactionIntelligenceRules.LocatedThreshold - (previous?.Evidence ?? 0f));
            _recordTargetObservation(new IntelObservation(
                observer,
                region,
                targetFaction,
                evidenceDelta,
                estimatedPopulation,
                estimatedMilitaryStrength,
                IntelObservationSource.BattleContact,
                0));
        }

        private static PlanetFaction GetOrMaterializeObserver(Planet planet, Faction faction)
        {
            if (planet.PlanetFactionMap.TryGetValue(faction.Id, out PlanetFaction existing))
            {
                return existing;
            }

            PlanetFaction materialized = new(faction) { IsPublic = faction.IsPlayerFaction };
            planet.PlanetFactionMap[faction.Id] = materialized;
            planet.NotifyPlanetFactionAdded(materialized);
            return materialized;
        }

        public static long CalculateDefenderBattleValue(RegionFaction defender)
        {
            if (defender == null) return 0;
            long landedNpcBattleValue = defender.LandedSquads
                .Where(squad => squad?.Faction?.IsPlayerFaction == false)
                .SelectMany(squad => squad.Members)
                .Sum(soldier => (long)soldier.Template.BattleValue);

            // GetDeployedStrength, not raw MilitaryStrength: organized BV is the concrete share of a
            // faction's strength that can actually be fielded, and every other consumer of "how strong
            // is this defender" now works in those units - the planner's spare-troops arithmetic
            // (FactionStrategyController.GeneratePlanetOrders), the defensive reserve
            // (CalculateRequiredDefensiveBattleValue), and the tactical defence that materialises it
            // (PrepareAssaultMissionStep.AssembleDefendingForce). Reading the raw pool here meant a
            // disorganized region was priced as fully mobilized for strategic combat while being priced
            // correctly everywhere else.
            long persistentCommandBattleValue = GameDataSingleton.Instance?.Sector?.StrategicInvasionForces
                ?.Where(force => force.IsActive
                    && force.Faction == defender.PlanetFaction.Faction
                    && force.CurrentRegion == defender.Region)
                .SelectMany(force => force.CommandSquad?.Members ?? [])
                .Sum(member => (long)(member.Template?.BattleValue ?? 0))
                ?? 0L;
            return defender.GetDeployedStrength() + landedNpcBattleValue + persistentCommandBattleValue;
        }

        internal static long CalculateDefenderBattleValueAgainst(RegionFaction target, Faction attacker)
        {
            return GetDefendingFactions(target, attacker).Sum(CalculateDefenderBattleValue);
        }

        private static long CalculateEngagedDefenderBattleValue(
            StrategicCombatMission mission,
            RegionFaction target,
            IReadOnlyCollection<RegionFaction> defenders)
        {
            long fullDefenderBattleValue = defenders.Sum(CalculateDefenderBattleValue);
            if (mission.MissionType != MissionType.LightningRaid || fullDefenderBattleValue <= 0)
            {
                return fullDefenderBattleValue;
            }

            double attackerIntel = target.Region.GetFactionRegionAwareness(mission.Attacker);
            double defenderIntel = target.GetOwnRegionAwareness();
            double intelEdge = Math.Clamp(attackerIntel - defenderIntel, -2.0, 4.0);
            double exposedShare = Math.Clamp(0.40 + intelEdge * 0.08, 0.25, 0.75);
            long exposedDefenders = (long)Math.Round(fullDefenderBattleValue * exposedShare);
            long manageableDefenders = (long)Math.Round(mission.CommittedBattleValue * 1.25);

            return Math.Max(1, Math.Min(fullDefenderBattleValue, Math.Min(exposedDefenders, manageableDefenders)));
        }

        private static List<RegionFaction> GetDefendingFactions(RegionFaction target, Faction attacker)
        {
            if (target?.Region == null) return [];
            List<RegionFaction> defenders = [target];
            defenders.AddRange(target.Region.RegionFactionMap.Values.Where(candidate =>
                candidate != target
                && FactionRelationshipService.DefendsHostAgainst(candidate, attacker)));
            return defenders;
        }

        private static void ApplyDefenderLosses(
            IReadOnlyList<RegionFaction> defenders,
            long losses,
            long totalMilitaryStrength)
        {
            if (losses <= 0 || totalMilitaryStrength <= 0) return;

            long applied = 0;
            foreach (RegionFaction defender in defenders.OrderByDescending(item => item.OrganizedMilitaryStrength))
            {
                long share = (long)Math.Floor(losses * (defender.OrganizedMilitaryStrength / (double)totalMilitaryStrength));
                share = Math.Min(share, defender.OrganizedMilitaryStrength);
                defender.RemoveOrganizedMilitaryStrength(share);
                applied += share;
            }

            long residue = losses - applied;
            foreach (RegionFaction defender in defenders.OrderByDescending(item => item.OrganizedMilitaryStrength))
            {
                if (residue <= 0) break;
                long removed = defender.RemoveOrganizedMilitaryStrength(residue);
                residue -= removed;
            }
        }

        public static double CalculateAttackerEffectiveStrength(StrategicCombatMission mission)
        {
            if (mission == null) return 0;
            return mission.CommittedBattleValue
                * StrategicCombatRules.FactionQuality(mission.Attacker)
                * StrategicCombatRules.AggressionStrengthMultiplier(mission.Aggression);
        }

        public static double CalculateDefenderEffectiveStrength(RegionFaction defender, long defenderBattleValue)
        {
            if (defender == null || defenderBattleValue <= 0) return 0;
            return defenderBattleValue
                * StrategicCombatRules.FactionQuality(defender.PlanetFaction.Faction)
                // The defender fights from the whole position its side holds here, not just the
                // stretch of trench its own faction dug (RegionDefenses).
                * StrategicCombatRules.EntrenchmentMultiplier(
                    RegionDefenses.GetShared(defender, DefenseType.Entrenchment));
        }

        private static long ClampLoss(long calculatedLoss, long availableStrength, double opposingEffectiveStrength)
        {
            if (availableStrength <= 0 || opposingEffectiveStrength <= 0) return 0;
            long loss = calculatedLoss <= 0 ? 1 : calculatedLoss;
            return Math.Min(loss, availableStrength);
        }

        private static void ReturnSurvivors(
            IReadOnlyList<StrategicCombatContribution> contributions,
            long survivors,
            long committed)
        {
            if (survivors <= 0 || committed <= 0 || contributions == null || contributions.Count == 0)
            {
                return;
            }

            List<StrategicCombatContribution> orderedContributions = contributions
                .Where(c => c.BattleValue > 0)
                .OrderByDescending(c => c.BattleValue)
                .ToList();
            if (orderedContributions.Count == 0) return;

            long returned = 0;
            foreach (StrategicCombatContribution contribution in orderedContributions)
            {
                long amount = (long)Math.Floor(survivors * (contribution.BattleValue / (double)committed));
                contribution.StagingFaction?.AddMilitaryStrength(amount);
                returned += amount;
            }

            long residue = survivors - returned;
            int index = 0;
            while (residue > 0)
            {
                orderedContributions[index % orderedContributions.Count].StagingFaction?.AddMilitaryStrength(1);
                residue--;
                index++;
            }
        }

        private static void HideBrokenCivilianDefender(RegionFaction defender)
        {
            if (defender?.PlanetFaction?.Faction == null) return;
            if (defender.PlanetFaction.Faction.HasBehavior(FactionBehavior.PopulationIsMilitary)) return;
            if (defender.MilitaryStrength > 0) return;
            if (defender.Population <= 0) return;
            defender.IsPublic = false;
            // An ally still standing in the region takes the works over intact — the trenches did
            // not stop existing because the faction that dug them was broken, and the merge is
            // level-preserving for the side (RegionDefenses.TransferToAlly). Only when nobody is
            // left to man them does the conquest wreck or capture half, with the remainder rotting
            // each turn it sits unmanned under the occupier (RegionControlTurnProcessor.DecayUnmannedDefenses).
            if (RegionDefenses.TransferToAlly(defender) == null)
            {
                defender.HalveDefensesOnGoingToGround();
            }
        }
    }
}
