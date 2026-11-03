using System;
using Foundation.TypedIds;
using Foundation.GameFramework.Identity;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.GameFramework.Tests.Identity
{
    public sealed class LocalUserIdentityProviderTests
    {
        private const string PlayerPrefsKey = "Foundation.GameFramework.Identity.UserId";

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);
        }

        // A counter instead of randomness: the same ids in the same order on every run.
        private sealed class SequentialIdGenerator : IGuidGenerator
        {
            private int _next;

            public Guid Next() => new(++_next, 0, 0, new byte[8]);
        }

        private static string FirstId() => new SequentialIdGenerator().Next().ToString("N");

        [Test]
        public void FirstRun_GeneratesAndPersistsUserId()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);

            var provider = new LocalUserIdentityProvider(new SequentialIdGenerator());

            Assert.That(provider.UserId, Is.EqualTo(FirstId()));
            Assert.That(PlayerPrefs.GetString(PlayerPrefsKey), Is.EqualTo(FirstId()));
        }

        [Test]
        public void SecondRun_ReusesThePersistedUserId_WhateverFormatAnOlderBuildWroteIt()
        {
            const string persistedByAnOlderBuild = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
            PlayerPrefs.SetString(PlayerPrefsKey, persistedByAnOlderBuild);

            var provider = new LocalUserIdentityProvider(new SequentialIdGenerator());

            Assert.That(provider.UserId, Is.EqualTo(persistedByAnOlderBuild));
        }

        [Test]
        public void SessionId_IsFreshEveryTime_EvenWithTheSameUserId()
        {
            PlayerPrefs.SetString(PlayerPrefsKey, "existing-user");
            var sharedGenerator = new SequentialIdGenerator();

            var first = new LocalUserIdentityProvider(sharedGenerator);
            var second = new LocalUserIdentityProvider(sharedGenerator);

            Assert.That(first.UserId, Is.EqualTo(second.UserId));
            Assert.That(first.SessionId, Is.Not.EqualTo(second.SessionId));
        }
    }
}
