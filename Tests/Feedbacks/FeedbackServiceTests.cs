using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Audio;
using Foundation.Feedbacks;
using Foundation.Haptics;
using Foundation.Feedbacks.UI;
using Foundation.UI.UGui;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEditor;
using Object = UnityEngine.Object;
using Foundation.Motion;

namespace Foundation.Feedbacks.Tests
{
    public sealed class FeedbackServiceTests
    {
        public sealed class CountingAction : FeedbackAction
        {
            public int Calls { get; private set; }

            public override UniTask PlayAsync(FeedbackContext context, FeedbackServices services, CancellationToken cancellationToken)
            {
                Calls++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class AudioSpy : IAudioService
        {
            public AudioEventId LastEvent { get; private set; }
            public AudioHandle Play(AudioEventId id) { LastEvent = id; return default; }
            public AudioHandle Play(AudioEventId id, in AudioPlayOptions options) { LastEvent = id; return default; }
            public AudioPlayResult TryPlay(AudioEventId id, in AudioPlayOptions options, out AudioHandle handle)
            { LastEvent = id; handle = default; return default; }
            public void Stop(AudioEventId id, float fadeSeconds = 0f) { }
            public void StopChannel(AudioChannelId channel, float fadeSeconds = 0f) { }
            public AudioPlayResult PlayMusic(AudioEventId id, float fadeInSeconds = 0f) => default;
            public AudioPlayResult CrossFadeMusic(AudioEventId id, float durationSeconds) => default;
            public void StopMusic(AudioChannelId channel, float fadeOutSeconds = 0f) { }
            public UniTask PreloadAsync(IReadOnlyList<AudioEventId> ids, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask PlayAsync(string audioId, string layerId, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask PlayOneShotAsync(string audioId, string layerId, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask StopAsync(string audioId, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask SetVolumeAsync(string layerId, float volume, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask FadeAsync(string layerId, float targetVolume, float durationSeconds, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask CrossFadeAsync(string audioId, string layerId, float durationSeconds, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        }

        private sealed class HapticsSpy : IHapticsService
        {
            public bool IsSupported => true;
            public HapticFeedbackKind? LastKind { get; private set; }
            public void Play(HapticFeedbackKind kind) => LastKind = kind;
        }

        private readonly List<Object> _assets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _assets)
                Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        [Test]
        public async Task CompositeExecutesAudioHapticsAndCustomAction()
        {
            var audio = new AudioSpy();
            var haptics = new HapticsSpy();
            var soundId = AudioEventId.From("ui.click");
            var sound = Track(PlayAudioFeedback.Create(soundId));
            var vibration = Track(HapticFeedbackAction.Create(HapticFeedbackKind.LightTap));
            var custom = Track(ScriptableObject.CreateInstance<CountingAction>());
            var definition = Track(FeedbackDefinition.Create(FeedbackId.From("click"), new[]
            {
                new FeedbackStep(0f, sound), new FeedbackStep(0f, vibration), new FeedbackStep(0f, custom),
            }));
            using var service = new FeedbackService(new[] { definition }, audio, haptics);

            await service.PlayAsync(definition.Id);

            Assert.That(audio.LastEvent, Is.EqualTo(soundId));
            Assert.That(haptics.LastKind, Is.EqualTo(HapticFeedbackKind.LightTap));
            Assert.That(custom.Calls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DelayedActionCanBeCancelledAndIgnorePolicyDoesNotRestart()
        {
            var action = Track(ScriptableObject.CreateInstance<CountingAction>());
            var definition = Track(FeedbackDefinition.Create(FeedbackId.From("delayed"),
                new[] { new FeedbackStep(1f, action) }, FeedbackRepeatPolicy.IgnoreWhilePlaying));
            using var service = new FeedbackService(new[] { definition });

            var first = service.Play(definition.Id);
            var second = service.Play(definition.Id);
            Assert.That(second, Is.SameAs(first));
            Assert.That(action.Calls, Is.Zero);
            first.Cancel();
            for (var frame = 0; frame < 10 && first.IsPlaying; frame++)
                yield return null;
            Assert.That(first.IsPlaying, Is.False);
            Assert.That(action.Calls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DelayedActionExecutesAndRestartCancelsPreviousPlayback()
        {
            var action = Track(ScriptableObject.CreateInstance<CountingAction>());
            var definition = Track(FeedbackDefinition.Create(FeedbackId.From("restart"),
                new[] { new FeedbackStep(0.02f, action) }));
            using var service = new FeedbackService(new[] { definition });
            var first = service.Play(definition.Id);
            var second = service.Play(definition.Id);
            Assert.That(second, Is.Not.SameAs(first));
            var deadline = EditorApplication.timeSinceStartup + 2d;
            while (second.IsPlaying && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(second.IsPlaying, Is.False);
            Assert.That(action.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task InvalidTargetIsIgnoredAndInvalidIdIsRejected()
        {
            var action = Track(ScriptableObject.CreateInstance<CountingAction>());
            var definition = Track(FeedbackDefinition.Create(FeedbackId.From("targeted"), new[] { new FeedbackStep(0f, action) }));
            using var service = new FeedbackService(new[] { definition });
            var target = new GameObject("destroyed");
            var context = FeedbackContext.For(target);
            Object.DestroyImmediate(target);

            await service.PlayAsync(definition.Id, context);

            Assert.Throws<ArgumentException>(() => service.Play(default));
            Assert.Throws<ArgumentException>(() => service.Play(FeedbackId.From("missing")));
        }

        [Test]
        public void FoundationButtonAssemblyDoesNotReferenceFeedbackRuntime()
        {
            var references = typeof(FoundationButton).Assembly.GetReferencedAssemblies().Select(item => item.Name);
            Assert.That(references, Does.Not.Contain("Foundation.Feedbacks"));
        }

        [Test]
        public void ClosingViewDuringClickStillRunsCapturedAsyncFeedbackHandler()
        {
            var gameObject = new GameObject("button");
            var button = gameObject.AddComponent<FoundationButton>();
            var handled = false;
            var handler = button.SubscribeAsync((_, _) =>
            {
                handled = true;
                return UniTask.CompletedTask;
            });
            using var clicked = button.Clicked.Subscribe(_ =>
            {
                handler.Dispose();
                gameObject.SetActive(false);
            });

            button.OnPointerClick(new PointerEventData(null) { button = PointerEventData.InputButton.Left });

            Assert.That(handled, Is.True);
            Object.DestroyImmediate(gameObject);
        }

        // An action that tweens must not animate on a hidden default engine when the scope forgot to register one.
        [Test]
        public void ATweeningAction_WithoutAMotionService_FailsInsteadOfAnimatingOnADefault()
        {
            var withoutMotion = new FeedbackServices(null, null);
            var motion = new PlayerLoopMotionService();
            var withMotion = new FeedbackServices(null, null, motion);

            Assert.Throws<InvalidOperationException>(() => withoutMotion.RequireMotion());
            Assert.That(withMotion.RequireMotion(), Is.SameAs(motion));
        }

        private T Track<T>(T asset) where T : Object
        {
            _assets.Add(asset);
            return asset;
        }
    }
}
