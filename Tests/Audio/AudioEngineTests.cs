using System;
using System.Collections.Generic;
using Foundation.Audio;
using Foundation.Randomization;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Tests.Audio
{
    public sealed class AudioEngineTests
    {
        private static readonly AudioChannelId Ui = AudioChannelId.From("ui");
        private static readonly AudioChannelId Sfx = AudioChannelId.From("sfx");
        private static readonly AudioChannelId Music = AudioChannelId.From("music");
        private static readonly AudioChannelId Voice = AudioChannelId.From("voice");

        private readonly List<Object> _cleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _cleanup)
            {
                if (item != null)
                    Object.DestroyImmediate(item);
            }

            _cleanup.Clear();
        }

        [Test]
        public void UnknownIds_ReportWhyNothingPlayed_AndDoNotThrow()
        {
            var env = new Env(this);
            env.Define("known", "missing.channel");

            Assert.That(env.Engine.TryPlay(AudioEventId.From("nope"), default, out var handle), Is.EqualTo(AudioPlayResult.UnknownEvent));
            Assert.That(handle.IsPlaying, Is.False);
            Assert.That(env.Engine.TryPlay(default, default, out _), Is.EqualTo(AudioPlayResult.UnknownEvent), "the empty id is not an event");
            Assert.That(env.Engine.TryPlay(AudioEventId.From("known"), default, out _), Is.EqualTo(AudioPlayResult.UnknownChannel));
            Assert.That(env.Backend.Starts, Is.Empty);
        }

        [Test]
        public void Cooldown_RejectsRapidRepeats_AndMeasuresFromTheLastStartedPlay()
        {
            var env = new Env(this);
            env.Define("click", Ui, cooldown: 0.5f, maxPlaybacks: 1);

            Assert.That(env.Play("click"), Is.EqualTo(AudioPlayResult.Started));
            env.Clock.Now = 0.4;
            Assert.That(env.Play("click"), Is.EqualTo(AudioPlayResult.Cooldown));
            env.Clock.Now = 0.6; // 0.6 since the start; the rejected request at 0.4 must not have restarted the cooldown
            Assert.That(env.Play("click"), Is.EqualTo(AudioPlayResult.EventPlaybackLimit), "cooldown is over; the one allowed playback is still busy");
            Assert.That(env.Engine.GetStats().CooldownRejects, Is.EqualTo(1));
        }

        [Test]
        public void EventPlaybackLimit_IgnoreNew_KeepsTheExistingPlaybacks()
        {
            var env = new Env(this);
            env.Define("hit", Sfx, maxPlaybacks: 2);

            Assert.That(env.Play("hit"), Is.EqualTo(AudioPlayResult.Started));
            Assert.That(env.Play("hit"), Is.EqualTo(AudioPlayResult.Started));
            Assert.That(env.Play("hit"), Is.EqualTo(AudioPlayResult.EventPlaybackLimit));

            Assert.That(env.Backend.Starts, Has.Count.EqualTo(2));
            Assert.That(env.Engine.GetStats().ActivePlaybacks, Is.EqualTo(2));
        }

        [Test]
        public void EventPlaybackLimit_StopOldest_ReplacesTheLongestPlayingPlayback()
        {
            var env = new Env(this);
            env.Define("hit", Sfx, maxPlaybacks: 2, overflow: AudioPlaybackOverflowPolicy.StopOldest);
            env.Engine.TryPlay(AudioEventId.From("hit"), default, out var first);
            env.Clock.Now = 1;
            env.Engine.TryPlay(AudioEventId.From("hit"), default, out var second);
            env.Clock.Now = 2;

            env.Engine.TryPlay(AudioEventId.From("hit"), default, out var third);

            Assert.That(first.IsPlaying, Is.False);
            Assert.That(second.IsPlaying, Is.True);
            Assert.That(third.IsPlaying, Is.True);
            Assert.That(env.Engine.GetStats().ActivePlaybacks, Is.EqualTo(2));
            Assert.That(env.Engine.GetStats().Steals, Is.EqualTo(1));
        }

        [Test]
        public void FullChannel_StealsTheLowestPriorityPlayback_OrRejectsAWeakerSound()
        {
            var env = new Env(this); // ui channel holds 2 playbacks
            env.Define("ambient.tick", Ui, priority: 10);
            env.Define("notification", Ui, priority: 100);
            env.Define("minor", Ui, priority: 5);
            env.Engine.TryPlay(AudioEventId.From("ambient.tick"), default, out var tick);
            env.Clock.Now = 1;
            env.Engine.TryPlay(AudioEventId.From("notification"), default, out var notification);

            Assert.That(env.Play("minor"), Is.EqualTo(AudioPlayResult.ChannelFull), "nothing in the channel is weaker than 5");
            Assert.That(tick.IsPlaying && notification.IsPlaying, Is.True, "a rejected sound must not stop anything");

            env.Engine.TryPlay(AudioEventId.From("notification"), default, out var second);

            Assert.That(tick.IsPlaying, Is.False, "the low priority playback made room");
            Assert.That(notification.IsPlaying && second.IsPlaying, Is.True);
        }

        [Test]
        public void SamePriority_StealsTheOldest()
        {
            var env = new Env(this);
            env.Define("pop", Ui);
            env.Engine.TryPlay(AudioEventId.From("pop"), default, out var oldest);
            env.Clock.Now = 1;
            env.Engine.TryPlay(AudioEventId.From("pop"), default, out var newer);
            env.Clock.Now = 2;

            env.Engine.TryPlay(AudioEventId.From("pop"), default, out var latest);

            Assert.That(oldest.IsPlaying, Is.False);
            Assert.That(newer.IsPlaying && latest.IsPlaying, Is.True);
        }

        [Test]
        public void ARejectedSound_NeverStopsOtherPlaybacks()
        {
            var env = new Env(this);
            env.Define("strong", Ui, priority: 200);
            env.Define("weak", Ui, priority: 1, maxPlaybacks: 1, overflow: AudioPlaybackOverflowPolicy.StopOldest);
            env.Engine.TryPlay(AudioEventId.From("strong"), default, out var a);
            env.Engine.TryPlay(AudioEventId.From("strong"), default, out var b);

            // The channel is full of stronger sounds, so "weak" is dropped and nothing else is touched.
            Assert.That(env.Play("weak"), Is.EqualTo(AudioPlayResult.ChannelFull));
            Assert.That(a.IsPlaying && b.IsPlaying, Is.True);
        }

        [Test]
        public void EngineWidePlaybackLimit_AppliesAcrossChannels()
        {
            var env = new Env(this, maxPlaybacks: 2);
            env.Define("a", Sfx, priority: 50);
            env.Define("b", Voice, priority: 150);
            env.Engine.TryPlay(AudioEventId.From("a"), default, out var a);
            env.Clock.Now = 1;
            env.Engine.TryPlay(AudioEventId.From("a"), default, out var a2);
            env.Clock.Now = 2;

            Assert.That(env.Play("a"), Is.EqualTo(AudioPlayResult.Started), "equal priority steals the oldest");
            Assert.That(a.IsPlaying, Is.False);
            env.Define("c", Sfx, priority: 10);
            Assert.That(env.Play("c"), Is.EqualTo(AudioPlayResult.Saturated));
            Assert.That(env.Play("b"), Is.EqualTo(AudioPlayResult.Started), "a more important sound steals across channels");
            Assert.That(env.Engine.GetStats().ActivePlaybacks, Is.EqualTo(2));
            Assert.That(a2.IsPlaying, Is.False, "the lowest priority, oldest playback made room");
        }

        [Test]
        public void ChosenVolumeAndPitch_StayInsideTheEventRanges_AndCombineWithChannelVolume()
        {
            var env = new Env(this);
            env.Define("whoosh", Sfx, volume: new Vector2(0.5f, 0.8f), pitch: new Vector2(0.9f, 1.1f));
            env.Settings.SetMasterVolume(0.5f);
            env.Settings.SetChannelVolume("sfx", 0.5f);
            env.Engine.Tick();

            for (var index = 0; index < 20; index++)
                env.Engine.TryPlay(AudioEventId.From("whoosh"), default, out var handle);

            foreach (var start in env.Backend.Starts)
            {
                Assert.That(start.Pitch, Is.InRange(0.9f, 1.1f));
                Assert.That(start.Volume, Is.InRange(0.5f * 0.5f, 0.8f * 0.5f), "event volume x channel volume (master is applied by the listener)");
            }

            Assert.That(env.Backend.MasterVolume, Is.EqualTo(0.5f));
        }

        [Test]
        public void ChannelVolumeAndMute_ApplyToPlayingPlaybacksImmediately_AndMuteBlocksNewOnes()
        {
            var env = new Env(this);
            env.Define("loop", Sfx, loop: true);
            env.Engine.TryPlay(AudioEventId.From("loop"), default, out var handle);
            var slot = env.Backend.LastSlot;
            Assert.That(env.Backend.Volumes[slot], Is.EqualTo(1f));

            env.Settings.SetChannelVolume("sfx", 0.25f);
            env.Engine.Tick();
            Assert.That(env.Backend.Volumes[slot], Is.EqualTo(0.25f).Within(1e-4f));

            env.Settings.SetChannelMuted("sfx", true);
            env.Engine.Tick();
            Assert.That(env.Backend.Volumes[slot], Is.EqualTo(0f));
            Assert.That(env.Play("loop"), Is.EqualTo(AudioPlayResult.Silenced));
            Assert.That(handle.IsPlaying, Is.True, "muting silences, it does not end playbacks");

            env.Settings.SetChannelMuted("sfx", false);
            env.Engine.Tick();
            Assert.That(env.Backend.Volumes[slot], Is.EqualTo(0.25f).Within(1e-4f));
        }

        [Test]
        public void Playback_EndsWhenItsClipFinished_ReleasingItsAssetLease()
        {
            var env = new Env(this);
            var lease = new Lease();
            env.Engine.TryPlayClip(env.Clip(1f), Ui, lease, out var handle);
            var slot = env.Backend.LastSlot;

            env.Clock.Now = 0.5;
            env.Engine.Tick();
            Assert.That(handle.IsPlaying && !lease.Disposed, Is.True, "still within the clip");

            env.Clock.Now = 2; // past the clip, but the backend still plays (for example paused by the OS)
            env.Engine.Tick();
            Assert.That(handle.IsPlaying, Is.True);

            env.Backend.Playing[slot] = false;
            env.Engine.Tick();
            Assert.That(handle.IsPlaying, Is.False);
            Assert.That(lease.Disposed, Is.True);
            Assert.That(env.Engine.GetStats().ActivePlaybacks, Is.EqualTo(0));
        }

        [Test]
        public void RejectedClip_ReleasesTheLeaseItWasHanded()
        {
            var env = new Env(this);
            env.Settings.SetChannelMuted("ui", true);
            env.Engine.Tick();
            var lease = new Lease();

            var result = env.Engine.TryPlayClip(env.Clip(1f), Ui, lease, out _);

            Assert.That(result, Is.EqualTo(AudioPlayResult.Silenced));
            Assert.That(lease.Disposed, Is.True, "a one-shot that never played must not keep its asset pinned");
        }

        [Test]
        public void StaleHandle_CannotAffectAPlaybackThatReusedItsSlot()
        {
            var env = new Env(this, maxPlaybacks: 1);
            env.Define("a", Ui);
            env.Engine.TryPlay(AudioEventId.From("a"), default, out var first);
            first.Stop();
            env.Engine.TryPlay(AudioEventId.From("a"), default, out var second);
            Assert.That(env.Backend.StartSlots, Is.EqualTo(new[] { 0, 0 }), "both playbacks used the same slot");

            first.Stop();
            first.SetVolume(0f);

            Assert.That(second.IsPlaying, Is.True);
            Assert.That(env.Backend.Volumes[0], Is.EqualTo(1f));
        }

        [Test]
        public void FadeOut_LowersTheVolumeOverTime_ThenStopsThePlayback()
        {
            var env = new Env(this);
            env.Define("loop", Sfx, loop: true);
            env.Engine.TryPlay(AudioEventId.From("loop"), default, out var handle);
            var slot = env.Backend.LastSlot;

            handle.FadeOut(2f);
            env.Clock.Now = 1;
            env.Engine.Tick();
            Assert.That(env.Backend.Volumes[slot], Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(handle.IsPlaying, Is.True);

            env.Clock.Now = 2.1;
            env.Engine.Tick();
            Assert.That(handle.IsPlaying, Is.False);
            Assert.That(env.Backend.Playing[slot], Is.False);
        }

        [Test]
        public void Ducking_TurnsTheTargetDownWhileTheTriggerPlays_AndRestoresItAfterwards()
        {
            var env = new Env(this, duck: new AudioDuckingRule(Voice, Music, targetGain: 0.5f, attackSeconds: 1f, releaseSeconds: 2f));
            env.Define("line", Voice);
            env.Define("theme", Music, loop: true);
            env.Engine.PlayMusic(AudioEventId.From("theme"));
            Assert.That(env.Backend.TrackVolumes[0], Is.EqualTo(1f));

            env.Engine.TryPlay(AudioEventId.From("line"), default, out var line);
            env.Advance(0.5);
            Assert.That(env.Backend.TrackVolumes[0], Is.EqualTo(0.75f).Within(1e-3f), "halfway through the attack");
            env.Advance(1);
            Assert.That(env.Backend.TrackVolumes[0], Is.EqualTo(0.5f).Within(1e-3f));

            line.Stop();
            env.Advance(1); // one second into a two second release
            Assert.That(env.Backend.TrackVolumes[0], Is.EqualTo(0.75f).Within(1e-3f));
            env.Advance(2);
            Assert.That(env.Backend.TrackVolumes[0], Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Ducking_RuleWithUnknownChannel_FailsAtStartup()
        {
            Assert.Throws<InvalidOperationException>(() => new Env(this, duck: new AudioDuckingRule(Voice, AudioChannelId.From("ghost"))));
        }

        [Test]
        public void CrossFade_RunsBothTracksUntilTheOldOneHasFadedOut()
        {
            var env = new Env(this);
            env.Define("day", Music, loop: true);
            env.Define("night", Music, loop: true);
            env.Engine.PlayMusic(AudioEventId.From("day"));

            Assert.That(env.Engine.CrossFadeMusic(AudioEventId.From("night"), 2f), Is.EqualTo(AudioPlayResult.Started));
            env.Advance(1);

            Assert.That(env.Backend.TrackVolumes[0], Is.EqualTo(0.5f).Within(1e-3f), "old track fading out");
            Assert.That(env.Backend.TrackVolumes[1], Is.EqualTo(0.5f).Within(1e-3f), "new track fading in");
            Assert.That(env.Engine.GetTrackKey(Music), Is.EqualTo("night"));

            env.Advance(1.1);
            Assert.That(env.Backend.TrackPlaying[0], Is.False);
            Assert.That(env.Backend.TrackPlaying[1], Is.True);
            Assert.That(env.Backend.TrackVolumes[1], Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void CrossFadeInterrupted_CutsTheOlderOutgoingTrackAndKeepsAtMostTwo()
        {
            var env = new Env(this);
            env.Define("a", Music, loop: true);
            env.Define("b", Music, loop: true);
            env.Define("c", Music, loop: true);
            env.Engine.PlayMusic(AudioEventId.From("a"));
            env.Engine.CrossFadeMusic(AudioEventId.From("b"), 4f);
            env.Clock.Now = 1;

            env.Engine.CrossFadeMusic(AudioEventId.From("c"), 4f);

            Assert.That(env.Engine.GetStats().ActiveTracks, Is.EqualTo(2));
            Assert.That(env.Engine.GetTrackKey(Music), Is.EqualTo("c"));
        }

        [Test]
        public void StopMusic_WithAFade_KeepsPlayingUntilTheFadeEnds()
        {
            var env = new Env(this);
            env.Define("theme", Music, loop: true);
            env.Engine.PlayMusic(AudioEventId.From("theme"));

            env.Engine.StopMusic(Music, 1f);
            env.Advance(0.5);
            Assert.That(env.Engine.IsTrackPlaying(Music), Is.True);

            env.Advance(0.7);
            Assert.That(env.Engine.GetStats().ActiveTracks, Is.EqualTo(0));
            Assert.That(env.Backend.TrackPlaying[0], Is.False);
        }

        [Test]
        public void LegacyTrack_IsReplacedByTheNextOne_AndItsLeaseReleased()
        {
            var env = new Env(this);
            var first = new Lease();
            var second = new Lease();
            env.Engine.PlayTrack(Music, env.Clip(10f), "first", first, loop: true);

            env.Engine.PlayTrack(Music, env.Clip(10f), "second", second, loop: true);

            Assert.That(first.Disposed, Is.True);
            Assert.That(second.Disposed, Is.False);
            env.Engine.StopTracks("second");
            Assert.That(second.Disposed, Is.True);
            Assert.That(env.Engine.IsTrackPlaying(Music), Is.False);
        }

        [Test]
        public void ChannelFade_ScalesEverythingOnTheChannelWithoutTouchingTheVolumeSetting()
        {
            var env = new Env(this);
            env.Define("loop", Sfx, loop: true);
            env.Engine.TryPlay(AudioEventId.From("loop"), default, out _);
            var slot = env.Backend.LastSlot;

            env.Engine.FadeChannel(Sfx, 0f, 2f);
            env.Clock.Now = 1;
            env.Engine.Tick();

            Assert.That(env.Backend.Volumes[slot], Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(env.Settings.GetChannelVolume("sfx"), Is.EqualTo(1f));
        }

        [Test]
        public void ClipThatIsNotResident_IsRejectedWithoutSpendingTheCooldownOrTheSelection()
        {
            var env = new Env(this);
            env.Define("late", Ui, cooldown: 10f, resident: false);

            Assert.That(env.Play("late"), Is.EqualTo(AudioPlayResult.ClipNotLoaded));
            env.Resolver.Resident = true;

            Assert.That(env.Play("late"), Is.EqualTo(AudioPlayResult.Started), "the failed attempt must not have started the cooldown");
            Assert.That(env.Engine.GetStats().ClipNotLoadedRejects, Is.EqualTo(1));
        }

        [Test]
        public void SpatialEvent_UsesThePositionOnlyWhenOneWasGiven()
        {
            var env = new Env(this);
            env.Define("boom", Sfx, spatial: new AudioSpatialSettings(true, 1f, 2f, 40f));

            env.Engine.TryPlay(AudioEventId.From("boom"), AudioPlayOptions.At(new Vector3(3, 0, 4)), out _);
            env.Engine.TryPlay(AudioEventId.From("boom"), default, out _);

            Assert.That(env.Backend.Starts[0].IsSpatial, Is.True);
            Assert.That(env.Backend.Starts[0].Position, Is.EqualTo(new Vector3(3, 0, 4)));
            Assert.That(env.Backend.Starts[0].MaxDistance, Is.EqualTo(40f));
            Assert.That(env.Backend.Starts[1].IsSpatial, Is.False, "no position: it plays as an ordinary 2D sound instead of at the origin");
        }

        [Test]
        public void BackendRefusal_LeavesNoGhostPlaybackBehind()
        {
            var env = new Env(this);
            env.Define("a", Ui);
            env.Backend.AcceptStarts = false;
            var lease = new Lease();

            var result = env.Engine.TryPlayClip(env.Clip(1f), Ui, lease, out var handle);

            Assert.That(result, Is.EqualTo(AudioPlayResult.BackendRejected));
            Assert.That(handle.IsPlaying, Is.False);
            Assert.That(lease.Disposed, Is.True);
            Assert.That(env.Engine.GetStats().ActivePlaybacks, Is.EqualTo(0));
        }

        [Test]
        public void Requests_AreCountedPerSecond()
        {
            var env = new Env(this);
            env.Define("a", Sfx);
            for (var index = 0; index < 5; index++)
                env.Play("a");

            env.Clock.Now = 1.1;
            env.Engine.Tick();

            Assert.That(env.Engine.GetStats().RequestsLastSecond, Is.EqualTo(5));
            env.Clock.Now = 2.3;
            env.Engine.Tick();
            Assert.That(env.Engine.GetStats().RequestsLastSecond, Is.EqualTo(0));
        }

        [Test]
        public void Dispose_StopsEverythingAndReleasesLeases()
        {
            var env = new Env(this);
            var lease = new Lease();
            env.Engine.TryPlayClip(env.Clip(5f), Ui, lease, out var handle);
            env.Define("theme", Music, loop: true);
            env.Engine.PlayMusic(AudioEventId.From("theme"));

            env.Engine.Dispose();

            Assert.That(handle.IsPlaying, Is.False);
            Assert.That(lease.Disposed, Is.True);
            Assert.That(env.Backend.TrackPlaying[0], Is.False);
            Assert.That(env.Engine.TryPlay(AudioEventId.From("theme"), default, out _), Is.EqualTo(AudioPlayResult.Disposed));
            Assert.Throws<ObjectDisposedException>(() => env.Engine.PlayTrack(Music, env.Clip(1f), "x", null, false));
        }

        private sealed class Lease : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }

        private sealed class FakeClock : IAudioClock
        {
            public double Now { get; set; }
        }

        private sealed class FakeResolver : IAudioClipResolver
        {
            public bool Resident = true;

            public bool TryGetClip(in AudioClipEntry entry, out AudioClip clip)
            {
                clip = entry.Clip;
                return Resident && clip != null;
            }

            public UniTask PreloadAsync(IReadOnlyList<AudioClipEntry> entries, CancellationToken cancellationToken = default)
                => UniTask.CompletedTask;
        }

        private sealed class FakeBackend : IAudioBackend
        {
            public readonly List<AudioPlaybackStart> Starts = new();
            public readonly List<int> StartSlots = new();
            public bool[] Playing = new bool[0];
            public float[] Volumes = new float[0];
            public bool[] TrackPlaying = new bool[16];
            public float[] TrackVolumes = new float[16];
            public bool AcceptStarts = true;
            public float MasterVolume = 1f;
            public int LastSlot = -1;

            public void Configure(IReadOnlyList<IAudioChannelDefinition> channels, int maxPlaybacks)
            {
                Playing = new bool[maxPlaybacks];
                Volumes = new float[maxPlaybacks];
            }

            public bool StartPlayback(int slot, in AudioPlaybackStart start)
            {
                if (!AcceptStarts)
                    return false;

                Starts.Add(start);
                StartSlots.Add(slot);
                Playing[slot] = true;
                Volumes[slot] = start.Volume;
                LastSlot = slot;
                return true;
            }

            public void StopPlayback(int slot) => Playing[slot] = false;

            public void SetPlaybackVolume(int slot, float volume) => Volumes[slot] = volume;

            public void SetPlaybackPitch(int slot, float pitch)
            {
            }

            public bool IsSlotPlaying(int slot) => Playing[slot];

            public void StartTrack(int channelIndex, int slot, AudioClip clip, float volume, float pitch, bool loop)
            {
                TrackPlaying[slot] = true;
                TrackVolumes[slot] = volume;
            }

            public void StopTrack(int channelIndex, int slot) => TrackPlaying[slot] = false;

            public void SetTrackVolume(int channelIndex, int slot, float volume) => TrackVolumes[slot] = volume;

            public bool IsTrackPlaying(int channelIndex, int slot) => TrackPlaying[slot];

            public void SetMasterVolume(float volume) => MasterVolume = volume;

            public void Tick()
            {
            }
        }

        private sealed class Env
        {
            private readonly AudioEngineTests _owner;
            private readonly AudioClip _clip;

            public Env(AudioEngineTests owner, int maxPlaybacks = 32, AudioDuckingRule? duck = null)
            {
                _owner = owner;
                _clip = Clip(0.5f);
                Backend = new();
                Clock = new();
                Resolver = new();
                Registry = new();
                var channels = new IAudioChannelDefinition[]
                {
                    new AudioChannelDefinition("ui", maxOneShotCount: 2),
                    new AudioChannelDefinition("sfx", maxOneShotCount: 8),
                    new AudioChannelDefinition("music", loop: true, maxOneShotCount: 1),
                    new AudioChannelDefinition("voice", maxOneShotCount: 4),
                };
                Settings = new(channels);
                var rules = duck.HasValue ? new[] { duck.Value } : null;
                Engine = new(
                    Backend, channels, Settings, Registry, Resolver, new SystemRandomService(1234), Clock, rules,
                    new AudioEngineSettings { MaxPlaybacks = maxPlaybacks });
            }

            public FakeBackend Backend { get; }

            public FakeClock Clock { get; }

            public FakeResolver Resolver { get; }

            public AudioEventRegistry Registry { get; }

            public AudioSettingsState Settings { get; }

            public AudioEngine Engine { get; }

            public void Advance(double seconds, double step = 0.05)
            {
                var target = Clock.Now + seconds;
                while (Clock.Now < target - 1e-9)
                {
                    Clock.Now = Math.Min(target, Clock.Now + step);
                    Engine.Tick();
                }
            }

            public AudioClip Clip(float seconds)
            {
                var clip = AudioClip.Create("test", Mathf.Max(1, (int)(seconds * 44100)), 1, 44100, false);
                _owner._cleanup.Add(clip);
                return clip;
            }

            public AudioPlayResult Play(string id)
            {
                var result = Engine.TryPlay(AudioEventId.From(id), default, out _);
                return result;
            }

            public void Define(
                string id,
                AudioChannelId channel,
                float cooldown = 0f,
                int maxPlaybacks = 0,
                AudioPlaybackOverflowPolicy overflow = AudioPlaybackOverflowPolicy.IgnoreNew,
                int priority = 128,
                bool loop = false,
                Vector2? volume = null,
                Vector2? pitch = null,
                AudioSpatialSettings spatial = default,
                bool resident = true)
            {
                Resolver.Resident = resident;
                var v = volume ?? Vector2.one;
                var p = pitch ?? Vector2.one;
                Registry.Register(new AudioEventDefinition(
                    AudioEventId.From(id), channel, new[] { new AudioClipEntry(_clip) },
                    volumeMin: v.x, volumeMax: v.y, pitchMin: p.x, pitchMax: p.y,
                    cooldownSeconds: cooldown, maxPlaybacks: maxPlaybacks, overflowPolicy: overflow,
                    priority: priority, loop: loop, spatial: spatial));
            }

            public void Define(string id, string channel) => Define(id, AudioChannelId.From(channel));
        }
    }
}
