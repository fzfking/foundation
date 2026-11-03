namespace Foundation.Content.Editor
{
    public enum ConfigBuildErrorKind
    {
        NoExporter,
        MultipleExporters,
        InvalidFileName,
        EmptyId,
        DuplicateId,
        NullEntry,
        Invalid,
        NoSerializer,
        DuplicateOutput,
    }

    public sealed class ConfigBuildError
    {
        public string Source { get; }

        public string Group { get; }

        public string EntryId { get; }

        public ConfigBuildErrorKind Kind { get; }

        public string Message { get; }

        public ConfigBuildError(
            string source,
            string group,
            ConfigBuildErrorKind kind,
            string message,
            string entryId = null)
        {
            Source = source;
            Group = group;
            Kind = kind;
            Message = message;
            EntryId = entryId;
        }

        public override string ToString()
        {
            var scope = string.IsNullOrEmpty(Group) ? Source : $"{Source}/{Group}";
            if (!string.IsNullOrEmpty(EntryId))
                scope += $"#{EntryId}";

            return $"[{Kind}] {scope}: {Message}";
        }
    }
}
