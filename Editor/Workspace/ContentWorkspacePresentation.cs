using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Foundation.Content.Editor.Workspace
{
    public interface IContentWorkspacePresentation
    {
        void Attach(VisualElement root);
        VisualElement CreateField(SerializedProperty property);
        VisualElement CreateTable(IReadOnlyList<ContentWorkspaceRow> rows, Action<int> select, int selectedIndex);
    }

    public sealed class DefaultContentWorkspacePresentation : IContentWorkspacePresentation
    {
        public void Attach(VisualElement root) { }

        public VisualElement CreateField(SerializedProperty property) => new PropertyField(property);

        public VisualElement CreateTable(IReadOnlyList<ContentWorkspaceRow> rows, Action<int> select, int selectedIndex)
        {
            var table = new MultiColumnListView
            {
                itemsSource = new List<ContentWorkspaceRow>(rows),
                fixedItemHeight = 28,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
            };
            foreach (var name in ContentWorkspaceModel.Columns(rows))
            {
                var key = name;
                table.columns.Add(new Column
                {
                    name = key,
                    title = ObjectNames.NicifyVariableName(key),
                    width = 150,
                    minWidth = 90,
                    makeCell = () => new Label(),
                    bindCell = (cell, index) => ((Label)cell).text = rows[index].Values.TryGetValue(key, out var value) ? value : string.Empty,
                });
            }
            table.SetSelectionWithoutNotify(selectedIndex >= 0 ? new[] { selectedIndex } : Array.Empty<int>());
            table.selectedIndicesChanged += indices =>
            {
                foreach (var index in indices)
                {
                    select(index);
                    break;
                }
            };
            return table;
        }
    }
}
