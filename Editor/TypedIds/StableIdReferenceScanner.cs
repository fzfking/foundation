using System;
using System.Collections.Generic;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Foundation.TypedIds.Editor
{
    /// <summary>
    /// Finds every serialized field of one id type in ScriptableObjects and prefabs. Reads serialized data
    /// only, but visits every such asset: do not call it per frame.
    /// </summary>
    public static class StableIdReferenceScanner
    {
        private const string ValueField = "value";

        /// <param name="reportProgress">Reports the current asset path and progress; returning true cancels the scan by throwing <see cref="OperationCanceledException"/>.</param>
        public static List<StableIdReference> Scan(Type idType, Func<string, float, bool> reportProgress = null)
        {
            var references = new List<StableIdReference>();
            var guids = AssetDatabase.FindAssets("t:ScriptableObject t:Prefab", new[] { "Assets" });
            for (var index = 0; index < guids.Length; index++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[index]);
                if (reportProgress != null && reportProgress(path, (float)index / guids.Length))
                    throw new OperationCanceledException("Typed ID reference scanning was cancelled.");

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset != null)
                        ScanObject(asset, path, idType, references);
                }
            }

            return references;
        }

        public static void ScanObject(Object asset, string path, Type idType, List<StableIdReference> references)
        {
            var serialized = new SerializedObject(asset);
            var iterator = serialized.GetIterator();
            var enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyType != SerializedPropertyType.Generic || iterator.type != idType.Name)
                    continue;

                var value = iterator.FindPropertyRelative(ValueField);
                if (value != null && value.propertyType == SerializedPropertyType.String)
                    references.Add(new StableIdReference(value.stringValue, asset, path, iterator.propertyPath));

                enterChildren = false;
            }
        }
    }
}
