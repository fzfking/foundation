using System;
using Foundation.Serialization;
using MemoryPack;

namespace Foundation.Infrastructure.MemoryPack
{
    public sealed class MemoryPackSerializerAdapter : ISerializer
    {
        public MemoryPackSerializerAdapter() => FoundationMemoryPackFormatters.Register();
        
        public byte[] Serialize<T>(T value) => MemoryPackSerializer.Serialize(value);
        public T Deserialize<T>(byte[] bytes) => MemoryPackSerializer.Deserialize<T>(bytes);
        public object Deserialize(Type type, byte[] bytes) => MemoryPackSerializer.Deserialize(type, bytes);
    }
}
