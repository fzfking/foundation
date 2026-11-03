using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.ContentMirroring;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.TestTools;
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

namespace Foundation.Infrastructure.Addressables.Tests
{
    public sealed class AddressablesContentSynchronizerMirrorTests
    {
        [Test]
        public void Rewrite_IgnoresSiblingPathButRewritesTheContentRootPath()
        {
            const string root = "https://cdn.example.com/content";
            const string endpoint = "https://cdn.example.com/content/Prod/";

            Assert.That(
                AddressablesEndpointRouting.Rewrite(root, endpoint, "https://cdn.example.com/content-backup/Dev/Android/catalog.hash"),
                Is.EqualTo("https://cdn.example.com/content-backup/Dev/Android/catalog.hash"));
            Assert.That(
                AddressablesEndpointRouting.Rewrite(root, endpoint, "https://cdn.example.com/content/Dev/Android/catalog.hash"),
                Is.EqualTo("https://cdn.example.com/content/Prod/Android/catalog.hash"));
        }

        [Test]
        public void MirrorRewrite_DoesNotDuplicateTheContentRootAndAppliesItsQuery()
        {
            const string root = "https://cdn.example.com/content";
            const string endpoint = "https://cdn.example.com/content/Prod/";
            const string internalId = "https://cdn.example.com/content/Dev/Android/catalog.hash";
            var mirror = new ContentMirror("edge", "https://edge.example/content?token=x", _ => "signature=y");

            Assert.That(
                AddressablesEndpointRouting.RewriteToContentMirror(root, endpoint, mirror, internalId),
                Is.EqualTo("https://edge.example/content/Prod/Android/catalog.hash?token=x&signature=y"));
        }

        [Test]
        public void MirrorRewrite_WithRootMirrorBase_KeepsTheOriginalContentPath()
        {
            const string root = "https://cdn.example.com/VI/";
            const string endpoint = "https://cdn.example.com/VI/Prod/";
            const string internalId = "https://cdn.example.com/VI/Dev/Android/catalog.hash";
            var mirror = new ContentMirror("edge", "https://edge.example/");

            Assert.That(
                AddressablesEndpointRouting.RewriteToContentMirror(root, endpoint, mirror, internalId),
                Is.EqualTo("https://edge.example/VI/Prod/Android/catalog.hash"));
        }

        [Test]
        public void MirrorRewrite_AppliesQueryProviderWhenMirrorHasADifferentBasePath()
        {
            const string root = "https://cdn.example.com/VI/";
            const string endpoint = "https://cdn.example.com/VI/Prod/";
            const string internalId = "https://cdn.example.com/VI/Dev/Android/catalog.hash";
            var mirror = new ContentMirror("edge", "https://edge.example/mirror?token=x", _ => "signature=y");

            Assert.That(
                AddressablesEndpointRouting.RewriteToContentMirror(root, endpoint, mirror, internalId),
                Is.EqualTo("https://edge.example/mirror/VI/Prod/Android/catalog.hash?token=x&signature=y"));
        }

        [UnityTest]
        public IEnumerator StartupOffline_WithAllContentCached_SucceedsWithoutProbingMirrors() => UniTask.ToCoroutine(async () =>
        {
            var checker = new StubReachabilityChecker(isReachable: false, networkUnavailable: true);
            var probe = new CountingProbe();
            var synchronizer = CreateSynchronizer(checker, probe);

            await synchronizer.SynchronizeForStartupAsync();

            Assert.That(synchronizer.State, Is.EqualTo(AddressablesContentSynchronizer.SynchronizationState.Succeeded));
            Assert.That(probe.CallCount, Is.Zero);
            Assert.That(checker.CallCount, Is.Zero);
        });

        [UnityTest]
        public IEnumerator Startup_WhenALegacyCheckerReportsUnreachableAndTheDeviceIsOffline_DoesNotProbe() => UniTask.ToCoroutine(async () =>
        {
            var probe = new CountingProbe();
            var synchronizer = CreateSynchronizer(new LegacyReachabilityChecker(isReachable: false), probe);
            var previousReachability = AddressablesContentSynchronizer.DeviceNetworkReachabilityProvider;
            AddressablesContentSynchronizer.DeviceNetworkReachabilityProvider = () => NetworkReachability.NotReachable;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await synchronizer.SynchronizeForStartupAsync(cancellationToken: timeout.Token);

                Assert.That(synchronizer.State, Is.EqualTo(AddressablesContentSynchronizer.SynchronizationState.Succeeded));
                Assert.That(probe.CallCount, Is.Zero);
            }
            finally
            {
                AddressablesContentSynchronizer.DeviceNetworkReachabilityProvider = previousReachability;
            }
        });

        [UnityTest]
        public IEnumerator Startup_WhenALegacyCheckerReportsUnreachableButTheDeviceIsOnline_StillAttempts() => UniTask.ToCoroutine(async () =>
        {
            var probe = new CountingProbe();
            var synchronizer = CreateSynchronizer(new LegacyReachabilityChecker(isReachable: false), probe);
            var previousReachability = AddressablesContentSynchronizer.DeviceNetworkReachabilityProvider;
            AddressablesContentSynchronizer.DeviceNetworkReachabilityProvider = () => NetworkReachability.ReachableViaLocalAreaNetwork;
            try
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                synchronizer.StateChanged += state =>
                {
                    if (state == AddressablesContentSynchronizer.SynchronizationState.WaitingForConnection)
                        cancellation.Cancel();
                };

                try
                {
                    await synchronizer.SynchronizeForStartupAsync(cancellationToken: cancellation.Token);
                    Assert.Fail("A failed mirror run should keep waiting, not finish.");
                }
                catch (OperationCanceledException)
                {
                }

                Assert.That(probe.CallCount, Is.EqualTo(1));
            }
            finally
            {
                AddressablesContentSynchronizer.DeviceNetworkReachabilityProvider = previousReachability;
            }
        });

        [UnityTest]
        public IEnumerator Startup_WhenHostProbesFailOnAReachableNetwork_StillAttemptsAndRestoresTheEndpoint() => UniTask.ToCoroutine(async () =>
        {
            const string root = "https://cdn.example.com/content";
            const string endpoint = "https://cdn.example.com/content/Prod/";
            AddressablesEndpointRouting.Apply(root, endpoint);
            AddressablesEndpointRouting.ApplyMirror(new ContentMirror("stale", "https://stale.example/"));
            try
            {
                var checker = new StubReachabilityChecker(isReachable: false, networkUnavailable: false);
                var probe = new CountingProbe();
                var synchronizer = CreateSynchronizer(checker, probe);
                using var cancellation = new CancellationTokenSource();
                synchronizer.StateChanged += state =>
                {
                    if (state == AddressablesContentSynchronizer.SynchronizationState.WaitingForConnection)
                        cancellation.Cancel();
                };

                try
                {
                    await synchronizer.SynchronizeForStartupAsync(cancellationToken: cancellation.Token);
                    Assert.Fail("A failed mirror run should keep waiting, not finish.");
                }
                catch (OperationCanceledException)
                {
                }

                Assert.That(probe.CallCount, Is.EqualTo(1));
                Assert.That(checker.CallCount, Is.Zero);
                Assert.That(AddressablesEndpointRouting.CurrentMirrorBaseUrl, Is.Null);
                Assert.That(
                    Transform("https://cdn.example.com/content/Dev/Android/catalog.hash"),
                    Is.EqualTo("https://cdn.example.com/content/Prod/Android/catalog.hash"));
            }
            finally
            {
                AddressablesEndpointRouting.Apply(null, null);
            }
        });

        [Test]
        public void RestoreEndpoint_DropsTheMirrorAndUsesTheEndpointBaselineAgain()
        {
            AssertMirrorRestoresBaseline(AddressablesEndpointRouting.RestoreEndpoint);
        }

        [Test]
        public void ApplyMirrorNull_DropsTheMirrorAndUsesTheEndpointBaselineAgain()
        {
            AssertMirrorRestoresBaseline(() => AddressablesEndpointRouting.ApplyMirror((ContentMirror)null));
        }

        [Test]
        public void RouteToEndpoint_RoutesABakedUrlToTheReleaseFolderAndIgnoresTheMirror()
        {
            const string root = "https://cdn.example.com/content";
            const string endpoint = "https://cdn.example.com/content/Prod/";
            try
            {
                AddressablesEndpointRouting.Apply(root, endpoint);
                AddressablesEndpointRouting.ApplyMirror(new ContentMirror("edge", "https://edge.example/"));

                Assert.That(
                    AddressablesEndpointRouting.RouteToEndpoint("https://cdn.example.com/content/Dev/Android/catalog_content.hash"),
                    Is.EqualTo("https://cdn.example.com/content/Prod/Android/catalog_content.hash"));
            }
            finally
            {
                AddressablesEndpointRouting.Apply(null, null);
            }
        }

        [Test]
        public void CatalogReadiness_FindsTheBakedRemoteHashLocation()
        {
            const string settings =
                "{\"m_buildTarget\":\"Android\",\"m_CatalogLocations\":[" +
                "{\"m_Keys\":[\"AddressablesMainContentCatalogRemoteHash\"]," +
                "\"m_InternalId\":\"https://cdn.example.com/VI/Dev/Android/catalog_content.hash\"}," +
                "{\"m_Keys\":[\"AddressablesMainContentCatalog\"]," +
                "\"m_InternalId\":\"{UnityEngine.AddressableAssets.Addressables.RuntimePath}/catalog.bin\"}]}";

            Assert.That(
                RemoteCatalogReadiness.FindRemoteHashUrl(settings),
                Is.EqualTo("https://cdn.example.com/VI/Dev/Android/catalog_content.hash"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{\"m_CatalogLocations\":[{\"m_Keys\":[\"Other\"],\"m_InternalId\":\"https://cdn.example.com/x\"}]}")]
        public void CatalogReadiness_WrongSettingsGiveNoHashLocation(string settings)
        {
            Assert.That(RemoteCatalogReadiness.FindRemoteHashUrl(settings), Is.Null);
        }

        [Test]
        public void CatalogReadiness_MapsTheHashUrlToTheCatalogItDescribes()
        {
            Assert.That(
                RemoteCatalogReadiness.ToCatalogUrl("https://cdn.example.com/VI/Dev/Android/catalog_content.hash?token=x"),
                Is.EqualTo("https://cdn.example.com/VI/Dev/Android/catalog_content.bin?token=x"));
            Assert.That(
                RemoteCatalogReadiness.ToCatalogUrl("https://cdn.example.com/VI/Dev/Android/catalog_content.bin"),
                Is.EqualTo("https://cdn.example.com/VI/Dev/Android/catalog_content.bin"));
        }

        private static void AssertMirrorRestoresBaseline(Action restore)
        {
            const string root = "https://cdn.example.com/content";
            const string endpoint = "https://cdn.example.com/content/Prod/";
            try
            {
                AddressablesEndpointRouting.Apply(root, endpoint);
                AddressablesEndpointRouting.ApplyMirror(new ContentMirror("edge", "https://edge.example/"));
                Assert.That(AddressablesEndpointRouting.CurrentMirrorBaseUrl, Is.Not.Null);

                restore();

                Assert.That(AddressablesEndpointRouting.CurrentMirrorBaseUrl, Is.Null);
                Assert.That(
                    Transform("https://cdn.example.com/content/Dev/Android/catalog.hash"),
                    Is.EqualTo("https://cdn.example.com/content/Prod/Android/catalog.hash"));
            }
            finally
            {
                AddressablesEndpointRouting.Apply(null, null);
            }
        }

        [UnityTest]
        public IEnumerator Reachability_ParallelSuccess_CancelsAndDrainsOtherChecks() => UniTask.ToCoroutine(async () =>
        {
            var first = new ControlledReachabilityChecker();
            var second = new ControlledReachabilityChecker();
            var checker = new AnyContentHostReachabilityChecker(new IContentHostReachabilityChecker[] { first, second }, true);
            var operation = checker.IsReachableAsync();
            Assert.That(first.Started && second.Started, Is.True);
            second.Result.TrySetResult(true);
            Assert.That(await operation, Is.True);
            Assert.That(first.Cancelled, Is.True);
        });

        [UnityTest]
        public IEnumerator Reachability_ParallelCancellation_CancelsAllChecks() => UniTask.ToCoroutine(async () =>
        {
            var first = new ControlledReachabilityChecker();
            var second = new ControlledReachabilityChecker();
            var checker = new AnyContentHostReachabilityChecker(new IContentHostReachabilityChecker[] { first, second }, true);
            using var cancellation = new CancellationTokenSource();
            var operation = checker.IsReachableAsync(cancellation.Token);
            cancellation.Cancel();
            try
            {
                await operation;
                Assert.Fail("Expected cancellation.");
            }
            catch (OperationCanceledException)
            {
                Assert.That(first.Cancelled && second.Cancelled, Is.True);
            }
        });

        [UnityTest]
        public IEnumerator Reachability_Sequential_WaitsForFirstCheck() => UniTask.ToCoroutine(async () =>
        {
            var first = new ControlledReachabilityChecker();
            var second = new ControlledReachabilityChecker();
            var checker = new AnyContentHostReachabilityChecker(new IContentHostReachabilityChecker[] { first, second }, false);
            var operation = checker.IsReachableAsync();
            Assert.That(first.Started, Is.True);
            Assert.That(second.Started, Is.False);
            first.Result.TrySetResult(false);
            Assert.That(second.Started, Is.True);
            second.Result.TrySetResult(true);
            Assert.That(await operation, Is.True);
        });

        [UnityTest]
        public IEnumerator Reachability_ParallelFailures_PropagatesErrorWhenNoneSucceeds() => UniTask.ToCoroutine(async () =>
        {
            var first = new ControlledReachabilityChecker();
            var second = new ControlledReachabilityChecker();
            var checker = new AnyContentHostReachabilityChecker(new IContentHostReachabilityChecker[] { first, second }, true);
            var operation = checker.IsReachableAsync();
            first.Result.TrySetException(new InvalidOperationException("host failure"));
            second.Result.TrySetResult(false);
            try
            {
                await operation;
                Assert.Fail("Expected failure.");
            }
            catch (InvalidOperationException exception)
            {
                Assert.That(exception.Message, Is.EqualTo("host failure"));
            }
        });

        [UnityTest]
        public IEnumerator Reachability_ParallelChecks_ReturnsFalseWhenEveryHostFails() => UniTask.ToCoroutine(async () =>
        {
            var first = new ControlledReachabilityChecker();
            var second = new ControlledReachabilityChecker();
            var checker = new AnyContentHostReachabilityChecker(new IContentHostReachabilityChecker[] { first, second }, true);
            var operation = checker.IsReachableAsync();
            first.Result.TrySetResult(false);
            second.Result.TrySetResult(false);
            Assert.That(await operation, Is.False);
        });

        private sealed class ControlledReachabilityChecker : IContentHostReachabilityChecker
        {
            public readonly UniTaskCompletionSource<bool> Result = new();
            public bool Started { get; private set; }
            public bool Cancelled { get; private set; }

            public async UniTask<bool> IsReachableAsync(CancellationToken cancellationToken = default)
            {
                Started = true;
                try
                {
                    return await Result.Task.AttachExternalCancellation(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Cancelled = true;
                    throw;
                }
            }
        }

        private static string Transform(string internalId)
            => AddressablesApi.InternalIdTransformFunc(
                new ResourceLocationBase("key", internalId, "provider", typeof(object)));

        private static AddressablesContentSynchronizer CreateSynchronizer(
            IContentHostReachabilityChecker checker,
            CountingProbe probe)
            => new(
                checker,
                mirrorCoordinator: new ContentMirrorCoordinator(
                    new ContentMirrorConfiguration(
                        new[] { new ContentMirror("primary", "https://primary.example/") },
                        "mirror-probe.bin",
                        256 * 1024,
                        TimeSpan.FromSeconds(30),
                        TimeSpan.FromMinutes(10)),
                    probe,
                    new EmptyStatisticsStore()));

        private sealed class LegacyReachabilityChecker : IContentHostReachabilityChecker
        {
            private readonly bool _isReachable;

            public LegacyReachabilityChecker(bool isReachable) => _isReachable = isReachable;

            public UniTask<bool> IsReachableAsync(CancellationToken cancellationToken = default)
                => UniTask.FromResult(_isReachable);
        }

        private sealed class StubReachabilityChecker : IContentHostReachabilityChecker, INetworkReachabilityStatusProvider
        {
            private readonly bool _isReachable;

            public int CallCount { get; private set; }

            public bool IsNetworkUnavailable { get; }

            public StubReachabilityChecker(bool isReachable, bool networkUnavailable)
            {
                _isReachable = isReachable;
                IsNetworkUnavailable = networkUnavailable;
            }

            public UniTask<bool> IsReachableAsync(CancellationToken cancellationToken = default)
            {
                CallCount++;
                return UniTask.FromResult(_isReachable);
            }
        }

        private sealed class CountingProbe : IContentMirrorProbe
        {
            public int CallCount { get; private set; }

            public UniTask<ContentMirrorProbeResult> ProbeAsync(
                ContentMirror mirror,
                string relativePath,
                int expectedSizeBytes,
                CancellationToken cancellationToken)
            {
                CallCount++;
                return UniTask.FromResult(new ContentMirrorProbeResult(false, 0d));
            }
        }

        private sealed class EmptyStatisticsStore : IContentMirrorStatisticsStore
        {
            public bool TryGetBytesPerSecond(string mirrorId, DateTimeOffset now, TimeSpan maximumAge, out double bytesPerSecond)
            {
                bytesPerSecond = 0d;
                return false;
            }

            public void SaveBytesPerSecond(string mirrorId, double bytesPerSecond, DateTimeOffset measuredAt) { }
            public void Remove(string mirrorId) { }
        }
    }
}
