using UnityEditor;
using UnityEngine;

namespace Foundation.UI.UGui.Editor
{
    [CustomPropertyDrawer(typeof(FoundationButtonStateDefinition))]
    internal sealed class FoundationButtonStateDefinitionDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var id = property.FindPropertyRelative("id");
            var foldout = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var title = string.IsNullOrWhiteSpace(id.stringValue) ? label.text : $"State: {id.stringValue}";
            property.isExpanded = EditorGUI.Foldout(foldout, property.isExpanded, title, true);
            if (!property.isExpanded)
                return;

            var y = foldout.yMax + Spacing;
            DrawProperty(ref y, position, property.FindPropertyRelative("id"));
            DrawProperty(ref y, position, property.FindPropertyRelative("interactable"));
            DrawProperty(ref y, position, property.FindPropertyRelative("localizationKey"));
            DrawProperty(ref y, position, property.FindPropertyRelative("localizationArguments"));
            DrawProperty(ref y, position, property.FindPropertyRelative("strategies"));
            DrawProperty(ref y, position, property.FindPropertyRelative("entered"));
            DrawProperty(ref y, position, property.FindPropertyRelative("exited"));
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return height;

            height += Spacing + GetHeight(property.FindPropertyRelative("id"));
            height += Spacing + GetHeight(property.FindPropertyRelative("interactable"));
            height += Spacing + GetHeight(property.FindPropertyRelative("localizationKey"));
            height += Spacing + GetHeight(property.FindPropertyRelative("localizationArguments"));
            height += Spacing + GetHeight(property.FindPropertyRelative("strategies"));
            height += Spacing + GetHeight(property.FindPropertyRelative("entered"));
            height += Spacing + GetHeight(property.FindPropertyRelative("exited"));
            return height;
        }

        private static void DrawProperty(ref float y, Rect parent, SerializedProperty property)
        {
            var height = GetHeight(property);
            var rect = new Rect(parent.x, y, parent.width, height);
            EditorGUI.PropertyField(rect, property, true);
            y += height + Spacing;
        }

        private static float GetHeight(SerializedProperty property)
            => EditorGUI.GetPropertyHeight(property, true);
    }
}
