using System;
using System.Collections.Generic;
using Foundation.Serialization;
using MessagePack;
using MessagePack.Resolvers;

namespace Foundation.Infrastructure.MessagePack
{
    /// <summary>
    /// <see cref="ISerializer"/> on MessagePack-CSharp. Foundation's own data types are handled by
    /// <see cref="FoundationMessagePackResolver"/>, which needs no code generation and so works under IL2CPP.
    /// A game's own types need a resolver that suits its platform (a generated one for AOT builds); pass it
    /// here and it is consulted after Foundation's. Without any, MessagePack's standard resolver is the fallback,
    /// which needs the attributes on the types and emits code at runtime, so it is for the Editor and Mono.
    /// </summary>
    public sealed class MessagePackSerializerAdapter : ISerializer
    {
        private readonly MessagePackSerializerOptions _options;

        public MessagePackSerializerAdapter(params IFormatterResolver[] resolvers)
        {
            var chain = new List<IFormatterResolver> { FoundationMessagePackResolver.Instance };
            if (resolvers != null && resolvers.Length > 0)
            {
                chain.AddRange(resolvers);
                chain.Add(BuiltinResolver.Instance);
            }
            else
            {
                chain.Add(StandardResolver.Instance);
            }

            // What is read here is a save or a config that came from a device or a server, so it is treated as untrusted data:
            // object nesting is capped and collection keys are hashed so that crafted keys cannot degrade a dictionary.
            _options = MessagePackSerializerOptions.Standard
                .WithResolver(CompositeResolver.Create(chain.ToArray()))
                .WithSecurity(MessagePackSecurity.UntrustedData);
        }

        public byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, _options);

        public T Deserialize<T>(byte[] bytes) => MessagePackSerializer.Deserialize<T>(bytes, _options);

        public object Deserialize(Type type, byte[] bytes) => MessagePackSerializer.Deserialize(type, bytes, _options);
    }
}
