using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Missions
{
    // Non-battle missions (recon, infiltration, sabotage, assassination, diversions, raids, ...)
    // left no trace in a soldier's career history: only combat resolved through
    // PlayerChapterBattleAftermathPolicy wrote a SoldierEvent. This closes that gap (PRD 4.13
    // "Mission Record & Experience") by writing one MissionOutcome event per participating
    // PlayerSoldier at the end of every player-run mission, mirroring the style of
    // PlayerChapterBattleAftermathPolicy.ProcessSoldierHistoryForBattle.
    //
    // Called once, immediately after MissionStepOrchestrator finishes running a player order's
    // mission (TurnController.ProcessCombatMissions / ProcessDiversionMissions). NPC missions never
    // call this - NPC squads have no PlayerSoldier members and no career log to write to.
    public static class MissionOutcomeRecorder
    {
        public static void RecordMissionOutcome(MissionContext context, Date date)
        {
            RegionFaction targetFaction = context.Order?.Mission?.RegionFaction;
            if (targetFaction == null) return;

            MissionOutcomeClassification classification = MissionOutcomeClassifier.Classify(context);
            string detail = BuildOutcomeDetail(classification, targetFaction);
            int? factionId = targetFaction.PlanetFaction?.Faction?.Id;
            string locationName = $"{targetFaction.Region.Name}, {targetFaction.Region.Planet.Name}";
            int? magnitude = classification.EnemiesKilled > 0 ? classification.EnemiesKilled : null;

            foreach (BattleSquad squad in context.MissionSquads)
            {
                // A soldier killed mid-mission (e.g. in the meeting engagement embedded in a
                // Lightning Raid) is already removed from squad.Soldiers by BattleTurnResolver, so
                // this naturally skips them rather than writing to (or throwing on) a dead soldier.
                foreach (BattleSoldier battleSoldier in squad.Soldiers)
                {
                    if (battleSoldier.Soldier is PlayerSoldier playerSoldier)
                    {
                        playerSoldier.AddEvent(new SoldierEvent(
                            date,
                            SoldierEventType.MissionOutcome,
                            detail,
                            factionId: factionId,
                            magnitude: magnitude,
                            locationName: locationName));
                    }
                }
            }
        }

        private static string BuildOutcomeDetail(MissionOutcomeClassification classification, RegionFaction targetFaction)
        {
            string regionName = targetFaction.Region.Name;
            string enemyName = targetFaction.PlanetFaction?.Faction?.Name ?? "the enemy";
            bool detected = classification.WasDetected;
            // The career log treats a force lost behind enemy lines or one that never reached its
            // objective as "aborted" for these one-line summaries (the old string match keyed on
            // "aborted"/"gone to ground"/"assumed dead", which map to exactly these two dispositions).
            bool aborted = classification.Disposition is MissionForceDisposition.LostContact
                                                       or MissionForceDisposition.AbortedBeforeObjective;
            int killed = classification.EnemiesKilled;

            switch (classification.MissionType)
            {
                case MissionType.Recon:
                    return detected
                        ? $"Reconnaissance of {regionName} compromised; detected by the {enemyName} and forced to break contact."
                        : $"Reconnaissance conducted in {regionName}; region infiltrated undetected.";

                case MissionType.Infiltrate:
                    return detected
                        ? $"Infiltration of {regionName} compromised; detected by the {enemyName}."
                        : $"Successfully infiltrated {regionName} undetected.";

                case MissionType.Sabotage:
                    if (aborted) return $"Sabotage mission into {regionName} aborted before objectives were met.";
                    return classification.Impact > 0
                        ? $"Sabotage carried out against {enemyName} assets in {regionName}."
                        : $"Attempted sabotage in {regionName}, but failed to achieve significant effect.";

                case MissionType.Assassination:
                    if (aborted) return $"Assassination attempt in {regionName} aborted before the target could be reached.";
                    return classification.TargetEliminated
                        ? $"Assassination mission in {regionName} successful; target eliminated."
                        : $"Assassination attempt in {regionName} failed to eliminate the target.";

                case MissionType.Diversion:
                    return $"Conducted a demonstration of force against the {enemyName} in {regionName}.";

                case MissionType.LightningRaid:
                case MissionType.HitAndRun:
                case MissionType.DeepStrike:
                case MissionType.ObjectiveRaid:
                    if (aborted) return $"Raid into {regionName} aborted before contact.";
                    return killed > 0
                        ? $"Raid conducted in {regionName}; struck the {enemyName} and withdrew, {killed} enemy casualties."
                        : $"Raid conducted in {regionName}; withdrew without engaging a worthwhile target.";

                case MissionType.Ambush:
                    return killed > 0
                        ? $"Ambush laid against the {enemyName} in {regionName}; {killed} enemy casualties."
                        : $"Attempted an ambush in {regionName}, but no target presented itself.";

                case MissionType.Extermination:
                    return killed > 0
                        ? $"Extermination operation conducted in {regionName}; {killed} {enemyName} eliminated."
                        : $"Extermination operation conducted in {regionName}.";

                default:
                    return aborted
                        ? $"{classification.MissionType} operation in {regionName} aborted."
                        : $"Conducted a {classification.MissionType} operation in {regionName}.";
            }
        }
    }
}
