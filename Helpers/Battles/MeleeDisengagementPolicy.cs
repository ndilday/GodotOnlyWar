using System.Collections.Generic;

using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles;

public enum MeleeDisengagementChoice
{
    StandAndFight,
    KeepRunning
}

/// <summary>
/// Decides whether a soldier in an organized withdrawal, caught in melee, turns and fights or
/// keeps running (Design/Active/WithdrawalAndPursuit.md §6.2). Withdrawal is not a rout: a bound
/// squad is under orders and still has the discipline to make this call, where a routing soldier
/// has none and simply fights when he cannot flee.
///
/// The call is the matchup plus morale, and nothing else — the force-level layer already owns
/// whether the withdrawal itself is viable. Pure and RNG-free, like every other decision helper
/// here; the probabilities are supplied by the caller from
/// <see cref="Actions.MeleeAttackAction.EstimateHitProbability"/>.
/// </summary>
public static class MeleeDisengagementPolicy
{
    /// <summary>
    /// How much of a favourable exchange a Shaken squad demands before it will stop running.
    /// Steady troops stand on an even trade; shaken ones need to be clearly winning. Expressed
    /// in the same units as the score — differences of hit probability. Phase 7 calibration.
    /// </summary>
    public const float SteadyStandThreshold = 0.0f;
    public const float ShakenStandThreshold = 0.35f;

    /// <summary>
    /// Penalty to standing per additional enemy in contact. Turning to fight two or three
    /// attackers means being pulled down while the rest of the squad leaves, so being swarmed
    /// pushes toward accepting the free swings and going.
    /// </summary>
    public const float OutnumberedStandPenalty = 0.25f;

    /// <param name="ChanceIHitHim">My chance to land a blow if I stop and fight.</param>
    /// <param name="ChanceHeHitsMeStanding">His chance to hit me while I defend properly.</param>
    /// <param name="ChanceHeHitsMeRunning">
    /// His chance to hit me once I turn my back — melee skill and parry replaced by foot speed.
    /// The gap between this and <paramref name="ChanceHeHitsMeStanding"/> is what fleeing costs.
    /// </param>
    /// <param name="AdjacentEnemies">Enemies currently in contact with me.</param>
    /// <param name="Morale">The squad's morale state this turn.</param>
    public sealed record Input(
        float ChanceIHitHim,
        float ChanceHeHitsMeStanding,
        float ChanceHeHitsMeRunning,
        int AdjacentEnemies,
        MoraleState Morale);

    public sealed record Result(
        MeleeDisengagementChoice Choice,
        float Score,
        float Threshold,
        string Reason,
        BattleDecisionTrace Trace);

    public static float StandThreshold(MoraleState morale) => morale switch
    {
        MoraleState.Shaken => ShakenStandThreshold,
        _ => SteadyStandThreshold
    };

    /// <summary>
    /// Score above the morale threshold means stand. Two terms, both in hit-probability units:
    /// whether the exchange itself favours me, and what running would cost me on top. A soldier
    /// who would lose the duel outright still stands when fleeing is even worse than losing it.
    /// </summary>
    public static Result Evaluate(Input input)
    {
        float exchangeAdvantage = input.ChanceIHitHim - input.ChanceHeHitsMeStanding;
        float costOfFleeing = input.ChanceHeHitsMeRunning - input.ChanceHeHitsMeStanding;
        float outnumbered = OutnumberedStandPenalty * System.Math.Max(0, input.AdjacentEnemies - 1);
        float score = exchangeAdvantage + costOfFleeing - outnumbered;
        float threshold = StandThreshold(input.Morale);

        bool stands = score >= threshold;
        string reason = stands
            ? exchangeAdvantage >= 0 ? "wins_the_exchange" : "fleeing_costs_more"
            : outnumbered > 0 ? "outnumbered_in_contact" : "loses_the_exchange";

        BattleDecisionTrace trace = new("MELEE_DISENGAGE", new List<KeyValuePair<string, string>>
        {
            BattleDecisionTrace.Field("chance_i_hit", input.ChanceIHitHim),
            BattleDecisionTrace.Field("chance_he_hits_standing", input.ChanceHeHitsMeStanding),
            BattleDecisionTrace.Field("chance_he_hits_running", input.ChanceHeHitsMeRunning),
            BattleDecisionTrace.Field("adjacent_enemies", input.AdjacentEnemies),
            BattleDecisionTrace.Field("morale", input.Morale),
            BattleDecisionTrace.Field("exchange_advantage", exchangeAdvantage),
            BattleDecisionTrace.Field("cost_of_fleeing", costOfFleeing),
            BattleDecisionTrace.Field("outnumbered_penalty", outnumbered),
            BattleDecisionTrace.Field("score", score),
            BattleDecisionTrace.Field("threshold", threshold),
            BattleDecisionTrace.Field(
                "decision",
                stands ? MeleeDisengagementChoice.StandAndFight : MeleeDisengagementChoice.KeepRunning),
            BattleDecisionTrace.Field("reason", reason)
        });
        BattleLog.Write(trace.Render());
        return new Result(
            stands ? MeleeDisengagementChoice.StandAndFight : MeleeDisengagementChoice.KeepRunning,
            score,
            threshold,
            reason,
            trace);
    }
}
