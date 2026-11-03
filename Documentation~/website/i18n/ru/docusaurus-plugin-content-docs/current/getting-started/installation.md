---
title: Установка
sidebar_position: 1
---

# Установка

Требуется Unity 6000.5 или новее.

## 1. Добавьте UniTask {#1-add-unitask}

UniTask — единственная обязательная сторонняя зависимость: без неё Foundation не компилируется, поэтому добавьте её первой.
Манифест UPM-пакета Foundation не может объявлять UniTask как Git-зависимость, поэтому добавьте UniTask отдельно в
манифест проекта Unity `Packages/manifest.json` по этому Git-адресу:

```text
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
```

## 2. Добавьте пакет {#2-add-the-package}

Package Manager → **Add package from git URL**:

```text
https://github.com/fzfking/foundation.git#0.8.0
```

или в `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.fzfking.foundation": "https://github.com/fzfking/foundation.git#0.8.0"
  }
}
```

Суффикс `#0.8.0` закрепляет версию по тегу релиза; без него Unity возьмёт последний коммит ветки `main`. uGUI (`com.unity.ugui`) Unity Package Manager установит как зависимость Foundation.

## 3. Добавьте нужные адаптеры {#3-add-the-adapters-you-want}

Манифест Foundation не может объявлять Git-зависимости на другие UPM-пакеты, поэтому их устанавливает сам проект.
Добавьте нужные UPM-библиотеки в `Packages/manifest.json`, а NuGet-библиотеки установите через NuGetForUnity.
URL-адреса и проверенные версии указаны в [таблице адаптеров](../adapters/overview#installing-a-dependency).

Типичный набор для мобильной игры:

| Что нужно | Что установить |
| --- | --- |
| DI-контейнер | VContainer |
| Сохранения и файлы конфигов | MemoryPack |
| Удалённый контент | Addressables (или YooAsset) |

## 4. Завершите настройку {#4-finish-the-setup}

Некоторые зависимости устанавливаются через NuGet и попадают в проект в виде DLL. Unity не распознаёт их как
UPM-пакеты, поэтому после установки нужно включить соответствующие символы компиляции:

**Foundation → Bootstrap New Project → Finish Setup (Verify And Set Defines)**

Команда выставит `FOUNDATION_MEMORYPACK`, `FOUNDATION_MESSAGEPACK` и
`FOUNDATION_SMARTFORMAT` для найденных библиотек. Адаптеры на UPM (VContainer, Addressables,
YooAsset, HybridCLR, uLayout) включаются сами.

Остальные пункты меню описаны в разделе [Начальная настройка проекта](../editor/bootstrap).

Пространства имён и имена сборок имеют общий корень: `Foundation` (ядро), `Foundation.UI` (+ `.UGui`,
`.Toolkit`, `.Imgui`), `Foundation.Audio`, `Foundation.Infrastructure.<Library>`,
`Foundation.GameFramework.*`, `Foundation.Content.*`.
