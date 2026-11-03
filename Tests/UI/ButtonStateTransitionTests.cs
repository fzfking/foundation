using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Motion;
using Foundation.UI.UGui;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Cysharp.Threading.Tasks;

namespace Foundation.Tests.UI
{
    public sealed class ButtonStateTransitionTests
    {
        // A button without states reports an empty state id, which is what the strategies are applied for here.
        private const string StateId = "";

        private GameObject _gameObject;
        private FoundationButton _button;
        private FoundationButtonStrategyContext _context;
        private SteppedMotion _motion;

        [SetUp]
        public void CreateButton()
        {
            _gameObject = new GameObject("button");
            _button = _gameObject.AddComponent<FoundationButton>();
            _context = new FoundationButtonStrategyContext(_button);
            _motion = new SteppedMotion();
        }

        [TearDown]
        public void DestroyButton()
        {
            _context.Dispose();
            Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void TheFirstStateAButtonShows_IsAppliedAtOnce_WithoutNeedingAMotionService()
        {
            var selected = ScaleStrategy(Vector3.one * 1.2f, duration: 0.2f);

            selected.ApplyAsync(_context, StateId, CancellationToken.None);

            Assert.That(_button.transform.localScale.x, Is.EqualTo(1.2f).Within(1e-5f));
        }

        // Regression: a spawned button applies its state in Awake, before its scope (and motion service) is bound; a
        // tweening strategy threw there for want of a motion service.
        [Test]
        public void AChangeBeforeTheScopeIsBound_AppliesAtOnce_InsteadOfFailingForWantOfAMotionService()
        {
            ScaleStrategy(Vector3.one, duration: 0.2f).ApplyAsync(_context, StateId, CancellationToken.None);

            Assert.DoesNotThrow(() => ScaleStrategy(Vector3.one * 1.2f, duration: 0.2f).ApplyAsync(_context, StateId, CancellationToken.None));

            Assert.That(_button.transform.localScale.x, Is.EqualTo(1.2f).Within(1e-5f));
        }

        [Test]
        public void ALaterScaleChange_TweensFromTheCurrentScale()
        {
            _context.SetMotion(_motion);
            ScaleStrategy(Vector3.one * 1.2f, duration: 0.2f).ApplyAsync(_context, StateId, CancellationToken.None);

            ScaleStrategy(Vector3.one, duration: 0.2f).ApplyAsync(_context, StateId, CancellationToken.None);
            Assert.That(_button.transform.localScale.x, Is.EqualTo(1.2f).Within(1e-5f), "nothing jumps before the tween runs");

            _motion.Step(0.5f);
            Assert.That(_button.transform.localScale.x, Is.EqualTo(1.1f).Within(1e-5f));
            _motion.Step(1f);
            Assert.That(_button.transform.localScale.x, Is.EqualTo(1f).Within(1e-5f));
        }

        // A tab clicked twice in a row must continue from where the first change got to, and the first tween must stop.
        [Test]
        public void ANewerScaleState_ReplacesTheRunningTween_FromWhereItGot()
        {
            _context.SetMotion(_motion);
            ScaleStrategy(Vector3.one, duration: 0.2f).ApplyAsync(_context, StateId, CancellationToken.None);
            ScaleStrategy(Vector3.one * 1.2f, duration: 0.2f).ApplyAsync(_context, StateId, CancellationToken.None);
            _motion.Step(0.5f);

            ScaleStrategy(Vector3.one, duration: 0.2f).ApplyAsync(_context, StateId, CancellationToken.None);

            Assert.That(_motion.Started[0].Cancelled, Is.True);
            _motion.Step(1f);
            Assert.That(_button.transform.localScale.x, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void ZeroDuration_AppliesTheChangeAtOnce_EvenWithoutAMotionService()
        {
            ScaleStrategy(Vector3.one, duration: 0f).ApplyAsync(_context, StateId, CancellationToken.None);

            ScaleStrategy(Vector3.one * 1.2f, duration: 0f).ApplyAsync(_context, StateId, CancellationToken.None);

            Assert.That(_button.transform.localScale.x, Is.EqualTo(1.2f).Within(1e-5f));
        }

        [Test]
        public void ColorChange_TweensEveryGraphicFromItsOwnColor_AndEndsExactlyOnTheTarget()
        {
            var image = _gameObject.AddComponent<Image>();
            _context.SetMotion(_motion);
            ColorStrategy(Color.white, duration: 0.2f).ApplyAsync(_context, StateId, CancellationToken.None);
            Assert.That(image.color, Is.EqualTo(Color.white));

            ColorStrategy(Color.black, duration: 0.2f).ApplyAsync(_context, StateId, CancellationToken.None);
            _motion.Step(0.5f);
            Assert.That(image.color.r, Is.EqualTo(0.5f).Within(1e-5f));
            _motion.Step(1f);

            Assert.That(image.color, Is.EqualTo(Color.black));
        }

        private static FoundationButtonStateScaleStrategy ScaleStrategy(Vector3 scale, float duration)
        {
            var strategy = ScriptableObject.CreateInstance<FoundationButtonStateScaleStrategy>();
            var serialized = new SerializedObject(strategy);
            serialized.FindProperty("scale").vector3Value = scale;
            serialized.FindProperty("duration").floatValue = duration;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return strategy;
        }

        private static FoundationButtonStateColorStrategy ColorStrategy(Color color, float duration)
        {
            var strategy = ScriptableObject.CreateInstance<FoundationButtonStateColorStrategy>();
            var serialized = new SerializedObject(strategy);
            serialized.FindProperty("color").colorValue = color;
            serialized.FindProperty("duration").floatValue = duration;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return strategy;
        }

        // Plays no time by itself: the test says how far the newest tween has got.
        private sealed class SteppedMotion : IMotionService
        {
            public List<Tween> Started { get; } = new();

            public IMotionHandle Animate<TState>(float from, float to, float duration, MotionEase ease, TState state, Action<float, TState> onUpdate, MotionLoop loop = default, CancellationToken lifetime = default)
                where TState : class
            {
                var tween = new Tween(progress => onUpdate(Mathf.Lerp(from, to, progress), state));
                Started.Add(tween);
                return tween;
            }

            public void Step(float progress)
            {
                var newest = Started[Started.Count - 1];
                if (!newest.Cancelled)
                    newest.Apply(progress);
            }

            public sealed class Tween : IMotionHandle
            {
                private readonly Action<float> _apply;

                public Tween(Action<float> apply)
                {
                    _apply = apply;
                }

                public bool Cancelled { get; private set; }

                public bool IsActive => !Cancelled;

                public void Apply(float progress) => _apply(progress);

                public void Cancel() => Cancelled = true;

                public UniTask WaitAsync(CancellationToken cancellationToken = default)
                    => UniTask.CompletedTask;
            }
        }
    }
}
