using System;

namespace Foundation.Serialization
{
    public interface ISerializer
    {
        byte[] Serialize<T>(T value);
        T Deserialize<T>(byte[] bytes);
        object Deserialize(Type type, byte[] bytes);
    }
}
