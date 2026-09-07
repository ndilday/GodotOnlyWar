using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Database.GameRules
{
    /// <summary>
    /// Resolves a rules-data reference with the source relation in the exception. Loaders should
    /// use this at the hydration boundary so malformed data never leaks a raw KeyNotFoundException
    /// or an unqualified LINQ failure to the campaign startup path.
    /// </summary>
    internal static class RulesDatabaseLookup
    {
        public static TValue Require<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue> values,
            TKey key,
            string source)
        {
            if (values == null || !values.TryGetValue(key, out TValue value))
            {
                throw new InvalidOperationException(
                    $"Rules database reference '{source}' points to missing id '{key}'.");
            }
            return value;
        }
    }
}
