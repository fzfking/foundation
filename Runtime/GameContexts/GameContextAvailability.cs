using System;
using System.Globalization;

namespace Foundation.GameContexts
{
    /// <summary>
    /// When a context can be entered: always, or within a UTC window such as a season. The window is
    /// half-open: it opens at <see cref="From"/> and is closed from <see cref="Until"/>. The device clock is
    /// judged, and the player can move it; a season that must not open early needs a trusted time passed to
    /// <see cref="IsOpenAt"/>.
    /// </summary>
    public readonly struct GameContextAvailability
    {
        /// <summary>Open from any time on: the default, and every compiled-in context.</summary>
        public static GameContextAvailability Always => default;

        public DateTimeOffset? From { get; }
        public DateTimeOffset? Until { get; }

        public bool IsLimited => From.HasValue || Until.HasValue;

        public GameContextAvailability(DateTimeOffset? from, DateTimeOffset? until)
        {
            if (from.HasValue && until.HasValue && until.Value <= from.Value)
                throw new ArgumentException("A context's availability must end after it starts.", nameof(until));

            From = from;
            Until = until;
        }

        public bool IsOpenAt(DateTimeOffset now)
            => (!From.HasValue || now >= From.Value) && (!Until.HasValue || now < Until.Value);

        /// <summary>How long is left until it closes; null when it never does or is not open.</summary>
        public TimeSpan? RemainingAt(DateTimeOffset now)
            => Until.HasValue && IsOpenAt(now) ? Until.Value - now : (TimeSpan?)null;

        /// <summary>Reads the authored window (ISO 8601 UTC, an empty side being open). False with a reason when it is malformed.</summary>
        public static bool TryParse(string from, string until, out GameContextAvailability availability, out string problem)
        {
            availability = Always;
            problem = null;
            if (!TryParseSide(from, out var start))
            {
                problem = $"'{from}' is not an ISO 8601 UTC time.";
                return false;
            }

            if (!TryParseSide(until, out var end))
            {
                problem = $"'{until}' is not an ISO 8601 UTC time.";
                return false;
            }

            if (start.HasValue && end.HasValue && end.Value <= start.Value)
            {
                problem = $"It ends ({until}) before it starts ({from}).";
                return false;
            }

            availability = new GameContextAvailability(start, end);
            return true;
        }

        private static bool TryParseSide(string value, out DateTimeOffset? time)
        {
            time = null;
            if (string.IsNullOrWhiteSpace(value))
                return true;

            if (!DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return false;

            time = parsed;
            return true;
        }
    }

    /// <summary>
    /// The season the running context belongs to, registered in every context's scope, so a seasonal event's
    /// shared logic can read which season it is and when it ends. A non-seasonal context has no season id.
    /// </summary>
    public sealed class GameContextSeason
    {
        public string SeasonId { get; }

        public bool IsSeasonal => SeasonId != null;

        public GameContextAvailability Availability { get; }

        public GameContextSeason(string seasonId, GameContextAvailability availability)
        {
            SeasonId = string.IsNullOrWhiteSpace(seasonId) ? null : seasonId;
            Availability = availability;
        }
    }
}
