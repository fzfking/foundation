using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.ContentMirroring;
using Foundation.Infrastructure.HybridClr;
using NUnit.Framework;
using Object = UnityEngine.Object;
using UnityEngine;

namespace Foundation.Tests.HybridClr
{
    public sealed class MirroredHotUpdateAssemblySourceTests
    {
        [Test]
        public void LoadVerifiedAsync_RetriesOnlyTheInvalidAddressOnAnotherMirror()
        {
            var mirrors = CreateMirrors();
            var provider = new FakeAssetProvider((mirrorId, address) =>
                address == "invalid" && mirrorId == "current" ? "tampered" : "signed")
            {
                CurrentMirrorId = "current"
            };
            var clearedAddresses = new List<string>();
            var source = CreateSource(mirrors, provider, clearedAddresses);

            var existingBytes = source.LoadVerifiedAsync("valid").GetAwaiter().GetResult();
            var recoveredBytes = source.LoadVerifiedAsync("invalid").GetAwaiter().GetResult();

            Assert.That(existingBytes, Is.EqualTo(FakeVerifier.Payload));
            Assert.That(recoveredBytes, Is.EqualTo(FakeVerifier.Payload));
            Assert.That(provider.Loads, Is.EqualTo(new[]
            {
                ("current", "valid"),
                ("current", "invalid"),
                ("backup", "invalid")
            }));
            Assert.That(clearedAddresses, Is.EqualTo(new[] { "invalid" }));
            Assert.That(provider.DisposedAssetCount, Is.EqualTo(3));
        }

        [Test]
        public void LoadVerifiedAsync_WhenEveryMirrorHasAnInvalidSignature_FailsClosed()
        {
            var mirrors = CreateMirrors(includeFinal: true);
            var provider = new FakeAssetProvider((_, _) => "tampered")
            {
                CurrentMirrorId = "current"
            };
            var clearedAddresses = new List<string>();
            var appliedMirrors = new List<ContentMirror>();
            var source = CreateSource(mirrors, provider, clearedAddresses, appliedMirrors);

            Assert.Throws<HotUpdateSignatureException>(() =>
                source.LoadVerifiedAsync("invalid").GetAwaiter().GetResult());

            Assert.That(provider.Loads, Is.EqualTo(new[]
            {
                ("current", "invalid"),
                ("backup", "invalid"),
                ("final", "invalid")
            }));
            Assert.That(clearedAddresses, Is.EqualTo(new[] { "invalid", "invalid" }));
            Assert.That(provider.DisposedAssetCount, Is.EqualTo(3));
            Assert.That(appliedMirrors.Count, Is.EqualTo(3));
            Assert.That(appliedMirrors[appliedMirrors.Count - 1], Is.Null);
        }

        private static MirroredHotUpdateAssemblySource CreateSource(
            IReadOnlyList<ContentMirror> mirrors,
            FakeAssetProvider provider,
            List<string> clearedAddresses,
            List<ContentMirror> appliedMirrors = null)
        {
            var statistics = new MemoryStatisticsStore();
            var now = DateTimeOffset.UtcNow;
            statistics.SaveBytesPerSecond("current", 4000d, now);
            statistics.SaveBytesPerSecond("backup", 2000d, now);
            statistics.SaveBytesPerSecond("final", 1000d, now);
            var coordinator = new ContentMirrorCoordinator(
                new ContentMirrorConfiguration(
                    mirrors,
                    "mirror-probe.bin",
                    256 * 1024,
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromMinutes(10)),
                new SuccessfulProbe(),
                statistics);

            return new MirroredHotUpdateAssemblySource(
                provider,
                new FakeVerifier(),
                coordinator,
                mirror =>
                {
                    appliedMirrors?.Add(mirror);
                    if (mirror != null)
                        provider.CurrentMirrorId = mirror.Id;
                },
                () => provider.GetBaseUrl(provider.CurrentMirrorId),
                (address, _) =>
                {
                    clearedAddresses.Add(address);
                    return UniTask.FromResult(true);
                },
                (_, _) => UniTask.FromResult(4096L));
        }

        private static IReadOnlyList<ContentMirror> CreateMirrors(bool includeFinal = false)
        {
            var mirrors = new List<ContentMirror>
            {
                new("current", "https://current.example/"),
                new("backup", "https://backup.example/")
            };
            if (includeFinal)
                mirrors.Add(new ContentMirror("final", "https://final.example/"));
            return mirrors;
        }

        private sealed class FakeAssetProvider : IAssetProvider
        {
            private readonly Func<string, string, string> _getText;
            private readonly Dictionary<string, string> _baseUrls = new()
            {
                ["current"] = "https://current.example/",
                ["backup"] = "https://backup.example/",
                ["final"] = "https://final.example/"
            };

            public string CurrentMirrorId { get; set; }
            public List<(string MirrorId, string Address)> Loads { get; } = new();
            public int DisposedAssetCount { get; private set; }

            public FakeAssetProvider(Func<string, string, string> getText)
            {
                _getText = getText;
            }

            public string GetBaseUrl(string mirrorId) => _baseUrls[mirrorId];

            public UniTask<IAssetHandle<T>> LoadAssetAsync<T>(
                string assetId,
                CancellationToken cancellationToken = default)
                where T : Object
            {
                cancellationToken.ThrowIfCancellationRequested();
                Loads.Add((CurrentMirrorId, assetId));
                var asset = new TextAsset(_getText(CurrentMirrorId, assetId));
                return UniTask.FromResult((IAssetHandle<T>)(object)new TestAssetHandle(asset, () =>
                {
                    DisposedAssetCount++;
                    Object.DestroyImmediate(asset);
                }));
            }

            public UniTask<IAssetInstance> InstantiateAsync(
                string assetId,
                Transform parent = null,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public bool ContainsAsset<T>(string assetId) where T : Object => false;

            public UniTask<IPreloadHandle> PreloadAsync(
                IReadOnlyList<string> assetIds,
                IProgress<float> progress = null,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }

        private sealed class TestAssetHandle : IAssetHandle<TextAsset>
        {
            private readonly Action _onDispose;

            public TextAsset Asset { get; }

            public TestAssetHandle(TextAsset asset, Action onDispose)
            {
                Asset = asset;
                _onDispose = onDispose;
            }

            public void Dispose() => _onDispose();
        }

        private sealed class FakeVerifier : IHotUpdateAssemblyVerifier
        {
            public static readonly byte[] Payload = { 0x4D, 0x5A };

            public byte[] Verify(string address, byte[] signedBytes)
            {
                if (Encoding.UTF8.GetString(signedBytes) != "signed")
                    throw new HotUpdateSignatureException($"Invalid signature for '{address}'.");

                return Payload;
            }
        }

        private sealed class SuccessfulProbe : IContentMirrorProbe
        {
            public UniTask<ContentMirrorProbeResult> ProbeAsync(
                ContentMirror mirror,
                string relativePath,
                int expectedSizeBytes,
                CancellationToken cancellationToken)
                => UniTask.FromResult(new ContentMirrorProbeResult(
                    true,
                    mirror.Id == "backup" ? 2000d : 1000d));
        }

        private sealed class MemoryStatisticsStore : IContentMirrorStatisticsStore
        {
            private readonly Dictionary<string, double> _speeds = new();

            public bool TryGetBytesPerSecond(
                string mirrorId,
                DateTimeOffset now,
                TimeSpan maximumAge,
                out double bytesPerSecond)
                => _speeds.TryGetValue(mirrorId, out bytesPerSecond);

            public void SaveBytesPerSecond(string mirrorId, double bytesPerSecond, DateTimeOffset measuredAt)
                => _speeds[mirrorId] = bytesPerSecond;

            public void Remove(string mirrorId) => _speeds.Remove(mirrorId);
        }
    }
}
