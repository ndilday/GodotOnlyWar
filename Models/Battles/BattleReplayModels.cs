using System.Collections.Generic;

namespace OnlyWar.Models.Battles
{
    public enum BattleEventSeverity
    {
        Normal,
        Warning,
        Critical
    }

    public sealed class BattleReplayDisplay
    {
        public int CurrentTurnIndex { get; }
        public int CurrentTurnNumber { get; }
        public int LastTurnNumber { get; }
        public string BattleTitle { get; }
        public string PhaseLabel { get; }
        public string ResultLabel { get; }
        public int? SelectedFormationId { get; }
        public IReadOnlyList<BattleForceHierarchyNode> ForceHierarchy { get; }
        public BattleFormationSummary SelectedFormation { get; }
        public IReadOnlyList<BattleEventEntry> CurrentTurnEvents { get; }
        public IReadOnlyList<BattleTimelineEntry> Timeline { get; }
        public IReadOnlyList<BattleCasualtyRoundSummary> CasualtiesByRound { get; }

        public BattleReplayDisplay(
            int currentTurnIndex,
            int currentTurnNumber,
            int lastTurnNumber,
            string battleTitle,
            string phaseLabel,
            string resultLabel,
            int? selectedFormationId,
            IReadOnlyList<BattleForceHierarchyNode> forceHierarchy,
            BattleFormationSummary selectedFormation,
            IReadOnlyList<BattleEventEntry> currentTurnEvents,
            IReadOnlyList<BattleTimelineEntry> timeline,
            IReadOnlyList<BattleCasualtyRoundSummary> casualtiesByRound)
        {
            CurrentTurnIndex = currentTurnIndex;
            CurrentTurnNumber = currentTurnNumber;
            LastTurnNumber = lastTurnNumber;
            BattleTitle = battleTitle;
            PhaseLabel = phaseLabel;
            ResultLabel = resultLabel;
            SelectedFormationId = selectedFormationId;
            ForceHierarchy = forceHierarchy;
            SelectedFormation = selectedFormation;
            CurrentTurnEvents = currentTurnEvents;
            Timeline = timeline;
            CasualtiesByRound = casualtiesByRound;
        }
    }

    public sealed class BattleForceHierarchyNode
    {
        public int? FormationId { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public string IconKey { get; }
        public bool IsPlayerForce { get; }
        public bool IsSelected { get; }
        public int StartingStrength { get; }
        public int CurrentStrength { get; }
        public int Losses { get; }
        public IReadOnlyList<BattleForceHierarchyNode> Children { get; }

        public BattleForceHierarchyNode(
            int? formationId,
            string title,
            string subtitle,
            string iconKey,
            bool isPlayerForce,
            bool isSelected,
            int startingStrength,
            int currentStrength,
            IReadOnlyList<BattleForceHierarchyNode> children)
        {
            FormationId = formationId;
            Title = title;
            Subtitle = subtitle;
            IconKey = iconKey;
            IsPlayerForce = isPlayerForce;
            IsSelected = isSelected;
            StartingStrength = startingStrength;
            CurrentStrength = currentStrength;
            Losses = StartingStrength - CurrentStrength;
            Children = children;
        }
    }

    public sealed class BattleFormationSummary
    {
        public int FormationId { get; }
        public string Name { get; }
        public string ForceName { get; }
        public string CommanderName { get; }
        public string FormationType { get; }
        public bool IsPlayerForce { get; }
        public int StartingStrength { get; }
        public int CurrentStrength { get; }
        public int Losses { get; }
        public float LossPercent { get; }
        public string FatigueLabel { get; }
        public string MoraleLabel { get; }
        public string AmmunitionLabel { get; }
        public IReadOnlyList<string> NotableEffects { get; }

        public BattleFormationSummary(
            int formationId,
            string name,
            string forceName,
            string commanderName,
            string formationType,
            bool isPlayerForce,
            int startingStrength,
            int currentStrength,
            string fatigueLabel,
            string moraleLabel,
            string ammunitionLabel,
            IReadOnlyList<string> notableEffects)
        {
            FormationId = formationId;
            Name = name;
            ForceName = forceName;
            CommanderName = commanderName;
            FormationType = formationType;
            IsPlayerForce = isPlayerForce;
            StartingStrength = startingStrength;
            CurrentStrength = currentStrength;
            Losses = StartingStrength - CurrentStrength;
            LossPercent = StartingStrength == 0 ? 0 : (float)Losses / StartingStrength;
            FatigueLabel = fatigueLabel;
            MoraleLabel = moraleLabel;
            AmmunitionLabel = ammunitionLabel;
            NotableEffects = notableEffects;
        }
    }

    public sealed class BattleEventEntry
    {
        public int TurnNumber { get; }
        public string Timestamp { get; }
        public string ActorName { get; }
        public string FormationName { get; }
        public string EventType { get; }
        public string Text { get; }
        public BattleEventSeverity Severity { get; }

        public BattleEventEntry(
            int turnNumber,
            string timestamp,
            string actorName,
            string formationName,
            string eventType,
            string text,
            BattleEventSeverity severity)
        {
            TurnNumber = turnNumber;
            Timestamp = timestamp;
            ActorName = actorName;
            FormationName = formationName;
            EventType = eventType;
            Text = text;
            Severity = severity;
        }
    }

    public sealed class BattleTimelineEntry
    {
        public int TurnIndex { get; }
        public int TurnNumber { get; }
        public string Label { get; }
        public string Summary { get; }
        public bool IsSelected { get; }
        public BattleEventSeverity Severity { get; }

        public BattleTimelineEntry(int turnIndex, int turnNumber, string label, string summary, bool isSelected, BattleEventSeverity severity)
        {
            TurnIndex = turnIndex;
            TurnNumber = turnNumber;
            Label = label;
            Summary = summary;
            IsSelected = isSelected;
            Severity = severity;
        }
    }

    public sealed class BattleCasualtyRoundSummary
    {
        public int TurnNumber { get; }
        public int PlayerLossesThisRound { get; }
        public int OpposingLossesThisRound { get; }
        public int PlayerCumulativeLosses { get; }
        public int OpposingCumulativeLosses { get; }

        public BattleCasualtyRoundSummary(
            int turnNumber,
            int playerLossesThisRound,
            int opposingLossesThisRound,
            int playerCumulativeLosses,
            int opposingCumulativeLosses)
        {
            TurnNumber = turnNumber;
            PlayerLossesThisRound = playerLossesThisRound;
            OpposingLossesThisRound = opposingLossesThisRound;
            PlayerCumulativeLosses = playerCumulativeLosses;
            OpposingCumulativeLosses = opposingCumulativeLosses;
        }
    }
}
