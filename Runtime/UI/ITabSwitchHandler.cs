using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.UI
{
    /// <summary>
    /// One asynchronous reaction to a tab selection: switching a router, toggling containers, following the
    /// tab buttons' animation with a layout refresh. A tab group starts every handler it has when a tab is
    /// selected and finishes the switch when all of them have completed; what the handlers do, and whether they
    /// depend on each other, is not the group's concern.
    /// </summary>
    public interface ITabSwitchHandler
    {
        UniTask HandleAsync(TabSwitch tabSwitch, CancellationToken cancellationToken);
    }

    /// <summary>The state of one tab switch, passed to every <see cref="ITabSwitchHandler"/>.</summary>
    public readonly struct TabSwitch
    {
        public TabSwitch(string previousTabId, string tabId, int direction, UniTask selectionVisuals)
        {
            PreviousTabId = previousTabId;
            TabId = tabId;
            Direction = direction;
            SelectionVisuals = selectionVisuals;
        }

        /// <summary>The tab that was selected before, or null for the group's first selection.</summary>
        public string PreviousTabId { get; }

        public string TabId { get; }

        /// <summary>+1 if the new tab sits to the right of the previous one (or there was none), -1 if to the left.</summary>
        public int Direction { get; }

        /// <summary>Completes when the tab buttons have finished their selected/deselected animation.</summary>
        public UniTask SelectionVisuals { get; }
    }
}
