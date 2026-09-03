using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Helpers;
using OnlyWar.Helpers.PlanetaryOperations;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    // The kind of mission being offered. Mirrors the synthetic mission codes that
    // OrderDialogController.PopulateMissions used to hand the OptionButton directly
    // (Recon=-9, Advance/Move=-2, Defend=-3, Patrol=-4, Construction=-5/-6/-7, Diversion=-8),
    // plus Special for anything sourced from Region.SpecialMissions (real Mission.Id).
    public enum MissionAvailabilityKind
    {
        Recon,
        Defend,
        Patrol,
        FortifyEntrenchment,
        BuildListeningPost,
        BuildAntiAir,
        Attack,
        Diversion,
        Move,
        Special
    }

    // A single mission option offered to the player for a given origin/target region pair.
    // SpecialMission is non-null only when Kind == Special, and carries the real Mission
    // (with its real Mission.Id) sourced from Region.SpecialMissions.
    public class AvailableMission
    {
        public string Label { get; }
        public MissionAvailabilityKind Kind { get; }
        public Mission SpecialMission { get; }
        // Attack options are faction-specific. Keeping the target on the descriptor means the
        // button the player selects is the complete order intent rather than half of it, with a
        // separate dropdown supplying the strategically important half later.
        public RegionFaction TargetFaction { get; }

        public AvailableMission(
            string label,
            MissionAvailabilityKind kind,
            Mission specialMission = null,
            RegionFaction targetFaction = null)
        {
            Label = label;
            Kind = kind;
            SpecialMission = specialMission;
            TargetFaction = targetFaction;
        }

        public bool RepresentsSameOption(AvailableMission other)
        {
            if (other == null || Kind != other.Kind) return false;
            if (Kind == MissionAvailabilityKind.Attack)
            {
                return TargetFaction?.PlanetFaction?.Faction?.Id
                    == other.TargetFaction?.PlanetFaction?.Faction?.Id;
            }
            if (Kind != MissionAvailabilityKind.Special) return true;

            return SpecialMission != null
                && other.SpecialMission != null
                && SpecialMission.Id == other.SpecialMission.Id;
        }

        public string IdentityKey => Kind switch
        {
            MissionAvailabilityKind.Special => $"Special:{SpecialMission?.Id}",
            MissionAvailabilityKind.Attack =>
                $"Attack:{TargetFaction?.PlanetFaction?.Faction?.Id}",
            _ => Kind.ToString()
        };

        public bool RepresentsOrder(Order order)
        {
            Mission orderMission = order?.Mission;
            if (orderMission == null) return false;

            return Kind switch
            {
                MissionAvailabilityKind.Recon => orderMission.MissionType == MissionType.Recon,
                MissionAvailabilityKind.Defend => orderMission.MissionType == MissionType.DefenseInDepth,
                MissionAvailabilityKind.Patrol => orderMission.MissionType == MissionType.Patrol,
                MissionAvailabilityKind.FortifyEntrenchment =>
                    orderMission is ConstructionMission construction
                    && construction.ConstructionType == DefenseType.Entrenchment,
                MissionAvailabilityKind.BuildListeningPost =>
                    orderMission is ConstructionMission construction
                    && construction.ConstructionType == DefenseType.ListeningPost,
                MissionAvailabilityKind.BuildAntiAir =>
                    orderMission is ConstructionMission construction
                    && construction.ConstructionType == DefenseType.AntiAir,
                MissionAvailabilityKind.Attack =>
                    orderMission.MissionType == MissionType.Advance
                    && TargetFaction != null
                    && orderMission.RegionFaction?.PlanetFaction?.Faction?.Id
                        == TargetFaction.PlanetFaction.Faction.Id,
                MissionAvailabilityKind.Move =>
                    orderMission.MissionType == MissionType.Advance
                    && orderMission.RegionFaction?.PlanetFaction?.Faction?.IsPlayerFaction == true,
                MissionAvailabilityKind.Diversion =>
                    orderMission.MissionType == MissionType.Diversion,
                MissionAvailabilityKind.Special =>
                    SpecialMission != null && SpecialMission.Id == orderMission.Id,
                _ => false
            };
        }
    }

    // Pure-logic extraction of OrderDialogController.PopulateMissions' branching, so it can be
    // reused by other UI (e.g. a future multi-squad operations board) without depending on Godot
    // or the dialog. Reproduces the original branching EXACTLY - see the moved comments below.
    public static class MissionAvailability
    {
        public static string GetConstructionLabel(DefenseType constructionType) =>
            constructionType switch
            {
                DefenseType.Entrenchment => "Build Fortifications",
                DefenseType.ListeningPost => "Build Listening Post",
                DefenseType.AntiAir => "Build Anti-Air",
                _ => MissionType.Construction.ToString()
            };

        public static string GetOrderLabel(Mission mission)
        {
            if (mission is ConstructionMission construction)
            {
                return GetConstructionLabel(construction.ConstructionType);
            }
            if (mission?.MissionType == MissionType.Advance)
            {
                Faction target = mission.RegionFaction?.PlanetFaction?.Faction;
                return target?.IsPlayerFaction == true
                    ? "Move"
                    : target == null ? "Attack" : $"Attack ({target.Name})";
            }
            return mission == null
                ? string.Empty
                : SpecialMissionPresentation.GetMissionTypeLabel(mission.MissionType);
        }

        public static IReadOnlyList<AvailableMission> GetAvailableMissions(Region originRegion, Region targetRegion)
        {
            List<AvailableMission> missionOptions = new List<AvailableMission>();
            if (targetRegion?.Planet?.RelationshipLedger != null)
            {
                FactionIntelligenceService.ObservePublicActivity(targetRegion.Planet, 0);
            }
            List<RegionFaction> publicEnemies = IntelligenceTargetService
                .GetPlayerVisibleTargets(targetRegion)
                .Select(target => target.CurrentPresence)
                .Where(rf => rf != null && rf.IsPublic)
                .OrderBy(rf => rf.PlanetFaction.Faction.Name)
                .ThenBy(rf => rf.PlanetFaction.Faction.Id)
                .ToList();
            // NOTE: id -9 (not -1) for Recon in the original OptionButton-based scheme. Godot's
            // OptionButton.AddItem treats id == -1 as "auto-assign to the item's index", so a
            // literal -1 would be silently replaced with the item index (0), breaking Recon
            // selection/confirmation. Preserved here as a comment for whoever maps these
            // descriptors back onto an OptionButton id scheme.
            missionOptions.Add(new AvailableMission("Recon", MissionAvailabilityKind.Recon));
            if (targetRegion == originRegion)
            {
                missionOptions.Add(new AvailableMission("Defend", MissionAvailabilityKind.Defend));
                missionOptions.Add(new AvailableMission("Patrol", MissionAvailabilityKind.Patrol));
                // Fortification: the squad spends the turn building defenses in its own region.
                missionOptions.Add(new AvailableMission(
                    GetConstructionLabel(DefenseType.Entrenchment),
                    MissionAvailabilityKind.FortifyEntrenchment));
                missionOptions.Add(new AvailableMission(
                    GetConstructionLabel(DefenseType.ListeningPost),
                    MissionAvailabilityKind.BuildListeningPost));
                missionOptions.Add(new AvailableMission(
                    GetConstructionLabel(DefenseType.AntiAir),
                    MissionAvailabilityKind.BuildAntiAir));
                AddAttackOptions(missionOptions, publicEnemies);
            }
            else if (publicEnemies.Count > 0)
            {
                AddAttackOptions(missionOptions, publicEnemies);
                missionOptions.Add(new AvailableMission("Diversion", MissionAvailabilityKind.Diversion));
            }
            else
            {
                missionOptions.Add(new AvailableMission("Move", MissionAvailabilityKind.Move));
            }
            IReadOnlyDictionary<int, string> specialMissionLabels =
                SpecialMissionPresentation.BuildLabels(targetRegion);
            foreach (var mission in targetRegion.SpecialMissions
                .Where(IsPlayerVisibleSpecialMission))
            {
                // Show of Force has no movement step - the squads hold position to be seen - so a
                // squad ordered onto it from elsewhere would never actually arrive, and only
                // presence IN this region counts toward the request. Offer it the way Patrol and
                // Defend are offered: to squads already here.
                if (mission.MissionType == MissionType.ShowOfForce
                    && targetRegion != originRegion)
                {
                    continue;
                }
                missionOptions.Add(new AvailableMission(
                    specialMissionLabels[mission.Id],
                    MissionAvailabilityKind.Special,
                    mission));
            }
            return missionOptions;
        }

        public static bool IsPlayerVisibleSpecialMission(Mission mission)
        {
            if (mission == null) return false;
            if (mission.MissionType != MissionType.Extermination) return true;

            RegionFaction target = mission.RegionFaction;
            if (target == null)
            {
                return FactionCapabilities.HasDormantPopulations(mission.TargetFaction)
                    && mission.Region != null
                    && IntelligenceTargetService.GetBestPlayerVisibleBelief(
                        mission.Region,
                        mission.TargetFaction)?.Level >= IntelLevel.Confirmed;
            }
            if (target.IsPublic) return false;
            return !FactionCapabilities.HasDormantPopulations(target.PlanetFaction?.Faction)
                || DormantPopulationCulling.CanTarget(
                    target,
                    IntelligenceTargetService.GetBestPlayerVisibleBelief(
                        target.Region,
                        target.PlanetFaction.Faction));
        }

        private static void AddAttackOptions(
            ICollection<AvailableMission> missionOptions,
            IEnumerable<RegionFaction> publicEnemies)
        {
            foreach (RegionFaction enemy in publicEnemies)
            {
                missionOptions.Add(new AvailableMission(
                    $"Attack ({enemy.PlanetFaction.Faction.Name})",
                    MissionAvailabilityKind.Attack,
                    targetFaction: enemy));
            }
        }
    }
}
