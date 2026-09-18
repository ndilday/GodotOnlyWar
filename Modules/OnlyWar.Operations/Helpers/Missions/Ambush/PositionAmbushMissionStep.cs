using OnlyWar.Runtime.Factories;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Units;
using OnlyWar.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Operations.Missions.Ambush
{
    public class PositionAmbushMissionStep : IMissionStep
    {
        public string Description { get { return "Ambush Stealth"; } }

        public bool ConsumesDay => true;

        // What follows once the ambush has resolved, instead of withdrawing. Null keeps the standalone
        // ambush's behaviour - spring it and exfiltrate - which is what a lightning raid and an
        // intelligence-discovered player ambush both want.
        private readonly IMissionStep _continuation;

        // How the ambushed force is raised. Null keeps AmbushMissionSizing's rolled slice. An opening
        // ambush on an advance supplies the ASSAULT's own defence assembly instead, so that the troops
        // caught in the ambush and the troops met on the following days are one garrison rather than two
        // independently generated forces. Nothing extra is needed to reconcile them: this step records
        // its kills through MissionContext.RecordDefenderLosses, and AssembleDefendingForce already
        // deducts DefenderBattleValueDestroyed from the reserve it mobilises each morning.
        private readonly Func<MissionExecutionContext, float, List<OperationalMissionElement>> _opposingForce;

        public PositionAmbushMissionStep() { }

        public PositionAmbushMissionStep(
            IMissionStep continuation,
            Func<MissionExecutionContext, float, List<OperationalMissionElement>> opposingForce = null)
        {
            _continuation = continuation;
            _opposingForce = opposingForce;
        }

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = execution.Rules.Stealth;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            Faction attacker = context.MissionSquads.FirstOrDefault()?.Faction;
            int headcount = context.MissionSquads.Sum(s => s.AbleMembers.Count);
            // Setting an ambush without being seen first is contested by everyone watching the
            // ground, not just the faction being ambushed, so this uses the same aggregated
            // search-effort model as ReconStealthMissionStep - and with it that model's log10(1 + x)
            // shape, so a region held by a zero-Garrison horde can no longer produce Log(0) =
            // -infinity, which used to guarantee the ambushers got into position however badly they
            // rolled. Patrolled ground is now the thing that spoils an ambush setup, not raw mass.
            float difficulty = MissionStealthDifficulty
                .Calculate(enemyFaction.Region, headcount, attacker).Total
                // Aggression's EXPOSURE axis: a cautious ambush takes its time and is harder to spot.
                //
                // There is deliberately no separate effect-axis CHECK here, for the same reason
                // Assault has none (OnlyWar_TDD.md §6.4): an ambush's
                // objective IS the engagement, so aggression's existing casualty threshold is
                // already its effect axis - press the ambush and you destroy more of the enemy, break
                // off early and you destroy less. This margin additionally decides the range the
                // ambush is sprung at (see PerformAmbushMissionStep / MissionOpeningRange), so a
                // patient setup also buys the fight on the ambusher's own terms.
                + MissionAggressionModifiers.ExposureDifficulty(context.Order.LevelOfAggression);
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);

            // The force is raised AFTER the positioning check so a supplier can size and prepare it
            // against how well the ambush was actually set. The rolled slice ignores the margin, so the
            // standalone ambush is unaffected beyond the order of its RNG draws.
            context.OpposingSquads = _opposingForce != null
                ? _opposingForce(execution, margin)
                : PopulateOpposingForce(
                    context.Order.Mission,
                    enemyFaction,
                    execution.Random,
                    execution.EntityIds,
                    execution.EngagementElements);

            if (margin > 0.0f)
            {
                return MissionStepResult.Continue(new PerformAmbushMissionStep(_continuation), margin);
            }

            // The setup was seen. The force still fights - but as a meeting engagement against an
            // alerted enemy, not as an ambush - so the debrief is told why the plan changed before
            // MeetingEngagementMissionStep logs the fight itself.
            context.AmbushSpoiled = AmbushSpoilStage.DuringSetup;
            context.AddLog(
                $"Day {context.DaysElapsed}: Force was spotted moving into ambush positions; "
                + "the enemy was alerted and no ambush could be set.");
            // An opening ambush that was spotted still presses the attack it was opening: the meeting
            // engagement resumes into the assault rather than ending the week.
            return MissionStepResult.Continue(new MeetingEngagementMissionStep(), margin, _continuation);
        }

        private static List<OperationalMissionElement> PopulateOpposingForce(
            Mission mission,
            RegionFaction enemyFaction,
            IRNG random,
            IEntityIdAllocator entityIds,
            IEngagementElementFactory engagementElements)
        {
            long targetBattleValue =
                AmbushMissionSizing.ResolveTargetBattleValue(mission, random);

            // generate opposing force
            var request = new ForceGenerationRequest
            {
                Faction = enemyFaction.PlanetFaction.Faction,
                // Intelligence-discovered ambushes persist this concrete budget when the opportunity
                // is created. Legacy and non-special ambush orders retain the old execution-time roll.
                TargetBattleValue = targetBattleValue,
                Profile = ForceCompositionProfile.AmbushForce
            };
            return ForceGenerator.GenerateForce(request, random, entityIds)
                .Select(s => engagementElements.CreateSquad(false, s))
                .ToList();
        }
    }
}
