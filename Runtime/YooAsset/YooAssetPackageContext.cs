using System;
using YooAsset;

namespace Foundation.Infrastructure.YooAsset
{
    public sealed class YooAssetPackageContext
    {
        private ResourcePackage _package;

        public bool IsInitialized => _package != null;

        public ResourcePackage Package => _package ?? throw new InvalidOperationException(
            "YooAsset resource package is not initialized. Await YooAssetContentInitializer.InitializeAsync first.");

        internal void Publish(ResourcePackage package)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));
            if (_package != null)
                throw new InvalidOperationException("YooAsset resource package has already been published.");

            _package = package;
        }

        internal void Clear(ResourcePackage package)
        {
            if (ReferenceEquals(_package, package))
                _package = null;
        }
    }
}
