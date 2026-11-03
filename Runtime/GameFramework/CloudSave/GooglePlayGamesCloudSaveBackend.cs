using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Foundation.Logging;
using Foundation.Save;
using Cysharp.Threading.Tasks;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.SavedGame;

// The awaits live in the UNITY_ANDROID branches; in the Editor these methods complete synchronously.
#pragma warning disable CS1998

namespace Foundation.GameFramework.CloudSave
{
    /// <summary>
    /// Cloud save backend on Google Play Games Saved Games, plugged into the CompositeSaveRepository as an
    /// ISaveRepositoryBackend: loads pick the copy with the newest timestamp, saves fan out to all backends,
    /// and a failing cloud never breaks the local save.
    /// Cloud commits happen only on top of local save triggers, only after the cloud snapshot was read once
    /// for the same account and slot (so a late or failed sign-in cannot overwrite newer cloud data), only when content
    /// differs from the last upload, and not more often than the interval. Skipped writes are not persistence.
    /// Cancellation stops waiting; a submitted SDK commit can still complete afterwards.
    /// The time of an upload is the save's own <see cref="SaveData.SavedAtUtcUnixMilliseconds"/>, so a game that
    /// builds its saves with a trusted (server) time gets that time in the upload interval and the snapshot
    /// description too.
    /// </summary>
    public sealed class GooglePlayGamesCloudSaveBackend : ISaveRepositoryBackendWithResult
    {
        private const string LogCategory = "CloudSave";

        private readonly string _snapshotName;
        private readonly GooglePlayGamesSignIn _signIn;
        private readonly CloudSaveUploadPolicy _uploadPolicy;
        private readonly IAppLogger _logger;
        private readonly HashSet<string> _readSnapshots = new(StringComparer.Ordinal);

        public GooglePlayGamesCloudSaveBackend(
            string snapshotName,
            GooglePlayGamesSignIn signIn,
            CloudSaveUploadPolicy uploadPolicy,
            IAppLogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(snapshotName))
                throw new ArgumentException("Snapshot name must not be empty.", nameof(snapshotName));

            _snapshotName = snapshotName;
            _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
            _uploadPolicy = uploadPolicy ?? throw new ArgumentNullException(nameof(uploadPolicy));
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
        }

        public async UniTask<SaveData> LoadAsync(CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
        {
#if UNITY_ANDROID
            cancellationToken.ThrowIfCancellationRequested();
            if (!_signIn.IsPlatformAvailable || !_signIn.IsPreferenceEnabled)
                return null;

            if (!await _signIn.AwaitAsync(cancellationToken))
                return null;

            var snapshotName = ResolveSnapshotName(slotId);
            var accountId = PlayGamesPlatform.Instance.GetUserId();
            var stateKey = accountId + "|" + snapshotName;
            _readSnapshots.Remove(stateKey);
            var metadata = await OpenSnapshotAsync(snapshotName, DataSource.ReadNetworkOnly, cancellationToken);
            if (metadata == null)
                return null;

            // The cloud state is known only once its payload is in hand. A failed read leaves it unknown, and an upload
            // then would replace whatever the snapshot holds, possibly newer progress from another device.
            var payload = await ReadPayloadAsync(metadata, cancellationToken);
            if (payload == null || !IsSameAccount(accountId))
                return null;

            // An existing-but-empty snapshot is known cloud state too - without this a fresh account would never be
            // allowed its first upload.
            if (payload.Length == 0)
            {
                _readSnapshots.Add(stateKey);
                return null;
            }

            try
            {
                var save = GooglePlayGamesSnapshotPayload.Decode(payload);
                _readSnapshots.Add(stateKey);
                return save;
            }
            catch (InvalidDataException exception)
            {
                // Still unknown: an upload would replace content this build cannot read, possibly a newer format.
                _logger.Log(LogLevel.Warning, LogCategory, $"Cloud save '{snapshotName}' is unreadable, ignoring it: {exception.Message}");
                return null;
            }
#else
            cancellationToken.ThrowIfCancellationRequested();
            return null;
#endif
        }

        public async UniTask SaveAsync(SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
        {
            if (await SaveWithResultAsync(data, cancellationToken, slotId) != SaveWriteResult.Persisted)
                throw new IOException("Cloud save was skipped; no cloud write was confirmed.");
        }

        public async UniTask<SaveWriteResult> SaveWithResultAsync(
            SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID
            if (!_signIn.IsActive)
                return SaveWriteResult.Skipped;

            var snapshotName = ResolveSnapshotName(slotId);
            var accountId = PlayGamesPlatform.Instance.GetUserId();
            var stateKey = accountId + "|" + snapshotName;
            if (!_readSnapshots.Contains(stateKey))
                return SaveWriteResult.Skipped;

            var savedAt = data.SavedAtUtcUnixMilliseconds;
            if (!_uploadPolicy.ShouldUpload(data, savedAt, stateKey))
                return SaveWriteResult.Skipped;

            var metadata = await OpenSnapshotAsync(snapshotName, DataSource.ReadCacheOrNetwork, cancellationToken);
            if (metadata == null)
            {
                throw new IOException($"Cloud save '{snapshotName}' could not be opened for writing.");
            }

            if (!IsSameAccount(accountId))
                return SaveWriteResult.Skipped;
            cancellationToken.ThrowIfCancellationRequested();

            var update = new SavedGameMetadataUpdate.Builder()
                .WithUpdatedDescription($"Saved {DateTimeOffset.FromUnixTimeMilliseconds(savedAt):yyyy-MM-dd HH:mm} UTC")
                .Build();
            var payload = GooglePlayGamesSnapshotPayload.Encode(data);
            var completion = new UniTaskCompletionSource<bool>();
            PlayGamesPlatform.Instance.SavedGame.CommitUpdate(metadata, update, payload, (status, _) =>
            {
                if (status != SavedGameRequestStatus.Success)
                    _logger.Log(LogLevel.Warning, LogCategory, $"Cloud save '{snapshotName}' commit status: {status}.");
                completion.TrySetResult(status == SavedGameRequestStatus.Success);
            });

            if (!await completion.Task.AttachExternalCancellation(cancellationToken))
                throw new IOException($"Cloud save '{snapshotName}' commit failed.");

            _uploadPolicy.OnUploaded(data, savedAt, stateKey);
            return SaveWriteResult.Persisted;
#else
            cancellationToken.ThrowIfCancellationRequested();
            return SaveWriteResult.Skipped;
#endif
        }

#if UNITY_ANDROID
        private async UniTask<ISavedGameMetadata> OpenSnapshotAsync(
            string snapshotName, DataSource source, CancellationToken cancellationToken)
        {
            var savedGameClient = PlayGamesPlatform.Instance.SavedGame;
            if (savedGameClient == null)
                return null;

            cancellationToken.ThrowIfCancellationRequested();
            var completion = new UniTaskCompletionSource<ISavedGameMetadata>();
            savedGameClient.OpenWithAutomaticConflictResolution(
                snapshotName, source, ConflictResolutionStrategy.UseMostRecentlySaved,
                (status, metadata) =>
                {
                    if (status != SavedGameRequestStatus.Success)
                        _logger.Log(LogLevel.Warning, LogCategory, $"Cloud save '{snapshotName}' open failed: {status} (source {source}).");
                    completion.TrySetResult(status == SavedGameRequestStatus.Success ? metadata : null);
                });
            return await completion.Task.AttachExternalCancellation(cancellationToken);
        }

        private async UniTask<byte[]> ReadPayloadAsync(ISavedGameMetadata metadata, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new UniTaskCompletionSource<byte[]>();
            PlayGamesPlatform.Instance.SavedGame.ReadBinaryData(metadata, (status, payload) =>
            {
                if (status != SavedGameRequestStatus.Success)
                    _logger.Log(LogLevel.Warning, LogCategory, $"Cloud save '{metadata.Filename}' read failed: {status}.");
                completion.TrySetResult(status == SavedGameRequestStatus.Success ? payload : null);
            });
            return await completion.Task.AttachExternalCancellation(cancellationToken);
        }

        private bool IsSameAccount(string accountId)
            => _signIn.IsActive && string.Equals(accountId, PlayGamesPlatform.Instance.GetUserId(), StringComparison.Ordinal);

#endif

        private string ResolveSnapshotName(string slotId)
            => slotId == SaveSlot.Default ? _snapshotName : $"{_snapshotName}-{slotId}";
    }
}
