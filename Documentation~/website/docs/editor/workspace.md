---
title: Content Workspace
sidebar_position: 2
---

# Content Workspace

**Content → Content Workspace** is an Editor window for browsing and editing the sources of your game data and validating or
building them. It lives in the content Editor assembly (`Foundation.Content.Editor`), has no game-specific references, and
is built on Unity UI Toolkit. A project that wants a different editor UI can replace its presentation with
`ContentWorkspaceWindow.PresentationFactory`.

## It adapts to your project

The workspace calls `ConfigBuildEntryPoint.BuilderFactory().DiscoverSources()`, so it sees the same sources and registered
providers as your build pipeline (see [Config](../app/config#build-time-pipeline)). No separate content registry is needed.

For Unity authoring assets that implement `IConfigAuthoringSource` (the contract from `Foundation.Content.Authoring`):

- Top-level serialized arrays and lists become groups automatically, including empty collections.
- Other visible serialized fields, including inherited ones, appear in **Settings**.
- Direct record fields become table columns; nested data is editable in the details pane.
- A field named `Id` (case-insensitive, including an auto-property's backing field) labels records and supports best-effort
  navigation from validation errors. This is a convention, not a schema.
- Search matches all displayed values and requires all space-separated terms.
- **Add** constructs a default record, **Duplicate** makes a serialized copy, **Delete** removes a record. Collections of
  abstract or polymorphic elements can start with a null slot; choose a concrete type in the details editor. Concrete
  records need a parameterless constructor for Add.
- Duplicate preserves the original id. Assign a new one before validation or build.
- Edits use Unity serialization and Undo. **Save source** saves only the selected source. Builds consume in-memory
  authoring data but do not implicitly save authoring assets.

## File-backed sources

A source backed by a file implements `IContentWorkspaceFileSource`, which exposes canonical paths and formats independently
of exporters and generated binaries.

- Localization exposes its actual CSV as one group: key, context and every authored locale column are visible and editable.
- Tutorial sources expose each original `.txt` as a group with a full multiline script editor.
- **Open source file** opens the canonical file; **Reload file** rereads it, asking before discarding an edited draft.
- CSV saving quotes cells correctly, preserves embedded newlines and all authored columns, and writes UTF-8. Before saving,
  the workspace checks whether the source changed or was removed and blocks the save if it detects a difference. A write
  from another process between the final check and file replacement can still be overwritten.
- CSV drafts with parse errors or rows wider than the header show diagnostics and cannot be edited or saved. Fix the source
  file outside the workspace, then reload it.
- Drafts stay available while you switch sources and across Editor assembly reloads, with Unity Undo. Closing the workspace
  uses Unity's save-or-discard prompt for unsaved file drafts.
- **Validate** and **Build** require file drafts to be saved, because the pipeline reads the canonical files.

Other plain C# providers can implement the same interface to expose CSV or text source files. A provider without an
authoring view shows an explanation; exported `.bytes` files are never used as a source view.

## Validate and build

**Validate all** calls `ConfigBuilder.Validate`: export and validation, without binary serialization, file writes or
post-processors. Exporters and validators must therefore be free of side effects. **Build all** uses the normal
`ConfigBuilder.Build` flow.

A validation error locates a source and, when `Source` and `EntryId` identify one, the matching record. Errors without
record ids locate only the source. When names or ids are ambiguous, the first matching source or group is selected.

## Integrating a project

Keep your existing `BuilderFactory` composition, exporters, validators and post-processors. Adding a serialized collection to
an existing source needs no workspace registration. A new source needs `IConfigAuthoringSource` and the same exporter and
validator registration that `ConfigBuilder` requires.

## What it does not do

It adds no graph editing, balance simulation or reference inference. The workspace does not infer gameplay reference
semantics from arbitrary string fields; those need real project contracts rather than guesses about field names.
