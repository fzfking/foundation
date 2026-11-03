using System;
using System.Collections.Generic;
using Foundation.Config;
using NUnit.Framework;

namespace Foundation.Tests.Config
{
    public class ConfigRepositoryTests
    {
        private ConfigRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _repository = new();
        }

        [Test]
        public void ConfigsAreKeyedByTypeAndId_SoSameIdsOfDifferentTypesDoNotCollide()
        {
            var first = new FakeConfig { Id = "a", Value = 1 };
            var other = new FakeOtherConfig { Id = "a" };
            _repository.Register("a", first);
            _repository.Register("a", other);

            Assert.That(_repository.Get<FakeConfig>("a"), Is.SameAs(first));
            Assert.That(_repository.Get<FakeOtherConfig>("a"), Is.SameAs(other));
            Assert.That(_repository.Contains<FakeConfig>("a"), Is.True);
            Assert.That(_repository.Contains<FakeOtherConfig>("b"), Is.False);
            Assert.That(_repository.GetAll<FakeConfig>(), Is.EqualTo(new[] { first }));
            Assert.That(_repository.Count<FakeConfig>(), Is.EqualTo(1));
        }

        [Test]
        public void Get_Missing_ThrowsKeyNotFound()
        {
            Assert.Throws<KeyNotFoundException>(() => _repository.Get<FakeConfig>("nonexistent"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Register_WithoutId_IsRejected(string id)
        {
            Assert.Throws<ArgumentNullException>(() => _repository.Register(id, new FakeConfig()));
        }

        [Test]
        public void Register_DuplicateId_IsRejected_AndKeepsTheFirstConfig()
        {
            _repository.Register("cfg_1", new FakeConfig { Id = "cfg_1", Value = 1 });

            Assert.Throws<InvalidOperationException>(
                () => _repository.Register("cfg_1", new FakeConfig { Id = "cfg_1", Value = 2 }));

            Assert.That(_repository.Get<FakeConfig>("cfg_1").Value, Is.EqualTo(1));
        }

        public sealed class FakeConfig
        {
            public string Id;
            public int Value;
        }

        public sealed class FakeOtherConfig
        {
            public string Id;
        }
    }
}
