using System;

namespace Foundation.Save
{
    public interface IFeatureSaveModule
    {
        string SegmentId { get; }
        Type SnapshotType { get; }
        object Capture();
        void Restore(object snapshot);
        byte[] Capture(ISaveSerializer serializer);
        void Restore(ISaveSerializer serializer, byte[] bytes);
    }

    public interface IFeatureSaveModule<TSnapshot> : IFeatureSaveModule
    {
        new TSnapshot Capture();
        void Restore(TSnapshot snapshot);
    }
}
