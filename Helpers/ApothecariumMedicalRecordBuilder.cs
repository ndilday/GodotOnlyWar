using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public class ApothecariumMedicalRecordBuilder
    {
        private const int FirstProgenoidMaturesWeeks = 5 * 52;
        private const int SecondProgenoidMaturesWeeks = 10 * 52;

        public IReadOnlyList<ApothecariumTreeItem> BuildTree(Unit chapter, ApothecariumSelectionKind selectedKind, int? selectedId, bool woundedOnly = true)
        {
            if (chapter == null)
            {
                return [];
            }

            List<ApothecariumTreeItem> items = [];
            foreach (Unit company in chapter.ChildUnits ?? [])
            {
                ApothecariumTreeItem item = BuildUnitTreeItem(company, selectedKind, selectedId, woundedOnly);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            foreach (Squad squad in chapter.Squads ?? [])
            {
                ApothecariumTreeItem item = BuildSquadTreeItem(squad, selectedKind, selectedId, woundedOnly);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        public GeneSeedVaultSummary BuildVault(PlayerForce force, Date currentDate)
        {
            IReadOnlyList<PlayerSoldier> soldiers = GetPlayerSoldiers(force);
            int stockpile = force?.GeneseedStockpile ?? 0;
            int matureImplanted = soldiers.Sum(soldier => CountMatureProgenoids(soldier, currentDate));
            int immatureImplanted = Math.Max(0, soldiers.Count * 2 - matureImplanted);
            int maturingSoon = soldiers.Sum(soldier => CountMaturingWithinOneYear(soldier, currentDate));
            int atRisk = soldiers.Sum(CountAtRiskProgenoids);
            MedicalSeverity severity = atRisk > 0 ? MedicalSeverity.Watch : MedicalSeverity.Stable;
            string purityStatus = atRisk > 0 ? "Watch" : "Stable";

            return new GeneSeedVaultSummary(
                stockpile,
                matureImplanted,
                immatureImplanted,
                maturingSoon,
                atRisk,
                purityStatus,
                [
                    new GeneSeedVaultRow("Mature sealed progenoids", "Available in the Chapter vault.", stockpile.ToString(), MedicalSeverity.Stable),
                    new GeneSeedVaultRow("Mature implanted progenoids", "Recoverable from living battle-brothers.", matureImplanted.ToString(), MedicalSeverity.Stable),
                    new GeneSeedVaultRow("Immature implanted progenoids", "Still maturing in active battle-brothers.", immatureImplanted.ToString(), MedicalSeverity.Watch),
                    new GeneSeedVaultRow("Maturing within one year", "Expected to become recoverable soon.", maturingSoon.ToString(), MedicalSeverity.Stable),
                    new GeneSeedVaultRow("At-risk implanted progenoids", "Held in damaged or severed locations.", atRisk.ToString(), severity)
                ],
                BuildFormationSummaries(force, currentDate),
                force?.Army?.Requisition ?? 0);
        }

        public MedicalUnitSummary BuildUnitSummary(Unit unit)
        {
            IReadOnlyList<ISoldier> soldiers = GetUnitMembers(unit);
            return BuildUnitSummary(
                ApothecariumSelectionKind.Unit,
                unit?.Id ?? 0,
                "chapter",
                unit?.Name ?? "Unit",
                $"{CountWounded(soldiers)} wounded / {CountOutOfAction(soldiers)} out of action",
                soldiers);
        }

        public MedicalUnitSummary BuildSquadSummary(Squad squad)
        {
            IReadOnlyList<ISoldier> soldiers = squad?.Members?.ToList() ?? [];
            return BuildUnitSummary(
                ApothecariumSelectionKind.Squad,
                squad?.Id ?? 0,
                GetSquadIconKey(squad),
                squad?.Name ?? "Squad",
                $"{squad?.ParentUnit?.Name ?? "Unassigned"} - {CountWounded(soldiers)} wounded / {CountOutOfAction(soldiers)} out",
                soldiers);
        }

        public MedicalSoldierSummary BuildSoldierSummary(ISoldier soldier)
        {
            if (soldier == null)
            {
                return new MedicalSoldierSummary(0, "wounded", "No Soldier", "No assignment.", false, 0, "Unknown", MedicalSeverity.None, [], []);
            }

            List<WoundLocationSummary> wounds = soldier.Body.HitLocations
                .Select(BuildWoundLocationSummary)
                .ToList();
            MedicalSeverity worstSeverity = wounds.Count == 0 ? MedicalSeverity.None : wounds.Max(w => w.Severity);
            int maxRecovery = wounds.Select(w => ParseRecoveryWeeks(w.Recovery)).DefaultIfEmpty(0).Max();
            string geneSeedStatus = BuildGeneSeedStatus(wounds);

            return new MedicalSoldierSummary(
                soldier.Id,
                GetSoldierIconKey(soldier),
                $"{soldier.Template?.Name ?? "Battle-Brother"} {soldier.Name}",
                $"{soldier.AssignedSquad?.Name ?? "Unassigned"}, {soldier.AssignedSquad?.ParentUnit?.Name ?? "No Company"}",
                soldier.CanFight,
                maxRecovery,
                geneSeedStatus,
                worstSeverity,
                wounds,
                BuildReplacementOptions(soldier.Body.HitLocations));
        }

        public static string GetSoldierIconKey(ISoldier soldier)
        {
            if (soldier == null || !soldier.CanFight)
            {
                return "wounded";
            }

            return soldier.Template?.IsSquadLeader == true ? "rank_sergeant" : "rank_battle_brother";
        }

        private ApothecariumTreeItem BuildUnitTreeItem(Unit unit, ApothecariumSelectionKind selectedKind, int? selectedId, bool woundedOnly)
        {
            List<ApothecariumTreeItem> children = [];
            foreach (Unit childUnit in unit.ChildUnits ?? [])
            {
                ApothecariumTreeItem child = BuildUnitTreeItem(childUnit, selectedKind, selectedId, woundedOnly);
                if (child != null)
                {
                    children.Add(child);
                }
            }

            foreach (Squad squad in unit.Squads ?? [])
            {
                ApothecariumTreeItem child = BuildSquadTreeItem(squad, selectedKind, selectedId, woundedOnly);
                if (child != null)
                {
                    children.Add(child);
                }
            }

            IReadOnlyList<ISoldier> members = GetUnitMembers(unit);
            bool relevant = children.Count > 0 || !woundedOnly || members.Any(IsMedicallyRelevant);
            if (!relevant)
            {
                return null;
            }

            return new ApothecariumTreeItem(
                ApothecariumSelectionKind.Unit,
                unit.Id,
                "chapter",
                unit.Name,
                $"{CountWounded(members)} wounded / {CountOutOfAction(members)} out",
                "",
                SeverityForSoldiers(members),
                selectedKind == ApothecariumSelectionKind.Unit && selectedId == unit.Id,
                children);
        }

        private ApothecariumTreeItem BuildSquadTreeItem(Squad squad, ApothecariumSelectionKind selectedKind, int? selectedId, bool woundedOnly)
        {
            List<ApothecariumTreeItem> children = [];
            foreach (ISoldier soldier in squad.Members ?? [])
            {
                if (!woundedOnly || IsMedicallyRelevant(soldier))
                {
                    children.Add(BuildSoldierTreeItem(soldier, selectedKind, selectedId));
                }
            }

            IReadOnlyList<ISoldier> members = squad.Members?.ToList() ?? [];
            bool relevant = children.Count > 0 || !woundedOnly;
            if (!relevant)
            {
                return null;
            }

            return new ApothecariumTreeItem(
                ApothecariumSelectionKind.Squad,
                squad.Id,
                GetSquadIconKey(squad),
                squad.Name,
                $"{CountWounded(members)} wounded / {CountOutOfAction(members)} out",
                "",
                SeverityForSoldiers(members),
                selectedKind == ApothecariumSelectionKind.Squad && selectedId == squad.Id,
                children);
        }

        private ApothecariumTreeItem BuildSoldierTreeItem(ISoldier soldier, ApothecariumSelectionKind selectedKind, int? selectedId)
        {
            MedicalSoldierSummary summary = BuildSoldierSummary(soldier);
            string status = summary.ReplacementOptions.Count > 0
                ? "replacement"
                : summary.MaxRecoveryWeeks > 0
                    ? $"{summary.MaxRecoveryWeeks} wk"
                    : soldier.CanFight ? "ready" : "out";

            return new ApothecariumTreeItem(
                ApothecariumSelectionKind.Soldier,
                soldier.Id,
                summary.IconKey,
                soldier.Name,
                summary.Wounds.FirstOrDefault(w => w.Severity >= MedicalSeverity.Watch)?.Status ?? "wounded",
                status,
                summary.WorstSeverity,
                selectedKind == ApothecariumSelectionKind.Soldier && selectedId == soldier.Id,
                []);
        }

        private MedicalUnitSummary BuildUnitSummary(
            ApothecariumSelectionKind kind,
            int id,
            string iconKey,
            string title,
            string subtitle,
            IReadOnlyList<ISoldier> soldiers)
        {
            int healthy = soldiers.Count(s => !IsWounded(s));
            int wounded = CountWounded(soldiers);
            int outOfAction = CountOutOfAction(soldiers);
            int readyNext = soldiers.Count(s => IsWounded(s) && BuildSoldierSummary(s).MaxRecoveryWeeks <= 1 && s.CanFight);
            int maxRecovery = soldiers.Select(s => BuildSoldierSummary(s).MaxRecoveryWeeks).DefaultIfEmpty(0).Max();

            return new MedicalUnitSummary(
                kind,
                id,
                iconKey,
                title,
                subtitle,
                healthy,
                wounded,
                outOfAction,
                readyNext,
                maxRecovery,
                BuildSeriousWoundRows(soldiers));
        }

        private IReadOnlyList<MedicalSeriousWoundRow> BuildSeriousWoundRows(IReadOnlyList<ISoldier> soldiers)
        {
            List<MedicalSeriousWoundRow> rows = [];
            foreach (ISoldier soldier in soldiers)
            {
                MedicalSoldierSummary summary = BuildSoldierSummary(soldier);
                WoundLocationSummary wound = summary.Wounds
                    .Where(w => w.Severity >= MedicalSeverity.Watch || w.NeedsReplacement)
                    .OrderByDescending(w => w.Severity)
                    .FirstOrDefault();
                if (wound == null)
                {
                    continue;
                }

                rows.Add(new MedicalSeriousWoundRow(
                    soldier.Id,
                    soldier.Name,
                    $"{wound.LocationName}: {wound.Status}",
                    summary.ReplacementOptions.Count > 0 ? "replacement required" : wound.Recovery,
                    summary.ReplacementOptions.Count > 0 ? "assign replacement" : soldier.CanFight ? "monitor" : "recover",
                    wound.Severity));
            }

            return rows
                .OrderByDescending(r => r.Severity)
                .ThenBy(r => r.SoldierName)
                .ToList();
        }

        private WoundLocationSummary BuildWoundLocationSummary(HitLocation location)
        {
            MedicalSeverity severity = GetSeverity(location);
            bool needsReplacement = location.IsReplacementEligible;
            string status = GetStatus(location);
            string recovery = needsReplacement
                ? "Replacement required"
                : location.Wounds.RecoveryTimeLeft() > 0
                    ? $"{location.Wounds.RecoveryTimeLeft()} weeks"
                    : "No delay";

            return new WoundLocationSummary(
                location.Template.Id,
                location.Template.Name,
                status,
                recovery,
                location.Template.HoldsProgenoid,
                location.IsCybernetic,
                needsReplacement,
                severity);
        }

        private IReadOnlyList<ReplacementOption> BuildReplacementOptions(IEnumerable<HitLocation> locations)
        {
            List<ReplacementOption> options = [];
            foreach (HitLocation location in locations.Where(l => l.IsReplacementEligible))
            {
                bool severed = location.IsSevered;
                options.Add(new ReplacementOption(
                    location.Template.Id,
                    MedicalProcedureType.Cybernetic,
                    location.Template.Name,
                    $"Cybernetic {location.Template.Name}",
                    "Fastest return to duty; adds an augmetic replacement.",
                    MedicalProcedureRules.GetWeeks(MedicalProcedureType.Cybernetic, severed),
                    MedicalProcedureRules.GetRequisitionCost(MedicalProcedureType.Cybernetic, severed),
                    true));
                options.Add(new ReplacementOption(
                    location.Template.Id,
                    MedicalProcedureType.VatGrown,
                    location.Template.Name,
                    $"Vat-Grown {location.Template.Name}",
                    "Rare restorative treatment; slower and more resource intensive.",
                    MedicalProcedureRules.GetWeeks(MedicalProcedureType.VatGrown, severed),
                    MedicalProcedureRules.GetRequisitionCost(MedicalProcedureType.VatGrown, severed),
                    true));
            }

            return options;
        }

        private IReadOnlyList<GeneSeedFormationSummary> BuildFormationSummaries(PlayerForce force, Date currentDate)
        {
            Unit chapter = force?.Army?.OrderOfBattle;
            if (chapter == null)
            {
                return [];
            }

            List<GeneSeedFormationSummary> summaries = [];
            foreach (Unit company in chapter.ChildUnits ?? [])
            {
                IReadOnlyList<PlayerSoldier> soldiers = GetUnitMembers(company).OfType<PlayerSoldier>().ToList();
                if (soldiers.Count == 0)
                {
                    continue;
                }

                int atRisk = soldiers.Sum(CountAtRiskProgenoids);
                MedicalSeverity severity = atRisk > 0 ? MedicalSeverity.Watch : MedicalSeverity.Stable;
                summaries.Add(new GeneSeedFormationSummary(
                    company.Name,
                    soldiers.Sum(s => CountMatureProgenoids(s, currentDate)),
                    soldiers.Sum(s => Math.Max(0, 2 - CountMatureProgenoids(s, currentDate))),
                    atRisk,
                    atRisk > 0 ? "Watch" : "Stable",
                    severity));
            }

            return summaries;
        }

        private static IReadOnlyList<PlayerSoldier> GetPlayerSoldiers(PlayerForce force)
        {
            if (force?.Army?.PlayerSoldierMap != null)
            {
                return force.Army.PlayerSoldierMap.Values.ToList();
            }

            return force?.Army?.OrderOfBattle?.GetAllMembers()?.OfType<PlayerSoldier>().ToList() ?? [];
        }

        private static IReadOnlyList<ISoldier> GetUnitMembers(Unit unit)
        {
            return unit?.GetAllMembers()?.ToList() ?? [];
        }

        private static int CountMatureProgenoids(PlayerSoldier soldier, Date currentDate)
        {
            int ageWeeks = GetImplantAgeWeeks(soldier, currentDate);
            if (ageWeeks >= SecondProgenoidMaturesWeeks)
            {
                return 2;
            }
            if (ageWeeks >= FirstProgenoidMaturesWeeks)
            {
                return 1;
            }

            return 0;
        }

        private static int CountMaturingWithinOneYear(PlayerSoldier soldier, Date currentDate)
        {
            int ageWeeks = GetImplantAgeWeeks(soldier, currentDate);
            int oneYear = 52;
            int count = 0;
            if (ageWeeks >= FirstProgenoidMaturesWeeks - oneYear && ageWeeks < FirstProgenoidMaturesWeeks)
            {
                count++;
            }
            if (ageWeeks >= SecondProgenoidMaturesWeeks - oneYear && ageWeeks < SecondProgenoidMaturesWeeks)
            {
                count++;
            }

            return count;
        }

        private static int GetImplantAgeWeeks(PlayerSoldier soldier, Date currentDate)
        {
            if (soldier?.ProgenoidImplantDate == null || currentDate == null)
            {
                return 0;
            }

            return currentDate.GetWeeksDifference(soldier.ProgenoidImplantDate);
        }

        private static int CountAtRiskProgenoids(ISoldier soldier)
        {
            return soldier.Body.HitLocations.Count(hl => hl.Template.HoldsProgenoid && (hl.IsCrippled || hl.IsSevered));
        }

        private static int CountWounded(IReadOnlyList<ISoldier> soldiers)
        {
            return soldiers.Count(IsWounded);
        }

        private static int CountOutOfAction(IReadOnlyList<ISoldier> soldiers)
        {
            return soldiers.Count(s => !s.CanFight);
        }

        private static bool IsWounded(ISoldier soldier)
        {
            return soldier.Body.HitLocations.Any(hl => hl.Wounds.WoundTotal > 0 || hl.IsSevered);
        }

        private static bool IsMedicallyRelevant(ISoldier soldier)
        {
            return IsWounded(soldier) || !soldier.CanFight || soldier.Body.HitLocations.Any(hl => hl.IsCybernetic);
        }

        private static MedicalSeverity SeverityForSoldiers(IReadOnlyList<ISoldier> soldiers)
        {
            return soldiers.Count == 0
                ? MedicalSeverity.None
                : soldiers.Select(s => BuildWorstSeverity(s.Body.HitLocations)).DefaultIfEmpty(MedicalSeverity.None).Max();
        }

        private static MedicalSeverity BuildWorstSeverity(IEnumerable<HitLocation> locations)
        {
            return locations.Select(GetSeverity).DefaultIfEmpty(MedicalSeverity.None).Max();
        }

        private static MedicalSeverity GetSeverity(HitLocation location)
        {
            if (location.IsSevered)
            {
                return MedicalSeverity.Lost;
            }
            if (location.IsCrippled)
            {
                return MedicalSeverity.Critical;
            }
            if (location.Wounds.MortalWounds > 0 || location.Wounds.UnsurvivableWounds > 0 || location.Wounds.MassiveWounds > 0)
            {
                return MedicalSeverity.Critical;
            }
            if (location.Wounds.CriticalWounds > 0 || location.Wounds.MajorWounds > 0)
            {
                return MedicalSeverity.Serious;
            }
            if (location.Wounds.ModerateWounds > 0)
            {
                return MedicalSeverity.Watch;
            }
            if (location.Wounds.MinorWounds > 0 || location.Wounds.NegligibleWounds > 0)
            {
                return MedicalSeverity.Stable;
            }

            return MedicalSeverity.None;
        }

        private static string GetStatus(HitLocation location)
        {
            if (location.IsCybernetic)
            {
                return "Cybernetic";
            }
            if (location.IsSevered)
            {
                return "Severed";
            }
            if (location.IsCrippled)
            {
                return "Crippled";
            }
            if (location.Wounds.UnsurvivableWounds > 0)
            {
                return "Unsurvivable";
            }
            if (location.Wounds.MortalWounds > 0)
            {
                return "Mortal";
            }
            if (location.Wounds.MassiveWounds > 0)
            {
                return "Massive";
            }
            if (location.Wounds.CriticalWounds > 0)
            {
                return "Critical";
            }
            if (location.Wounds.MajorWounds > 0)
            {
                return "Major";
            }
            if (location.Wounds.ModerateWounds > 0)
            {
                return "Moderate";
            }
            if (location.Wounds.MinorWounds > 0)
            {
                return "Minor";
            }
            if (location.Wounds.NegligibleWounds > 0)
            {
                return "Negligible";
            }

            return "Clear";
        }

        private static string BuildGeneSeedStatus(IReadOnlyList<WoundLocationSummary> wounds)
        {
            if (wounds.Any(w => w.HoldsProgenoid && w.Severity == MedicalSeverity.Lost))
            {
                return "Lost";
            }
            if (wounds.Any(w => w.HoldsProgenoid && w.Severity >= MedicalSeverity.Critical))
            {
                return "At Risk";
            }
            if (wounds.Any(w => w.HoldsProgenoid && w.Severity >= MedicalSeverity.Watch))
            {
                return "Watch";
            }

            return "Safe";
        }

        private static int ParseRecoveryWeeks(string recovery)
        {
            if (string.IsNullOrWhiteSpace(recovery))
            {
                return 0;
            }

            string number = new(recovery.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(number, out int weeks) ? weeks : 0;
        }

        private static string GetSquadIconKey(Squad squad)
        {
            if (squad == null)
            {
                return "infantry";
            }

            SquadTypes type = squad.SquadTemplate?.SquadType ?? SquadTypes.None;
            if ((type & SquadTypes.HQ) != 0) return "hq";
            if ((type & SquadTypes.Scout) != 0) return "scout";
            if ((type & SquadTypes.Elite) != 0) return "elite";
            if ((type & SquadTypes.Fast) != 0) return "fast";
            if ((type & SquadTypes.Heavy) != 0) return "heavy";
            if ((type & SquadTypes.Bodyguard) != 0) return "bodyguard";
            return "infantry";
        }
    }
}
