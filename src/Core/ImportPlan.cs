using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProbablyEdited
{
    public sealed class ImportPlan
    {
        private readonly ReadOnlyDictionary<TextId, string> _importedOverrides;
        private readonly ReadOnlyCollection<string> _warnings;

        public int TotalCount => _importedOverrides.Count;
        public int ConflictCount { get; }
        public int UnavailableCount { get; }
        public IReadOnlyList<string> Warnings => _warnings;

        internal IReadOnlyDictionary<TextId, string> ImportedOverrides => _importedOverrides;
        internal object StateMarker { get; }
        internal long StateRevision { get; }

        internal ImportPlan(
            IDictionary<TextId, string> importedOverrides,
            int conflictCount,
            int unavailableCount,
            IList<string> warnings,
            object stateMarker,
            long stateRevision)
        {
            _importedOverrides = new ReadOnlyDictionary<TextId, string>(
                new Dictionary<TextId, string>(importedOverrides));
            _warnings = new List<string>(warnings).AsReadOnly();
            ConflictCount = conflictCount;
            UnavailableCount = unavailableCount;
            StateMarker = stateMarker;
            StateRevision = stateRevision;
        }
    }
}
