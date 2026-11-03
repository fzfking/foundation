using System;
using Foundation.Content.Authoring;
using System.Collections.Generic;
using Foundation.Content.Editor.Workspace;

namespace Foundation.Content.Editor.Localization
{
    public sealed class LocalizationCsvSource : IConfigAuthoringSource, IContentWorkspaceFileSource
    {
        public LocalizationDocument Document { get; }

        public string WorkspacePath => Document.SourcePath;

        public LocalizationCsvSource(LocalizationDocument document)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public IReadOnlyList<ContentWorkspaceFile> GetWorkspaceFiles()
            => new[] { new ContentWorkspaceFile(Document.SourcePath, ContentWorkspaceFileFormat.Csv) };

        public override string ToString() => Document.SourcePath;
    }
}
