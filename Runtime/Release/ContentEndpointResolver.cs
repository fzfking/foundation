using System;

namespace Foundation.Release
{
    /// <summary>
    /// Turns the selected release into the folder its content is served from. Backends (Addressables) apply
    /// the result themselves, so release logic depends on none of them.
    /// </summary>
    public interface IContentEndpointResolver
    {
        /// <summary>The folder URL, ending with '/', or null when the selection cannot be served.</summary>
        string Resolve(ReleaseSelection selection);
    }

    public sealed class ContentEndpointResolver : IContentEndpointResolver
    {
        private readonly string _contentRoot;

        public ContentEndpointResolver(string contentRoot)
        {
            if (string.IsNullOrEmpty(contentRoot))
                throw new ArgumentException("A content root is required.", nameof(contentRoot));

            _contentRoot = contentRoot.EndsWith('/') ? contentRoot : contentRoot + "/";
        }

        public string Resolve(ReleaseSelection selection)
        {
            if (!selection.CanServe)
                return null;

            var path = selection.Slot.path?.Trim('/');
            if (string.IsNullOrEmpty(path))
                return null;

            var segments = path.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                if (string.IsNullOrWhiteSpace(segment)
                    || segment.Trim().Trim('.').Length == 0
                    || segment.Contains('\\')
                    || segment.Contains('%'))
                {
                    return null;
                }

                try
                {
                    segments[index] = Uri.EscapeDataString(segment);
                }
                catch (UriFormatException)
                {
                    return null;
                }
            }

            return _contentRoot + string.Join("/", segments) + "/";
        }
    }
}
