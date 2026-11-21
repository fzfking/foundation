using System;
using System.Collections.Generic;
using Foundation.Content.Authoring;

namespace Foundation.Content.Editor
{
    public interface IConfigBuildPostProcessor
    {
        /// <summary>
        /// Declares extra files for validation and build without writing to disk. A build commits them with the config binaries.
        /// </summary>
        IEnumerable<(string Path, byte[] Contents)> CollectOutputs(IReadOnlyList<IConfigAuthoringSource> sources) =>
            Array.Empty<(string, byte[])>();

        /// <summary>Runs after the files are committed. Exceptions propagate and leave the committed files in place.</summary>
        void OnBuildSucceeded(IReadOnlyList<IConfigAuthoringSource> sources);
    }
}
