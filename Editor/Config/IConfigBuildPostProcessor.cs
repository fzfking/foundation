using System.Collections.Generic;
using Foundation.Content.Authoring;

namespace Foundation.Content.Editor
{
    public interface IConfigBuildPostProcessor
    {
        void OnBuildSucceeded(IReadOnlyList<IConfigAuthoringSource> sources);
    }
}
