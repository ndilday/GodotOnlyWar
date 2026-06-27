using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
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
            BattleTurn initialTurn = history.Turns[0];
            BattleTurn currentTurn = history.Turns[currentTurnIndex];
            int? resolvedSelection = ResolveSelectedFormationId(currentTurn.State, selectedFormationId);

            IReadOnlyList<BattleForceHierarchyNode> hierarchy = BuildForceHierarchy(initialTurn.State, currentTurn.State, resolvedSelection);
            BattleFormationSummary selectedFormation = resolvedSelection.HasValue
                ? BuildFormationSummary(initialTurn.State, currentTurn.State, resolvedSelection.Value)
                : null;

            return new BattleReplayDisplay(
                currentTurnIndex,
                currentTurn.TurnNumber,
                history.Turns[^1].TurnNumber,
                "Battle Chronicle",
                BuildPhaseLabel(currentTurn),
                BuildResultLabel(initialTurn.State, currentTurn.State),
                resolvedSelection,
                hierarchy,
                selectedFormation,
                BuildEventEntries(currentTurn),
                BuildTimeline(history, currentTurnIndex),
                BuildCasualtySummaries(history));
        }

        private static int? ResolveSelectedFormationId(BattleState state, int? selectedFormationId)
        {
            if (selectedFormationId.HasValue && ContainsSquad(state, selectedFormationId.Value))
            {
                return selectedFormationId.Value;
            }

            BattleSquad firstPlayer = state.PlayerSquads.Values.OrderBy(s => s.Id).FirstOrDefault();
            if (firstPlayer != null) return firstPlayer.Id;

            return state.OpposingSquads.Values.OrderBy(s => s.Id).FirstOrDefault()?.Id;
        }

        private static IReadOnlyList<BattleForceHierarchyNode> BuildForceHierarchy(
            BattleState initialState,
            BattleState currentState,
            int? selectedFormationId)
        {
            List<BattleForceHierarchyNode> roots =
            [
                BuildForceRoot("Player Force", "Imperial formations", "controlled", true, initialState.PlayerSquads.Values, currentState.PlayerSquads.Values, selectedFormationId),
                BuildForceRoot("Opposing Force", "Hostile formations", "hostile", false, initialState.OpposingSquads.Values, currentState.OpposingSquads.Values, selectedFormationId)
            ];

            return roots.Where(root => root.StartingStrength > 0 || root.CurrentStrength > 0).ToList();
        }

        private static BattleForceHierarchyNode BuildForceRoot(
            string title,
            string subtitle,
            string iconKey,
            bool isPlayerForce,
            IEnumerable<BattleSquad> initialSquads,
            IEnumerable<BattleSquad> currentSquads,
            int? selectedFormationId)
        {
            List<BattleForceHierarchyNode> children = [];
            Dictionary<string, List<BattleSquad>> currentByUnit = currentSquads
                .OrderBy(squad => GetUnitName(squad))
                .ThenBy(squad => squad.Name)
                .GroupBy(GetUnitName)
                .ToDictionary(group => group.Key, group => group.ToList());
            Dictionary<string, List<BattleSquad>> initialByUnit = initialSquads
                .GroupBy(GetUnitName)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (string unitName in initialByUnit.Keys.Union(currentByUnit.Keys).OrderBy(name => name))
            {
                List<BattleSquad> initialUnitSquads = initialByUnit.TryGetValue(unitName, out List<BattleSquad> initial) ? initial : [];
                List<BattleSquad> currentUnitSquads = currentByUnit.TryGetValue(unitName, out List<BattleSquad> current) ? current : [];
                List<BattleForceHierarchyNode> squadNodes = initialUnitSquads
                    .Select(initialSquad =>
                    {
                        BattleSquad currentSquad = currentUnitSquads.FirstOrDefault(squad => squad.Id == initialSquad.Id);
                        return BuildSquadNode(initialSquad, currentSquad, selectedFormationId);
                    })
                    .Concat(currentUnitSquads
                        .Where(currentSquad => initialUnitSquads.All(initialSquad => initialSquad.Id != currentSquad.Id))
                        .Select(currentSquad => BuildSquadNode(null, currentSquad, selectedFormationId)))
                    .OrderBy(node => node.Title)
                    .ToList();

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

        private static BattleForceHierarchyNode BuildSquadNode(BattleSquad initialSquad, BattleSquad currentSquad, int? selectedFormationId)
        {
            BattleSquad source = currentSquad ?? initialSquad;
            int startingStrength = CountAble(initialSquad);
            int currentStrength = CountAble(currentSquad);
            return new BattleForceHierarchyNode(
                source.Id,
                source.Name,
                BuildSquadSubtitle(source, startingStrength, currentStrength),
                GetSquadIconKey(source),
                source.IsPlayerSquad,
                selectedFormationId == source.Id,
                startingStrength,
                currentStrength,
                []);
        }

        private static BattleFormationSummary BuildFormationSummary(BattleState initialState, BattleState currentState, int formationId)
        {
            BattleSquad initialSquad = TryGetSquad(initialState, formationId);
            BattleSquad currentSquad = TryGetSquad(currentState, formationId);
            BattleSquad source = currentSquad ?? initialSquad;
            int startingStrength = CountAble(initialSquad);
            int currentStrength = CountAble(currentSquad);
            int losses = startingStrength - currentStrength;
            float lossPercent = startingStrength == 0 ? 0 : (float)losses / startingStrength;

            List<string> effects = [];
            if (currentSquad?.IsInMelee == true) effects.Add("Engaged in melee");
            if (currentStrength == 0) effects.Add("Formation combat ineffective");
            if (lossPercent >= 0.5f) effects.Add("Severe losses");
            if (effects.Count == 0) effects.Add("No notable effects tracked");

            return new BattleFormationSummary(
                source.Id,
                source.Name,
                GetUnitName(source),
                source.SquadLeader?.Soldier?.Name ?? "No leader",
                GetFormationType(source),
                source.IsPlayerSquad,
                startingStrength,
                currentStrength,
                BuildFatigueLabel(source),
                BuildMoraleLabel(lossPercent, currentStrength),
                BuildAmmunitionLabel(source),
                effects);
        }

        private static IReadOnlyList<BattleEventEntry> BuildEventEntries(BattleTurn turn)
        {
            List<BattleEventEntry> entries = [];
            int sequence = 0;
            foreach (IAction action in turn.Actions)
            {
                sequence++;
                BattleSoldier actor = turn.State.Soldiers.TryGetValue(action.ActorId, out BattleSoldier soldier) ? soldier : null;
                int woundCount = CountWounds(action);
                string text = SafeDescription(action).Trim();
                entries.Add(new BattleEventEntry(
                    turn.TurnNumber,
                    $"{turn.TurnNumber:00}:{sequence:00}",
                    actor?.Soldier?.Name ?? $"Actor {action.ActorId}",
                    actor?.BattleSquad?.Name ?? "Unknown formation",
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
            int startingPlayer = CountAble(history.Turns[0].State.PlayerSquads.Values);
            int startingOpposing = CountAble(history.Turns[0].State.OpposingSquads.Values);
            int previousPlayerLosses = 0;
            int previousOpposingLosses = 0;

            foreach (BattleTurn turn in history.Turns)
            {
                int currentPlayerLosses = startingPlayer - CountAble(turn.State.PlayerSquads.Values);
                int currentOpposingLosses = startingOpposing - CountAble(turn.State.OpposingSquads.Values);
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
            if (turn.Actions.Any(action => action is ShootAction)) return "Fire phase";
            if (turn.Actions.Any(action => action is MoveAction)) return "Maneuver phase";
            return turn.TurnNumber == 0 ? "Deployment" : "Battle phase";
        }

        private static string BuildResultLabel(BattleState initialState, BattleState currentState)
        {
            int playerCurrent = CountAble(currentState.PlayerSquads.Values);
            int opposingCurrent = CountAble(currentState.OpposingSquads.Values);
            if (playerCurrent == 0 && opposingCurrent == 0) return "Mutual destruction";
            if (opposingCurrent == 0) return "Player force victorious";
            if (playerCurrent == 0) return "Opposing force victorious";

            int playerStarting = CountAble(initialState.PlayerSquads.Values);
            int opposingStarting = CountAble(initialState.OpposingSquads.Values);
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

        private static BattleSquad TryGetSquad(BattleState state, int squadId)
        {
            if (state.PlayerSquads.TryGetValue(squadId, out BattleSquad playerSquad)) return playerSquad;
            if (state.OpposingSquads.TryGetValue(squadId, out BattleSquad opposingSquad)) return opposingSquad;
            return null;
        }

        private static bool ContainsSquad(BattleState state, int squadId)
        {
            return state.PlayerSquads.ContainsKey(squadId) || state.OpposingSquads.ContainsKey(squadId);
        }

        private static int CountAble(BattleSquad squad)
        {
            return squad?.AbleSoldiers.Count ?? 0;
        }

        private static int CountAble(IEnumerable<BattleSquad> squads)
        {
            return squads.Sum(CountAble);
        }

        private static string GetUnitName(BattleSquad squad)
        {
            return squad?.Squad?.ParentUnit?.Name ?? (squad?.IsPlayerSquad == true ? "Strike Force" : "Enemy Warhost");
        }

        private static string BuildSquadSubtitle(BattleSquad squad, int startingStrength, int currentStrength)
        {
            string type = GetFormationType(squad);
            return $"{type} - {currentStrength}/{startingStrength}";
        }

        private static string GetFormationType(BattleSquad squad)
        {
            return squad?.Squad?.SquadTemplate?.Name ?? "Formation";
        }

        private static string GetSquadIconKey(BattleSquad squad)
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

        private static string BuildFatigueLabel(BattleSquad squad)
        {
            if (squad == null || squad.AbleSoldiers.Count == 0) return "Broken";
            float runningTurns = squad.AbleSoldiers.Sum(soldier => soldier.TurnsRunning);
            if (runningTurns > squad.AbleSoldiers.Count * 4) return "High";
            if (runningTurns > squad.AbleSoldiers.Count * 2) return "Moderate";
            return "Low";
        }

        private static string BuildMoraleLabel(float lossPercent, int currentStrength)
        {
            if (currentStrength == 0) return "Collapsed";
            if (lossPercent >= 0.5f) return "Wavering";
            if (lossPercent >= 0.25f) return "Pressed";
            return "Steady";
        }

        private static string BuildAmmunitionLabel(BattleSquad squad)
        {
            if (squad == null) return "Unknown";
            int shots = squad.AbleSoldiers.Sum(soldier => soldier.TurnsShooting);
            return shots == 0 ? "Unspent" : $"{shots} volleys";
        }
    }
}
