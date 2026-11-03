using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Lifecycle;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests
{
    public sealed class BootPipelineTests
    {
        private sealed class TestBootstrapEntryPoint : BootstrapEntryPoint
        {
            public bool StopBoot { get; set; }
            public bool FailBeforeBoot { get; set; }
            public bool Stopped { get; private set; }
            public Exception Failure { get; private set; }

            protected override void OnBeforeBoot()
            {
                if (FailBeforeBoot)
                    throw new InvalidOperationException("before boot failed");
            }

            protected override IReadOnlyList<IBootStep> CreateSteps()
                => new IBootStep[]
                {
                    new DelegateBootStep("test", 0, (_, _) => UniTask.FromResult(
                        StopBoot ? BootStepOutcome.Stop : BootStepOutcome.Continue)),
                };

            protected override void OnBootStopped() => Stopped = true;

            protected override void OnBootFailed(Exception exception) => Failure = exception;
        }

        private sealed class RecordingCurtain : ILoadingCurtain
        {
            public readonly List<string> Calls = new();

            public void BeginReload() { }
            public void CancelReload() { }
            public UniTask CompleteAsync() => UniTask.CompletedTask;
            public void AdvanceStep(string status) => Calls.Add("advance");
            public void SetTransitionProgress(float progress) { }
            public void AdvanceWeightedStep(int weight, string status) => Calls.Add($"advance:{weight}");
            public void SetWeightedStepFraction(float fraction, int weight, string status) => Calls.Add($"fraction:{fraction.ToString("0.0", CultureInfo.InvariantCulture)}/{weight}");
            public void ShowFatalError(string title, string description) { }
            public void ShowWaitingForConnection(string title, string description) { }
            public void ClearWaitingForConnection() { }
        }

        [Test]
        public void StepsRunInOrder_AndEachFillsItsWeightedShareOfTheBar()
        {
            var order = new List<string>();
            var curtain = new RecordingCurtain();
            var pipeline = new BootPipeline(new IBootStep[]
            {
                new DelegateBootStep("first", 2, (progress, _) =>
                {
                    order.Add("first");
                    progress.Report(0.5f);
                    return UniTask.CompletedTask;
                }),
                new DelegateBootStep("second", 1, (_, _) =>
                {
                    order.Add("second");
                    return UniTask.CompletedTask;
                }),
            }, curtain);

            var completed = pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.That(completed, Is.True);
            Assert.That(order, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(curtain.Calls, Is.EqualTo(new[] { "fraction:0.5/2", "advance:2", "advance:1" }));
        }

        [Test]
        public void AStepThatStopsTheBoot_EndsItBeforeTheNextStep_AndIsNotCountedAsDone()
        {
            var ran = new List<string>();
            var curtain = new RecordingCurtain();
            var pipeline = new BootPipeline(new IBootStep[]
            {
                new DelegateBootStep("gate", 1, (_, _) =>
                {
                    ran.Add("gate");
                    return UniTask.FromResult(BootStepOutcome.Stop);
                }),
                new DelegateBootStep("never", 1, (_, _) =>
                {
                    ran.Add("never");
                    return UniTask.CompletedTask;
                }),
            }, curtain);

            var completed = pipeline.RunAsync().GetAwaiter().GetResult();

            Assert.That(completed, Is.False);
            Assert.That(ran, Is.EqualTo(new[] { "gate" }));
            Assert.That(curtain.Calls, Is.Empty, "a stop must not push the bar forward");
        }

        [Test]
        public void AStepWithoutWeight_DoesNotMoveTheBar()
        {
            var curtain = new RecordingCurtain();
            var pipeline = new BootPipeline(new IBootStep[]
            {
                new DelegateBootStep("prelude", 0, (progress, _) =>
                {
                    Assert.That(progress, Is.Null);
                    return UniTask.CompletedTask;
                }),
            }, curtain);

            Assert.That(pipeline.RunAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(curtain.Calls, Is.Empty);
        }

        [Test]
        public void AFailingStep_PropagatesToTheHost_AndLaterStepsDoNotRun()
        {
            var laterRan = false;
            var pipeline = new BootPipeline(new IBootStep[]
            {
                new DelegateBootStep("broken", 1, (_, _) => UniTask.FromException(new InvalidOperationException("no content"))),
                new DelegateBootStep("later", 1, (_, _) =>
                {
                    laterRan = true;
                    return UniTask.CompletedTask;
                }),
            });

            Assert.Throws<InvalidOperationException>(() => pipeline.RunAsync().GetAwaiter().GetResult());
            Assert.That(laterRan, Is.False);
        }

        [Test]
        public void ACancelledBoot_StopsBeforeTheNextStep()
        {
            using var cancellation = new CancellationTokenSource();
            var ran = new List<string>();
            var pipeline = new BootPipeline(new IBootStep[]
            {
                new DelegateBootStep("first", 1, (_, _) =>
                {
                    ran.Add("first");
                    cancellation.Cancel();
                    return UniTask.CompletedTask;
                }),
                new DelegateBootStep("second", 1, (_, _) =>
                {
                    ran.Add("second");
                    return UniTask.CompletedTask;
                }),
            });

            Assert.Throws<OperationCanceledException>(() => pipeline.RunAsync(cancellation.Token).GetAwaiter().GetResult());
            Assert.That(ran, Is.EqualTo(new[] { "first" }));
        }

        [Test]
        public void Bootstrap_ReportsIntentionalStopSeparatelyFromFailure()
        {
            var gameObject = new GameObject("bootstrap test");
            try
            {
                var bootstrap = gameObject.AddComponent<TestBootstrapEntryPoint>();
                bootstrap.StopBoot = true;

                typeof(BootstrapEntryPoint)
                    .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(bootstrap, null);

                Assert.That(bootstrap.Stopped, Is.True);
                Assert.That(bootstrap.Failure, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Bootstrap_ReportsOnBeforeBootExceptionAsFailure()
        {
            var gameObject = new GameObject("bootstrap test");
            try
            {
                var bootstrap = gameObject.AddComponent<TestBootstrapEntryPoint>();
                bootstrap.FailBeforeBoot = true;

                typeof(BootstrapEntryPoint)
                    .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(bootstrap, null);

                Assert.That(bootstrap.Failure, Is.TypeOf<InvalidOperationException>());
                Assert.That(bootstrap.Stopped, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }
}
