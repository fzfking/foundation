using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Content;
using Foundation.DI;
using Foundation.Logging;
using Foundation.Motion;
using Cysharp.Threading.Tasks;
using Foundation.Reactive;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Foundation.UI.UGui
{
    [AddComponentMenu("UI (Canvas)/Foundation Button")]
    public sealed class FoundationButton : Button, IServiceResolverClient
    {
        [SerializeField] private string initialState = "normal";

        [SerializeField] private FoundationButtonStateDefinition[] states = Array.Empty<FoundationButtonStateDefinition>();

        [SerializeField] private LocalizedText localizedLabel;

        [SerializeField, Min(0.1f)]
        private float longPressSeconds = 0.5f;

        private readonly Dictionary<string, FoundationButtonStateDefinition> _statesById = new(StringComparer.Ordinal);
        // Replaced, never changed in place, so a click can run the handlers it started with without copying them
        // while a handler subscribes or unsubscribes.
        private AsyncHandler[] _asyncHandlers = Array.Empty<AsyncHandler>();
        private readonly EventStream<FoundationButtonClick> _clicked = new();
        private readonly EventStream<FoundationButtonSignal> _signals = new();
        private readonly ReactiveValue<string> _state = new(string.Empty);
        private readonly ReactiveValue<bool> _busy = new(false);
        private CancellationTokenSource _clickCancellation;
        private FoundationButtonStateDefinition _currentState;
        private FoundationButtonStrategyContext _strategyContext;
        private int _stateVersion;
        private int _pressVersion;
        private IServiceResolver _resolver;
        private IAppLogger _logger;

        public IEventStream<FoundationButtonClick> Clicked => _clicked;

        public IEventStream<FoundationButtonSignal> Signals => _signals;

        public IReadOnlyReactiveValue<string> State => _state;

        public IReadOnlyReactiveValue<bool> Busy => _busy;

        public string CurrentStateId => _state.Value;

        public FoundationButtonStrategyContext StrategyContext => _strategyContext;

        protected override void Awake()
        {
            transition = Selectable.Transition.None;
            base.Awake();
            _clickCancellation = new();
            _strategyContext = new(this);
            BuildStateIndex();

            if (_statesById.Count > 0 && string.IsNullOrWhiteSpace(initialState) && states != null)
                initialState = Array.Find(states, state => state != null)?.Id;
            if (_statesById.Count > 0 && !string.IsNullOrWhiteSpace(initialState))
                SetState(initialState);

            ApplyResolverIfReady();
        }

        protected override void OnEnable()
        {
            transition = Selectable.Transition.None;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            CancelLongPress();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            _clickCancellation?.Cancel();
            _clickCancellation?.Dispose();
            _clickCancellation = null;
            CancelLongPress();
            _strategyContext?.Dispose();
            _strategyContext = null;
            _clicked.Dispose();
            _signals.Dispose();
            _state.Dispose();
            _busy.Dispose();
            base.OnDestroy();
        }

        public bool HasState(string stateId)
            => !string.IsNullOrWhiteSpace(stateId) && _statesById.ContainsKey(stateId);

        public void Bind(IServiceResolver resolver)
        {
            // A component instantiated inactive (a screen prefab the router keeps hidden) gets Awake only on
            // first activation, while the injector calls Bind right after instantiation. Bind can run before
            // Awake, when _strategyContext/_currentState are still null, dropping the services with nothing
            // to re-apply them. Caching the resolver and retrying from Awake closes the gap in either order.
            _resolver = resolver;
            _logger = resolver.TryResolve(out IAppLogger logger) ? logger : null;
            ApplyResolverIfReady();
        }

        public bool SetState(string stateId)
        {
            if (string.IsNullOrWhiteSpace(stateId))
                throw new ArgumentException("Button state id is required.", nameof(stateId));
            if (!_statesById.TryGetValue(stateId, out var nextState))
            {
                ReportError($"has no state '{stateId}'.");
                return false;
            }
            if (string.Equals(_state.Value, stateId, StringComparison.Ordinal))
                return true;

            _currentState?.Exited?.Invoke();
            _currentState = nextState;
            _state.Value = stateId;
            ApplyState(_currentState);
            _currentState.Entered?.Invoke();
            _strategyContext.ClearStateAssets();
            ApplyStateStrategiesAsync(_currentState, ++_stateVersion).Forget();
            return true;
        }

        public IDisposable SubscribeAsync(
            Func<FoundationButtonClick, CancellationToken, UniTask> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return AddAsyncHandler(new AsyncHandler(null, handler));
        }

        public IDisposable Subscribe(
            string stateId,
            Action<FoundationButtonClick> handler)
        {
            RequireStateId(stateId);
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return _clicked.Subscribe(click =>
            {
                if (string.Equals(click.StateId, stateId, StringComparison.Ordinal))
                    handler(click);
            });
        }

        public IDisposable SubscribeAsync(
            string stateId,
            Func<FoundationButtonClick, CancellationToken, UniTask> handler)
        {
            RequireStateId(stateId);
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return AddAsyncHandler(new AsyncHandler(stateId, handler));
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (eventData is not { button: PointerEventData.InputButton.Left })
                return;
            
            if (!TryBeginClick())
            {
                Emit(FoundationButtonSignalKind.Invalid);
                return;
            }

            base.OnPointerClick(eventData);
            PublishClick();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left || !IsInteractable())
                return;
            Emit(FoundationButtonSignalKind.PointerDown);
            CancelLongPress();
            PublishLongPressAsync(_pressVersion).Forget();
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            if (eventData is not { button: PointerEventData.InputButton.Left })
                return;

            CancelLongPress();
            Emit(FoundationButtonSignalKind.PointerUp);
        }

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            Emit(FoundationButtonSignalKind.Selected);
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            base.OnDeselect(eventData);
            Emit(FoundationButtonSignalKind.Deselected);
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            if (!TryBeginClick())
            {
                Emit(FoundationButtonSignalKind.Invalid);
                return;
            }

            base.OnSubmit(eventData);
            PublishClick();
        }

        private void ApplyResolverIfReady()
        {
            if (_resolver == null || _strategyContext == null)
                return;

            _resolver.TryResolve(out IAssetProvider assets);
            _strategyContext.SetAssets(assets);
            _resolver.TryResolve(out IMotionService motion);
            _strategyContext.SetMotion(motion);
            if (_currentState != null)
                ApplyStateStrategiesAsync(_currentState, ++_stateVersion).Forget();
        }

        private void BuildStateIndex()
        {
            _statesById.Clear();
            if (states == null)
                return;

            foreach (var state in states)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.Id))
                    continue;
                
                if (!_statesById.TryAdd(state.Id, state))
                    Debug.LogError($"FoundationButton '{name}' has duplicate state '{state.Id}'.", this);
            }
        }

        private void ApplyState(FoundationButtonStateDefinition state)
        {
            base.interactable = (state == null || state.Interactable) && !_busy.Value;
            if (localizedLabel == null || state == null)
                return;

            if (!string.IsNullOrWhiteSpace(state.LocalizationKey))
                localizedLabel.SetKey(state.LocalizationKey);
            localizedLabel.SetArguments(state.LocalizationArguments);
        }

        private bool TryBeginClick()
        {
            if (!isActiveAndEnabled || _busy.Value || !IsInteractable())
                return false;

            _busy.Value = true;
            return true;
        }

        private void PublishClick()
        {
            ApplyState(_currentState);
            var click = new FoundationButtonClick(this, _state.Value);
            var handlers = _asyncHandlers;
            Emit(FoundationButtonSignalKind.Click);
            _clicked.Publish(click);
            ExecuteClickAsync(click, handlers).Forget();
        }

        private async UniTask ExecuteClickAsync(FoundationButtonClick click, AsyncHandler[] handlers)
        {
            try
            {
                List<UniTask> tasks = null;
                Exception failure = null;
                OperationCanceledException cancellation = null;
                foreach (var subscription in handlers)
                {
                    if (subscription.StateId != null
                        && !string.Equals(subscription.StateId, click.StateId, StringComparison.Ordinal))
                        continue;

                    try
                    {
                        (tasks ??= new List<UniTask>()).Add(subscription.Handler(click, _clickCancellation.Token));
                    }
                    catch (OperationCanceledException exception)
                    {
                        cancellation ??= exception;
                    }
                    catch (Exception exception)
                    {
                        failure ??= exception;
                    }
                }

                if (tasks != null)
                {
                    foreach (var task in tasks)
                    {
                        try
                        {
                            await task;
                        }
                        catch (OperationCanceledException exception)
                        {
                            cancellation ??= exception;
                        }
                        catch (Exception exception)
                        {
                            failure ??= exception;
                        }
                    }
                }

                if (failure != null)
                    throw failure;
                if (cancellation != null)
                    throw cancellation;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                ReportException(exception);
            }
            finally
            {
                if (this != null)
                {
                    _busy.Value = false;
                    ApplyState(_currentState);
                }
            }
        }

        private async UniTask ApplyStateStrategiesAsync(FoundationButtonStateDefinition state, int version)
        {
            if (state?.Strategies == null)
                return;

            foreach (var strategy in state.Strategies)
            {
                if (!strategy.TryGetValue(out var value))
                    continue;

                await value.ApplyAsync(_strategyContext, state.Id, _clickCancellation.Token);
                if (version == _stateVersion) 
                    continue;
                
                _strategyContext.ClearStateAssets();
                return;
            }
        }

        // Without a container there is no logger, so the Unity console (with this button as the context) takes the message.
        private void ReportError(string message)
        {
            if (_logger != null)
                _logger.Log(LogLevel.Error, "UI", $"FoundationButton '{name}' {message}");
            else
                Debug.LogError($"FoundationButton '{name}' {message}", this);
        }

        private void ReportException(Exception exception)
        {
            if (_logger != null)
                _logger.Log(LogLevel.Error, "UI", $"FoundationButton '{name}' failed: {exception}");
            else
                Debug.LogException(exception, this);
        }

        private static void RequireStateId(string stateId)
        {
            if (string.IsNullOrWhiteSpace(stateId))
                throw new ArgumentException("Button state id is required.", nameof(stateId));
        }

        private void Emit(FoundationButtonSignalKind kind)
            => _signals.Publish(new FoundationButtonSignal(kind, _state.Value));

        // A press is identified by a counter instead of a cancellation source per pointer-down: a release only
        // invalidates the number, and the pending delay finds it stale when it ends.
        private async UniTask PublishLongPressAsync(int pressVersion)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(longPressSeconds), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, _clickCancellation.Token);
                if (pressVersion == _pressVersion && isActiveAndEnabled && IsInteractable())
                    Emit(FoundationButtonSignalKind.LongPress);
            }
            catch (OperationCanceledException) { }
        }

        private void CancelLongPress() => _pressVersion++;

        private IDisposable AddAsyncHandler(AsyncHandler handler)
        {
            var updated = new AsyncHandler[_asyncHandlers.Length + 1];
            Array.Copy(_asyncHandlers, updated, _asyncHandlers.Length);
            updated[_asyncHandlers.Length] = handler;
            _asyncHandlers = updated;
            return new FoundationButtonSubscription(this, handler);
        }

        private void RemoveAsyncHandler(AsyncHandler handler)
        {
            var current = _asyncHandlers;
            var position = Array.IndexOf(current, handler);
            if (position < 0)
                return;

            var updated = new AsyncHandler[current.Length - 1];
            Array.Copy(current, 0, updated, 0, position);
            Array.Copy(current, position + 1, updated, position, current.Length - position - 1);
            _asyncHandlers = updated;
        }

        internal sealed class AsyncHandler
        {
            public AsyncHandler(string stateId, Func<FoundationButtonClick, CancellationToken, UniTask> handler)
            {
                StateId = stateId;
                Handler = handler;
            }

            public string StateId { get; }

            public Func<FoundationButtonClick, CancellationToken, UniTask> Handler { get; }
        }

        internal void Unsubscribe(AsyncHandler handler) => RemoveAsyncHandler(handler);
    }
}
