using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Infrastructure.Addressables
{
    /// <summary>
    /// Answers one question for <see cref="AddressablesContentInitializer"/>: can the remote content host be
    /// reached right now. It only gates whether to try the network at all, not whether Addressables works.
    /// The host to probe is a project decision; <see cref="UnityWebRequestHostReachabilityChecker"/> is the
    /// ready implementation. Without any checker the initializer always tries the network.
    /// </summary>
    public interface IContentHostReachabilityChecker
    {
        UniTask<bool> IsReachableAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional add-on for a checker that can tell "the device has no network" from "a host probe failed".
    /// Only the former may stop a content attempt: a failed probe on a reachable network is unknown.
    /// A checker without this interface is not assumed to tell the two apart.
    /// </summary>
    public interface INetworkReachabilityStatusProvider
    {
        bool IsNetworkUnavailable { get; }
    }
}
