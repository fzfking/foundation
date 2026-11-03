using System;
using System.Collections.Generic;
using Foundation.Localization;
using Foundation.Tutorial;
using Foundation.Tutorial.Flow;
using MessagePack;
using MessagePack.Formatters;

namespace Foundation.Infrastructure.MessagePack
{
    /// <summary>
    /// MessagePack formatters for the Foundation's own data types (<see cref="TutorialConfig"/>, the tutorial
    /// snapshot and script model, <see cref="LocalizationTableData"/>), found through an ordinary resolver: no
    /// attributes on the types, no global registration, nothing generated. Each object is an array of its public
    /// fields in declaration order - the layout <c>[MessagePackObject]</c> with sequential <c>[Key]</c>s gives - and
    /// reading tolerates a newer writer's extra fields and an older writer's missing ones.
    /// </summary>
    public sealed class FoundationMessagePackResolver : IFormatterResolver
    {
        public static readonly FoundationMessagePackResolver Instance = new();

        // Concrete instances only, so nothing is built by reflection at runtime. The arrays are there because a
        // serialized config group is an array of its entries.
        private static readonly Dictionary<Type, object> Formatters = new()
        {
            { typeof(TutorialConfig), new TutorialConfigFormatter() },
            { typeof(TutorialChapterSnapshot), new TutorialChapterSnapshotFormatter() },
            { typeof(TutorialSnapshot), new TutorialSnapshotFormatter() },
            { typeof(TutorialScriptConfig), new TutorialScriptConfigFormatter() },
            { typeof(TutorialStepScript), new TutorialStepScriptFormatter() },
            { typeof(TutorialNode), new TutorialNodeFormatter() },
            { typeof(TutorialNamedArgument), new TutorialNamedArgumentFormatter() },
            { typeof(LocalizationTableData), new LocalizationTableDataFormatter() },
            { typeof(TutorialConfig[]), new ArrayFormatter<TutorialConfig>() },
            { typeof(TutorialChapterSnapshot[]), new ArrayFormatter<TutorialChapterSnapshot>() },
            { typeof(TutorialScriptConfig[]), new ArrayFormatter<TutorialScriptConfig>() },
            { typeof(TutorialStepScript[]), new ArrayFormatter<TutorialStepScript>() },
            { typeof(TutorialNode[]), new ArrayFormatter<TutorialNode>() },
            { typeof(TutorialNamedArgument[]), new ArrayFormatter<TutorialNamedArgument>() },
            { typeof(LocalizationTableData[]), new ArrayFormatter<LocalizationTableData>() },
        };

        private FoundationMessagePackResolver()
        {
        }

        public IMessagePackFormatter<T> GetFormatter<T>() => Cache<T>.Formatter;

        private static class Cache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter =
                Formatters.TryGetValue(typeof(T), out var formatter) ? (IMessagePackFormatter<T>)formatter : null;
        }

        private static void WriteStrings(ref MessagePackWriter writer, string[] values)
        {
            if (values == null)
            {
                writer.WriteNil();
                return;
            }

            writer.WriteArrayHeader(values.Length);
            foreach (var value in values)
                writer.Write(value);
        }

        private static string[] ReadStrings(ref MessagePackReader reader)
        {
            if (reader.TryReadNil())
                return null;

            var values = new string[reader.ReadArrayHeader()];
            for (var index = 0; index < values.Length; index++)
                values[index] = reader.ReadString();
            return values;
        }

        private static void WriteArray<T>(ref MessagePackWriter writer, T[] values, MessagePackSerializerOptions options)
        {
            if (values == null)
            {
                writer.WriteNil();
                return;
            }

            var formatter = options.Resolver.GetFormatterWithVerify<T>();
            writer.WriteArrayHeader(values.Length);
            foreach (var value in values)
                formatter.Serialize(ref writer, value, options);
        }

        private static T[] ReadArray<T>(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
                return null;

            var formatter = options.Resolver.GetFormatterWithVerify<T>();
            var values = new T[reader.ReadArrayHeader()];
            options.Security.DepthStep(ref reader);
            try
            {
                for (var index = 0; index < values.Length; index++)
                    values[index] = formatter.Deserialize(ref reader, options);
            }
            finally
            {
                reader.Depth--;
            }

            return values;
        }

        private sealed class TutorialConfigFormatter : IMessagePackFormatter<TutorialConfig>
        {
            public void Serialize(ref MessagePackWriter writer, TutorialConfig value, MessagePackSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNil();
                    return;
                }

                writer.WriteArrayHeader(5);
                writer.Write(value.Id);
                writer.Write(value.TriggerId);
                writer.Write(value.Priority);
                WriteStrings(ref writer, value.StepIds);
                WriteStrings(ref writer, value.CheckpointStepIds);
            }

            public TutorialConfig Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                if (reader.TryReadNil())
                    return null;

                options.Security.DepthStep(ref reader);
                try
                {
                    var result = new TutorialConfig();
                    var count = reader.ReadArrayHeader();
                    for (var index = 0; index < count; index++)
                    {
                        switch (index)
                        {
                            case 0: result.Id = reader.ReadString(); break;
                            case 1: result.TriggerId = reader.ReadString(); break;
                            case 2: result.Priority = reader.ReadInt32(); break;
                            case 3: result.StepIds = ReadStrings(ref reader); break;
                            case 4: result.CheckpointStepIds = ReadStrings(ref reader); break;
                            default: reader.Skip(); break;
                        }
                    }

                    return result;
                }
                finally
                {
                    reader.Depth--;
                }
            }
        }

        private sealed class TutorialChapterSnapshotFormatter : IMessagePackFormatter<TutorialChapterSnapshot>
        {
            public void Serialize(ref MessagePackWriter writer, TutorialChapterSnapshot value, MessagePackSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNil();
                    return;
                }

                writer.WriteArrayHeader(5);
                writer.Write(value.ChapterId);
                writer.Write(value.Status);
                writer.Write(value.CurrentStepId);
                writer.Write(value.IsPending);
                WriteStrings(ref writer, value.CompletedStepIds);
            }

            public TutorialChapterSnapshot Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                if (reader.TryReadNil())
                    return null;

                options.Security.DepthStep(ref reader);
                try
                {
                    var result = new TutorialChapterSnapshot();
                    var count = reader.ReadArrayHeader();
                    for (var index = 0; index < count; index++)
                    {
                        switch (index)
                        {
                            case 0: result.ChapterId = reader.ReadString(); break;
                            case 1: result.Status = reader.ReadInt32(); break;
                            case 2: result.CurrentStepId = reader.ReadString(); break;
                            case 3: result.IsPending = reader.ReadBoolean(); break;
                            case 4: result.CompletedStepIds = ReadStrings(ref reader); break;
                            default: reader.Skip(); break;
                        }
                    }

                    return result;
                }
                finally
                {
                    reader.Depth--;
                }
            }
        }

        private sealed class TutorialSnapshotFormatter : IMessagePackFormatter<TutorialSnapshot>
        {
            public void Serialize(ref MessagePackWriter writer, TutorialSnapshot value, MessagePackSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNil();
                    return;
                }

                writer.WriteArrayHeader(1);
                WriteArray(ref writer, value.Chapters, options);
            }

            public TutorialSnapshot Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                if (reader.TryReadNil())
                    return null;

                options.Security.DepthStep(ref reader);
                try
                {
                    var result = new TutorialSnapshot();
                    var count = reader.ReadArrayHeader();
                    for (var index = 0; index < count; index++)
                    {
                        switch (index)
                        {
                            case 0: result.Chapters = ReadArray<TutorialChapterSnapshot>(ref reader, options); break;
                            default: reader.Skip(); break;
                        }
                    }

                    return result;
                }
                finally
                {
                    reader.Depth--;
                }
            }
        }

        private sealed class TutorialScriptConfigFormatter : IMessagePackFormatter<TutorialScriptConfig>
        {
            public void Serialize(ref MessagePackWriter writer, TutorialScriptConfig value, MessagePackSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNil();
                    return;
                }

                writer.WriteArrayHeader(4);
                writer.Write(value.Id);
                writer.Write(value.TriggerId);
                writer.Write(value.Priority);
                WriteArray(ref writer, value.Steps, options);
            }

            public TutorialScriptConfig Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                if (reader.TryReadNil())
                    return null;

                options.Security.DepthStep(ref reader);
                try
                {
                    var result = new TutorialScriptConfig();
                    var count = reader.ReadArrayHeader();
                    for (var index = 0; index < count; index++)
                    {
                        switch (index)
                        {
                            case 0: result.Id = reader.ReadString(); break;
                            case 1: result.TriggerId = reader.ReadString(); break;
                            case 2: result.Priority = reader.ReadInt32(); break;
                            case 3: result.Steps = ReadArray<TutorialStepScript>(ref reader, options); break;
                            default: reader.Skip(); break;
                        }
                    }

                    return result;
                }
                finally
                {
                    reader.Depth--;
                }
            }
        }

        private sealed class TutorialStepScriptFormatter : IMessagePackFormatter<TutorialStepScript>
        {
            public void Serialize(ref MessagePackWriter writer, TutorialStepScript value, MessagePackSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNil();
                    return;
                }

                writer.WriteArrayHeader(4);
                writer.Write(value.Id);
                writer.Write(value.IsCheckpoint);
                WriteArray(ref writer, value.Body, options);
                WriteArray(ref writer, value.Finally, options);
            }

            public TutorialStepScript Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                if (reader.TryReadNil())
                    return null;

                options.Security.DepthStep(ref reader);
                try
                {
                    var result = new TutorialStepScript();
                    var count = reader.ReadArrayHeader();
                    for (var index = 0; index < count; index++)
                    {
                        switch (index)
                        {
                            case 0: result.Id = reader.ReadString(); break;
                            case 1: result.IsCheckpoint = reader.ReadBoolean(); break;
                            case 2: result.Body = ReadArray<TutorialNode>(ref reader, options); break;
                            case 3: result.Finally = ReadArray<TutorialNode>(ref reader, options); break;
                            default: reader.Skip(); break;
                        }
                    }

                    return result;
                }
                finally
                {
                    reader.Depth--;
                }
            }
        }

        private sealed class TutorialNodeFormatter : IMessagePackFormatter<TutorialNode>
        {
            public void Serialize(ref MessagePackWriter writer, TutorialNode value, MessagePackSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNil();
                    return;
                }

                writer.WriteArrayHeader(8);
                writer.Write((byte)value.Kind);
                writer.Write(value.Command);
                WriteStrings(ref writer, value.Arguments);
                WriteArray(ref writer, value.NamedArguments, options);
                writer.Write(value.Negated);
                WriteArray(ref writer, value.Children, options);
                WriteArray(ref writer, value.ElseChildren, options);
                writer.Write(value.Line);
            }

            public TutorialNode Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                if (reader.TryReadNil())
                    return null;

                options.Security.DepthStep(ref reader);
                try
                {
                    var result = new TutorialNode();
                    var count = reader.ReadArrayHeader();
                    for (var index = 0; index < count; index++)
                    {
                        switch (index)
                        {
                            case 0: result.Kind = (TutorialNodeKind)reader.ReadByte(); break;
                            case 1: result.Command = reader.ReadString(); break;
                            case 2: result.Arguments = ReadStrings(ref reader); break;
                            case 3: result.NamedArguments = ReadArray<TutorialNamedArgument>(ref reader, options); break;
                            case 4: result.Negated = reader.ReadBoolean(); break;
                            case 5: result.Children = ReadArray<TutorialNode>(ref reader, options); break;
                            case 6: result.ElseChildren = ReadArray<TutorialNode>(ref reader, options); break;
                            case 7: result.Line = reader.ReadInt32(); break;
                            default: reader.Skip(); break;
                        }
                    }

                    return result;
                }
                finally
                {
                    reader.Depth--;
                }
            }
        }

        private sealed class TutorialNamedArgumentFormatter : IMessagePackFormatter<TutorialNamedArgument>
        {
            public void Serialize(ref MessagePackWriter writer, TutorialNamedArgument value, MessagePackSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNil();
                    return;
                }

                writer.WriteArrayHeader(2);
                writer.Write(value.Name);
                writer.Write(value.Value);
            }

            public TutorialNamedArgument Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                if (reader.TryReadNil())
                    return null;

                options.Security.DepthStep(ref reader);
                try
                {
                    var result = new TutorialNamedArgument();
                    var count = reader.ReadArrayHeader();
                    for (var index = 0; index < count; index++)
                    {
                        switch (index)
                        {
                            case 0: result.Name = reader.ReadString(); break;
                            case 1: result.Value = reader.ReadString(); break;
                            default: reader.Skip(); break;
                        }
                    }

                    return result;
                }
                finally
                {
                    reader.Depth--;
                }
            }
        }

        private sealed class LocalizationTableDataFormatter : IMessagePackFormatter<LocalizationTableData>
        {
            public void Serialize(ref MessagePackWriter writer, LocalizationTableData value, MessagePackSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNil();
                    return;
                }

                writer.WriteArrayHeader(3);
                writer.Write(value.Locale);
                WriteStrings(ref writer, value.Keys);
                WriteStrings(ref writer, value.Values);
            }

            public LocalizationTableData Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                if (reader.TryReadNil())
                    return null;

                options.Security.DepthStep(ref reader);
                try
                {
                    var result = new LocalizationTableData();
                    var count = reader.ReadArrayHeader();
                    for (var index = 0; index < count; index++)
                    {
                        switch (index)
                        {
                            case 0: result.Locale = reader.ReadString(); break;
                            case 1: result.Keys = ReadStrings(ref reader); break;
                            case 2: result.Values = ReadStrings(ref reader); break;
                            default: reader.Skip(); break;
                        }
                    }

                    return result;
                }
                finally
                {
                    reader.Depth--;
                }
            }
        }
    }
}
