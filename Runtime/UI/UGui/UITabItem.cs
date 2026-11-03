using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.UI.UGui
{
    /// <summary>
    /// One tab button. It shows the button state named by <c>normalStateId</c> or <c>selectedStateId</c>, unless a
    /// game has put another state over it (<see cref="SetStateOverride"/>: locked, highlighted, whatever the
    /// game's buttons define), and can be made to ignore clicks. What decides to lock or highlight a tab belongs
    /// to the game.
    /// </summary>
    public sealed class UITabItem : MonoBehaviour
    {
        [SerializeField] private string tabId;
        [SerializeField] private FoundationButton button;
        [SerializeField] private string normalStateId = "normal";
        [SerializeField] private string selectedStateId = "selected";
        [SerializeField] private GameObject selectedState;
        [SerializeField] private GameObject unselectedState;
        [SerializeField] private UITabSelectionVisual[] selectionVisuals;

        private IDisposable _clickSubscription;
        private string _stateOverride;
        private bool _selected;
        private bool _clickBlocked;

        internal event Action<UITabItem> Clicked;

        public string TabId => tabId;
        public FoundationButton Button => button;
        public bool IsSelected => _selected;
        public string StateOverride => _stateOverride;

        private void Awake()
        {
            if (button == null)
                return;

            _clickSubscription = button.Clicked.Subscribe(HandleClicked);
        }

        private void OnDestroy()
        {
            _clickSubscription?.Dispose();
            _clickSubscription = null;
        }

        /// <summary>Shows <paramref name="stateId"/> on the button instead of the selected/normal one; null removes it.</summary>
        public void SetStateOverride(string stateId)
        {
            _stateOverride = stateId;
            ApplyButtonState();
        }

        /// <summary>While blocked a click on the tab does nothing; selecting from code still works.</summary>
        public void SetClickBlocked(bool blocked) => _clickBlocked = blocked;

        internal async UniTask SetSelectedAsync(bool selected, CancellationToken cancellationToken)
        {
            _selected = selected;
            ApplyButtonState();

            if (selectedState != null && selectedState.activeSelf != selected)
                selectedState.SetActive(selected);
            if (unselectedState != null && unselectedState.activeSelf == selected)
                unselectedState.SetActive(!selected);

            if (selectionVisuals == null)
                return;

            // One visual is the usual case; the list is only needed to start several at once.
            if (selectionVisuals.Length == 1)
            {
                if (selectionVisuals[0] != null)
                    await selectionVisuals[0].PlayAsync(selected, cancellationToken);
                return;
            }

            var tasks = new List<UniTask>(selectionVisuals.Length);
            foreach (var visual in selectionVisuals)
            {
                if (visual != null)
                    tasks.Add(visual.PlayAsync(selected, cancellationToken));
            }

            await UniTask.WhenAll(tasks);
        }

        private void ApplyButtonState()
        {
            if (button == null)
                return;

            var state = _stateOverride;
            if (string.IsNullOrEmpty(state) || !button.HasState(state))
                state = _selected ? selectedStateId : normalStateId;
            if (button.HasState(state))
                button.SetState(state);
        }

        private void HandleClicked(FoundationButtonClick click)
        {
            if (_clickBlocked)
                return;

            Clicked?.Invoke(this);
        }
    }
}
