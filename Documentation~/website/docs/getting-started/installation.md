---
title: Installation
sidebar_position: 1
---

# Installation

Requires Unity 6000.5 or newer.

## 1. Add UniTask

UniTask is the only mandatory third-party dependency; Foundation does not compile without it, so add it first. Foundation's
package manifest cannot declare UniTask as a Git dependency, so add UniTask separately to the Unity project through
`Packages/manifest.json` using this Git URL:

```text
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
```

## 2. Add the package

Package Manager → **Add package from git URL**:

```text
https://github.com/fzfking/foundation.git#0.8.0
```

or in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.fzfking.foundation": "https://github.com/fzfking/foundation.git#0.8.0"
  }
}
```

The `#0.8.0` suffix pins the release tag; without it Unity takes the latest commit of `main`. Unity Package Manager
installs the uGUI package (`com.unity.ugui`) as a dependency of Foundation.

## 3. Add the adapters you want

Foundation's package manifest cannot declare Git dependencies on UPM libraries, so the project manages their installation.
Add the UPM libraries you use to `Packages/manifest.json`; install NuGet libraries through NuGetForUnity. The URLs and
tested versions are in the [adapter table](../adapters/overview#installing-a-dependency).

A typical set for a mobile game:

| You need | Install |
| --- | --- |
| A DI container | VContainer |
| Saved data and config files | MemoryPack |
| Remote content | Addressables (or YooAsset) |

## 4. Finish the setup

Some libraries are delivered as NuGet DLLs, which Unity cannot detect as packages. Their adapters are switched on by a
scripting define. After the DLLs are in the project, run:

**Foundation → Bootstrap New Project → Finish Setup (Verify And Set Defines)**

It sets `FOUNDATION_MEMORYPACK`, `FOUNDATION_MESSAGEPACK` and `FOUNDATION_SMARTFORMAT` for the
libraries it finds. The UPM-based adapters (VContainer, Addressables, YooAsset, HybridCLR, uLayout)
switch on by themselves.

See [Project bootstrap](../editor/bootstrap) for the other menu items.

Namespaces and assembly names share one root: `Foundation` (core), `Foundation.UI` (+ `.UGui`, `.Toolkit`, `.Imgui`),
`Foundation.Audio`, `Foundation.Infrastructure.<Library>`, `Foundation.GameFramework.*`, `Foundation.Content.*`.
