#if UNITY_EDITOR || FOUNDATION_DEVELOPMENT
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameContexts
{
    /// <summary>
    /// Dev-facing entry points for what is awkward to do by hand in a running build: which context and save
    /// namespace is active, re-enter it, re-enter it from a wiped save. Wire it into the project's console or
    /// menu.
    /// <see cref="Current"/> exists because editor menu items, console commands and debug buttons have no
    /// constructor to inject into. Set by the application scope; development tooling only.
    /// </summary>
    public sealed class GameContextDebugService
    {
        private readonly IGameContextController _controller;
        private readonly IGameContextCatalog _catalog;
        private readonly IGameContextSelectionStore _selectionStore;

        public GameContextDebugService(
            IGameContextController controller,
            IGameContextCatalog catalog,
            IGameContextSelectionStore selectionStore)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
            Current = this;
        }

        public static GameContextDebugService Current { get; private set; }

        public GameContextId ActiveContextId => _controller.HasActiveContext ? _controller.ActiveContext.Id : default;

        public IReadOnlyList<GameContextDefinition> AvailableContexts => _catalog.Definitions;

        /// <summary>One block of text answering "which context, which namespace, what else could I enter".</summary>
        public string DescribeActive()
        {
            var builder = new StringBuilder();
            var active = _controller.ActiveContext;

            if (active == null || active.IsDisposed)
            {
                builder.AppendLine("Active game context: <none>");
            }
            else
            {
                builder.AppendLine($"Active game context: {active.Id} ({active.Definition.DisplayName})");
                builder.AppendLine($"  state:          {active.State}");
                builder.AppendLine($"  save namespace: {active.SaveNamespace}");
                builder.AppendLine($"  save slot:      {active.SaveSlotId}");
                builder.AppendLine($"  save version:   {active.Definition.Save.SaveVersion}");
            }

            builder.AppendLine($"Persisted selection: {(_selectionStore.TryLoadLastActive(out var persisted) ? persisted.Value : "<none>")}");
            builder.AppendLine($"Fallback context:    {_catalog.FallbackContextId}");
            builder.Append("Registered contexts: ");
            for (var index = 0; index < _catalog.Definitions.Count; index++)
            {
                if (index > 0)
                    builder.Append(", ");

                var definition = _catalog.Definitions[index];
                builder.Append($"{definition.Id} -> '{definition.Save.Namespace}'");
            }

            return builder.ToString();
        }

        public async UniTask EnterAsync(string contextId, CancellationToken cancellationToken = default)
            => await _controller.EnterAsync(new GameContextId(contextId), cancellationToken);

        public async UniTask ReloadActiveAsync(CancellationToken cancellationToken = default)
            => await _controller.ReloadActiveAsync(cancellationToken);

        public async UniTask ResetActiveAsync(CancellationToken cancellationToken = default)
            => await _controller.ResetActiveAsync(cancellationToken);

        /// <summary>Forgets the persisted selection, so the next startup lands on the fallback.</summary>
        public void ClearPersistedSelection() => _selectionStore.Clear();

        // For callers with nowhere to await: a console command, an editor menu item, a debug button.
        public void RequestEnter(string contextId) => EnterAsync(contextId).Forget();

        public void RequestReloadActive() => ReloadActiveAsync().Forget();

        public void RequestResetActive() => ResetActiveAsync().Forget();
    }
}
#endif
