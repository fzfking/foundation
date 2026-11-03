using System;
using UnityEditor;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Foundation.UI.UGui.Editor
{
    public static class FoundationButtonInspectorPresentation
    {
        public static Func<SerializedObject, Object[], VisualElement> Factory { get; set; }
    }
}
