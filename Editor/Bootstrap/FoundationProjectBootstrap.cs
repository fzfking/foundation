using System.Reflection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Foundation.Bootstrap
{
    internal static class FoundationProjectBootstrap
    {
        private const string MenuRoot = "Foundation/Bootstrap New Project/";

        // The skeleton every project gets regardless of its features. Game-specific folders (Buildings,
        // Economy, Planets) are not part of it; they grow under Scripts/Features and GameContent/Configs.
        private static readonly string[] GameContentFolders =
        {
            "Assets/GameContent/Audio/SFX",
            "Assets/GameContent/Audio/Ambient",
            "Assets/GameContent/Audio/Music",
            "Assets/GameContent/Audio/Mixers",
            "Assets/GameContent/Configs/Tutorial",
            "Assets/GameContent/Configs/FeatureFlags",
            "Assets/GameContent/Localization",
            "Assets/GameContent/UI/Fonts",
            "Assets/GameContent/UI/Common",
            "Assets/GameContent/UI/Views",
            "Assets/GameContent/UI/Screens",
            "Assets/GameContent/UI/Popups",
            "Assets/GameContent/UI/Icons",
            "Assets/GameContent/UI/Tutorial",
            "Assets/GameContent/Art",
            "Assets/GameContent/Scenes",
        };

        private static readonly string[] ScriptFolders =
        {
            "Assets/Scripts/Project/Lifecycle",
            "Assets/Scripts/Project/Presentation/UI",
            "Assets/Scripts/Project/Localization",
            "Assets/Scripts/Project/Tutorial",
            "Assets/Scripts/Project/FeatureFlags",
            "Assets/Scripts/Project/Editor/Localization",
            "Assets/Scripts/Content/Data",
            "Assets/Scripts/Content/Authoring",
            "Assets/Scripts/Features",
            "Assets/Scripts/Tests/Project",
            "Assets/Scripts/Tests/Content",
        };

        [MenuItem(MenuRoot + "Generate Project Folders", false, 5)]
        private static void GenerateProjectFolders()
        {
            var created = new List<string>();
            foreach (var path in GameContentFolders.Concat(ScriptFolders))
                if (CreateFolderRecursive(path))
                    created.Add(path);

            AssetDatabase.Refresh();

            var report = created.Count > 0
                ? "Created:\n" + string.Join("\n", created)
                : "Every folder already existed — nothing to do.";
            Debug.Log("[Foundation Bootstrap] Generate Project Folders: " +
                (created.Count > 0 ? string.Join(", ", created) : "nothing new"));
            EditorUtility.DisplayDialog("Foundation Bootstrap - Generate Project Folders", report, "OK");
        }

        private static bool CreateFolderRecursive(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return false;

            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            var folderName = Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                CreateFolderRecursive(parent);

            AssetDatabase.CreateFolder(parent, folderName);
            return true;
        }

        [MenuItem(MenuRoot + "Enable GameFramework Module", false, 10)]
        private static void EnableGameFramework()
        {
            SetDefineForAllTargets("FOUNDATION_GAMEFRAMEWORK", true);
            Debug.Log("[Foundation Bootstrap] FOUNDATION_GAMEFRAMEWORK set for all build target groups.");
        }

        [MenuItem(MenuRoot + "Finish Setup (Verify And Set Defines)", false, 20)]
        private static void FinishSetup()
        {
            var memoryPackPresent = IsAssemblyPresent("MemoryPack") || IsAssemblyPresent("MemoryPack.Core");
            var messagePackPresent = IsAssemblyPresent("MessagePack");
            var smartFormatPresent = IsAssemblyPresent("SmartFormat");

            if (memoryPackPresent)
                SetDefineForAllTargets("FOUNDATION_MEMORYPACK", true);
            if (messagePackPresent)
                SetDefineForAllTargets("FOUNDATION_MESSAGEPACK", true);
            if (smartFormatPresent)
                SetDefineForAllTargets("FOUNDATION_SMARTFORMAT", true);

            var message =
                "MemoryPack: " + (memoryPackPresent
                    ? "found - FOUNDATION_MEMORYPACK has been set for all build target groups."
                    : "not found - optional; install MemoryPack with NuGetForUnity, restore packages via NuGet > Restore Packages, then run this again. Without it Foundation.Infrastructure.MemoryPack stays excluded; pick another serializer for the content build or supply your own.") +
                "\n\nMessagePack: " + (messagePackPresent
                    ? "found - FOUNDATION_MESSAGEPACK has been set for all build target groups."
                    : "not installed - optional; Foundation.Infrastructure.MessagePack stays excluded.") +
                "\n\nSmartFormat: " + (smartFormatPresent
                    ? "found - FOUNDATION_SMARTFORMAT has been set for all build target groups."
                    : "not installed - optional; Foundation.Infrastructure.SmartFormat stays excluded, supply your own ILocalizationFormatter.");

            Debug.Log("[Foundation Bootstrap] " + message.Replace("\n\n", " "));
            EditorUtility.DisplayDialog("Foundation Bootstrap - Finish Setup", message, "OK");
        }

        private static bool IsAssemblyPresent(string simpleName) =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => string.Equals(SafeName(a), simpleName, StringComparison.OrdinalIgnoreCase));

        private static string SafeName(Assembly assembly)
        {
            try { return assembly.GetName().Name; }
            catch { return null; }
        }

        private static void SetDefineForAllTargets(string define, bool enabled)
        {
            foreach (var target in AllNamedBuildTargets())
            {
                var defines = PlayerSettings.GetScriptingDefineSymbols(target);
                var list = defines.Split(';').Where(s => !string.IsNullOrEmpty(s)).ToList();

                bool changed;
                if (enabled && !list.Contains(define))
                {
                    list.Add(define);
                    changed = true;
                }
                else
                {
                    changed = !enabled && list.Remove(define);
                }

                if (changed)
                    PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
            }
        }

        private static IEnumerable<NamedBuildTarget> AllNamedBuildTargets()
        {
            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown)
                    continue;

                NamedBuildTarget named;
                try { named = NamedBuildTarget.FromBuildTargetGroup(group); }
                catch { continue; }

                yield return named;
            }
        }
    }
}
