using Foundation.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEditorBase = UnityEditor.Editor;

namespace Foundation.UI.UGui.Editor
{
    [CustomEditor(typeof(FoundationButton), true)]
    [CanEditMultipleObjects]
    internal sealed class FoundationButtonEditor : UnityEditorBase
    {
        private bool _showStandardButtonSettings;

        public override VisualElement CreateInspectorGUI()
            => FoundationButtonInspectorPresentation.Factory?.Invoke(serializedObject, targets)
                ?? new IMGUIContainer(OnInspectorGUI);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Foundation Button", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("initialState"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("states"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("localizedLabel"));

            serializedObject.FindProperty("m_Transition").enumValueIndex =
                (int)Selectable.Transition.None;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Click actions, sounds and effects are configured per state. Runtime code can subscribe with Subscribe(stateId, ...) or SubscribeAsync(stateId, ...).",
                MessageType.Info);

            _showStandardButtonSettings = EditorGUILayout.Foldout(
                _showStandardButtonSettings,
                "Standard Button Settings",
                true);
            if (_showStandardButtonSettings)
                DrawStandardButtonSettings();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStandardButtonSettings()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Interactable"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_TargetGraphic"));

            var transition = serializedObject.FindProperty("m_Transition");
            EditorGUILayout.PropertyField(transition);
            switch ((Selectable.Transition)transition.enumValueIndex)
            {
                case Selectable.Transition.ColorTint:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Colors"));
                    break;
                case Selectable.Transition.SpriteSwap:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("m_SpriteState"));
                    break;
                case Selectable.Transition.Animation:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("m_AnimationTriggers"));
                    break;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Navigation"));
        }
    }
}
