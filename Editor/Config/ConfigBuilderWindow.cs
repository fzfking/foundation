using System.Collections.Generic;
using System.Linq;
using Foundation.Content.Authoring;
using UnityEditor;
using UnityEngine;

namespace Foundation.Content.Editor
{
    public sealed class ConfigBuilderWindow : EditorWindow
    {
        private IReadOnlyList<IConfigAuthoringSource> _sources;
        private ConfigBuildReport _lastReport;

        private void OnGUI()
        {
            if (_sources == null)
                Refresh();

            EditorGUILayout.LabelField($"Config sources: {_sources.Count}");

            if (_sources.Count == 0)
            {
                EditorGUILayout.HelpBox("No IConfigAuthoringSource assets found.", MessageType.Warning);
            }
            else
            {
                foreach (var source in _sources)
                    EditorGUILayout.LabelField("  " + (source as Object)?.name);
            }

            EditorGUILayout.Space();

        if (GUILayout.Button("Validate"))
        {
            var builder = ConfigBuildEntryPoint.BuilderFactory();
            _lastReport = builder.Validate(_sources);
            LogReport(_lastReport, true);
        }

        if (GUILayout.Button("Build"))
        {
            var builder = ConfigBuildEntryPoint.BuilderFactory();
            _lastReport = builder.Build(_sources);
            LogReport(_lastReport);

            if (_lastReport.Success)
                AssetDatabase.Refresh();
        }

            if (_lastReport != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    _lastReport.Success ? "Result: OK" : $"Result: {_lastReport.Errors.Count} error(s)");
            }
        }

        [MenuItem("Content/Config/Build All Config")]
        public static void BuildAllConfig()
        {
            RunBuild();
        }

    [MenuItem("Content/Config/Validate Config")]
    public static void ValidateConfig()
    {
        var builder = ConfigBuildEntryPoint.BuilderFactory();
        var sources = builder.DiscoverSources();
        var report = builder.Validate(sources);
        LogReport(report, true);
    }

        [MenuItem("Content/Config/Config Builder")]
        public static void OpenWindow()
        {
            GetWindow<ConfigBuilderWindow>("Config Builder").Refresh();
        }

    private static void RunBuild()
    {
        var builder = ConfigBuildEntryPoint.BuilderFactory();
        var sources = builder.DiscoverSources();
        var report = builder.Build(sources);
        LogReport(report);

        if (report.Success)
            AssetDatabase.Refresh();
    }

        private static void LogReport(ConfigBuildReport report, bool validateOnly = false)
        {
            if (report.Success)
            {
                if (validateOnly)
                {
                    Debug.Log("[ConfigBuilder] Validation passed; no files written.");
                    return;
                }

                Debug.Log(
                    $"[ConfigBuilder] Built {report.WrittenFiles.Count} file(s):\n" +
                    string.Join("\n", report.WrittenFiles.Select(f => "  " + f)));
                return;
            }

            foreach (var error in report.Errors)
                Debug.LogError($"[ConfigBuilder] {error}");
        }

    private void Refresh()
    {
        var builder = ConfigBuildEntryPoint.BuilderFactory();
        _sources = builder.DiscoverSources();
    }
    }
}
