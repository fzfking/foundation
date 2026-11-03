using System.Collections;
using System;
using System.Collections.Generic;
using Foundation.Serialization;

namespace Foundation.Content.Editor
{
    public abstract class ConfigExportGroup
    {
        public string FileName { get; }

        public abstract IReadOnlyList<string> EntryIds { get; }

        protected ConfigExportGroup(string fileName)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        }

        public abstract bool ContainsNullEntry();

        public abstract byte[] Serialize(ISerializer serializer);
    }

    public sealed class ConfigExportGroup<T> : ConfigExportGroup
    {
        private readonly T _payload;
        private readonly IReadOnlyList<string> _entryIds;

        public override IReadOnlyList<string> EntryIds => _entryIds;

        public ConfigExportGroup(string fileName, T payload, IReadOnlyList<string> entryIds = null)
            : base(fileName)
        {
            _payload = payload;
            _entryIds = entryIds ?? Array.Empty<string>();
        }

        public override bool ContainsNullEntry()
        {
            if (_payload is IEnumerable enumerable && _payload is not string)
            {
                foreach (var item in enumerable)
                    if (item == null)
                        return true;

                return false;
            }

            return _payload == null;
        }

        public override byte[] Serialize(ISerializer serializer) => serializer.Serialize(_payload);
    }
}
