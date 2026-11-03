using System.Collections.Generic;
using System.Threading;
using System;
using Foundation.Analytics;
using Foundation.Messaging;
using Cysharp.Threading.Tasks;
using Firebase.Analytics;

namespace Foundation.GameFramework.Analytics.Firebase
{
    /// <summary>
    /// Forwards analytics events to Firebase with names made valid by <see cref="FirebaseEventFormat"/>.
    /// Numbers and booleans keep their type, anything else is sent as text. Does nothing while
    /// <see cref="FirebaseAnalyticsCollection.IsEnabled"/> is false; the consent gate turns it on.
    /// </summary>
    public sealed class FirebaseAnalyticsProviderBackend : IAnalyticsProviderBackend
    {
        public UniTask TrackAsync(IAnalyticsEvent analyticsEvent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FirebaseAnalyticsCollection.IsEnabled)
                return UniTask.CompletedTask;

            var name = FirebaseEventFormat.SanitizeName(analyticsEvent.Name);
            var source = analyticsEvent.Parameters;
            if (source == null || source.Count == 0)
            {
                FirebaseAnalytics.LogEvent(name);
                return UniTask.CompletedTask;
            }

            var parameters = new List<Parameter>(Math.Min(source.Count, FirebaseEventFormat.MaxParameters));
            foreach (var pair in source)
            {
                if (parameters.Count == FirebaseEventFormat.MaxParameters)
                    break;
                parameters.Add(ToParameter(FirebaseEventFormat.SanitizeName(pair.Key), pair.Value));
            }

            FirebaseAnalytics.LogEvent(name, parameters.ToArray());
            return UniTask.CompletedTask;
        }

        private static Parameter ToParameter(string name, object value)
        {
            switch (value)
            {
                case bool flag:
                    return new Parameter(name, flag ? 1L : 0L);
                case int number:
                    return new Parameter(name, (long)number);
                case long number:
                    return new Parameter(name, number);
                case float number:
                    return new Parameter(name, (double)number);
                case double number:
                    return new Parameter(name, number);
                default:
                    return new Parameter(name, FirebaseEventFormat.TrimValue(value?.ToString()));
            }
        }
    }
}
