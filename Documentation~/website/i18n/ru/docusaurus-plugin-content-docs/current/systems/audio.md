---
title: Аудио
sidebar_position: 1
---

# Аудио

Игровой код запрашивает звук по `AudioEventId`. Аудиодвижок выбирает клип, громкость, высоту тона и канал,
затем проверяет ограничения: интервал между воспроизведениями, число одновременных воспроизведений и настройки отключения звука.

| Часть | Роль |
| --- | --- |
| `IAudioService` | То, что вызывает игра: `Play`, `TryPlay`, `PlayMusic`, `CrossFadeMusic`, `Stop`, `StopChannel`, `StopMusic`, `PreloadAsync`. |
| `AudioEventDefinition` | Один звук: канал, клипы, режим выбора, диапазоны громкости и высоты, кулдаун, лимит одновременных воспроизведений, приоритет, зацикленность, пространственные настройки. |
| `AudioEventLibrary` | `ScriptableObject` с определениями. |
| `AudioChannelDefinition` | Канал (UI, музыка, окружение, ...). Проекты определяют свои; пакет их не поставляет. |
| `AudioDuckingRule` | «Пока играет канал-триггер, приглушать целевой канал». |
| `IAudioSettings` | Громкость для всей игры, отдельных каналов и групп вывода, а также выключение звука отдельных каналов. Только в памяти; сохраняйте через [сохранения](../app/save). |
| `IAudioMixerRouting` | Сопоставляет канал с `AudioMixerGroup`. |

Воспроизведение использует компоненты `AudioSource` из пула, поэтому установите `PoolingInstaller` (см.
[Пулы](pooling)) **до** `AudioInstaller`.

## Настройка {#setup}

Каналы — простые определения. `AudioInstaller` регистрирует движок, настройки, кэш клипов и `IAudioService` и
обновляет движок через точку входа Unity.

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

`AudioInstaller` также принимает необязательные `AudioEngineSettings`, `IAudioMixerRouting` (ассет
`AudioMixerRouting`, созданный через **Create → Foundation → Audio → Mixer Routing**) и задержку
`clipRetryDelaySeconds` для [повторов загрузки клипов](#clip-load-retries).

## Определение звуков {#defining-sounds}

Определение сериализуемо, поэтому обычно создаётся в ассете `AudioEventLibrary` (**Create → Foundation → Audio
→ Event Library**). Тот же тип можно построить в коде:

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

Запись клипа содержит либо `AudioClip`, либо идентификатор ассета, разрешаемый через [провайдер
ассетов](../app/content), так что клипы можно доставлять удалённо.

| `AudioSelectionMode` | Выбирает |
| --- | --- |
| `Single` | первый клип |
| `Random` | любой клип |
| `RandomNoImmediateRepeat` | любой клип, кроме проигранного последним |
| `WeightedRandom` | по весу каждой записи |

Когда у определения есть лимит `MaxPlaybacks`, `AudioPlaybackOverflowPolicy` говорит, что делает новое
воспроизведение на пределе: `IgnoreNew` отбрасывает его, `StopOldest` заменяет самое старое воспроизведение.

## Регистрация библиотеки и предзагрузка {#registering-the-library-and-preloading}

Реестр ничего не знает, пока вы не дадите ему библиотеку. Сделайте это, когда контент доступен, а затем
предзагрузите клипы, чтобы первое воспроизведение не подвисало:

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

`PreloadAsync` ждёт загрузки связанных клипов. Если клип не загрузился, его исключение передаётся вызывающему коду;
отмена тоже передаётся. Так запуск может остановиться, а не продолжиться без нужного звука. Запросы воспроизведения
по-прежнему работают в режиме best-effort.

## Повторы загрузки клипов {#clip-load-retries}

Автоматический повтор по умолчанию выключен. Чтобы включить его, передайте положительную задержку:

```csharp
new AudioInstaller(channels, ducking, clipRetryDelaySeconds: 5d).Install(registry);
```

`AudioInstaller` передаёт значение в `AudioClipCache` как `retryDelaySeconds`. Неудачная загрузка ассета записывается
предупреждением в лог, а клип остаётся недоступным. Если задержка задана, следующий запрос воспроизведения не раньше
чем через указанное число секунд после ошибки запускает новую загрузку. Этот запрос всё равно отклоняется с
`ClipNotLoaded`; клип зазвучит, когда его найдёт один из следующих запросов. Запросы, пришедшие во время загрузки,
используют её же, поэтому медленный повтор не создаёт дубликатов.

Число попыток не ограничено, нарастающей задержки нет: каждая ошибка снова ждёт ту же задержку, пока воспроизведение
продолжает запрашивать клип. `PreloadAsync` не учитывает задержку и повторяет неудачные клипы сразу, поэтому это способ
восстановиться там, где пропущенный звук недопустим. При задержке по умолчанию, равной нулю, неудачный клип повторяет
только `PreloadAsync`. Эти правила касаются клипов, заданных идентификатором ассета; прямые ссылки на `AudioClip` идут
мимо кэша.

## Воспроизведение {#playing}

`Play` возвращает `AudioHandle`. Храните его, только если позже нужно остановить, плавно приглушить или
перенастроить воспроизведение. `TryPlay` возвращает `AudioPlayResult`, который говорит, почему звук не начался
(`Cooldown`, `EventPlaybackLimit`, `ChannelFull`, `Silenced`, ...).

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

`AudioPlayOptions` содержит коэффициенты громкости и высоты тона и, для пространственных звуков, либо мировую `Position`
(`AudioPlayOptions.At`), либо `Transform`, за которым следовать (`AudioPlayOptions.Following`).

## Настройки {#settings}

`IAudioSettings` — модель громкости для игрока. Привяжите к ней экран настроек и сохраняйте значения сами:

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

`Changed` срабатывает после любого изменения — это точка для сохранения.

## Приглушение (ducking) {#ducking}

`AudioDuckingRule(trigger, target, targetGain, attackSeconds, releaseSeconds)` плавно снижает громкость целевого
канала до `targetGain` за время атаки, пока на канале-триггере играет хотя бы один звук. После остановки всех
звуков исходная громкость возвращается за время отпускания. Канал не может приглушать сам себя.

## Инструменты редактора {#editor-tooling}

`AudioEventValidator` сообщает о проблемах в библиотеке (отсутствующий канал, нет клипов, неверные диапазоны,
дубликаты идентификаторов), а `AudioPreviewPlayer` проигрывает определение из редактора. Оба находятся в
сборке редактора и не требуют настройки.
