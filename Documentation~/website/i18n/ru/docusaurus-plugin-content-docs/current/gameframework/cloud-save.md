---
title: Облачные сохранения
sidebar_position: 4
---

# Облачные сохранения

Адаптер Google Play Games Saved Games подключается к [системе сохранений](../app/save) через интерфейс
`ISaveRepositoryBackend`. При загрузке `CompositeSaveRepository` выбирает копию с самой поздней меткой времени,
а при сохранении отправляет данные всем реализациям репозитория. После входа в аккаунт игрок может восстановить
доступную облачную копию обычной загрузкой. Сбой облачной записи не отменяет успешное сохранение на устройстве.

| Тип | Роль |
| --- | --- |
| `GooglePlayGamesSignIn` | Управляет входом в течение сессии: сначала запускает тихую проверку через `WarmUp()`, затем при необходимости запрашивает вход и хранит до восьми последних попыток входа за сессию. |
| `ICloudSavePreference`, `PlayerPrefsCloudSavePreference` | Хранит настройку облачных сохранений. Идентификатор настройки задаёт игра. |
| `GooglePlayGamesCloudSaveBackend` | Открывает, читает и фиксирует снимок; перед записью проверяет текущее состояние и применяет политику загрузки. Не возвращает сохранение, если Play Games недоступен или снимок пуст. |
| `CloudSaveUploadPolicy` | Ограничивает загрузки: отправка выполняется, только если содержимое изменилось и истёк минимальный интервал. |
| `GooglePlayGamesSnapshotPayload` | Внутреннее представление данных (`SaveData` ↔ `byte[]`), используемое адаптером для создания снимка. |

## Требования {#requirements}

Сборка подключается на Android и в редакторе при выполнении двух условий компиляции:

- определён `FOUNDATION_GAMEFRAMEWORK` (см. [обзор GameFramework](overview#enabling-the-module));
- установлен пакет `com.google.play.games` (он выставляет `FOUNDATION_GOOGLE_PLAY_GAMES`).

Для работы облака также включите Saved Games для приложения в Play Console. Это настройка сервиса, а не
условие компиляции. В редакторе адаптер доступен для композиции, но вход, чтение и запись в облако отключены.

## Подключение {#wiring}

Вход начинается до создания контейнеров. Поэтому создайте один экземпляр `GooglePlayGamesSignIn` в загрузочной
сцене, вызовите `WarmUp()`, а затем зарегистрируйте тот же экземпляр в контейнере. Облачное хранилище
регистрируется как ещё одна реализация `ISaveRepositoryBackend` рядом с локальным хранилищем.

```csharp compile
using System;
using Foundation.DI;
using Foundation.GameFramework.CloudSave;
using Foundation.Save;

public sealed class CloudSaveInstaller : IFeatureInstaller
{
    private readonly GooglePlayGamesSignIn _signIn;

    public CloudSaveInstaller(GooglePlayGamesSignIn signIn) => _signIn = signIn;

    public static GooglePlayGamesSignIn CreateSignIn()
        => new GooglePlayGamesSignIn(
            new PlayerPrefsCloudSavePreference("MyGame.CloudSave"),
            GooglePlayGamesSignIn.DefaultAuthenticationTimeout);

    public void Install(IServiceRegistry registry)
    {
        registry.RegisterInstance(_signIn);
        registry.Register(_ => new CloudSaveUploadPolicy(TimeSpan.FromSeconds(60)), ServiceLifetime.Singleton);
        registry.Register(
            resolver => new GooglePlayGamesCloudSaveBackend(
                "my-game",
                resolver.Resolve<GooglePlayGamesSignIn>(),
                resolver.Resolve<CloudSaveUploadPolicy>()),
            ServiceLifetime.Singleton).As<ISaveRepositoryBackend>();
    }
}
```

`CompositeSaveRepository` в вашем [установщике сохранений](../app/save) получает локальное и облачное
хранилища через `IEnumerable<ISaveRepositoryBackend>`.

Для расчёта интервала между загрузками и описания снимка используется значение поля
`SaveData.SavedAtUtcUnixMilliseconds`. Это метка сохранения, а не фактическое время загрузки в облако. Если игра задаёт
метке серверное время, адаптер использует его.

## Как дать игроку отключить облако {#letting-the-player-turn-it-off}

`GooglePlayGamesSignIn.SetEnabled(bool)` записывает переключатель через настройку, `IsPreferenceEnabled`
читает его, а `TryEnableAsync` запускает интерактивный запрос, когда игрок включает функцию.
`AuthenticationReported` и `AuthenticationHistory` сообщают о каждой попытке в виде
`GooglePlayGamesAuthReport` (этап, статус, длительность и диагностика устройства). Каждый отчёт записывается через
`IAppLogger`; дальнейшая отправка зависит от настройки логгера. Запись в журнале и `ToParameters()` могут включать
сведения об устройстве, SHA-1 сертификата подписи и сведения об исключении. Настройте логирование и передавайте только
необходимые поля в соответствии с политикой согласия проекта.

Защита «сначала прочитать, потом записывать» и история отправок независимы для каждого аккаунта и
снимка/слота. При ошибке повторного чтения состояние снимка считается неизвестным; чтение другого слота не
разрешает его перезапись. Сравнение содержимого учитывает версию сохранения. Первая запись слота разрешена
сразу; последующие изменения подчиняются его собственному минимальному интервалу.

Облачное хранилище сообщает о пропущенной записи через `ISaveRepositoryBackendWithResult`. Прямой вызов его
`SaveAsync` бросает исключение, если запись не подтверждена. Подключите его вместе с локальным хранилищем,
чтобы пропуск облачной записи не мешал сохранению на устройстве. Ошибки записи SDK передаются вызывающему коду. При отмене операция перестаёт ждать обратного вызова SDK. Уже отправленная операция записи может завершиться позже: отмена не
откатывает её.

Сборка адаптера также компилируется в редакторе при выбранной настольной целевой платформе.
