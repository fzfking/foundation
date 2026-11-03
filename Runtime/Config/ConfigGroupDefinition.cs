using System;

namespace Foundation.Config
{
    public sealed class ConfigGroupDefinition
    {
        public string Id { get; }

        public string AssetId { get; }

        public Type ConfigType { get; }

        public Func<object, string> IdSelector { get; }

        internal ConfigGroupDefinition(
            string id,
            string assetId,
            Type configType,
            Func<object, string> idSelector)
        {
            Id = id;
            AssetId = assetId;
            ConfigType = configType;
            IdSelector = idSelector;
        }
    }
}
