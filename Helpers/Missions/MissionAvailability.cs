using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
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

        public AvailableMission(string label, MissionAvailabilityKind kind, Mission specialMission = null)
        {
            Label = label;
            Kind = kind;
            SpecialMission = specialMission;
        }
    }

    // Pure-logic extraction of OrderDialogController.PopulateMissions' branching, so it can be
    // reused by other UI (e.g. a future multi-squad operations board) without depending on Godot
    // or the dialog. Reproduces the original branching EXACTLY - see the moved comments below.
    public static class MissionAvailability
    {
        public static IReadOnlyList<AvailableMission> GetAvailableMissions(Region originRegion, Region targetRegion)
        {
            List<AvailableMission> missionOptions = new List<AvailableMission>();
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
                missionOptions.Add(new AvailableMission("Build Fortifications", MissionAvailabilityKind.FortifyEntrenchment));
                missionOptions.Add(new AvailableMission("Build Listening Post", MissionAvailabilityKind.BuildListeningPost));
                missionOptions.Add(new AvailableMission("Build Anti-Air", MissionAvailabilityKind.BuildAntiAir));
            }
            else if (targetRegion.RegionFactionMap.Values.Any(rf => !rf.PlanetFaction.Faction.IsDefaultFaction && !rf.PlanetFaction.Faction.IsPlayerFaction))
            {
                missionOptions.Add(new AvailableMission("Attack", MissionAvailabilityKind.Attack));
                missionOptions.Add(new AvailableMission("Diversion", MissionAvailabilityKind.Diversion));
            }
            else
            {
                missionOptions.Add(new AvailableMission("Move", MissionAvailabilityKind.Move));
            }
            foreach (var mission in targetRegion.SpecialMissions)
            {
                missionOptions.Add(new AvailableMission(mission.MissionType.ToString(), MissionAvailabilityKind.Special, mission));
            }
            return missionOptions;
        }
    }
}
