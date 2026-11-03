# Changelog

## 0.8.0 - Initial release

First public release of Foundation: a reusable, game-agnostic application foundation for Unity.

- Composition and lifecycle: a container-agnostic DI port, feature installers, entry points, game contexts and loadable
  game modules.
- Core services: messaging (commands, queries, events), state machine, simulation, scheduling, requirements, rewards,
  feature flags, diagnostics, time and randomization.
- Data and content: segmented save, config groups, asset provider, localization and an editor content pipeline.
- Presentation: a backend-neutral UI router with uGUI, UI Toolkit and IMGUI backends, audio, pooling, feedbacks,
  badges, scripted sequences, tutorials, motion and strongly typed IDs.
- Optional adapters: VContainer, MemoryPack, MessagePack, SmartFormat, Addressables, YooAsset, LitMotion, HybridCLR,
  uLayout and mobile services (ads, IAP, identity, notifications, cloud save,
  analytics). UniTask is the only required third-party dependency.

See the [documentation](https://fzfking.github.io/foundation/) for details.
