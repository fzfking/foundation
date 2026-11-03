using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Tutorial.Flow
{
    /// <summary>
    /// Declares the script key and parameters of an <see cref="ITutorialAction"/>,
    /// <see cref="ITutorialCondition"/> or <see cref="ITutorialWait"/>. A parameter name ending in <c>?</c> is
    /// optional. The same metadata drives build-time validation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TutorialCommandAttribute : Attribute
    {
        public string Key { get; }
        public string[] Parameters { get; }

        public TutorialCommandAttribute(string key, params string[] parameters)
        {
            Key = key;
            Parameters = parameters ?? Array.Empty<string>();
        }
    }

    public readonly struct TutorialCommandContext
    {
        public string ChapterId { get; }
        public string StepId { get; }
        public int StepIndex { get; }
        public IReadOnlyList<string> Arguments { get; }
        public int Line { get; }

        public TutorialCommandContext(string chapterId, string stepId, int stepIndex, IReadOnlyList<string> arguments, int line)
        {
            ChapterId = chapterId;
            StepId = stepId;
            StepIndex = stepIndex;
            Arguments = arguments ?? Array.Empty<string>();
            Line = line;
        }

        public string Argument(int index, string fallback = null)
            => index < Arguments.Count ? Arguments[index] : fallback;

        public int IntArgument(int index, int fallback = 0)
            => index < Arguments.Count
                && int.TryParse(Arguments[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        /// <summary>Reads <c>on</c>/<c>off</c>, <c>true</c>/<c>false</c>, <c>yes</c>/<c>no</c>.</summary>
        public bool FlagArgument(int index, bool fallback = false)
            => index < Arguments.Count ? TutorialArguments.ParseFlag(Arguments[index], fallback) : fallback;
    }

    /// <summary>A game action invoked by <c>action &lt;key&gt; args...</c>.</summary>
    public interface ITutorialAction
    {
        UniTask ExecuteAsync(TutorialCommandContext context, CancellationToken cancellationToken);
    }

    /// <summary>A game predicate used by <c>if</c>, <c>skip_if</c> and <c>wait_until</c>.</summary>
    public interface ITutorialCondition
    {
        bool Evaluate(TutorialCommandContext context);
    }

    /// <summary>A game event or state change awaited by <c>wait &lt;key&gt; args...</c>.</summary>
    public interface ITutorialWait
    {
        UniTask WaitAsync(TutorialCommandContext context, CancellationToken cancellationToken);
    }

    public enum TutorialCommandCategory
    {
        Action,
        Condition,
        Wait,
    }

    public sealed class TutorialCommandSignature
    {
        public TutorialCommandCategory Category { get; }
        public string Key { get; }
        public IReadOnlyList<string> Parameters { get; }
        public int RequiredCount { get; }

        public string Usage => Parameters.Count == 0 ? Key : Key + " " + string.Join(" ", Parameters);

        public TutorialCommandSignature(TutorialCommandCategory category, string key, IReadOnlyList<string> parameters)
        {
            Category = category;
            Key = key;
            Parameters = parameters;
            var required = 0;
            foreach (var parameter in parameters)
            {
                if (!parameter.EndsWith("?", StringComparison.Ordinal))
                    required++;
            }

            RequiredCount = required;
        }
    }

    /// <summary>
    /// The game commands a script may reference, by category and key. Built from command instances at runtime
    /// (<see cref="TutorialCommandRegistry"/>) or from command types at build time.
    /// </summary>
    public sealed class TutorialCommandCatalog
    {
        private readonly Dictionary<(TutorialCommandCategory, string), TutorialCommandSignature> _signatures = new();

        public IEnumerable<TutorialCommandSignature> Signatures => _signatures.Values;

        public static TutorialCommandCatalog FromTypes(IEnumerable<Type> types)
        {
            var catalog = new TutorialCommandCatalog();
            foreach (var type in types)
                catalog.TryAdd(type);
            return catalog;
        }

        public bool TryGet(TutorialCommandCategory category, string key, out TutorialCommandSignature signature)
            => _signatures.TryGetValue((category, key), out signature);

        public TutorialCommandSignature Add(Type type)
        {
            var signature = CreateSignature(type)
                ?? throw new InvalidOperationException(
                    $"Tutorial command '{type.FullName}' needs a [TutorialCommand] attribute and must implement one of the tutorial command interfaces.");
            AddSignature(signature);
            return signature;
        }

        private void TryAdd(Type type)
        {
            if (type.IsAbstract || type.IsInterface)
                return;

            var signature = CreateSignature(type);
            if (signature != null)
                AddSignature(signature);
        }

        // Dropping a repeated key here would let the build pass while the runtime registry fails at startup.
        private void AddSignature(TutorialCommandSignature signature)
        {
            if (!_signatures.TryAdd((signature.Category, signature.Key), signature))
                throw new InvalidOperationException($"Tutorial {signature.Category} '{signature.Key}' is declared more than once.");
        }

        private static TutorialCommandSignature CreateSignature(Type type)
        {
            var attribute = type.GetCustomAttribute<TutorialCommandAttribute>();
            if (attribute == null || string.IsNullOrWhiteSpace(attribute.Key))
                return null;

            TutorialCommandCategory category;
            if (typeof(ITutorialAction).IsAssignableFrom(type))
                category = TutorialCommandCategory.Action;
            else if (typeof(ITutorialCondition).IsAssignableFrom(type))
                category = TutorialCommandCategory.Condition;
            else if (typeof(ITutorialWait).IsAssignableFrom(type))
                category = TutorialCommandCategory.Wait;
            else
                return null;

            return new TutorialCommandSignature(category, attribute.Key, attribute.Parameters);
        }
    }

    public sealed class TutorialCommandRegistry
    {
        private readonly Dictionary<string, ITutorialAction> _actions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ITutorialCondition> _conditions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ITutorialWait> _waits = new(StringComparer.Ordinal);

        public TutorialCommandCatalog Catalog { get; }

        public TutorialCommandRegistry(
            IEnumerable<ITutorialAction> actions = null,
            IEnumerable<ITutorialCondition> conditions = null,
            IEnumerable<ITutorialWait> waits = null)
        {
            Catalog = new();
            Register(actions, _actions);
            Register(conditions, _conditions);
            Register(waits, _waits);
        }

        public ITutorialAction GetAction(string key) => Get(_actions, key, "action");
        public ITutorialCondition GetCondition(string key) => Get(_conditions, key, "condition");
        public ITutorialWait GetWait(string key) => Get(_waits, key, "wait");

        private void Register<T>(IEnumerable<T> commands, Dictionary<string, T> target)
        {
            if (commands == null)
                return;

            foreach (var command in commands)
            {
                if (command == null)
                    continue;

                var signature = Catalog.Add(command.GetType());
                target[signature.Key] = command;
            }
        }

        private static T Get<T>(Dictionary<string, T> commands, string key, string category)
            => commands.TryGetValue(key ?? string.Empty, out var command)
                ? command
                : throw new InvalidOperationException($"Tutorial {category} '{key}' is not registered.");
    }

    internal static class TutorialArguments
    {
        public static bool ParseFlag(string value, bool fallback)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "on":
                case "true":
                case "yes":
                case "1":
                    return true;
                case "off":
                case "false":
                case "no":
                case "0":
                    return false;
                default:
                    return fallback;
            }
        }
    }
}
