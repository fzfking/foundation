namespace Foundation.Messaging
{
    /// <summary>How the handlers of one published event run.</summary>
    public enum EventDispatchMode
    {
        /// <summary>
        /// In the order they subscribed, each one finishing before the next starts. A later handler sees what an
        /// earlier one did, so the outcome does not depend on timing.
        /// </summary>
        Sequential,

        /// <summary>
        /// Every handler starts at once (in subscription order, up to its first await) and the publish waits for all
        /// of them unless it is cancelled. A cancelled publish throws instead of waiting, and handlers already
        /// running continue on their own: each must observe the token to stop. Faster when handlers are independent
        /// and slow, but their completion order is up to them.
        /// </summary>
        Concurrent,
    }

    /// <summary>How the command bus runs commands that arrive while another is still being handled.</summary>
    public enum CommandExecutionMode
    {
        /// <summary>A command goes to its handler at once; commands may overlap.</summary>
        Immediate,

        /// <summary>
        /// One command at a time, in the order they were sent: a command waits until the one before it has finished.
        /// For state that must change in a strict order, such as a networked game. A handler must not await another
        /// command sent through the same bus, since that command would wait for the handler that awaits it.
        /// </summary>
        Serialized,
    }

    /// <summary>The ordering the buses guarantee, chosen once where they are registered.</summary>
    public sealed class MessagingOptions
    {
        public static MessagingOptions Default { get; } = new MessagingOptions();

        public EventDispatchMode Events { get; }

        public CommandExecutionMode Commands { get; }

        public MessagingOptions(
            EventDispatchMode events = EventDispatchMode.Sequential,
            CommandExecutionMode commands = CommandExecutionMode.Immediate)
        {
            Events = events;
            Commands = commands;
        }
    }
}
