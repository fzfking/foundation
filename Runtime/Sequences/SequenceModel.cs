using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Sequences
{
    public enum SequenceNodeKind
    {
        Command,
        If,
        Race,
    }

    public sealed class SequenceNode
    {
        public SequenceNodeKind Kind;
        public string Command;
        public string[] Arguments = Array.Empty<string>();
        public bool Negated;
        public SequenceNode[] Children;
        public SequenceNode[] ElseChildren;
        public int Line;
        public IReadOnlyDictionary<string, string> Options;

        public string GetOption(string name, string fallback = null)
            => Options != null && Options.TryGetValue(name, out var value) ? value : fallback;

        public static SequenceNode Action(string key, params string[] arguments)
        {
            var values = new string[arguments.Length + 1];
            values[0] = key;
            Array.Copy(arguments, 0, values, 1, arguments.Length);
            return new SequenceNode { Command = "action", Arguments = values };
        }
    }

    public readonly struct SequenceContext
    {
        public string SequenceId { get; }
        public string StepId { get; }
        public int StepIndex { get; }
        public SequenceNode Node { get; }
        public IReadOnlyList<string> Arguments { get; }

        public int Line => Node.Line;

        public SequenceContext(string sequenceId, string stepId, int stepIndex, SequenceNode node, int firstArgument = 0)
        {
            SequenceId = sequenceId;
            StepId = stepId;
            StepIndex = stepIndex;
            Node = node;
            var arguments = node.Arguments ?? Array.Empty<string>();
            var rest = new string[Math.Max(0, arguments.Length - firstArgument)];
            Array.Copy(arguments, firstArgument, rest, 0, rest.Length);
            Arguments = rest;
        }
    }

    public interface ISequenceCommandHandler
    {
        UniTask ExecuteAsync(SequenceContext context, CancellationToken cancellationToken);
    }

    public interface ISequenceHandlerProvider
    {
        void Register(SequenceHandlerRegistry registry);
    }

    /// <summary>Normal early exit of a step; cleanup still runs.</summary>
    public sealed class SequenceStepExitException : Exception
    {
    }
}
