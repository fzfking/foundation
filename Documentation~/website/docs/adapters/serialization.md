---
title: Serialization
sidebar_position: 3
---

# Serialization

Save data, configs and tutorial data go through one small port, so the binary format is a project decision.

```csharp
public interface ISerializer
{
    byte[] Serialize<T>(T value);
    T Deserialize<T>(byte[] bytes);
    object Deserialize(Type type, byte[] bytes);
}
```

Serialization has no default implementation. Choose one adapter and register it once, for example next to the
[save](../app/save) and [config](../app/config) installers.

| Adapter | Assembly | Switched on by |
| --- | --- | --- |
| MemoryPack | `Foundation.Infrastructure.MemoryPack` | `FOUNDATION_MEMORYPACK` (manual) |
| MessagePack | `Foundation.Infrastructure.MessagePack` | `FOUNDATION_MESSAGEPACK` (manual) |

Both libraries are NuGet packages, which Unity's `versionDefines` cannot detect. Install them with NuGetForUnity, then
run **Foundation → Bootstrap New Project → Finish Setup**, which verifies the assemblies are present and sets the
define.

```csharp compile
using Foundation.DI;
using Foundation.Infrastructure.MemoryPack;
using Foundation.Serialization;

public sealed class SerializationInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
        => registry.Register<MemoryPackSerializerAdapter>(ServiceLifetime.Singleton).As<ISerializer>();
}
```

## MemoryPack

`MemoryPackSerializerAdapter` is one available option. Its editor assembly registers it with the content build
(`ConfigSerializers`), so with MemoryPack as the only installed adapter the config and localization export writes
MemoryPack binaries without any setup; see [the binary format of the build](../app/config#the-binary-format).

Your own types use MemoryPack's usual `[MemoryPackable]`. The package's data types (`TutorialConfig`,
`TutorialSnapshot`, `TutorialScriptConfig` and its nodes, `LocalizationTableData`) carry no attribute, so
`FoundationMemoryPackFormatters` holds hand-written formatters for them, in the layout the attribute would give. They
are registered

- when the adapter is constructed;
- on `[RuntimeInitializeOnLoadMethod]` and, in the Editor, `[InitializeOnLoadMethod]`, so code that calls
  `MemoryPackSerializer` directly works without the adapter;
- by an explicit `FoundationMemoryPackFormatters.Register()`, which a HybridCLR hot-update assembly needs because no
  load-time initializers run there.

## MessagePack

`MessagePackSerializerAdapter` serves runtime save and config data you author yourself. `FoundationMessagePackResolver`
contains hand-written formatters for the package's data types, plus array formatters so a config group such as
`TutorialScriptConfig[]` works as a root. Nothing is generated and nothing is registered globally: the resolver is
passed in the serializer options, so the supplied formatters do not require dynamic code generation. Project types still
need AOT-compatible formatters.

The adapter reads with `MessagePackSecurity.UntrustedData`, because a save or a downloaded config is not data the
program wrote a moment ago: nesting is capped and dictionary keys are hashed so that crafted input cannot exhaust the
stack or degrade a dictionary.

For your own types, `new MessagePackSerializerAdapter()` falls back to `StandardResolver`, which needs attributes and
emits code at runtime: fine in the Editor and on Mono, not on IL2CPP. For an AOT build, pass the resolvers your game
generated:

```csharp
new MessagePackSerializerAdapter(GeneratedResolver.Instance);
```

The content build can write MessagePack too: with it as the only installed adapter the build uses it, and with both
installed you choose through `ConfigBuildSettings.Serializer`. The adapter and its tests were verified against
MessagePack-CSharp 3.1.2 in a standalone .NET project (the adapter's options were checked again on 3.1.9, the version to
use: earlier releases are affected by published advisories, including unbounded recursion on skipped nested values that
`UntrustedData` does not prevent); neither the adapter nor a MessagePack content build has been
exercised inside a Unity project.

## Writing another serializer

Implement `ISerializer` over any library. The package's data types are plain classes with public fields; if your library
needs formatters or attributes for them, supply those in the adapter, as the two above do.

To have the content build write that format, either set `ConfigBuildSettings.Serializer` to an instance, or ship a small
editor assembly like the two adapters do that calls `ConfigSerializers.Register("Name", () => new YourSerializer())`
from an `[InitializeOnLoadMethod]`.
