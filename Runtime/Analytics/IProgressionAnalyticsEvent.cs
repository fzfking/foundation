using Foundation.Messaging;

namespace Foundation.Analytics
{
    /// <summary>
    /// Provider-independent progression status. Deliberately not GameAnalytics' enum, so no SDK type leaks
    /// into gameplay; providers without a progression concept ignore it.
    /// </summary>
    public enum AnalyticsProgressionStatus
    {
        Start,
        Complete,
        Fail,
    }

    /// <summary>
    /// A step in a sequential flow the player goes through once per attempt (tutorial steps, an unlock
    /// sequence), as opposed to a repeatable action (<see cref="IDesignAnalyticsEvent"/>). Progression01-03
    /// mirror GameAnalytics' hierarchy without its SDK types.
    /// </summary>
    public interface IProgressionAnalyticsEvent : IAnalyticsEvent
    {
        AnalyticsProgressionStatus Status { get; }

        string Progression01 { get; }
        string Progression02 { get; }
        string Progression03 { get; }

        double? Value { get; }
    }
}
