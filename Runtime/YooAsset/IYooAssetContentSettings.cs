namespace Foundation.Infrastructure.YooAsset
{
    public interface IYooAssetContentSettings
    {
        string PackageName { get; }
        bool UseEditorSimulation { get; }
        bool UseOfflinePlayerMode { get; }
    }
}
