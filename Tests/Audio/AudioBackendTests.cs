using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Foundation.Audio;
using Foundation.Content;
using Foundation.Pooling;
using Foundation.Randomization;
using Foundation.Tests.Support;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Foundation.Tests.Audio
{
    public sealed class AudioBackendTests
    {
        private readonly List<Object> _cleanup = new();
        private readonly List<IDisposable> _disposables = new();

        [TearDown]
        public void TearDown()
        {
            for (var index = _disposables.Count - 1; index >= 0; index--)
                _disposables[index].Dispose();

            _disposables.Clear();
            foreach (var item in _cleanup)
            {
                if (item != null)
                    Object.DestroyImmediate(item);
            }

            _cleanup.Clear();
        }

        [Test]
        public void RepeatedPlays_ReuseSourcesFromThePools_WithoutCreatingOrDestroyingAny()
        {
            var env = new Env(this);
            env.Define("click", false, false);
            env.Pools.TryGet(AudioPoolIds.Playback2D, out var pool2D);
            var createdBefore = pool2D.Stats.Created;

            for (var index = 0; index < 50; index++)
            {
                env.Engine.TryPlay(AudioEventId.From("click"), default, out var handle);
                handle.Stop();
            }

            Assert.That(pool2D.Stats.Created, Is.EqualTo(createdBefore), "no AudioSource may be created per play");
            Assert.That(pool2D.Stats.Destroyed, Is.EqualTo(0));
            Assert.That(pool2D.Stats.Active, Is.EqualTo(0), "stopped playbacks go back to the pool");
            Assert.That(pool2D.Stats.RentCount, Is.EqualTo(50));
        }

        [Test]
        public void PlaybackKinds_UseDifferentPools_SoLoopsAndSpatialSoundsNeverStarveOneShots()
        {
            var env = new Env(this);
            env.Define("click", false, false);
            env.Define("engine", true, false);
            env.Define("boom", false, true);
            env.Pools.TryGet(AudioPoolIds.Playback2D, out var pool2D);
            env.Pools.TryGet(AudioPoolIds.Playback3D, out var pool3D);
            env.Pools.TryGet(AudioPoolIds.PlaybackLoop, out var poolLoop);

            env.Engine.TryPlay(AudioEventId.From("click"), default, out _);
            env.Engine.TryPlay(AudioEventId.From("engine"), default, out _);
            env.Engine.TryPlay(AudioEventId.From("boom"), AudioPlayOptions.At(Vector3.one), out _);

            Assert.That((pool2D.Stats.Active, pool3D.Stats.Active, poolLoop.Stats.Active), Is.EqualTo((1, 1, 1)));
        }

        [Test]
        public void FollowingSound_TracksItsTransform_WithoutAnyPerPlaybackUpdate()
        {
            var env = new Env(this);
            env.Define("hum", true, true);
            var target = new GameObject("target");
            _cleanup.Add(target);
            env.Engine.TryPlay(AudioEventId.From("hum"), AudioPlayOptions.Following(target.transform), out _);
            var source = FindActivePlaybackSource(env.Backend.PlaybackRoot);

            target.transform.position = new Vector3(4, 5, 6);
            env.Engine.Tick();

            Assert.That(source.transform.position, Is.EqualTo(new Vector3(4, 5, 6)));
        }

        [UnityTest]
        public IEnumerator LegacyOneShot_KeepsItsAssetHandleUntilThePlaybackStops()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var env = new Env(this);
                var provider = new FakeClipProvider(Make(1f));
                var legacy = new UnityAudioPlaybackBackend(provider, env.Engine, env.Settings);

                await legacy.PlayOneShotAsync("sfx/coin", "ui", CancellationToken.None);
                Assert.That(env.Engine.GetStats().ActivePlaybacks, Is.EqualTo(1));
                Assert.That(provider.HandlesDisposed, Is.EqualTo(0), "the clip must stay loaded while it plays");

                env.Engine.StopAll();

                Assert.That(provider.HandlesDisposed, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator LegacyTrack_ReplacesThePreviousOne_AndReleasesItsHandle()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var env = new Env(this);
                var provider = new FakeClipProvider(Make(10f));
                var legacy = new UnityAudioPlaybackBackend(provider, env.Engine, env.Settings);

                await legacy.PlayAsync("music/a", "music", CancellationToken.None);
                await legacy.PlayAsync("music/b", "music", CancellationToken.None);

                Assert.That(provider.HandlesDisposed, Is.EqualTo(1), "the replaced track's clip is released");
                Assert.That(env.Engine.GetTrackKey(AudioChannelId.From("music")), Is.EqualTo("music/b"));
                await legacy.StopAsync("music/b", CancellationToken.None);
                Assert.That(provider.HandlesDisposed, Is.EqualTo(2));
            });
        }

        [UnityTest]
        public IEnumerator LegacyTrack_LoadFinishingAfterEngineDisposal_ReleasesItsUntransferredHandle()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var env = new Env(this);
                var provider = new FakeClipProvider(Make(10f)) { HoldLoads = true };
                var legacy = new UnityAudioPlaybackBackend(provider, env.Engine, env.Settings);
                var playback = legacy.PlayAsync("music/late", "music", CancellationToken.None);

                await UniTask.Yield();
                env.Engine.Dispose();
                provider.CompleteLoad();

                Exception failure = null;
                try
                {
                    await playback;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                Assert.That(failure, Is.TypeOf<ObjectDisposedException>());
                Assert.That(provider.HandlesDisposed, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator LegacyCrossFade_LoadFinishingAfterEngineDisposal_ReleasesItsUntransferredHandle()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var env = new Env(this);
                var provider = new FakeClipProvider(Make(10f)) { HoldLoads = true };
                var legacy = new UnityAudioPlaybackBackend(provider, env.Engine, env.Settings);
                var playback = legacy.CrossFadeAsync("music/late", "music", 1f, CancellationToken.None);

                await UniTask.Yield();
                env.Engine.Dispose();
                provider.CompleteLoad();

                Exception failure = null;
                try
                {
                    await playback;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                Assert.That(failure, Is.TypeOf<ObjectDisposedException>());
                Assert.That(provider.HandlesDisposed, Is.EqualTo(1));
            });
        }

        [Test]
        public void LegacyApi_ToAnUnknownChannel_FailsClearly()
        {
            var env = new Env(this);
            var legacy = new UnityAudioPlaybackBackend(new FakeClipProvider(Make(1f)), env.Engine, env.Settings);

            Assert.Throws<InvalidOperationException>(() => legacy.SetVolumeAsync("nowhere", 0.5f, CancellationToken.None).GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator ClipCache_LoadsOnceInTheBackground_AndReleasesOnUnload()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var provider = new FakeClipProvider(Make(1f));
                var cache = new AudioClipCache(provider);
                _disposables.Add(cache);
                var entry = new AudioClipEntry("clips/boom");

                Assert.That(cache.TryGetClip(in entry, out _), Is.False, "a clip that is not resident is rejected, never awaited");
                Assert.That(cache.TryGetClip(in entry, out _), Is.False);
                await UniTask.Yield();
                await UniTask.Yield();

                Assert.That(cache.TryGetClip(in entry, out var clip), Is.True);
                Assert.That(clip, Is.Not.Null);
                Assert.That(provider.LoadCount, Is.EqualTo(1), "two early requests share one load");

                cache.Unload("clips/boom");
                Assert.That(provider.HandlesDisposed, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator ClipCache_PreloadMakesEventClipsAvailableForTheFirstPlay()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var provider = new FakeClipProvider(Make(1f));
                var cache = new AudioClipCache(provider);
                _disposables.Add(cache);
                var entries = new List<AudioClipEntry> { new AudioClipEntry("a"), new AudioClipEntry("a"), new AudioClipEntry("b") };

                await cache.PreloadAsync(entries);

                Assert.That(provider.LoadCount, Is.EqualTo(2), "duplicates are loaded once");
                var a = entries[0];
                Assert.That(cache.TryGetClip(in a, out _), Is.True);
            });
        }

        [UnityTest]
        public IEnumerator ClipCache_PreloadPropagatesLoadFailures()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var provider = new FakeClipProvider(null) { Fail = true };
                var cache = new AudioClipCache(provider);
                _disposables.Add(cache);
                var entries = new List<AudioClipEntry> { new AudioClipEntry("broken") };
                using var log = new LogCapture();

                Exception failure = null;
                try
                {
                    await cache.PreloadAsync(entries);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                Assert.That(failure, Is.TypeOf<InvalidOperationException>());
                Assert.That(failure?.Message, Is.EqualTo("no such asset"));
                Assert.That(provider.LoadCount, Is.EqualTo(1));
                Assert.That(log.Contains(LogType.Warning, "broken"), Is.True);
            });
        }

        [UnityTest]
        public IEnumerator ClipCache_SuccessfulPreloadRetryClearsTheFailedState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var provider = new FakeClipProvider(Make(1f)) { Fail = true };
                var cache = new AudioClipCache(provider);
                _disposables.Add(cache);
                var entry = new AudioClipEntry("recovering");
                using var log = new LogCapture();

                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();
                await UniTask.Yield();
                provider.Fail = false;
                await cache.PreloadAsync(new[] { entry });

                cache.Unload("recovering");
                Assert.That(cache.TryGetClip(in entry, out _), Is.False);
                Assert.That(provider.LoadCount, Is.EqualTo(3), "a successful explicit retry allows a later reload");
                Assert.That(log.Contains(LogType.Warning, "recovering"), Is.True);
            });
        }

        [UnityTest]
        public IEnumerator ClipCache_ADisposedCacheReleasesLateLoads_AndAFailedLoadIsNotRetriedForever()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var failing = new FakeClipProvider(Make(1f)) { Fail = true };
                var cache = new AudioClipCache(failing);
                _disposables.Add(cache);
                var entry = new AudioClipEntry("broken");
                using var log = new LogCapture();

                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();
                await UniTask.Yield();
                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();

                Assert.That(failing.LoadCount, Is.EqualTo(1));
                Assert.That(log.Contains(LogType.Warning, "broken"), Is.True, "the failed load is reported");
            });
        }

        [UnityTest]
        public IEnumerator ClipCache_DefaultRetryIsOff_EvenWhenTheClockAdvances()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeAudioClock();
                var provider = new FakeClipProvider(Make(1f)) { Fail = true };
                var cache = new AudioClipCache(provider, clock: clock);
                _disposables.Add(cache);
                var entry = new AudioClipEntry("retry-off");

                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();
                await UniTask.Yield();
                provider.Fail = false;
                clock.Now = 100d;

                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();
                await UniTask.Yield();

                Assert.That(provider.LoadCount, Is.EqualTo(1), "without a configured delay a failed clip is not retried");
            });
        }

        [UnityTest]
        public IEnumerator ClipCache_AutoRetryWaitsForTheDelay_ThenRecovers()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeAudioClock();
                var provider = new FakeClipProvider(Make(1f)) { Fail = true };
                var cache = new AudioClipCache(provider, retryDelaySeconds: 2d, clock: clock);
                _disposables.Add(cache);
                var entry = new AudioClipEntry("delayed-retry");

                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();
                await UniTask.Yield();
                provider.Fail = false;

                clock.Now = 1.99d;
                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();
                await UniTask.Yield();
                Assert.That(provider.LoadCount, Is.EqualTo(1), "the retry waits for the delay");

                clock.Now = 2d;
                Assert.That(cache.TryGetClip(in entry, out _), Is.False, "the retry request itself is rejected");
                await UniTask.Yield();
                await UniTask.Yield();

                Assert.That(provider.LoadCount, Is.EqualTo(2));
                Assert.That(cache.TryGetClip(in entry, out var clip), Is.True);
                Assert.That(clip, Is.Not.Null);
            });
        }

        [UnityTest]
        public IEnumerator ClipCache_AnInFlightRetryIsNotStartedAgain()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeAudioClock();
                var provider = new FakeClipProvider(Make(1f)) { HoldLoads = true, Fail = true };
                var cache = new AudioClipCache(provider, retryDelaySeconds: 1d, clock: clock);
                _disposables.Add(cache);
                var entry = new AudioClipEntry("held-retry");

                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();
                provider.CompleteLoad();
                await UniTask.Yield();
                await UniTask.Yield();

                clock.Now = 1d;
                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();
                clock.Now = 2d;
                cache.TryGetClip(in entry, out _);
                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();

                Assert.That(provider.LoadCount, Is.EqualTo(2), "later playback attempts share the running retry");
                provider.CompleteLoad();
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator ClipCache_DisposeStopsRetryAndReleasesAnInFlightRetry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeAudioClock();
                var provider = new FakeClipProvider(Make(1f)) { Fail = true };
                var cache = new AudioClipCache(provider, retryDelaySeconds: 1d, clock: clock);
                _disposables.Add(cache);
                var entry = new AudioClipEntry("disposed-retry");

                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();
                await UniTask.Yield();

                provider.Fail = false;
                provider.HoldLoads = true;
                clock.Now = 1d;
                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();

                cache.Dispose();
                provider.CompleteLoad();
                await UniTask.Yield();
                await UniTask.Yield();

                Assert.That(provider.LoadCount, Is.EqualTo(2));
                Assert.That(provider.HandlesDisposed, Is.EqualTo(1), "a retry that finishes after disposal releases its handle");
                Assert.That(cache.TryGetClip(in entry, out _), Is.False);

                clock.Now = 100d;
                cache.TryGetClip(in entry, out _);
                await UniTask.Yield();
                Assert.That(provider.LoadCount, Is.EqualTo(2), "disposal stops automatic retries");
            });
        }

        private AudioClip Make(float seconds)
        {
            var clip = AudioClip.Create("test", Mathf.Max(1, (int)(seconds * 44100)), 1, 44100, false);
            _cleanup.Add(clip);
            return clip;
        }

        private static AudioSource FindActivePlaybackSource(Transform playbackRoot)
        {
            foreach (var source in playbackRoot.GetComponentsInChildren<AudioSource>())
            {
                if (source.gameObject.name == "PlaybackLoop")
                    return source;
            }

            throw new InvalidOperationException("No active loop playback source was found.");
        }

        private sealed class FakeAudioClock : IAudioClock
        {
            public double Now { get; set; }
        }

        private sealed class FakeClipProvider : IAssetProvider
        {
            public bool ContainsAsset<T>(string assetId) where T : Object
                => throw new NotSupportedException();

            private readonly AudioClip _clip;

            public FakeClipProvider(AudioClip clip)
            {
                _clip = clip;
            }

            public int LoadCount { get; private set; }

            public int HandlesDisposed { get; private set; }

            public bool Fail { get; set; }

            public bool HoldLoads { get; set; }

            private UniTaskCompletionSource _loadCompletion;

            public void CompleteLoad()
            {
                _loadCompletion?.TrySetResult();
                _loadCompletion = null;
            }

            public async UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string assetId, CancellationToken cancellationToken = default)
                where T : Object
            {
                LoadCount++;
                if (HoldLoads)
                {
                    _loadCompletion ??= new UniTaskCompletionSource();
                    await _loadCompletion.Task;
                }
                else
                {
                    await UniTask.Yield();
                }

                if (Fail)
                    throw new InvalidOperationException("no such asset");

                return new Handle<T>((T)(Object)_clip, () => HandlesDisposed++);
            }

            public UniTask<IAssetInstance> InstantiateAsync(string assetId, Transform parent = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public UniTask<IPreloadHandle> PreloadAsync(IReadOnlyList<string> assetIds, IProgress<float> progress = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public void Dispose()
            {
            }

            private sealed class Handle<T> : IAssetHandle<T>
                where T : Object
            {
                private readonly Action _onDispose;
                private bool _disposed;

                public Handle(T asset, Action onDispose)
                {
                    Asset = asset;
                    _onDispose = onDispose;
                }

                public T Asset { get; }

                public void Dispose()
                {
                    if (_disposed)
                        return;

                    _disposed = true;
                    _onDispose();
                }
            }
        }

        private sealed class Env
        {
            private readonly AudioBackendTests _owner;
            private readonly AudioClip _clip;

            public Env(AudioBackendTests owner)
            {
                _owner = owner;
                _clip = owner.Make(0.5f);
                Pools = new(null);
                owner._disposables.Add(Pools);
                var backend = new UnityAudioBackend(Pools, NullAudioMixerRouting.Instance);
                owner._disposables.Add(backend);
                Backend = backend;
                var channels = new IAudioChannelDefinition[]
                {
                    new AudioChannelDefinition("ui", maxOneShotCount: 64),
                    new AudioChannelDefinition("music", loop: true),
                };
                Settings = new(channels);
                Registry = new();
                Engine = new(backend, channels, Settings, Registry, new DirectAudioClipResolver(), new SystemRandomService(5));
                owner._disposables.Add(Engine);
            }

            public PoolService Pools { get; }

            public UnityAudioBackend Backend { get; }

            public AudioSettingsState Settings { get; }

            public AudioEventRegistry Registry { get; }

            public AudioEngine Engine { get; }

            public void Define(string id, bool loop, bool spatial)
                => Registry.Register(new AudioEventDefinition(
                    AudioEventId.From(id), AudioChannelId.From("ui"), new[] { new AudioClipEntry(_clip) },
                    loop: loop, spatial: new AudioSpatialSettings(spatial)));
        }
    }
}
