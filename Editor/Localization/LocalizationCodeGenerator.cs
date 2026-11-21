using System;
using System.Collections.Generic;
using System.IO;
using Foundation.Content.Authoring;
using UnityEditor;

namespace Foundation.Content.Editor.Localization
{
    /// <summary>Generates the project's localization constants from the CSV accepted by the content build.</summary>
    public sealed class LocalizationCodeGenerator : IConfigBuildPostProcessor
    {
        private readonly LocalizationBuildSettings _settings;

        public LocalizationCodeGenerator(LocalizationBuildSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public IEnumerable<(string Path, byte[] Contents)> CollectOutputs(IReadOnlyList<IConfigAuthoringSource> sources)
        {
            foreach (var source in sources)
            {
                if (source is not LocalizationCsvSource csvSource)
                    continue;

                foreach (var output in LocalizationKeysGenerator.CollectOutputs(
                             csvSource.Document, _settings.KeysOutputDirectory, _settings.KeysNamespace))
                    yield return output;
            }
        }

        public void OnBuildSucceeded(IReadOnlyList<IConfigAuthoringSource> sources)
        {
            foreach (var source in sources)
            {
                if (source is not LocalizationCsvSource)
                    continue;

                ImportScript(_settings.KeysOutputDirectory, LocalizationKeysGenerator.KeysFileName);
                ImportScript(_settings.KeysOutputDirectory, LocalizationKeysGenerator.LocalesFileName);
                return;
            }
        }

        private static void ImportScript(string directory, string fileName)
        {
            var path = FileUtil.GetProjectRelativePath(Path.GetFullPath(Path.Combine(directory, fileName)));
            if (!string.IsNullOrEmpty(path) &&
                (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)))
                AssetDatabase.ImportAsset(path);
        }
    }
}
