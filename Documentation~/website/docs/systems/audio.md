---
title: Audio
sidebar_position: 1
---

# Audio

Gameplay asks for a sound by id (`AudioEventId`). The audio engine decides whether it plays: which clip, at what volume
and pitch, on which channel, and whether the cooldown, the playback limit or the mute setting says no.

| Piece | Role |
| --- | --- |
| `IAudioService` | What gameplay calls: `Play`, `TryPlay`, `PlayMusic`, `CrossFadeMusic`, `Stop`, `StopChannel`, `StopMusic`, `PreloadAsync`. |
| `AudioEventDefinition` | One sound: its channel, clips, selection mode, volume and pitch ranges, cooldown, playback limit, priority, loop, spatial settings. |
| `AudioEventLibrary` | A `ScriptableObject` holding the definitions. |
| `AudioChannelDefinition` | A channel (UI, music, ambient, ...). Projects define their own; the package ships none. |
| `AudioDuckingRule` | "While the trigger channel plays, lower the target channel." |
| `IAudioSettings` | Master, per-channel and per-output-group volume, plus per-channel mute. In memory only; persist it with the [save](../app/save). |
| `IAudioMixerRouting` | Maps a channel to an `AudioMixerGroup`. |

Playback uses pooled `AudioSource` components, so install `PoolingInstaller` (see [Pooling](pooling)) **before**
`AudioInstaller`.

## Setup

Channels are plain definitions. `AudioInstaller` registers the engine, the settings, the clip cache and `IAudioService`,
and ticks the engine from a Unity entry point.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Audio;
using Foundation.DI;
using Foundation.Lifecycle;
using UnityEngine;

public static class GameAudioChannels
{
    public static readonly AudioChannelId Ui = AudioChannelId.From("ui");
    public static readonly AudioChannelId Music = AudioChannelId.From("music");
    public static readonly AudioChannelId Voice = AudioChannelId.From("voice");
}

public static class GameAudioEvents
{
    public static readonly AudioEventId ButtonClick = AudioEventId.From("ui.button_click");
    public static readonly AudioEventId MenuTheme = AudioEventId.From("music.menu");
    public static readonly AudioEventId Narration = AudioEventId.From("voice.narration");
}

public sealed class GameAudioInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        var channels = new IAudioChannelDefinition[]
        {
            new AudioChannelDefinition(GameAudioChannels.Ui.Value, maxOneShotCount: 12),
            new AudioChannelDefinition(GameAudioChannels.Music.Value, loop: true),
            new AudioChannelDefinition(GameAudioChannels.Voice.Value),
        };

        var ducking = new[]
        {
            new AudioDuckingRule(GameAudioChannels.Voice, GameAudioChannels.Music, targetGain: 0.4f),
        };

        new AudioInstaller(channels, ducking).Install(registry);
    }
}
```

`AudioInstaller` also takes optional `AudioEngineSettings`, an `IAudioMixerRouting` (an `AudioMixerRouting` asset
created from **Create → Foundation → Audio → Mixer Routing**) and `clipRetryDelaySeconds` for [clip load
retries](#clip-load-retries).

## Defining sounds

A definition is serializable, so authoring normally happens in an `AudioEventLibrary` asset
(**Create → Foundation → Audio → Event Library**). The same type can be built in code:

```csharp compile
public static class GameAudioContent
{
    public static AudioEventLibrary CreateLibrary(
        AudioClip click,
        AudioClip[] clickVariants,
        AudioClip menuTheme,
        AudioClip narration)
    {
        var variants = new AudioClipEntry[clickVariants.Length + 1];
        variants[0] = new AudioClipEntry(click);
        for (var index = 0; index < clickVariants.Length; index++)
            variants[index + 1] = new AudioClipEntry(clickVariants[index]);

        return AudioEventLibrary.Create(new[]
        {
            new AudioEventDefinition(
                GameAudioEvents.ButtonClick,
                GameAudioChannels.Ui,
                variants,
                selection: AudioSelectionMode.RandomNoImmediateRepeat,
                pitchMin: 0.95f,
                pitchMax: 1.05f,
                cooldownSeconds: 0.05f,
                maxPlaybacks: 4),
            new AudioEventDefinition(
                GameAudioEvents.MenuTheme,
                GameAudioChannels.Music,
                new[] { new AudioClipEntry(menuTheme) },
                loop: true),
            new AudioEventDefinition(
                GameAudioEvents.Narration,
                GameAudioChannels.Voice,
                new[] { new AudioClipEntry(narration) }),
        });
    }
}
```

A clip entry holds either an `AudioClip` or an asset id resolved through the [asset provider](../app/content), so clips
can be delivered remotely.

| `AudioSelectionMode` | Picks |
| --- | --- |
| `Single` | the first clip |
| `Random` | any clip |
| `RandomNoImmediateRepeat` | any clip except the one played last |
| `WeightedRandom` | by each entry's weight |

When a definition has a `MaxPlaybacks` limit, `AudioPlaybackOverflowPolicy` says what a new play does at the limit:
`IgnoreNew` drops it, `StopOldest` replaces the oldest playback.

## Registering the library and preloading

The registry knows nothing until you give it the library. Do it once content is available, then preload the clips so the
first play does not stall:

```csharp compile
public sealed class AudioContentLoader
{
    private readonly AudioEventLibrary _library;
    private readonly AudioEventRegistry _registry;
    private readonly IAudioService _audio;

    public AudioContentLoader(AudioEventLibrary library, AudioEventRegistry registry, IAudioService audio)
    {
        _library = library;
        _registry = registry;
        _audio = audio;
    }

    public async UniTask LoadAsync(CancellationToken cancellationToken)
    {
        _registry.Register(_library);

        var ids = new List<AudioEventId>();
        foreach (var definition in _library.Events)
            ids.Add(definition.Id);

        await _audio.PreloadAsync(ids, cancellationToken);
    }

    public void Unload() => _registry.Unregister(_library);
}
```

`PreloadAsync` waits for the referenced clips to load. If a clip fails to load, its exception is propagated; cancellation is
propagated as well. This lets startup stop instead of continuing with missing audio. Playback requests remain best-effort.

## Clip load retries

Automatic retry is off by default. Pass a positive delay to turn it on:

```csharp
new AudioInstaller(channels, ducking, clipRetryDelaySeconds: 5d).Install(registry);
```

`AudioInstaller` forwards the value to `AudioClipCache` as `retryDelaySeconds`. A failed asset load is logged as a
warning and the clip stays unavailable. With a delay set, the next playback request at least that many seconds after
the failure starts another load. That request is still rejected with `ClipNotLoaded`; the clip plays once a later
request finds it loaded. Requests that arrive while a load is running share it, so a slow retry does not start
duplicates.

Retries have no attempt limit and no backoff: each failure waits the same delay again for as long as playback keeps
asking. `PreloadAsync` ignores the delay and retries failed clips immediately, which makes it the way to recover where
a missing sound is not acceptable. With the default zero delay, only `PreloadAsync` retries a failed clip. These rules
apply to clips referenced by asset id; direct `AudioClip` references bypass the cache.

## Playing

`Play` returns an `AudioHandle`. Keep it only if you need to stop, fade or retune the playback later. `TryPlay` returns an
`AudioPlayResult` that says why a sound did not start (`Cooldown`, `EventPlaybackLimit`, `ChannelFull`, `Silenced`, ...).

```csharp compile
public sealed class AudioExamples
{
    private readonly IAudioService _audio;

    public AudioExamples(IAudioService audio) => _audio = audio;

    public void Click() => _audio.Play(GameAudioEvents.ButtonClick);

    public void Explosion(Vector3 position)
        => _audio.Play(GameAudioEvents.ButtonClick, AudioPlayOptions.At(position).WithVolume(0.8f));

    public bool TryNarrate(out AudioHandle handle)
        => _audio.TryPlay(GameAudioEvents.Narration, default, out handle) == AudioPlayResult.Started;

    public void StartMenuMusic() => _audio.PlayMusic(GameAudioEvents.MenuTheme, fadeInSeconds: 1f);

    public void SwitchMusic(AudioEventId next) => _audio.CrossFadeMusic(next, durationSeconds: 2f);

    public void StopMusic() => _audio.StopMusic(GameAudioChannels.Music, fadeOutSeconds: 0.5f);
}
```

`AudioPlayOptions` carries a volume and pitch scale and, for spatial sounds, either a world `Position`
(`AudioPlayOptions.At`) or a `Transform` to follow (`AudioPlayOptions.Following`).

## Settings

`IAudioSettings` is the player-facing volume model. Bind a settings screen to it and persist the values yourself:

```csharp compile
public sealed class AudioSettingsPresenter
{
    private readonly IAudioSettings _settings;

    public AudioSettingsPresenter(IAudioSettings settings) => _settings = settings;

    public void SetMaster(float volume) => _settings.SetMasterVolume(volume);

    public void SetMusic(float volume) => _settings.SetChannelVolume(GameAudioChannels.Music.Value, volume);

    public void MuteUi(bool muted) => _settings.SetChannelMuted(GameAudioChannels.Ui.Value, muted);
}
```

`Changed` fires after any change, which is the hook for saving.

## Ducking

An `AudioDuckingRule(trigger, target, targetGain, attackSeconds, releaseSeconds)` lowers the target channel to
`targetGain` while any sound plays on the trigger channel, easing in over the attack time and out over the release time.
A channel cannot duck itself.

## Editor tooling

`AudioEventValidator` reports problems in a library (missing channel, no clips, bad ranges, duplicate ids) and
`AudioPreviewPlayer` plays a definition from the Editor. Both live in the Editor assembly and need no setup.
