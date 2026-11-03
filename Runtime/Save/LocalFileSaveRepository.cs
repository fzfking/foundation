using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Logging;

namespace Foundation.Save
{
    public sealed class LocalFileSaveRepository : ISaveRepositoryBackend
    {
        private const int FormatVersion = 1;
        private readonly ILocalSaveLocation _location;
        private readonly IAppLogger _logger;

        public LocalFileSaveRepository(ILocalSaveLocation location, IAppLogger logger = null)
        {
            _location = location ?? throw new ArgumentNullException(nameof(location));
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
        }

        public UniTask<SaveData> LoadAsync(CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
        {
            var path = _location.GetSaveFilePath(slotId);
            return UniTask.RunOnThreadPool(() => LoadBlocking(path, cancellationToken), cancellationToken: cancellationToken);
        }

        public UniTask SaveAsync(SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            var path = _location.GetSaveFilePath(slotId);
            return UniTask.RunOnThreadPool(() => SaveBlocking(path, data, cancellationToken), cancellationToken: cancellationToken);
        }

        /// <summary>
        /// The previous save, kept by every save that replaces one. Read only when the save exists but cannot
        /// be read, so whoever deletes a save deletes this too (a missing save means a new player or a reset,
        /// never a recovery).
        /// </summary>
        public static string BackupPathOf(string savePath) => savePath + ".bak";

        /// <summary>Where a save that could not be read is moved once its backup has been loaded instead.</summary>
        public static string CorruptPathOf(string savePath) => savePath + ".corrupt";

        private SaveData LoadBlocking(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
                return null;

            try
            {
                return Read(path, cancellationToken);
            }
            catch (InvalidDataException failure)
            {
                var backupPath = BackupPathOf(path);
                if (!File.Exists(backupPath))
                    throw;

                SaveData backup;
                try
                {
                    backup = Read(backupPath, cancellationToken);
                }
                catch (InvalidDataException backupFailure)
                {
                    throw new InvalidDataException($"Local save '{path}' and its backup are both unreadable.",
                        new AggregateException(failure, backupFailure));
                }

                // Keep recovery durable even if the process exits before the next autosave. The good backup stays intact.
                var recoveryPath = path + ".recovery.tmp";
                try
                {
                    using (var source = File.OpenRead(backupPath))
                    using (var destination = File.Create(recoveryPath))
                    {
                        source.CopyTo(destination);
                        destination.Flush(true);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    File.Replace(recoveryPath, path, CorruptPathOf(path));
                }
                finally
                {
                    if (File.Exists(recoveryPath))
                        File.Delete(recoveryPath);
                }
                _logger.Log(LogLevel.Warning, "Save", $"local save '{path}' was unreadable ({failure.Message}); loaded its backup instead.");
                return backup;
            }
        }

        private static SaveData Read(string path, CancellationToken cancellationToken)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                if (reader.ReadInt32() != FormatVersion)
                    throw new InvalidDataException($"Unsupported local save format in '{path}'.");

                var timestamp = reader.ReadInt64();
                var version = reader.ReadInt32();
                var count = reader.ReadInt32();
                if (count < 0 || count > stream.Length)
                    throw new InvalidDataException("Save segment count is invalid.");
                var segments = new Dictionary<string, byte[]>(count, StringComparer.Ordinal);
                for (var i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var id = ReadSegmentId(reader, path);
                    var length = reader.ReadInt32();
                    // -1 is how a null segment is written. A length beyond the file is damage, and reading it would allocate it.
                    if (length < -1 || length > stream.Length - stream.Position)
                        throw new InvalidDataException("Save segment length is invalid.");
                    if (segments.ContainsKey(id))
                        throw new InvalidDataException($"Save segment '{id}' appears twice.");
                    segments.Add(id, length == -1 ? null : reader.ReadBytes(length));
                }
                return new SaveData(version, segments, timestamp);
            }
            catch (EndOfStreamException exception)
            {
                throw new InvalidDataException($"Local save file '{path}' is truncated.", exception);
            }
        }

        // A damaged length prefix surfaces as FormatException or as a plain IOException rather than a short read; both mean the
        // file is unreadable, which is what sends the load to the backup instead of failing it outright.
        private static string ReadSegmentId(BinaryReader reader, string path)
        {
            try
            {
                return reader.ReadString();
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException($"Local save file '{path}' has a damaged segment name.", exception);
            }
            catch (IOException exception) when (exception is not EndOfStreamException)
            {
                throw new InvalidDataException($"Local save file '{path}' has a damaged segment name.", exception);
            }
        }

        private static void SaveBlocking(string path, SaveData data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var temporaryPath = path + ".tmp";
            try
            {
                using (var stream = File.Create(temporaryPath))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(FormatVersion);
                    writer.Write(data.SavedAtUtcUnixMilliseconds);
                    writer.Write(data.Version);
                    writer.Write(data.Segments.Count);
                    foreach (var pair in data.Segments)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.Write(pair.Key);
                        writer.Write(pair.Value?.Length ?? -1);
                        if (pair.Value != null)
                            writer.Write(pair.Value);
                    }
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, BackupPathOf(path));
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}
