using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Config;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.Config
{
    public class ConfigResolverTests
    {
        private ConfigRepository _repository;
        private RecordingProvider _provider;
        private ConfigResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            _repository = new();
            _provider = new();
            _resolver = new(_provider, _repository);
        }

        [Test]
        public void ResolveAsync_EnsuresConfig_AndReturns()
        {
            _repository.Register("mars", new FakeConfig { Id = "mars", Value = 42 });

            var result = _resolver.ResolveAsync<FakeConfig>("mars").GetAwaiter().GetResult();

            Assert.That(result.Id, Is.EqualTo("mars"));
            Assert.That(result.Value, Is.EqualTo(42));
            Assert.That(_provider.Ensured, Is.EqualTo(new[] { typeof(FakeConfig) }));
        }

        [Test]
        public void ResolveAllAsync_EnsuresConfig_AndReturnsAll()
        {
            _repository.Register("a", new FakeConfig { Id = "a" });
            _repository.Register("b", new FakeConfig { Id = "b" });

            var result = _resolver.ResolveAllAsync<FakeConfig>().GetAwaiter().GetResult();

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(_provider.Ensured, Is.EqualTo(new[] { typeof(FakeConfig) }));
        }

        private sealed class RecordingProvider : IConfigProvider
        {
            public readonly List<Type> Ensured = new();

            public UniTask EnsureAsync<TConfig>(CancellationToken cancellationToken = default)
            {
                Ensured.Add(typeof(TConfig));
                return UniTask.CompletedTask;
            }
        }

        private sealed class FakeConfig
        {
            public string Id;
            public int Value;
        }
    }
}
