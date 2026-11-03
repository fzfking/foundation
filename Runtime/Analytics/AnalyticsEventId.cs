using Foundation.Messaging;

namespace Foundation.Analytics
{
    /// <summary>
    /// A structured analytics event id: category/action plus optional subject/variant, instead of a
    /// hand-assembled ":"-joined string. A provider that wants a hierarchical id (GameAnalytics) calls
    /// <see cref="ToString"/>; others can use <see cref="IAnalyticsEvent.Name"/>.
    /// Every part must be a stable, finite, config-derived identifier, never a localized string or a runtime
    /// number: dynamic data belongs in Value/Parameters, or the event cardinality explodes.
    /// </summary>
    public readonly struct AnalyticsEventId
    {
        public string Category { get; }
        public string Action { get; }
        public string Subject { get; }
        public string Variant { get; }

        public AnalyticsEventId(string category, string action, string subject = null, string variant = null)
        {
            Category = category;
            Action = action;
            Subject = subject;
            Variant = variant;
        }

        public override string ToString()
        {
            var value = Category + ":" + Action;
            if (!string.IsNullOrEmpty(Subject))
                value += ":" + Subject;
            if (!string.IsNullOrEmpty(Variant))
                value += ":" + Variant;
            return value;
        }
    }
}
