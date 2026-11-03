using System;
using System.Collections;
using System.Threading;
using Foundation.Release;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Foundation.Tests.Release
{
    public sealed class ReleaseConfigProviderTests
    {
        private const string ServerJson = "{\"schema\":1,\"client\":{\"minimum\":\"0.9.21\"},\"environments\":[]}";
        private const string CachedJson = "{\"schema\":1,\"client\":{\"minimum\":\"0.9.1\"},\"environments\":[]}";

        private static readonly ReleaseConfig BuiltIn = new ReleaseConfig
        {
            schema = ReleaseConfig.SupportedSchema,
            client = new ClientPolicy { minimum = "" },
            environments = Array.Empty<ReleaseEnvironment>(),
        };

        [UnityTest]
        public IEnumerator ServerConfig_IsUsed_AndSavedForTheNextOfflineStart()
            => ServerScenario().ToCoroutine();

        [UnityTest]
        public IEnumerator UnreachableServer_FallsBackToTheCachedConfig()
            => CachedScenario().ToCoroutine();

        [UnityTest]
        public IEnumerator InvalidServerAnswer_IsIgnored_AndNeverCached()
            => InvalidAnswerScenario().ToCoroutine();

        [UnityTest]
        public IEnumerator NoServerAndNoCache_UsesTheBuiltInConfig()
            => BuiltInScenario().ToCoroutine();

        private static async UniTask ServerScenario()
        {
            var cache = new MemoryCache();

            var result = await Provider(new FakeSource(ServerJson), cache).LoadAsync();

            Assert.That(result.Origin, Is.EqualTo(ReleaseConfigOrigin.Remote));
            Assert.That(result.Config.client.minimum, Is.EqualTo("0.9.21"));
            Assert.That(cache.Saved, Is.EqualTo(ServerJson));
        }

        private static async UniTask CachedScenario()
        {
            var failure = new InvalidOperationException("offline");

            var result = await Provider(new FakeSource(failure), new MemoryCache(CachedJson)).LoadAsync();

            Assert.That(result.Origin, Is.EqualTo(ReleaseConfigOrigin.Cache));
            Assert.That(result.Config.client.minimum, Is.EqualTo("0.9.1"));
        }

        // A newer schema is a configuration this client does not understand, so it is no answer at all.
        private static async UniTask InvalidAnswerScenario()
        {
            var cache = new MemoryCache();

            var result = await Provider(new FakeSource("{\"schema\":2}"), cache).LoadAsync();

            Assert.That(result.Origin, Is.EqualTo(ReleaseConfigOrigin.Default));
            Assert.That(cache.Saved, Is.Null);
        }

        private static async UniTask BuiltInScenario()
        {
            var failure = new InvalidOperationException("offline");

            var result = await Provider(new FakeSource(failure), new MemoryCache()).LoadAsync();

            Assert.That(result.Origin, Is.EqualTo(ReleaseConfigOrigin.Default));
            Assert.That(result.Config, Is.SameAs(BuiltIn));
        }

        private static ReleaseConfigProvider Provider(IReleaseConfigSource source, IReleaseConfigCache cache)
            => new ReleaseConfigProvider("https://cdn.example/release.json", source, cache, BuiltIn);

        private sealed class FakeSource : IReleaseConfigSource
        {
            private readonly string _answer;
            private readonly Exception _failure;

            public FakeSource(string answer)
            {
                _answer = answer;
            }

            public FakeSource(Exception failure)
            {
                _failure = failure;
            }

            public UniTask<string> FetchAsync(string url, CancellationToken cancellationToken = default)
                => _failure != null
                    ? UniTask.FromException<string>(_failure)
                    : UniTask.FromResult(_answer);
        }

        private sealed class MemoryCache : IReleaseConfigCache
        {
            private string _json;

            public MemoryCache()
            {
            }

            public MemoryCache(string json)
            {
                _json = json;
            }

            public string Saved { get; private set; }

            public bool TryLoad(out string json)
            {
                json = _json;
                return _json != null;
            }

            public void Save(string json)
            {
                Saved = json;
                _json = json;
            }
        }
    }
}
