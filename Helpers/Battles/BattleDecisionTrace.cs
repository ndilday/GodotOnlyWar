using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// A deterministic, machine-inspectable diagnostic record for a force-level battle decision.
    /// Field order is part of the rendered format so seeded battle traces remain diffable.
    /// </summary>
    public sealed class BattleDecisionTrace
    {
        private readonly IReadOnlyList<KeyValuePair<string, string>> _orderedFields;

        public string RecordType { get; }
        public IReadOnlyDictionary<string, string> Fields { get; }

        public string this[string fieldName] => Fields[fieldName];

        public BattleDecisionTrace(
            string recordType,
            IEnumerable<KeyValuePair<string, string>> orderedFields)
        {
            if (string.IsNullOrWhiteSpace(recordType))
            {
                throw new ArgumentException("A trace record type is required.", nameof(recordType));
            }

            RecordType = recordType;
            _orderedFields = orderedFields?.ToList()
                ?? throw new ArgumentNullException(nameof(orderedFields));

            Dictionary<string, string> fields = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> field in _orderedFields)
            {
                if (!fields.TryAdd(field.Key, field.Value))
                {
                    throw new ArgumentException(
                        $"Trace field '{field.Key}' appears more than once.",
                        nameof(orderedFields));
                }
            }

            Fields = new ReadOnlyDictionary<string, string>(fields);
        }

        public string Render()
        {
            return RecordType + " " + string.Join(
                " ",
                _orderedFields.Select(field => $"{field.Key}={field.Value}"));
        }

        public override string ToString() => Render();

        internal static KeyValuePair<string, string> Field(string name, object value)
        {
            string rendered = value switch
            {
                null => "none",
                bool boolean => boolean ? "true" : "false",
                double number => number.ToString("0.###", CultureInfo.InvariantCulture),
                float number => number.ToString("0.###", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };

            return new KeyValuePair<string, string>(name, rendered);
        }
    }
}
