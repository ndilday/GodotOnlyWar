namespace OnlyWar.Runtime;
public static class IdGenerator
{
    public static int GetNextOrderId() => Models.LegacyCampaignIds.GetNextOrderId();
    public static int GetNextMissionId() => Models.LegacyCampaignIds.GetNextMissionId();
    public static void SetNextOrderId(int value) => Models.LegacyCampaignIds.SetNextOrderId(value);
    public static void SetNextMissionId(int value) => Models.LegacyCampaignIds.SetNextMissionId(value);
}