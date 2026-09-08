using System.Collections.Generic;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Generation.Abstractions;

/// <summary>
/// Training and rating policy. The implementation is campaign-owned; Generation and the turn
/// processors consume it through this contract so neither depends on the other (plan §3.2).
/// </summary>
public interface ISoldierTrainingService
{
    void UpdateRatings(Date date, PlayerSoldier soldier);
    void EvaluateSoldier(PlayerSoldier soldier, Date trainingFinishedYear);
    void ApplySoldierWorkExperience(ISoldier soldier, Squad squad, float points);
    void TrainScouts(
        IEnumerable<Squad> scoutSquads,
        Dictionary<int, string> squadTrainingOptionMap,
        float points = 0.2f,
        IReadOnlyDictionary<int, float> pointsBySquad = null);
}
