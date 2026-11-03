using UnityEngine;

namespace Foundation.TypedIds.Editor
{
    public readonly struct StableIdReference
    {
        public string Id { get; }
        public Object Asset { get; }
        public string AssetPath { get; }
        public string PropertyPath { get; }

        public StableIdReference(string id, Object asset, string assetPath, string propertyPath)
        {
            Id = id;
            Asset = asset;
            AssetPath = assetPath;
            PropertyPath = propertyPath;
        }
    }
}
