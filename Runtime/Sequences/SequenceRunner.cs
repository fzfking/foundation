using System;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Sequences
{
    /// <summary>Stateless linear async execution. Presentation and event subscriptions belong to handlers.</summary>
    public sealed class SequenceRunner
    {
        private readonly SequenceHandlerRegistry _handlers;
        private readonly ISequenceCommandHandler _commands;

        public SequenceRunner(SequenceHandlerRegistry handlers, ISequenceCommandHandler commands = null)
        {
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
            _commands = commands;
        }

        public async UniTask RunStepAsync(string sequenceId, string stepId, int stepIndex,
            SequenceNode[] body, SequenceNode[] cleanup = null, CancellationToken cancellationToken = default)
        {
            Exception bodyFailure = null;
            try
            {
                await RunNodesAsync(sequenceId, stepId, stepIndex, body, cancellationToken);
            }
            catch (SequenceStepExitException)
            {
            }
            catch (Exception exception)
            {
                bodyFailure = exception;
            }

            try
            {
                await RunNodesAsync(sequenceId, stepId, stepIndex, cleanup, CancellationToken.None);
            }
            catch (SequenceStepExitException)
            {
            }
            catch (Exception cleanupFailure)
            {
                if (bodyFailure == null)
                    throw;

                throw new AggregateException("The sequence step body and cleanup both failed.", bodyFailure, cleanupFailure);
            }

            if (bodyFailure != null)
                ExceptionDispatchInfo.Capture(bodyFailure).Throw();
        }

        private async UniTask RunNodesAsync(string sequenceId, string stepId, int stepIndex, SequenceNode[] nodes, CancellationToken token)
        {
            if (nodes == null)
                return;
            foreach (var node in nodes)
            {
                token.ThrowIfCancellationRequested();
                await RunNodeAsync(sequenceId, stepId, stepIndex, node, token);
            }
        }

        private async UniTask RunNodeAsync(string sequenceId, string stepId, int stepIndex, SequenceNode node, CancellationToken token)
        {
            SequenceContext Context(int offset = 0) => new(sequenceId, stepId, stepIndex, node, offset);
            if (node.Kind == SequenceNodeKind.If)
            {
                var result = _handlers.Evaluate(node.Command, Context()) != node.Negated;
                await RunNodesAsync(sequenceId, stepId, stepIndex, result ? node.Children : node.ElseChildren, token);
                return;
            }
            if (node.Kind == SequenceNodeKind.Race)
            {
                await RunRaceAsync(sequenceId, stepId, stepIndex, node.Children, token);
                return;
            }

            var arguments = node.Arguments ?? Array.Empty<string>();
            bool Condition()
            {
                var negated = arguments.Length > 0 && arguments[0] == "not";
                var offset = negated ? 1 : 0;
                return _handlers.Evaluate(arguments[offset], Context(offset + 1)) != negated;
            }

            switch (node.Command)
            {
                case "action":
                    await _handlers.ExecuteAsync(arguments[0], Context(1), token);
                    break;
                case "wait":
                    await _handlers.WaitAsync(arguments[0], Context(1), token);
                    break;
                case "wait_until":
                    await UniTask.WaitUntil(Condition, PlayerLoopTiming.Update, token);
                    break;
                case "skip_if":
                    if (Condition())
                        throw new SequenceStepExitException();
                    break;
                case "delay":
                    var seconds = double.Parse(arguments[0], CultureInfo.InvariantCulture);
                    if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                        throw new InvalidOperationException($"Invalid sequence delay (line {node.Line}).");
                    await UniTask.Delay(TimeSpan.FromSeconds(seconds), ignoreTimeScale: true, cancellationToken: token);
                    break;
                default:
                    if (_commands == null)
                        throw new InvalidOperationException($"Unknown sequence command '{node.Command}' (line {node.Line}).");
                    await _commands.ExecuteAsync(Context(), token);
                    break;
            }
        }

        private async UniTask RunRaceAsync(string sequenceId, string stepId, int stepIndex, SequenceNode[] nodes, CancellationToken token)
        {
            if (nodes == null || nodes.Length == 0)
                throw new InvalidOperationException("A sequence race requires a branch.");
            using var race = CancellationTokenSource.CreateLinkedTokenSource(token);
            var branches = new UniTask[nodes.Length];
            for (var index = 0; index < branches.Length; index++)
                branches[index] = RunBranchAsync(sequenceId, stepId, stepIndex, nodes[index], race.Token).Preserve();
            try
            {
                await UniTask.WhenAny(branches);
            }
            finally
            {
                race.Cancel();
                // Observe and drain every branch before step cleanup can release its resources.
                foreach (var branch in branches)
                {
                    try
                    {
                        await branch;
                    }
                    catch (Exception)
                    {
                        // WhenAny already propagated the winning branch's failure.
                    }
                }
            }
            token.ThrowIfCancellationRequested();
        }

        private async UniTask RunBranchAsync(string sequenceId, string stepId, int stepIndex, SequenceNode node, CancellationToken token)
        {
            try
            {
                await RunNodeAsync(sequenceId, stepId, stepIndex, node, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }
    }
}
