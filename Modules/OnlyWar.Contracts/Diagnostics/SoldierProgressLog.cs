using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OnlyWar.Helpers
{
    // Shared dev-facing debug log for soldier skill/attribute growth over a discrete activity
    // (a mission, a battle, or a week of training). Captures a snapshot of each soldier's raw
    // skill points and attribute values before the activity, then diffs and reports at Debug level.
    // It lives with the neutral diagnostics contracts so both Operations and Battles can use it
    // without creating a dependency from either bounded context onto the other.
    public static class SoldierProgressLog
    {
        private static readonly (string Label, Func<ISoldier, float> Read)[] AttributeReaders =
        {
            ("Str", s => s.Strength),
            ("Dex", s => s.Dexterity),
            ("Con", s => s.Constitution),
            ("Per", s => s.Perception),
            ("Int", s => s.Intelligence),
            ("Ego", s => s.Ego),
            ("Cha", s => s.Charisma),
        };

        private const float Epsilon = 0.0001f;

        public sealed class ProgressSnapshot
        {
            internal Dictionary<int, Dictionary<int, float>> SkillPoints { get; }
            internal Dictionary<int, Dictionary<string, float>> Attributes { get; }

            internal ProgressSnapshot(
                Dictionary<int, Dictionary<int, float>> skillPoints,
                Dictionary<int, Dictionary<string, float>> attributes)
            {
                SkillPoints = skillPoints;
                Attributes = attributes;
            }
        }

        public static ProgressSnapshot Capture(IEnumerable<ISoldier> soldiers)
        {
            if (!GameLog.IsEnabled(GameLogLevel.Debug) || soldiers == null)
            {
                return null;
            }
            var skillPoints = new Dictionary<int, Dictionary<int, float>>();
            var attributes = new Dictionary<int, Dictionary<string, float>>();
            foreach (ISoldier soldier in soldiers)
            {
                if (soldier == null || skillPoints.ContainsKey(soldier.Id))
                {
                    continue;
                }
                skillPoints[soldier.Id] = soldier.Skills.ToDictionary(
                    skill => skill.BaseSkill.Id, skill => skill.PointsInvested);
                attributes[soldier.Id] = AttributeReaders.ToDictionary(
                    reader => reader.Label, reader => reader.Read(soldier));
            }
            return new ProgressSnapshot(skillPoints, attributes);
        }

        public static void LogDelta(string header, IEnumerable<ISoldier> soldiers, ProgressSnapshot before)
        {
            if (before == null || soldiers == null)
            {
                return;
            }

            List<ISoldier> soldierList = soldiers
                .Where(soldier => soldier != null)
                .GroupBy(soldier => soldier.Id)
                .Select(group => group.First())
                .ToList();
            if (soldierList.Count == 0)
            {
                return;
            }

            var skillTotals = new Dictionary<string, float>();
            var attributeTotals = new Dictionary<string, float>();
            var soldierLines = new List<string>();

            foreach (ISoldier soldier in soldierList)
            {
                var gains = new List<string>();

                before.SkillPoints.TryGetValue(soldier.Id, out Dictionary<int, float> priorSkills);
                foreach (Skill skill in soldier.Skills)
                {
                    float oldPoints = priorSkills != null
                        && priorSkills.TryGetValue(skill.BaseSkill.Id, out float p) ? p : 0f;
                    float deltaPoints = skill.PointsInvested - oldPoints;
                    if (deltaPoints <= Epsilon)
                    {
                        continue;
                    }
                    float oldBonus = BonusForPoints(oldPoints, skill.BaseSkill.Difficulty);
                    gains.Add($"{skill.BaseSkill.Name} +{deltaPoints:F3}pts "
                        + $"(skill {oldBonus:F2}->{skill.SkillBonus:F2})");
                    skillTotals[skill.BaseSkill.Name] =
                        skillTotals.GetValueOrDefault(skill.BaseSkill.Name) + deltaPoints;
                }

                before.Attributes.TryGetValue(soldier.Id, out Dictionary<string, float> priorAttrs);
                if (priorAttrs != null)
                {
                    foreach ((string label, Func<ISoldier, float> read) in AttributeReaders)
                    {
                        float oldValue = priorAttrs.TryGetValue(label, out float v) ? v : read(soldier);
                        float newValue = read(soldier);
                        float delta = newValue - oldValue;
                        if (delta <= Epsilon)
                        {
                            continue;
                        }
                        gains.Add($"{label} +{delta:F3} ({oldValue:F2}->{newValue:F2})");
                        attributeTotals[label] = attributeTotals.GetValueOrDefault(label) + delta;
                    }
                }

                if (gains.Count > 0)
                {
                    soldierLines.Add($"    {soldier.Name}: {string.Join(", ", gains)}");
                }
            }

            if (skillTotals.Count == 0 && attributeTotals.Count == 0)
            {
                GameLog.Debug(() => $"{header}: no XP gained");
                return;
            }

            int count = soldierList.Count;
            var sb = new StringBuilder();
            sb.Append(header).Append(':');
            foreach (KeyValuePair<string, float> entry in skillTotals.OrderByDescending(e => e.Value))
            {
                sb.AppendLine();
                sb.Append($"  {entry.Key}: +{entry.Value:F3}pts total (avg +{entry.Value / count:F3}/soldier)");
            }
            foreach (KeyValuePair<string, float> entry in attributeTotals.OrderByDescending(e => e.Value))
            {
                sb.AppendLine();
                sb.Append($"  {entry.Key}: +{entry.Value:F3} total (avg +{entry.Value / count:F3}/soldier)");
            }
            foreach (string line in soldierLines)
            {
                sb.AppendLine();
                sb.Append(line);
            }
            string report = sb.ToString();
            GameLog.Debug(() => report);
        }

        private static float BonusForPoints(float points, float difficulty) =>
            (points <= 0f ? -4f : (float)Math.Log(points, 2)) - difficulty;
    }
}
