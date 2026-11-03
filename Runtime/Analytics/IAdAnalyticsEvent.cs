using Foundation.Messaging;

namespace Foundation.Analytics
{
    public enum AnalyticsAdAction
    {
        Request,
        Loaded,
        Show,
        Clicked,
        RewardReceived,
        FailedShow,
    }

    public enum AnalyticsAdType
    {
        RewardedVideo,
        Interstitial,
        OfferWall,
        Banner,
        AppOpen,
    }

    /// <summary>
    /// A moment in a rewarded/interstitial ad's lifecycle (requested, shown, rewarded, failed). Providers
    /// with a native ad-event API map it directly; others read Name/Parameters.
    /// </summary>
    public interface IAdAnalyticsEvent : IAnalyticsEvent
    {
        AnalyticsAdAction Action { get; }
        AnalyticsAdType AdType { get; }

        /// <summary>Where in the game this ad was offered/shown (e.g. "offline_reward_double") -
        /// a stable, finite identifier from the game's own placement list.</summary>
        string Placement { get; }
    }
}
