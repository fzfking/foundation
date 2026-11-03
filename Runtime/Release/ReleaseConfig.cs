using System;

namespace Foundation.Release
{
    /// <summary>
    /// The release configuration a server publishes. Plain data read with JsonUtility, which maps field names
    /// onto the JSON keys, hence the lowercase names.
    /// </summary>
    [Serializable]
    public sealed class ReleaseConfig
    {
        public const int SupportedSchema = 1;

        public int schema;
        public ClientPolicy client;
        public ReleaseEnvironment[] environments;
    }

    [Serializable]
    public sealed class ClientPolicy
    {
        public string minimum;
        public string recommended;
        public string storeUrl;
    }

    /// <summary>
    /// One environment, such as "production" or "rc": the release it serves now and the one kept for clients
    /// that have not updated yet.
    /// </summary>
    [Serializable]
    public sealed class ReleaseEnvironment
    {
        public string id;
        public ReleaseSlot current;
        public ReleaseSlot previous;
    }

    [Serializable]
    public sealed class ReleaseSlot
    {
        public string release;
        public string minClient;
        public string path;
    }
}
