using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Foundation.Content.Authoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Content.Editor
{
    public sealed class ConfigBuilder
    {
        private static readonly char[] PortableInvalidFileNameChars = { '<', '>', ':', '"', '|', '?', '*' };

        private readonly ConfigBuildSettings _settings;
        private readonly IReadOnlyList<IConfigExporter> _exporters;
        private readonly IReadOnlyList<IConfigValidator> _validators;
        private readonly IReadOnlyList<IConfigSourceProvider> _sourceProviders;
        private readonly IReadOnlyList<IConfigBuildPostProcessor> _postProcessors;

    public ConfigBuilder(ConfigBuildSettings settings, ConfigBuildRegistry registry)
        : this(
            settings,
            registry.Exporters,
            registry.Validators,
            registry.SourceProviders,
            registry.PostProcessors)
    {
    }

    public ConfigBuilder(
        ConfigBuildSettings settings,
        IEnumerable<IConfigExporter> exporters,
        IEnumerable<IConfigValidator> validators,
        IEnumerable<IConfigSourceProvider> sourceProviders = null,
        IEnumerable<IConfigBuildPostProcessor> postProcessors = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _exporters = exporters?.ToArray() ?? throw new ArgumentNullException(nameof(exporters));
        _validators = validators?.ToArray() ?? Array.Empty<IConfigValidator>();
        _sourceProviders = sourceProviders?.ToArray() ?? Array.Empty<IConfigSourceProvider>();
        _postProcessors = postProcessors?.ToArray() ?? Array.Empty<IConfigBuildPostProcessor>();
    }

        public IReadOnlyList<IConfigAuthoringSource> DiscoverSources()
        {
            var result = new List<IConfigAuthoringSource>();
            var guids = AssetDatabase.FindAssets("t:ScriptableObject");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset is IConfigAuthoringSource source)
                    result.Add(source);
            }

            foreach (var provider in _sourceProviders)
                result.AddRange(provider.Provide());

            return result;
        }

        public IReadOnlyList<ConfigExportGroup> InspectSource(IConfigAuthoringSource source)
        {
            var matches = _exporters.Where(exporter => exporter.SourceType.IsInstanceOfType(source)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected one exporter for '{source.GetType().Name}', found {matches.Length}.");
            return matches[0].Export(source);
        }

        public ConfigBuildReport Validate(IReadOnlyList<IConfigAuthoringSource> sources) => Execute(sources, false);

        public ConfigBuildReport Build(IReadOnlyList<IConfigAuthoringSource> sources) => Execute(sources, true);

        private ConfigBuildReport Execute(IReadOnlyList<IConfigAuthoringSource> sources, bool writeFiles)
        {
            var errors = new List<ConfigBuildError>();
            var resolved = new List<(IConfigAuthoringSource source, IReadOnlyList<ConfigExportGroup> groups)>();

            foreach (var source in sources)
            {
                var sourceName = GetSourceName(source);
                var matches = _exporters.Where(e => e.SourceType.IsInstanceOfType(source)).ToArray();

                if (matches.Length == 0)
                {
                    errors.Add(new ConfigBuildError(
                        sourceName,
                        null,
                        ConfigBuildErrorKind.NoExporter,
                        $"No exporter registered for source type '{source.GetType().Name}'."));
                    continue;
                }

                if (matches.Length > 1)
                {
                    errors.Add(new ConfigBuildError(
                        sourceName,
                        null,
                        ConfigBuildErrorKind.MultipleExporters,
                        $"Multiple exporters registered for source type '{source.GetType().Name}'."));
                    continue;
                }

                var groups = matches[0].Export(source);
                resolved.Add((source, groups));
            }

            var outputOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (source, groups) in resolved)
            {
                var sourceName = GetSourceName(source);
                foreach (var group in groups)
                {
                    if (!IsValidFileName(group.FileName))
                    {
                        errors.Add(new ConfigBuildError(
                            sourceName,
                            group.FileName,
                            ConfigBuildErrorKind.InvalidFileName,
                            $"Output file name '{group.FileName}' is invalid."));
                        continue;
                    }

                    var outputPath = Path.GetFullPath(Path.Combine(_settings.OutputDirectory, group.FileName));
                    if (outputOwners.TryGetValue(outputPath, out var owner))
                    {
                        errors.Add(new ConfigBuildError(sourceName, group.FileName,
                            ConfigBuildErrorKind.DuplicateOutput,
                            $"Output file '{group.FileName}' is also produced by '{owner}'."));
                    }
                    else
                    {
                        outputOwners.Add(outputPath, sourceName);
                    }

                    ValidateEntryIds(sourceName, group, errors);
                }
            }

            foreach (var (source, groups) in resolved)
            {
                foreach (var validator in _validators)
                    errors.AddRange(validator.Validate(source, groups));
            }

            if (errors.Count > 0)
                return ConfigBuildReport.CreateFailure(errors);

            if (!writeFiles)
                return ConfigBuildReport.CreateSuccess(Array.Empty<string>());

            if (!ConfigSerializers.TryResolve(_settings.Serializer, out var serializer, out var serializerProblem))
            {
                return ConfigBuildReport.CreateFailure(new[]
                {
                    new ConfigBuildError("Serializer", null, ConfigBuildErrorKind.NoSerializer, serializerProblem),
                });
            }

            var outputs = new List<(string reportPath, string fullPath, byte[] contents)>();
            foreach (var (_, groups) in resolved)
            {
                foreach (var group in groups)
                {
                    var reportPath = Path.Combine(_settings.OutputDirectory, group.FileName);
                    outputs.Add((reportPath, Path.GetFullPath(reportPath), group.Serialize(serializer)));
                }
            }

            if (outputs.Count == 0)
            {
                foreach (var postProcessor in _postProcessors)
                    postProcessor.OnBuildSucceeded(sources);

                return ConfigBuildReport.CreateSuccess(Array.Empty<string>());
            }

            var outputDirectory = Path.GetFullPath(_settings.OutputDirectory);
            Directory.CreateDirectory(outputDirectory);
            var stagingDirectory = Path.Combine(
                outputDirectory,
                ".foundation-config-build-" + Guid.NewGuid().ToString("N"));
            var stagedPaths = new string[outputs.Count];
            var committedFiles = new List<(string outputPath, string backupPath, bool replacedExisting)>();
            var preserveStagingDirectory = false;
            try
            {
                Directory.CreateDirectory(stagingDirectory);
                for (var i = 0; i < outputs.Count; i++)
                {
                    stagedPaths[i] = Path.Combine(stagingDirectory, "new-" + i);
                    File.WriteAllBytes(stagedPaths[i], outputs[i].contents);
                }

                for (var i = 0; i < outputs.Count; i++)
                {
                    var outputPath = outputs[i].fullPath;
                    if (File.Exists(outputPath))
                    {
                        var backupPath = Path.Combine(stagingDirectory, "backup-" + i);
                        File.Replace(stagedPaths[i], outputPath, backupPath);
                        committedFiles.Add((outputPath, backupPath, true));
                    }
                    else
                    {
                        File.Move(stagedPaths[i], outputPath);
                        committedFiles.Add((outputPath, null, false));
                    }
                }

                foreach (var postProcessor in _postProcessors)
                    postProcessor.OnBuildSucceeded(sources);

                return ConfigBuildReport.CreateSuccess(outputs.Select(output => output.reportPath).ToArray());
            }
            catch (Exception buildException)
            {
                var rollbackExceptions = new List<Exception>();
                for (var i = committedFiles.Count - 1; i >= 0; i--)
                {
                    var committedFile = committedFiles[i];
                    try
                    {
                        if (committedFile.replacedExisting)
                        {
                            var rollbackBackupPath = Path.Combine(stagingDirectory, "rollback-" + i);
                            File.Replace(committedFile.backupPath, committedFile.outputPath, rollbackBackupPath);
                        }
                        else if (File.Exists(committedFile.outputPath))
                        {
                            File.Delete(committedFile.outputPath);
                        }
                    }
                    catch (Exception rollbackException)
                    {
                        rollbackExceptions.Add(rollbackException);
                    }
                }

                if (rollbackExceptions.Count > 0)
                {
                    preserveStagingDirectory = true;
                    rollbackExceptions.Insert(0, buildException);
                    throw new AggregateException(
                        "Config build failed and one or more output files could not be restored. Staged backups were preserved.",
                        rollbackExceptions);
                }

                throw;
            }
            finally
            {
                if (!preserveStagingDirectory && Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, true);
            }
        }

        private static void ValidateEntryIds(
            string sourceName,
            ConfigExportGroup group,
            List<ConfigBuildError> errors)
        {
            if (group.ContainsNullEntry())
            {
                errors.Add(new ConfigBuildError(
                    sourceName,
                    group.FileName,
                    ConfigBuildErrorKind.NullEntry,
                    "Group contains null entries."));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in group.EntryIds)
            {
                if (string.IsNullOrEmpty(id))
                {
                    errors.Add(new ConfigBuildError(
                        sourceName,
                        group.FileName,
                        ConfigBuildErrorKind.EmptyId,
                        "Entry has an empty or missing Id."));
                    continue;
                }

                if (!seen.Add(id))
                {
                    errors.Add(new ConfigBuildError(
                        sourceName,
                        group.FileName,
                        ConfigBuildErrorKind.DuplicateId,
                        $"Duplicate Id '{id}' within group '{group.FileName}'.",
                        id));
                }
            }
        }

        private static bool IsValidFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            if (fileName == "." || fileName == "..")
                return false;

            if (fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0)
                return false;

            if (fileName.StartsWith(".."))
                return false;

            if (fileName.EndsWith(".", StringComparison.Ordinal) || fileName.EndsWith(" ", StringComparison.Ordinal))
                return false;

            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.IndexOfAny(PortableInvalidFileNameChars) >= 0
                || fileName.Any(char.IsControl))
                return false;

            if (Path.GetFileName(fileName) != fileName || IsWindowsReservedDeviceName(fileName))
                return false;

            return true;
        }

        private static bool IsWindowsReservedDeviceName(string fileName)
        {
            var extensionIndex = fileName.IndexOf('.');
            var baseName = extensionIndex < 0 ? fileName : fileName.Substring(0, extensionIndex);
            baseName = baseName.TrimEnd(' ', '.');

            if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
                || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
                || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || baseName.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
                || baseName.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
                return true;

            return baseName.Length == 4
                && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && baseName[3] >= '1'
                && baseName[3] <= '9';
        }

        private static string GetSourceName(IConfigAuthoringSource source)
            => (source as Object)?.name ?? source.GetType().Name;
    }
}
