using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Foundation.Content.Editor.Workspace
{
    public interface IContentWorkspaceFileSource
    {
        string WorkspacePath { get; }
        IReadOnlyList<ContentWorkspaceFile> GetWorkspaceFiles();
    }

    public enum ContentWorkspaceFileFormat
    {
        Csv,
        Text,
    }

    public sealed class ContentWorkspaceFile
    {
        public string Path { get; }
        public ContentWorkspaceFileFormat Format { get; }

        public ContentWorkspaceFile(string path, ContentWorkspaceFileFormat format)
        {
            Path = path;
            Format = format;
        }
    }

    [Serializable]
    public sealed class ContentWorkspaceFileRow
    {
        public string[] Cells;

        public ContentWorkspaceFileRow(string[] cells)
        {
            Cells = cells;
        }
    }

    public sealed class ContentWorkspaceFileDraft : ScriptableObject
    {
        [SerializeField] private string sourcePath;
        [SerializeField] private ContentWorkspaceFileFormat format;
        [SerializeField] private ContentWorkspaceFileCheckpoint checkpoint;
        [SerializeField] private bool utf8Bom;
        [SerializeField] private string newline;
        [SerializeField] private string[] columns;
        [SerializeField] private List<ContentWorkspaceFileRow> rows = new();
        [SerializeField] private string[] parseErrors = Array.Empty<string>();

        public string SourcePath => sourcePath;

        public IReadOnlyList<string> Columns => columns;

        public IReadOnlyList<ContentWorkspaceFileRow> Rows => rows;

        public IReadOnlyList<string> ParseErrors => parseErrors ?? Array.Empty<string>();

        public bool HasParseErrors => parseErrors != null && parseErrors.Length > 0;

        public bool CanEditRows => format == ContentWorkspaceFileFormat.Csv && !HasParseErrors;

        public bool IsDirty => checkpoint != null && checkpoint.Baseline != Snapshot();

        public bool HasExternalChanges => !File.Exists(sourcePath)
            || Convert.ToBase64String(File.ReadAllBytes(sourcePath)) != checkpoint.OriginalBytes;

        private void OnDestroy()
        {
            if (checkpoint != null)
                DestroyImmediate(checkpoint);
        }

        public static ContentWorkspaceFileDraft Open(ContentWorkspaceFile file)
        {
            var draft = CreateInstance<ContentWorkspaceFileDraft>();
            draft.hideFlags = HideFlags.HideAndDontSave;
            draft.sourcePath = Path.GetFullPath(file.Path);
            draft.format = file.Format;
            draft.checkpoint = CreateInstance<ContentWorkspaceFileCheckpoint>();
            draft.checkpoint.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                draft.Reload();
                return draft;
            }
            catch
            {
                DestroyImmediate(draft);
                throw;
            }
        }

        public void Reload()
        {
            var bytes = File.ReadAllBytes(sourcePath);
            var text = File.ReadAllText(sourcePath);
            var newRows = new List<ContentWorkspaceFileRow>();
            string[] newColumns;
            var newParseErrors = Array.Empty<string>();
            if (format == ContentWorkspaceFileFormat.Csv)
            {
                using var reader = new StringReader(text);
                using var csv = CsvRecordReader.Create(reader);
                newColumns = Enumerable.Range(0, csv.FieldCount).Select(csv.GetName).ToArray();
                while (csv.Read())
                    newRows.Add(new ContentWorkspaceFileRow(Enumerable.Range(0, csv.FieldCount).Select(csv.GetString).ToArray()));
                newParseErrors = csv.Errors.ToArray();
            }
            else
            {
                newColumns = new[] { "Script" };
                newRows.Add(new ContentWorkspaceFileRow(new[] { text }));
            }
            columns = newColumns;
            rows = newRows;
            parseErrors = newParseErrors;
            utf8Bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
            newline = text.Contains("\r\n") ? "\r\n" : "\n";
            checkpoint.OriginalBytes = Convert.ToBase64String(bytes);
            checkpoint.Baseline = Snapshot();
            Undo.ClearUndo(this);
        }

        public void SetCell(int row, int column, string value)
        {
            ThrowIfCsvHasErrors();
            Undo.RecordObject(this, "Edit content source");
            rows[row].Cells[column] = value ?? string.Empty;
            EditorUtility.SetDirty(this);
        }

        public int AddRow(int copyIndex = -1)
        {
            ThrowIfCsvHasErrors();
            if (!CanEditRows)
                throw new InvalidOperationException("A text document has one editable body.");
            Undo.RecordObject(this, copyIndex < 0 ? "Add CSV row" : "Duplicate CSV row");
            var cells = copyIndex < 0 ? new string[columns.Length] : (string[])rows[copyIndex].Cells.Clone();
            rows.Add(new ContentWorkspaceFileRow(cells));
            EditorUtility.SetDirty(this);
            return rows.Count - 1;
        }

        public void DeleteRow(int index)
        {
            ThrowIfCsvHasErrors();
            if (!CanEditRows)
                throw new InvalidOperationException("A text document has one editable body.");
            Undo.RecordObject(this, "Delete CSV row");
            rows.RemoveAt(index);
            EditorUtility.SetDirty(this);
        }

        public IReadOnlyList<ContentWorkspaceRow> ReadRows()
        {
            var result = new List<ContentWorkspaceRow>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var values = new Dictionary<string, string>();
                for (var column = 0; column < columns.Length; column++)
                {
                    var label = string.IsNullOrWhiteSpace(columns[column]) ? $"Column {column + 1}" : columns[column];
                    if (values.ContainsKey(label))
                        label += $" [{column + 1}]";
                    values[label] = rows[i].Cells[column] ?? string.Empty;
                }
                var keyColumn = Array.FindIndex(columns, column => string.Equals(column?.Trim(), "Key", StringComparison.OrdinalIgnoreCase));
                var id = keyColumn >= 0 ? rows[i].Cells[keyColumn] : Path.GetFileName(sourcePath);
                result.Add(new ContentWorkspaceRow(i, sourcePath, id ?? string.Empty, values));
            }
            return result;
        }

        public void Save()
        {
            ThrowIfCsvHasErrors();
            if (!IsDirty)
                return;
            if (HasExternalChanges)
                throw new IOException($"Source file changed outside the workspace: {sourcePath}. Reload it before saving; the file has not been overwritten.");

            var text = format == ContentWorkspaceFileFormat.Text ? rows[0].Cells[0] : WriteCsv();
            var temporary = sourcePath + ".workspace-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(utf8Bom));
                if (HasExternalChanges)
                    throw new IOException($"Source file changed while saving: {sourcePath}.");
                File.Replace(temporary, sourcePath, null);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            checkpoint.OriginalBytes = Convert.ToBase64String(File.ReadAllBytes(sourcePath));
            checkpoint.Baseline = Snapshot();
        }

        private string WriteCsv()
        {
            var result = new StringBuilder();
            result.Append(string.Join(",", columns.Select(Quote))).Append(newline);
            foreach (var row in rows)
                result.Append(string.Join(",", row.Cells.Select(Quote))).Append(newline);
            return result.ToString();
        }

        private string Snapshot() => JsonUtility.ToJson(new FileData { Columns = columns, Rows = rows });

        private void ThrowIfCsvHasErrors()
        {
            if (HasParseErrors)
                throw new InvalidDataException(
                    "The CSV cannot be edited or saved until its parse errors are corrected: " + string.Join(" ", ParseErrors));
        }

        private static string Quote(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        [Serializable]
        private sealed class FileData
        {
            public string[] Columns;
            public List<ContentWorkspaceFileRow> Rows;
        }
    }

    internal sealed class ContentWorkspaceFileCheckpoint : ScriptableObject
    {
        public string OriginalBytes;
        public string Baseline;
    }

}
