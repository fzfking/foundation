#if UNITY_EDITOR || FOUNDATION_DEVELOPMENT
using System;
using System.Collections.Generic;

namespace Foundation.Diagnostics
{
    public sealed class OperationTraceOptions
    {
        private readonly HashSet<string> _mutedNames = new(StringComparer.Ordinal);

        public OperationTraceOptions(OperationTraceCategory categories = OperationTraceCategory.Default)
        {
            Categories = categories;
        }

        public OperationTraceCategory Categories { get; set; }

        public IReadOnlyCollection<string> MutedNames => _mutedNames;

        public OperationTraceOptions Mute(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _mutedNames.Add(name);
            return this;
        }

        public bool IsEnabled(OperationTraceCategory category) => (Categories & category) != 0;

        public bool IsMuted(string name) => name != null && _mutedNames.Contains(name);
    }
}
#endif
