using System;
using Foundation.TypedIds;
using UnityEngine;

namespace Foundation.GameFramework.Identity
{
    public interface IUserIdentityProvider
    {
        string UserId { get; }
        string SessionId { get; }
    }

    public sealed class LocalUserIdentityProvider : IUserIdentityProvider
    {
        private const string UserIdPlayerPrefsKey = "Foundation.GameFramework.Identity.UserId";

        public string UserId { get; }
        public string SessionId { get; }

        public LocalUserIdentityProvider(IGuidGenerator idGenerator)
        {
            if (idGenerator == null)
                throw new ArgumentNullException(nameof(idGenerator));

            if (PlayerPrefs.HasKey(UserIdPlayerPrefsKey))
            {
                UserId = PlayerPrefs.GetString(UserIdPlayerPrefsKey);
            }
            else
            {
                UserId = idGenerator.Next().ToString("N");
                PlayerPrefs.SetString(UserIdPlayerPrefsKey, UserId);
                PlayerPrefs.Save();
            }

            SessionId = idGenerator.Next().ToString("N");
        }
    }
}
