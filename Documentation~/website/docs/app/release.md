---
title: Release controller
sidebar_position: 7
---

# Release controller

Client version policy, release selection per environment and a remote release configuration. The package ships the
contracts and the default logic; a project supplies its URLs, environment ids, store link and the built-in fallback
configuration.

It answers two questions at startup:

1. **Does this client need to update?** `None`, `Recommended` or `Required`.
2. **Which content release may this client use?** `Current`, `Previous` (for clients that have not updated yet),
   `Incompatible`, or `Unknown` when the configuration does not describe the environment or publishes no release in
   it. For a usable release, where it is served from.

| Type | Role |
| --- | --- |
| `ClientVersion` | One to three numeric parts separated by dots; missing trailing parts count as zero. Parts are compared as numbers. |
| `ReleaseConfigProvider` | Loads the configuration from the server, then the last successfully saved cache, then the built-in fallback. A cache write can fail without failing the remote load. |
| `IReleaseManager` | `Select(environmentId, clientVersion, config)` returns a `ReleaseSelection`. |
| `IClientUpdateService` | `Evaluate(...)` returns a `ClientUpdateStatus`; `StoreUrl(config)` is where to send the player. |
| `IContentEndpointResolver` | Returns the selected release's content folder. The Addressables adapter uses this path when it loads content. |

## The server configuration

```json
{
  "schema": 1,
  "client": { "minimum": "1.2.0", "recommended": "1.4.0", "storeUrl": "" },
  "environments": [
    {
      "id": "production",
      "current": { "release": "2026-03-10", "minClient": "1.4.0", "path": "Prod" },
      "previous": { "release": "2026-02-20", "minClient": "1.2.0", "path": "Prod-prev" }
    }
  ]
}
```

- `client.minimum`: a client below it must update. `client.recommended`: a client below it is nudged.
- Each environment has a `current` slot and an optional `previous` one. A slot is published when its `release` is
  non-empty. A release lists the lowest client that can use it (`minClient`) and a relative folder path under the
  configured content root (`path`). Leading and trailing slashes are trimmed. Empty internal segments, dot-only
  segments, backslashes and percent signs are rejected; other characters are escaped as URL path data.

## Checking at startup

```csharp compile
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Release;
using UnityEngine;

public sealed class ReleaseCheck
{
    public async UniTask<ReleaseDecision> CheckAsync(CancellationToken cancellationToken)
    {
        var provider = new ReleaseConfigProvider(
            "https://example.com/release.json",
            new UnityWebRequestReleaseConfigSource(timeoutSeconds: 10),
            new FileReleaseConfigCache(Path.Combine(Application.persistentDataPath, "release.json")),
            BuiltInConfig());

        var loaded = await provider.LoadAsync(cancellationToken);
        if (!ClientVersion.TryParse(Application.version, out var version))
            return new ReleaseDecision(ClientUpdateStatus.None, null);

        var selection = new ReleaseManager().Select("production", version, loaded.Config);
        var status = new ClientUpdateService("https://example.com/store").Evaluate(version, loaded.Config, selection);
        var contentFolder = selection.CanServe
            ? new ContentEndpointResolver("https://cdn.example.com/content").Resolve(selection)
            : null;

        return new ReleaseDecision(status, contentFolder);
    }

    private static ReleaseConfig BuiltInConfig() => new()
    {
        schema = ReleaseConfig.SupportedSchema,
        client = new ClientPolicy { minimum = "1.0.0", recommended = "1.0.0", storeUrl = string.Empty },
        environments = new[]
        {
            new ReleaseEnvironment
            {
                id = "production",
                current = new ReleaseSlot { release = "built-in", minClient = "1.0.0", path = "Prod" },
            },
        },
    };
}

public readonly struct ReleaseDecision
{
    public ReleaseDecision(ClientUpdateStatus status, string contentFolder)
    {
        Status = status;
        ContentFolder = contentFolder;
    }

    public ClientUpdateStatus Status { get; }

    public string ContentFolder { get; }
}
```

With the Addressables adapter, `AddressablesReleaseGate` wraps this: it selects the release, routes Addressables to its
content folder (`AddressablesEndpointRouting`) and tells the boot step whether the client must update first. It plugs into
`ContentStartupBootStep` as the `afterInitialize` gate ([Application composition](../concepts/app-composition)).

## Rules the controller keeps

- **A broken source does not block the player.** Fetch failures fall back to another source; cancellation is propagated. A configuration that cannot be read, or whose
  `schema` is not `ReleaseConfig.SupportedSchema`, falls through to the next source (cache, then built-in).
- **An unreadable version is no rule.** A typo on the server must never lock players out of the game.
- **`previous` stays on the server** until every client that needs it has updated from the store. Only then may its content
  be deleted. One previous release is enough: content is not kept per client version.
- **Serve the configuration and the content over HTTPS.** The release configuration decides which folder content is
  downloaded from. A project can configure signature verification for hot-update assemblies and supplemental AOT metadata;
  configs, localization and art are not signed automatically. Over plain HTTP a man in the middle could change what the
  client downloads.
- **Promotion is a server operation:** publish the release's content into the environment folder and update the
  configuration. Nothing is rebuilt on the client.
