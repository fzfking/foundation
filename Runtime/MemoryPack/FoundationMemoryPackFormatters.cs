using Foundation.Localization;
using Foundation.Tutorial;
using Foundation.Tutorial.Flow;
using MemoryPack;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// MemoryPack declares `scoped` on the value parameter; Unity compiles with an older language version where the keyword is unavailable.
#pragma warning disable CS9074

namespace Foundation.Infrastructure.MemoryPack
{
    /// <summary>
    /// MemoryPack formatters for the Foundation's own data types (<see cref="TutorialConfig"/>, the tutorial
    /// snapshot and script model, <see cref="LocalizationTableData"/>). The types stay plain classes that any
    /// <see cref="Foundation.Serialization.ISerializer"/> can handle; this adapter teaches MemoryPack their shape
    /// instead of the types carrying <c>[MemoryPackable]</c> behind a define. The byte layout is the one the
    /// generator produces for a public-field class (member count, then the fields in declaration order).
    /// </summary>
    /// <remarks>
    /// Registered automatically in the Editor and in players, and by the adapter's constructor. An assembly whose
    /// static initializers do not run on load (HybridCLR hot-update) that uses MemoryPack directly, without the
    /// adapter, calls <see cref="Register"/> itself at startup.
    /// </remarks>
    public static class FoundationMemoryPackFormatters
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered)
                return;

            _registered = true;
            MemoryPackFormatterProvider.Register(new TutorialConfigFormatter());
            MemoryPackFormatterProvider.Register(new TutorialChapterSnapshotFormatter());
            MemoryPackFormatterProvider.Register(new TutorialSnapshotFormatter());
            MemoryPackFormatterProvider.Register(new TutorialScriptConfigFormatter());
            MemoryPackFormatterProvider.Register(new TutorialStepScriptFormatter());
            MemoryPackFormatterProvider.Register(new TutorialNodeFormatter());
            MemoryPackFormatterProvider.Register(new TutorialNamedArgumentFormatter());
            MemoryPackFormatterProvider.Register(new LocalizationTableDataFormatter());
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterAtRuntime() => Register();

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void RegisterInEditor() => Register();
#endif

        private sealed class TutorialConfigFormatter : MemoryPackFormatter<TutorialConfig>
        {
            private const byte Members = 5;

            public override void Serialize<TBufferWriter>(
                ref MemoryPackWriter<TBufferWriter> writer,
                ref TutorialConfig value)
            {
                if (value == null)
                {
                    writer.WriteNullObjectHeader();
                    return;
                }

                writer.WriteObjectHeader(Members);
                writer.WriteString(value.Id);
                writer.WriteString(value.TriggerId);
                writer.WriteUnmanaged(value.Priority);
                writer.WriteArray(value.StepIds);
                writer.WriteArray(value.CheckpointStepIds);
            }

            public override void Deserialize(
                ref MemoryPackReader reader,
                ref TutorialConfig value)
            {
                if (!reader.TryReadObjectHeader(out var count))
                {
                    value = null;
                    return;
                }

                if (count != Members)
                    MemoryPackSerializationException.ThrowInvalidPropertyCount(typeof(TutorialConfig), Members, count);

                value = new TutorialConfig
                {
                    Id = reader.ReadString(),
                    TriggerId = reader.ReadString(),
                    Priority = reader.ReadUnmanaged<int>(),
                    StepIds = reader.ReadArray<string>(),
                    CheckpointStepIds = reader.ReadArray<string>(),
                };
            }
        }

        private sealed class TutorialChapterSnapshotFormatter : MemoryPackFormatter<TutorialChapterSnapshot>
        {
            private const byte Members = 5;

            public override void Serialize<TBufferWriter>(
                ref MemoryPackWriter<TBufferWriter> writer,
                ref TutorialChapterSnapshot value)
            {
                if (value == null)
                {
                    writer.WriteNullObjectHeader();
                    return;
                }

                writer.WriteObjectHeader(Members);
                writer.WriteString(value.ChapterId);
                writer.WriteUnmanaged(value.Status);
                writer.WriteString(value.CurrentStepId);
                writer.WriteUnmanaged(value.IsPending);
                writer.WriteArray(value.CompletedStepIds);
            }

            public override void Deserialize(
                ref MemoryPackReader reader,
                ref TutorialChapterSnapshot value)
            {
                if (!reader.TryReadObjectHeader(out var count))
                {
                    value = null;
                    return;
                }

                if (count != Members)
                    MemoryPackSerializationException.ThrowInvalidPropertyCount(typeof(TutorialChapterSnapshot), Members, count);

                value = new TutorialChapterSnapshot
                {
                    ChapterId = reader.ReadString(),
                    Status = reader.ReadUnmanaged<int>(),
                    CurrentStepId = reader.ReadString(),
                    IsPending = reader.ReadUnmanaged<bool>(),
                    CompletedStepIds = reader.ReadArray<string>(),
                };
            }
        }

        private sealed class TutorialSnapshotFormatter : MemoryPackFormatter<TutorialSnapshot>
        {
            private const byte Members = 1;

            public override void Serialize<TBufferWriter>(
                ref MemoryPackWriter<TBufferWriter> writer,
                ref TutorialSnapshot value)
            {
                if (value == null)
                {
                    writer.WriteNullObjectHeader();
                    return;
                }

                writer.WriteObjectHeader(Members);
                writer.WriteArray(value.Chapters);
            }

            public override void Deserialize(
                ref MemoryPackReader reader,
                ref TutorialSnapshot value)
            {
                if (!reader.TryReadObjectHeader(out var count))
                {
                    value = null;
                    return;
                }

                if (count != Members)
                    MemoryPackSerializationException.ThrowInvalidPropertyCount(typeof(TutorialSnapshot), Members, count);

                value = new TutorialSnapshot { Chapters = reader.ReadArray<TutorialChapterSnapshot>() };
            }
        }

        private sealed class TutorialScriptConfigFormatter : MemoryPackFormatter<TutorialScriptConfig>
        {
            private const byte Members = 4;

            public override void Serialize<TBufferWriter>(
                ref MemoryPackWriter<TBufferWriter> writer,
                ref TutorialScriptConfig value)
            {
                if (value == null)
                {
                    writer.WriteNullObjectHeader();
                    return;
                }

                writer.WriteObjectHeader(Members);
                writer.WriteString(value.Id);
                writer.WriteString(value.TriggerId);
                writer.WriteUnmanaged(value.Priority);
                writer.WriteArray(value.Steps);
            }

            public override void Deserialize(
                ref MemoryPackReader reader,
                ref TutorialScriptConfig value)
            {
                if (!reader.TryReadObjectHeader(out var count))
                {
                    value = null;
                    return;
                }

                if (count != Members)
                    MemoryPackSerializationException.ThrowInvalidPropertyCount(typeof(TutorialScriptConfig), Members, count);

                value = new TutorialScriptConfig
                {
                    Id = reader.ReadString(),
                    TriggerId = reader.ReadString(),
                    Priority = reader.ReadUnmanaged<int>(),
                    Steps = reader.ReadArray<TutorialStepScript>(),
                };
            }
        }

        private sealed class TutorialStepScriptFormatter : MemoryPackFormatter<TutorialStepScript>
        {
            private const byte Members = 4;

            public override void Serialize<TBufferWriter>(
                ref MemoryPackWriter<TBufferWriter> writer,
                ref TutorialStepScript value)
            {
                if (value == null)
                {
                    writer.WriteNullObjectHeader();
                    return;
                }

                writer.WriteObjectHeader(Members);
                writer.WriteString(value.Id);
                writer.WriteUnmanaged(value.IsCheckpoint);
                writer.WriteArray(value.Body);
                writer.WriteArray(value.Finally);
            }

            public override void Deserialize(
                ref MemoryPackReader reader,
                ref TutorialStepScript value)
            {
                if (!reader.TryReadObjectHeader(out var count))
                {
                    value = null;
                    return;
                }

                if (count != Members)
                    MemoryPackSerializationException.ThrowInvalidPropertyCount(typeof(TutorialStepScript), Members, count);

                value = new TutorialStepScript
                {
                    Id = reader.ReadString(),
                    IsCheckpoint = reader.ReadUnmanaged<bool>(),
                    Body = reader.ReadArray<TutorialNode>(),
                    Finally = reader.ReadArray<TutorialNode>(),
                };
            }
        }

        private sealed class TutorialNodeFormatter : MemoryPackFormatter<TutorialNode>
        {
            private const byte Members = 8;

            public override void Serialize<TBufferWriter>(
                ref MemoryPackWriter<TBufferWriter> writer,
                ref TutorialNode value)
            {
                if (value == null)
                {
                    writer.WriteNullObjectHeader();
                    return;
                }

                writer.WriteObjectHeader(Members);
                writer.WriteUnmanaged(value.Kind);
                writer.WriteString(value.Command);
                writer.WriteArray(value.Arguments);
                writer.WriteArray(value.NamedArguments);
                writer.WriteUnmanaged(value.Negated);
                writer.WriteArray(value.Children);
                writer.WriteArray(value.ElseChildren);
                writer.WriteUnmanaged(value.Line);
            }

            public override void Deserialize(
                ref MemoryPackReader reader,
                ref TutorialNode value)
            {
                if (!reader.TryReadObjectHeader(out var count))
                {
                    value = null;
                    return;
                }

                if (count != Members)
                    MemoryPackSerializationException.ThrowInvalidPropertyCount(typeof(TutorialNode), Members, count);

                value = new TutorialNode
                {
                    Kind = reader.ReadUnmanaged<TutorialNodeKind>(),
                    Command = reader.ReadString(),
                    Arguments = reader.ReadArray<string>(),
                    NamedArguments = reader.ReadArray<TutorialNamedArgument>(),
                    Negated = reader.ReadUnmanaged<bool>(),
                    Children = reader.ReadArray<TutorialNode>(),
                    ElseChildren = reader.ReadArray<TutorialNode>(),
                    Line = reader.ReadUnmanaged<int>(),
                };
            }
        }

        private sealed class TutorialNamedArgumentFormatter : MemoryPackFormatter<TutorialNamedArgument>
        {
            private const byte Members = 2;

            public override void Serialize<TBufferWriter>(
                ref MemoryPackWriter<TBufferWriter> writer,
                ref TutorialNamedArgument value)
            {
                if (value == null)
                {
                    writer.WriteNullObjectHeader();
                    return;
                }

                writer.WriteObjectHeader(Members);
                writer.WriteString(value.Name);
                writer.WriteString(value.Value);
            }

            public override void Deserialize(
                ref MemoryPackReader reader,
                ref TutorialNamedArgument value)
            {
                if (!reader.TryReadObjectHeader(out var count))
                {
                    value = null;
                    return;
                }

                if (count != Members)
                    MemoryPackSerializationException.ThrowInvalidPropertyCount(typeof(TutorialNamedArgument), Members, count);

                value = new TutorialNamedArgument { Name = reader.ReadString(), Value = reader.ReadString() };
            }
        }

        private sealed class LocalizationTableDataFormatter : MemoryPackFormatter<LocalizationTableData>
        {
            private const byte Members = 3;

            public override void Serialize<TBufferWriter>(
                ref MemoryPackWriter<TBufferWriter> writer,
                ref LocalizationTableData value)
            {
                if (value == null)
                {
                    writer.WriteNullObjectHeader();
                    return;
                }

                writer.WriteObjectHeader(Members);
                writer.WriteString(value.Locale);
                writer.WriteArray(value.Keys);
                writer.WriteArray(value.Values);
            }

            public override void Deserialize(
                ref MemoryPackReader reader,
                ref LocalizationTableData value)
            {
                if (!reader.TryReadObjectHeader(out var count))
                {
                    value = null;
                    return;
                }

                if (count != Members)
                    MemoryPackSerializationException.ThrowInvalidPropertyCount(typeof(LocalizationTableData), Members, count);

                value = new LocalizationTableData
                {
                    Locale = reader.ReadString(),
                    Keys = reader.ReadArray<string>(),
                    Values = reader.ReadArray<string>(),
                };
            }
        }
    }
}
