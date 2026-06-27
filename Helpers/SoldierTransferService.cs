using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public sealed record SoldierTransferOption(
        int SquadId,
        SoldierTemplate SoldierTemplate,
        string DisplayName,
        bool IsCurrentAssignment = false);

    public class SoldierTransferService
    {
        public List<SoldierTransferOption> GetTransferOptions(
            Unit orderOfBattle,
            PlayerSoldier soldier,
            bool includeCurrentAssignment = false)
        {
            if (orderOfBattle == null || soldier?.AssignedSquad == null)
            {
                return [];
            }

            List<SoldierTransferOption> openings = GetOpeningsInUnit(
                orderOfBattle,
                soldier.AssignedSquad,
                soldier.Template);

            if (includeCurrentAssignment)
            {
                openings.Insert(0, new SoldierTransferOption(
                    soldier.AssignedSquad.Id,
                    soldier.Template,
                    $"{soldier.Template.Name}, {soldier.AssignedSquad.Name}, {soldier.AssignedSquad.ParentUnit?.Name ?? "Unassigned"}",
                    true));
            }

            return openings;
        }

        public IReadOnlyList<string> PreviewHistory(
            PlayerSoldier soldier,
            SoldierTransferOption option,
            Date date)
        {
            List<string> history = soldier.SoldierHistory.ToList();
            if (option == null || option.IsCurrentAssignment)
            {
                return history;
            }

            if (soldier.Template != option.SoldierTemplate)
            {
                history.Add($"{date}: promoted to {option.SoldierTemplate.Name}");
            }
            if (soldier.AssignedSquad.Id != option.SquadId)
            {
                history.Add($"{date}: transferred to {option.DisplayName}");
            }

            return history;
        }

        public bool ApplyTransfer(
            PlayerSoldier soldier,
            SoldierTransferOption option,
            IReadOnlyDictionary<int, Squad> squadMap,
            Date date)
        {
            if (soldier == null || option == null || option.IsCurrentAssignment)
            {
                return false;
            }
            if (soldier.AssignedSquad == null)
            {
                throw new InvalidOperationException("Cannot transfer a soldier with no assigned squad.");
            }
            if (!squadMap.TryGetValue(option.SquadId, out Squad newSquad))
            {
                throw new InvalidOperationException($"Could not find transfer target squad {option.SquadId}.");
            }
            if (soldier.AssignedSquad == newSquad && soldier.Template == option.SoldierTemplate)
            {
                return false;
            }

            Squad currentSquad = soldier.AssignedSquad;
            currentSquad.RemoveSquadMember(soldier);
            if (soldier.Template.IsSquadLeader &&
                (currentSquad.SquadTemplate.SquadType & SquadTypes.HQ) == 0)
            {
                currentSquad.Name = currentSquad.SquadTemplate.Name;
            }

            newSquad.AddSquadMember(soldier);
            UpdateSquadLocations(currentSquad, newSquad);

            if (soldier.Template != option.SoldierTemplate)
            {
                soldier.AddEvent(new SoldierEvent(date, SoldierEventType.Promotion,
                    $"promoted to {option.SoldierTemplate.Name}"));
                soldier.Template = option.SoldierTemplate;
            }

            if (soldier.Template.IsSquadLeader &&
                (newSquad.SquadTemplate.SquadType & SquadTypes.HQ) == 0)
            {
                soldier.AssignedSquad.Name = soldier.Name.Split(' ')[1] + " Squad";
            }

            if (currentSquad.Members.Count == 0 &&
                (currentSquad.SquadTemplate.SquadType & SquadTypes.Scout) == SquadTypes.Scout)
            {
                Unit parentUnit = currentSquad.ParentUnit;
                parentUnit.RemoveSquad(currentSquad);
                if (squadMap is IDictionary<int, Squad> writableSquadMap)
                {
                    writableSquadMap.Remove(currentSquad.Id);
                }
            }

            if (currentSquad != newSquad)
            {
                soldier.AddEvent(new SoldierEvent(date, SoldierEventType.Transfer,
                    $"transferred to {option.DisplayName}"));
            }

            return true;
        }

        private static void UpdateSquadLocations(Squad oldSquad, Squad newSquad)
        {
            if (newSquad.Members.Count == 1)
            {
                newSquad.CurrentRegion = oldSquad.CurrentRegion;
                newSquad.BoardedLocation = oldSquad.BoardedLocation;
            }
            if (oldSquad.Members.Count == 0)
            {
                oldSquad.CurrentRegion = null;
                oldSquad.BoardedLocation = null;
            }
        }

        private List<SoldierTransferOption> GetOpeningsInUnit(
            Unit unit,
            Squad currentSquad,
            SoldierTemplate soldierTemplate)
        {
            List<SoldierTransferOption> openSlots = [];
            foreach (Squad squad in unit.Squads)
            {
                IEnumerable<SoldierTemplate> squadSlots = GetOpeningsInSquad(squad, currentSquad, soldierTemplate);
                foreach (SoldierTemplate template in squadSlots)
                {
                    openSlots.Add(new SoldierTransferOption(
                        squad.Id,
                        template,
                        $"{template.Name}, {squad.Name}, {unit.Name}"));
                }
            }
            foreach (Unit childUnit in unit.ChildUnits ?? Enumerable.Empty<Unit>())
            {
                openSlots.AddRange(GetOpeningsInUnit(childUnit, currentSquad, soldierTemplate));
            }

            return openSlots;
        }

        private static IEnumerable<SoldierTemplate> GetOpeningsInSquad(
            Squad squad,
            Squad currentSquad,
            SoldierTemplate soldierTemplate)
        {
            List<SoldierTemplate> openSpots = [];
            bool hasSquadLeader = squad.SquadLeader != null;
            Dictionary<SoldierTemplate, int> typeCountMap = squad.Members
                .GroupBy(s => s.Template)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (SquadTemplateElement element in squad.SquadTemplate.Elements)
            {
                if (!hasSquadLeader && !element.SoldierTemplate.IsSquadLeader)
                {
                    continue;
                }
                if (currentSquad == squad && element.SoldierTemplate == soldierTemplate)
                {
                    continue;
                }
                if (element.SoldierTemplate.Rank < soldierTemplate.Rank ||
                    element.SoldierTemplate.Rank > soldierTemplate.Rank + 1)
                {
                    continue;
                }

                int existingHeadcount = typeCountMap.TryGetValue(element.SoldierTemplate, out int count) ? count : 0;
                if (existingHeadcount < element.MaximumNumber)
                {
                    openSpots.Add(element.SoldierTemplate);
                }
            }

            return openSpots;
        }
    }
}
