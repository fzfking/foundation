using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Foundation.Content.Editor.Workspace;
using Foundation.Content.Authoring;
using Foundation.Content.Editor;
using Foundation.Content.Editor.Localization;
using UnityEditor;
using UnityEngine;

namespace Foundation.Tutorial.Flow.Editor
{
    public sealed class TutorialScriptFile
    {
        public string Path { get; }
        public TutorialScriptConfig Config { get; }
        public IReadOnlyList<TutorialScriptError> Errors { get; }

        public TutorialScriptFile(string path, TutorialScriptConfig config, List<TutorialScriptError> errors)
        {
            Path = path;
            Config = config;
            Errors = errors;
        }
    }

    public sealed class TutorialScriptsSource : IConfigAuthoringSource, IContentWorkspaceFileSource
    {
        /// <summary>The folder the chapter scripts (<c>*.txt</c>, one chapter per file) were read from.</summary>
        public string Folder { get; }

        public string FileName { get; }

        public IReadOnlyList<TutorialScriptFile> Files { get; }

        public string WorkspacePath => Folder;

        public TutorialScriptsSource(string folder, string fileName, IReadOnlyList<TutorialScriptFile> files)
        {
            Folder = folder;
            FileName = fileName;
            Files = files ?? throw new ArgumentNullException(nameof(files));
        }

        public IReadOnlyList<ContentWorkspaceFile> GetWorkspaceFiles()
            => Files.Select(file => new ContentWorkspaceFile(file.Path, ContentWorkspaceFileFormat.Text)).ToArray();

        public override string ToString() => Folder;
    }

    /// <summary>
    /// Reads and checks the chapter scripts in a folder. Command keys come from every
    /// <see cref="TutorialCommandAttribute"/> type; text keys from the localization CSV (skipped without a CSV
    /// path).
    /// </summary>
    public sealed class TutorialScriptSourceProvider : IConfigSourceProvider
    {
        private readonly string _folder;
        private readonly string _fileName;
        private readonly string _localizationCsvPath;

        public TutorialScriptSourceProvider(string folder, string fileName, string localizationCsvPath = null)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("A tutorial script folder is required.", nameof(folder));
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("A compiled tutorial config file name is required.", nameof(fileName));

            _folder = folder;
            _fileName = fileName;
            _localizationCsvPath = localizationCsvPath;
        }

        public IEnumerable<IConfigAuthoringSource> Provide()
        {
            if (!Directory.Exists(_folder))
            {
                Debug.LogWarning($"[Tutorial] '{_folder}' not found; no tutorial chapters are built.");
                yield break;
            }

            var catalog = TutorialCommandCatalog.FromTypes(TypeCache.GetTypesWithAttribute<TutorialCommandAttribute>());
            var textKeys = ReadTextKeys();
            var paths = Directory.GetFiles(_folder, "*.txt", SearchOption.TopDirectoryOnly);
            Array.Sort(paths, StringComparer.Ordinal);

            var files = new List<TutorialScriptFile>(paths.Length);
            foreach (var path in paths)
            {
                var errors = new List<TutorialScriptError>();
                var config = TutorialScriptParser.Parse(File.ReadAllText(path), errors);
                TutorialScriptValidator.Validate(config, catalog, textKeys == null ? null : textKeys.Contains, errors);
                files.Add(new TutorialScriptFile(path.Replace('\\', '/'), config, errors));
            }

            yield return new TutorialScriptsSource(_folder, _fileName, files);
        }

        private HashSet<string> ReadTextKeys()
        {
            if (string.IsNullOrEmpty(_localizationCsvPath) || !File.Exists(_localizationCsvPath))
                return null;

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in LocalizationCsvReader.ReadFile(_localizationCsvPath).Rows)
                keys.Add(row.Key);
            return keys;
        }
    }

    public sealed class TutorialScriptExporter : IConfigExporter
    {
        public Type SourceType => typeof(TutorialScriptsSource);

        public IReadOnlyList<ConfigExportGroup> Export(IConfigAuthoringSource source)
        {
            var scripts = (TutorialScriptsSource)source;
            var configs = new TutorialScriptConfig[scripts.Files.Count];
            var ids = new string[configs.Length];
            for (var index = 0; index < configs.Length; index++)
            {
                configs[index] = scripts.Files[index].Config;
                ids[index] = configs[index]?.Id;
            }

            return new ConfigExportGroup[] { new ConfigExportGroup<TutorialScriptConfig[]>(scripts.FileName, configs, ids) };
        }
    }

    /// <summary>Reports script errors with file and line, and ids that clash across chapters.</summary>
    public sealed class TutorialScriptConfigValidator : IConfigValidator
    {
        public IEnumerable<ConfigBuildError> Validate(IConfigAuthoringSource source, IReadOnlyList<ConfigExportGroup> groups)
        {
            if (!(source is TutorialScriptsSource scripts))
                yield break;

            var chapterIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var stepIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in scripts.Files)
            {
                foreach (var error in file.Errors)
                    yield return Error(scripts.FileName, file.Path, error.ToString());

                var config = file.Config;
                if (config == null || string.IsNullOrEmpty(config.Id))
                    continue;

                if (chapterIds.TryGetValue(config.Id, out var otherChapterFile))
                    yield return Error(scripts.FileName, file.Path, $"Chapter '{config.Id}' is also declared in '{otherChapterFile}'.", config.Id);
                else
                    chapterIds.Add(config.Id, file.Path);

                foreach (var step in config.Steps ?? Array.Empty<TutorialStepScript>())
                {
                    if (step == null || string.IsNullOrEmpty(step.Id))
                        continue;

                    if (stepIds.TryGetValue(step.Id, out var otherStepFile) && otherStepFile != file.Path)
                        yield return Error(scripts.FileName, file.Path, $"Step '{step.Id}' is also declared in '{otherStepFile}'.", step.Id);
                    else
                        stepIds[step.Id] = file.Path;
                }
            }
        }

        private static ConfigBuildError Error(string fileName, string path, string message, string entryId = null)
            => new(path, fileName, ConfigBuildErrorKind.Invalid, message, entryId);
    }
}
