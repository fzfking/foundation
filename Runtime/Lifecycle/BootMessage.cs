namespace Foundation.Lifecycle
{
    /// <summary>A title and a description shown on the loading curtain before any localization exists.</summary>
    public readonly struct BootMessage
    {
        public string Title { get; }

        public string Description { get; }

        public BootMessage(string title, string description)
        {
            Title = title;
            Description = description;
        }
    }
}
