using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Foundation.TypedIds.Editor
{
    /// <summary>
    /// Base for definitions that live in ScriptableObject assets of one type; the derived class describes the
    /// ids inside each asset.
    /// </summary>
    public abstract class ScriptableObjectStableIdProvider<TIdType, TAsset> : IStableIdProvider
        where TAsset : ScriptableObject
    {
        public Type IdType => typeof(TIdType);

        public void Collect(List<StableIdEntry> entries)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(TAsset).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<TAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                    Describe(asset, entries);
            }
        }

        protected abstract void Describe(TAsset asset, List<StableIdEntry> entries);
    }
}
