using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.ContentMirroring;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;

namespace Foundation.Infrastructure.YooAsset.PlayModeTests
{
    public sealed class YooAssetContentInitializerTests
    {
        [UnityTest]
        public IEnumerator ReusedPackageRunsTheGateAndRemainsOwnedByItsCreator() => UniTask.ToCoroutine(async () =>
        {
            if (!Application.isEditor)
                Assert.Ignore("The test initializes the external package with the Editor file system.");
            if (YooAssets.IsInitialized)
                Assert.Ignore("The test needs to own the YooAsset instance it creates.");

            YooAssets.Initialize();
            try
            {
                var packageName = $"foundation-test-{Guid.NewGuid():N}";
                var package = YooAssets.CreatePackage(packageName);
                var options = new EditorSimulateModeOptions
                {
                    EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(Application.temporaryCachePath),
                };
                var operation = package.InitializePackageAsync(options);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await UniTask.WaitUntil(() => operation.IsDone, cancellationToken: timeout.Token);
                Assert.That(operation.Status, Is.EqualTo(EOperationStatus.Succeeded), operation.Error);

                foreach (var strategy in new[] { ContentStartupStrategy.Standard, ContentStartupStrategy.OfflineFirst })
                {
                    var settings = new Settings(packageName, strategy);
                    var context = new YooAssetPackageContext();
                    var synchronizer = new YooAssetContentSynchronizer(new EmptyVersionStore(), _ => UniTask.FromResult(true));
                    using var initializer = new YooAssetContentInitializer(settings, context, synchronizer);
                    var gateCalls = 0;
                    UniTask<bool> Stop(CancellationToken token)
                    {
                        gateCalls++;
                        return UniTask.FromResult(false);
                    }

                    for (var i = 0; i < 2; i++)
                    {
                        try
                        {
                            await initializer.InitializeAsync(beforeRemoteUpdate: Stop);
                            Assert.Fail("The gate should stop initialization.");
                        }
                        catch (ContentUpdateStoppedException)
                        {
                        }
                    }

                    Assert.That(gateCalls, Is.EqualTo(1));
                    Assert.That(context.IsInitialized, Is.False);
                    Assert.That(YooAssets.GetPackage(packageName), Is.SameAs(package));
                    Assert.That(package.InitializeStatus, Is.EqualTo(EOperationStatus.Succeeded));
                }

                var sharedContext = new YooAssetPackageContext();
                using var sharedInitializer = new YooAssetContentInitializer(
                    new Settings(packageName, ContentStartupStrategy.Standard), sharedContext);
                var gate = new UniTaskCompletionSource<bool>();
                var sharedGateCalls = 0;
                UniTask<bool> WaitForGate(CancellationToken token)
                {
                    sharedGateCalls++;
                    return gate.Task;
                }

                var first = IsStoppedAsync(sharedInitializer.InitializeAsync(beforeRemoteUpdate: WaitForGate));
                var second = IsStoppedAsync(sharedInitializer.InitializeAsync(beforeRemoteUpdate: WaitForGate));
                using var canceledWait = new CancellationTokenSource();
                var third = sharedInitializer.InitializeAsync(canceledWait.Token, beforeRemoteUpdate: WaitForGate);
                canceledWait.Cancel();
                Assert.That(await third.SuppressCancellationThrow(), Is.True);
                Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Pending));

                gate.TrySetResult(false);
                Assert.That(await first, Is.True);
                Assert.That(await second, Is.True);
                Assert.That(sharedGateCalls, Is.EqualTo(1));
                Assert.That(YooAssets.GetPackage(packageName), Is.SameAs(package));

                var existingContext = new YooAssetPackageContext();
                typeof(YooAssetPackageContext)
                    .GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(existingContext, new object[] { package });
                using var rejectedInitializer = new YooAssetContentInitializer(
                    new Settings(packageName, ContentStartupStrategy.Standard), existingContext);
                Assert.That(await IsStoppedAsync(rejectedInitializer.InitializeAsync(
                    beforeRemoteUpdate: _ => UniTask.FromResult(false))), Is.True);
                Assert.That(existingContext.Package, Is.SameAs(package));

                var disposedContext = new YooAssetPackageContext();
                var disposedInitializer = new YooAssetContentInitializer(
                    new Settings(packageName, ContentStartupStrategy.Standard), disposedContext);
                var pendingGate = new UniTaskCompletionSource<bool>();
                var pending = disposedInitializer.InitializeAsync(beforeRemoteUpdate: _ => pendingGate.Task);
                disposedInitializer.Dispose();
                Assert.That(await pending.SuppressCancellationThrow(), Is.True);
                pendingGate.TrySetResult(true);
                await UniTask.Yield();
                Assert.That(disposedContext.IsInitialized, Is.False);
                Assert.That(YooAssets.GetPackage(packageName), Is.SameAs(package));

                var versionStore = new CountingVersionStore();
                var guardedSynchronizer = new YooAssetContentSynchronizer(versionStore, _ => UniTask.FromResult(false));
                using var guardedInitializer = new YooAssetContentInitializer(
                    new Settings(packageName, ContentStartupStrategy.OfflineFirst, "missing"),
                    new YooAssetPackageContext(), guardedSynchronizer);
                Assert.That(await IsStoppedAsync(guardedInitializer.InitializeAsync(
                    beforeRemoteUpdate: _ => UniTask.FromResult(false))), Is.True);
                Assert.That(versionStore.ReadCount, Is.Zero);
            }
            finally
            {
                YooAssets.Destroy();
            }
        });

        [UnityTest]
        public IEnumerator WhenEveryMirrorAttemptFails_TheRemoteServiceReturnsToThePrimaryUrl() => UniTask.ToCoroutine(async () =>
        {
            if (!Application.isEditor)
                Assert.Ignore("The test initializes the external package with the Editor file system.");
            if (YooAssets.IsInitialized)
                Assert.Ignore("The test needs to own the YooAsset instance it creates.");

            YooAssets.Initialize();
            try
            {
                var packageName = $"foundation-test-{Guid.NewGuid():N}";
                var package = YooAssets.CreatePackage(packageName);
                var appliedMirrors = new List<ContentMirror>();
                var probe = new FailingProbe();
                var coordinator = new ContentMirrorCoordinator(
                    new ContentMirrorConfiguration(
                        new[] { new ContentMirror("primary", "https://primary.example/") },
                        "mirror-probe.bin",
                        256 * 1024,
                        TimeSpan.FromSeconds(30),
                        TimeSpan.FromMinutes(10)),
                    probe,
                    new EmptyStatisticsStore());
                var synchronizer = new YooAssetContentSynchronizer(
                    new EmptyVersionStore(),
                    _ => UniTask.FromResult(true),
                    retryInterval: TimeSpan.FromMilliseconds(1),
                    mirrorCoordinator: coordinator);
                using var cancellation = new CancellationTokenSource();
                synchronizer.StateChanged += state =>
                {
                    if (state == YooAssetContentSynchronizer.SynchronizationState.WaitingForConnection)
                        cancellation.Cancel();
                };

                try
                {
                    await synchronizer.SynchronizeAsync(
                        package,
                        new Settings(packageName, ContentStartupStrategy.OfflineFirst),
                        cancellationToken: cancellation.Token,
                        applyMirror: mirror => appliedMirrors.Add(mirror));
                    Assert.Fail("A failed mirror run should keep waiting, not finish.");
                }
                catch (OperationCanceledException)
                {
                }

                Assert.That(probe.CallCount, Is.EqualTo(1));
                Assert.That(appliedMirrors.Count, Is.EqualTo(1));
                Assert.That(appliedMirrors[0], Is.Null);
            }
            finally
            {
                YooAssets.Destroy();
            }
        });

        [Test]
        public void OfflineFirstCopiesTheConfiguredBuiltinManifest()
        {
            var settings = new Settings("test", ContentStartupStrategy.OfflineFirst, "builtin-v1");
            var synchronizer = new YooAssetContentSynchronizer(new EmptyVersionStore(), _ => UniTask.FromResult(false));
            using var initializer = new YooAssetContentInitializer(settings, new YooAssetPackageContext(), synchronizer);
            var options = (HostPlayModeOptions)typeof(YooAssetContentInitializer)
                .GetMethod("CreateInitializeOptions", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(initializer, null);
            var parameters = (IDictionary)typeof(FileSystemParameters)
                .GetField("_createParameters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(options.BuiltinFileSystemParameters);
            Assert.That(parameters[nameof(EFileSystemParameter.CopyBuiltinPackageManifest)], Is.EqualTo(true));
        }

        private static async UniTask<bool> IsStoppedAsync(UniTask initialization)
        {
            try
            {
                await initialization;
                return false;
            }
            catch (ContentUpdateStoppedException)
            {
                return true;
            }
        }

        private sealed class Settings : IYooAssetRemoteContentSettings
        {
            public string PackageName { get; }
            public bool UseEditorSimulation => false;
            public bool UseOfflinePlayerMode => false;
            public string RemoteUrl => "http://127.0.0.1";
            public string BuiltinPackageVersion { get; }
            public ContentStartupStrategy StartupStrategy { get; }

            public Settings(string packageName, ContentStartupStrategy strategy, string builtinPackageVersion = null)
            {
                PackageName = packageName;
                StartupStrategy = strategy;
                BuiltinPackageVersion = builtinPackageVersion;
            }
        }

        private sealed class EmptyVersionStore : IYooAssetVersionStore
        {
            public string Read(string packageName) => null;
            public void Write(string packageName, string version) { }
        }

        private sealed class FailingProbe : IContentMirrorProbe
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

        private sealed class CountingVersionStore : IYooAssetVersionStore
        {
            public int ReadCount { get; private set; }

            public string Read(string packageName)
            {
                ReadCount++;
                return null;
            }

            public void Write(string packageName, string version) { }
        }

    }
}
