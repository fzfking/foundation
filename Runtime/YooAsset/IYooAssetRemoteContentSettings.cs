using Foundation.Content;

namespace Foundation.Infrastructure.YooAsset
{
    /// <summary>
    /// Opts a project into host play mode: the package is read from the build and the local cache, and updated from
    /// <see cref="RemoteUrl"/> when it is reachable (see <see cref="YooAssetContentSynchronizer"/>). The offline-first part is
    /// optional and chosen by <see cref="StartupStrategy"/>.
    /// </summary>
    public interface IYooAssetRemoteContentSettings : IYooAssetContentSettings
    {
        /// <summary>The folder that holds the package files; a bundle or manifest name is appended to it.</summary>
        string RemoteUrl { get; }

        /// <summary>
        /// The version of the package copied into the build, or empty when the build carries none. For an offline first
        /// launch, the configured version must match the built-in package and all required bundles must be available.
        /// </summary>
        string BuiltinPackageVersion { get; }

        /// <summary>
        /// <see cref="ContentStartupStrategy.OfflineFirst"/> synchronizes through a <see cref="YooAssetContentSynchronizer"/>.
        /// <see cref="ContentStartupStrategy.Standard"/> is YooAsset's plain host mode: the newest version is requested from
        /// the host at start, files are fetched when they are first loaded, and an unreachable host fails the initialization.
        /// </summary>
        ContentStartupStrategy StartupStrategy { get; }
    }
}
