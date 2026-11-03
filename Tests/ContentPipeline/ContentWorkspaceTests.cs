using System;
using System.Collections.Generic;
using System.Linq;
using Foundation.Content.Authoring;
using Foundation.Content.Editor.Workspace;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.UIElements;

namespace Foundation.Content.Editor.PackageTests
{
    public class WorkspaceTestSourceBase : ScriptableObject, IConfigAuthoringSource
    {
#pragma warning disable CS0414 // read through serialization by the workspace reader, not by code
        [SerializeField] private string description = "inherited setting";
#pragma warning restore CS0414
    }

    public sealed class WorkspaceTestSource : WorkspaceTestSourceBase
    {
        public WorkspaceTestRecord[] Records = Array.Empty<WorkspaceTestRecord>();
        public List<int> Weights = new();
        public Object[] References = Array.Empty<Object>();
        public string Title = "a string is not a collection";
    }

    [Serializable]
    public sealed class WorkspaceTestRecord
    {
        public string Id;
        public int Cost;
        public WorkspaceTestNested Nested = new();
    }

    [Serializable]
    public sealed class WorkspaceTestNested
    {
        public int Amount;
    }

    public sealed class ContentWorkspaceTests
    {
        private WorkspaceTestSource _asset;
        private SerializedObject _serialized;
        private ContentWorkspaceGroup _records;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<WorkspaceTestSource>();
            _asset.Records = new[]
            {
                new WorkspaceTestRecord { Id = "alpha", Cost = 10, Nested = new WorkspaceTestNested { Amount = 4 } },
                new WorkspaceTestRecord { Id = "beta", Cost = 20 },
            };
            _serialized = new(_asset);
            _records = ContentWorkspaceModel.DiscoverGroups(_serialized).Single(group => group.Path == "Records");
        }

        [TearDown]
        public void TearDown()
        {
            _serialized.Dispose();
            Undo.ClearUndo(_asset);
            Object.DestroyImmediate(_asset);
        }

        [Test]
        public void Discovery_SeparatesCollectionsFromScalarAndInheritedSettings()
        {
            var groups = ContentWorkspaceModel.DiscoverGroups(_serialized);
            Assert.That(groups.Where(group => group.IsCollection).Select(group => group.Path),
                Is.EquivalentTo(new[] { "Records", "Weights", "References" }));
            var settings = ContentWorkspaceModel.ReadRows(_serialized, groups.Single(group => !group.IsCollection));
            Assert.That(settings.Single().Values.Keys, Does.Contain("Title"));
            Assert.That(settings.Single().Values.Keys, Does.Contain("description"));
        }

        [Test]
        public void Filter_RetainsOriginalIndicesAndRequiresEveryTermAcrossFields()
        {
            var rows = ContentWorkspaceModel.ReadRows(_serialized, _records);
            var filtered = ContentWorkspaceModel.Filter(rows, "BETA 20");
            Assert.That(filtered.Select(row => row.Index), Is.EqualTo(new[] { 1 }));
            Assert.That(filtered.Single().Path, Is.EqualTo("Records.Array.data[1]"));
            Assert.That(ContentWorkspaceModel.Filter(rows, "alpha 20"), Is.Empty);
        }

        [Test]
        public void Add_CreatesFreshDefaultsInsteadOfRepeatingPreviousRecord()
        {
            var index = ContentWorkspaceModel.Add(_serialized, _records);
            Assert.That(index, Is.EqualTo(2));
            Assert.That(_asset.Records[2].Id, Is.Null.Or.Empty);
            Assert.That(_asset.Records[2].Cost, Is.Zero);
            Assert.That(_asset.Records[0].Cost, Is.EqualTo(10));
        }

        [Test]
        public void Duplicate_PreservesDataAndOwnsIndependentNestedValues()
        {
            var index = ContentWorkspaceModel.Duplicate(_serialized, _records, 0);
            Assert.That(_asset.Records[index].Id, Is.EqualTo("alpha"));
            Assert.That(_asset.Records[index].Cost, Is.EqualTo(10));
            Assert.That(_asset.Records[index].Nested.Amount, Is.EqualTo(4));
            _asset.Records[index].Nested.Amount = 99;
            Assert.That(_asset.Records[0].Nested.Amount, Is.EqualTo(4));
        }

        [Test]
        public void Delete_RemovesObjectReferenceSlotRatherThanOnlyClearingIt()
        {
            _asset.References = new Object[] { _asset, _asset };
            _serialized.Update();
            var group = ContentWorkspaceModel.DiscoverGroups(_serialized).Single(item => item.Path == "References");
            ContentWorkspaceModel.Delete(_serialized, group, 0);
            Assert.That(_asset.References, Has.Length.EqualTo(1));
            Assert.That(_asset.References[0], Is.SameAs(_asset));
        }

        [Test]
        public void CollectionEdits_CanBeUndoneWithoutLosingOriginalRecords()
        {
            Undo.IncrementCurrentGroup();
            ContentWorkspaceModel.Delete(_serialized, _records, 0);
            Undo.FlushUndoRecordObjects();
            Assert.That(_asset.Records.Select(record => record.Id), Is.EqualTo(new[] { "beta" }));
            Undo.PerformUndo();
            _serialized.Update();
            Assert.That(_asset.Records.Select(record => record.Id), Is.EqualTo(new[] { "alpha", "beta" }));
        }

        [Test]
        public void Add_ToPrimitiveListStartsAtZero()
        {
            _asset.Weights.Add(42);
            _serialized.Update();
            var group = ContentWorkspaceModel.DiscoverGroups(_serialized).Single(item => item.Path == "Weights");
            ContentWorkspaceModel.Add(_serialized, group);
            Assert.That(_asset.Weights, Is.EqualTo(new[] { 42, 0 }));
        }

        [Test]
        public void DefaultPresentation_CanConstructWorkspaceWithoutLightSide()
        {
            var rows = ContentWorkspaceModel.ReadRows(_serialized, _records);
            var presentation = new DefaultContentWorkspacePresentation();
            var table = presentation.CreateTable(rows, _ => { }, 0);
            Assert.That(table, Is.TypeOf<MultiColumnListView>());
            Assert.That(((MultiColumnListView)table).columns.Count, Is.EqualTo(3));
        }
    }
}
