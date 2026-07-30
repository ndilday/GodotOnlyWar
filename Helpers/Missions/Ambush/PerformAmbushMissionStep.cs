using OnlyWar.Models.Missions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Units;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Helpers.Extensions;

namespace OnlyWar.Helpers.Missions.Ambush
{
    public class PerformAmbushMissionStep : IMissionStep
    {
        public string Description { get { return "Ambush"; } }

        public bool ConsumesDay => true;

        public PerformAmbushMissionStep() { }

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            List<BattleSquad> missionSquads = context.MissionSquads
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .ToList();
            List<BattleSquad> opposingSquads = context.OpposingSquads
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .ToList();
            if (missionSquads.Count == 0 || opposingSquads.Count == 0)
            {
                context.NoViableTarget = true;
                context.AddLog($"Day {context.DaysElapsed}: No combat-capable forces remain for ambush.");
                return MissionStepResult.Complete;
            }

            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = execution.Rules.Stealth;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            float difficulty = enemyFaction.GetOwnRegionIntel() * 0.5f;
            // every degree of magnitude of troops adds one to the difficulty
            difficulty += (float)Math.Log(missionSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // the attacker's own knowledge of the region makes it easier to find a stealthy route
            Faction attacker = missionSquads.FirstOrDefault()?.Squad.Faction;
            if (attacker != null) difficulty -= enemyFaction.Region.GetFactionRegionIntel(attacker);
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(missionSquads, execution.Random);
            if (margin > 0.0f)
            {
                // See MeetingEngagementMissionStep: the loadout is redistributed at the start of every
                // battle, so a fallen carrier's weapon is picked up rather than lost for the mission.
                foreach (BattleSquad squad in missionSquads)
                {
                    squad.ReallocateEquipment();
                }

                // How well the ambush was positioned decides whose fight this is. A well-set ambush
                // is sprung at the range the AMBUSHERS want; a poorly-set one is sprung at the range
                // that suits whoever walked into it.
                //
                // This used to be `70 - marginOfSuccess * 20`, floored at 20 - two constants that
                // ignored what either force was carrying and always trended toward contact. That is
                // wrong in the case the mission exists for: marines ambushing Tyranids do not want
                // to be as close as possible, they want bolter range, and pulling every successful
                // ambush toward 20 yards handed the gribblies exactly the fight they wanted. The
                // margin should push the range toward the ambusher's PREFERENCE, which is what
                // MeetingEngagementMissionStep already did and what MissionOpeningRange now shares.
                //
                // marginOfSuccess is PositionAmbushMissionStep's check (how good the position is),
                // not this step's - the local check asks only whether the ambush stayed hidden until
                // the enemy walked in.
                ushort range = MissionOpeningRange.Interpolate(
                    missionSquads, opposingSquads, marginOfSuccess, execution.Random);
                // set up Ambush battle with OpFor attacker and context.Squad defender
                BattleGridManager bgm = new BattleGridManager();
                AmbushPlacer placer = new AmbushPlacer(bgm, range);
                var squadPostionMap = placer.PlaceSquads(opposingSquads, missionSquads);
                // burrowing squads erupt straight into melee — see Design/EvasionBurrowAndAmbush.md
                BurrowPlacer.PlaceBurrowers(bgm, missionSquads.Concat(opposingSquads));
                int oppForSize = opposingSquads.Sum(s => s.AbleSoldiers.Count);
                // See AmbushedMissionStep: Faction is guarded rather than assumed everywhere else.
                string opposingFaction =
                    opposingSquads.First().Squad?.Faction?.Name ?? "an unidentified force";
                string log = $"Day {context.DaysElapsed}: Force ambushed {oppForSize} {opposingFaction}\n";
                context.AddLog(log);
                long opposingBattleValueBefore = AbleBattleValue(opposingSquads);
                // run the battle
                BattleTurnResolver resolver = new BattleTurnResolver(
                    bgm,
                    missionSquads,
                    opposingSquads,
                    context.Order.Mission.RegionFaction.Region,
                    execution.Battle,
                    context.CreateMissionBattleProfile(BattleRole.Ambusher),
                    MissionContext.CreateOpposingBattleProfile(opposingSquads, BattleRole.Ambushed));
                bool battleDone = false;
                resolver.OnBattleComplete += (sender, e) => { battleDone = true; };
                while (!battleDone)
                {
                    resolver.ProcessNextTurn();
                }
                context.RecordBattleOutcome(resolver.BattleHistory);
                context.AddBattleReport(resolver.BattleHistory);
                context.RecordDefenderLosses(
                    opposingBattleValueBefore - AbleBattleValue(opposingSquads));
                if (!context.MissionSquads.Any(squad => squad.AbleSoldiers.Count > 0))
                {
                    context.ForceWithdrewUnderFire = true;
                    context.AddLog($"Day {context.DaysElapsed}: Force combat-ineffective; mission ended.");
                    return MissionStepResult.Complete;
                }
                if (context.ForceWithdrewUnderFire)
                {
                    context.AddLog($"Day {context.DaysElapsed}: Force withdrew from the ambush under fire.");
                    return MissionStepResult.Complete;
                }
                return MissionStepResult.Continue(new ExfiltrateMissionStep());
            }

            // A spoiled ambush turns into a meeting engagement, and the exfiltration is reached as
            // that engagement's resume target - so a force left spent by it does not attempt to
            // withdraw. That was the existing behaviour and is preserved deliberately.
            return MissionStepResult.Continue(
                new MeetingEngagementMissionStep(), margin, new ExfiltrateMissionStep());
        }

        private static long AbleBattleValue(IEnumerable<BattleSquad> squads) =>
            squads
                .SelectMany(squad => squad.AbleSoldiers)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);
    }
}
