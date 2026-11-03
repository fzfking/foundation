using System;

namespace Foundation.Release
{
    public enum ClientUpdateStatus
    {
        None,
        Recommended,
        Required,
    }

    /// <summary>Compares this client with the server's update policy and names the store that serves the update.</summary>
    public interface IClientUpdateService
    {
        ClientUpdateStatus Evaluate(ClientVersion clientVersion, ReleaseConfig config, ReleaseSelection selection);

        string StoreUrl(ReleaseConfig config);
    }

    public sealed class ClientUpdateService : IClientUpdateService
    {
        // The update button opens this URL, and the server's config is the one place it can come from after the build: only a
        // store link may be opened, never a cleartext, file or app-specific link a tampered config could put there.
        private static readonly string[] StoreSchemes = { "https", "market", "itms-apps" };

        private readonly string _fallbackStoreUrl;

        public ClientUpdateService(string fallbackStoreUrl)
        {
            _fallbackStoreUrl = fallbackStoreUrl;
        }

        public ClientUpdateStatus Evaluate(ClientVersion clientVersion, ReleaseConfig config, ReleaseSelection selection)
        {
            if (selection.Kind == ReleaseSelectionKind.Incompatible)
                return ClientUpdateStatus.Required;

            var policy = config?.client;
            if (policy == null)
                return ClientUpdateStatus.None;

            if (ClientVersion.TryParse(policy.minimum, out var minimum) && clientVersion < minimum)
                return ClientUpdateStatus.Required;

            if (ClientVersion.TryParse(policy.recommended, out var recommended) && clientVersion < recommended)
                return ClientUpdateStatus.Recommended;

            return ClientUpdateStatus.None;
        }

        public string StoreUrl(ReleaseConfig config)
        {
            var url = config?.client?.storeUrl;
            return IsStoreUrl(url) ? url : _fallbackStoreUrl;
        }

        private static bool IsStoreUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return Array.IndexOf(StoreSchemes, uri.Scheme) >= 0;
        }
    }
}
