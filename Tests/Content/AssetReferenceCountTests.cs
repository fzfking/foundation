using Foundation.Content;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests.Content
{
    public class LoadedAssetRegistryTests
    {
        private static readonly LoadedAssetRegistry.Key Planet = new("art/planet", typeof(Texture2D));
        private static readonly LoadedAssetRegistry.Key Moon = new("art/moon", typeof(Texture2D));

        private LoadedAssetRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new();
        }

        [Test]
        public void EntryStaysLoadedUntilItsLastReferenceIsReleased()
        {
            Load(Planet, references: 1);

            Assert.That(_registry.AddRef(Planet), Is.EqualTo(2));
            Assert.That(_registry.ReleaseRef(Planet), Is.EqualTo(1));
            Assert.That(_registry.TryGetLoaded(Planet, out _), Is.True);

            Assert.That(_registry.ReleaseRef(Planet), Is.EqualTo(0));
            Assert.That(_registry.TryGetLoaded(Planet, out _), Is.False);
            Assert.That(_registry.LoadedCount, Is.EqualTo(0));
        }

        [Test]
        public void UnknownAsset_ReportsNegativeCount_ForBothAddAndRelease()
        {
            Assert.That(_registry.AddRef(Planet), Is.EqualTo(-1));
            Assert.That(_registry.ReleaseRef(Planet), Is.EqualTo(-1));
        }

        [Test]
        public void DifferentAssets_DoNotAffectEachOther()
        {
            Load(Planet, references: 1);
            Load(Moon, references: 1);

            _registry.ReleaseRef(Planet);

            Assert.That(_registry.TryGetLoaded(Planet, out _), Is.False);
            Assert.That(_registry.GetRefCount(Moon), Is.EqualTo(1));
        }

        [Test]
        public void CompletingALoad_ClearsItsPendingMarker_AndRegistersTheEntry()
        {
            var completion = _registry.JoinPending(Planet, out _);
            Assert.That(_registry.PendingCount, Is.EqualTo(1));

            _registry.CompletePending(Planet, completion, NewEntry());

            Assert.That(_registry.PendingCount, Is.EqualTo(0));
            Assert.That(_registry.LoadedCount, Is.EqualTo(1));
        }

        [Test]
        public void Clear_RemovesAllLoadedEntries()
        {
            Load(Planet, references: 1);
            Load(Moon, references: 2);

            _registry.Clear();

            Assert.That(_registry.LoadedCount, Is.EqualTo(0));
            Assert.That(_registry.GetAllEntries(), Is.Empty);
        }

        // Loads an asset the way a provider does: callers reserve it, then the load completes with those references.
        private void Load(LoadedAssetRegistry.Key key, int references)
        {
            var completion = _registry.JoinPending(key, out _);
            for (var index = 1; index < references; index++)
                _registry.JoinPending(key, out _);

            _registry.CompletePending(key, completion, NewEntry());
        }

        private static LoadedAssetRegistry.Entry NewEntry() => new() { Asset = new object(), Handle = new object() };
    }
}
