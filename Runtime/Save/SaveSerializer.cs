using System;
using Foundation.Serialization;

namespace Foundation.Save
{
    public interface ISaveSerializer
    {
        byte[] Serialize<T>(T value);
        T Deserialize<T>(byte[] bytes);
        object Deserialize(Type type, byte[] bytes);
    }

    public sealed class SaveSerializer : ISaveSerializer
    {
        private readonly ISerializer _serializer;

        public SaveSerializer(ISerializer serializer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public byte[] Serialize<T>(T value) => _serializer.Serialize(value);
        public T Deserialize<T>(byte[] bytes) => _serializer.Deserialize<T>(bytes);
        public object Deserialize(Type type, byte[] bytes) => _serializer.Deserialize(type, bytes);
    }
}
