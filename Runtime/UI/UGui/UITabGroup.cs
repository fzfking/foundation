using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.UI.UGui
{
    public sealed class UITabGroup : MonoBehaviour, ITabGroup, ITabGroupTrySelect
    {
        [SerializeField] private UITabItem[] items;

        [SerializeField] private string initialTabId;

        /// <summary>What the selection triggers besides the buttons' own look, authored next to the group (a
        /// layout refresher, a content switch). More can be added from code.</summary>
        [SerializeField] private InterfaceReference<ITabSwitchHandler>[] switchHandlers;

        private readonly Dictionary<string, UITabItem> _itemsById = new();
        private readonly List<ITabSwitchHandler> _handlers = new();
        private CancellationTokenSource _destroyCancellation;
        private CancellationTokenSource _selectionCancellation;
        private string _selectedTabId;
        private bool _transitioning;

        public event Action<string> SelectedTabChanged;

        /// <summary>Raised once a tab is fully open: every switch handler has completed and the buttons have
        /// finished their animation (unlike <see cref="SelectedTabChanged"/>, which fires as the switch starts).
        /// The switch is already committed here: a subscriber's exception reaches the caller of the selection
        /// without rolling the selection or the handler content back.</summary>
        public event Action<string> SelectedTabSettled;

        public string SelectedTabId => _selectedTabId;

        public bool IsTransitioning => _transitioning;

        public IReadOnlyList<UITabItem> Items => items;

        private void Awake()
        {
            _destroyCancellation = new();
            if (switchHandlers != null)
            {
                foreach (var reference in switchHandlers)
                {
                    if (reference.TryGetValue(out var handler))
                        _handlers.Add(handler);
                }
            }

            if (items == null)
                return;

            foreach (var item in items)
            {
                if (item == null)
                    continue;
                if (string.IsNullOrWhiteSpace(item.TabId))
                    throw new InvalidOperationException($"Tab item '{item.name}' requires a tab id.");
                if (!_itemsById.TryAdd(item.TabId, item))
                    throw new InvalidOperationException($"Tab id '{item.TabId}' is registered more than once.");
                item.Clicked += HandleItemClicked;
            }
        }

        private void OnDestroy()
        {
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item != null)
                        item.Clicked -= HandleItemClicked;
                }
            }

            _selectionCancellation?.Cancel();
            _selectionCancellation?.Dispose();
            _destroyCancellation?.Cancel();
            _destroyCancellation?.Dispose();
            _selectionCancellation = null;
            _destroyCancellation = null;
        }

        public bool TryGetItem(string tabId, out UITabItem item) => _itemsById.TryGetValue(tabId, out item);

        public void AddSwitchHandler(ITabSwitchHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _handlers.Add(handler);
        }

        public void RemoveSwitchHandler(ITabSwitchHandler handler) => _handlers.Remove(handler);

        public UniTask SelectInitialAsync(CancellationToken cancellationToken = default)
        {
            var tabId = initialTabId;
            if (string.IsNullOrWhiteSpace(tabId))
                tabId = items != null && items.Length > 0 && items[0] != null ? items[0].TabId : null;
            if (string.IsNullOrWhiteSpace(tabId))
                throw new InvalidOperationException($"Tab group '{name}' does not contain an initial tab.");

            return SelectAsync(tabId, cancellationToken);
        }

        public UniTask SelectAsync(string tabId, CancellationToken cancellationToken = default)
            => TrySelectAsync(tabId, cancellationToken);

        /// <summary>Selects a tab and says so, unless another selection is still transitioning; then returns false
        /// at once without queueing the request.</summary>
        public async UniTask<bool> TrySelectAsync(string tabId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tabId))
                throw new ArgumentException("Tab id is required.", nameof(tabId));
            if (!_itemsById.TryGetValue(tabId, out var nextItem))
                throw new InvalidOperationException($"Tab id '{tabId}' is not registered in group '{name}'.");
            if (_transitioning)
                return false;
            if (string.Equals(_selectedTabId, tabId, StringComparison.Ordinal))
                return true;

            _transitioning = true;
            var previousTabId = _selectedTabId;
            var previousItem = string.IsNullOrWhiteSpace(previousTabId) ? null : _itemsById[previousTabId];
            UniTask selectionVisualTask = UniTask.CompletedTask;
            try
            {
                try
                {
                    _selectionCancellation?.Cancel();
                    _selectionCancellation?.Dispose();
                    _selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        _destroyCancellation.Token,
                        cancellationToken);
                    var selectionToken = _selectionCancellation.Token;
                    _selectedTabId = tabId;

                    // The selected visual of the tapped tab (and the deselect of the previous one) fires at once,
                    // in parallel with the screen slide, so the tap reads as registered immediately.
                    selectionVisualTask = UniTask.WhenAll(
                        previousItem != null ? previousItem.SetSelectedAsync(false, selectionToken) : UniTask.CompletedTask,
                        nextItem.SetSelectedAsync(true, selectionToken)).Preserve();

                    var tabSwitch = new TabSwitch(
                        previousItem != null ? previousItem.TabId : null,
                        tabId,
                        GetDirection(previousItem, nextItem),
                        selectionVisualTask);

                    SelectedTabChanged?.Invoke(tabId);

                    await RunHandlersAsync(tabSwitch, selectionToken);

                    await selectionVisualTask;
                }
                catch
                {
                    _selectionCancellation?.Cancel();
                    try
                    {
                        await selectionVisualTask;
                    }
                    catch (Exception)
                    {
                    }

                    _selectedTabId = previousTabId;
                    try
                    {
                        await UniTask.WhenAll(
                            previousItem != null
                                ? previousItem.SetSelectedAsync(true, CancellationToken.None)
                                : UniTask.CompletedTask,
                            nextItem.SetSelectedAsync(false, CancellationToken.None));
                    }
                    catch (Exception)
                    {
                        // Keep the original transition failure; the next selection can retry the handlers.
                    }

                    throw;
                }

                // Handlers and visuals have already committed this switch; a subscriber failure is reported to the
                // caller without undoing the selection or the content the handlers switched.
                SelectedTabSettled?.Invoke(tabId);
                return true;
            }
            finally
            {
                _transitioning = false;
            }
        }

        private async UniTask RunHandlersAsync(TabSwitch tabSwitch, CancellationToken cancellationToken)
        {
            var count = _handlers.Count;
            if (count == 0)
                return;
            var pendingHandlers = new List<UniTask>(count);
            Exception failure = null;
            OperationCanceledException cancellation = null;
            for (var index = 0; index < count; index++)
            {
                try
                {
                    pendingHandlers.Add(_handlers[index].HandleAsync(tabSwitch, cancellationToken));
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

            for (var index = 0; index < pendingHandlers.Count; index++)
            {
                try
                {
                    await pendingHandlers[index];
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

            if (failure != null)
                throw failure;
            if (cancellation != null)
                throw cancellation;
        }

        private void HandleItemClicked(UITabItem item)
        {
            if (_transitioning)
                return;

            SelectAsync(item.TabId).Forget();
        }

        // +1 when the next tab sits to the right of the previous one in the authored order (or there is none), -1 to
        // the left, so the incoming screen slides in from the side that was tapped.
        private int GetDirection(UITabItem previousItem, UITabItem nextItem)
        {
            if (previousItem == null || items == null)
                return 1;

            var previousIndex = Array.IndexOf(items, previousItem);
            var nextIndex = Array.IndexOf(items, nextItem);
            if (previousIndex < 0 || nextIndex < 0)
                return 1;

            return nextIndex >= previousIndex ? 1 : -1;
        }
    }
}
