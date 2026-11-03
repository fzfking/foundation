using Foundation.Messaging;

namespace Foundation.ErrorHandling
{
    public sealed class ErrorReportedEvent : IApplicationEvent
    {
        public ErrorContext Context { get; }

        public ErrorReportedEvent(ErrorContext context)
        {
            Context = context;
        }
    }
}
