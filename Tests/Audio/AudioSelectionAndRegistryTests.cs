using System;
using System.Collections.Generic;
using Foundation.Audio;
using Foundation.Randomization;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Tests.Audio
{
    public sealed class AudioSelectionAndRegistryTests
    {
        private readonly List<Object> _cleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _cleanup)
                Object.DestroyImmediate(item);

            _cleanup.Clear();
        }

        [Test]
        public void NoImmediateRepeat_NeverRepeatsTheLastClip_ButReachesEveryOther()
        {
            var clips = Clips(3);
            var random = new SystemRandomService(7);
            var last = -1;
            var seen = new HashSet<int>();

            for (var index = 0; index < 300; index++)
            {
                var pick = AudioClipSelector.Pick(AudioSelectionMode.RandomNoImmediateRepeat, clips, last, random);
                Assert.That(pick, Is.Not.EqualTo(last));
                seen.Add(pick);
                last = pick;
            }

            Assert.That(seen, Is.EquivalentTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void NoImmediateRepeat_WithOneClip_RepeatsInsteadOfPlayingNothing()
        {
            Assert.That(AudioClipSelector.Pick(AudioSelectionMode.RandomNoImmediateRepeat, Clips(1), 0, new SystemRandomService(1)), Is.EqualTo(0));
        }

        [Test]
        public void EmptyEntries_AreNeverPicked_InAnyMode()
        {
            var clips = new[] { default(AudioClipEntry), Clip(), default(AudioClipEntry) };
            var random = new SystemRandomService(3);

            foreach (var mode in (AudioSelectionMode[])Enum.GetValues(typeof(AudioSelectionMode)))
            {
                for (var index = 0; index < 20; index++)
                    Assert.That(AudioClipSelector.Pick(mode, clips, -1, random), Is.EqualTo(1), mode.ToString());
            }

            Assert.That(AudioClipSelector.Pick(AudioSelectionMode.Random, new AudioClipEntry[0], -1, random), Is.EqualTo(-1));
            Assert.That(AudioClipSelector.Pick(AudioSelectionMode.Single, new[] { default(AudioClipEntry) }, -1, random), Is.EqualTo(-1));
        }

        [TestCase(0.00, 0)]
        [TestCase(0.24, 0)]
        [TestCase(0.26, 1)]
        [TestCase(0.99, 1)]
        public void WeightedRandom_FollowsTheWeights_AndSkipsZeroWeightClips(double roll, int expected)
        {
            var clips = new[] { Clip(1f), Clip(3f), new AudioClipEntry(clip: null, weight: 0f) };

            Assert.That(AudioClipSelector.Pick(AudioSelectionMode.WeightedRandom, clips, -1, new FixedRandom(roll)), Is.EqualTo(expected));
        }

        [Test]
        public void Registry_RejectsDuplicateIds_AcrossLibraries_AndRegistersNothingFromAFailingLibrary()
        {
            var registry = new AudioEventRegistry();
            var first = Library("ui.click", "ui.hover");
            var clashing = Library("ui.open", "ui.click", "ui.close");
            registry.Register(first);

            var exception = Assert.Throws<InvalidOperationException>(() => registry.Register(clashing));

            Assert.That(exception.Message, Does.Contain("ui.click"));
            Assert.That(registry.Contains(AudioEventId.From("ui.open")), Is.False, "a failed library must not leave a half registered state");
            Assert.That(registry.Count, Is.EqualTo(2));
        }

        [Test]
        public void Registry_UnregisterRemovesOnlyThatLibrarysEvents_AndFreesTheIds()
        {
            var registry = new AudioEventRegistry();
            var level = Library("level.start");
            var global = Library("ui.click");
            registry.Register(level);
            registry.Register(global);

            registry.Unregister(level);

            Assert.That(registry.Contains(AudioEventId.From("level.start")), Is.False);
            Assert.That(registry.Contains(AudioEventId.From("ui.click")), Is.True);
            Assert.DoesNotThrow(() => registry.Register(Library("level.start")));
        }

        [Test]
        public void Registry_RejectsEventsWithoutAnId()
        {
            var registry = new AudioEventRegistry();

            Assert.Throws<ArgumentException>(() => registry.Register(new AudioEventDefinition()));
        }

        private AudioClipEntry[] Clips(int count)
        {
            var clips = new AudioClipEntry[count];
            for (var index = 0; index < count; index++)
                clips[index] = Clip();

            return clips;
        }

        private AudioClipEntry Clip(float weight = 1f)
        {
            var clip = AudioClip.Create("c", 100, 1, 44100, false);
            _cleanup.Add(clip);
            return new AudioClipEntry(clip, weight);
        }

        private AudioEventLibrary Library(params string[] ids)
        {
            var definitions = new List<AudioEventDefinition>();
            foreach (var id in ids)
                definitions.Add(new AudioEventDefinition(AudioEventId.From(id), AudioChannelId.From("ui"), new[] { Clip() }));

            var library = AudioEventLibrary.Create(definitions);
            _cleanup.Add(library);
            return library;
        }

        private sealed class FixedRandom : IRandomService
        {
            private readonly double _value;

            public FixedRandom(double value)
            {
                _value = value;
            }

            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;

            public float NextFloat(float minInclusive, float maxExclusive) => minInclusive;

            public double NextDouble() => _value;

            public bool NextBool() => false;

            public void Shuffle<T>(IList<T> values)
            {
            }

            public T NextWeighted<T>(IReadOnlyList<WeightedItem<T>> items) => items[0].Value;
        }
    }
}
