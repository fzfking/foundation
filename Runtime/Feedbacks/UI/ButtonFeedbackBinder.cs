using System;
using System.Collections.Generic;
using Foundation.DI;
using Foundation.UI.UGui;
using UnityEngine;

namespace Foundation.Feedbacks.UI
{
    [Serializable]
    public sealed class ButtonFeedbackMapping
    {
        [SerializeField] private string stateId;
        [SerializeField] private FoundationButtonSignalKind signal;
        [SerializeField] private FeedbackId feedbackId;

        public string StateId => stateId;
        public FoundationButtonSignalKind Signal => signal;
        public FeedbackId FeedbackId => feedbackId;
    }

    [RequireComponent(typeof(FoundationButton))]
    [DisallowMultipleComponent]
    public sealed class ButtonFeedbackBinder : MonoBehaviour, IServiceResolverClient
    {
        [SerializeField] private FoundationButton button;
        [SerializeField] private List<ButtonFeedbackMapping> mappings = new();

        private readonly Dictionary<(string State, FoundationButtonSignalKind Signal), FeedbackId> _index = new();
        private IFeedbackService _feedbacks;
        private IDisposable _subscription;

        private void Awake()
        {
            if (button == null)
                button = GetComponent<FoundationButton>();
            BuildIndex();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void OnValidate() => BuildIndex();

        public void Bind(IServiceResolver resolver)
        {
            resolver.TryResolve(out _feedbacks);
            if (isActiveAndEnabled)
                Subscribe();
        }

        private void BuildIndex()
        {
            _index.Clear();
            if (mappings == null)
                return;
            foreach (var mapping in mappings)
            {
                if (mapping == null || mapping.FeedbackId.IsEmpty)
                    continue;
                _index[(mapping.StateId ?? string.Empty, mapping.Signal)] = mapping.FeedbackId;
            }
        }

        private void Subscribe()
        {
            if (button == null || _subscription != null)
                return;
            _subscription = button.Signals.Subscribe(OnSignal);
        }

        private void OnSignal(FoundationButtonSignal signal)
        {
            if (_feedbacks != null &&
                (_index.TryGetValue((signal.StateId, signal.Kind), out var id) ||
                 _index.TryGetValue((string.Empty, signal.Kind), out id)))
                _feedbacks.Play(id, FeedbackContext.For(button));
        }
    }
}
