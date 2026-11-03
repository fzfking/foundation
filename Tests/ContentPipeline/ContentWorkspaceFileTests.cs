using System;
using System.IO;
using Foundation.Content.Editor.Localization;
using Foundation.Content.Editor.Workspace;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Foundation.Localization;

namespace Foundation.Content.Editor.PackageTests
{
    public sealed class ContentWorkspaceFileTests
    {
        private string _path;
        private ContentWorkspaceFileDraft _draft;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "workspace-file-" + Guid.NewGuid().ToString("N") + ".csv");
        }

        [TearDown]
        public void TearDown()
        {
            if (_draft != null)
            {
                Undo.ClearUndo(_draft);
                Object.DestroyImmediate(_draft);
            }
            if (File.Exists(_path))
                File.Delete(_path);
        }

        [Test]
        public void CsvPreview_ReadsAllTranslationsAndContextFromCanonicalFile()
        {
            File.WriteAllText(_path, "Key,Context,ru,en,ja\nhello,button,Привет,Hello,こんにちは\n");
            var source = new LocalizationCsvSource(LocalizationCsvReader.ReadFile(_path));
            _draft = ContentWorkspaceFileDraft.Open(source.GetWorkspaceFiles()[0]);
            var row = _draft.ReadRows()[0];
            Assert.That(source.WorkspacePath, Is.EqualTo(_path));
            Assert.That(row.Values["Context"], Is.EqualTo("button"));
            Assert.That(row.Values["ru"], Is.EqualTo("Привет"));
            Assert.That(row.Values["ja"], Is.EqualTo("こんにちは"));
            Assert.That(row.Path, Is.EqualTo(_path));
            Assert.That(_draft.IsDirty, Is.False);
        }

        [Test]
        public void CsvSave_RoundTripsQuotesCommasMultilineAndUntouchedLocales()
        {
            File.WriteAllText(_path, "Key,Context,ru,en\r\nhello,button,Привет,Hello\r\n");
            _draft = ContentWorkspaceFileDraft.Open(new ContentWorkspaceFile(_path, ContentWorkspaceFileFormat.Csv));
            var translation = "Line one, \"quoted\"\nLine two\r\nLine three";
            _draft.SetCell(0, 3, translation);
            Assert.That(File.ReadAllText(_path), Does.Contain(",Hello"));
            _draft.Save();
            var restored = LocalizationCsvReader.ReadFile(_path);
            Assert.That(restored.Rows[0].Values[new LocaleId("en")], Is.EqualTo(translation));
            Assert.That(restored.Rows[0].Values[new LocaleId("ru")], Is.EqualTo("Привет"));
            Assert.That(_draft.IsDirty, Is.False);
        }

        [Test]
        public void CsvWithExtraFields_CannotBeEditedOrSavedAndOriginalBytesArePreserved()
        {
            const string original = "Key,Context,en\nhello,button,Original,unexpected\n";
            File.WriteAllText(_path, original);
            _draft = ContentWorkspaceFileDraft.Open(new ContentWorkspaceFile(_path, ContentWorkspaceFileFormat.Csv));

            Assert.That(_draft.HasParseErrors, Is.True);
            Assert.That(_draft.CanEditRows, Is.False);
            Assert.That(_draft.ParseErrors, Has.Some.Contains("4 fields, but the header has 3"));
            Assert.Throws<InvalidDataException>(() => _draft.SetCell(0, 2, "Changed"));
            Assert.Throws<InvalidDataException>(() => _draft.Save());
            Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
        }

        [Test]
        public void CsvWithMalformedQuoting_CannotBeEditedOrSavedAndOriginalBytesArePreserved()
        {
            const string original = "Key,Context,en\nhello,button,\"unfinished\nnext,button,Second\n";
            File.WriteAllText(_path, original);
            _draft = ContentWorkspaceFileDraft.Open(new ContentWorkspaceFile(_path, ContentWorkspaceFileFormat.Csv));

            Assert.That(_draft.HasParseErrors, Is.True);
            Assert.That(_draft.CanEditRows, Is.False);
            Assert.That(_draft.ParseErrors, Has.Some.Contains("never closed"));
            Assert.Throws<InvalidDataException>(() => _draft.SetCell(0, 2, "Changed"));
            Assert.Throws<InvalidDataException>(() => _draft.Save());
            Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
        }

        [Test]
        public void CsvParseErrors_SurviveDraftSerialization()
        {
            File.WriteAllText(_path, "Key,en\nhello,Original,unexpected\n");
            _draft = ContentWorkspaceFileDraft.Open(new ContentWorkspaceFile(_path, ContentWorkspaceFileFormat.Csv));
            var restored = ScriptableObject.CreateInstance<ContentWorkspaceFileDraft>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(_draft), restored);

                Assert.That(restored.HasParseErrors, Is.True);
                Assert.That(restored.CanEditRows, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(restored);
            }
        }

        [Test]
        public void Save_RejectsExternalChangesWithoutOverwritingEitherVersion()
        {
            File.WriteAllText(_path, "Key,Context,en\nhello,button,Original\n");
            _draft = ContentWorkspaceFileDraft.Open(new ContentWorkspaceFile(_path, ContentWorkspaceFileFormat.Csv));
            _draft.SetCell(0, 2, "Draft");
            var external = "Key,Context,en\nhello,button,External\n";
            File.WriteAllText(_path, external);
            Assert.Throws<IOException>(() => _draft.Save());
            Assert.That(File.ReadAllText(_path), Is.EqualTo(external));
            Assert.That(_draft.Rows[0].Cells[2], Is.EqualTo("Draft"));
            Assert.That(_draft.IsDirty, Is.True);
        }

        [Test]
        public void UndoAfterSave_BecomesDirtyAndCanSaveRevertedContent()
        {
            File.WriteAllText(_path, "Key,Context,en\nhello,button,Original\n");
            _draft = ContentWorkspaceFileDraft.Open(new ContentWorkspaceFile(_path, ContentWorkspaceFileFormat.Csv));
            Undo.IncrementCurrentGroup();
            _draft.SetCell(0, 2, "Changed");
            Undo.FlushUndoRecordObjects();
            _draft.Save();
            Undo.PerformUndo();
            Assert.That(_draft.Rows[0].Cells[2], Is.EqualTo("Original"));
            Assert.That(_draft.IsDirty, Is.True);
            _draft.Save();
            Assert.That(LocalizationCsvReader.ReadFile(_path).Rows[0].Values[new LocaleId("en")], Is.EqualTo("Original"));
        }

        [Test]
        public void TextSource_ShowsAndSavesFullScriptWithoutRecompilingIt()
        {
            var script = "chapter intro\nstep first\n  show \"Привет\"\n";
            File.WriteAllText(_path, script);
            _draft = ContentWorkspaceFileDraft.Open(new ContentWorkspaceFile(_path, ContentWorkspaceFileFormat.Text));
            Assert.That(_draft.ReadRows()[0].Values["Script"], Is.EqualTo(script));
            _draft.SetCell(0, 0, script + "  wait 1\n");
            _draft.Save();
            Assert.That(File.ReadAllText(_path), Is.EqualTo(script + "  wait 1\n"));
        }
    }
}
