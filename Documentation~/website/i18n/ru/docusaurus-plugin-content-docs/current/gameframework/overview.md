---
title: Обзор
sidebar_position: 1
---

# GameFramework

Необязательные модули GameFramework предоставляют интеграции с мобильными сервисами: рекламу, встроенные
покупки, идентификацию, уведомления, системные разрешения, проверку связи и состояния устройства, облачные
сохранения и аналитику. Они отделены от базовой `Foundation`, поэтому проект может использовать только нужные ему функции.

| Модуль | Что это | Страница |
| --- | --- | --- |
| Реклама | Медиация с упорядоченной по приоритету цепочкой запасных вариантов | [Реклама](ads) |
| Интеграции аналитики | AppMetrica, Firebase Analytics, Firebase Crashlytics | [Интеграции аналитики](analytics) |
| Облачные сохранения | Google Play Games Saved Games | [Облачные сохранения](cloud-save) |
| Связь | Опрос доступности сети и необязательная проверка состояния | [Платформенные сервисы](services#connectivity) |
| IAP | Контракт встроенных покупок | [Платформенные сервисы](services#in-app-purchases) |
| Идентификация | Анонимные идентификаторы пользователя и сессии | [Платформенные сервисы](services#identity) |
| Уведомления | Локальные уведомления с минимальным интервалом между отправками | [Платформенные сервисы](services#notifications) |
| Обмен данными | Буфер обмена, открытие URL, системное меню «Поделиться» | [Платформенные сервисы](services#sharing) |
| Системные разрешения | Запросы разрешений ОС | [Платформенные сервисы](services#system-permissions) |
| Диагностика устройства | Проверки хранилища и памяти | [Платформенные сервисы](services#device-diagnostics) |

## Включение модуля {#enabling-the-module}

Модуль включается вручную символом компиляции — `FOUNDATION_GAMEFRAMEWORK`. Ни один сторонний пакет не
сигнализирует «включи GameFramework», как Addressables для своего адаптера, поэтому вы выставляете его сами: в
**Project Settings → Player → Scripting Define Symbols** или командой **Foundation → Bootstrap New Project →
Enable GameFramework Module**.

Некоторым подмодулям сверх этого нужен собственный SDK. Они компилируются только при наличии и define, и
пакета, в противном случае исключаются из компиляции:

| Подмодуль | Также нужно | Define, выставляемый пакетом |
| --- | --- | --- |
| Analytics / AppMetrica | `io.appmetrica.analytics` | `FOUNDATION_APPMETRICA` |
| Analytics / Firebase | `com.google.firebase.app`, `.analytics` | `FOUNDATION_FIREBASE_ANALYTICS` |
| Crashlytics / Firebase | `com.google.firebase.crashlytics` | `FOUNDATION_FIREBASE_CRASHLYTICS` |
| Облачные сохранения | `com.google.play.games` (только Android и редактор) | `FOUNDATION_GOOGLE_PLAY_GAMES` |
| Notifications / Mobile | `com.unity.mobile.notifications` | `FOUNDATION_MOBILE_NOTIFICATIONS` |

Проверенные версии указаны в [таблице адаптеров](../adapters/overview#installing-a-dependency).

## По умолчанию ничего не подключено {#nothing-is-wired-by-default}

Установщики проекта регистрируют нужные сервисы. Для `IIapService` и `IPermissionService` пакет предоставляет
контракты, а реализацию поверх SDK магазина или платформы пишет проект. Для отдельных сервисов есть реализации
без действий, например `NullAdsProvider` и `NullSharingService`.

## Согласие {#consent}

Аналитика, отчёты о сбоях и рекламные SDK обычно должны ждать согласия игрока. Интеграции этого раздела не
решают: вы вызываете их методы активации или `SetEnabledAsync` из собственного шлюза согласия в выбранное вами
время.
