---
title: Overview
sidebar_position: 1
---

# GameFramework

Optional modules for mobile and live-service concerns that most gameplay code does not need: ads, in-app purchases,
identity, notifications, sharing, OS permissions, connectivity, device diagnostics, cloud save, and analytics backends.
They are grouped apart from the base `Foundation` modules because a project can reasonably ship without any of them.

| Module | What it is | Page |
| --- | --- | --- |
| Ads | Mediation with a priority-ordered fallback chain | [Ads](ads) |
| Analytics backends | AppMetrica, Firebase Analytics, Firebase Crashlytics | [Analytics backends](analytics) |
| Cloud save | Google Play Games Saved Games as a save backend | [Cloud save](cloud-save) |
| Connectivity | Polled reachability plus an optional health check | [Platform services](services#connectivity) |
| IAP | In-app purchase contract | [Platform services](services#in-app-purchases) |
| Identity | Anonymous user and session ids | [Platform services](services#identity) |
| Notifications | Local notifications with a minimum-spacing queue | [Platform services](services#notifications) |
| Sharing | Clipboard, URL launching, native share sheet | [Platform services](services#sharing) |
| System permissions | OS permission requests | [Platform services](services#system-permissions) |
| Device diagnostics | Storage and memory checks | [Platform services](services#device-diagnostics) |

## Enabling the module

The whole module is gated behind one manual define, `FOUNDATION_GAMEFRAMEWORK`. No third-party package signals "enable
GameFramework" the way Addressables does for its adapter, so you set it yourself: in **Project Settings → Player →
Scripting Define Symbols**, or with **Foundation → Bootstrap New Project → Enable GameFramework Module**.

Several submodules need their own SDK on top of that. They compile only when both the define and the package are
present, and compile out cleanly otherwise:

| Submodule | Also needs | Define set by the package |
| --- | --- | --- |
| Analytics / AppMetrica | `io.appmetrica.analytics` | `FOUNDATION_APPMETRICA` |
| Analytics / Firebase | `com.google.firebase.app`, `.analytics` | `FOUNDATION_FIREBASE_ANALYTICS` |
| Crashlytics / Firebase | `com.google.firebase.crashlytics` | `FOUNDATION_FIREBASE_CRASHLYTICS` |
| Cloud save | `com.google.play.games` (Android and Editor only) | `FOUNDATION_GOOGLE_PLAY_GAMES` |
| Notifications / Mobile | `com.unity.mobile.notifications` | `FOUNDATION_MOBILE_NOTIFICATIONS` |

Versions that were tested are in the [adapter table](../adapters/overview#installing-a-dependency).

## Nothing is wired by default

The modules are available, not active, until your installers bind them. `IIapService` and `IPermissionService` provide
contracts that the project implements against its store or platform SDK. Some services also have no-op implementations,
such as `NullAdsProvider` and `NullSharingService`.

## Consent

Analytics, crash reporting and ad SDKs usually must wait for the player's consent. The backends here never decide that:
you call their activation or `SetEnabledAsync` methods from your own consent gate, at the time you choose.
