using OnlyWar.Models.Missions;

namespace OnlyWar.Helpers.Missions
{
    /// <summary>
    /// Describes whether the squads assigned to an order act as one tactical force or as
    /// independent mission elements. UnifiedForce is deliberately the default: missions only
    /// fan out when their design explicitly opts into it.
    /// </summary>
    public enum MissionForceMode
    {
        UnifiedForce = 0,
        IndependentSquads
    }

    public static class MissionForcePolicy
    {
        public static MissionForceMode GetMode(MissionType missionType) =>
            missionType == MissionType.Recon
                ? MissionForceMode.IndependentSquads
                : MissionForceMode.UnifiedForce;
    }
}
