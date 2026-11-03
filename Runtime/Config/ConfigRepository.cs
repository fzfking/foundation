using System;
using System.Collections.Generic;

namespace Foundation.Config
{
    public interface IConfigRepository
    {
        void Register<T>(string configId, T config);

        void Register(Type configType, string configId, object config);

        T Get<T>(string configId);

        IReadOnlyList<T> GetAll<T>();

        bool Contains<T>(string configId);
    }

    public sealed class ConfigRepository : IConfigRepository
    {
        private readonly Dictionary<Type, Dictionary<string, object>> _store = new();

        public void Register<T>(string configId, T config)
        {
            Register(typeof(T), configId, config);
        }

        public void Register(Type configType, string configId, object config)
        {
            if (configType == null)
                throw new ArgumentNullException(nameof(configType));

            if (string.IsNullOrEmpty(configId))
                throw new ArgumentNullException(nameof(configId));

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (!_store.TryGetValue(configType, out var configs))
            {
                configs = new Dictionary<string, object>(StringComparer.Ordinal);
                _store.Add(configType, configs);
            }

            if (!configs.TryAdd(configId, config))
                throw new InvalidOperationException(
                    $"Config '{configId}' of type '{configType.Name}' is already registered.");
        }

        public T Get<T>(string configId)
        {
            var type = typeof(T);
            if (!_store.TryGetValue(type, out var dict))
                throw new KeyNotFoundException($"No configs of type {type.Name} registered.");

            if (!dict.TryGetValue(configId, out var obj))
                throw new KeyNotFoundException($"Config '{configId}' of type {type.Name} not found.");

            return (T)obj;
        }

        public IReadOnlyList<T> GetAll<T>()
        {
            var type = typeof(T);
            if (!_store.TryGetValue(type, out var dict))
                return Array.Empty<T>();

            var result = new List<T>(dict.Count);
            foreach (var kvp in dict)
                result.Add((T)kvp.Value);

            return result;
        }

        public bool Contains<T>(string configId)
        {
            var type = typeof(T);
            return _store.TryGetValue(type, out var dict) && dict.ContainsKey(configId);
        }

        public int Count<T>()
        {
            var type = typeof(T);
            return _store.TryGetValue(type, out var dict) ? dict.Count : 0;
        }
    }
}
