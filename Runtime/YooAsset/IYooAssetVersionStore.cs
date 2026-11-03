namespace Foundation.Infrastructure.YooAsset
{
    /// <summary>
    /// Remembers the package version whose files are all on the device. YooAsset itself cannot say that offline, so the
    /// synchronizer records a version only after every file of it has been downloaded.
    /// </summary>
    public interface IYooAssetVersionStore
    {
        string Read(string packageName);
        void Write(string packageName, string version);
    }
}
