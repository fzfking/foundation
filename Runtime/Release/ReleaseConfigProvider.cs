using System;
using System.Threading;
using Foundation.Logging;
using Cysharp.Threading.Tasks;
using JsonUtility = UnityEngine.JsonUtility;

namespace Foundation.Release
{
    public enum ReleaseConfigOrigin
    {
        Remote,
        Cache,
        Default,
    }

    public readonly struct ReleaseConfigLoadResult
    {
        public ReleaseConfig Config { get; }

        public ReleaseConfigOrigin Origin { get; }

        public ReleaseConfigLoadResult(ReleaseConfig config, ReleaseConfigOrigin origin)
        {
            Config = config;
            Origin = origin;
        }
    }

    /// <summary>
    /// Loads the release configuration in the order that keeps the game playable: the server, then the copy
    /// the last successful fetch saved, then the one built into the client. An unreadable source is never
    /// fatal; it falls through.
    /// </summary>
    public sealed class ReleaseConfigProvider
    {
        private const string LogCategory = "Release";

        private readonly string _url;
        private readonly IReleaseConfigSource _source;
        private readonly IReleaseConfigCache _cache;
        private readonly ReleaseConfig _fallback;
        private readonly IAppLogger _logger;

        public ReleaseConfigProvider(
            string url,
            IReleaseConfigSource source,
            IReleaseConfigCache cache,
            ReleaseConfig fallback,
            IAppLogger logger = null)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _logger = logger;
        }

        public async UniTask<ReleaseConfigLoadResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            var json = await TryFetchAsync(cancellationToken);
            if (TryParse(json, out var remote))
            {
                TrySaveToCache(json);
                return new ReleaseConfigLoadResult(remote, ReleaseConfigOrigin.Remote);
            }

            if (TryLoadFromCache(out var cached))
                return new ReleaseConfigLoadResult(cached, ReleaseConfigOrigin.Cache);

            _logger?.Log(LogLevel.Warning, LogCategory, "No release configuration from the server or the cache; using the built-in one.");
            return new ReleaseConfigLoadResult(_fallback, ReleaseConfigOrigin.Default);
        }

        private async UniTask<string> TryFetchAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _source.FetchAsync(_url, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.Log(LogLevel.Warning, LogCategory, $"Release configuration could not be fetched: {exception.Message}");
                return null;
            }
        }

        private bool TryLoadFromCache(out ReleaseConfig config)
        {
            config = null;
            try
            {
                return _cache.TryLoad(out var json) && TryParse(json, out config);
            }
            catch (Exception exception)
            {
                _logger?.Log(LogLevel.Warning, LogCategory, $"Cached release configuration could not be read: {exception.Message}");
                return false;
            }
        }

        private void TrySaveToCache(string json)
        {
            try
            {
                _cache.Save(json);
            }
            catch (Exception exception)
            {
                _logger?.Log(LogLevel.Warning, LogCategory, $"Release configuration could not be cached: {exception.Message}");
            }
        }

        // The schema check keeps an older client from reading a newer, differently shaped configuration.
        private static bool TryParse(string json, out ReleaseConfig config)
        {
            config = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                config = JsonUtility.FromJson<ReleaseConfig>(json);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return config is { schema: ReleaseConfig.SupportedSchema };
        }
    }
}
