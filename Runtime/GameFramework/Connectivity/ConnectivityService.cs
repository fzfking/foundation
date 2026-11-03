using Foundation.Logging;
using Foundation.Reactive;
using System;
using System.Threading;
using Foundation.Scheduling;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Foundation.GameFramework.Connectivity
{
    public enum ConnectivityState
    {
        Unknown,
        Offline,
        Online
    }

    public interface IConnectivityService
    {
        ConnectivityState State { get; }
        IReadOnlyReactiveValue<ConnectivityState> StateChanges { get; }
    }

    public sealed class UnityConnectivityService : IConnectivityService, IDisposable
    {
        // A check that never answers would hold the in-flight flag and end every later check.
        private const int HealthCheckTimeoutSeconds = 10;

        private readonly ReactiveValue<ConnectivityState> _state = new(ConnectivityState.Unknown);
        private readonly IReadOnlyReactiveValue<ConnectivityState> _stateChanges;
        private readonly string _healthCheckUrl;
        private readonly IAppLogger _logger;
        private readonly IScheduledHandle _reachabilityHandle;
        private readonly IScheduledHandle _healthCheckHandle;
        private readonly CancellationTokenSource _lifetime = new();
        private bool _hasNetworkInterface;
        private bool? _healthCheckOk;
        private bool _healthCheckInFlight;
        private bool _disposed;

        public ConnectivityState State => _state.Value;

        public IReadOnlyReactiveValue<ConnectivityState> StateChanges => _stateChanges;

        public UnityConnectivityService(
            IScheduler scheduler,
            string healthCheckUrl = null,
            float reachabilityPollIntervalSeconds = 5f,
            float healthCheckIntervalSeconds = 45f,
            IAppLogger logger = null)
        {
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            _healthCheckUrl = healthCheckUrl;
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
            _stateChanges = new DisposedAwareStateChanges(this);

            PollReachability();
            _reachabilityHandle = scheduler.ScheduleRepeating(reachabilityPollIntervalSeconds, PollReachability);

            if (!string.IsNullOrEmpty(_healthCheckUrl))
                _healthCheckHandle = scheduler.ScheduleRepeating(healthCheckIntervalSeconds, () => RunHealthCheckAsync().Forget());
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _reachabilityHandle?.Dispose();
            _healthCheckHandle?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private void PollReachability()
        {
            _hasNetworkInterface = Application.internetReachability != NetworkReachability.NotReachable;
            Recompute();
        }

        private async UniTaskVoid RunHealthCheckAsync()
        {
            if (_healthCheckInFlight || _disposed)
                return;

            _healthCheckInFlight = true;
            try
            {
                using var request = UnityWebRequest.Get(_healthCheckUrl);
                request.timeout = HealthCheckTimeoutSeconds;
                await request.SendWebRequest().ToUniTask(cancellationToken: _lifetime.Token, cancelImmediately: true);
                if (_disposed)
                    return;
                _healthCheckOk = request.result == UnityWebRequest.Result.Success && request.responseCode == 200;
            }
            catch (OperationCanceledException) when (_disposed)
            {
                // Disposal canceled the request; its result is no longer wanted.
            }
            catch (Exception exception)
            {
                // A timeout or a lost network is an ordinary "offline" answer; only an exception here is a fault, so it is logged.
                _logger.Log(LogLevel.Warning, "Connectivity", $"health check failed: {exception.Message}");
                _healthCheckOk = false;
            }
            finally
            {
                _healthCheckInFlight = false;
                Recompute();
            }
        }

        private void Recompute()
        {
            if (_disposed)
                return;

            if (!_hasNetworkInterface)
            {
                _state.Value = ConnectivityState.Offline;
                return;
            }

            if (string.IsNullOrEmpty(_healthCheckUrl))
            {
                _state.Value = ConnectivityState.Online;
                return;
            }

            _state.Value = _healthCheckOk switch
            {
                null => ConnectivityState.Unknown,
                true => ConnectivityState.Online,
                false => ConnectivityState.Offline
            };
        }

        private sealed class DisposedAwareStateChanges : IReadOnlyReactiveValue<ConnectivityState>
        {
            private readonly UnityConnectivityService _owner;

            public DisposedAwareStateChanges(UnityConnectivityService owner) => _owner = owner;

            public ConnectivityState Value => _owner._state.Value;

            public IDisposable Subscribe(Action<ConnectivityState> onNext)
            {
                if (_owner._disposed)
                    throw new ObjectDisposedException(nameof(UnityConnectivityService));

                return _owner._state.Subscribe(onNext);
            }
        }
    }
}
