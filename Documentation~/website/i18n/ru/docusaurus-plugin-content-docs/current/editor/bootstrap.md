---
title: Настройка проекта
sidebar_position: 1
---

# Настройка проекта

Пункт меню **Foundation → Bootstrap New Project** охватывает ту часть настройки проекта, которую безопасно
выполнять *после* того, как Unity открылся. Команды не устанавливают зависимости: добавьте нужные пакеты до
завершающей настройки. Правьте `Packages/manifest.json` вручную; адреса есть в [таблице
адаптеров](../adapters/overview#installing-a-dependency).

Сборка редактора для первичной настройки не ссылается на другие сборки Foundation или сторонние библиотеки.
Чтобы её команды стали доступны, Unity должна завершить компиляцию и загрузить сборку. **Finish Setup**
добавляет символы для найденных библиотек, но не удаляет ранее включённые символы, если библиотека больше не
обнаруживается.

| Пункт меню | Что делает |
| --- | --- |
| **Generate Project Folders** | Создаёт типовой набор папок `Assets/GameContent/*` и `Assets/Scripts/*` через `AssetDatabase.CreateFolder`, так что у каждой папки сразу есть корректный `.meta`. Идемпотентен: существующие папки пропускаются. Папки игровых подсистем в набор не входят. |
| **Enable GameFramework Module** | Выставляет `FOUNDATION_GAMEFRAMEWORK` для всех групп целевых платформ. См. [GameFramework](../gameframework/overview). |
| **Finish Setup (Verify And Set Defines)** | Ищет загруженные сборки `MemoryPack`, `MemoryPack.Core`, `MessagePack` и `SmartFormat` и соответственно выставляет `FOUNDATION_MEMORYPACK`, `FOUNDATION_MESSAGEPACK` и `FOUNDATION_SMARTFORMAT`. Безопасно запускать повторно после каждого восстановления NuGet. |

## Почему часть символов задаётся вручную {#why-some-defines-are-manual}

Адаптер для UPM-пакета включается сам: в его asmdef пакет указан в `versionDefines`. У библиотеки, устанавливаемой
через NuGet и попадающей в проект в виде DLL, нет UPM-пакета для обнаружения. Поэтому **Finish Setup** выставляет
соответствующий символ компиляции (`define`) после загрузки DLL.
