using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Messaging;
using System;
using System.Collections.Generic;

namespace Foundation.Analytics
{
    public interface IAnalyticsProvider
    {
        UniTask TrackAsync(
            IAnalyticsEvent analyticsEvent,
            CancellationToken cancellationToken = default);
    }

    public sealed class CompositeAnalyticsProvider : IAnalyticsProvider
    {
        private readonly IReadOnlyList<IAnalyticsProviderBackend> _providers;

        public CompositeAnalyticsProvider(IEnumerable<IAnalyticsProviderBackend> providers)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            _providers = new List<IAnalyticsProviderBackend>(providers);
            if (_providers.Count == 0)
                throw new ArgumentException("At least one analytics provider is required.", nameof(providers));
        }

        public async UniTask TrackAsync(IAnalyticsEvent analyticsEvent, CancellationToken cancellationToken = default)
        {
            if (analyticsEvent == null)
                throw new ArgumentNullException(nameof(analyticsEvent));
            var tasks = new UniTask<Exception>[_providers.Count];
            for (var i = 0; i < _providers.Count; i++)
                tasks[i] = TrackProviderAsync(_providers[i], analyticsEvent, cancellationToken);

            var errors = await UniTask.WhenAll(tasks);
            var failures = new List<Exception>();
            foreach (var error in errors)
                if (error != null) 
                    failures.Add(error);

            if (failures.Count == _providers.Count)
                throw new AggregateException("All analytics providers failed.", failures);
        }

        private static async UniTask<Exception> TrackProviderAsync(
            IAnalyticsProviderBackend provider,
            IAnalyticsEvent analyticsEvent,
            CancellationToken cancellationToken)
        {
            try
            {
                await provider.TrackAsync(analyticsEvent, cancellationToken);
                return null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return exception;
            }
        }
    }
}
