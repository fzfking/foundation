using System;
using System.Collections.Generic;

namespace Foundation.GameFramework.Notifications
{
    public sealed class NotificationSpacingQueue
    {
        private readonly TimeSpan _minimumSpacing;
        private readonly List<DateTime> _pendingFireTimes = new();

        public NotificationSpacingQueue(TimeSpan? minimumSpacing = null)
        {
            _minimumSpacing = minimumSpacing ?? TimeSpan.FromMinutes(30);
        }

        public DateTime Reserve(DateTime requestedFireAtUtc)
        {
            var candidate = requestedFireAtUtc;

            var conflictFound = true;
            while (conflictFound)
            {
                conflictFound = false;
                foreach (var existing in _pendingFireTimes)
                {
                    if ((candidate - existing).Duration() < _minimumSpacing)
                    {
                        candidate = existing + _minimumSpacing;
                        conflictFound = true;
                    }
                }
            }

            _pendingFireTimes.Add(candidate);
            _pendingFireTimes.Sort();
            return candidate;
        }

        public void Clear() => _pendingFireTimes.Clear();
    }
}
