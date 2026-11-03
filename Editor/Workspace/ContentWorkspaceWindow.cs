using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Foundation.Content.Editor.Workspace
{
    public sealed class ContentWorkspaceWindow : EditorWindow
    {
        [SerializeField] private string selectedAssetPath;
        [SerializeField] private string selectedGroupPath;
        [SerializeField] private int selectedRecordIndex;
        [SerializeField] private string searchText;
        [SerializeField] private List<ContentWorkspaceFileDraft> fileDrafts = new();

        private readonly List<ContentWorkspaceSource> _sources = new();
        private IContentWorkspacePresentation _presentation;
        private SerializedObject _serialized;
        private ContentWorkspaceSource _source;
        private IReadOnlyList<ContentWorkspaceGroup> _groups = Array.Empty<ContentWorkspaceGroup>();
        private ContentWorkspaceGroup _group;
        private IReadOnlyList<ContentWorkspaceFile> _files = Array.Empty<ContentWorkspaceFile>();
        private ContentWorkspaceFileDraft _fileDraft;
        private bool _preserveBuildReport;
        private IReadOnlyList<ContentWorkspaceRow> _rows = Array.Empty<ContentWorkspaceRow>();
        private ListView _sourceList;
        private ListView _groupList;
        private VisualElement _tableHost;
        private ScrollView _detail;
        private ScrollView _report;
        private Label _status;
        private Label _groupTitle;
        private Label _sourcePath;
        private Button _add;
        private Button _duplicate;
        private Button _delete;
        private Button _save;
        private bool _refreshPending;
        private bool _hasReport;
        private bool _refreshDetailsPending;
        private bool _refreshSourcesPending;
        private bool _invalidateReportPending;

        public static Func<IContentWorkspacePresentation> PresentationFactory { get; set; }
            = () => new DefaultContentWorkspacePresentation();

        private void OnEnable()
        {
            minSize = new Vector2(980, 560);
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            EditorApplication.projectChanged -= OnProjectChanged;
            _serialized?.Dispose();
            _serialized = null;
        }

        private void OnDestroy()
        {
            foreach (var draft in fileDrafts.Where(draft => draft != null))
            {
                Undo.ClearUndo(draft);
                DestroyImmediate(draft);
            }
            fileDrafts.Clear();
        }

        [MenuItem("Content/Content Workspace", priority = 0)]
        public static void Open()
        {
            GetWindow<ContentWorkspaceWindow>("Content Workspace");
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            _presentation = PresentationFactory();
            _presentation.Attach(root);
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;

            var heading = new Label("CONTENT WORKSPACE");
            heading.style.fontSize = 19;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(heading);
            var subtitle = new Label("Project content • discover, edit, validate and build");
            subtitle.style.opacity = 0.7f;
            subtitle.style.marginBottom = 10;
            root.Add(subtitle);

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(RefreshSources) { text = "Refresh sources" });
            _save = new ToolbarButton(SaveSelected) { text = "Save source" };
            toolbar.Add(_save);
            toolbar.Add(new ToolbarButton(OpenSourceFile) { text = "Open source file" });
            toolbar.Add(new ToolbarButton(ReloadSourceFile) { text = "Reload file" });
            toolbar.Add(new ToolbarSpacer { flex = true });
            toolbar.Add(new ToolbarButton(() => RunPipeline(false)) { text = "Validate all" });
            toolbar.Add(new ToolbarButton(() => RunPipeline(true)) { text = "Build all" });
            root.Add(toolbar);

            var outer = new TwoPaneSplitView(0, 230, TwoPaneSplitViewOrientation.Horizontal);
            outer.style.flexGrow = 1;
            outer.style.minHeight = 220;
            outer.style.marginTop = 8;
            var navigation = new VisualElement();
            navigation.style.minWidth = 160;
            navigation.style.paddingRight = 8;
            navigation.Add(Title("SOURCES"));
            _sourceList = new ListView
            {
                itemsSource = _sources,
                fixedItemHeight = 34,
                selectionType = SelectionType.Single,
                makeItem = () => new Label(),
                bindItem = (element, index) =>
                {
                    var label = (Label)element;
                    label.text = _sources[index].Name;
                    label.tooltip = _sources[index].Path;
                },
            };
            _sourceList.style.height = 150;
            _sourceList.selectionChanged += values => SelectSource(values.OfType<ContentWorkspaceSource>().FirstOrDefault());
            navigation.Add(_sourceList);
            navigation.Add(Title("GROUPS"));
            _groupList = new ListView
            {
                fixedItemHeight = 28,
                selectionType = SelectionType.Single,
                makeItem = () => new Label(),
                bindItem = (element, index) => ((Label)element).text = _groups[index].Name,
            };
            _groupList.style.flexGrow = 1;
            _groupList.selectionChanged += values => SelectGroup(values.OfType<ContentWorkspaceGroup>().FirstOrDefault());
            navigation.Add(_groupList);
            outer.Add(navigation);

            var inner = new TwoPaneSplitView(1, 340, TwoPaneSplitViewOrientation.Horizontal);
            var records = new VisualElement();
            records.style.minWidth = 300;
            records.style.paddingRight = 8;
            _groupTitle = Title("RECORDS");
            records.Add(_groupTitle);
            var search = new ToolbarSearchField { value = searchText ?? string.Empty };
            search.tooltip = "Search all record fields; separate terms with spaces.";
            search.RegisterValueChangedCallback(evt =>
            {
                searchText = evt.newValue;
                RefreshRows();
                ShowSelectedRecord();
            });
            records.Add(search);
            var actions = new Toolbar();
            _add = new ToolbarButton(() => Mutate(() => _fileDraft != null ? _fileDraft.AddRow() : ContentWorkspaceModel.Add(_serialized, _group), "Add content record")) { text = "Add" };
            _duplicate = new ToolbarButton(() => Mutate(() => _fileDraft != null ? _fileDraft.AddRow(selectedRecordIndex) : ContentWorkspaceModel.Duplicate(_serialized, _group, selectedRecordIndex), "Duplicate content record")) { text = "Duplicate" };
            _delete = new ToolbarButton(DeleteSelected) { text = "Delete" };
            actions.Add(_add);
            actions.Add(_duplicate);
            actions.Add(_delete);
            records.Add(actions);
            _tableHost = new();
            _tableHost.style.flexGrow = 1;
            _tableHost.style.minHeight = 100;
            records.Add(_tableHost);
            inner.Add(records);

            var detailPane = new VisualElement();
            detailPane.style.minWidth = 260;
            detailPane.Add(Title("RECORD DETAILS"));
            _sourcePath = new();
            _sourcePath.style.whiteSpace = WhiteSpace.Normal;
            _sourcePath.style.opacity = 0.65f;
            _sourcePath.style.marginBottom = 8;
            detailPane.Add(_sourcePath);
            _detail = new();
            _detail.style.flexGrow = 1;
            detailPane.Add(_detail);
            inner.Add(detailPane);
            outer.Add(inner);
            root.Add(outer);

            _status = new("Discovering project content…");
            _status.style.marginTop = 8;
            root.Add(_status);
            _report = new();
            _report.style.maxHeight = 140;
            root.Add(_report);
            RefreshSources();
            UpdateUnsavedState();
        }

        public override void SaveChanges()
        {
            foreach (var draft in fileDrafts.Where(draft => draft != null && draft.IsDirty))
                draft.Save();
            base.SaveChanges();
            AssetDatabase.Refresh();
        }

        public override void DiscardChanges()
        {
            foreach (var draft in fileDrafts.Where(draft => draft != null && draft.IsDirty).ToArray())
            {
                fileDrafts.Remove(draft);
                Undo.ClearUndo(draft);
                DestroyImmediate(draft);
            }
            _fileDraft = null;
            base.DiscardChanges();
        }

        private void RefreshSources()
        {
            try
            {
                var builder = ConfigBuildEntryPoint.BuilderFactory();
                var discovered = builder.DiscoverSources();
                _sources.Clear();
                _sources.AddRange(discovered.Select(source => new ContentWorkspaceSource(source)).OrderBy(source => source.Name));
                _sourceList.Rebuild();
                var index = _sources.FindIndex(source => source.Key == selectedAssetPath);
                if (index < 0 && _sources.Count > 0)
                    index = 0;
                _sourceList.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : Array.Empty<int>());
                SelectSource(index >= 0 ? _sources[index] : null);
                if (!_hasReport)
                    _status.text = $"{_sources.Count} sources discovered. Edits use Unity Undo; save the selected asset when ready.";
            }
            catch (Exception exception)
            {
                ShowFailure(exception);
            }
        }

        private void SelectSource(ContentWorkspaceSource source)
        {
            _detail.Unbind();
            _serialized?.Dispose();
            _serialized = null;
            _source = source;
            _files = Array.Empty<ContentWorkspaceFile>();
            _fileDraft = null;
            _sourcePath.text = source?.Path ?? string.Empty;
            selectedAssetPath = source?.Key;
            if (source?.Asset != null)
            {
                _serialized = new(source.Asset);
                _groups = ContentWorkspaceModel.DiscoverGroups(_serialized);
            }
            else if (source?.Source is IContentWorkspaceFileSource fileSource)
            {
                _files = fileSource.GetWorkspaceFiles();
                _groups = _files.Select(file => new ContentWorkspaceGroup(file.Path, Path.GetFileName(file.Path), file.Format == ContentWorkspaceFileFormat.Csv)).ToArray();
            }
            else
                _groups = Array.Empty<ContentWorkspaceGroup>();

            _groupList.itemsSource = _groups.ToList();
            _groupList.Rebuild();
            var index = _groups.ToList().FindIndex(group => group.Path == selectedGroupPath);
            if (index < 0 && _groups.Count > 0)
                index = 0;
            _groupList.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : Array.Empty<int>());
            SelectGroup(index >= 0 ? _groups[index] : null);
            _save.SetEnabled(_serialized != null || (_fileDraft != null && !_fileDraft.HasParseErrors));
        }

        private void SelectGroup(ContentWorkspaceGroup group)
        {
            _group = group;
            _fileDraft = null;
            var file = _files.FirstOrDefault(item => item.Path == group?.Path);
            if (file != null)
            {
                var fullPath = Path.GetFullPath(file.Path);
                _fileDraft = fileDrafts.FirstOrDefault(draft => draft != null && draft.SourcePath == fullPath);
                if (_fileDraft == null)
                {
                    _fileDraft = ContentWorkspaceFileDraft.Open(file);
                    fileDrafts.Add(_fileDraft);
                }
                else if (!_fileDraft.IsDirty && _fileDraft.HasExternalChanges)
                    _fileDraft.Reload();
                _sourcePath.text = file.Path;
            }
            _save.SetEnabled(_serialized != null || (_fileDraft != null && !_fileDraft.HasParseErrors));
            selectedGroupPath = group?.Path ?? string.Empty;
            RefreshRows();
            ShowSelectedRecord();
        }

        private void RefreshRows()
        {
            _tableHost.Clear();
            IReadOnlyList<ContentWorkspaceRow> records = Array.Empty<ContentWorkspaceRow>();
            if (_serialized != null && _group != null)
                records = ContentWorkspaceModel.ReadRows(_serialized, _group);
            else if (_fileDraft != null)
                records = _fileDraft.ReadRows();
            _rows = ContentWorkspaceModel.Filter(records, searchText);
            _groupTitle.text = $"{_group?.Name ?? "RECORDS"} • {_rows.Count}";
            if (_rows.Count > 0)
            {
                if (!_rows.Any(row => row.Index == selectedRecordIndex))
                    selectedRecordIndex = _rows[0].Index;
                var selectedRow = _rows.ToList().FindIndex(row => row.Index == selectedRecordIndex);
                var table = _presentation.CreateTable(_rows, index =>
                {
                    if (index < 0 || index >= _rows.Count)
                        return;
                    selectedRecordIndex = _rows[index].Index;
                    ShowSelectedRecord();
                }, selectedRow);
                table.style.flexGrow = 1;
                _tableHost.Add(table);
            }
            else
                _tableHost.Add(new HelpBox(_serialized == null
                    ? "No matching source records. Clear the search or select a source file."
                    : "No matching records. Clear the search or add a record.", HelpBoxMessageType.Info));

            UpdateActions();
        }

        private void ShowSelectedRecord()
        {
            _detail.Unbind();
            _detail.Clear();
            if (_fileDraft != null)
            {
                ShowFileRecord();
                return;
            }
            if (_serialized == null)
            {
                ShowExternalSource();
                return;
            }
            if (_group == null)
                return;
            if (_rows.Count == 0 && !string.IsNullOrWhiteSpace(searchText))
            {
                _detail.Add(new HelpBox("No matching records. Clear the search to edit this group.", HelpBoxMessageType.Info));
                return;
            }

            if (!_group.IsCollection)
            {
                var property = _serialized.GetIterator();
                for (var enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
                {
                    if (property.name != "m_Script" && (!property.isArray || property.propertyType == SerializedPropertyType.String))
                        _detail.Add(_presentation.CreateField(property.Copy()));
                }
            }
            else
            {
                var collection = _serialized.FindProperty(_group.Path);
                if (collection == null || collection.arraySize == 0)
                {
                    _detail.Add(new HelpBox("This group is empty. Use Add to create a record.", HelpBoxMessageType.Info));
                    return;
                }
                selectedRecordIndex = Mathf.Clamp(selectedRecordIndex, 0, collection.arraySize - 1);
                var element = collection.GetArrayElementAtIndex(selectedRecordIndex);
                element.isExpanded = true;
                _detail.Add(_presentation.CreateField(element));
            }
            _detail.Bind(_serialized);
            _detail.TrackSerializedObjectValue(_serialized, _ => ScheduleRefresh(false));
            UpdateActions();
        }

        private void ShowFileRecord()
        {
            var draft = _fileDraft;
            if (draft.HasParseErrors)
            {
                _detail.Add(new HelpBox(
                    "This CSV has parse errors and cannot be safely edited or saved. Fix the source file externally, then choose Reload source file.\n"
                    + string.Join("\n", draft.ParseErrors),
                    HelpBoxMessageType.Error));
                return;
            }

            var row = _rows.FirstOrDefault(item => item.Index == selectedRecordIndex);
            if (row == null)
                return;
            _detail.Add(new HelpBox("Editing the original source file. Save source writes this file; generated binaries are untouched.", HelpBoxMessageType.Info));
            for (var column = 0; column < draft.Columns.Count; column++)
            {
                var index = column;
                var field = new TextField(draft.Columns[index])
                {
                    value = draft.Rows[row.Index].Cells[index] ?? string.Empty,
                    multiline = index > 1 || !draft.CanEditRows,
                };
                field.style.whiteSpace = WhiteSpace.Normal;
                field.style.marginBottom = 8;
                if (field.multiline)
                    field.style.minHeight = draft.CanEditRows ? 60 : 420;
                field.RegisterValueChangedCallback(evt =>
                {
                    draft.SetCell(row.Index, index, evt.newValue);
                    UpdateUnsavedState();
                    ScheduleRefresh(false);
                });
                _detail.Add(field);
            }
        }

        private void OpenSourceFile()
        {
            if (_fileDraft != null)
                EditorUtility.OpenWithDefaultApp(_fileDraft.SourcePath);
            else if (_source?.Asset != null)
                EditorGUIUtility.PingObject(_source.Asset);
        }

        private void ReloadSourceFile()
        {
            if (_fileDraft == null)
                return;
            if (_fileDraft.IsDirty && !EditorUtility.DisplayDialog("Reload source file", "Discard the workspace edits and reload this source file?", "Reload", "Cancel"))
                return;
            try
            {
                _fileDraft.Reload();
                UpdateUnsavedState();
                RefreshRows();
                ShowSelectedRecord();
                InvalidateReport();
            }
            catch (Exception exception)
            {
                ShowFailure(exception);
            }
        }

        private void UpdateUnsavedState()
        {
            hasUnsavedChanges = fileDrafts.Any(draft => draft != null && draft.IsDirty);
            saveChangesMessage = "Save edited content source files before closing the workspace?";
        }

        private void ShowExternalSource()
        {
            if (_source == null)
            {
                _detail.Add(new HelpBox("No content sources found. Implement IConfigAuthoringSource on an authoring asset or register a source provider with ConfigBuilder.", HelpBoxMessageType.Info));
                return;
            }
            _detail.Add(new HelpBox("This provider has no authoring view. Implement IContentWorkspaceFileSource to expose canonical CSV or text files.", HelpBoxMessageType.Info));
            _detail.Add(new Label(_source.Source.GetType().FullName));
            if (_group != null)
                _detail.Add(new Label($"Source group: {_group.Name}"));
            var record = _rows.FirstOrDefault(row => row.Index == selectedRecordIndex);
            if (record != null)
                _detail.Add(new Label($"Record: {record.Id}"));
        }

        private void Mutate(Func<int> edit, string undoName)
        {
            if ((_serialized == null && _fileDraft == null) || _group?.IsCollection != true)
                return;
            try
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                selectedRecordIndex = edit();
                searchText = string.Empty;
                rootVisualElement.Q<ToolbarSearchField>().SetValueWithoutNotify(string.Empty);
                InvalidateReport();
                UpdateUnsavedState();
                RefreshRows();
                ShowSelectedRecord();
            }
            catch (Exception exception)
            {
                _serialized?.Update();
                ShowFailure(exception);
            }
        }

        private void DeleteSelected()
        {
            var record = _rows.FirstOrDefault(row => row.Index == selectedRecordIndex);
            if (record == null || !EditorUtility.DisplayDialog("Delete content record", $"Delete '{record.Id}' from {_group.Name}? This can be undone.", "Delete", "Cancel"))
                return;
            Mutate(() =>
            {
                if (_fileDraft != null)
                    _fileDraft.DeleteRow(selectedRecordIndex);
                else
                    ContentWorkspaceModel.Delete(_serialized, _group, selectedRecordIndex);
                return Mathf.Max(0, selectedRecordIndex - 1);
            }, "Delete content record");
        }

        private void SaveSelected()
        {
            try
            {
                if (_fileDraft != null)
                {
                    _fileDraft.Save();
                    UpdateUnsavedState();
                    _status.text = $"Saved {_fileDraft.SourcePath}.";
                    AssetDatabase.Refresh();
                    return;
                }
                if (_source?.Asset == null)
                    return;
                AssetDatabase.SaveAssetIfDirty(_source.Asset);
                _status.text = $"Saved {_source.Name}.";
            }
            catch (Exception exception)
            {
                ShowFailure(exception);
            }
        }

        private void RunPipeline(bool build)
        {
            try
            {
                if (fileDrafts.Any(draft => draft != null && draft.IsDirty))
                {
                    _status.text = "Save edited source files before validation or build; the pipeline reads the canonical files.";
                    return;
                }
                _serialized?.ApplyModifiedProperties();
                var builder = ConfigBuildEntryPoint.BuilderFactory();
                var sources = builder.DiscoverSources();
                var result = build ? builder.Build(sources) : builder.Validate(sources);
                _report.Clear();
                _hasReport = true;
                _status.text = result.Success
                    ? build ? $"Build succeeded • {result.WrittenFiles.Count} files written. Unsaved source assets remain unsaved." : $"Validation passed • {sources.Count} sources • no files written."
                    : $"{(build ? "Build" : "Validation")} failed • {result.Errors.Count} errors.";
                foreach (var error in result.Errors)
                {
                    var captured = error;
                    _report.Add(new Button(() => NavigateToError(captured)) { text = error.ToString(), tooltip = error + "\nClick to locate the source and matching record, when available." });
                }
                foreach (var path in result.WrittenFiles)
                    _report.Add(new Label(path));
                if (build && result.Success)
                {
                    _preserveBuildReport = true;
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception exception)
            {
                ShowFailure(exception);
            }
        }

        private void NavigateToError(ConfigBuildError error)
        {
            var source = _sources.FirstOrDefault(item => item.Name == error.Source || item.Path == error.Source
                || (item.Source is IContentWorkspaceFileSource files && files.GetWorkspaceFiles().Any(file => file.Path == error.Source || Path.GetFileName(file.Path) == error.Source)));
            if (source == null)
                return;
            _sourceList.SetSelectionWithoutNotify(new[] { _sources.IndexOf(source) });
            SelectSource(source);
            if (_serialized == null)
            {
                var fileGroup = _groups.FirstOrDefault(group => group.Path == error.Source) ?? _groups.FirstOrDefault();
                if (fileGroup != null)
                {
                    _groupList.SetSelectionWithoutNotify(new[] { _groups.ToList().IndexOf(fileGroup) });
                    SelectGroup(fileGroup);
                    var row = _rows.FirstOrDefault(item => item.Id == error.EntryId);
                    if (row != null)
                    {
                        selectedRecordIndex = row.Index;
                        ShowSelectedRecord();
                    }
                }
                return;
            }
            if (string.IsNullOrEmpty(error.EntryId))
                return;
            foreach (var group in _groups)
            {
                var row = ContentWorkspaceModel.ReadRows(_serialized, group).FirstOrDefault(item => item.Id == error.EntryId);
                if (row == null)
                    continue;
                selectedRecordIndex = row.Index;
                searchText = string.Empty;
                rootVisualElement.Q<ToolbarSearchField>().SetValueWithoutNotify(string.Empty);
                _groupList.SetSelectionWithoutNotify(new[] { _groups.ToList().IndexOf(group) });
                SelectGroup(group);
                break;
            }
        }

        private void UpdateActions()
        {
            var editable = (_serialized != null || _fileDraft?.CanEditRows == true) && _group?.IsCollection == true;
            _add.SetEnabled(editable);
            var selected = editable && _rows.Any(row => row.Index == selectedRecordIndex);
            _duplicate.SetEnabled(selected);
            _delete.SetEnabled(selected);
        }

        private void OnUndo()
        {
            UpdateUnsavedState();
            ScheduleRefresh(true);
        }

        private void OnProjectChanged()
        {
            var preserveReport = _preserveBuildReport;
            _preserveBuildReport = false;
            ScheduleRefresh(true, true, !preserveReport);
        }

        private void ScheduleRefresh(bool details, bool sources = false, bool invalidateReport = true)
        {
            _invalidateReportPending |= invalidateReport;
            _refreshDetailsPending |= details;
            _refreshSourcesPending |= sources;
            if (_refreshPending || _tableHost == null)
                return;
            _refreshPending = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _refreshPending = false;
                var refreshDetails = _refreshDetailsPending;
                var refreshSources = _refreshSourcesPending;
                _refreshDetailsPending = false;
                _refreshSourcesPending = false;
                if (_invalidateReportPending)
                    InvalidateReport();
                _invalidateReportPending = false;
                if (refreshSources || (_source?.Asset == null && _serialized != null))
                    RefreshSources();
                else
                {
                    RefreshRows();
                    if (refreshDetails)
                        ShowSelectedRecord();
                }
            }).ExecuteLater(150);
        }

        private void InvalidateReport()
        {
            if (!_hasReport)
                return;
            _hasReport = false;
            _report.Clear();
            _status.text = "Content changed. Validate again to check the current data.";
        }

        private void ShowFailure(Exception exception)
        {
            _status.text = "Content Workspace could not complete the operation.";
            _report.Clear();
            _report.Add(new HelpBox(exception.Message, HelpBoxMessageType.Error));
            Debug.LogException(exception);
        }

        private static Label Title(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 6;
            label.style.marginBottom = 6;
            return label;
        }
    }
}
