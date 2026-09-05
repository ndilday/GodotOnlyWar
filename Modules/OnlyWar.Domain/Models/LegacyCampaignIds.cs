using System.Threading;
namespace OnlyWar.Models;
public static class LegacyCampaignIds
{
    private static int _nextOrderId;
    private static int _nextMissionId;
    public static int GetNextOrderId() => Interlocked.Increment(ref _nextOrderId) - 1;
    public static int GetNextMissionId() => Interlocked.Increment(ref _nextMissionId) - 1;
    public static void SetNextOrderId(int value) => Volatile.Write(ref _nextOrderId, value);
    public static void SetNextMissionId(int value) => Volatile.Write(ref _nextMissionId, value);
}