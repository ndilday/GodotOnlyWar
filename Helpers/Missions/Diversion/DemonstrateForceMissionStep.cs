using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Diversion
{
    /// <summary>
    /// A day of overt demonstration against an adjacent region: the force is trying to BE seen, in
    /// order to pull the attention of whoever is watching that ground away from it.
    /// </summary>
    /// <remarks>
    /// Two checks per day (Design/Active/DailyMissionResolution.md §4).
    ///
    /// <para><b>Roll A - draw.</b> How much of the target's search effort the demonstration commits to
    /// watching it, written into <see cref="RegionFaction.CommittedAttention"/> and read by every
    /// stealth check in that region during the same day's Acting pass. This is the diversion's actual
    /// product.</para>
    ///
    /// <para><b>Roll B - response.</b> Whether the enemy answers with force rather than merely with
    /// attention. Its difficulty rises with Roll A's draw: the more eyes you pull onto yourself, the
    /// likelier somebody arrives with guns. Failure produces a battle THAT DAY, in the region the feint
    /// force is standing in.</para>
    ///
    /// Roll B replaces the old <c>RegionFaction.ProvocationLevel</c>, which baited a counterattack by
    /// nudging the enemy AI's offensive thresholds during planning. That could only ever land the
    /// following turn and required the AI to make a decision; this delivers a mid-week response as a
    /// consequence of the player's own dice, with no AI re-planning at all - which is what lets the
    /// "enemy plans weekly, resolves daily" boundary hold.
    ///
    /// The old per-day Impact accumulation and its conversion into a perceived-threat bonus are gone
    /// entirely: garrison inflation was dropped deliberately, since a feint begun on Monday cannot
    /// retroactively change planning the enemy did on Sunday. Impact is still accrued so the mission
    /// report has something to describe.
    /// </remarks>
    public class DemonstrateForceMissionStep : IMissionStep
    {
        // Share of the target's remaining patrol effort a demonstration commits, per sigma of Roll A
        // margin. At 0.165 a strong day (about +2 sigma) pulls roughly a third of the screen, which is
        // enough to matter to an infiltrator without making a feint a prerequisite for every
        // infiltration.
        private const float DrawPerMarginSigma = 0.165f;

        // Ceiling on a single day's draw. Above the patrol term the draw spills into the ambient
        // (dug-in) term, so a strong feint can begin to peel defenders off a position. Reaching that
        // spill takes real force committed to the demonstration, because the draw is gated on a margin
        // that reads the feint force's battle value against the defender's - see RunDrawCheck.
        private const float MaxDrawFraction = 0.60f;

        // Roll B's baseline, mirroring the difficulty of the other leader-level mission checks.
        private const float ResponseBaseDifficulty = 10.0f;

        // How much harder avoiding a real engagement gets per unit of attention drawn. This is the
        // coupling that makes two rolls better than one: the feint's success is what puts it in danger,
        // so the risk curve is a consequence of the draw rather than an independent coin flip.
        private const float ResponseDifficultyPerDraw = 2.0f;

        public string Description => "Demonstrate Force";

        // The one step that SHAPES rather than acts: the whole purpose of a feint is to move the
        // attention of the region's occupants, so it must resolve before the missions that have to
        // cross that ground the same day. Declared here rather than special-cased in the scheduler -
        // see MissionStepPhase.
        public MissionStepPhase Phase => MissionStepPhase.Shaping;

        public bool ConsumesDay => true;

        public MissionStepResult ExecuteMissionStep(
            MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            BaseSkill tactics = execution.Rules.Tactics;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;

            context.DaysElapsed++;
            context.AddLog(
                $"Day {context.DaysElapsed}: Force makes a show of strength against {enemyFaction.Region.Name}");

            // Read once and shared by both rolls: how much force is actually putting on the show. Using
            // ABLE soldiers means a feint force whittled down by counterattacks earlier in the week
            // becomes less convincing as it bleeds, which is the right direction.
            long feintBattleValue = FeintBattleValue(context);

            float drawn = RunDrawCheck(execution, context, enemyFaction, tactics, feintBattleValue);
            MissionStepResult response = RunResponseCheck(
                execution, context, enemyFaction, tactics, drawn, feintBattleValue);
            if (response.Next != null)
            {
                return response;
            }

            // The feint force stays in the open for the whole turn; it does not infiltrate or
            // exfiltrate, so just keep demonstrating until the campaign week is spent.
            return context.DaysElapsed < MissionContext.MissionDurationDays
                ? MissionStepResult.Continue(this, marginOfSuccess, resumeStep)
                : MissionStepResult.Complete;
        }

        // Roll A. Returns the attention committed, in MissionStealthDifficulty's units.
        private static float RunDrawCheck(
            MissionExecutionContext execution,
            MissionContext context,
            RegionFaction enemyFaction,
            BaseSkill tactics,
            long feintBattleValue)
        {
            // The harder the enemy is to bluff (better detection, more troops fielded to appraise the
            // threat), the harder it is to project a convincing feint.
            //
            // Deliberately NOT on MissionStealthDifficulty's search-effort model. A feint is overt -
            // the force is trying to BE seen - so "who is out hunting for intruders" is the wrong
            // question entirely; what matters is how much force the enemy has on hand to appraise the
            // threat with, and an idle staff appraises just as well as a patrolling one. Capping that
            // the way ambient search is capped would make a hive fleet as gullible as a village. It
            // borrows only Magnitude's log10(1+x) shape so an emptied region reads as 0 rather than
            // -infinity.
            float difficulty = enemyFaction.GetOwnRegionIntel() * 0.5f;
            difficulty += MissionStealthDifficulty.Magnitude(enemyFaction.GetDeployedStrength());
            // Credibility is RELATIVE. A squad demonstrating at a hive fleet is noise; a company
            // demonstrating at a village is overwhelming. Without this term every feint was equally
            // convincing regardless of what was putting on the show, so committing more squads to a
            // diversion bought nothing and one scout squad could bend a hive's whole screen - and the
            // "prising defenders loose takes commitment" property the draw priority exists to create
            // had no way to be paid for. Same shape LightningRaidMissionStep uses to price a raid
            // against its target.
            difficulty -= MissionStealthDifficulty.Magnitude(feintBattleValue);
            // Boldness is what draws attention, so this is aggression's EFFECT axis.
            difficulty += MissionAggressionModifiers.EffectDifficulty(context.Order.LevelOfAggression);

            float margin = new LeaderMissionTest(tactics, difficulty)
                .RunMissionCheck(context.MissionSquads, execution.Random);
            if (margin > 0)
            {
                context.Impact += margin;
            }
            if (margin <= 0f)
            {
                GameLog.Trace(() =>
                    $"Diversion draw {DescribeTarget(enemyFaction)} day {context.DaysElapsed}: "
                    + $"difficulty={difficulty:F2}, margin={margin:F2} -> unconvincing, nothing drawn");
                return 0f;
            }

            // The draw is a share of the effort actually out there to be pulled, so a feint against an
            // unwatched region accomplishes nothing and one against a heavily patrolled region has a
            // great deal to move.
            float patrolTerm = MissionStealthDifficulty.CalculateWatchTerms(enemyFaction).Patrol;
            float fraction = Math.Min(MaxDrawFraction, margin * DrawPerMarginSigma);
            float drawn = patrolTerm * fraction;
            enemyFaction.CommittedAttention += drawn;
            GameLog.Debug(() =>
                $"Diversion draw {DescribeTarget(enemyFaction)} day {context.DaysElapsed}: "
                + $"difficulty={difficulty:F2} (feintBV={feintBattleValue}), margin={margin:F2}, "
                + $"patrolTerm={patrolTerm:F2}, fraction={fraction:F2} -> drawn={drawn:F2} "
                + $"(committed now {enemyFaction.CommittedAttention:F2})");
            return drawn;
        }

        // Roll B. Returns a step result carrying the engagement when the enemy answers with force, or
        // Complete when the demonstration stayed a demonstration.
        private static MissionStepResult RunResponseCheck(
            MissionExecutionContext execution,
            MissionContext context,
            RegionFaction enemyFaction,
            BaseSkill tactics,
            float drawn,
            long feintBattleValue)
        {
            float difficulty = ResponseBaseDifficulty
                + drawn * ResponseDifficultyPerDraw
                // Avoiding a real fight is aggression's EXPOSURE axis: a cautious demonstration keeps
                // its distance and breaks off, a bold one stands and invites the answer.
                + MissionAggressionModifiers.ExposureDifficulty(context.Order.LevelOfAggression);

            float margin = new LeaderMissionTest(tactics, difficulty)
                .RunMissionCheck(context.MissionSquads, execution.Random);
            if (margin > 0f)
            {
                GameLog.Trace(() =>
                    $"Diversion response {DescribeTarget(enemyFaction)} day {context.DaysElapsed}: "
                    + $"difficulty={difficulty:F2} (drawn={drawn:F2}), margin={margin:F2} "
                    + "-> remained a feint");
                return MissionStepResult.Complete;
            }

            List<BattleSquad> responders =
                GenerateResponse(execution, enemyFaction, drawn, feintBattleValue);
            if (responders.Count == 0)
            {
                GameLog.Trace(() =>
                    $"Diversion response {DescribeTarget(enemyFaction)} day {context.DaysElapsed}: "
                    + $"provoked (margin={margin:F2}) but no force materialized; feint continues");
                return MissionStepResult.Complete;
            }

            context.OpposingSquads = responders;
            context.AddLog(
                $"Day {context.DaysElapsed}: The demonstration draws a counterattack from "
                + $"{enemyFaction.PlanetFaction.Faction.Name}.");
            GameLog.Debug(() =>
                $"Diversion response {DescribeTarget(enemyFaction)} day {context.DaysElapsed}: "
                + $"difficulty={difficulty:F2} (drawn={drawn:F2}), margin={margin:F2} -> COUNTERATTACK "
                + $"({responders.Count} squads, {responders.Sum(s => s.AbleSoldiers.Count)} soldiers)");

            // The engagement resolves on this same day (MeetingEngagementMissionStep does not consume
            // one), and a force that survives it resumes demonstrating tomorrow.
            return MissionStepResult.Continue(
                new MeetingEngagementMissionStep(), margin, new DemonstrateForceMissionStep());
        }

        // The responding force is sized relative to the FEINT force rather than to the defender's whole
        // strength, scaled up by how much attention was drawn. Scaling off the defender's deployed
        // strength instead would send a fraction of a hive fleet - millions of BV, clamped to the
        // tactical ceiling - at a single demonstrating squad every time it succeeded, which would make
        // diversions against exactly the factions they are most useful against unsurvivable.
        private static List<BattleSquad> GenerateResponse(
            MissionExecutionContext execution,
            RegionFaction enemyFaction,
            float drawn,
            long feintBattleValue)
        {
            long available = enemyFaction.GetDeployedStrength();
            if (available <= 0) return new List<BattleSquad>();

            long target = (long)Math.Round(feintBattleValue * (1.0 + drawn));
            target = Math.Max(target, enemyFaction.PlanetFaction.Faction.MinimumForceRequest);
            target = Math.Min(target, available);
            target = Math.Min(target, StrategicCombatRules.MassCombatBattleValueFloor - 1);
            if (target <= 0) return new List<BattleSquad>();

            var request = new ForceGenerationRequest
            {
                Faction = enemyFaction.PlanetFaction.Faction,
                TargetBattleValue = target,
                Profile = ForceCompositionProfile.Garrison
            };
            return ForceGenerator.GenerateForce(request, execution.Random, execution.EntityIds)
                .Select(squad => new BattleSquad(false, squad))
                .ToList();
        }

        private static long FeintBattleValue(MissionContext context) =>
            Math.Max(1L, context.MissionSquads
                .SelectMany(squad => squad.AbleSoldiers)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue));

        private static string DescribeTarget(RegionFaction target) =>
            $"{target.Region.Planet.Name}/{target.Region.Name}/{target.PlanetFaction.Faction.Name}";
    }
}
