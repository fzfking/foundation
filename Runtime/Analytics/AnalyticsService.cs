using System;
using System.Threading;
using Foundation.Lifecycle;
using Foundation.Messaging;
using Cysharp.Threading.Tasks;

namespace Foundation.Analytics
{
    /// <summary>
    /// Forwards every <see cref="IAnalyticsEvent"/> published on the event bus to the provider. Register it as
    /// an entry point so it subscribes when the scope is built, though nothing depends on it directly.
    /// </summary>
    public sealed class AnalyticsService : IStartableEntryPoint, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly IAnalyticsProvider _provider;
        private IDisposable _subscription;

        public AnalyticsService(IEventBus eventBus, IAnalyticsProvider provider)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public void Start()
        {
            _subscription ??= _eventBus.Subscribe(new AnalyticsEventHandler(this));
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        public UniTask TrackAsync(
            IAnalyticsEvent analyticsEvent,
            CancellationToken cancellationToken = default)
        {
            if (analyticsEvent == null)
                throw new ArgumentNullException(nameof(analyticsEvent));

            return _provider.TrackAsync(analyticsEvent, cancellationToken);
        }

        private sealed class AnalyticsEventHandler : IEventHandler<IAnalyticsEvent>
        {
            private readonly AnalyticsService _service;

            public AnalyticsEventHandler(AnalyticsService service)
            {
                _service = service;
            }

            public UniTask HandleAsync(
                IAnalyticsEvent message,
                CancellationToken cancellationToken = default)
            {
                return _service.TrackAsync(message, cancellationToken);
            }
        }
    }
}
