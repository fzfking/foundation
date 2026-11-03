using System;
using System.Collections.Generic;
using Foundation.Content.Authoring;

namespace Foundation.Content.Editor
{
    public interface IConfigExporter
    {
        Type SourceType { get; }

        IReadOnlyList<ConfigExportGroup> Export(IConfigAuthoringSource source);
    }
}
