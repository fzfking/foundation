using Foundation.Messaging;

namespace Foundation.Analytics
{
    public enum AnalyticsResourceFlow
    {
        Source,

        Sink,
    }

    /// <summary>
    /// A real movement of a virtual currency in or out of the balance. Only for currencies where an event per
    /// transaction makes sense, not for passive production where every tick would become an event.
    /// </summary>
    public interface IResourceAnalyticsEvent : IAnalyticsEvent
    {
        AnalyticsResourceFlow Flow { get; }

        /// <summary>The provider-facing currency code (e.g. "PremiumCoin") - not necessarily the
        /// same string a legacy <see cref="IAnalyticsEvent.Parameters"/> entry already uses for
        /// this currency, since that string may be tied to an existing dashboard.</summary>
        string Currency { get; }

        /// <summary>The linear (not log-scaled) amount moved. Only ever use this for currencies
        /// whose amounts stay within a plain numeric range - see the interface doc.</summary>
        double Amount { get; }

        /// <summary>Stable category for this transaction (e.g. "PrestigeReset", "UpgradePurchase") -
        /// from a small, finite set of concepts that actually exist in the game, matching whatever
        /// is declared in the provider's own Resource Item Type whitelist.</summary>
        string ItemType { get; }

        /// <summary>The specific object/reason within <see cref="ItemType"/> (e.g. a specific
        /// upgrade's config id) - a stable, finite, game-config-derived identifier, never a
        /// dynamically generated one.</summary>
        string ItemId { get; }
    }
}
