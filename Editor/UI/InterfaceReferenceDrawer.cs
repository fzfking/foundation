using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.UI.UGui.Editor
{
    [CustomPropertyDrawer(typeof(InterfaceReference<>), true)]
    internal sealed class InterfaceReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var target = property.FindPropertyRelative("target");
            var interfaceType = ResolveInterfaceType(fieldInfo.FieldType);
            var content = new GUIContent(label.text, interfaceType != null ? $"Implements {interfaceType.Name}" : label.tooltip);

            EditorGUI.BeginProperty(position, content, property);
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.ObjectField(position, content, target.objectReferenceValue, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                var coerced = Coerce(picked, interfaceType);
                if (picked != null && coerced == null)
                    Debug.LogWarning($"'{picked.name}' does not implement {interfaceType?.Name}.");
                else
                    target.objectReferenceValue = coerced;
            }

            EditorGUI.EndProperty();
        }

        // A field is InterfaceReference<T>, InterfaceReference<T>[] or List<InterfaceReference<T>>.
        private static Type ResolveInterfaceType(Type fieldType)
        {
            if (fieldType.IsArray)
                fieldType = fieldType.GetElementType();
            else if (typeof(IList).IsAssignableFrom(fieldType) && fieldType.IsGenericType)
                fieldType = fieldType.GetGenericArguments()[0];

            return fieldType != null && fieldType.IsGenericType ? fieldType.GetGenericArguments()[0] : null;
        }

        private static Object Coerce(Object picked, Type interfaceType)
        {
            if (picked == null || interfaceType == null)
                return null;
            if (interfaceType.IsInstanceOfType(picked))
                return picked;

            if (picked is GameObject gameObject)
                return gameObject.GetComponent(interfaceType) as Component;
            if (picked is Component component)
                return component.GetComponent(interfaceType) as Component;

            return null;
        }
    }
}
