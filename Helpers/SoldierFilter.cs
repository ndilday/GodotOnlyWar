namespace OnlyWar.Helpers
{
    // The attribute a filter condition tests. The set is intentionally small and
    // enum-driven so new fields (specialty, skill thresholds, injury status, ...) can be
    // added by extending this enum plus the switch in SoldierFilterService and the field
    // list in ChapterFilterDialog.
    public enum SoldierFilterField
    {
        Rank,           // Template.Name; Equals / NotEquals
        Honor,          // award Type + minimum Level, or a rating flag; Has / DoesNotHave
        TimeInService,  // weeks; AtLeast / AtMost
        TimeInRank,     // weeks; AtLeast / AtMost
        TimeInSquad     // weeks; AtLeast / AtMost
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
        // Prefix marking a filter value that refers to a RatingFlag history entry (a
        // yes/no distinction like Novice or aptitude flags) rather than a tiered award.
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

        // A flag option's value carries the exact RatingFlag event text it matches.
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

        // Splits a stored filter value back into its Type and Level.
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

    // A single filter row: field + operator + value. TextValue carries a choice value;
    // NumberValue carries aptitude/duration thresholds and Unit qualifies durations.
    public sealed class SoldierFilterCondition
    {
        public SoldierFilterField Field { get; set; }
        public SoldierFilterOperator Operator { get; set; }
        public string TextValue { get; set; }
        public int NumberValue { get; set; }
        public SoldierDurationUnit Unit { get; set; }

        // Threshold expressed in weeks, matching the SoldierDossierService accessors.
        public int ThresholdWeeks => Unit == SoldierDurationUnit.Years ? NumberValue * 52 : NumberValue;

        public static bool IsDurationField(SoldierFilterField field) =>
            field == SoldierFilterField.TimeInService
            || field == SoldierFilterField.TimeInRank
            || field == SoldierFilterField.TimeInSquad;
    }
}
