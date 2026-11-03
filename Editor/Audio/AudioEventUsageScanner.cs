using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Foundation.TypedIds.Editor;

namespace Foundation.Audio.Editor
{
    /// <summary>
    /// Finds events nothing refers to: no string literal in code and no serialized <see cref="AudioEventId"/>
    /// holds their id. Ids built at runtime from pieces are invisible, so treat the result as candidates.
    /// </summary>
    public static class AudioEventUsageScanner
    {
        private static readonly Regex StringLiteral = new("\"([A-Za-z0-9_.:/\\-]{1,128})\"", RegexOptions.Compiled);

        public static HashSet<string> FindUnused(IEnumerable<string> eventIds)
        {
            var used = CollectReferencedStrings();
            foreach (var reference in StableIdReferenceScanner.Scan(typeof(AudioEventId)))
            {
                if (!string.IsNullOrEmpty(reference.Id))
                    used.Add(reference.Id);
            }

            var unused = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in eventIds)
            {
                if (!string.IsNullOrEmpty(id) && !used.Contains(id))
                    unused.Add(id);
            }

            return unused;
        }

        private static HashSet<string> CollectReferencedStrings()
        {
            var literals = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles("Assets", "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in StringLiteral.Matches(File.ReadAllText(path)))
                    literals.Add(match.Groups[1].Value);
            }

            return literals;
        }
    }
}
