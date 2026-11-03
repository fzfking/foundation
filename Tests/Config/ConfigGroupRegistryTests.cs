using System;
using Foundation.Config;
using NUnit.Framework;

namespace Foundation.Tests.Config
{
    public class ConfigGroupRegistryTests
    {
        private ConfigGroupRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new();
        }

        [Test]
        public void Register_DuplicateGroupId_Throws()
        {
            _registry.Register<FakeConfig>("group", "asset/a", c => c.Id);

            Assert.Throws<InvalidOperationException>(
                () => _registry.Register<FakeOtherConfig>("group", "asset/b", c => c.Id));
        }

        [Test]
        public void Register_DuplicateConfigType_Throws()
        {
            _registry.Register<FakeConfig>("group_a", "asset/a", c => c.Id);

            Assert.Throws<InvalidOperationException>(
                () => _registry.Register<FakeConfig>("group_b", "asset/b", c => c.Id));
        }

        [TestCase("", "asset/id")]
        [TestCase("group", "")]
        public void Register_WithoutGroupOrAssetId_Throws(string groupId, string assetId)
        {
            Assert.Throws<ArgumentException>(() => _registry.Register<FakeConfig>(groupId, assetId, c => c.Id));
        }

        [Test]
        public void GetGroup_UnregisteredType_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _registry.GetGroup(typeof(FakeConfig)));
        }

        private sealed class FakeConfig
        {
            public string Id;
        }

        private sealed class FakeOtherConfig
        {
            public string Id;
        }
    }
}
