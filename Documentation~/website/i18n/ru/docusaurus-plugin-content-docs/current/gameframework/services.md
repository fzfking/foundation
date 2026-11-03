---
title: Платформенные сервисы
sidebar_position: 5
---

# Платформенные сервисы

GameFramework описывает несколько сервисов, работу которых выполняет платформа. Для большинства сервисов пакет предоставляет только контракт или нейтральную реализацию; проект подключает реализацию поверх SDK там, где это указано в таблице.

| Сервис | Контракт | Поставляется |
| --- | --- | --- |
| [Связь](#connectivity) | `IConnectivityService` | `UnityConnectivityService` |
| [Встроенные покупки](#in-app-purchases) | `IIapService` | только контракт |
| [Идентификация](#identity) | `IUserIdentityProvider` | `LocalUserIdentityProvider` |
| [Уведомления](#notifications) | `IPushNotificationService` | очередь интервалов; `MobileNotificationService` с `com.unity.mobile.notifications` |
| [Обмен данными](#sharing) | `IClipboardService`, `IUrlLauncher`, `ISharingService` | буфер обмена и открытие URL; `NullSharingService` |
| [Системные разрешения](#system-permissions) | `IPermissionService` | только контракт |
| [Диагностика устройства](#device-diagnostics) | `IDeviceDiagnostics` | `UnityDeviceDiagnostics` |

Всем им нужен `FOUNDATION_GAMEFRAMEWORK` ([как включить](overview#enabling-the-module)).

## Связь {#connectivity}

`IConnectivityService` сочетает локальную проверку с небольшими затратами через заданный интервал
(`Application.internetReachability`, её запускает [планировщик](../core/time-scheduling-random), а не каждый
кадр) с необязательной, более редкой настоящей HTTP-проверкой состояния по заданному вами URL, ожидающей ответ
200. Доступный сетевой интерфейс не доказывает доступ в интернет (например, при Wi-Fi без интернета или captive-портале).
При заданном URL HTTP-проверка определяет доступность только тогда, когда локальная проверка видит сетевой интерфейс;
если интерфейса нет, состояние сразу становится `Offline`. Пока не завершилась первая HTTP-проверка, состояние — `Unknown`.

`State` принимает значения `Unknown`, `Offline` или `Online`; `StateChanges` — это
`IReadOnlyReactiveValue<ConnectivityState>`, который при подписке сразу выдаёт текущее значение (см.
[Реактивные значения](../core/reactive-motion)).

`Dispose` останавливает опрос доступности и расписание HTTP-проверки состояния (если задан URL), отменяет
незавершённую проверку и отбрасывает её результат. После возврата из `Dispose` состояние больше не меняется, а
`StateChanges` ничего не публикует. Подписка на `StateChanges` после этого бросает `ObjectDisposedException`, в том
числе через сохранённую ранее ссылку, и переданный колбэк не вызывается. `Value` и `State` остаются доступными для
чтения и сохраняют последнее значение.

```csharp compile
using System;
using Foundation.GameFramework.Connectivity;
using Foundation.Scheduling;

public sealed class OfflineBanner : IDisposable
{
    private readonly UnityConnectivityService _connectivity;
    private readonly IDisposable _subscription;

    public OfflineBanner(IScheduler scheduler, Action<bool> setVisible)
    {
        _connectivity = new UnityConnectivityService(scheduler, healthCheckUrl: "https://api.example.com/health");
        _subscription = _connectivity.StateChanges.Subscribe(state => setVisible(state == ConnectivityState.Offline));
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _connectivity.Dispose();
    }
}
```

## Встроенные покупки {#in-app-purchases}

`IIapService` описывает каталог товаров, покупку, восстановление и отдельный шаг подтверждения (acknowledge);
он рассчитан и на API в стиле Google Play Billing, и на шлюзы с серверным посредником. Данные чека остаются
непрозрачной строкой в `PurchaseRecord.ReceiptData`, потому что их формат зависит от магазина, и Foundation
никогда его не разбирает. Проект реализует интерфейс поверх SDK своего магазина.

В сокращённом примере отмечено место, где проект должен проверить чек через магазин или сервер и идемпотентно выдать
покупку. Этот шаг нужно завершить до подтверждения покупки; его реализация зависит от проекта.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.GameFramework.Iap;

public sealed class Shop
{
    private readonly IIapService _iap;

    public Shop(IIapService iap) => _iap = iap;

    public async UniTask<bool> BuyAsync(string productId, CancellationToken cancellationToken)
    {
        var result = await _iap.PurchaseAsync(productId, cancellationToken);
        if (!result.Success)
            return false;

        // Проверьте чек через магазин или сервер и идемпотентно выдайте покупку.
        return await _iap.ConfirmPurchaseAsync(result.Purchase, cancellationToken);
    }

    public UniTask<IReadOnlyList<PurchaseRecord>> RestoreAsync(CancellationToken cancellationToken)
        => _iap.RestorePurchasesAsync(cancellationToken);
}
```

## Идентификация {#identity}

`IUserIdentityProvider` предоставляет `UserId` и `SessionId`, которые проект может использовать для сопоставления
событий. `LocalUserIdentityProvider` генерирует анонимный идентификатор пользователя через `IGuidGenerator` и
сохраняет его в `PlayerPrefs`. Новый идентификатор сессии создаётся при каждом создании провайдера, поэтому
зарегистрируйте один экземпляр на нужное время жизни сессии. Аналитика и отчёты об ошибках автоматически этот
интерфейс не используют. Проект может предоставить провайдер с серверной аутентификацией через тот же интерфейс.

## Уведомления {#notifications}

У `IPushNotificationService` есть `RequestAuthorizationAsync`, `ScheduleLocal(LocalNotification)` и
`CancelAll`. `MobileNotificationService` планирует уведомления через `NotificationSpacingQueue`: если запрошенное время срабатывания попадает
меньше `minimumSpacing` (по умолчанию 30 минут) от уже запланированного, время сдвигается вперёд, пока интервал не
будет соблюдён. Очередь не ищет более раннее свободное время. Это помогает не отправлять все уведомления разом. Сервис
из необязательной сборки `Notifications/Mobile` реализует контракт поверх `com.unity.mobile.notifications`;
другие реализации сами определяют правила планирования.

```csharp compile
using System;
using Foundation.GameFramework.Notifications;

public sealed class ComeBackReminder
{
    private readonly IPushNotificationService _notifications;

    public ComeBackReminder(IPushNotificationService notifications) => _notifications = notifications;

    public void Schedule()
    {
        _notifications.CancelAll();
        _notifications.ScheduleLocal(new LocalNotification
        {
            Id = "come_back",
            Title = "Your harvest is ready",
            Body = "Come back and collect it.",
            FireAtUtc = DateTime.UtcNow.AddHours(8),
        });
    }
}
```

## Обмен данными {#sharing}

- `IClipboardService`/`ClipboardService`: копирование и чтение текста, по умолчанию через
  `GUIUtility.systemCopyBuffer`.
- `IUrlLauncher`/`UnityUrlLauncher`: открытие разрешённого URL через `Application.OpenURL`. Токен проверяется перед
  запуском; после передачи ссылки системному обработчику отменить открытие нельзя.
- `ISharingService`/`NullSharingService`: контракт нативного меню «Поделиться» (`ShareAsync(SharePayload)`).
  Поставляется только пустая реализация, потому что настоящему меню нужен платформенный плагин, который
  выбираете вы.

## Системные разрешения {#system-permissions}

`IPermissionService` проверяет или запрашивает разрешение ОС по строковому идентификатору (`CheckAsync`,
`RequestAsync`; результат — `Granted`, `Denied` или `NotDetermined`). Он отвечает на вопрос «выдала ли ОС
приложению разрешение Y», а это другой вопрос, чем [требования](../core/requirements-rewards) («может ли игрок
сейчас сделать X»). Платформенную реализацию предоставляет проект.

## Диагностика устройства {#device-diagnostics}

`IDeviceDiagnostics` позволяет проекту предупредить игрока перед рискованной операцией, например загрузкой
большого обновления контента, с учётом ресурсов устройства. `UnityDeviceDiagnostics` сообщает
`TotalSystemMemoryMb`, `TryGetAvailableStorageMb` и `HasEnoughStorageFor`. Учитывайте ограничения платформ:
реализация сообщает общий объём системной памяти, а не объём, доступный приложению. Если свободное место не удалось измерить, `HasEnoughStorageFor` возвращает `true`;
это означает либо что места достаточно, либо что проверка недоступна. Используйте `TryGetAvailableStorageMb`, если
нужно различать эти случаи.

Проверка ресурсов устройства и [трассировка операций](../core/diagnostics#operation-tracing) — отдельные
механизмы.
