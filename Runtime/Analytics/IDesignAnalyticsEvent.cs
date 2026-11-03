using Foundation.Messaging;

namespace Foundation.Analytics
{
    /// <summary>
    /// A repeatable custom action (a purchase, a booster activation), as opposed to a funnel step (
    /// <see cref="IProgressionAnalyticsEvent"/>), a currency movement (<see cref="IResourceAnalyticsEvent"/>) or
    /// an ad moment (<see cref="IAdAnalyticsEvent"/>). Providers with a native "design event" use
    /// <see cref="EventId"/> and <see cref="Value"/>.
    /// </summary>
    public interface IDesignAnalyticsEvent : IAnalyticsEvent
    {
        /// <summary>Structured, stable identifier for providers with a hierarchical design-event
        /// concept. <see cref="IAnalyticsEvent.Name"/> stays the legacy/flat identifier so a
        /// provider (or dashboard) already keyed off it is unaffected.</summary>
        AnalyticsEventId EventId { get; }

        /// <summary>
        /// The one metric worth native numeric aggregation (e.g. the premium currency gained on a prestige
        /// reset), or null. Never pack several numbers here: the rest belongs in
        /// <see cref="IAnalyticsEvent.Parameters"/>.
        /// </summary>
        double? Value { get; }
    }
}
