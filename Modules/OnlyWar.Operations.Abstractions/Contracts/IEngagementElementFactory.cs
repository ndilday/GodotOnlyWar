using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;

namespace OnlyWar.Operations.Abstractions;

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
