using System;
using System.IO;
using UnityEngine;

namespace Foundation.Save
{
    public interface ILocalSaveLocation
    {
        string GetSaveFilePath(string slotId);
    }

    public sealed class PersistentDataSaveLocation : ILocalSaveLocation
    {
        private readonly string _defaultFileName;

        public PersistentDataSaveLocation(string defaultFileName = "save.dat")
        {
            ThrowIfNotAFileName(defaultFileName, nameof(defaultFileName));
            _defaultFileName = defaultFileName;
        }

        public string GetSaveFilePath(string slotId)
        {
            if (slotId != SaveSlot.Default)
                ThrowIfNotAFileName(slotId, nameof(slotId), rejectDoubleDot: true);

            var fileName = slotId == SaveSlot.Default ? _defaultFileName : $"save-{slotId}.dat";
            return Path.Combine(Application.persistentDataPath, fileName);
        }

        // Save names must be a single path segment so writes stay inside persistentDataPath.
        private static void ThrowIfNotAFileName(string fileName, string parameterName, bool rejectDoubleDot = false)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName == "."
                || fileName == ".."
                || (rejectDoubleDot && fileName.Contains(".."))
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                throw new ArgumentException($"'{fileName}' must be a file name, not a path.", parameterName);
            }
        }
    }
}
