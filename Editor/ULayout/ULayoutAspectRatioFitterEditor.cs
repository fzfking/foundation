using Foundation.UI.ULayout;
using Poke.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEditorBase = UnityEditor.Editor;

namespace Foundation.UI.ULayout.Editor
{
    [CustomEditor(typeof(ULayoutAspectRatioFitter))]
    public sealed class ULayoutAspectRatioFitterEditor : UnityEditorBase
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var fitter = (ULayoutAspectRatioFitter)target;
            if (GUI.changed)
                fitter.Refresh();

            EditorGUILayout.HelpBox(
                (fitter.Mode == ULayoutAspectRatioFitter.FitMode.HeightFromWidth
                    ? "uLayout controls slot width; the fitter sets its height. Use Grow X / Fixed Y. Content is optional."
                    : "uLayout controls this slot; the fitter contains a direct child inside it. Use Grow on the slot and Fixed sizing on the content."),
                MessageType.Info);

            if (fitter.GetComponent<Layout>())
                EditorGUILayout.HelpBox("Use LayoutItem on this slot, not Layout: Layout would also control the content.", MessageType.Error);

            if (fitter.Mode == ULayoutAspectRatioFitter.FitMode.HeightFromWidth)
            {
                var slot = fitter.GetComponent<LayoutItem>();
                if (slot.Sizing.y == SizingMode.Grow)
                    EditorGUILayout.HelpBox("Set slot height to Fixed: Grow Y competes with Height From Width.", MessageType.Error);
                if (GUILayout.Button("Configure Slot: Grow Width / Fixed Height"))
                {
                    var serializedSlot = new SerializedObject(slot);
                    var sizing = serializedSlot.FindProperty("m_sizing");
                    sizing.FindPropertyRelative("x").enumValueIndex = (int)SizingMode.Grow;
                    sizing.FindPropertyRelative("y").enumValueIndex = (int)SizingMode.Fixed;
                    serializedSlot.ApplyModifiedProperties();
                    slot.SetDirty();
                    fitter.Refresh();
                }
            }

            var content = fitter.Content;
            if (!content)
            {
                if (GUILayout.Button("Create Content Child"))
                {
                    var child = new GameObject("Content", typeof(RectTransform));
                    Undo.RegisterCreatedObjectUndo(child, "Create aspect ratio content");
                    Undo.SetTransformParent(child.transform, fitter.transform, "Parent aspect ratio content");
                    child.transform.localPosition = Vector3.zero;
                    child.transform.localRotation = Quaternion.identity;
                    child.transform.localScale = Vector3.one;
                    Undo.RecordObject(fitter, "Assign aspect ratio content");
                    fitter.Content = (RectTransform)child.transform;
                    EditorUtility.SetDirty(fitter);
                }
                return;
            }

            if (content.parent != fitter.transform)
                EditorGUILayout.HelpBox("Content must be a direct child of this slot.", MessageType.Error);
            if (content.GetComponent<AspectRatioFitter>() || content.GetComponent<ContentSizeFitter>())
                EditorGUILayout.HelpBox("Remove the other fitter from Content to avoid competing size changes.", MessageType.Error);
            var item = content.GetComponent<LayoutItem>();
            if (item && (item.Sizing.x != SizingMode.Fixed || item.Sizing.y != SizingMode.Fixed))
                EditorGUILayout.HelpBox("Set Content uLayout sizing to Fixed on both axes.", MessageType.Warning);
            if (content.localScale != Vector3.one || content.localRotation != Quaternion.identity)
                EditorGUILayout.HelpBox("Use unit local scale and no local rotation on Content for correct containment.", MessageType.Warning);
        }
    }
}
