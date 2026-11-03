using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using System;

namespace Foundation.Config
{
    public interface IConfigResolver
    {
        UniTask<TConfig> ResolveAsync<TConfig>(
            string configId,
            CancellationToken cancellationToken = default);

        UniTask<IReadOnlyList<TConfig>> ResolveAllAsync<TConfig>(
            CancellationToken cancellationToken = default);
    }

    public sealed class ConfigResolver : IConfigResolver
    {
        private readonly IConfigProvider _provider;
        private readonly IConfigRepository _repository;

        public ConfigResolver(IConfigProvider provider, IConfigRepository repository)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async UniTask<TConfig> ResolveAsync<TConfig>(
            string configId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _provider.EnsureAsync<TConfig>(cancellationToken);
            return _repository.Get<TConfig>(configId);
        }

        public async UniTask<IReadOnlyList<TConfig>> ResolveAllAsync<TConfig>(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _provider.EnsureAsync<TConfig>(cancellationToken);
            return _repository.GetAll<TConfig>();
        }
    }
}
