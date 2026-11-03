using System.Collections.Generic;
using Foundation.Content.Authoring;

namespace Foundation.Content.Editor
{
    public interface IConfigSourceProvider
    {
        IEnumerable<IConfigAuthoringSource> Provide();
    }
}
