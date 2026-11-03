using System;
using Foundation.ContentMirroring;
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

namespace Foundation.Infrastructure.Addressables
{
    /// <summary>
    /// Points Addressables' remote requests (catalog check and download, bundles) at the environment folder
    /// the release configuration selected. Without it the folder baked into the build profile is always read.
    /// </summary>
    public static class AddressablesEndpointRouting
    {
        private static string _contentRoot;
        private static string _endpoint;
        private static string _mirrorBaseUrl;
        private static ContentMirror _mirror;

        /// <summary>The mirror currently used for remote Addressables requests, or null when no mirror is selected.</summary>
        public static string CurrentMirrorBaseUrl => _mirrorBaseUrl;

        /// <summary>The release folder remote requests are routed to, or null before <see cref="Apply"/> selected one.</summary>
        public static string CurrentEndpoint => _endpoint;

        public static void Apply(string contentRoot, string endpoint)
        {
            _contentRoot = contentRoot;
            _endpoint = endpoint;
            _mirrorBaseUrl = null;
            _mirror = null;
            ApplyTransform();
        }

        public static void ApplyMirror(string mirrorBaseUrl)
        {
            if (!Uri.TryCreate(mirrorBaseUrl, UriKind.Absolute, out var mirror)
                || (mirror.Scheme != Uri.UriSchemeHttp && mirror.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("A mirror must use an absolute HTTP(S) URL.", nameof(mirrorBaseUrl));

            _mirror = new ContentMirror("addressables-routing", mirrorBaseUrl);
            _mirrorBaseUrl = _mirror.BaseUrl;
            ApplyTransform();
        }

        /// <summary>Routes remote requests through <paramref name="mirror"/>; null restores the endpoint baseline.</summary>
        public static void ApplyMirror(ContentMirror mirror)
        {
            if (mirror == null)
            {
                RestoreEndpoint();
                return;
            }

            _mirror = mirror;
            _mirrorBaseUrl = mirror.BaseUrl;
            ApplyTransform();
        }

        /// <summary>Drops the selected mirror and routes remote requests to the selected endpoint again.</summary>
        public static void RestoreEndpoint()
        {
            _mirror = null;
            _mirrorBaseUrl = null;
            ApplyTransform();
        }

        /// <summary>Routes a baked remote URL to the selected release folder, ignoring any selected mirror.</summary>
        public static string RouteToEndpoint(string internalId)
            => _contentRoot == null || _endpoint == null
                ? internalId
                : Rewrite(_contentRoot, _endpoint, internalId);

        private static void ApplyTransform()
        {
            if (_contentRoot == null || (_endpoint == null && _mirrorBaseUrl == null))
            {
                AddressablesApi.InternalIdTransformFunc = null;
                return;
            }

            AddressablesApi.InternalIdTransformFunc = location => _mirror == null
                ? RewriteToMirror(_contentRoot, _endpoint, _mirrorBaseUrl, location.InternalId)
                : RewriteToContentMirror(_contentRoot, _endpoint, _mirror, location.InternalId);
        }

        public static string Rewrite(string contentRoot, string endpoint, string internalId)
        {
            if (contentRoot == null || endpoint == null || internalId == null)
                return internalId;
            if (!internalId.StartsWith(contentRoot, StringComparison.Ordinal))
                return internalId;
            if (contentRoot.Length > 0
                && contentRoot[contentRoot.Length - 1] != '/'
                && internalId.Length > contentRoot.Length
                && internalId[contentRoot.Length] != '/')
                return internalId;

            var environmentStart = contentRoot.EndsWith("/", StringComparison.Ordinal)
                ? contentRoot.Length
                : contentRoot.Length + 1;
            var folderEnd = internalId.IndexOf('/', environmentStart);
            if (folderEnd < 0)
                return internalId;

            return endpoint + internalId.Substring(folderEnd + 1);
        }

        public static string RewriteToContentMirror(
            string contentRoot,
            string endpoint,
            ContentMirror mirror,
            string internalId)
        {
            if (mirror == null)
                return RewriteToMirror(contentRoot, endpoint, null, internalId);

            if (!Uri.TryCreate(contentRoot, UriKind.Absolute, out var originalRoot)
                || !Uri.TryCreate(internalId, UriKind.Absolute, out var sourceUri)
                || !string.Equals(originalRoot.Scheme, sourceUri.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(originalRoot.Host, sourceUri.Host, StringComparison.OrdinalIgnoreCase)
                || originalRoot.Port != sourceUri.Port
                || !HasPathPrefix(sourceUri.AbsolutePath, originalRoot.AbsolutePath))
                return RewriteToMirror(contentRoot, endpoint, mirror.BaseUrl, internalId);

            var routed = RewriteToMirror(contentRoot, endpoint, mirror.BaseUrl, internalId);
            if (!Uri.TryCreate(routed, UriKind.Absolute, out var routedUri)
                || !Uri.TryCreate(mirror.BaseUrl, UriKind.Absolute, out var mirrorUri)
                || !string.Equals(routedUri.Scheme, mirrorUri.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(routedUri.Host, mirrorUri.Host, StringComparison.OrdinalIgnoreCase)
                || routedUri.Port != mirrorUri.Port
                || !HasPathPrefix(routedUri.AbsolutePath, mirrorUri.AbsolutePath))
                return routed;

            return mirror.ApplyQueryParameters(routed);
        }

        public static string RewriteToMirror(string contentRoot, string endpoint, string mirrorBaseUrl, string internalId)
        {
            var routed = Rewrite(contentRoot, endpoint, internalId);
            if (string.IsNullOrEmpty(mirrorBaseUrl)
                || !Uri.TryCreate(contentRoot, UriKind.Absolute, out var originalRoot)
                || !Uri.TryCreate(mirrorBaseUrl, UriKind.Absolute, out var mirrorRoot)
                || !Uri.TryCreate(routed, UriKind.Absolute, out var routedUri)
                || !string.Equals(originalRoot.Scheme, routedUri.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(originalRoot.Host, routedUri.Host, StringComparison.OrdinalIgnoreCase)
                || originalRoot.Port != routedUri.Port
                || !HasPathPrefix(routedUri.AbsolutePath, originalRoot.AbsolutePath))
                return routed;

            var originalSegments = SplitPath(originalRoot.AbsolutePath);
            var mirrorSegments = SplitPath(mirrorRoot.AbsolutePath);
            var routedSegments = SplitPath(routedUri.AbsolutePath);
            var overlap = GetPathOverlap(originalSegments, mirrorSegments);
            var pathSegments = new string[mirrorSegments.Length + routedSegments.Length - overlap];
            Array.Copy(mirrorSegments, pathSegments, mirrorSegments.Length);
            Array.Copy(routedSegments, overlap, pathSegments, mirrorSegments.Length, routedSegments.Length - overlap);
            var mirrorPath = pathSegments.Length == 0 ? "/" : "/" + string.Join("/", pathSegments);
            if (routedUri.AbsolutePath.EndsWith("/", StringComparison.Ordinal) && !mirrorPath.EndsWith("/", StringComparison.Ordinal))
                mirrorPath += "/";

            var builder = new UriBuilder(routedUri)
            {
                Scheme = mirrorRoot.Scheme,
                Host = mirrorRoot.Host,
                Port = mirrorRoot.IsDefaultPort ? -1 : mirrorRoot.Port,
                Path = mirrorPath,
                Query = CombineQueries(routedUri.Query, mirrorRoot.Query)
            };
            return builder.Uri.AbsoluteUri;
        }

        private static string CombineQueries(string first, string second)
        {
            first = first?.Trim().TrimStart('?').Trim('&');
            second = second?.Trim().TrimStart('?').Trim('&');
            if (string.IsNullOrEmpty(first))
                return second ?? string.Empty;
            if (string.IsNullOrEmpty(second))
                return first;
            return first + "&" + second;
        }

        private static bool HasPathPrefix(string path, string prefix)
        {
            var normalizedPrefix = prefix.TrimEnd('/');
            return path.StartsWith(normalizedPrefix, StringComparison.Ordinal)
                   && (path.Length == normalizedPrefix.Length
                       || normalizedPrefix.Length == 0
                       || path[normalizedPrefix.Length] == '/');
        }

        private static string[] SplitPath(string path)
            => path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        private static int GetPathOverlap(string[] sourceRoot, string[] mirrorBase)
        {
            for (var count = Math.Min(sourceRoot.Length, mirrorBase.Length); count > 0; count--)
            {
                var matches = true;
                for (var index = 0; index < count; index++)
                {
                    if (string.Equals(sourceRoot[index], mirrorBase[mirrorBase.Length - count + index], StringComparison.Ordinal))
                        continue;

                    matches = false;
                    break;
                }

                if (matches)
                    return count;
            }

            return 0;
        }
    }
}
