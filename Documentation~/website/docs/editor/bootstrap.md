---
title: Project bootstrap
sidebar_position: 1
---

# Project bootstrap

The editor menu under **Foundation → Bootstrap New Project** covers the part of setting up a project that is safe to run
*after* Unity has opened. These commands do not install dependencies; add the required packages before finishing setup.
Edit `Packages/manifest.json` by hand; the URLs are in the [adapter
table](../adapters/overview#installing-a-dependency).

The bootstrap editor assembly has no references to other Foundation assemblies or third-party libraries. Its commands
still require Unity to finish compilation and load the editor assembly. **Finish Setup** adds symbols for libraries it
finds; it does not remove previously enabled symbols when a library is missing.

| Menu item | What it does |
| --- | --- |
| **Generate Project Folders** | Creates a generic `Assets/GameContent/*` and `Assets/Scripts/*` folder skeleton through `AssetDatabase.CreateFolder`, so every folder gets a valid `.meta` at once. Idempotent: existing folders are skipped. Game-specific feature folders are not part of it. |
| **Enable GameFramework Module** | Sets `FOUNDATION_GAMEFRAMEWORK` for every build target group. See [GameFramework](../gameframework/overview). |
| **Finish Setup (Verify And Set Defines)** | Looks for the loaded `MemoryPack`, `MemoryPack.Core`, `MessagePack` and `SmartFormat` assemblies and sets `FOUNDATION_MEMORYPACK`, `FOUNDATION_MESSAGEPACK` and `FOUNDATION_SMARTFORMAT` accordingly. Safe to re-run after each NuGet restore. |

## Why some defines are manual

An adapter for a UPM package switches on by itself: its asmdef lists the package in `versionDefines`. A library
delivered as a NuGet DLL has no package to detect, so its define is set by **Finish Setup** once the DLLs are present.
