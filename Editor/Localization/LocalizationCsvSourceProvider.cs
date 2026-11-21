using System;
using System.Collections.Generic;
using System.IO;
using Foundation.Content.Authoring;

namespace Foundation.Content.Editor.Localization
{
    public sealed class LocalizationCsvSourceProvider : IConfigSourceProvider
    {
        private readonly LocalizationBuildSettings _settings;

        public LocalizationCsvSourceProvider(LocalizationBuildSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public IEnumerable<IConfigAuthoringSource> Provide()
        {
            var path = _settings.CsvPath;
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("A localization CSV path is required.");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Localization CSV not found: {path}", path);

            yield return new LocalizationCsvSource(LocalizationCsvReader.ReadFile(path));
        }
    }
}
