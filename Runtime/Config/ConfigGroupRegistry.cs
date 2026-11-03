using System;
using System.Collections.Generic;

namespace Foundation.Config
{
    public interface IConfigGroupRegistry
    {
        void Register<TConfig>(string groupId, string assetId, Func<TConfig, string> idSelector);

        ConfigGroupDefinition GetGroup(Type configType);
    }

    public sealed class ConfigGroupRegistry : IConfigGroupRegistry
    {
        private readonly Dictionary<string, ConfigGroupDefinition> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<Type, ConfigGroupDefinition> _byType = new();

        public void Register<TConfig>(string groupId, string assetId, Func<TConfig, string> idSelector)
        {
            if (string.IsNullOrWhiteSpace(groupId))
                throw new ArgumentException("Group id must not be empty.", nameof(groupId));

            if (string.IsNullOrWhiteSpace(assetId))
                throw new ArgumentException("Asset id must not be empty.", nameof(assetId));

            if (idSelector == null)
                throw new ArgumentNullException(nameof(idSelector));

            var configType = typeof(TConfig);

            if (_byId.ContainsKey(groupId))
                throw new InvalidOperationException($"Config group '{groupId}' is already registered.");

            if (_byType.ContainsKey(configType))
                throw new InvalidOperationException(
                    $"Config group for type '{configType.Name}' is already registered.");

            var definition = new ConfigGroupDefinition(
                groupId,
                assetId,
                configType,
                config => idSelector((TConfig)config));

            _byId.Add(groupId, definition);
            _byType.Add(configType, definition);
        }

        public ConfigGroupDefinition GetGroup(Type configType)
        {
            if (configType == null)
                throw new ArgumentNullException(nameof(configType));

            if (_byType.TryGetValue(configType, out var definition))
                return definition;

            throw new InvalidOperationException(
                $"No config group registered for type '{configType.Name}'.");
        }
    }
}
