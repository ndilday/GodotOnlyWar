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
        SquadTemplate TargetSquadTemplate = null,
        bool IsProvisionalSquad = false,
        Guid? ProvisionalFormationId = null);

    /// <summary>
    /// Read-only snapshot used while calculating transfer options for a roster. It captures the
    /// formation traversal, ordering, leadership, and per-role occupancy once; locations and
    /// soldier-specific eligibility are still evaluated for each query. Rebuild it after any
    /// roster mutation that changes squad membership, administration, or formation structure.
    /// </summary>
    public sealed class SoldierTransferContext
    {
        internal sealed record SquadContext(
            Squad Squad,
            bool HasSquadLeader,
            IReadOnlyDictionary<SoldierTemplate, int> TypeCounts);

        internal sealed record UnitContext(
            Unit Unit,
            IReadOnlyList<SquadContext> Squads,
            IReadOnlyList<SquadTemplateSlot> Slots,
            string OrderKey);

        internal Unit OrderOfBattle { get; }
        internal IReadOnlyList<UnitContext> Units { get; }
        internal IReadOnlyDictionary<Unit, string> UnitOrderKeys { get; }
        internal IReadOnlyDictionary<int, Squad> SquadMap { get; }
        internal IReadOnlyDictionary<(Unit Unit, SquadTemplate Template), int> SquadTypeOrders { get; }

        private SoldierTransferContext(
            Unit orderOfBattle,
            IReadOnlyList<UnitContext> units,
            IReadOnlyDictionary<Unit, string> unitOrderKeys,
            IReadOnlyDictionary<int, Squad> squadMap,
            IReadOnlyDictionary<(Unit Unit, SquadTemplate Template), int> squadTypeOrders)
        {
            OrderOfBattle = orderOfBattle;
            Units = units;
            UnitOrderKeys = unitOrderKeys;
            SquadMap = squadMap;
            SquadTypeOrders = squadTypeOrders;
        }

        public static SoldierTransferContext Build(Unit orderOfBattle)
        {
            if (orderOfBattle == null)
            {
                throw new ArgumentNullException(nameof(orderOfBattle));
            }

            List<UnitContext> units = [];
            Dictionary<Unit, string> unitOrderKeys = [];
            Dictionary<int, Squad> squadMap = [];
            Dictionary<(Unit Unit, SquadTemplate Template), int> squadTypeOrders = [];
            CollectUnits(orderOfBattle, units, unitOrderKeys, squadMap, squadTypeOrders);
            return new SoldierTransferContext(
                orderOfBattle, units, unitOrderKeys, squadMap, squadTypeOrders);
        }

        private static void CollectUnits(
            Unit unit,
            List<UnitContext> units,
            Dictionary<Unit, string> unitOrderKeys,
            Dictionary<int, Squad> squadMap,
            Dictionary<(Unit Unit, SquadTemplate Template), int> squadTypeOrders)
        {
            List<Squad> squads = unit.Squads?.ToList() ?? [];
            List<SoldierTransferContext.SquadContext> squadContexts = squads
                .Select(squad => new SoldierTransferContext.SquadContext(
                    squad,
                    squad.SquadLeader != null,
                    squad.Members
                        .GroupBy(member => member.Template)
                        .ToDictionary(group => group.Key, group => group.Count())))
                .ToList();
            List<SquadTemplateSlot> slots = unit.UnitTemplate?.GetChildSquadSlots()?.ToList() ?? [];
            string orderKey = GetUnitOrderKey(unit);
            units.Add(new UnitContext(unit, squadContexts, slots, orderKey));
            unitOrderKeys[unit] = orderKey;

            foreach (Squad squad in squads)
            {
                squadMap[squad.Id] = squad;
            }

            for (int index = 0; index < squads.Count; index++)
            {
                SquadTemplate template = squads[index].SquadTemplate;
                if (template != null)
                {
                    squadTypeOrders.TryAdd((unit, template), index);
                }
            }

            foreach (Unit childUnit in unit.ChildUnits ?? Enumerable.Empty<Unit>())
            {
                CollectUnits(childUnit, units, unitOrderKeys, squadMap, squadTypeOrders);
            }
        }

        private static string GetUnitOrderKey(Unit unit)
        {
            Stack<string> segments = [];
            Unit current = unit;
            while (current != null)
            {
                Unit parent = current.ParentUnit;
                if (parent == null)
                {
                    segments.Push($"root:{current.Name}:{current.Id:D8}");
                    break;
                }

                int index = parent.ChildUnits?.IndexOf(current) ?? -1;
                segments.Push(index >= 0 ? $"{index:D8}" : $"unknown:{current.Name}:{current.Id:D8}");
                current = parent;
            }

            return string.Join("/", segments);
        }
    }

    public class SoldierTransferService
    {
        private readonly SoldierTemplateEligibilityService _eligibilityService = new();

        public SoldierTransferContext CreateContext(Unit orderOfBattle) =>
            SoldierTransferContext.Build(orderOfBattle);

        public List<SoldierTransferOption> GetTransferOptions(
            Unit orderOfBattle,
            PlayerSoldier soldier,
            bool includeCurrentAssignment = false)
        {
            if (orderOfBattle == null || soldier?.AssignedSquad == null)
            {
                return [];
            }

            return GetTransferOptions(CreateContext(orderOfBattle), soldier, includeCurrentAssignment);
        }

        public List<SoldierTransferOption> GetTransferOptions(
            SoldierTransferContext context,
            PlayerSoldier soldier,
            bool includeCurrentAssignment = false)
        {
            if (context == null || soldier?.AssignedSquad == null)
            {
                return [];
            }

            // Present openings by ascending target rank, so lateral transfers come first,
            // then single-level promotions, then higher jumps. Subrank breaks ties within a
            // rank (e.g. a Veteran move sorts ahead of a Veteran Sergeant promotion, both at
            // Rank 5). Within the same role, preserve order-of-battle company and squad-type
            // order, then alphabetize squads of the same type. The current assignment, when
            // shown, is pinned to the top regardless.
            List<SoldierTransferOption> openings = GetOpeningsInUnit(
                context,
                soldier.AssignedSquad,
                soldier)
                .OrderBy(option => option.SoldierTemplate.Rank)
                .ThenBy(option => option.SoldierTemplate.Subrank)
                .ThenBy(option => GetUnitOrderKey(option, context))
                .ThenBy(option => GetSquadTypeOrder(option, context))
                .ThenBy(option => GetSquadName(option, context), StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.SquadId)
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

        /// <summary>
        /// Cheap candidate-only query. It uses the same eligibility rules as
        /// <see cref="GetTransferOptions(SoldierTransferContext, PlayerSoldier, bool)"/>,
        /// but stops at the first matching opening and does not allocate or sort transfer
        /// options. The full destination list is only needed after the player selects a soldier.
        /// </summary>
        public bool HasLegalTransferOption(
            SoldierTransferContext context,
            PlayerSoldier soldier,
            bool promotionOnly)
        {
            if (context == null || soldier?.AssignedSquad == null)
            {
                return false;
            }

            foreach (SoldierTransferContext.UnitContext unitContext in context.Units)
            {
                foreach (SoldierTransferContext.SquadContext squadContext in unitContext.Squads)
                {
                    if (IsTransferLocationAllowed(soldier.AssignedSquad, squadContext.Squad)
                        && HasOpeningInSquad(squadContext, soldier.AssignedSquad, soldier, promotionOnly))
                    {
                        return true;
                    }
                }

                foreach (SquadTemplateSlot slot in unitContext.Slots)
                {
                    if (!CanCreateSquadInUnit(unitContext.Unit))
                    {
                        continue;
                    }
                    int existing = unitContext.Squads.Count(
                        squadContext => squadContext.Squad.SquadTemplate == slot.Template);
                    if (existing < slot.MaxCount
                        && HasOpeningInEmptySquad(slot.Template, soldier, promotionOnly))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static Unit GetTargetUnit(
            SoldierTransferOption option,
            SoldierTransferContext context)
        {
            return option.IsNewSquad
                ? option.TargetUnit
                : context.SquadMap.TryGetValue(option.SquadId, out Squad squad) ? squad.ParentUnit : null;
        }

        private static string GetUnitOrderKey(
            SoldierTransferOption option,
            SoldierTransferContext context)
        {
            Unit unit = GetTargetUnit(option, context);
            return unit != null && context.UnitOrderKeys.TryGetValue(unit, out string orderKey)
                ? orderKey
                : "zzzzzzzz";
        }

        private static int GetSquadTypeOrder(
            SoldierTransferOption option,
            SoldierTransferContext context)
        {
            Unit unit = GetTargetUnit(option, context);
            SquadTemplate targetTemplate = option.IsNewSquad
                ? option.TargetSquadTemplate
                : context.SquadMap.TryGetValue(option.SquadId, out Squad squad) ? squad.SquadTemplate : null;
            if (unit == null || targetTemplate == null)
            {
                return int.MaxValue;
            }

            return context.SquadTypeOrders.TryGetValue((unit, targetTemplate), out int index)
                ? index
                : int.MaxValue;
        }

        private static string GetSquadName(
            SoldierTransferOption option,
            SoldierTransferContext context)
        {
            if (option.IsNewSquad)
            {
                return $"New {option.TargetSquadTemplate?.Name}";
            }

            return context.SquadMap.TryGetValue(option.SquadId, out Squad squad)
                ? squad.Name
                : option.DisplayName;
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
                option.IsCurrentAssignment || option.IsNewSquad || option.IsProvisionalSquad)
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

        /// <summary>
        /// Returns the role slots a soldier could fill in a staged formation whose members do not
        /// exist in the live order of battle yet. The caller supplies the projected member roles,
        /// including the founding Sergeant, so rank-and-file slots become available as soon as the
        /// provisional formation has a leader.
        /// </summary>
        public IReadOnlyList<SoldierTemplate> GetProvisionalSquadOpenings(
            SquadTemplate squadTemplate,
            IEnumerable<SoldierTemplate> projectedMembers,
            PlayerSoldier soldier)
        {
            if (squadTemplate == null || soldier == null)
            {
                return [];
            }

            List<SoldierTemplate> members = projectedMembers?.ToList() ?? [];
            bool hasSquadLeader = members.Any(template => template?.IsSquadLeader == true);
            List<SoldierTemplate> openings = [];
            foreach (SquadTemplateElement element in squadTemplate.Elements)
            {
                if ((squadTemplate.SquadType & SquadTypes.Administrative) == 0
                    && element.SoldierTemplate.IsSquadLeader == hasSquadLeader)
                {
                    continue;
                }
                if (members.Count(member => member == element.SoldierTemplate)
                    >= element.MaximumNumber)
                {
                    continue;
                }
                if (!IsRankEligible(element.SoldierTemplate, soldier.Template)
                    || !IsSpecialistEligible(element.SoldierTemplate, soldier.Template)
                    || !_eligibilityService.IsEligible(soldier, element.SoldierTemplate))
                {
                    continue;
                }
                openings.Add(element.SoldierTemplate);
            }
            return openings;
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
            // Compatibility-bearing Scouts are campaign-recruited neophytes. A role
            // change is resolved only by the Black Carapace procedure, which performs
            // the reserved transfer on success. Founding Scouts have no score and keep
            // the ordinary immediate transfer flow.
            if (RequiresBlackCarapace(soldier, option))
            {
                return false;
            }
            // A brother attached to an operation is in the field with someone else's force;
            // reposting him mid-operation is not a decision the Chapter screen may make. The
            // UI hides the options, but this is the enforcement point
            // (Design/Reference/SpecialistAttachment.md §3.4).
            if (soldier.AttachedOrder != null)
            {
                return false;
            }
            if (soldier.AssignedSquad == null)
            {
                throw new InvalidOperationException("Cannot transfer a soldier with no assigned squad.");
            }
            if (!_eligibilityService.IsEligible(soldier, option.SoldierTemplate))
            {
                return false;
            }
            Squad newSquad;
            if (option.IsNewSquad)
            {
                if (option.TargetUnit == null || option.TargetSquadTemplate == null)
                {
                    throw new InvalidOperationException("New-squad transfer option is missing its target unit or template.");
                }
                if (!CanCreateSquadInUnit(option.TargetUnit))
                {
                    return false;
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

            newSquad.AddSquadMember(soldier);
            UpdateSquadLocations(currentSquad, newSquad);

            if (soldier.Template != option.SoldierTemplate)
            {
                soldier.AddEvent(new SoldierEvent(date, SoldierEventType.Promotion,
                    $"promoted to {option.SoldierTemplate.Name}"));
                soldier.Template = option.SoldierTemplate;
            }

            if (currentSquad.Members.Count == 0)
            {
                new SquadLifecycleService(squadMap: squadMap as IDictionary<int, Squad>)
                    .HandleEmptySquad(currentSquad);
            }

            if (currentSquad != newSquad)
            {
                soldier.AddEvent(new SoldierEvent(date, SoldierEventType.Transfer,
                    $"transferred to {option.DisplayName}"));
            }

            return true;
        }

        public static bool RequiresBlackCarapace(
            PlayerSoldier soldier,
            SoldierTransferOption option)
        {
            return soldier?.GeneticCompatibility.HasValue == true
                && (soldier.AssignedSquad?.SquadTemplate?.SquadType
                    & SquadTypes.Scout) != 0
                && option?.SoldierTemplate != null
                && option.SoldierTemplate != soldier.Template;
        }

        private static void UpdateSquadLocations(Squad oldSquad, Squad newSquad)
        {
            if (oldSquad == newSquad)
            {
                return;
            }

            if (newSquad.Members.Count == 1)
            {
                // Location and orders have both a squad-side pointer and an owning roster.
                // An empty squad may still retain stale registration from an earlier posting,
                // so detach it before inheriting the source squad's active deployment.
                SquadLifecycleService.DetachDeployment(newSquad);
                if (newSquad.IsOperational)
                {
                    newSquad.CurrentRegion = oldSquad.CurrentRegion;
                    newSquad.BoardedLocation = oldSquad.BoardedLocation;
                    newSquad.BoardedLocation?.LoadSquad(newSquad);

                    RegionFaction regionFaction = FindRegionFaction(oldSquad);
                    if (regionFaction != null && !regionFaction.LandedSquads.Contains(newSquad))
                    {
                        regionFaction.LandedSquads.Add(newSquad);
                    }

                    newSquad.CurrentOrders = oldSquad.CurrentOrders;
                    if (newSquad.CurrentOrders != null
                        && !newSquad.CurrentOrders.AssignedSquads.Contains(newSquad))
                    {
                        newSquad.CurrentOrders.AssignedSquads.Add(newSquad);
                    }
                }
            }
            if (oldSquad.Members.Count == 0)
            {
                SquadLifecycleService.DetachDeployment(oldSquad);
            }
        }

        private static RegionFaction FindRegionFaction(Squad squad)
        {
            if (squad?.CurrentRegion == null)
            {
                return null;
            }

            if (squad.Faction != null
                && squad.CurrentRegion.RegionFactionMap.TryGetValue(
                    squad.Faction.Id, out RegionFaction factionPresence))
            {
                return factionPresence;
            }

            return squad.CurrentRegion.RegionFactionMap.Values
                .FirstOrDefault(regionFaction => regionFaction.LandedSquads.Contains(squad));
        }

        private List<SoldierTransferOption> GetOpeningsInUnit(
            SoldierTransferContext context,
            Squad currentSquad,
            PlayerSoldier soldier)
        {
            List<SoldierTransferOption> openSlots = [];
            foreach (SoldierTransferContext.UnitContext unitContext in context.Units)
            {
                foreach (SoldierTransferContext.SquadContext squadContext in unitContext.Squads)
                {
                    Squad squad = squadContext.Squad;
                    if (!IsTransferLocationAllowed(currentSquad, squad))
                    {
                        continue;
                    }
                    IEnumerable<SoldierTemplate> squadSlots = GetOpeningsInSquad(
                        squadContext, currentSquad, soldier);
                    foreach (SoldierTemplate template in squadSlots)
                    {
                        openSlots.Add(new SoldierTransferOption(
                            squad.Id,
                            template,
                            FormatExistingSquadDisplay(
                                template, squad, unitContext.Unit)));
                    }
                }
                // New-squad openings: any squad template the unit may still hold more of.
                // A brand-new squad is empty, so only its leader slot is open — i.e. the
                // soldier starts the squad by becoming its sergeant.
                if (!CanCreateSquadInUnit(unitContext.Unit))
                {
                    continue;
                }
                foreach (SquadTemplateSlot slot in unitContext.Slots)
                {
                    int existing = unitContext.Squads.Count(
                        squadContext => squadContext.Squad.SquadTemplate == slot.Template);
                    if (existing >= slot.MaxCount)
                    {
                        continue;
                    }
                    foreach (SoldierTemplate template in GetOpeningsInEmptySquad(slot.Template, soldier))
                    {
                        openSlots.Add(new SoldierTransferOption(
                            0,
                            template,
                            $"{template.Name}, New {slot.Template.Name}, {unitContext.Unit.Name}",
                            IsNewSquad: true,
                            TargetUnit: unitContext.Unit,
                            TargetSquadTemplate: slot.Template));
                    }
                }
            }

            return openSlots;
        }

        private static string FormatExistingSquadDisplay(
            SoldierTemplate template,
            Squad squad,
            Unit unit)
        {
            string display = $"{template.Name}, {squad.Name}";
            bool squadNameCarriesCompany = squad.FormationOrdinal.HasValue
                || SquadDesignationFormatter.IsNumberedLineFormation(squad)
                || (!string.IsNullOrWhiteSpace(unit?.Name)
                    && squad.Name?.Contains(unit.Name, StringComparison.OrdinalIgnoreCase) == true);
            return squadNameCarriesCompany || string.IsNullOrWhiteSpace(unit?.Name)
                ? display
                : $"{display}, {unit.Name}";
        }

        // Company line formations are not available until the company's HQ has been founded.
        // HQ squads are created eagerly and remain visible while empty, so the presence of the
        // squad itself is not enough: a squad leader (normally the Captain) must be assigned.
        // Units without an HQ template, such as the chapter root in focused tests, retain their
        // existing on-demand squad behavior.
        private static bool CanCreateSquadInUnit(Unit unit) =>
            unit?.HQSquad == null || unit.HQSquad.SquadLeader != null;

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
            return controller != null
                   && FactionRelationshipService.IsImperial(controller.PlanetFaction.Faction);
        }

        private static Planet GetSquadPlanet(Squad squad)
        {
            if (squad.CurrentRegion != null)
            {
                return squad.CurrentRegion.Planet;
            }
            return squad.BoardedLocation?.Fleet?.Planet;
        }

        private IEnumerable<SoldierTemplate> GetOpeningsInEmptySquad(
            SquadTemplate squadTemplate,
            PlayerSoldier soldier)
        {
            List<SoldierTemplate> openSpots = [];
            foreach (SquadTemplateElement element in squadTemplate.Elements)
            {
                if (IsOpeningInEmptySquad(element, soldier, promotionOnly: false))
                {
                    openSpots.Add(element.SoldierTemplate);
                }
            }

            return openSpots;
        }

        private bool HasOpeningInEmptySquad(
            SquadTemplate squadTemplate,
            PlayerSoldier soldier,
            bool promotionOnly)
        {
            return squadTemplate.Elements.Any(element =>
                IsOpeningInEmptySquad(element, soldier, promotionOnly));
        }

        private bool IsOpeningInEmptySquad(
            SquadTemplateElement element,
            PlayerSoldier soldier,
            bool promotionOnly)
        {
            // An empty squad has no leader, so only leader-eligible slots are open.
            return element.SoldierTemplate.IsSquadLeader
                && (!promotionOnly || element.SoldierTemplate.Rank > soldier.Template.Rank)
                && IsRankEligible(element.SoldierTemplate, soldier.Template)
                && IsSpecialistEligible(element.SoldierTemplate, soldier.Template)
                && _eligibilityService.IsEligible(soldier, element.SoldierTemplate)
                && element.MaximumNumber > 0;
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

        private IEnumerable<SoldierTemplate> GetOpeningsInSquad(
            SoldierTransferContext.SquadContext squadContext,
            Squad currentSquad,
            PlayerSoldier soldier)
        {
            List<SoldierTemplate> openSpots = [];
            foreach (SquadTemplateElement element in squadContext.Squad.SquadTemplate.Elements)
            {
                if (IsOpeningInSquad(squadContext, currentSquad, soldier, element, promotionOnly: false))
                {
                    openSpots.Add(element.SoldierTemplate);
                }
            }

            return openSpots;
        }

        private bool HasOpeningInSquad(
            SoldierTransferContext.SquadContext squadContext,
            Squad currentSquad,
            PlayerSoldier soldier,
            bool promotionOnly)
        {
            return squadContext.Squad.SquadTemplate.Elements.Any(element =>
                IsOpeningInSquad(squadContext, currentSquad, soldier, element, promotionOnly));
        }

        private bool IsOpeningInSquad(
            SoldierTransferContext.SquadContext squadContext,
            Squad currentSquad,
            PlayerSoldier soldier,
            SquadTemplateElement element,
            bool promotionOnly)
        {
            // A squad has exactly one leader: offer a leader slot only while the
            // squad is leaderless, and offer rank-and-file slots only once a leader
            // is in place. Administrative squads are the exception because their
            // leader elements are staff qualifications rather than command seats.
            if (!squadContext.Squad.IsAdministrative
                && element.SoldierTemplate.IsSquadLeader == squadContext.HasSquadLeader)
            {
                return false;
            }
            if (currentSquad == squadContext.Squad
                && element.SoldierTemplate == soldier.Template)
            {
                return false;
            }
            if (promotionOnly && element.SoldierTemplate.Rank <= soldier.Template.Rank)
            {
                return false;
            }
            if (!IsRankEligible(element.SoldierTemplate, soldier.Template)
                || !IsSpecialistEligible(element.SoldierTemplate, soldier.Template))
            {
                return false;
            }

            if (!_eligibilityService.IsEligible(soldier, element.SoldierTemplate))
            {
                return false;
            }

            int existingHeadcount = squadContext.TypeCounts.TryGetValue(
                element.SoldierTemplate, out int count) ? count : 0;
            return existingHeadcount < element.MaximumNumber;
        }
    }
}
