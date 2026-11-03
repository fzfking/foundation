using UnityEngine;

namespace Foundation.TypedIds.Editor
{
    public readonly struct StableIdEntry
    {
        /// <summary>The permanent key stored in saves and configs.</summary>
        public string Id { get; }

        /// <summary>What designers read. May change freely: only <see cref="Id"/> is referenced.</summary>
        public string DisplayName { get; }

        /// <summary>Slash separated path used to group choices in pickers.</summary>
        public string Group { get; }

        public Object Source { get; }

        public string Description { get; }

        public StableIdEntry(string id, string displayName, string group = null, Object source = null, string description = null)
        {
            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
            Group = group;
            Source = source;
            Description = description;
        }
    }
}
