# Contributing / Участие в разработке

## English

Foundation is a Unity package. Its `package.json` currently declares Unity 6000.5 as the minimum version.

### Before opening an issue

Include the Unity and Foundation versions, the adapters and their versions involved, the expected and actual behavior,
and clear reproduction steps. Add relevant logs or a small sample when they help explain the problem; remove secrets and
personal data.

### Changes and pull requests

- Keep each change focused and follow the conventions used by nearby code.
- For user-visible changes, update the English and Russian documentation together.
- Add or update tests for behavior changes. Keep Unity `.meta` files and GUIDs stable, and include a sidecar `.meta` file
  for new Unity assets.
- When changing the TypedIds generator, update its source under `Generators~` and the packaged analyzer under
  `Analyzers`. Rebuild it with `dotnet build -c Release` from `Generators~/TypedIds`, then copy the resulting DLL to
  `Analyzers/`, preserving its `.meta` file.
- In the pull request description, explain the behavior changed and list the checks you ran. If a relevant check could not
  be run, say so.

### Adapter changes

- Add an adapter assembly under `Runtime/<Library>/` with an asmdef that references `Foundation` and the library. Use
  `versionDefines` and `defineConstraints` to enable it when the dependency is present.
- Put adapter tests under `Tests/<Library>/` and gate them with the same define.
- For a DI adapter, use `MinimalServiceScope` as a limited test implementation and the VContainer tests as a reference
  for checking exposure and `As`, child-scope shadowing and ownership, factories with `TryResolve`, build and dispose
  callbacks, collection parameters, entry points, and the game-object injector.

### Validation

Core EditMode tests use `MinimalServiceScope`; integration and PlayMode tests exercise optional adapters and Unity
behavior. For Unity code, add the package to `testables` in a Unity project's `Packages/manifest.json` and run the relevant
EditMode and PlayMode tests. Adapter tests require their dependencies and compilation symbols. To run EditMode tests in
batch mode:

```bash
Unity -batchmode -nographics -projectPath /path/to/unity-project -runTests -testPlatform EditMode -testResults results.xml
```

For documentation changes, run `npm ci` and `npm run build` from `Documentation~/website`, then review the corresponding
English and Russian pages. Use `npm start` or `npm run start:ru` to preview one language, or `npm run preview` to build
and serve both. The site build checks rendering and links but does not compile C# examples.

### Repository layout

```text
Runtime/         Foundation runtime and optional modules and integrations
Editor/          content pipeline, workspace window, typed-id and audio validation, bootstrap menu
Tests/           EditMode and PlayMode tests, including optional adapter tests
Generators~/     TypedIds generator source (the packaged DLL is in Analyzers/)
Documentation~/  documentation site
```

## Русский

Foundation — пакет для Unity. В `package.json` сейчас указана минимальная версия Unity 6000.5.

### Перед созданием задачи

Укажите версии Unity и Foundation, задействованные адаптеры и их версии, ожидаемое и фактическое поведение, а также
понятные шаги для воспроизведения проблемы. Если это поможет разобраться, приложите относящиеся к проблеме логи или
небольшой пример. Удалите из них секреты и персональные данные.

### Изменения и pull request

- Ограничивайте каждое изменение одной задачей и придерживайтесь стиля соседнего кода.
- При изменении поведения, заметного пользователям, обновляйте английскую и русскую документацию одновременно.
- Для изменений поведения добавляйте или обновляйте тесты. Сохраняйте `.meta`-файлы Unity и их GUID; для новых ресурсов
  Unity добавляйте соответствующий `.meta`-файл.
- Если меняете генератор TypedIds, обновляйте исходный код в `Generators~` и включённый в пакет анализатор в
  `Analyzers`. Пересоберите его командой `dotnet build -c Release` из `Generators~/TypedIds`, затем скопируйте DLL в
  `Analyzers/`, сохранив `.meta`-файл.
- В описании pull request укажите, что изменилось и какие проверки вы запустили. Если нужную проверку выполнить не
  удалось, напишите об этом.

### Изменения адаптеров

- Размещайте сборку адаптера в `Runtime/<Library>/`. Её asmdef должен ссылаться на `Foundation` и библиотеку; для
  подключения при наличии зависимости настройте `versionDefines` и `defineConstraints`.
- Размещайте тесты адаптера в `Tests/<Library>/` и ограничивайте их тем же define.
- Для DI-адаптера используйте `MinimalServiceScope` как ограниченную тестовую реализацию, а тесты VContainer — как
  ориентир для проверок доступности и `As`, переопределения регистраций и владения в дочерней области, фабрик с
  `TryResolve`, колбэков сборки и уничтожения, параметров-коллекций, точек входа и инжектора игровых объектов.

### Проверки

Основные EditMode-тесты используют `MinimalServiceScope`; интеграционные и PlayMode-тесты проверяют адаптеры и поведение
объектов Unity. Для проверки кода добавьте пакет в поле `testables` файла `Packages/manifest.json` проекта Unity и
запустите подходящие EditMode- и PlayMode-тесты. Для тестов адаптеров нужны их зависимости и символы компиляции. Для
запуска EditMode-тестов в пакетном режиме:

```bash
Unity -batchmode -nographics -projectPath /path/to/unity-project -runTests -testPlatform EditMode -testResults results.xml
```

Для проверки документации выполните `npm ci` и `npm run build` в каталоге `Documentation~/website`, затем просмотрите
изменённые страницы на английском и русском языках. Для локального просмотра используйте `npm start` или
`npm run start:ru`; `npm run preview` собирает и показывает обе локали. Сборка сайта проверяет страницы и ссылки, но не
компилирует примеры C#.

### Структура репозитория

```text
Runtime/         рантайм Foundation, дополнительные модули и интеграции
Editor/          конвейер контента, окно рабочей области, проверка типизированных ID и аудио, меню начальной настройки
Tests/           EditMode- и PlayMode-тесты, в том числе необязательных адаптеров
Generators~/     исходный код генератора TypedIds (DLL пакета находится в Analyzers/)
Documentation~/  сайт документации
```
