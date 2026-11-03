using System.Collections.Generic;
using Foundation.Content.Authoring;

namespace Foundation.Content.Editor
{
    public interface IConfigValidator
    {
        IEnumerable<ConfigBuildError> Validate(
            IConfigAuthoringSource source,
            IReadOnlyList<ConfigExportGroup> groups);
    }
}
