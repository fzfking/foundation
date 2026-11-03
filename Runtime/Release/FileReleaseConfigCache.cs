using System.IO;

namespace Foundation.Release
{
    /// <summary>The last release configuration the server gave this device, kept for when it cannot be reached.</summary>
    public interface IReleaseConfigCache
    {
        bool TryLoad(out string json);

        void Save(string json);
    }

    public sealed class FileReleaseConfigCache : IReleaseConfigCache
    {
        private readonly string _path;

        public FileReleaseConfigCache(string path)
        {
            _path = path;
        }

        public bool TryLoad(out string json)
        {
            json = null;
            if (!File.Exists(_path))
                return false;

            json = File.ReadAllText(_path);
            return true;
        }

        // Written beside the file and moved over it, so an interrupted save never leaves a half-written configuration behind.
        public void Save(string json)
        {
            var temporary = _path + ".tmp";
            try
            {
                File.WriteAllText(temporary, json);
                if (File.Exists(_path))
                    File.Replace(temporary, _path, null);
                else
                    File.Move(temporary, _path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
