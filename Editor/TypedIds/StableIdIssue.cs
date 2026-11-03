using UnityEngine;

namespace Foundation.TypedIds.Editor
{
    public enum StableIdIssueKind
    {
        Duplicate,

        InvalidKey,

        MissingDefinition,
    }

    public readonly struct StableIdIssue
    {
        public StableIdIssueKind Kind { get; }
        public string Id { get; }
        public string Message { get; }

        /// <summary>The asset to select when the issue is clicked.</summary>
        public Object Context { get; }

        public StableIdIssue(StableIdIssueKind kind, string id, string message, Object context)
        {
            Kind = kind;
            Id = id;
            Message = message;
            Context = context;
        }

        public override string ToString() => Kind + ": " + Message;
    }
}
