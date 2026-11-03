---
title: Typed ids
sidebar_position: 3
---

# Typed ids

Typed IDs prevent accidentally passing a `string` or `Guid` of the wrong kind between systems. For example, this does not compile:

```csharp
audio.Play(screenId);        // ScreenId is not an AudioEventId
router.Open(audioEventId);   // and the other way around
```

Assembly: `Foundation.TypedIds` (no dependencies). Editor tooling: `Foundation.TypedIds.Editor`.

## Two kinds of id, never mixed

| | Static content id | Runtime id |
| --- | --- | --- |
| Identifies | authored definitions: an audio event, a screen, a production | things created while the game runs: entities, save transactions, network records |
| Backed by | a string **key** (`"ui.button_click"`) | a `Guid` |
| Declared with | `[StableId]` | `[RuntimeId]` |
| Created by | a designer, or `AudioEventId.From("ui.button_click")` | `SaveTransactionId.New(guidGenerator)` |
| Stable across renames | yes: the key is separate from the display name | not applicable |
| Stored in saves and configs | yes | yes |

A stable id is a **key, not a hash and not a GUID**: it is readable in git diffs, logs and YAML, and renaming a
definition's display name never touches it. Changing the *key* requires a content migration. Validation detects missing
references in the scanned assets, but cannot detect every key change or migrate existing saves. Runtime IDs use GUIDs
when identity is generated because they need no coordination; they say nothing about content and do not sort by
creation time.

## Declaring ids

```csharp compile
using System.Collections.Generic;
using Foundation.TypedIds;

[StableId]
public partial struct ScreenId
{
}

[StableId]
public partial struct ProductionId
{
}

[RuntimeId]
public partial struct SaveTransactionId
{
}

public static class TypedIdExample
{
    public static void Run()
    {
        var id = ScreenId.From("hud.main");        // or new ScreenId("hud.main")
        var key = id.Value;                        // "hud.main" (empty string for default)
        var empty = default(ScreenId).IsEmpty;     // true
        var lookup = new Dictionary<ScreenId, string> { [id] = key };

        var guids = new GuidGenerator();           // inject IGuidGenerator; a test supplies fixed ids
        var transaction = SaveTransactionId.New(guids);
        UnityEngine.Debug.Log($"{id} {empty} {lookup.Count} {transaction}");
    }
}
```

Declare it `partial struct`, not `readonly` (Unity does not serialize readonly fields; the generated members are
readonly). A Roslyn source generator adds the rest, so there is no per-id boilerplate. A stable id has
`From(key)`, a constructor, `Value`, `IsEmpty`, `None`, `==`, `CompareTo`, and a debugger display. A runtime id uses
a `Guid`, with `New(generator)`, `TryParse` and a `Guid` constructor instead of `From(key)`.

- Stable-id equality, hashing and ordering are by key, ordinal. `null` and `""` are the same "none" id, so a freshly deserialized
  default equals `default`.
- No boxing, reflection or allocation in normal use; `Dictionary<TId, T>` uses the struct's own `IEquatable`.
- The API defines **no implicit or explicit conversions** to or from `string` or between id types. That is the point.
- `StableIdKey.IsValid(key)` defines the key syntax (ASCII letters, digits, `. _ - / :`, at most 128 characters) and
  `StableIdKey.StableHash(key)` is a process-independent FNV-1a for when a hash must be persisted. The runtime
  `GetHashCode()` is **not** stable between processes.

Foundation declares `AudioEventId`, `AudioChannelId`, `PoolId`, `BadgeId` and `FeedbackId`. Declare project-specific ids
(`ProductionId`, `ScreenId`, `ItemId`, `SequenceId`, ...) next to the code that owns the concept.

## The generator

Unity applies the source generator to every assembly in the project, including game assemblies.

| Diagnostic | Meaning |
| --- | --- |
| `TID001` | The id type is not `partial`. |
| `TID002` | The id type is `readonly`. |
| `TID003` | The id type is generic or nested. |

`UnityEngine.SerializeField` is added only when the declaring assembly references UnityEngine; in an engine-free
assembly the id still works in code but is not Unity-serialized. With MemoryPack in an engine-free assembly, call
`TypedIdRegistration.RegisterMemoryPackFormatters()` explicitly.

## Serialization

- **Unity.** A `[StableId]` is serialized as one string field named `value`, and round-trips through `JsonUtility`,
  ScriptableObjects, prefabs and scenes. A `[RuntimeId]` is not Unity-serialized (Unity cannot serialize `Guid`); create
  it at runtime and persist it with MemoryPack.
- **MemoryPack.** When MemoryPack is referenced, the generator emits `{Id}MemoryPackFormatter` and
  `<Assembly>.Generated.TypedIdRegistration.RegisterMemoryPackFormatters()`. Registration runs automatically in the
  Editor and in players, but **not** in assemblies whose static initializers do not run on load (HybridCLR hot-update
  assemblies): call it yourself at startup. Annotate the DTO field:

  ```csharp
  [MemoryPackable]
  public partial class AudioBinding
  {
      [MemoryPackAllowSerialize] public AudioEventId Event;   // stable id: formatter
      public SaveTransactionId Transaction;                   // runtime id: plain 16 bytes, no annotation
  }
  ```

  **Limitation.** MemoryPack's generator cannot see members created by another generator, so it assumes a `[StableId]`
  struct is unmanaged. If the id is declared in the *same assembly* as the `[MemoryPackable]` type that contains it,
  compilation fails (`CS8377`). Put shared ids in an assembly the DTOs reference (automatic for Foundation ids), or keep
  the DTO field a `string` and expose a typed property. Runtime ids have no such limit.

## Editor tooling

- **Catalog.** Tell the editor what the definitions are with an `IStableIdProvider` (found automatically) or, for
  ScriptableObject definitions, derive `ScriptableObjectStableIdProvider<TId, TAsset>`:

  ```csharp
  public sealed class ItemIdProvider : ScriptableObjectStableIdProvider<ItemId, ItemDefinition>
  {
      protected override void Describe(ItemDefinition asset, List<StableIdEntry> entries)
          => entries.Add(new StableIdEntry(asset.Id, asset.DisplayName, "Items", asset));
  }
  ```

- **Validation.** **Foundation → Typed IDs → Validate All IDs** checks, per id type: empty or malformed keys,
  **duplicate** keys, and every serialized field in ScriptableObjects and prefabs under `Assets/` that references an id
  no definition has (a renamed or deleted definition).
- The catalog and validator are the data behind an id picker. The package ships no picker UI, so a project that wants a
  searchable dropdown writes a `PropertyDrawer` over `StableIdCatalog`.

## Not included

Ids are not interned or hashed to integers (a lookup costs one string hash; hot paths such as audio resolve an id once
per request). Key changes have no automatic migration. The package provides no scene-object ids or networking support.
