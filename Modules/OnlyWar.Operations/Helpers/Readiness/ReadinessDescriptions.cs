namespace OnlyWar.Helpers.Readiness;

// Shared explanations for order/mission reports; rows style these as presentation tokens.
public static class ReadinessDescriptions
{
        public static string BlockerLabel(SquadReadinessBlocker blocker) => blocker switch
        {
            SquadReadinessBlocker.Administrative => "ADMINISTRATIVE",
            SquadReadinessBlocker.EmptyFormation => "EMPTY FORMATION",
            SquadReadinessBlocker.NoEffectiveMembers => "NO EFFECTIVE MEMBERS",
            SquadReadinessBlocker.Leaderless => "NO LEADER",
            SquadReadinessBlocker.BelowMinimumDutyReadyStrength => "BELOW MINIMUM DUTY STRENGTH",
            SquadReadinessBlocker.RequiredLeaderUnavailable => "REQUIRED LEADER UNAVAILABLE",
            SquadReadinessBlocker.ReservedForProcedure => "PROCEDURE RESERVED",
            SquadReadinessBlocker.AssignedElsewhere => "ASSIGNED ELSEWHERE",
            SquadReadinessBlocker.Embarked => "ABOARD SHIP",
            SquadReadinessBlocker.NotLanded => "NOT LANDED",
            SquadReadinessBlocker.NotOrbiting => "NOT IN ORBIT",
            SquadReadinessBlocker.InWarp => "IN WARP",
            SquadReadinessBlocker.CommittedToTraining => "TRAINING",
            SquadReadinessBlocker.OutsideArea => "OUTSIDE AREA",
            SquadReadinessBlocker.MissionUnavailable => "MISSION UNAVAILABLE",
            SquadReadinessBlocker.DestinationCapacity => "NO CAPACITY",
            SquadReadinessBlocker.InWarpContact => "OUT OF CONTACT",
            SquadReadinessBlocker.HistoricalFormation => "HISTORICAL",
            SquadReadinessBlocker.Other => "UNAVAILABLE",
            _ => string.Empty
        };

}
