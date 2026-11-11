using System;
using System.Collections.Generic;

namespace ProbablyEdited
{
    internal static class SourceFilterChoices
    {
        public static List<string> BuildValues(ICollection<string> sourceIds, string gameSourceId)
        {
            var values = new List<string>(sourceIds.Count + 1);
            bool hasGameSource = false;
            foreach (string sourceId in sourceIds)
            {
                if (string.Equals(sourceId, gameSourceId, StringComparison.Ordinal))
                    hasGameSource = true;
                else
                    values.Add(sourceId);
            }

            values.Sort(StringComparer.OrdinalIgnoreCase);
            if (hasGameSource)
                values.Insert(0, gameSourceId);
            values.Add(null);
            return values;
        }

        public static string ResolveSelection(
            ICollection<string> sourceIds,
            string selectedSource,
            bool allSourcesExplicitlySelected,
            string gameSourceId)
        {
            if (selectedSource == null && allSourcesExplicitlySelected)
                return null;
            if (selectedSource != null && sourceIds.Contains(selectedSource))
                return selectedSource;
            return sourceIds.Contains(gameSourceId) ? gameSourceId : null;
        }
    }
}
