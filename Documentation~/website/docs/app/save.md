---
title: Save
sidebar_position: 2
---

# Save

A segmented save: each feature owns and serializes its own slice, and the orchestrator collects them into one blob. Treating each slice as private is an architectural convention; the API does not prevent one feature from accessing another feature's data.

| Type | Role |
| --- | --- |
| `IFeatureSaveModule` / `FeatureSaveModule<T>` | One feature's segment: a `SegmentId`, `Capture()` and `Restore(snapshot)`. |
| `SaveOrchestrator` | Collects all modules, writes and reads the blob. `SaveAsync`, `LoadAsync`. |
| `ISaveRepository` | Reads and writes a `SaveData`. `LocalFileSaveRepository`, `InMemorySaveRepository`, `CompositeSaveRepository`. |
| `ISaveSerializer` / `SaveSerializer` | The byte format, built on any `ISerializer` (for example the [MemoryPack adapter](../adapters/serialization)). |
| `ISaveMigration` | One version-to-version upgrade step. |
| `SaveSlot` | Every repository method takes a `slotId`; `SaveSlot.Default` is the single-save case. |
| `SaveLifecyclePump` | The `MonoBehaviour` that keeps the save current while the app runs. |

## A feature's segment

A segment is a snapshot type your serializer can handle plus a module that maps it to and from runtime state. Capture
and restore are synchronous and must not touch other features:

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Infrastructure.MemoryPack;
using Foundation.Logging;
using Foundation.Save;
using MemoryPack;

public sealed class Wallet
{
    public int Gold { get; set; }
}

[MemoryPackable]
public sealed partial class WalletSnapshot
{
    public int Gold { get; set; }
}

public sealed class WalletSaveModule : FeatureSaveModule<WalletSnapshot>
{
    private readonly Wallet _wallet;

    public WalletSaveModule(Wallet wallet) => _wallet = wallet;

    public override string SegmentId => "wallet";

    public override WalletSnapshot Capture() => new() { Gold = _wallet.Gold };

    // Restore replaces the state: nothing from before the load may survive in it.
    public override void Restore(WalletSnapshot snapshot) => _wallet.Gold = snapshot.Gold;
}
```

Rules for a good segment:

- **Restore replaces, it does not merge.** After `Restore` the feature must look exactly like the snapshot. Do not leave
  stale entries from the state that was there before.
- A segment missing from the save (a feature added after the save was written) is simply not restored, so the feature
  keeps its constructed defaults.
- `SegmentId` values must be unique; a duplicate is an error when the orchestrator is built.
- An exception in `Restore` fails the load and names the segment. The orchestrator attempts to restore changed modules to
  their pre-load state in reverse order. If a rollback call also throws, the resulting state is unknown.

## Wiring

An installer registers the feature's module, plus the persistence plumbing once (usually in the application scope,
with the orchestrator in each [game context](game-contexts)):

```csharp compile
public sealed class SaveInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.RegisterInstance<ISaveSerializer>(new SaveSerializer(new MemoryPackSerializerAdapter()));
        registry.RegisterInstance<ILocalSaveLocation>(new PersistentDataSaveLocation());
        registry.Register(
            resolver => new LocalFileSaveRepository(
                resolver.Resolve<ILocalSaveLocation>(),
                resolver.TryResolve<IAppLogger>(out var logger) ? logger : null),
            ServiceLifetime.Singleton).As<ISaveRepositoryBackend>();
        registry.Register(
            resolver => new CompositeSaveRepository(resolver.Resolve<IReadOnlyList<ISaveRepositoryBackend>>()),
            ServiceLifetime.Singleton).As<ISaveRepository>();
    }
}

public sealed class WalletInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<Wallet>(ServiceLifetime.Singleton);
        registry.Register<WalletSaveModule>(ServiceLifetime.Singleton).As<IFeatureSaveModule>();
    }
}

public sealed class SaveOrchestratorInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register(
            resolver => new SaveOrchestrator(
                resolver.Resolve<IReadOnlyList<IFeatureSaveModule>>(),
                resolver.Resolve<ISaveRepository>(),
                resolver.Resolve<ISaveSerializer>()),
            ServiceLifetime.Singleton);
    }
}
```

`IReadOnlyList<IFeatureSaveModule>` resolves to every module registered and visible from the scope.

## Saving and loading

```csharp compile
public sealed class SaveFlow
{
    private readonly SaveOrchestrator _save;

    public SaveFlow(SaveOrchestrator save) => _save = save;

    public async UniTask StartAsync(CancellationToken cancellationToken)
    {
        // False when no save exists yet; features keep their defaults.
        var hadSave = await _save.LoadAsync(cancellationToken);
        UnityEngine.Debug.Log(hadSave ? "Loaded." : "New game.");
    }

    public UniTask CheckpointAsync(CancellationToken cancellationToken) => _save.SaveAsync(cancellationToken);
}
```

What the orchestrator guarantees:

- **Saves are serialized.** Callers are uncoordinated (a checkpoint, a prestige reset, an ending, the boot load). A gate
  makes them run one at a time, and each captures its segments *when it runs*, so `await SaveAsync()` means your state
  is persisted.
- **A failed load blocks saving.** Once a load enters the gate, a failure or cancellation leaves its slot blocked, so
  `SaveAsync` refuses to overwrite it until a later load of that slot completes, including a result of "no save".
  Cancellation before entering the gate does not change the block. Loading another slot does not remove it.
  This prevents a corrupt-read from being replaced by a fresh empty save.
- **Saves are stamped.** `SavedAtUtcUnixMilliseconds` is never earlier than a save this instance loaded or wrote.
  It does not correct clock differences between devices; a backend choosing the newest timestamp can still pick the wrong copy.

## Versions and migrations

`SaveOrchestrator` takes the current save version and a list of `ISaveMigration`s. When a loaded save has an older version,
the orchestrator applies the chain `FromVersion → ToVersion` until it reaches the current one, and throws if there is no
path or a migration would exceed the supported version. A save from a **newer** version than the build supports is rejected.

```csharp compile
public sealed class WalletV1ToV2 : ISaveMigration
{
    public int FromVersion => 1;

    public int ToVersion => 2;

    public SaveData Migrate(SaveData data)
    {
        // Rewrite or rename segments here, then return data with the new version.
        return new SaveData(2, new Dictionary<string, byte[]>(data.Segments), data.SavedAtUtcUnixMilliseconds);
    }
}
```

## Repositories

| Repository | Use |
| --- | --- |
| `LocalFileSaveRepository` | A file under `Application.persistentDataPath` (via `ILocalSaveLocation`). Writes use a temporary file; replacing an existing save uses `File.Replace` and creates a `.bak` copy. On the first write, the temporary file is moved into place without a backup. A `.bak` is tried only when reading the primary file throws `InvalidDataException`; a successful recovery moves the invalid primary file aside as `.corrupt`. |
| `InMemorySaveRepository` | Tests, or a session that should not persist. |
| `CompositeSaveRepository` | Several backends at once (for example a local file and a cloud copy). Load takes the **newest** save by timestamp. If a backend failed to load and none returned a save, the load throws instead of reporting "no save". Save succeeds if at least one backend did. |

A backend whose `SaveAsync` completes successfully promises it persisted the data. A backend that can defer a write
implements `ISaveRepositoryBackendWithResult`: `SaveWithResultAsync` reports `Persisted` or `Skipped`.
`CompositeSaveRepository` succeeds only when at least one backend confirms persistence; skips do not hide a failed local
write. It throws when every backend fails or skips. A failure of the cloud alone does not fail a successful local write.

Recovery from `.bak` atomically restores the primary file before returning, keeping the backup and the damaged file
(`.corrupt`). A second load or process restart before the next autosave can read the recovered primary file.

A cloud backend implements `ISaveRepositoryBackend`; see [cloud save](../gameframework/cloud-save).

### Slots

Every repository method takes a `slotId` (default `SaveSlot.Default`). The default slot behaves exactly like a project with a
single save. `PersistentDataSaveLocation` writes the default slot to `save.dat` and any other slot to `save-<slotId>.dat`.
The optional `defaultFileName` constructor argument changes the default slot's name; it must be a file name, not a path.
[Game contexts](game-contexts) use slots as save namespaces.

## Keeping the save current: `SaveLifecyclePump`

`SaveLifecyclePump` is a component in the root scene that:

- saves every `periodicSaveSeconds` (30 by default);
- on pause or quit, synchronously invokes the supplied callback so the application can update active state, then attempts
  an asynchronous save. Android may freeze the process as soon as the callback returns, so the write may not finish;
- runs a resume hook when the app returns.

The active context attaches its orchestrator and callbacks with `Initialize(save, markActive, onResumed)` and removes them
with `DetachAsync`. `PrepareForDataResetAsync` stops the pump before a data reset so a pending save cannot resurrect the
wiped state.

`Initialize` resumes periodic and pause/quit persistence after a prior `DetachAsync` or `PrepareForDataResetAsync`.
A stale detach for another orchestrator does not stop the current one. To resume the same attachment after preparing a
reset without reinitializing it, call `ResumePeriodicPersistence()`.
