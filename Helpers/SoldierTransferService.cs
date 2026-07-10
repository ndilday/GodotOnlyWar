using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
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
        bool IsCurrentAssignment = false,
        // When IsNewSquad is set, no squad exists yet: ApplyTransfer creates a squad
        // of TargetSquadTemplate inside TargetUnit (the unit still has room under its
        // cap) and moves the soldier into it. SquadId is unused in that case.
        bool IsNewSquad = false,
        Unit TargetUnit = null,
        SquadTemplate TargetSquadTemplate = null);

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

            // Present openings by ascending target rank, so lateral transfers come first,
            // then single-level promotions, then higher jumps. Subrank breaks ties within a
            // rank (e.g. a Veteran move sorts ahead of a Veteran Sergeant promotion, both at
            // Rank 5). OrderBy is stable, so remaining ties keep the order-of-battle traversal
            // order. The current assignment, when shown, is pinned to the top regardless.
            List<SoldierTransferOption> openings = GetOpeningsInUnit(
                orderOfBattle,
                soldier.AssignedSquad,
                soldier.Template)
                .OrderBy(option => option.SoldierTemplate.Rank)
                .ThenBy(option => option.SoldierTemplate.Subrank)
                .ToList();

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

        // A transfer never changes how many soldiers are aboard a ship if the soldier is
        // already stationed on that same ship (moving between squads that share a boat is a
        // lateral reshuffle, not a new arrival). Otherwise, boarding a squad that's out of
        // room isn't possible until the ship (or the squad's berth) frees up a slot.
        public bool WouldExceedShipCapacity(
            PlayerSoldier soldier,
            SoldierTransferOption option,
            IReadOnlyDictionary<int, Squad> squadMap)
        {
            if (soldier?.AssignedSquad == null || option == null ||
                option.IsCurrentAssignment || option.IsNewSquad)
            {
                return false;
            }
            if (squadMap == null || !squadMap.TryGetValue(option.SquadId, out Squad targetSquad) ||
                targetSquad.BoardedLocation == null)
            {
                return false;
            }

            Ship targetShip = targetSquad.BoardedLocation;
            if (soldier.AssignedSquad.BoardedLocation == targetShip)
            {
                return false;
            }

            return targetShip.AvailableCapacity <= 0;
        }

        public string FormatBlockedTransferTarget(
            SoldierTransferOption option,
            IReadOnlyDictionary<int, Squad> squadMap)
        {
            if (option == null)
            {
                return "Selected transfer target";
            }
            if (option.IsNewSquad || squadMap == null ||
                !squadMap.TryGetValue(option.SquadId, out Squad targetSquad) ||
                targetSquad.BoardedLocation == null)
            {
                return option.DisplayName;
            }

            return $"{option.DisplayName} ({SquadLocationFormatter.Format(targetSquad)})";
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
            Squad newSquad;
            if (option.IsNewSquad)
            {
                if (option.TargetUnit == null || option.TargetSquadTemplate == null)
                {
                    throw new InvalidOperationException("New-squad transfer option is missing its target unit or template.");
                }
                newSquad = new Squad(option.TargetSquadTemplate.Name, option.TargetUnit, option.TargetSquadTemplate);
                option.TargetUnit.AddSquad(newSquad);
                if (squadMap is IDictionary<int, Squad> writableSquadMap)
                {
                    writableSquadMap[newSquad.Id] = newSquad;
                }
            }
            else if (!squadMap.TryGetValue(option.SquadId, out newSquad))
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

            if (currentSquad.Members.Count == 0 && IsRemovableWhenEmpty(currentSquad))
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
                if (!IsTransferLocationAllowed(currentSquad, squad))
                {
                    continue;
                }
                IEnumerable<SoldierTemplate> squadSlots = GetOpeningsInSquad(squad, currentSquad, soldierTemplate);
                foreach (SoldierTemplate template in squadSlots)
                {
                    openSlots.Add(new SoldierTransferOption(
                        squad.Id,
                        template,
                        $"{template.Name}, {squad.Name}, {unit.Name}"));
                }
            }
            // New-squad openings: any squad template the unit may still hold more of.
            // A brand-new squad is empty, so only its leader slot is open — i.e. the
            // soldier starts the squad by becoming its sergeant.
            foreach (SquadTemplateSlot slot in
                     unit.UnitTemplate?.GetChildSquadSlots() ?? Enumerable.Empty<SquadTemplateSlot>())
            {
                int existing = unit.Squads.Count(s => s.SquadTemplate == slot.Template);
                if (existing >= slot.MaxCount)
                {
                    continue;
                }
                foreach (SoldierTemplate template in GetOpeningsInEmptySquad(slot.Template, soldierTemplate))
                {
                    openSlots.Add(new SoldierTransferOption(
                        0,
                        template,
                        $"{template.Name}, New {slot.Template.Name}, {unit.Name}",
                        IsNewSquad: true,
                        TargetUnit: unit,
                        TargetSquadTemplate: slot.Template));
                }
            }

            foreach (Unit childUnit in unit.ChildUnits ?? Enumerable.Empty<Unit>())
            {
                openSlots.AddRange(GetOpeningsInUnit(childUnit, currentSquad, soldierTemplate));
            }

            return openSlots;
        }

        // A soldier may fill a slot at their current rank (a lateral transfer) or any
        // rank above it (a promotion of any number of levels). Slots below the soldier's
        // current rank are not offered, since transfers never demote.
        private static bool IsRankEligible(SoldierTemplate slot, SoldierTemplate soldier)
        {
            return slot.Rank >= soldier.Rank;
        }

        // Becoming a specialist is a one-way door. A line/command brother
        // (SpecialistType 0) may still be drawn into any track — a regular marine can
        // become a Chaplain, Apothecary, Techmarine, etc. But once a soldier holds a
        // specialist calling, he may only transfer within that same SpecialistType: he
        // can never return to the line or cross over to another specialty. This keeps an
        // Apothecary transferable only to Apothecary roles.
        private static bool IsSpecialistEligible(SoldierTemplate slot, SoldierTemplate soldier)
        {
            return soldier.SpecialistType == 0 || slot.SpecialistType == soldier.SpecialistType;
        }

        // Gates a transfer on where the two squads are, not just what slots are open.
        // A squad pinned in an enemy-controlled region is cut off: it may only trade
        // soldiers with another squad in that exact region, not even a ship in orbit
        // overhead. Everywhere else (a ship, or a player/allied-controlled region) is
        // "safe," and safe squads may freely trade as long as they share a planet. A
        // squad with no location at all (a brand-new squad, or an existing squad that
        // was emptied out and kept alive) has nothing to be pinned by, so it is always
        // reachable — mirroring the always-allowed new-squad option.
        private static bool IsTransferLocationAllowed(Squad source, Squad destination)
        {
            if (source == destination)
            {
                return true;
            }
            bool sourceHasLocation = source.CurrentRegion != null || source.BoardedLocation != null;
            if (!sourceHasLocation)
            {
                return true;
            }
            bool destinationHasLocation = destination.CurrentRegion != null || destination.BoardedLocation != null;
            if (!destinationHasLocation)
            {
                return true;
            }

            bool sourceSafe = IsSquadLocationSafe(source);
            bool destinationSafe = IsSquadLocationSafe(destination);
            if (!sourceSafe || !destinationSafe)
            {
                return source.CurrentRegion != null && source.CurrentRegion == destination.CurrentRegion;
            }

            return GetSquadPlanet(source) == GetSquadPlanet(destination);
        }

        private static bool IsSquadLocationSafe(Squad squad)
        {
            // Boarded on a ship is always safe: the ship isn't sitting in anyone's
            // contested territory.
            return squad.BoardedLocation != null || IsRegionSafe(squad.CurrentRegion);
        }

        private static bool IsRegionSafe(Region region)
        {
            if (region == null)
            {
                return false;
            }
            RegionFaction controller = region.ControllingFaction;
            return controller != null &&
                   (controller.PlanetFaction.Faction.IsPlayerFaction || controller.PlanetFaction.Faction.IsDefaultFaction);
        }

        private static Planet GetSquadPlanet(Squad squad)
        {
            if (squad.CurrentRegion != null)
            {
                return squad.CurrentRegion.Planet;
            }
            return squad.BoardedLocation?.Fleet?.Planet;
        }

        private static IEnumerable<SoldierTemplate> GetOpeningsInEmptySquad(
            SquadTemplate squadTemplate,
            SoldierTemplate soldierTemplate)
        {
            List<SoldierTemplate> openSpots = [];
            foreach (SquadTemplateElement element in squadTemplate.Elements)
            {
                // An empty squad has no leader, so only leader-eligible slots are open.
                if (!element.SoldierTemplate.IsSquadLeader)
                {
                    continue;
                }
                if (!IsRankEligible(element.SoldierTemplate, soldierTemplate))
                {
                    continue;
                }
                if (!IsSpecialistEligible(element.SoldierTemplate, soldierTemplate))
                {
                    continue;
                }
                if (element.MaximumNumber > 0)
                {
                    openSpots.Add(element.SoldierTemplate);
                }
            }

            return openSpots;
        }

        // A squad is cleaned up when its last member leaves unless it must always
        // exist: HQ squads and squads whose unit template requires at least one
        // (MinCount > 0, e.g. the chapter's command squads) are kept. Line squads
        // (MinCount 0) and ad-hoc squads with no slot are removed so none linger empty.
        private static bool IsRemovableWhenEmpty(Squad squad)
        {
            if ((squad.SquadTemplate.SquadType & SquadTypes.HQ) != 0)
            {
                return false;
            }
            Unit parent = squad.ParentUnit;
            if (parent?.UnitTemplate == null)
            {
                return true;
            }
            foreach (SquadTemplateSlot slot in parent.UnitTemplate.GetChildSquadSlots())
            {
                if (slot.Template == squad.SquadTemplate)
                {
                    return slot.MinCount == 0;
                }
            }
            return true;
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
                // A squad has exactly one leader: offer a leader slot only while the
                // squad is leaderless, and offer rank-and-file slots only once a leader
                // is in place. Without the leaderful case, a filled leader slot whose
                // occupant is a different leader template than the slot defines (e.g. a
                // Captain sitting in a slot the template calls "Recruitment Captain")
                // would still be offered as an opening.
                if (element.SoldierTemplate.IsSquadLeader == hasSquadLeader)
                {
                    continue;
                }
                if (currentSquad == squad && element.SoldierTemplate == soldierTemplate)
                {
                    continue;
                }
                if (!IsRankEligible(element.SoldierTemplate, soldierTemplate))
                {
                    continue;
                }
                if (!IsSpecialistEligible(element.SoldierTemplate, soldierTemplate))
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
