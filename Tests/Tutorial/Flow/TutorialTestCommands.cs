using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Tutorial.Flow;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Tests.Tutorial.Flow
{
    internal static class TutorialTestCommands
    {
        public static readonly TutorialCommandCatalog Catalog = TutorialCommandCatalog.FromTypes(new[]
        {
            typeof(IsCondition), typeof(SignalWait), typeof(LogAction),
        });
    }

    [TutorialCommand("test.is", "value")]
    internal sealed class IsCondition : ITutorialCondition
    {
        public bool Evaluate(TutorialCommandContext context) => context.FlagArgument(0);
    }

    [TutorialCommand("test.signal", "name")]
    internal sealed class SignalWait : ITutorialWait
    {
        private readonly Dictionary<string, UniTaskCompletionSource> _signals = new();

        public int Pending => _signals.Count;

        public async UniTask WaitAsync(TutorialCommandContext context, CancellationToken cancellationToken)
        {
            var name = context.Argument(0);
            var completion = new UniTaskCompletionSource();
            _signals[name] = completion;
            try
            {
                using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
                    await completion.Task;
            }
            finally
            {
                _signals.Remove(name);
            }
        }

        public void Raise(string name)
        {
            if (_signals.TryGetValue(name, out var completion))
                completion.TrySetResult();
        }
    }

    [TutorialCommand("test.log", "text?")]
    internal sealed class LogAction : ITutorialAction
    {
        private readonly List<string> _log;

        public LogAction(List<string> log)
        {
            _log = log;
        }

        public UniTask ExecuteAsync(TutorialCommandContext context, CancellationToken cancellationToken)
        {
            _log.Add(context.Argument(0, string.Empty));
            return UniTask.CompletedTask;
        }
    }

    internal sealed class FakeTarget : ITutorialTarget
    {
        public FakeTarget(string id, bool available = true)
        {
            Id = id;
            IsAvailable = available;
        }

        public string Id { get; }
        public RectTransform Rect => null;
        public bool IsAvailable { get; set; }

        public event Action Clicked;

        public void Click() => Clicked?.Invoke();
    }

    internal sealed class FakePresentation : ITutorialOverlay, ITutorialDialoguePresenter, ITutorialFocusPresenter
    {
        public bool CanOpen = true;
        public int OpenCount;
        public int CloseCount;
        public int ClearFocusCount;
        public readonly List<TutorialDialogueRequest> Dialogues = new();

        public ITutorialTarget Focused { get; private set; }

        public UniTask<bool> OpenAsync(CancellationToken cancellationToken = default)
        {
            if (CanOpen)
                OpenCount++;
            return UniTask.FromResult(CanOpen);
        }

        public void SetVisible(bool visible)
        {
        }

        public void Close() => CloseCount++;

        public UniTask ShowAsync(TutorialDialogueRequest request, CancellationToken cancellationToken = default)
        {
            Dialogues.Add(request);
            return UniTask.CompletedTask;
        }

        public void Focus(TutorialFocusRequest request) => Focused = request.Target;

        public void ClearFocus()
        {
            ClearFocusCount++;
            Focused = null;
        }
    }

    internal sealed class RecordingStepObserver : ITutorialStepObserver
    {
        public readonly List<string> Started = new();
        public readonly List<string> Completed = new();

        public void StepStarted(TutorialStepInfo step) => Started.Add(step.StepId);

        public void StepCompleted(TutorialStepInfo step) => Completed.Add(step.StepId);
    }
}
