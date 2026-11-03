using System;

namespace Foundation.Save
{
    public abstract class FeatureSaveModule<TSnapshot> : IFeatureSaveModule<TSnapshot>
    {
        public abstract string SegmentId { get; }

        public Type SnapshotType => typeof(TSnapshot);

        public abstract TSnapshot Capture();

        public abstract void Restore(TSnapshot snapshot);

        object IFeatureSaveModule.Capture()
        {
            return Capture();
        }

        void IFeatureSaveModule.Restore(object snapshot)
        {
            if (snapshot is not TSnapshot typedSnapshot)
                throw new ArgumentException(
                    $"Snapshot for segment '{SegmentId}' must be of type '{typeof(TSnapshot).Name}'.",
                    nameof(snapshot));

            Restore(typedSnapshot);
        }

        byte[] IFeatureSaveModule.Capture(ISaveSerializer serializer)
        {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            return serializer.Serialize(Capture());
        }

        void IFeatureSaveModule.Restore(ISaveSerializer serializer, byte[] bytes)
        {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));

            Restore(serializer.Deserialize<TSnapshot>(bytes));
        }
    }
}
