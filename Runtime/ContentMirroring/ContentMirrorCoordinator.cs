using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.ContentMirroring
{
    /// <summary>Runs one provider operation against one mirror at a time and owns failover policy.</summary>
    public sealed class ContentMirrorCoordinator
    {
        // Process-wide, but applied only to the exact mirror instance from the current configuration.
        private static readonly object StickyMirrorLock = new();
        private static ContentMirror _stickyMirror;
        private readonly ContentMirrorConfiguration _configuration;
        private readonly IContentMirrorProbe _probe;
        private readonly IContentMirrorStatisticsStore _statistics;
        private readonly string _preferredMirrorId;
        private readonly TimeSpan _preferredMirrorMaximumEta;
        private readonly object _activeAttemptTokenLock = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private CancellationToken _activeAttemptToken;
        private bool _hasActiveAttemptToken;

        public ContentMirrorCoordinator(
            ContentMirrorConfiguration configuration,
            IContentMirrorProbe probe,
            IContentMirrorStatisticsStore statistics,
            string preferredMirrorId = null,
            TimeSpan? preferredMirrorMaximumEta = null)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));

            if (string.IsNullOrWhiteSpace(preferredMirrorId))
            {
                if (preferredMirrorMaximumEta.HasValue)
                    throw new ArgumentException("A preferred mirror is required when its ETA limit is set.", nameof(preferredMirrorId));

                return;
            }

            if (!preferredMirrorMaximumEta.HasValue || preferredMirrorMaximumEta.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(preferredMirrorMaximumEta));

            var preferredMirrorExists = false;
            foreach (var mirror in _configuration.Mirrors)
                if (string.Equals(mirror.Id, preferredMirrorId, StringComparison.Ordinal))
                {
                    preferredMirrorExists = true;
                    break;
                }

            if (!preferredMirrorExists)
                throw new ArgumentException($"Preferred mirror '{preferredMirrorId}' is not configured.", nameof(preferredMirrorId));

            _preferredMirrorId = preferredMirrorId;
            _preferredMirrorMaximumEta = preferredMirrorMaximumEta.Value;
        }

        /// <summary>Identifies the token passed to the currently running mirror attempt.</summary>
        public bool IsActiveAttemptToken(CancellationToken cancellationToken)
        {
            lock (_activeAttemptTokenLock)
                return _hasActiveAttemptToken && _activeAttemptToken == cancellationToken;
        }

        public async UniTask<bool> ExecuteAsync(
            Func<ContentMirror, IProgress<ContentMirrorProgress>, CancellationToken, UniTask<bool>> attempt,
            CancellationToken cancellationToken = default,
            long expectedDownloadBytes = 0,
            string excludedMirrorBaseUrl = null)
        {
            if (attempt == null)
                throw new ArgumentNullException(nameof(attempt));
            if (expectedDownloadBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedDownloadBytes));

            await _gate.WaitAsync(cancellationToken);
            try
            {
                var candidates = await GetCandidatesAsync(cancellationToken, excludedMirrorBaseUrl);
                if (candidates.Count == 0)
                    return false;

                var fastestMirror = candidates[0].Mirror;
                if (!PrioritizeStickyMirror(candidates))
                    PrioritizePreferredMirror(candidates, expectedDownloadBytes);
                var allAttemptsExceededEta = true;
                for (var index = 0; index < candidates.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidate = candidates[index];
                    using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var stopwatch = Stopwatch.StartNew();
                    long latestDownloadedBytes = 0;
                    var etaExceeded = false;
                    var progress = new InlineProgress(value =>
                    {
                        latestDownloadedBytes = Math.Max(latestDownloadedBytes, value.DownloadedBytes);
                        if (value.TotalBytes <= value.DownloadedBytes)
                            return;

                        var bytesPerSecond = GetCurrentSpeed(candidate.BytesPerSecond, latestDownloadedBytes, stopwatch.Elapsed);
                        if (bytesPerSecond <= 0d)
                            return;

                        var remainingSeconds = (value.TotalBytes - value.DownloadedBytes) / bytesPerSecond;
                        if (remainingSeconds > _configuration.MaximumEta.TotalSeconds
                            && !attemptCancellation.IsCancellationRequested)
                        {
                            etaExceeded = true;
                            attemptCancellation.Cancel();
                        }
                    });

                    try
                    {
                        if (await InvokeAttemptAsync(attempt, candidate.Mirror, progress, attemptCancellation.Token))
                        {
                            SaveMeasurement(candidate.Mirror.Id, latestDownloadedBytes, stopwatch.Elapsed);
                            MarkMirrorSucceeded(candidate.Mirror);
                            return true;
                        }

                        if (!etaExceeded)
                        {
                            allAttemptsExceededEta = false;
                            ClearStickyMirrorIf(candidate.Mirror);
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attemptCancellation.IsCancellationRequested)
                    {
                        if (!etaExceeded)
                            throw;
                    }

                    if (latestDownloadedBytes > 0)
                        SaveMeasurement(candidate.Mirror.Id, latestDownloadedBytes, stopwatch.Elapsed);
                    else if (!etaExceeded)
                        _statistics.Remove(candidate.Mirror.Id);
                }

                if (!allAttemptsExceededEta)
                    return false;

                // If every route exceeds ETA, keep the game moving on the measured fastest mirror.
                cancellationToken.ThrowIfCancellationRequested();
                using var fallbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var fallbackSucceeded = await InvokeAttemptAsync(
                    attempt,
                    fastestMirror,
                    NoOpProgress.Instance,
                    fallbackCancellation.Token);
                if (fallbackSucceeded)
                    MarkMirrorSucceeded(fastestMirror);
                else
                    ClearStickyMirrorIf(fastestMirror);
                return fallbackSucceeded;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async UniTask<bool> InvokeAttemptAsync(
            Func<ContentMirror, IProgress<ContentMirrorProgress>, CancellationToken, UniTask<bool>> attempt,
            ContentMirror mirror,
            IProgress<ContentMirrorProgress> progress,
            CancellationToken cancellationToken)
        {
            lock (_activeAttemptTokenLock)
            {
                _activeAttemptToken = cancellationToken;
                _hasActiveAttemptToken = true;
            }

            try
            {
                return await attempt(mirror, progress, cancellationToken);
            }
            finally
            {
                lock (_activeAttemptTokenLock)
                {
                    _activeAttemptToken = default;
                    _hasActiveAttemptToken = false;
                }
            }
        }

        private async UniTask<List<Candidate>> GetCandidatesAsync(
            CancellationToken cancellationToken,
            string excludedMirrorBaseUrl)
        {
            var now = DateTimeOffset.UtcNow;
            var candidates = new List<Candidate>(_configuration.Mirrors.Count);
            ContentMirrorProbeResult[] parallelResults = null;
            if (_configuration.ParallelProbes)
            {
                var probes = new UniTask<ContentMirrorProbeResult>[_configuration.Mirrors.Count];
                for (var index = 0; index < probes.Length; index++)
                {
                    var mirror = _configuration.Mirrors[index];
                    probes[index] = IsExcluded(mirror, excludedMirrorBaseUrl)
                        ? UniTask.FromResult(default(ContentMirrorProbeResult))
                        : ProbeAsync(mirror, cancellationToken);
                }

                parallelResults = await UniTask.WhenAll(probes);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var mirrorIndex = 0;
            foreach (var mirror in _configuration.Mirrors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = mirrorIndex++;
                if (IsExcluded(mirror, excludedMirrorBaseUrl))
                    continue;

                var hasCachedSpeed = _statistics.TryGetBytesPerSecond(
                    mirror.Id,
                    now,
                    _configuration.StatisticsLifetime,
                    out var cachedSpeed);

                var result = parallelResults != null
                    ? parallelResults[index]
                    : await ProbeAsync(mirror, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (!result.Succeeded || result.BytesPerSecond <= 0d)
                {
                    _statistics.Remove(mirror.Id);
                    ClearStickyMirrorIf(mirror);
                    continue;
                }

                _statistics.SaveBytesPerSecond(mirror.Id, result.BytesPerSecond, now);
                candidates.Add(new Candidate(
                    mirror,
                    result.BytesPerSecond,
                    hasCachedSpeed ? cachedSpeed : result.BytesPerSecond));
            }

            candidates.Sort((left, right) => right.SortBytesPerSecond.CompareTo(left.SortBytesPerSecond));
            return candidates;
        }

        private async UniTask<ContentMirrorProbeResult> ProbeAsync(ContentMirror mirror, CancellationToken cancellationToken)
        {
            try
            {
                return await _probe.ProbeAsync(mirror, _configuration.ProbePath, _configuration.ProbeSizeBytes, cancellationToken);
            }
            catch (Exception)
            {
                // shortcut: probe errors become unavailable results; add diagnostics if callers need causes.
                return default;
            }
        }

        private static bool PrioritizeStickyMirror(List<Candidate> candidates)
        {
            ContentMirror stickyMirror;
            lock (StickyMirrorLock)
                stickyMirror = _stickyMirror;

            if (stickyMirror == null)
                return false;

            for (var index = 0; index < candidates.Count; index++)
            {
                if (!ReferenceEquals(candidates[index].Mirror, stickyMirror))
                    continue;

                if (index > 0)
                {
                    var candidate = candidates[index];
                    candidates.RemoveAt(index);
                    candidates.Insert(0, candidate);
                }

                return true;
            }

            return false;
        }

        private static void MarkMirrorSucceeded(ContentMirror mirror)
        {
            lock (StickyMirrorLock)
                _stickyMirror = mirror;
        }

        private static void ClearStickyMirrorIf(ContentMirror mirror)
        {
            lock (StickyMirrorLock)
                if (ReferenceEquals(_stickyMirror, mirror))
                    _stickyMirror = null;
        }

        private void PrioritizePreferredMirror(List<Candidate> candidates, long expectedDownloadBytes)
        {
            if (expectedDownloadBytes == 0 || _preferredMirrorId == null)
                return;

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (!string.Equals(candidate.Mirror.Id, _preferredMirrorId, StringComparison.Ordinal))
                    continue;
                if (expectedDownloadBytes / candidate.BytesPerSecond > _preferredMirrorMaximumEta.TotalSeconds)
                    return;

                candidates.RemoveAt(index);
                candidates.Insert(0, candidate);
                return;
            }
        }

        private static bool IsExcluded(ContentMirror mirror, string excludedMirrorBaseUrl)
        {
            return !string.IsNullOrWhiteSpace(excludedMirrorBaseUrl)
                && string.Equals(
                    mirror.BaseUrl,
                    excludedMirrorBaseUrl.TrimEnd('/') + "/",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static double GetCurrentSpeed(double probeSpeed, long downloadedBytes, TimeSpan elapsed)
        {
            if (downloadedBytes <= 0 || elapsed <= TimeSpan.Zero)
                return probeSpeed;

            var measuredSpeed = downloadedBytes / elapsed.TotalSeconds;
            return downloadedBytes < 256 * 1024 ? Math.Min(probeSpeed, measuredSpeed) : measuredSpeed;
        }

        private void SaveMeasurement(string mirrorId, long downloadedBytes, TimeSpan elapsed)
        {
            if (downloadedBytes > 0 && elapsed > TimeSpan.Zero)
                _statistics.SaveBytesPerSecond(mirrorId, downloadedBytes / elapsed.TotalSeconds, DateTimeOffset.UtcNow);
        }

        private readonly struct Candidate
        {
            public ContentMirror Mirror { get; }
            public double BytesPerSecond { get; }
            public double SortBytesPerSecond { get; }

            public Candidate(ContentMirror mirror, double bytesPerSecond, double sortBytesPerSecond)
            {
                Mirror = mirror;
                BytesPerSecond = bytesPerSecond;
                SortBytesPerSecond = sortBytesPerSecond;
            }
        }

        private sealed class InlineProgress : IProgress<ContentMirrorProgress>
        {
            private readonly Action<ContentMirrorProgress> _report;

            public InlineProgress(Action<ContentMirrorProgress> report) => _report = report;
            public void Report(ContentMirrorProgress value) => _report(value);
        }

        private sealed class NoOpProgress : IProgress<ContentMirrorProgress>
        {
            public static readonly NoOpProgress Instance = new();
            public void Report(ContentMirrorProgress value) { }
        }
    }
}
