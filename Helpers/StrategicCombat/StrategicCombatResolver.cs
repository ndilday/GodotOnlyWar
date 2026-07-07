using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers.StrategicCombat
{
    public class StrategicCombatResolver
    {
        private readonly IRNG _rng;

        public StrategicCombatResolver(IRNG rng = null)
        {
            _rng = rng ?? StaticRNG.Instance;
        }

        public StrategicCombatResult Resolve(StrategicCombatMission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            RegionFaction target = mission.RegionFaction;
            Faction attacker = mission.Attacker;
            long committed = Math.Max(0, mission.CommittedBattleValue);
            long defenderBattleValue = CalculateDefenderBattleValue(target);

            double attackerEffective = CalculateAttackerEffectiveStrength(mission);
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
                * StrategicCombatRules.DefenderProtection(target.Entrenchment);

            attackerLossRate = Math.Clamp(attackerLossRate, 0.01, 0.60);
            defenderLossRate = Math.Clamp(defenderLossRate, 0.01, 0.75);

            long attackerLosses = ClampLoss((long)Math.Round(committed * attackerLossRate), committed, defenderEffective);
            long mutableDefenderStrength = target.MilitaryStrength;
            long defenderLosses = ClampLoss((long)Math.Round(defenderBattleValue * defenderLossRate),
                mutableDefenderStrength, attackerEffective);

            target.RemoveMilitaryStrength(defenderLosses);
            long attackerSurvivors = committed - attackerLosses;

            bool controlChanged = false;
            StrategicCombatOutcome outcome;
            if (attackerSurvivors <= 0)
            {
                outcome = StrategicCombatOutcome.AttackerDestroyed;
            }
            else if (attackerWon && mission.InvadesOnVictory)
            {
                TurnController.EstablishInvaderPresence(attacker, target.Region, attackerSurvivors);
                HideBrokenCivilianDefender(target);
                controlChanged = true;
                outcome = StrategicCombatOutcome.InvaderFoothold;
            }
            else
            {
                ReturnSurvivors(mission.Contributions, attackerSurvivors, committed);
                outcome = attackerWon ? StrategicCombatOutcome.Raided : StrategicCombatOutcome.DefenderHeld;
            }

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

        public static long CalculateDefenderBattleValue(RegionFaction defender)
        {
            if (defender == null) return 0;
            long landedNpcBattleValue = defender.LandedSquads
                .Where(squad => squad?.Faction?.IsPlayerFaction == false)
                .SelectMany(squad => squad.Members)
                .Sum(soldier => (long)soldier.Template.BattleValue);

            return defender.MilitaryStrength + landedNpcBattleValue;
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
                * StrategicCombatRules.DefenderReadiness(defender.Organization)
                * StrategicCombatRules.FactionQuality(defender.PlanetFaction.Faction)
                * StrategicCombatRules.EntrenchmentMultiplier(defender.Entrenchment)
                * StrategicCombatRules.DetectionMultiplier(defender.Detection);
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
            if (defender.PlanetFaction.Faction.PopulationIsMilitary) return;
            if (defender.MilitaryStrength > 0) return;
            if (defender.Population <= 0) return;
            defender.IsPublic = false;
        }
    }
}
