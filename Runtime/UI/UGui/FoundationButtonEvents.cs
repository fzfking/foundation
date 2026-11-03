using System;

namespace Foundation.UI.UGui
{
    public readonly struct FoundationButtonClick
    {
        public FoundationButton Button { get; }
        public string StateId { get; }

        public FoundationButtonClick(FoundationButton button, string stateId)
        {
            Button = button;
            StateId = stateId;
        }
    }

    public enum FoundationButtonSignalKind
    {
        Click,
        PointerDown,
        PointerUp,
        Selected,
        Deselected,
        LongPress,
        Invalid,
    }

    public readonly struct FoundationButtonSignal
    {
        public FoundationButtonSignalKind Kind { get; }
        public string StateId { get; }

        public FoundationButtonSignal(FoundationButtonSignalKind kind, string stateId)
        {
            Kind = kind;
            StateId = stateId;
        }
    }

    internal sealed class FoundationButtonSubscription : IDisposable
    {
        private readonly FoundationButton.AsyncHandler _handler;
        private FoundationButton _owner;

        public FoundationButtonSubscription(FoundationButton owner, FoundationButton.AsyncHandler handler)
        {
            _owner = owner;
            _handler = handler;
        }

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            if (owner != null)
                owner.Unsubscribe(_handler);
        }
    }
}
