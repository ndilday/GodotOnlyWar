namespace OnlyWar.Application.Abstractions;

// The attribute a filter condition tests. The set is intentionally small and enum-driven so new
// fields can be added by extending this contract plus the application query and filter service.
public enum SoldierFilterField
{
    Rank,
    Honor,
    TimeInService,
    TimeInRank,
    TimeInSquad,
    SergeantRecommended
}

public enum SoldierFilterOperator
{
    Equals,
    NotEquals,
    Below,
    Above,
    Has,
    DoesNotHave,
    AtLeast,
    AtMost
}

public enum SoldierDurationUnit
{
    Weeks,
    Years
}

public sealed class SoldierHonorFilterOption
{
    private const string FlagPrefix = "flag:";

    public string Value { get; }
    public string Label { get; }
    public string Type { get; }
    public ushort Level { get; }

    public SoldierHonorFilterOption(string type, ushort level, string sampleName)
    {
        Type = type;
        Level = level;
        Value = ToValue(type, level);
        Label = string.IsNullOrWhiteSpace(sampleName)
            ? $"{type} Level {level}"
            : $"{sampleName} (Level {level})";
    }

    private SoldierHonorFilterOption(string flagText, string label)
    {
        Value = FlagPrefix + flagText;
        Label = label;
    }

    public static SoldierHonorFilterOption FromFlag(string flagText, string label) =>
        new(flagText, label);

    public static bool TryParseFlag(string value, out string flagText)
    {
        if (value != null && value.StartsWith(FlagPrefix))
        {
            flagText = value.Substring(FlagPrefix.Length);
            return true;
        }
        flagText = null;
        return false;
    }

    public static string ToValue(string type, ushort level) => $"{type}|{level}";

    public static bool TryParse(string value, out string type, out ushort? level)
    {
        type = null;
        level = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int separator = value.IndexOf('|');
        if (separator < 0)
        {
            type = value;
            return true;
        }

        type = value.Substring(0, separator);
        if (ushort.TryParse(value.Substring(separator + 1), out ushort parsed))
        {
            level = parsed;
        }
        return true;
    }
}

// A single filter row: field + operator + value. TextValue carries a choice value; NumberValue
// carries aptitude/duration thresholds and Unit qualifies durations.
public sealed class SoldierFilterCondition
{
    public SoldierFilterField Field { get; set; }
    public SoldierFilterOperator Operator { get; set; }
    public string TextValue { get; set; }
    public int NumberValue { get; set; }
    public SoldierDurationUnit Unit { get; set; }

    public int ThresholdWeeks => Unit == SoldierDurationUnit.Years ? NumberValue * 52 : NumberValue;

    public static bool IsDurationField(SoldierFilterField field) =>
        field == SoldierFilterField.TimeInService
        || field == SoldierFilterField.TimeInRank
        || field == SoldierFilterField.TimeInSquad;
}
