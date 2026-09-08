using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Operations.Contracts;

/// <summary>
/// Application-owned lifecycle for Operations mission elements. The operational model owns the
/// live campaign view; the factory is the only composition seam that creates its tactical state.
/// </summary>
public interface IEngagementElementFactory
{
    OperationalMissionElement CreateSquad(
        bool isPlayerSquad,
        Squad squad,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null);

    OperationalMissionElement CreateAttachedCharacter(
        PlayerSoldier character,
        int tacticalId,
        Faction fallbackFaction,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null);

    /// <summary>Applies the operational participant selection to retained tactical state.</summary>
    void Update(OperationalMissionElement element);
}
