using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.ContentMirroring
{
    public sealed class ContentMirror
    {
        private readonly string _baseQuery;

        public string Id { get; }
        public string BaseUrl { get; }
        public Func<Uri, string> QueryProvider { get; }

        public ContentMirror(string id, string baseUrl, Func<Uri, string> queryProvider = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A mirror ID is required.", nameof(id));
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("A mirror must use an absolute HTTP(S) URL.", nameof(baseUrl));
            if (!string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("A mirror URL cannot contain a fragment.", nameof(baseUrl));

            Id = id;
            _baseQuery = uri.Query.TrimStart('?');
            QueryProvider = queryProvider;
            var builder = new UriBuilder(uri)
            {
                Path = uri.AbsolutePath.TrimEnd('/') + "/",
                Query = string.Empty
            };
            BaseUrl = builder.Uri.AbsoluteUri;
        }

        public string GetUrl(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Uri.TryCreate(relativePath, UriKind.Absolute, out _))
                throw new ArgumentException("A relative content path is required.", nameof(relativePath));

            return ApplyQueryParameters(new Uri(new Uri(BaseUrl), relativePath.TrimStart('/')).AbsoluteUri);
        }

        public string RouteUrl(string sourceUrl)
        {
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source)
                || (source.Scheme != Uri.UriSchemeHttp && source.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("The source URL must be absolute HTTP(S).", nameof(sourceUrl));

            var mirror = new Uri(BaseUrl);
            var builder = new UriBuilder(source)
            {
                Scheme = mirror.Scheme,
                Host = mirror.Host,
                Port = mirror.IsDefaultPort ? -1 : mirror.Port,
                Path = mirror.AbsolutePath.TrimEnd('/') + source.AbsolutePath
            };
            return ApplyQueryParameters(builder.Uri.AbsoluteUri);
        }

        public string ApplyQueryParameters(string finalUrl)
        {
            if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("The final URL must be absolute HTTP(S).", nameof(finalUrl));

            var query = CombineQueries(uri.Query, _baseQuery);
            query = CombineQueries(query, QueryProvider?.Invoke(uri));
            if (string.Equals(query, uri.Query.TrimStart('?'), StringComparison.Ordinal))
                return uri.AbsoluteUri;

            var builder = new UriBuilder(uri) { Query = query };
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
    }

    public sealed class ContentMirrorConfiguration
    {
        public IReadOnlyList<ContentMirror> Mirrors { get; }
        public string ProbePath { get; }
        public int ProbeSizeBytes { get; }
        public TimeSpan MaximumEta { get; }
        public TimeSpan StatisticsLifetime { get; }
        public bool ParallelProbes { get; }

        public ContentMirrorConfiguration(
            IReadOnlyList<ContentMirror> mirrors,
            string probePath,
            int probeSizeBytes,
            TimeSpan maximumEta,
            TimeSpan statisticsLifetime)
            : this(mirrors, probePath, probeSizeBytes, maximumEta, statisticsLifetime, false)
        {
        }

        public ContentMirrorConfiguration(
            IReadOnlyList<ContentMirror> mirrors,
            string probePath,
            int probeSizeBytes,
            TimeSpan maximumEta,
            TimeSpan statisticsLifetime,
            bool parallelProbes)
        {
            if (mirrors == null || mirrors.Count == 0)
                throw new ArgumentException("At least one content mirror is required.", nameof(mirrors));
            if (string.IsNullOrWhiteSpace(probePath) || Uri.TryCreate(probePath, UriKind.Absolute, out _))
                throw new ArgumentException("A relative probe path is required.", nameof(probePath));
            if (probeSizeBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(probeSizeBytes));
            if (maximumEta <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(maximumEta));
            if (statisticsLifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(statisticsLifetime));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mirror in mirrors)
                if (mirror == null || !ids.Add(mirror.Id))
                    throw new ArgumentException("Mirror entries must be non-null and have unique IDs.", nameof(mirrors));

            Mirrors = new List<ContentMirror>(mirrors);
            ProbePath = probePath.TrimStart('/');
            ProbeSizeBytes = probeSizeBytes;
            MaximumEta = maximumEta;
            StatisticsLifetime = statisticsLifetime;
            ParallelProbes = parallelProbes;
        }
    }

    public readonly struct ContentMirrorProgress
    {
        public long DownloadedBytes { get; }
        public long TotalBytes { get; }

        public ContentMirrorProgress(long downloadedBytes, long totalBytes)
        {
            DownloadedBytes = Math.Max(0, downloadedBytes);
            TotalBytes = Math.Max(0, totalBytes);
        }
    }

    public readonly struct ContentMirrorProbeResult
    {
        public bool Succeeded { get; }
        public double BytesPerSecond { get; }

        public ContentMirrorProbeResult(bool succeeded, double bytesPerSecond)
        {
            Succeeded = succeeded;
            BytesPerSecond = Math.Max(0d, bytesPerSecond);
        }
    }

    public interface IContentMirrorProbe
    {
        UniTask<ContentMirrorProbeResult> ProbeAsync(
            ContentMirror mirror,
            string relativePath,
            int expectedSizeBytes,
            CancellationToken cancellationToken);
    }
}
