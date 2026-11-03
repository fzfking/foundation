using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Content;
using Foundation.Serialization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Config
{
    public sealed class SerializedConfigProvider : IConfigProvider
    {
        private readonly IAssetProvider _assetProvider;
        private readonly ISerializer _serializer;
        private readonly IConfigRepository _repository;
        private readonly IConfigGroupRegistry _groupRegistry;
        private readonly CancellationTokenSource _shutdown = new();

        private readonly object _gate = new();
        private readonly Dictionary<string, UniTask> _pending = new(StringComparer.Ordinal);
        private readonly HashSet<string> _loaded = new(StringComparer.Ordinal);
        private bool _shutdownRequested;

        public SerializedConfigProvider(
            IAssetProvider assetProvider,
            ISerializer serializer,
            IConfigRepository repository,
            IConfigGroupRegistry groupRegistry)
        {
            _assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _groupRegistry = groupRegistry ?? throw new ArgumentNullException(nameof(groupRegistry));
        }

        public void Shutdown()
        {
            lock (_gate)
            {
                if (_shutdownRequested)
                    return;

                _shutdownRequested = true;
            }

            _shutdown.Cancel();
        }

        public UniTask EnsureAsync<TConfig>(CancellationToken cancellationToken = default)
        {
            var group = _groupRegistry.GetGroup(typeof(TConfig));
            return EnsureGroupAsync<TConfig>(group, cancellationToken);
        }

        private UniTask EnsureGroupAsync<TConfig>(ConfigGroupDefinition group, CancellationToken waiterToken)
        {
            lock (_gate)
            {
                if (_shutdownRequested)
                    return UniTask.FromCanceled(new CancellationToken(true));

                if (_loaded.Contains(group.Id))
                    return UniTask.CompletedTask;

                if (_pending.TryGetValue(group.Id, out var shared))
                    return shared.AttachExternalCancellation(waiterToken);

                var completion = new UniTaskCompletionSource();
                _pending[group.Id] = completion.Task;
                _ = LoadGroupAsync<TConfig>(group, completion);
                return completion.Task.AttachExternalCancellation(waiterToken);
            }
        }

        private async UniTask LoadGroupAsync<TConfig>(
            ConfigGroupDefinition group,
            UniTaskCompletionSource completion)
        {
            try
            {
                await LoadGroupInternalAsync<TConfig>(group, _shutdown.Token);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                lock (_gate)
                    _pending.Remove(group.Id);
            }
        }

        private async UniTask LoadGroupInternalAsync<TConfig>(
            ConfigGroupDefinition group,
            CancellationToken cancellationToken)
        {
            IAssetHandle<TextAsset> handle = null;

            try
            {
                // shortcut: a provider that ignores cancellation can keep this load pending; add an adapter timeout if it can stall.
                handle = await _assetProvider.LoadAssetAsync<TextAsset>(group.AssetId, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var array = DeserializeGroup(group, handle.Asset.bytes);

                var entries = BuildEntries(group, array);
                lock (_gate)
                {
                    if (_shutdownRequested)
                        throw new OperationCanceledException(_shutdown.Token);

                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var (id, _) in entries)
                    {
                        if (_repository.Contains<TConfig>(id))
                            throw new InvalidOperationException(
                                $"Config '{id}' of type '{typeof(TConfig).Name}' is already registered; group '{group.Id}' was not loaded.");
                    }

                    foreach (var (id, config) in entries)
                        _repository.Register(id, (TConfig)config);

                    _loaded.Add(group.Id);
                }
            }
            finally
            {
                handle?.Dispose();
            }
        }

        private Array DeserializeGroup(ConfigGroupDefinition group, byte[] bytes)
        {
            var arrayType = group.ConfigType.MakeArrayType();

            object deserialized;
            try
            {
                deserialized = _serializer.Deserialize(arrayType, bytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize config group '{group.Id}' as {group.ConfigType.Name}[].", ex);
            }

            if (deserialized is not Array array)
            {
                throw new InvalidOperationException(
                    $"Deserialization of config group '{group.Id}' returned "
                    + $"'{deserialized?.GetType().Name ?? "null"}' instead of the expected {group.ConfigType.Name}[].");
            }

            return array;
        }

        private static List<(string Id, object Config)> BuildEntries(
            ConfigGroupDefinition group,
            Array array)
        {
            var entries = new List<(string, object)>(array.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var config in array)
            {
                var id = group.IdSelector(config);

                if (string.IsNullOrEmpty(id))
                    throw new InvalidOperationException(
                        $"Config group '{group.Id}' contains an entry with an empty id.");

                if (!seen.Add(id))
                    throw new InvalidOperationException(
                        $"Config group '{group.Id}' contains a duplicate id '{id}'.");

                entries.Add((id, config));
            }

            return entries;
        }
    }
}
