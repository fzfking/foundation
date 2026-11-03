using UnityEngine;

namespace Foundation.UI
{
    /// <summary>
    /// The definitions every popup opens with: on the popup layer, destroyed when it closes, and never merged with another
    /// open popup, so each opening is its own instance.
    /// </summary>
    public static class PopupDefinitions
    {
        public static UiDefinition FromAddress(string viewId, string address)
            => new(viewId, new AddressViewSource(address), UILayer.Popup, UICachePolicy.DestroyOnClose, blockInput: true, allowBack: true, allowDuplicates: true);

        public static UiDefinition FromPrefab(string viewId, GameObject prefab)
            => new(viewId, new PrefabViewSource(prefab), UILayer.Popup, UICachePolicy.DestroyOnClose, blockInput: true, allowBack: true, allowDuplicates: true);
    }
}
