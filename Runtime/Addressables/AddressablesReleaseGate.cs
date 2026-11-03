using Cysharp.Threading.Tasks;
using Foundation.Logging;
using Foundation.Release;
using UnityEngine;

namespace Foundation.Infrastructure.Addressables
{
    /// <summary>What a client's release check decided.</summary>
    public readonly struct ReleaseGateResult
    {
        /// <summary>True when this client cannot play and must be updated first.</summary>
        public bool UpdateRequired { get; }

        /// <summary>Where the player is sent to update; meaningful when <see cref="UpdateRequired"/>.</summary>
        public string StoreUrl { get; }

        public ReleaseGateResult(bool updateRequired, string storeUrl)
        {
            UpdateRequired = updateRequired;
            StoreUrl = storeUrl;
        }
    }

    /// <summary>
    /// Applies the release configuration to a starting client: selects the release for its version and environment,
    /// points Addressables at that release's content folder, and says whether the client must update. Start loading the
    /// configuration early (it is a network request) and evaluate it when the boot needs the answer, typically as the
    /// <c>afterInitialize</c> gate of <see cref="ContentStartupBootStep"/>.
    /// </summary>
    public sealed class AddressablesReleaseGate
    {
        private readonly UniTask<ReleaseConfigLoadResult> _load;
        private readonly string _environmentId;
        private readonly string _contentRoot;
        private readonly string _fallbackStoreUrl;
        private readonly string _clientVersionText;
        private readonly IAppLogger _logger;

        /// <param name="load">The configuration being loaded, started by the caller.</param>
        /// <param name="environmentId">The environment this build belongs to, such as production or staging.</param>
        /// <param name="contentRoot">The content folder the build profile bakes in; release folders replace its environment segment.</param>
        /// <param name="fallbackStoreUrl">The store page used when the configuration names none.</param>
        /// <param name="clientVersionText">The client's version, normally <see cref="Application.version"/>.</param>
        public AddressablesReleaseGate(
            UniTask<ReleaseConfigLoadResult> load,
            string environmentId,
            string contentRoot,
            string fallbackStoreUrl,
            string clientVersionText,
            IAppLogger logger = null)
        {
            _load = load;
            _environmentId = environmentId;
            _contentRoot = contentRoot;
            _fallbackStoreUrl = fallbackStoreUrl;
            _clientVersionText = clientVersionText;
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
        }

        public async UniTask<ReleaseGateResult> EvaluateAsync()
        {
            var release = await _load;
            if (!ClientVersion.TryParse(_clientVersionText, out var clientVersion))
            {
                _logger.Log(LogLevel.Warning, "Release", $"client version '{_clientVersionText}' is not dotted; update checks are skipped and the built content folder is used.");
                AddressablesEndpointRouting.Apply(_contentRoot, null);
                return default;
            }

            var selection = new ReleaseManager().Select(_environmentId, clientVersion, release.Config);
            var updates = new ClientUpdateService(_fallbackStoreUrl);
            var status = updates.Evaluate(clientVersion, release.Config, selection);
            // Null when the configuration names no folder for this client, so the folder the build was made for stays.
            var endpoint = new ContentEndpointResolver(_contentRoot).Resolve(selection);
            AddressablesEndpointRouting.Apply(_contentRoot, endpoint);
            _logger.Log(LogLevel.Info, "Release", $"client {clientVersion}, environment {_environmentId} from {release.Origin}: " +
                $"{selection.Kind}, update {status}, content {endpoint ?? "from the build"}.");

            return status == ClientUpdateStatus.Required
                ? new ReleaseGateResult(true, updates.StoreUrl(release.Config))
                : default;
        }
    }
}
