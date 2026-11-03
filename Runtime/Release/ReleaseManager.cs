using System;

namespace Foundation.Release
{
    /// <summary>
    /// A release serves the clients it declares compatible with: the current one first, then the previous one
    /// for clients that have not updated yet. A client that fits neither must update.
    /// </summary>
    public sealed class ReleaseManager : IReleaseManager
    {
        public ReleaseSelection Select(string environmentId, ClientVersion clientVersion, ReleaseConfig config)
        {
            var environment = FindEnvironment(config, environmentId);
            if (environment == null)
                return new ReleaseSelection(ReleaseSelectionKind.Unknown, null);

            var current = environment.current;
            var previous = environment.previous;
            if (IsPublished(current) && IsCompatible(clientVersion, current))
                return new ReleaseSelection(ReleaseSelectionKind.Current, current);

            if (IsPublished(previous) && IsCompatible(clientVersion, previous))
                return new ReleaseSelection(ReleaseSelectionKind.Previous, previous);

            // Nothing the client may use is left: a published release it does not fit means it must update.
            var blocking = IsPublished(current) ? current : previous;
            if (IsPublished(blocking))
                return new ReleaseSelection(ReleaseSelectionKind.Incompatible, blocking);

            return new ReleaseSelection(ReleaseSelectionKind.Unknown, null);
        }

        private static ReleaseEnvironment FindEnvironment(ReleaseConfig config, string environmentId)
        {
            var environments = config?.environments;
            if (environments == null)
                return null;

            var count = environments.Length;
            for (var i = 0; i < count; i++)
            {
                if (string.Equals(environments[i]?.id, environmentId, StringComparison.Ordinal))
                    return environments[i];
            }

            return null;
        }

        private static bool IsPublished(ReleaseSlot slot) => slot != null && !string.IsNullOrEmpty(slot.release);

        // An unreadable minimum is no rule at all: a typo on the server must never lock players out of the game.
        private static bool IsCompatible(ClientVersion clientVersion, ReleaseSlot slot)
            => !ClientVersion.TryParse(slot.minClient, out var minimum) || clientVersion >= minimum;
    }
}
