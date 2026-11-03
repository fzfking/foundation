using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.ContentMirroring;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Foundation.Tests.ContentMirroring
{
    public sealed class ContentMirrorCoordinatorTests
    {
        [Test]
        public void RouteUrl_ReplacesHostAndKeepsThePublishedRelativePath()
        {
            var mirror = new ContentMirror("edge", "https://edge.example/");

            Assert.That(
                mirror.RouteUrl("https://primary.example/VI/Prod/Android/catalog.hash"),
                Is.EqualTo("https://edge.example/VI/Prod/Android/catalog.hash"));
        }

        [Test]
        public void BaseQuery_IsAppendedAfterProbeAndRoutedPaths()
        {
            var mirror = new ContentMirror("edge", "https://edge.example/content?token=x");

            Assert.That(mirror.GetUrl("mirror-probe.bin"),
                Is.EqualTo("https://edge.example/content/mirror-probe.bin?token=x"));
            Assert.That(mirror.RouteUrl("https://primary.example/VI/Prod/Android/catalog.hash"),
                Is.EqualTo("https://edge.example/content/VI/Prod/Android/catalog.hash?token=x"));
        }

        [Test]
        public void QueryProvider_ReceivesTheFinalUriAndAddsProjectSpecificParameters()
        {
            Uri queriedUri = null;
            var mirror = new ContentMirror("edge", "https://edge.example/content", uri =>
            {
                queriedUri = uri;
                return "path=" + Uri.EscapeDataString(uri.AbsolutePath);
            });

            var url = mirror.GetUrl("mirror-probe.bin");

            Assert.That(queriedUri.AbsolutePath, Is.EqualTo("/content/mirror-probe.bin"));
            Assert.That(url, Is.EqualTo("https://edge.example/content/mirror-probe.bin?path=%2Fcontent%2Fmirror-probe.bin"));
        }

        [UnityTest]
        public IEnumerator Execute_ProbesEveryMirrorAndKeepsOneMirrorForAttempt() => UniTask.ToCoroutine(async () =>
        {
            var mirrors = CreateMirrors();
            var statistics = new MemoryStatisticsStore();
            var now = DateTimeOffset.UtcNow;
            statistics.SaveBytesPerSecond("fast", 4000d, now);
            statistics.SaveBytesPerSecond("slow", 1000d, now);
            var probe = new FakeProbe();
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 4000d);
            probe.Results["slow"] = new ContentMirrorProbeResult(true, 1000d);
            var coordinator = CreateCoordinator(mirrors, probe, statistics);
            var attempts = new List<string>();

            var succeeded = await coordinator.ExecuteAsync((mirror, _, _) =>
            {
                attempts.Add(mirror.Id);
                return UniTask.FromResult(true);
            });

            Assert.That(succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(new[] { "fast" }));
            Assert.That(probe.CallCount, Is.EqualTo(2));
        });

        [Test]
        public void Execute_CachedSpeedsOrderAttemptsWhileEveryMirrorIsProbed()
        {
            var mirrors = CreateMirrors();
            var statistics = new MemoryStatisticsStore();
            var now = DateTimeOffset.UtcNow;
            statistics.SaveBytesPerSecond("fast", 4000d, now);
            statistics.SaveBytesPerSecond("slow", 1000d, now);
            var probe = new FakeProbe();
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 1000d);
            probe.Results["slow"] = new ContentMirrorProbeResult(true, 4000d);
            var coordinator = CreateCoordinator(mirrors, probe, statistics);
            var attempts = new List<string>();

            var succeeded = coordinator.ExecuteAsync((mirror, _, _) =>
            {
                attempts.Add(mirror.Id);
                return UniTask.FromResult(true);
            }).GetAwaiter().GetResult();

            Assert.That(succeeded, Is.True);
            Assert.That(probe.CallCount, Is.EqualTo(2));
            Assert.That(attempts, Is.EqualTo(new[] { "fast" }));
            Assert.That(
                statistics.TryGetBytesPerSecond("slow", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10), out var savedSpeed),
                Is.True);
            Assert.That(savedSpeed, Is.EqualTo(4000d));
        }

        [Test]
        public void Execute_ReusesTheLastSuccessfulMirrorAcrossCoordinatorInstances()
        {
            var mirrors = CreateMirrors();
            var statistics = new MemoryStatisticsStore();
            var probe = new FakeProbe();
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 4000d);
            probe.Results["slow"] = new ContentMirrorProbeResult(true, 1000d);
            var firstAttempts = new List<string>();
            var first = CreateCoordinator(mirrors, probe, statistics);

            var firstSucceeded = first.ExecuteAsync((mirror, _, _) =>
            {
                firstAttempts.Add(mirror.Id);
                return UniTask.FromResult(true);
            }).GetAwaiter().GetResult();

            probe.Results["fast"] = new ContentMirrorProbeResult(true, 1000d);
            probe.Results["slow"] = new ContentMirrorProbeResult(true, 4000d);
            var secondAttempts = new List<string>();
            var second = CreateCoordinator(mirrors, probe, new MemoryStatisticsStore());

            var secondSucceeded = second.ExecuteAsync((mirror, _, _) =>
            {
                secondAttempts.Add(mirror.Id);
                return UniTask.FromResult(true);
            }).GetAwaiter().GetResult();

            Assert.That(firstSucceeded, Is.True);
            Assert.That(firstAttempts, Is.EqualTo(new[] { "fast" }));
            Assert.That(secondSucceeded, Is.True);
            Assert.That(secondAttempts, Is.EqualTo(new[] { "fast" }));
        }

        [Test]
        public void Execute_FailsOverAndClearsStickyWhenTheStickyAttemptFails()
        {
            var fast = new ContentMirror("fast", "https://fast.example/");
            var slow = new ContentMirror("slow", "https://slow.example/");
            var statistics = new MemoryStatisticsStore();
            var probe = new FakeProbe();
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 4000d);
            var seed = CreateCoordinator(new[] { fast }, probe, statistics);
            seed.ExecuteAsync((_, _, _) => UniTask.FromResult(true)).GetAwaiter().GetResult();

            probe.Results["slow"] = new ContentMirrorProbeResult(true, 1000d);
            var attempts = new List<string>();
            var coordinator = CreateCoordinator(new[] { fast, slow }, probe, statistics);

            var succeeded = coordinator.ExecuteAsync((mirror, _, _) =>
            {
                attempts.Add(mirror.Id);
                return UniTask.FromResult(mirror.Id == slow.Id);
            }).GetAwaiter().GetResult();

            Assert.That(succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(new[] { "fast", "slow" }));
        }

        [Test]
        public void Execute_ClearsStickyWhenItsProbeFails()
        {
            var fast = new ContentMirror("fast", "https://fast.example/");
            var slow = new ContentMirror("slow", "https://slow.example/");
            var statistics = new MemoryStatisticsStore();
            var probe = new FakeProbe();
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 4000d);
            var seed = CreateCoordinator(new[] { fast }, probe, statistics);
            seed.ExecuteAsync((_, _, _) => UniTask.FromResult(true)).GetAwaiter().GetResult();

            probe.Failures["fast"] = new InvalidOperationException("probe failed");
            var failed = CreateCoordinator(new[] { fast }, probe, statistics);
            Assert.That(failed.ExecuteAsync((_, _, _) => UniTask.FromResult(true)).GetAwaiter().GetResult(), Is.False);

            probe.Results["fast"] = new ContentMirrorProbeResult(true, 1000d);
            probe.Results["slow"] = new ContentMirrorProbeResult(true, 4000d);
            var attempts = new List<string>();
            var coordinator = CreateCoordinator(new[] { fast, slow }, probe, statistics);

            var succeeded = coordinator.ExecuteAsync((mirror, _, _) =>
            {
                attempts.Add(mirror.Id);
                return UniTask.FromResult(true);
            }).GetAwaiter().GetResult();

            Assert.That(succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(new[] { "slow" }));
        }

        [Test]
        public void Execute_ContinuesProbingAfterOneMirrorThrows()
        {
            var mirrors = CreateMirrors();
            var probe = new FakeProbe();
            probe.Failures["fast"] = new InvalidOperationException("probe failed");
            probe.Results["slow"] = new ContentMirrorProbeResult(true, 1000d);
            var coordinator = CreateCoordinator(mirrors, probe, new MemoryStatisticsStore());
            var attempts = new List<string>();

            var succeeded = coordinator.ExecuteAsync((mirror, _, _) =>
            {
                attempts.Add(mirror.Id);
                return UniTask.FromResult(true);
            }).GetAwaiter().GetResult();

            Assert.That(succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(new[] { "slow" }));
            Assert.That(probe.CallCount, Is.EqualTo(2));
        }

        [Test]
        public void Execute_WhenEveryProbeThrows_ReturnsFalseForCallerRetry()
        {
            var probe = new FakeProbe();
            probe.Failures["fast"] = new InvalidOperationException("fast failed");
            probe.Failures["slow"] = new InvalidOperationException("slow failed");
            var coordinator = CreateCoordinator(CreateMirrors(), probe, new MemoryStatisticsStore());

            var succeeded = coordinator.ExecuteAsync((_, _, _) => UniTask.FromResult(true)).GetAwaiter().GetResult();

            Assert.That(succeeded, Is.False);
            Assert.That(probe.CallCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Execute_WhenOneMirrorCannotLoadTheRequestedContent_UsesTheNextMirror() => UniTask.ToCoroutine(async () =>
        {
            var mirrors = CreateMirrors();
            var statistics = new MemoryStatisticsStore();
            var now = DateTimeOffset.UtcNow;
            statistics.SaveBytesPerSecond("fast", 4000d, now);
            statistics.SaveBytesPerSecond("slow", 1000d, now);
            var probe = new FakeProbe();
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 4000d);
            probe.Results["slow"] = new ContentMirrorProbeResult(true, 1000d);
            var coordinator = CreateCoordinator(mirrors, probe, statistics);
            var attempts = new List<string>();
            var activeTokenChecks = new List<bool>();

            var succeeded = await coordinator.ExecuteAsync((mirror, _, cancellationToken) =>
            {
                attempts.Add(mirror.Id);
                activeTokenChecks.Add(coordinator.IsActiveAttemptToken(cancellationToken));
                return UniTask.FromResult(mirror.Id == "slow");
            });

            Assert.That(succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(new[] { "fast", "slow" }));
            Assert.That(activeTokenChecks, Is.EqualTo(new[] { true, true }));
            Assert.That(coordinator.IsActiveAttemptToken(CancellationToken.None), Is.False);
        });

        [UnityTest]
        public IEnumerator Execute_PrefersConfiguredMirrorWhenItsEtaIsWithinThePreferredBudget() => UniTask.ToCoroutine(async () =>
        {
            var mirrors = new[]
            {
                new ContentMirror("preferred", "https://preferred.example/"),
                new ContentMirror("fast", "https://fast.example/")
            };
            var statistics = new MemoryStatisticsStore();
            var now = DateTimeOffset.UtcNow;
            statistics.SaveBytesPerSecond("preferred", 1000d, now);
            statistics.SaveBytesPerSecond("fast", 2000d, now);
            var probe = new FakeProbe();
            probe.Results["preferred"] = new ContentMirrorProbeResult(true, 1000d);
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 2000d);
            var coordinator = CreateCoordinator(
                mirrors,
                probe,
                statistics,
                "preferred",
                TimeSpan.FromSeconds(10));
            var attempts = new List<string>();

            var succeeded = await coordinator.ExecuteAsync((mirror, _, _) =>
            {
                attempts.Add(mirror.Id);
                return UniTask.FromResult(true);
            }, expectedDownloadBytes: 10_000);

            Assert.That(succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(new[] { "preferred" }));
        });

        [UnityTest]
        public IEnumerator Execute_UsesFastestMirrorWhenPreferredEtaExceedsItsBudget() => UniTask.ToCoroutine(async () =>
        {
            var mirrors = new[]
            {
                new ContentMirror("preferred", "https://preferred.example/"),
                new ContentMirror("fast", "https://fast.example/")
            };
            var statistics = new MemoryStatisticsStore();
            var now = DateTimeOffset.UtcNow;
            statistics.SaveBytesPerSecond("preferred", 1000d, now);
            statistics.SaveBytesPerSecond("fast", 2000d, now);
            var probe = new FakeProbe();
            probe.Results["preferred"] = new ContentMirrorProbeResult(true, 1000d);
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 2000d);
            var coordinator = CreateCoordinator(
                mirrors,
                probe,
                statistics,
                "preferred",
                TimeSpan.FromSeconds(10));
            var attempts = new List<string>();

            var succeeded = await coordinator.ExecuteAsync((mirror, _, _) =>
            {
                attempts.Add(mirror.Id);
                return UniTask.FromResult(true);
            }, expectedDownloadBytes: 10_001);

            Assert.That(succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(new[] { "fast" }));
        });

        [UnityTest]
        public IEnumerator Execute_FastestFallbackRemainsFastestAfterPreferredMirrorIsPromoted() => UniTask.ToCoroutine(async () =>
        {
            var mirrors = new[]
            {
                new ContentMirror("preferred", "https://preferred.example/"),
                new ContentMirror("fast", "https://fast.example/")
            };
            var statistics = new MemoryStatisticsStore();
            var now = DateTimeOffset.UtcNow;
            statistics.SaveBytesPerSecond("preferred", 1000d, now);
            statistics.SaveBytesPerSecond("fast", 2000d, now);
            var probe = new FakeProbe();
            probe.Results["preferred"] = new ContentMirrorProbeResult(true, 1000d);
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 2000d);
            var coordinator = CreateCoordinator(
                mirrors,
                probe,
                statistics,
                "preferred",
                TimeSpan.FromSeconds(10));
            var attempts = new List<string>();

            var succeeded = await coordinator.ExecuteAsync((mirror, progress, cancellationToken) =>
            {
                attempts.Add(mirror.Id);
                if (attempts.Count <= 2)
                {
                    progress.Report(new ContentMirrorProgress(0, 60_002));
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return UniTask.FromResult(true);
            }, expectedDownloadBytes: 10_000);

            Assert.That(succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(new[] { "preferred", "fast", "fast" }));
        });

        [UnityTest]
        public IEnumerator Execute_ExcludesTheMirrorThatAlreadyFailedSignatureVerification() => UniTask.ToCoroutine(async () =>
        {
            var mirrors = CreateMirrors();
            var statistics = new MemoryStatisticsStore();
            var now = DateTimeOffset.UtcNow;
            statistics.SaveBytesPerSecond("fast", 4000d, now);
            statistics.SaveBytesPerSecond("slow", 1000d, now);
            var probe = new FakeProbe();
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 4000d);
            probe.Results["slow"] = new ContentMirrorProbeResult(true, 1000d);
            var coordinator = CreateCoordinator(mirrors, probe, statistics);
            var attempts = new List<string>();

            var succeeded = await coordinator.ExecuteAsync((mirror, _, _) =>
            {
                attempts.Add(mirror.Id);
                return UniTask.FromResult(true);
            }, excludedMirrorBaseUrl: "https://fast.example/");

            Assert.That(succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(new[] { "slow" }));
        });

        [UnityTest]
        public IEnumerator Execute_WhenEtaIsTooLong_FailsOverThenFallsBackToFastestMirror() => UniTask.ToCoroutine(async () =>
        {
            var mirrors = CreateMirrors();
            var statistics = new MemoryStatisticsStore();
            var probe = new FakeProbe();
            probe.Results["fast"] = new ContentMirrorProbeResult(true, 4000d);
            probe.Results["slow"] = new ContentMirrorProbeResult(true, 1000d);
            var coordinator = CreateCoordinator(mirrors, probe, statistics);
            var attempts = new List<string>();
            var thresholdCancellations = 0;

            var succeeded = await coordinator.ExecuteAsync((mirror, progress, cancellationToken) =>
            {
                attempts.Add(mirror.Id);
                if (attempts.Count <= 2)
                {
                    progress.Report(new ContentMirrorProgress(0, 1024 * 1024));
                    if (cancellationToken.IsCancellationRequested)
                    {
                        thresholdCancellations++;
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                return UniTask.FromResult(true);
            });

            Assert.That(succeeded, Is.True);
            Assert.That(thresholdCancellations, Is.EqualTo(2));
            Assert.That(attempts, Is.EqualTo(new[] { "fast", "slow", "fast" }));
            Assert.That(statistics.TryGetBytesPerSecond("fast", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10), out _), Is.True);
            Assert.That(statistics.TryGetBytesPerSecond("slow", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10), out _), Is.True);
        });

        [UnityTest]
        public IEnumerator Execute_WithOneSlowMirror_CancelsThenUsesItAsFallback() => UniTask.ToCoroutine(async () =>
        {
            var mirror = new ContentMirror("only", "https://only.example/");
            var probe = new FakeProbe();
            probe.Results[mirror.Id] = new ContentMirrorProbeResult(true, 1000d);
            var coordinator = CreateCoordinator(new[] { mirror }, probe, new MemoryStatisticsStore());
            var attempts = 0;

            var succeeded = await coordinator.ExecuteAsync((_, progress, cancellationToken) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    progress.Report(new ContentMirrorProgress(0, 1024 * 1024));
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return UniTask.FromResult(true);
            });

            Assert.That(succeeded, Is.True);
            Assert.That(attempts, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator Execute_ParallelProbes_WaitsForAllAndUsesSuccessfulResult() => UniTask.ToCoroutine(async () =>
        {
            var probe = new ControlledProbe();
            var coordinator = CreateCoordinator(CreateMirrors(), probe, new MemoryStatisticsStore(), parallelProbes: true);
            string selected = null;
            var operation = coordinator.ExecuteAsync((mirror, _, _) =>
            {
                selected = mirror.Id;
                return UniTask.FromResult(true);
            });

            Assert.That(probe.Started, Is.EqualTo(2));
            probe.Slow.TrySetResult(new ContentMirrorProbeResult(true, 1000));
            Assert.That(selected, Is.Null);
            probe.Fast.TrySetException(new InvalidOperationException());
            Assert.That(await operation, Is.True);
            Assert.That(selected, Is.EqualTo("slow"));
        });

        [UnityTest]
        public IEnumerator Execute_ParallelProbes_CancelsAllBeforeReturning() => UniTask.ToCoroutine(async () =>
        {
            var probe = new ControlledProbe();
            var coordinator = CreateCoordinator(CreateMirrors(), probe, new MemoryStatisticsStore(), parallelProbes: true);
            using var cancellation = new CancellationTokenSource();
            var operation = coordinator.ExecuteAsync((_, _, _) => UniTask.FromResult(true), cancellation.Token);
            Assert.That(probe.Started, Is.EqualTo(2));
            cancellation.Cancel();
            try
            {
                await operation;
                Assert.Fail("Expected cancellation.");
            }
            catch (OperationCanceledException)
            {
                Assert.That(probe.Finished, Is.EqualTo(2));
            }
        });

        [UnityTest]
        public IEnumerator Execute_SequentialProbes_WaitsForFirstProbe() => UniTask.ToCoroutine(async () =>
        {
            var probe = new ControlledProbe();
            var coordinator = CreateCoordinator(CreateMirrors(), probe, new MemoryStatisticsStore());
            var operation = coordinator.ExecuteAsync((_, _, _) => UniTask.FromResult(true));
            Assert.That(probe.Started, Is.EqualTo(1));
            probe.Fast.TrySetResult(new ContentMirrorProbeResult(true, 1000));
            Assert.That(probe.Started, Is.EqualTo(2));
            probe.Slow.TrySetResult(new ContentMirrorProbeResult(true, 500));
            Assert.That(await operation, Is.True);
        });

        [UnityTest]
        public IEnumerator Execute_ParallelProbes_UsesCachedOrder() => UniTask.ToCoroutine(async () =>
        {
            var probe = new ControlledProbe();
            var statistics = new MemoryStatisticsStore();
            statistics.SaveBytesPerSecond("slow", 4000, DateTimeOffset.UtcNow);
            statistics.SaveBytesPerSecond("fast", 1000, DateTimeOffset.UtcNow);
            var coordinator = CreateCoordinator(CreateMirrors(), probe, statistics, parallelProbes: true);
            string selected = null;
            var operation = coordinator.ExecuteAsync((mirror, _, _) =>
            {
                selected = mirror.Id;
                return UniTask.FromResult(true);
            });
            probe.Fast.TrySetResult(new ContentMirrorProbeResult(true, 5000));
            probe.Slow.TrySetResult(new ContentMirrorProbeResult(true, 500));
            Assert.That(await operation, Is.True);
            Assert.That(selected, Is.EqualTo("slow"));
        });

        private static ContentMirrorCoordinator CreateCoordinator(
            IReadOnlyList<ContentMirror> mirrors,
            IContentMirrorProbe probe,
            IContentMirrorStatisticsStore statistics,
            string preferredMirrorId = null,
            TimeSpan? preferredMirrorMaximumEta = null,
            bool parallelProbes = false)
        {
            return new ContentMirrorCoordinator(
                new ContentMirrorConfiguration(
                    mirrors,
                    "mirror-probe.bin",
                    256 * 1024,
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromMinutes(10),
                    parallelProbes),
                probe,
                statistics,
                preferredMirrorId,
                preferredMirrorMaximumEta);
        }

        private static IReadOnlyList<ContentMirror> CreateMirrors() => new[]
        {
            new ContentMirror("fast", "https://fast.example/"),
            new ContentMirror("slow", "https://slow.example/")
        };

        private sealed class FakeProbe : IContentMirrorProbe
        {
            public Dictionary<string, ContentMirrorProbeResult> Results { get; } = new();
            public Dictionary<string, Exception> Failures { get; } = new();
            public int CallCount { get; private set; }

            public UniTask<ContentMirrorProbeResult> ProbeAsync(
                ContentMirror mirror,
                string relativePath,
                int expectedSizeBytes,
                CancellationToken cancellationToken)
            {
                CallCount++;
                if (Failures.TryGetValue(mirror.Id, out var failure))
                    throw failure;
                return UniTask.FromResult(Results[mirror.Id]);
            }
        }

        private sealed class ControlledProbe : IContentMirrorProbe
        {
            public readonly UniTaskCompletionSource<ContentMirrorProbeResult> Fast = new();
            public readonly UniTaskCompletionSource<ContentMirrorProbeResult> Slow = new();
            public int Started { get; private set; }
            public int Finished { get; private set; }

            public async UniTask<ContentMirrorProbeResult> ProbeAsync(
                ContentMirror mirror, string relativePath, int expectedSizeBytes, CancellationToken cancellationToken)
            {
                Started++;
                try
                {
                    return await (mirror.Id == "fast" ? Fast.Task : Slow.Task).AttachExternalCancellation(cancellationToken);
                }
                finally
                {
                    Finished++;
                }
            }
        }

        private sealed class MemoryStatisticsStore : IContentMirrorStatisticsStore
        {
            private readonly Dictionary<string, (double Speed, DateTimeOffset MeasuredAt)> _measurements = new();

            public bool TryGetBytesPerSecond(string mirrorId, DateTimeOffset now, TimeSpan maximumAge, out double bytesPerSecond)
            {
                bytesPerSecond = 0d;
                if (!_measurements.TryGetValue(mirrorId, out var measurement)
                    || now - measurement.MeasuredAt < TimeSpan.Zero
                    || now - measurement.MeasuredAt > maximumAge)
                    return false;

                bytesPerSecond = measurement.Speed;
                return bytesPerSecond > 0d;
            }

            public void SaveBytesPerSecond(string mirrorId, double bytesPerSecond, DateTimeOffset measuredAt)
                => _measurements[mirrorId] = (bytesPerSecond, measuredAt);

            public void Remove(string mirrorId) => _measurements.Remove(mirrorId);
        }
    }
}
