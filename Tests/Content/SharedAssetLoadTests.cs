using Foundation.Content;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests.Content
{
    // The provider shares one Addressables load between callers. These tests pin the reference bookkeeping behind that:
    // a caller that stops waiting must not leave the asset loaded, and must not take the asset away from the others.
    public sealed class SharedAssetLoadTests
    {
        private const string Address = "art/planet";

        private static readonly LoadedAssetRegistry.Key Planet = new(Address, typeof(Texture2D));

        [Test]
        public void JoinPending_FirstCallerStartsTheLoad_LaterCallersJoinIt()
        {
            var registry = new LoadedAssetRegistry();

            var first = registry.JoinPending(Planet, out var firstCreated);
            var second = registry.JoinPending(Planet, out var secondCreated);

            Assert.That(firstCreated, Is.True);
            Assert.That(secondCreated, Is.False);
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void CompletePending_WithTwoCallersStillWaiting_RegistersTwoReferences()
        {
            var registry = new LoadedAssetRegistry();
            var completion = registry.JoinPending(Planet, out _);
            registry.JoinPending(Planet, out _);

            var registered = registry.CompletePending(Planet, completion, NewEntry());

            Assert.That(registered, Is.True);
            Assert.That(registry.GetRefCount(Planet), Is.EqualTo(2));
        }

        [Test]
        public void CallerLeavesWhileTheLoadRuns_TheOtherCallerStillGetsTheAsset()
        {
            var registry = new LoadedAssetRegistry();
            var completion = registry.JoinPending(Planet, out _);
            registry.JoinPending(Planet, out _);

            Assert.That(registry.AbandonReservation(Planet, completion), Is.Null);
            Assert.That(registry.CompletePending(Planet, completion, NewEntry()), Is.True);
            Assert.That(registry.GetRefCount(Planet), Is.EqualTo(1));
        }

        [Test]
        public void EveryCallerLeftBeforeTheLoadEnded_NothingStaysLoaded()
        {
            var registry = new LoadedAssetRegistry();
            var completion = registry.JoinPending(Planet, out _);
            registry.AbandonReservation(Planet, completion);

            Assert.That(registry.CompletePending(Planet, completion, NewEntry()), Is.False);
            Assert.That(registry.LoadedCount, Is.Zero);
        }

        [Test]
        public void CallerLeavesAfterTheLoadEnded_ReleasesItsReference_AndReturnsTheEntryWhenItWasTheLast()
        {
            var registry = new LoadedAssetRegistry();
            var completion = registry.JoinPending(Planet, out _);
            var entry = NewEntry();
            registry.CompletePending(Planet, completion, entry);
            completion.TrySetResult();

            var released = registry.AbandonReservation(Planet, completion);

            Assert.That(released, Is.SameAs(entry));
            Assert.That(registry.LoadedCount, Is.Zero);
        }

        [Test]
        public void FailedLoad_DropsItsReservations_AndTheNextLoadStartsFresh()
        {
            var registry = new LoadedAssetRegistry();
            var failed = registry.JoinPending(Planet, out _);
            registry.JoinPending(Planet, out _);
            registry.FailPending(Planet, failed);

            var retry = registry.JoinPending(Planet, out var created);
            registry.CompletePending(Planet, retry, NewEntry());

            Assert.That(created, Is.True);
            Assert.That(registry.GetRefCount(Planet), Is.EqualTo(1));
        }

        [Test]
        public void SameAddressAsAnotherType_IsNotServedByTheLoadedEntry_AndReleasesIndependently()
        {
            var registry = new LoadedAssetRegistry();
            var sheet = new LoadedAssetRegistry.Key(Address, typeof(Texture2D));
            var sprite = new LoadedAssetRegistry.Key(Address, typeof(Sprite));
            registry.CompletePending(sheet, registry.JoinPending(sheet, out _), NewEntry());

            Assert.That(registry.TryGetLoaded(sprite, out _), Is.False, "a loaded sheet does not answer a sprite request");

            var spriteCompletion = registry.JoinPending(sprite, out var created);
            registry.CompletePending(sprite, spriteCompletion, NewEntry());

            Assert.That(created, Is.True);
            registry.ReleaseRef(sprite);
            Assert.That(registry.TryGetLoaded(sheet, out _), Is.True, "releasing the sprite leaves the sheet loaded");
        }

        private static LoadedAssetRegistry.Entry NewEntry() => new() { Asset = new object(), Handle = new object() };
    }
}
