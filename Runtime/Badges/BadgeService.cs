using System;
using System.Collections.Generic;
using Foundation.Reactive;

namespace Foundation.Badges
{
    public interface IBadgeSource
    {
        BadgeId Id { get; }
        BadgeValue Value { get; }
        event Action Changed;
    }

    public interface IAcknowledgeableBadgeSource : IBadgeSource
    {
        void Acknowledge();
    }

    public interface IBadgeService
    {
        BadgeValue Get(BadgeId id);
        IReadOnlyReactiveValue<BadgeValue> Observe(BadgeId id);
        void RegisterSource(IBadgeSource source);
        void UnregisterSource(IBadgeSource source);
        bool Acknowledge(BadgeId id);
    }

    public sealed class BadgeService : IBadgeService, IDisposable
    {
        private readonly Dictionary<BadgeId, Node> _nodes = new();
        private bool _disposed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static BadgeService DebugCurrent { get; private set; }
#endif

        public BadgeService(BadgeCatalog catalog) : this(catalog?.Definitions ?? throw new ArgumentNullException(nameof(catalog))) { }

        public BadgeService(IReadOnlyList<BadgeDefinition> definitions)
        {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));

            foreach (var definition in definitions)
            {
                if (definition == null || definition.Id.IsEmpty)
                    throw new ArgumentException("Badge id is required.", nameof(definitions));
                if (!_nodes.TryAdd(definition.Id, new Node(definition)))
                    throw new ArgumentException($"Duplicate badge id: {definition.Id}.", nameof(definitions));
            }

            foreach (var node in _nodes.Values)
            {
                var parentId = node.Definition.Parent;
                if (parentId.IsEmpty)
                    continue;
                if (!_nodes.TryGetValue(parentId, out var parent))
                    throw new ArgumentException($"Missing parent {parentId} for {node.Definition.Id}.", nameof(definitions));
                node.Parent = parent;
                parent.Children.Add(node);
            }

            foreach (var node in _nodes.Values)
            {
                var seen = new HashSet<BadgeId>();
                for (var cursor = node; cursor != null; cursor = cursor.Parent)
                {
                    if (!seen.Add(cursor.Definition.Id))
                        throw new ArgumentException($"Badge graph contains a cycle at {cursor.Definition.Id}.", nameof(definitions));
                }
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugCurrent = this;
#endif
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (ReferenceEquals(DebugCurrent, this))
                DebugCurrent = null;
#endif
            foreach (var node in _nodes.Values)
            {
                if (node.Source != null)
                    node.Source.Changed -= node.OnSourceChanged;
                node.Value.Dispose();
            }
            _nodes.Clear();
        }

        public BadgeValue Get(BadgeId id) => RequireNode(id).Value.Value;
        public IReadOnlyReactiveValue<BadgeValue> Observe(BadgeId id) => RequireNode(id).Value;

        public void RegisterSource(IBadgeSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var node = RequireNode(source.Id);
            if (node.Source != null)
                throw new InvalidOperationException($"Badge {source.Id} already has a source.");

            node.Source = source;
            node.OnSourceChanged = () => UpdatePath(node);
            source.Changed += node.OnSourceChanged;
            UpdatePath(node);
        }

        public void UnregisterSource(IBadgeSource source)
        {
            if (source == null)
                return;
            var node = RequireNode(source.Id);
            if (!ReferenceEquals(node.Source, source))
                return;
            source.Changed -= node.OnSourceChanged;
            node.Source = null;
            node.OnSourceChanged = null;
            UpdatePath(node);
        }

        public bool Acknowledge(BadgeId id)
        {
            if (RequireNode(id).Source is not IAcknowledgeableBadgeSource source)
                return false;
            source.Acknowledge();
            UpdatePath(RequireNode(id));
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string GetSourceName(BadgeId id) => RequireNode(id).Source?.GetType().Name ?? string.Empty;

        public void Force(BadgeId id, BadgeValue? value)
        {
            var node = RequireNode(id);
            node.Forced = value;
            UpdatePath(node);
        }
#endif

        private Node RequireNode(BadgeId id)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BadgeService));
            if (id.IsEmpty || !_nodes.TryGetValue(id, out var node))
                throw new ArgumentException($"Unknown badge id: {id}.", nameof(id));
            return node;
        }

        private static void UpdatePath(Node node)
        {
            for (var cursor = node; cursor != null; cursor = cursor.Parent)
            {
                var next = Compute(cursor);
                if (cursor.Value.Value == next)
                    break;
                cursor.Value.Value = next;
            }
        }

        private static BadgeValue Compute(Node node)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (node.Forced.HasValue)
                return node.Forced.Value;
#endif
            var own = node.Source?.Value ?? BadgeValue.None;
            if (node.Definition.Aggregation == BadgeAggregation.None)
                return own;

            var result = own.IsActive ? (own.Kind == BadgeKind.Count ? own.Count : 1) : 0;
            foreach (var child in node.Children)
            {
                var value = child.Value.Value;
                if (!value.IsActive)
                    continue;
                var count = value.Kind == BadgeKind.Count ? value.Count : 1;
                switch (node.Definition.Aggregation)
                {
                    case BadgeAggregation.Any:
                        return BadgeValue.Dot;
                    case BadgeAggregation.Sum:
                        result = (int)Math.Min(int.MaxValue, (long)result + count);
                        break;
                    case BadgeAggregation.Max:
                        result = Math.Max(result, count);
                        break;
                }
            }

            if (node.Definition.Aggregation == BadgeAggregation.Any)
                return own.IsActive ? BadgeValue.Dot : BadgeValue.None;
            return BadgeValue.FromCount(result);
        }

        private sealed class Node
        {
            public Node Parent;
            public IBadgeSource Source;
            public Action OnSourceChanged;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public BadgeValue? Forced;
#endif
            public readonly BadgeDefinition Definition;
            public readonly List<Node> Children = new();
            public readonly ReactiveValue<BadgeValue> Value = new(BadgeValue.None);

            public Node(BadgeDefinition definition) => Definition = definition;
        }
    }
}
