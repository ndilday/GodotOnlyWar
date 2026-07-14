using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    public sealed class BattleReplaySummaryBuilder
    {
        public BattleReplayDisplay Build(BattleHistory history, int requestedTurnIndex, int? selectedFormationId = null)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (history.Turns.Count == 0) throw new ArgumentException("Battle history must contain at least one turn.", nameof(history));

            int currentTurnIndex = Math.Clamp(requestedTurnIndex, 0, history.Turns.Count - 1);
            BattleStateSnapshot baselineState = BuildBaselineState(history);
            BattleTurn currentTurn = history.Turns[currentTurnIndex];
            int? resolvedSelection = ResolveSelectedFormationId(currentTurn.State, selectedFormationId);

            IReadOnlyList<BattleForceHierarchyNode> hierarchy = BuildForceHierarchy(baselineState, currentTurn.State, resolvedSelection);
            BattleFormationSummary selectedFormation = resolvedSelection.HasValue
                ? BuildFormationSummary(baselineState, currentTurn.State, resolvedSelection.Value)
                : null;

            return new BattleReplayDisplay(
                currentTurnIndex,
                currentTurn.TurnNumber,
                history.Turns[^1].TurnNumber,
                "Battle Chronicle",
                BuildPhaseLabel(currentTurn),
                BuildResultLabel(baselineState, currentTurn.State, currentTurnIndex == history.Turns.Count - 1 && currentTurnIndex > 0),
                resolvedSelection,
                hierarchy,
                selectedFormation,
                BuildEventEntries(currentTurn),
                BuildTimeline(history, currentTurnIndex),
                BuildCasualtySummaries(history));
        }

        private static BattleStateSnapshot BuildBaselineState(BattleHistory history)
        {
            // This reconstruction only needs two buckets to rebuild a snapshot; the replay
            // reads squads back via GetAllSquads and the player-aligned flag, never by tactical
            // side. Bucketing by IsPlayerAligned here (rather than by original attacker/defender
            // slot) is therefore purely cosmetic and does not have to match the live battle.
            Dictionary<int, BattleSquadSnapshot> playerSquads = [];
            Dictionary<int, BattleSquadSnapshot> opposingSquads = [];
            foreach (BattleSquadSnapshot squad in history.Turns
                .SelectMany(turn => GetAllSquads(turn.State))
                .GroupBy(squad => squad.Id)
                .Select(group => group.OrderByDescending(CountAble).First()))
            {
                (squad.IsPlayerAligned ? playerSquads : opposingSquads)[squad.Id] = squad;
            }
            return BattleStateSnapshot.FromSquads(0, playerSquads.Values, opposingSquads.Values);
        }

        private static int? ResolveSelectedFormationId(BattleStateSnapshot state, int? selectedFormationId)
        {
            if (selectedFormationId.HasValue && ContainsSquad(state, selectedFormationId.Value))
            {
                return selectedFormationId.Value;
            }

            BattleSquadSnapshot firstPlayer = GetAllSquads(state)
                .Where(s => s.IsPlayerAligned)
                .OrderBy(s => s.Id)
                .FirstOrDefault();
            if (firstPlayer != null) return firstPlayer.Id;

            return GetAllSquads(state).OrderBy(s => s.Id).FirstOrDefault()?.Id;
        }

        private static IReadOnlyList<BattleForceHierarchyNode> BuildForceHierarchy(
            BattleStateSnapshot initialState,
            BattleStateSnapshot currentState,
            int? selectedFormationId)
        {
            List<BattleSquadSnapshot> initialSquads = GetAllSquads(initialState).ToList();
            List<BattleSquadSnapshot> currentSquads = GetAllSquads(currentState).ToList();
            List<BattleForceHierarchyNode> roots =
            [
                BuildForceRoot("Player Force", "Imperial formations", "controlled", true, initialSquads.Where(s => s.IsPlayerAligned), currentSquads.Where(s => s.IsPlayerAligned), selectedFormationId),
                BuildForceRoot("Opposing Force", "Hostile formations", "hostile", false, initialSquads.Where(s => !s.IsPlayerAligned), currentSquads.Where(s => !s.IsPlayerAligned), selectedFormationId)
            ];

            return roots.Where(root => root.StartingStrength > 0 || root.CurrentStrength > 0).ToList();
        }

        private static BattleForceHierarchyNode BuildForceRoot(
            string title,
            string subtitle,
            string iconKey,
            bool isPlayerForce,
            IEnumerable<BattleSquadSnapshot> initialSquads,
            IEnumerable<BattleSquadSnapshot> currentSquads,
            int? selectedFormationId)
        {
            List<BattleForceHierarchyNode> children = [];
            Dictionary<string, List<BattleSquadSnapshot>> currentByUnit = currentSquads
                .OrderBy(squad => GetUnitName(squad))
                .ThenBy(squad => squad.Name)
                .GroupBy(GetUnitName)
                .ToDictionary(group => group.Key, group => group.ToList());
            Dictionary<string, List<BattleSquadSnapshot>> initialByUnit = initialSquads
                .GroupBy(GetUnitName)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (string unitName in initialByUnit.Keys.Union(currentByUnit.Keys).OrderBy(name => name))
            {
                List<BattleSquadSnapshot> initialUnitSquads = initialByUnit.TryGetValue(unitName, out List<BattleSquadSnapshot> initial) ? initial : [];
                List<BattleSquadSnapshot> currentUnitSquads = currentByUnit.TryGetValue(unitName, out List<BattleSquadSnapshot> current) ? current : [];
                List<BattleForceHierarchyNode> squadNodes = initialUnitSquads
                    .Select(initialSquad =>
                    {
                        BattleSquadSnapshot currentSquad = currentUnitSquads.FirstOrDefault(squad => squad.Id == initialSquad.Id);
                        return BuildSquadNode(initialSquad, currentSquad, selectedFormationId);
                    })
                    .Concat(currentUnitSquads
                        .Where(currentSquad => initialUnitSquads.All(initialSquad => initialSquad.Id != currentSquad.Id))
                        .Select(currentSquad => BuildSquadNode(null, currentSquad, selectedFormationId)))
                    .Where(node => node.StartingStrength > 0 || node.CurrentStrength > 0)
                    .OrderBy(node => node.Title)
                    .ToList();

                if (squadNodes.Count == 0)
                {
                    continue;
                }

                children.Add(new BattleForceHierarchyNode(
                    null,
                    unitName,
                    $"{squadNodes.Count} formations",
                    isPlayerForce ? "chapter" : "threat",
                    isPlayerForce,
                    squadNodes.Any(node => node.IsSelected),
                    squadNodes.Sum(node => node.StartingStrength),
                    squadNodes.Sum(node => node.CurrentStrength),
                    squadNodes));
            }

            return new BattleForceHierarchyNode(
                null,
                title,
                subtitle,
                iconKey,
                isPlayerForce,
                children.Any(child => child.IsSelected),
                children.Sum(child => child.StartingStrength),
                children.Sum(child => child.CurrentStrength),
                children);
        }

        private static BattleForceHierarchyNode BuildSquadNode(BattleSquadSnapshot initialSquad, BattleSquadSnapshot currentSquad, int? selectedFormationId)
        {
            BattleSquadSnapshot source = currentSquad ?? initialSquad;
            int startingStrength = CountAble(initialSquad);
            int currentStrength = CountAble(currentSquad);
            return new BattleForceHierarchyNode(
                source.Id,
                source.Name,
                BuildSquadSubtitle(source, startingStrength, currentStrength),
                GetSquadIconKey(source),
                source.IsPlayerAligned,
                selectedFormationId == source.Id,
                startingStrength,
                currentStrength,
                []);
        }

        private static BattleFormationSummary BuildFormationSummary(BattleStateSnapshot initialState, BattleStateSnapshot currentState, int formationId)
        {
            BattleSquadSnapshot initialSquad = TryGetSquad(initialState, formationId);
            BattleSquadSnapshot currentSquad = TryGetSquad(currentState, formationId);
            BattleSquadSnapshot source = currentSquad ?? initialSquad;
            int startingStrength = CountAble(initialSquad);
            int currentStrength = CountAble(currentSquad);
            int losses = startingStrength - currentStrength;
            float lossPercent = startingStrength == 0 ? 0 : (float)losses / startingStrength;

            List<string> effects = [];
            if (currentSquad?.IsInMelee == true) effects.Add("Engaged in melee");
            if (lossPercent >= 0.5f) effects.Add("Severe losses");
            if (effects.Count == 0) effects.Add("No notable effects tracked");

            return new BattleFormationSummary(
                source.Id,
                source.Name,
                GetUnitName(source),
                source.Soldiers.FirstOrDefault(soldier => soldier.Soldier.Template.IsSquadLeader)?.Soldier?.Name ?? "No leader",
                GetFormationType(source),
                source.IsPlayerAligned,
                startingStrength,
                currentStrength,
                BuildFatigueLabel(source),
                BuildMoraleLabel(lossPercent, currentStrength),
                BuildAmmunitionLabel(source),
                BuildActiveWeaponSets(source, startingStrength),
                effects);
        }

        private static IReadOnlyList<BattleWeaponSetSummary> BuildActiveWeaponSets(BattleSquadSnapshot squad, int startingStrength)
        {
            WeaponSet defaultWeaponSet = squad?.Squad?.SquadTemplate?.DefaultWeapons;
            if (defaultWeaponSet == null && squad?.Squad?.Loadout == null)
            {
                return [];
            }

            Dictionary<string, int> customSetCounts = new(StringComparer.OrdinalIgnoreCase);
            foreach (WeaponSet weaponSet in squad.Squad.Loadout ?? [])
            {
                if (weaponSet == null || (defaultWeaponSet != null && weaponSet.Id == defaultWeaponSet.Id))
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(weaponSet.Name) ? "Unnamed weapon set" : weaponSet.Name;
                customSetCounts[name] = customSetCounts.TryGetValue(name, out int count) ? count + 1 : 1;
            }

            List<BattleWeaponSetSummary> summaries = [];
            int customSetCount = customSetCounts.Values.Sum();
            int defaultSetCount = Math.Max(0, startingStrength - customSetCount);
            if (defaultWeaponSet != null && defaultSetCount > 0)
            {
                summaries.Add(new BattleWeaponSetSummary(defaultWeaponSet.Name, defaultSetCount));
            }

            summaries.AddRange(customSetCounts
                .OrderBy(entry => entry.Key)
                .Select(entry => new BattleWeaponSetSummary(entry.Key, entry.Value)));
            return summaries;
        }

        private static IReadOnlyList<BattleEventEntry> BuildEventEntries(BattleTurn turn)
        {
            List<BattleEventEntry> entries = [];
            int sequence = 0;
            foreach (IAction action in turn.Actions)
            {
                sequence++;
                BattleSoldierSnapshot actor = turn.State.Soldiers.TryGetValue(action.ActorId, out BattleSoldierSnapshot soldier) ? soldier : null;
                int woundCount = CountWounds(action);
                string text = SafeDescription(action).Trim();
                entries.Add(new BattleEventEntry(
                    turn.TurnNumber,
                    $"{turn.TurnNumber:00}:{sequence:00}",
                    actor?.Soldier?.Name ?? $"Actor {action.ActorId}",
                    actor?.SquadName ?? "Unknown formation",
                    BuildEventType(action),
                    string.IsNullOrWhiteSpace(text) ? action.GetType().Name : text,
                    BuildSeverity(text, woundCount)));
            }

            if (entries.Count == 0)
            {
                entries.Add(new BattleEventEntry(
                    turn.TurnNumber,
                    $"{turn.TurnNumber:00}:00",
                    "Battlefield",
                    "All formations",
                    "Status",
                    "No recorded actions for this round.",
                    BattleEventSeverity.Normal));
            }

            return entries;
        }

        private static IReadOnlyList<BattleTimelineEntry> BuildTimeline(BattleHistory history, int currentTurnIndex)
        {
            List<BattleTimelineEntry> entries = [];
            for (int i = 0; i < history.Turns.Count; i++)
            {
                BattleTurn turn = history.Turns[i];
                int woundCount = turn.Actions.Sum(CountWounds);
                string summary = turn.Actions.Count == 0
                    ? "Initial deployment"
                    : $"{turn.Actions.Count} actions, {woundCount} wounds";
                entries.Add(new BattleTimelineEntry(
                    i,
                    turn.TurnNumber,
                    $"R{turn.TurnNumber}",
                    summary,
                    i == currentTurnIndex,
                    woundCount > 0 ? BattleEventSeverity.Warning : BattleEventSeverity.Normal));
            }

            return entries;
        }

        private static IReadOnlyList<BattleCasualtyRoundSummary> BuildCasualtySummaries(BattleHistory history)
        {
            List<BattleCasualtyRoundSummary> summaries = [];
            int startingPlayer = CountAble(GetPlayerSquads(history.Turns[0].State));
            int startingOpposing = CountAble(GetOpposingSquads(history.Turns[0].State));
            int previousPlayerLosses = 0;
            int previousOpposingLosses = 0;

            foreach (BattleTurn turn in history.Turns)
            {
                int currentPlayerLosses = startingPlayer - CountAble(GetPlayerSquads(turn.State));
                int currentOpposingLosses = startingOpposing - CountAble(GetOpposingSquads(turn.State));
                summaries.Add(new BattleCasualtyRoundSummary(
                    turn.TurnNumber,
                    Math.Max(0, currentPlayerLosses - previousPlayerLosses),
                    Math.Max(0, currentOpposingLosses - previousOpposingLosses),
                    currentPlayerLosses,
                    currentOpposingLosses));

                previousPlayerLosses = currentPlayerLosses;
                previousOpposingLosses = currentOpposingLosses;
            }

            return summaries;
        }

        private static string BuildPhaseLabel(BattleTurn turn)
        {
            if (turn.Actions.Any(action => action is MeleeAttackAction)) return "Melee phase";
            if (turn.Actions.Any(action => action is ShootAction or AreaAttackAction)) return "Fire phase";
            if (turn.Actions.Any(action => action is MoveAction)) return "Maneuver phase";
            return turn.TurnNumber == 0 ? "Deployment" : "Battle phase";
        }

        private static string BuildResultLabel(BattleStateSnapshot initialState, BattleStateSnapshot currentState, bool isFinalRound)
        {
            if (!isFinalRound) return "Battle in progress";
            int playerCurrent = CountAble(GetPlayerSquads(currentState));
            int opposingCurrent = CountAble(GetOpposingSquads(currentState));
            if (playerCurrent == 0 && opposingCurrent == 0) return "Mutual destruction";
            if (opposingCurrent == 0) return "Player force victorious";
            if (playerCurrent == 0) return "Opposing force victorious";

            int playerStarting = CountAble(GetPlayerSquads(initialState));
            int opposingStarting = CountAble(GetOpposingSquads(initialState));
            int playerLosses = playerStarting - playerCurrent;
            int opposingLosses = opposingStarting - opposingCurrent;
            if (opposingLosses > playerLosses) return "Advantage: player force";
            if (playerLosses > opposingLosses) return "Advantage: opposing force";
            return "Battle unresolved";
        }

        private static string BuildEventType(IAction action)
        {
            return action switch
            {
                ShootAction => "Volley",
                AreaAttackAction => "Volley",
                MeleeAttackAction => "Melee",
                MoveAction => "Movement",
                AimAction => "Targeting",
                ReloadRangedWeaponAction => "Reload",
                ReadyRangedWeaponAction => "Ready",
                ReadyMeleeWeaponAction => "Ready",
                _ => "Action"
            };
        }

        private static BattleEventSeverity BuildSeverity(string text, int woundCount)
        {
            if (!string.IsNullOrWhiteSpace(text) && text.IndexOf("died", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return BattleEventSeverity.Critical;
            }

            return woundCount > 0 ? BattleEventSeverity.Warning : BattleEventSeverity.Normal;
        }

        private static int CountWounds(IAction action)
        {
            return action switch
            {
                ShootAction shootAction => shootAction.WoundResolutions.Count,
                AreaAttackAction areaAttackAction => areaAttackAction.WoundResolutions.Count,
                MeleeAttackAction meleeAttackAction => meleeAttackAction.WoundResolutions.Count,
                _ => 0
            };
        }

        private static string SafeDescription(IAction action)
        {
            try
            {
                return action.Description() ?? "";
            }
            catch
            {
                return action.GetType().Name;
            }
        }

        private static BattleSquadSnapshot TryGetSquad(BattleStateSnapshot state, int squadId)
        {
            if (state.AttackerSquads.TryGetValue(squadId, out BattleSquadSnapshot attackerSquad)) return attackerSquad;
            if (state.OpposingSquads.TryGetValue(squadId, out BattleSquadSnapshot opposingSquad)) return opposingSquad;
            return null;
        }

        private static bool ContainsSquad(BattleStateSnapshot state, int squadId)
        {
            return state.AttackerSquads.ContainsKey(squadId) || state.OpposingSquads.ContainsKey(squadId);
        }

        private static IEnumerable<BattleSquadSnapshot> GetAllSquads(BattleStateSnapshot state)
        {
            return state.AttackerSquads.Values.Concat(state.OpposingSquads.Values);
        }

        private static IEnumerable<BattleSquadSnapshot> GetPlayerSquads(BattleStateSnapshot state)
        {
            return GetAllSquads(state).Where(squad => squad.IsPlayerAligned);
        }

        private static IEnumerable<BattleSquadSnapshot> GetOpposingSquads(BattleStateSnapshot state)
        {
            return GetAllSquads(state).Where(squad => !squad.IsPlayerAligned);
        }

        private static int CountAble(BattleSquadSnapshot squad)
        {
            // The compact snapshot list is pruned as casualties occur, so its count is the
            // historically correct strength for that turn even though campaign soldier identity
            // references are shared.
            return squad?.Soldiers.Count ?? 0;
        }

        private static int CountAble(IEnumerable<BattleSquadSnapshot> squads)
        {
            return squads.Sum(CountAble);
        }

        private static string GetUnitName(BattleSquadSnapshot squad)
        {
            return squad?.Squad?.ParentUnit?.Name ?? (squad?.IsPlayerAligned == true ? "Imperial Defenders" : "Enemy Warhost");
        }

        private static string BuildSquadSubtitle(BattleSquadSnapshot squad, int startingStrength, int currentStrength)
        {
            string type = GetFormationType(squad);
            return $"{type} - {currentStrength}/{startingStrength}";
        }

        private static string GetFormationType(BattleSquadSnapshot squad)
        {
            return squad?.Squad?.SquadTemplate?.Name ?? "Formation";
        }

        private static string GetSquadIconKey(BattleSquadSnapshot squad)
        {
            if (squad == null)
            {
                return "infantry";
            }

            SquadTypes type = squad.Squad?.SquadTemplate?.SquadType ?? SquadTypes.None;
            if ((type & SquadTypes.HQ) != 0) return "hq";
            if ((type & SquadTypes.Scout) != 0) return "scout";
            if ((type & SquadTypes.Elite) != 0) return "elite";
            if ((type & SquadTypes.Fast) != 0) return "fast";
            if ((type & SquadTypes.Heavy) != 0) return "heavy";
            if ((type & SquadTypes.Bodyguard) != 0) return "bodyguard";
            return "infantry";
        }

        private static string BuildFatigueLabel(BattleSquadSnapshot squad)
        {
            if (squad == null || squad.Soldiers.Count == 0) return "Broken";
            float runningTurns = squad.Soldiers.Sum(soldier => soldier.TurnsRunning);
            if (runningTurns > squad.Soldiers.Count * 4) return "High";
            if (runningTurns > squad.Soldiers.Count * 2) return "Moderate";
            return "Low";
        }

        private static string BuildMoraleLabel(float lossPercent, int currentStrength)
        {
            if (currentStrength == 0) return "Collapsed";
            if (lossPercent >= 0.5f) return "Wavering";
            if (lossPercent >= 0.25f) return "Pressed";
            return "Steady";
        }

        private static string BuildAmmunitionLabel(BattleSquadSnapshot squad)
        {
            if (squad == null) return "Unknown";
            int shots = squad.Soldiers.Sum(soldier => soldier.TurnsShooting);
            return shots == 0 ? "Unspent" : $"{shots} volleys";
        }
    }
}
