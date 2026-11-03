using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Sequences;
using Foundation.UI;
using CoreLogger = Foundation.Logging.IAppLogger;
using LogLevel = Foundation.Logging.LogLevel;

namespace Foundation.Tutorial.Flow
{
    public readonly struct TutorialStepInfo
    {
        public string ChapterId { get; }
        public string StepId { get; }

        public int Index { get; }

        public TutorialStepInfo(string chapterId, string stepId, int index)
        {
            ChapterId = chapterId;
            StepId = stepId;
            Index = index;
        }
    }

    public interface ITutorialStepObserver
    {
        void StepStarted(TutorialStepInfo step);
        void StepCompleted(TutorialStepInfo step);
    }

    /// <summary>
    /// Executes a compiled step: built-in primitives drive the presenters and target registry, game keys go
    /// through <see cref="TutorialCommandRegistry"/>. Focus is cleared when a step ends; the last dialogue
    /// line stays so consecutive steps read as one conversation.
    /// </summary>
    public sealed class TutorialScriptRunner
    {
        private const string LogCategory = "Tutorial";
        private static readonly TimeSpan DefaultTargetTimeout = TimeSpan.FromSeconds(10);

        private readonly ITutorialOverlay _overlay;
        private readonly ITutorialDialoguePresenter _dialogue;
        private readonly ITutorialFocusPresenter _focus;
        private readonly ITutorialTargetRegistry _targets;
        private readonly SequenceHandlerRegistry _handlers;
        private readonly IReadOnlyList<ITutorialStepObserver> _observers;
        private readonly CoreLogger _logger;

        public TutorialScriptRunner(
            ITutorialOverlay overlay,
            ITutorialDialoguePresenter dialogue,
            ITutorialFocusPresenter focus,
            ITutorialTargetRegistry targets,
            TutorialCommandRegistry commands,
            IEnumerable<ITutorialStepObserver> observers = null,
            CoreLogger logger = null)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            _dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            _focus = focus ?? throw new ArgumentNullException(nameof(focus));
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));
            _handlers = new();
            foreach (var signature in commands.Catalog.Signatures)
            {
                var key = signature.Key;
                switch (signature.Category)
                {
                    case TutorialCommandCategory.Action:
                        _handlers.RegisterAction(key, (context, token) => commands.GetAction(key).ExecuteAsync(ToTutorialContext(context), token));
                        break;
                    case TutorialCommandCategory.Condition:
                        _handlers.RegisterCondition(key, context => commands.GetCondition(key).Evaluate(ToTutorialContext(context)));
                        break;
                    case TutorialCommandCategory.Wait:
                        _handlers.RegisterWait(key, (context, token) => commands.GetWait(key).WaitAsync(ToTutorialContext(context), token));
                        break;
                }
            }
            _observers = observers == null ? Array.Empty<ITutorialStepObserver>() : new List<ITutorialStepObserver>(observers);
            _logger = logger;
        }

        public async UniTask RunAsync(string chapterId, TutorialStepScript step, int index, CancellationToken cancellationToken = default)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));

            var scope = new StepScope(new TutorialStepInfo(chapterId, step.Id, index));
            for (var observer = 0; observer < _observers.Count; observer++)
                _observers[observer].StepStarted(scope.Info);

            var commands = new PresentationCommands(this, scope);
            var runner = new SequenceRunner(_handlers, commands);
            var cleanup = ConvertNodes(step.Finally) ?? Array.Empty<SequenceNode>();
            var finalNodes = new SequenceNode[cleanup.Length + 1];
            finalNodes[0] = new SequenceNode { Command = TutorialPrimitives.ClearHighlight };
            Array.Copy(cleanup, 0, finalNodes, 1, cleanup.Length);
            await runner.RunStepAsync(chapterId, step.Id, index, ConvertNodes(step.Body), finalNodes, cancellationToken);

            for (var observer = 0; observer < _observers.Count; observer++)
                _observers[observer].StepCompleted(scope.Info);
        }

        private async UniTask RunPresentationAsync(SequenceNode node, StepScope scope, CancellationToken cancellationToken)
        {
            var arguments = node.Arguments ?? Array.Empty<string>();
            switch (node.Command)
            {
                case TutorialPrimitives.Say:
                    await EnsureOpenAsync(scope, node, cancellationToken);
                    await _dialogue.ShowAsync(await CreateDialogueAsync(node, cancellationToken), cancellationToken);
                    return;
                case TutorialPrimitives.Highlight:
                    await EnsureOpenAsync(scope, node, cancellationToken);
                    var focusTarget = await FindTargetAsync(arguments[0], Timeout(node), node, cancellationToken);
                    if (focusTarget == null)
                        return;
                    _focus.Focus(new TutorialFocusRequest
                    {
                        Target = focusTarget,
                        Padding = TryGetNumber(node, TutorialPrimitives.PaddingArgument),
                        ShowHand = TutorialArguments.ParseFlag(node.GetOption(TutorialPrimitives.HandArgument), true),
                    });
                    return;
                case TutorialPrimitives.ClearHighlight:
                    _focus.ClearFocus();
                    return;
                case TutorialPrimitives.Show:
                    await EnsureOpenAsync(scope, node, cancellationToken);
                    _overlay.SetVisible(true);
                    return;
                case TutorialPrimitives.Hide:
                    _overlay.SetVisible(false);
                    return;
                case TutorialPrimitives.Close:
                    _focus.ClearFocus();
                    _overlay.Close();
                    scope.IsOpen = false;
                    return;
                case TutorialPrimitives.WaitTarget:
                    await FindTargetAsync(arguments[0], Timeout(node), node, cancellationToken);
                    return;
                case TutorialPrimitives.WaitClick:
                    // Like wait-target: a target that never appears is logged and skipped, so the chapter cannot hang on it.
                    var clickTarget = await FindTargetAsync(arguments[0], Timeout(node), node, cancellationToken);
                    if (clickTarget != null)
                        await WaitForClickAsync(clickTarget, cancellationToken);
                    return;
                default:
                    throw new InvalidOperationException($"Unknown tutorial presentation command '{node.Command}' (line {node.Line}).");
            }
        }

        private async UniTask EnsureOpenAsync(StepScope scope, SequenceNode node, CancellationToken cancellationToken)
        {
            if (scope.IsOpen)
                return;

            if (await _overlay.OpenAsync(cancellationToken))
            {
                scope.IsOpen = true;
                return;
            }

            _logger?.Log(LogLevel.Warning, LogCategory,
                $"Tutorial overlay could not open for step '{scope.Info.StepId}' (line {node.Line}); the step ends without it.");
            throw new SequenceStepExitException();
        }

        private async UniTask<TutorialDialogueRequest> CreateDialogueAsync(SequenceNode node, CancellationToken cancellationToken)
        {
            var anchorId = node.GetOption(TutorialPrimitives.TargetArgument);
            var anchor = anchorId != null
                ? await FindTargetAsync(anchorId, DefaultTargetTimeout, node, cancellationToken)
                : null;

            return new TutorialDialogueRequest
            {
                TextKey = node.Arguments[0],
                SpeakerKey = node.GetOption(TutorialPrimitives.SpeakerArgument),
                WaitForContinue = node.GetOption(TutorialPrimitives.ContinueArgument, TutorialPrimitives.ContinueTap) != TutorialPrimitives.ContinueNone,
                Anchor = anchor,
                FollowFocus = anchorId == null,
                Side = ParseSide(node.GetOption(TutorialPrimitives.SideArgument)),
                ShowArrow = TutorialArguments.ParseFlag(node.GetOption(TutorialPrimitives.ArrowArgument), true),
            };
        }

        private async UniTask<ITutorialTarget> FindTargetAsync(string id, TimeSpan timeout, SequenceNode node, CancellationToken cancellationToken)
        {
            if (_targets.TryGet(id, out var target))
                return target;

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Disposed before the source (reverse declaration order) so the timer never fires on a disposed source.
            using var timeoutTimer = timeoutSource.CancelAfterSlim(timeout, DelayType.UnscaledDeltaTime);
            try
            {
                return await _targets.WaitForAsync(id, timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.Log(LogLevel.Warning, LogCategory,
                    $"Tutorial target '{id}' did not appear within {timeout.TotalSeconds:0.#}s (line {node.Line}); continuing without it.");
                return null;
            }
        }

        private static async UniTask WaitForClickAsync(ITutorialTarget target, CancellationToken cancellationToken)
        {
            var completion = new UniTaskCompletionSource();
            void OnClicked() => completion.TrySetResult();
            target.Clicked += OnClicked;
            try
            {
                await using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
                    await completion.Task;
            }
            finally
            {
                target.Clicked -= OnClicked;
            }
        }

        private static TutorialCommandContext ToTutorialContext(SequenceContext context)
            => new(context.SequenceId, context.StepId, context.StepIndex, context.Arguments, context.Line);

        private static SequenceNode[] ConvertNodes(TutorialNode[] nodes)
        {
            if (nodes == null)
                return null;
            var result = new SequenceNode[nodes.Length];
            for (var index = 0; index < nodes.Length; index++)
            {
                var node = nodes[index];
                var options = new Dictionary<string, string>(StringComparer.Ordinal);
                if (node.NamedArguments != null)
                    foreach (var option in node.NamedArguments)
                        if (option != null)
                            options[option.Name] = option.Value;
                result[index] = new SequenceNode
                {
                    Kind = (SequenceNodeKind)node.Kind,
                    Command = node.Command,
                    Arguments = node.Arguments,
                    Negated = node.Negated,
                    Children = ConvertNodes(node.Children),
                    ElseChildren = ConvertNodes(node.ElseChildren),
                    Options = options,
                    Line = node.Line,
                };
            }
            return result;
        }

        private static TimeSpan Timeout(SequenceNode node)
            => TryGetNumber(node, TutorialPrimitives.TimeoutArgument) is float seconds
                ? TimeSpan.FromSeconds(seconds)
                : DefaultTargetTimeout;

        private static float? TryGetNumber(SequenceNode node, string name)
        {
            var value = node.GetOption(name);
            return value != null && TutorialScriptValidator.TryParseNumber(value, out var number) ? number : null;
        }

        private static TooltipSide ParseSide(string value) => value switch
        {
            "above" => TooltipSide.Above,
            "below" => TooltipSide.Below,
            "left" => TooltipSide.Left,
            "right" => TooltipSide.Right,
            _ => TooltipSide.Auto,
        };

        private sealed class PresentationCommands : ISequenceCommandHandler
        {
            private readonly TutorialScriptRunner _runner;
            private readonly StepScope _scope;

            public PresentationCommands(TutorialScriptRunner runner, StepScope scope)
            {
                _runner = runner;
                _scope = scope;
            }

            public UniTask ExecuteAsync(SequenceContext context, CancellationToken cancellationToken)
                => _runner.RunPresentationAsync(context.Node, _scope, cancellationToken);
        }

        private sealed class StepScope
        {
            public TutorialStepInfo Info { get; }

            public bool IsOpen { get; set; }

            public StepScope(TutorialStepInfo info)
            {
                Info = info;
            }
        }
    }
}
