using System.Collections.Generic;
using Foundation.TypedIds;
using MemoryPack;
using NUnit.Framework;

namespace Foundation.Tests.TypedIds
{
    [MemoryPackable]
    public partial class TypedIdConfig
    {
        [MemoryPackAllowSerialize]
        public TestScreenId Screen;

        [MemoryPackAllowSerialize]
        public TestAudioId Sound;

        public TestTransactionId Transaction;

        [MemoryPackAllowSerialize]
        public List<TestScreenId> Screens;
    }

    public sealed class TypedIdMemoryPackTests
    {
        [Test]
        public void Ids_RoundTripThroughMemoryPack_InsideConfigs()
        {
            var transaction = TestTransactionId.New(new GuidGenerator());
            var config = new TypedIdConfig
            {
                Screen = TestScreenId.From("hud.main"),
                Sound = default,
                Transaction = transaction,
                Screens = new List<TestScreenId> { TestScreenId.From("a"), TestScreenId.From("b") },
            };

            var restored = MemoryPackSerializer.Deserialize<TypedIdConfig>(MemoryPackSerializer.Serialize(config));

            Assert.That(restored.Screen, Is.EqualTo(TestScreenId.From("hud.main")));
            Assert.That(restored.Sound.IsEmpty, Is.True);
            Assert.That(restored.Transaction, Is.EqualTo(transaction));
            Assert.That(restored.Screens, Is.EqualTo(config.Screens));
        }
    }
}
