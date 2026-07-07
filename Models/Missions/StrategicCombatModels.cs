using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;

namespace OnlyWar.Models.Missions
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
        public long BattleValue { get; }

        public StrategicCombatContribution(RegionFaction stagingFaction, long battleValue)
        {
            StagingFaction = stagingFaction;
            BattleValue = battleValue < 0 ? 0 : battleValue;
        }
    }

    public class StrategicCombatMission : Mission
    {
        public Faction Attacker { get; }
        public long CommittedBattleValue { get; }
        public IReadOnlyList<StrategicCombatContribution> Contributions { get; }
        public Aggression Aggression { get; }
        public bool InvadesOnVictory { get; }

        public StrategicCombatMission(
            RegionFaction target,
            Faction attacker,
            long committedBattleValue,
            IEnumerable<StrategicCombatContribution> contributions,
            Aggression aggression,
            bool invadesOnVictory)
            : base(MissionType.Advance, target, 0)
        {
            Attacker = attacker;
            CommittedBattleValue = committedBattleValue < 0 ? 0 : committedBattleValue;
            Contributions = (contributions ?? Enumerable.Empty<StrategicCombatContribution>())
                .Where(c => c != null && c.BattleValue > 0)
                .ToList()
                .AsReadOnly();
            Aggression = aggression;
            InvadesOnVictory = invadesOnVictory;
        }
    }

    public class StrategicCombatResult
    {
        public RegionFaction Target { get; }
        public Faction Attacker { get; }
        public long CommittedBattleValue { get; }
        public long DefenderBattleValue { get; }
        public double AttackerEffectiveStrength { get; }
        public double DefenderEffectiveStrength { get; }
        public long AttackerLosses { get; }
        public long DefenderLosses { get; }
        public long AttackerSurvivors { get; }
        public StrategicCombatOutcome Outcome { get; }
        public bool AttackerWon { get; }
        public bool ControlChanged { get; }

        public StrategicCombatResult(
            RegionFaction target,
            Faction attacker,
            long committedBattleValue,
            long defenderBattleValue,
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
            CommittedBattleValue = committedBattleValue;
            DefenderBattleValue = defenderBattleValue;
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
