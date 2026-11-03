using System;
using System.Collections.Generic;
using Foundation.ContentMirroring;
using YooAsset;

namespace Foundation.Infrastructure.YooAsset
{
    internal sealed class YooAssetRemoteService : IRemoteService
    {
        private readonly string _baseUrl;
        private ContentMirror _mirror;

        public YooAssetRemoteService(string remoteUrl)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl))
                throw new ArgumentException("The remote URL must not be empty.", nameof(remoteUrl));

            _baseUrl = remoteUrl.TrimEnd('/') + "/";
        }

        /// <summary>Selects a mirror; null returns requests to the package's primary remote URL.</summary>
        public void ApplyMirror(ContentMirror mirror) => _mirror = mirror;

        public IReadOnlyList<string> GetRemoteUrls(string fileName)
        {
            var url = _baseUrl + fileName;
            return new[] { _mirror == null ? url : _mirror.RouteUrl(url) };
        }
    }
}
