using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.GameFramework.Sharing
{
    public interface IUrlLauncher
    {
        /// <summary>
        /// Opens a URL if cancellation has not been requested.
        /// </summary>
        /// <remarks>Cancellation can prevent the system handler from starting, but cannot stop it after launch.</remarks>
        UniTask OpenAsync(string url, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Opens a URL in the system handler. A URL often comes from content or a server, and the system will start whatever app
    /// owns its scheme, so only the schemes the project lists are opened (web, mail and store links by default).
    /// </summary>
    public sealed class UnityUrlLauncher : IUrlLauncher
    {
        private static readonly string[] DefaultSchemes = { "https", "http", "mailto", "market", "itms-apps" };

        private readonly HashSet<string> _allowedSchemes;

        public UnityUrlLauncher(IEnumerable<string> allowedSchemes = null)
        {
            _allowedSchemes = new HashSet<string>(allowedSchemes ?? DefaultSchemes, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsAllowed(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out var uri) && _allowedSchemes.Contains(uri.Scheme);

        public UniTask OpenAsync(string url, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsAllowed(url))
                throw new ArgumentException($"'{url}' is not a link this launcher opens.", nameof(url));

            Application.OpenURL(url);
            return UniTask.CompletedTask;
        }
    }
}
