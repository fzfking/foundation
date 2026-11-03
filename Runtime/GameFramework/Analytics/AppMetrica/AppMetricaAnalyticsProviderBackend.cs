using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Foundation.Analytics;
using Foundation.Messaging;
using Cysharp.Threading.Tasks;

namespace Foundation.GameFramework.Analytics.AppMetrica
{
    /// <summary>
    /// Forwards analytics events to AppMetrica. <c>ReportEvent</c> takes a JSON string, so parameters are
    /// serialized to a small hand-built JSON object instead of pulling in a JSON library. Reports only while
    /// <see cref="AppMetricaActivation.IsReporting"/>; before consent or after revocation events are dropped
    /// here, because the SDK may already run anonymously for another SDK.
    /// </summary>
    public sealed class AppMetricaAnalyticsProviderBackend : IAnalyticsProviderBackend
    {
        public UniTask TrackAsync(IAnalyticsEvent analyticsEvent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!AppMetricaActivation.IsReporting)
                return UniTask.CompletedTask;

            if (analyticsEvent.Parameters == null || analyticsEvent.Parameters.Count == 0)
            {
                Io.AppMetrica.AppMetrica.ReportEvent(analyticsEvent.Name);
                return UniTask.CompletedTask;
            }

            Io.AppMetrica.AppMetrica.ReportEvent(analyticsEvent.Name, ToJsonValue(analyticsEvent.Parameters));
            return UniTask.CompletedTask;
        }

        internal static string ToJsonValue(IReadOnlyDictionary<string, object> parameters)
        {
            var builder = new StringBuilder();
            builder.Append('{');
            var first = true;
            foreach (var pair in parameters)
            {
                if (!first)
                    builder.Append(',');
                first = false;

                AppendJsonString(builder, pair.Key);
                builder.Append(':');
                AppendJsonString(builder, pair.Value?.ToString() ?? string.Empty);
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }

            builder.Append('"');
        }
    }
}
