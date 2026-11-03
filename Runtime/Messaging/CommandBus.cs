using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    public interface ICommandHandlerRegistry
    {
        /// <summary>Registers the handler of <typeparamref name="TCommand"/>; disposing the result removes it again.</summary>
        IDisposable RegisterHandler<TCommand>(ICommandHandler<TCommand> handler);
    }

    /// <summary>
    /// Sends a command straight to its one handler, looked up by the type the caller sends (never the runtime
    /// type), so a command is never mis-routed or dropped silently. A handler belongs to whoever registered
    /// it: a game context binds its handlers for its scope's lifetime (
    /// <see cref="MessagingServiceRegistryExtensions.RegisterCommandHandler{TCommand,THandler}"/>), so they leave
    /// with it and the context can be entered again. With <see cref="CommandExecutionMode.Serialized"/> commands run
    /// one at a time in the order they were sent.
    /// </summary>
    public sealed class CommandBus : ICommandBus, ICommandHandlerRegistry
    {
        private readonly Dictionary<Type, object> _handlers = new();
        private readonly SerialExecutionGate _gate;

        public CommandBus() : this(MessagingOptions.Default)
        {
        }

        public CommandBus(MessagingOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _gate = options.Commands == CommandExecutionMode.Serialized ? new SerialExecutionGate() : null;
        }

        public IDisposable RegisterHandler<TCommand>(ICommandHandler<TCommand> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (!_handlers.TryAdd(typeof(TCommand), handler))
            {
                throw new InvalidOperationException(
                    $"A command handler for '{typeof(TCommand).FullName}' is already registered.");
            }

            return new HandlerRegistration<Type>(_handlers, typeof(TCommand), handler);
        }

        public UniTask SendAsync<TCommand>(
            TCommand command,
            CancellationToken cancellationToken = default)
        {
            if (!_handlers.TryGetValue(typeof(TCommand), out var handler))
            {
                throw new InvalidOperationException(
                    $"No command handler is registered for '{typeof(TCommand).FullName}'.");
            }

            var typed = (ICommandHandler<TCommand>)handler;
            return _gate == null ? typed.HandleAsync(command, cancellationToken) : SendSerializedAsync(typed, command, cancellationToken);
        }

        private async UniTask SendSerializedAsync<TCommand>(
            ICommandHandler<TCommand> handler,
            TCommand command,
            CancellationToken cancellationToken)
        {
            await _gate.EnterAsync(cancellationToken);
            try
            {
                await handler.HandleAsync(command, cancellationToken);
            }
            finally
            {
                _gate.Exit();
            }
        }
    }
}