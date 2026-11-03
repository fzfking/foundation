using System.Collections.Generic;
using System;

namespace Foundation.TypedIds.Editor
{
    /// <summary>The pure rules applied to the definitions of one id type.</summary>
    public static class StableIdValidator
    {
        public static List<StableIdIssue> ValidateDefinitions(IReadOnlyList<StableIdEntry> entries)
        {
            var issues = new List<StableIdIssue>();
            var firstByKey = new Dictionary<string, StableIdEntry>(entries.Count, StringComparer.Ordinal);
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (!StableIdKey.IsValid(entry.Id, out var reason))
                {
                    issues.Add(new StableIdIssue(StableIdIssueKind.InvalidKey, entry.Id,
                        $"'{entry.DisplayName}' has an invalid id '{entry.Id}'. {reason}", entry.Source));
                    continue;
                }

                if (firstByKey.TryGetValue(entry.Id, out var first))
                {
                    issues.Add(new StableIdIssue(StableIdIssueKind.Duplicate, entry.Id,
                        $"Id '{entry.Id}' is used by '{first.DisplayName}' and by '{entry.DisplayName}'.", entry.Source));
                    continue;
                }

                firstByKey.Add(entry.Id, entry);
            }

            return issues;
        }

        /// <summary>
        /// Checks references against the definitions. An empty reference is allowed; only non-empty ids with
        /// no definition are reported.
        /// </summary>
        public static List<StableIdIssue> ValidateReferences(
            IReadOnlyList<StableIdEntry> entries,
            IReadOnlyList<StableIdReference> references)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < entries.Count; index++)
                known.Add(entries[index].Id);

            var issues = new List<StableIdIssue>();
            for (var index = 0; index < references.Count; index++)
            {
                var reference = references[index];
                if (string.IsNullOrEmpty(reference.Id) || known.Contains(reference.Id))
                    continue;

                issues.Add(new StableIdIssue(StableIdIssueKind.MissingDefinition, reference.Id,
                    $"{reference.AssetPath} ({reference.PropertyPath}) references '{reference.Id}', which no definition has.",
                    reference.Asset));
            }

            return issues;
        }
    }
}
