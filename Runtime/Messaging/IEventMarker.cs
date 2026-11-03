using System.Collections.Generic;
namespace Foundation.Messaging
{
    public interface IEventMarker
    {
    }

    public interface IGameplayEvent : IEventMarker
    {
    }

    public interface IAnalyticsEvent : IEventMarker
    {
        string Name { get; }
        IReadOnlyDictionary<string, object> Parameters { get; }
    }

    public interface IApplicationEvent : IEventMarker
    {
    }
}
