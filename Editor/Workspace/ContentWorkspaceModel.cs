using System.Reflection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Foundation.Content.Authoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Content.Editor.Workspace
{
    public sealed class ContentWorkspaceSource
    {
        public IConfigAuthoringSource Source { get; }
        public Object Asset => Source as Object;
        public string Name => Asset != null ? Asset.name : Source.GetType().Name;
        public string Path => Asset != null ? AssetDatabase.GetAssetPath(Asset) : (Source as IContentWorkspaceFileSource)?.WorkspacePath ?? "External source • read only";
        public string Key => Asset != null ? Path : $"external:{Source.GetType().FullName}:{Path}";

        public ContentWorkspaceSource(IConfigAuthoringSource source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }
    }

    public sealed class ContentWorkspaceGroup
    {
        public string Path { get; }
        public string Name { get; }
        public bool IsCollection { get; }

        public ContentWorkspaceGroup(string path, string name, bool isCollection)
        {
            Path = path;
            Name = name;
            IsCollection = isCollection;
        }
    }

    public sealed class ContentWorkspaceRow
    {
        public int Index { get; }
        public string Path { get; }
        public string Id { get; }
        public IReadOnlyDictionary<string, string> Values { get; }
        public string SearchText { get; }

        public ContentWorkspaceRow(int index, string path, string id, Dictionary<string, string> values)
        {
            Index = index;
            Path = path;
            Id = id;
            Values = values;
            SearchText = string.Join(" ", values.Values.Prepend(id));
        }
    }

    public static class ContentWorkspaceModel
    {
        public static IReadOnlyList<ContentWorkspaceGroup> DiscoverGroups(SerializedObject source)
        {
            source.UpdateIfRequiredOrScript();
            var groups = new List<ContentWorkspaceGroup>();
            var hasSettings = false;
            var property = source.GetIterator();
            for (var enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
            {
                if (property.name == "m_Script")
                    continue;

                if (property.isArray && property.propertyType != SerializedPropertyType.String)
                    groups.Add(new ContentWorkspaceGroup(property.propertyPath, property.displayName, true));
                else
                    hasSettings = true;
            }

            if (hasSettings)
                groups.Add(new ContentWorkspaceGroup(string.Empty, "Settings", false));
            return groups;
        }

        public static IReadOnlyList<ContentWorkspaceRow> ReadRows(SerializedObject source, ContentWorkspaceGroup group)
        {
            source.UpdateIfRequiredOrScript();
            if (!group.IsCollection)
            {
                var values = new Dictionary<string, string>();
                var property = source.GetIterator();
                for (var enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
                {
                    if (property.name != "m_Script" && (!property.isArray || property.propertyType == SerializedPropertyType.String))
                        values[property.name] = Format(property);
                }
                return new[] { new ContentWorkspaceRow(0, string.Empty, "Settings", values) };
            }

            var collection = source.FindProperty(group.Path);
            var rows = new List<ContentWorkspaceRow>();
            if (collection == null)
                return rows;

            for (var i = 0; i < collection.arraySize; i++)
            {
                var element = collection.GetArrayElementAtIndex(i);
                var values = new Dictionary<string, string>();
                if (element.propertyType == SerializedPropertyType.Generic || element.propertyType == SerializedPropertyType.ManagedReference)
                {
                    var child = element.Copy();
                    var end = element.GetEndProperty();
                    for (var enterChildren = true; child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end); enterChildren = false)
                        values[child.name] = Format(child);
                }
                else
                    values["Value"] = Format(element);

                var id = values.FirstOrDefault(pair => IsId(pair.Key)).Value;
                rows.Add(new ContentWorkspaceRow(i, element.propertyPath, id ?? $"Record {i + 1}", values));
            }
            return rows;
        }

        public static IReadOnlyList<ContentWorkspaceRow> Filter(IReadOnlyList<ContentWorkspaceRow> rows, string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return rows;

            var terms = search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return rows.Where(row => terms.All(term => row.SearchText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
        }

        public static IReadOnlyList<string> Columns(IReadOnlyList<ContentWorkspaceRow> rows)
            => rows.SelectMany(row => row.Values.Keys).Distinct().ToArray();

        public static int Add(SerializedObject source, ContentWorkspaceGroup group)
        {
            source.Update();
            var collection = RequireCollection(source, group);
            var field = source.targetObject.GetType();
            FieldInfo info = null;
            for (var type = field; type != null && info == null; type = type.BaseType)
                info = type.GetField(group.Path, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var elementType = info?.FieldType.IsArray == true
                ? info.FieldType.GetElementType()
                : info?.FieldType.GetGenericArguments().FirstOrDefault();
            object value = null;
            if (elementType != null && !typeof(Object).IsAssignableFrom(elementType) && !elementType.IsAbstract)
                value = elementType == typeof(string) ? string.Empty : Activator.CreateInstance(elementType);

            var index = collection.arraySize;
            collection.arraySize++;
            var element = collection.GetArrayElementAtIndex(index);
            if (element.propertyType == SerializedPropertyType.ManagedReference)
                element.managedReferenceValue = value;
            else
                element.boxedValue = value;
            source.ApplyModifiedProperties();
            return index;
        }

        public static int Duplicate(SerializedObject source, ContentWorkspaceGroup group, int index)
        {
            source.Update();
            var collection = RequireCollection(source, group);
            if (index < 0 || index >= collection.arraySize)
                throw new ArgumentOutOfRangeException(nameof(index));

            var original = collection.GetArrayElementAtIndex(index);
            var value = original.boxedValue;
            if (value != null && value is not Object && value is not string && !value.GetType().IsPrimitive && !value.GetType().IsEnum)
                value = JsonUtility.FromJson(JsonUtility.ToJson(value), value.GetType());

            var result = collection.arraySize;
            collection.arraySize++;
            var copy = collection.GetArrayElementAtIndex(result);
            if (copy.propertyType == SerializedPropertyType.ManagedReference)
                copy.managedReferenceValue = value;
            else
                copy.boxedValue = value;
            source.ApplyModifiedProperties();
            return result;
        }

        public static void Delete(SerializedObject source, ContentWorkspaceGroup group, int index)
        {
            source.Update();
            var collection = RequireCollection(source, group);
            if (index < 0 || index >= collection.arraySize)
                throw new ArgumentOutOfRangeException(nameof(index));

            var previousSize = collection.arraySize;
            collection.DeleteArrayElementAtIndex(index);
            if (collection.arraySize == previousSize)
                collection.DeleteArrayElementAtIndex(index);
            source.ApplyModifiedProperties();
        }

        public static bool IsId(string name)
        {
            return string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "<Id>k__BackingField", StringComparison.OrdinalIgnoreCase);
        }

        private static SerializedProperty RequireCollection(SerializedObject source, ContentWorkspaceGroup group)
        {
            var property = source.FindProperty(group.Path);
            if (!group.IsCollection || property == null || !property.isArray)
                throw new InvalidOperationException("The selected group is not a serialized collection.");
            return property;
        }

        private static string Format(SerializedProperty property)
        {
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
                return $"{property.arraySize} items";

            return property.propertyType switch
            {
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Boolean => property.boolValue ? "True" : "False",
                SerializedPropertyType.Integer => property.longValue.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.Float => property.doubleValue.ToString("G6", CultureInfo.InvariantCulture),
                SerializedPropertyType.Enum => property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                    ? property.enumDisplayNames[property.enumValueIndex] : property.intValue.ToString(),
                SerializedPropertyType.ObjectReference => property.objectReferenceValue != null ? property.objectReferenceValue.name : "None",
                SerializedPropertyType.ManagedReference => string.IsNullOrEmpty(property.managedReferenceFullTypename) ? "None" : property.managedReferenceFullTypename.Split(' ').Last(),
                SerializedPropertyType.Generic => property.type,
                SerializedPropertyType.AnimationCurve => $"{property.animationCurveValue?.length ?? 0} keys",
                SerializedPropertyType.Gradient => "Gradient",
                SerializedPropertyType.FixedBufferSize => property.fixedBufferSize.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.ExposedReference => property.exposedReferenceValue != null ? property.exposedReferenceValue.name : "None",
                _ => property.boxedValue?.ToString() ?? string.Empty,
            };
        }
    }
}
