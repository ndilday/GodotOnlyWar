namespace OnlyWar.Builders;

/// <summary>Legacy namespace façade over Runtime's single campaign ID service.</summary>
public static class IdGenerator
{
    public static int GetNextOrderId() => Runtime.IdGenerator.GetNextOrderId();
    public static int GetNextMissionId() => Runtime.IdGenerator.GetNextMissionId();
    public static void SetNextOrderId(int value) => Runtime.IdGenerator.SetNextOrderId(value);
    public static void SetNextMissionId(int value) => Runtime.IdGenerator.SetNextMissionId(value);
}

