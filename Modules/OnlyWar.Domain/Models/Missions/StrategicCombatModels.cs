using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using System;

namespace OnlyWar.Domain.Missions
{
    public enum StrategicCombatOutcome
    {
        DefenderHeld = 0,
        Raided = 1,
        InvaderFoothold = 2,
        AttackerDestroyed = 3
    }

    public class StrategicCombatContribution
    {
        public RegionFaction StagingFaction { get; }
        public long StrategicValue { get; }

        public StrategicCombatContribution(RegionFaction stagingFaction, long strategicValue)
        {
            StagingFaction = stagingFaction;
            StrategicValue = strategicValue < 0 ? 0 : strategicValue;
        }
    }

    public class StrategicCombatMission : Mission
    {
        public Faction Attacker { get; }
        public long CommittedStrategicValue { get; }
        public IReadOnlyList<StrategicCombatContribution> Contributions { get; }
        public Aggression Aggression { get; }
        public bool InvadesOnVictory { get; }

        public StrategicCombatMission(
            int id,
            RegionFaction target,
            Faction attacker,
            long committedStrategicValue,
            IEnumerable<StrategicCombatContribution> contributions,
            Aggression aggression,
            bool invadesOnVictory,
            MissionType missionType = MissionType.Advance)
            : base(id, missionType, target, 0)
        {
            Attacker = attacker;
            CommittedStrategicValue = committedStrategicValue < 0 ? 0 : committedStrategicValue;
            Contributions = (contributions ?? Enumerable.Empty<StrategicCombatContribution>())
                .Where(c => c != null && c.StrategicValue > 0)
                .ToList()
                .AsReadOnly();
            Aggression = aggression;
            InvadesOnVictory = missionType == MissionType.LightningRaid ? false : invadesOnVictory;
        }

        public StrategicCombatMission(
            RegionFaction target,
            Faction attacker,
            long committedStrategicValue,
            IEnumerable<StrategicCombatContribution> contributions,
            Aggression aggression,
            bool invadesOnVictory,
            MissionType missionType = MissionType.Advance)
            : this(
                Guid.NewGuid().GetHashCode(),
                target,
                attacker,
                committedStrategicValue,
                contributions,
                aggression,
                invadesOnVictory,
                missionType)
        {
        }
    }

    public class StrategicCombatResult
    {
        public RegionFaction Target { get; }
        public Faction Attacker { get; }
        public long CommittedStrategicValue { get; }
        public long DefenderStrategicValue { get; }
        public double AttackerEffectiveStrength { get; }
        public double DefenderEffectiveStrength { get; }
        public long AttackerLosses { get; }
        public long DefenderLosses { get; }
        public long AttackerSurvivors { get; }
        public StrategicCombatOutcome Outcome { get; }
        public bool AttackerWon { get; }
        public bool ControlChanged { get; }
        public long? OriginatingStrategicInvasionForceId { get; set; }

        public StrategicCombatResult(
            RegionFaction target,
            Faction attacker,
            long committedStrategicValue,
            long defenderStrategicValue,
            double attackerEffectiveStrength,
            double defenderEffectiveStrength,
            long attackerLosses,
            long defenderLosses,
            long attackerSurvivors,
            StrategicCombatOutcome outcome,
            bool attackerWon,
            bool controlChanged)
        {
            Target = target;
            Attacker = attacker;
            CommittedStrategicValue = committedStrategicValue;
            DefenderStrategicValue = defenderStrategicValue;
            AttackerEffectiveStrength = attackerEffectiveStrength;
            DefenderEffectiveStrength = defenderEffectiveStrength;
            AttackerLosses = attackerLosses;
            DefenderLosses = defenderLosses;
            AttackerSurvivors = attackerSurvivors;
            Outcome = outcome;
            AttackerWon = attackerWon;
            ControlChanged = controlChanged;
        }
    }
}
