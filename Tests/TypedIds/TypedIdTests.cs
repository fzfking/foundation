using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Foundation.TypedIds;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests.TypedIds
{
    [Serializable]
    public sealed class UnityIdHolder
    {
        public TestScreenId screen;
    }

    public sealed class TypedIdTests
    {
        [Test]
        public void StableIds_AreEqualByKey_AndDefaultIsEmpty()
        {
            Assert.That(new TestScreenId("hud.main"), Is.EqualTo(TestScreenId.From("hud.main")));
            Assert.That(new TestScreenId("hud.main") == new TestScreenId("hud.shop"), Is.False);
            Assert.That(new TestScreenId("hud.main").GetHashCode(), Is.EqualTo(new TestScreenId("hud.main").GetHashCode()));
            Assert.That(default(TestScreenId).IsEmpty, Is.True);
            Assert.That(new TestScreenId(string.Empty), Is.EqualTo(default(TestScreenId)),
                "null and empty keys are the same 'none' id so deserialized defaults compare equal");
            Assert.That(new TestScreenId("hud.main").ToString(), Is.EqualTo("TestScreenId(hud.main)"));
        }

        [Test]
        public void StableIds_WorkAsDictionaryKeys_WithoutMixingTypes()
        {
            var map = new Dictionary<TestScreenId, int> { [TestScreenId.From("a")] = 1 };

            Assert.That(map[TestScreenId.From("a")], Is.EqualTo(1));
            Assert.That(map.ContainsKey(TestScreenId.From("b")), Is.False);
            Assert.That(((object)TestScreenId.From("a")).Equals(TestAudioId.From("a")), Is.False,
                "ids of different types with the same key are different values");
        }

        [Test]
        public void IdTypes_DeclareNoConversionsThatWouldLetThemBeMixedUp()
        {
            foreach (var type in new[] { typeof(TestScreenId), typeof(TestAudioId), typeof(TestTransactionId) })
            {
                var conversions = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(method => method.Name == "op_Implicit" || method.Name == "op_Explicit");

                Assert.That(conversions, Is.Empty, type.Name + " must not convert to or from other types implicitly");
            }
        }

        [Test]
        public void StableIds_SurviveUnitySerialization()
        {
            var holder = new UnityIdHolder { screen = TestScreenId.From("hud.main") };

            var restored = JsonUtility.FromJson<UnityIdHolder>(JsonUtility.ToJson(holder));

            Assert.That(restored.screen, Is.EqualTo(TestScreenId.From("hud.main")));
        }

        [TestCase("ui.click", true)]
        [TestCase("Night_Hunt-01/a:b", true)]
        [TestCase("", false)]
        [TestCase("has space", false)]
        [TestCase("кириллица", false)]
        public void StableIdKey_ValidatesSyntax(string key, bool expected)
        {
            Assert.That(StableIdKey.IsValid(key), Is.EqualTo(expected));
        }

        [Test]
        public void StableIdKey_TooLong_IsRejected()
        {
            Assert.That(StableIdKey.IsValid(new string('a', StableIdKey.MaxLength + 1)), Is.False);
            Assert.That(StableIdKey.IsValid(new string('a', StableIdKey.MaxLength)), Is.True);
        }

        [Test]
        public void StableHash_IsDeterministic_NotTheRuntimeStringHash()
        {
            // FNV-1a 32 of "a" is a published constant; the value must never change between versions
            // because it may be persisted.
            Assert.That(StableIdKey.StableHash("a"), Is.EqualTo(unchecked((int)0xE40C292C)));
            Assert.That(StableIdKey.StableHash(null), Is.EqualTo(unchecked((int)2166136261)));
        }

        [Test]
        public void RuntimeId_TextForm_RoundTrips_AndTheDefaultIsNone()
        {
            var id = TestTransactionId.New(new GuidGenerator());

            Assert.That(TestTransactionId.TryParse(id.Value.ToString(), out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(id));
            Assert.That(TestTransactionId.TryParse("not-a-guid", out _), Is.False);
            Assert.That(id.IsEmpty, Is.False);
            Assert.That(TestTransactionId.None.IsEmpty, Is.True);
        }
    }
}
