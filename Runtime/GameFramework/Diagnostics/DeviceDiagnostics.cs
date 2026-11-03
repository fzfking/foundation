using System.IO;
using UnityEngine;

namespace Foundation.GameFramework.Diagnostics
{
    public interface IDeviceDiagnostics
    {
        long TotalSystemMemoryMb { get; }

        bool TryGetAvailableStorageMb(out long availableMb);

        bool HasEnoughStorageFor(long requiredMb);
    }

    public sealed class UnityDeviceDiagnostics : IDeviceDiagnostics
    {
        public long TotalSystemMemoryMb => SystemInfo.systemMemorySize;

        public bool TryGetAvailableStorageMb(out long availableMb)
        {
            availableMb = 0;
            var path = Application.persistentDataPath;
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(path) ?? path);
                availableMb = drive.AvailableFreeSpace / (1024 * 1024);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool HasEnoughStorageFor(long requiredMb)
        {
            // Unknown is treated as "assume it's fine" rather than blocking the player on a check
            // this platform cannot answer.
            return !TryGetAvailableStorageMb(out var availableMb) || availableMb >= requiredMb;
        }
    }
}
