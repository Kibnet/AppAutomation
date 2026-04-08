# AppAutomation

[English](#appautomation) | [Русский](#-русская-версия)

[![NuGet Version](https://img.shields.io/nuget/v/AppAutomation.Abstractions?label=NuGet%20(AppAutomation.Abstractions))](https://www.nuget.org/packages/AppAutomation.Abstractions)

`AppAutomation` is a framework for UI automation of Avalonia desktop applications. The target consumer flow is:

1. You integrate the framework via NuGet, not by downloading source code;
2. You create the canonical test topology with a single command;
3. You write page objects and shared scenarios once;
4. You run the same scenarios in both `Headless` and `FlaUI`.

The resulting structure of a consumer repository should look like this:

```text
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

`Authoring` owns page objects and shared tests. `Headless` and `FlaUI` only run these scenarios through different runtime adapters.

## Compatibility

Supported baseline:

| Component | Support |
| --- | --- |
| `AppAutomation.Abstractions` | `net8.0+` |
| `AppAutomation.Session.Contracts` | `net8.0+` |
| `AppAutomation.TUnit` | `net8.0+` |
| `AppAutomation.TestHost.Avalonia` | `net8.0+` |
| `AppAutomation.Avalonia.Headless` | `net8.0`, `net10.0` |
| `AppAutomation.FlaUI` | `net8.0-windows7.0`, `net10.0-windows7.0` |
| `FlaUI` runtime | Windows only |
| Template package | `dotnet new` |
| CLI tool | `.NET tool`, command `appautomation` |

Full matrix: [docs/appautomation/compatibility.md](docs/appautomation/compatibility.md)

## Fast Path

The commands below use the latest version available from your configured feed.
If you need a reproducible install for a specific release, use the pinned example further below.

### 1. Install template package

```powershell
dotnet new install AppAutomation.Templates
```

### 2. Install CLI tool

Recommended local tool manifest in the consumer repo:

```powershell
dotnet new tool-manifest
dotnet tool install AppAutomation.Tooling
```

Fallback global install:

```powershell
dotnet tool install --global AppAutomation.Tooling
```

### 3. Generate canonical topology

From the root of your consumer repository:

```powershell
dotnet new appauto-avalonia --name MyApp
```

Pinned example for a specific release:

```powershell
dotnet new install AppAutomation.Templates@2.1.0
dotnet tool install AppAutomation.Tooling --version 2.1.0
dotnet new appauto-avalonia --name MyApp --AppAutomationVersion 2.1.0
```

The template will create:

- `tests/MyApp.UiTests.Authoring`
- `tests/MyApp.UiTests.Headless`
- `tests/MyApp.UiTests.FlaUI`
- `tests/MyApp.AppAutomation.TestHost`
- `APPAUTOMATION_NEXT_STEPS.md`

### 4. Run doctor immediately

If the tool is installed via local manifest:

```powershell
dotnet tool run appautomation doctor --repo-root .
```

If the tool is installed globally:

```powershell
appautomation doctor --repo-root .
```

`doctor` checks:

- whether canonical topology exists;
- whether you've switched to source dependency instead of `PackageReference`;
- whether generated scaffold still contains placeholder values;
- whether `TargetFramework` is compatible;
- whether `NuGet.Config` exists anywhere under the repository root;
- whether SDK is pinned via `global.json`.

## What to do in consumer repo after generation

The template creates the correct topology, but cannot know your AUT-specific bootstrap. Next, you need to do exactly the following things.

### 1. Fill in the real launch/bootstrap in `TestHost`

File:

```text
tests/MyApp.AppAutomation.TestHost/MyAppAppLaunchHost.cs
```

You need to replace placeholder values:

- solution file name;
- relative path to desktop `.csproj`;
- `TargetFramework` of AUT;
- desktop executable name;
- `AvaloniaAppType` used by generated headless hooks;
- `CreateHeadlessLaunchOptions()` with real `Window` creation.

Framework helpers that are already available out of the box:

- `AppAutomation.TestHost.Avalonia.AvaloniaDesktopLaunchHost`
- `AppAutomation.TestHost.Avalonia.AvaloniaHeadlessLaunchHost`
- `AppAutomation.TestHost.Avalonia.TemporaryDirectory`

### 2. Set `AutomationId` in the application

Minimum for the first iteration:

- root window;
- main tabs / navigation anchors;
- critical input/button/result controls;
- key child controls for composite widgets.
- explicit `AutomationProperties.Name` for controls you assert via `WaitUntilName*`.

Example:

```xml
<TabControl automation:AutomationProperties.AutomationId="MainTabs">
  <TabItem automation:AutomationProperties.AutomationId="SmokeTabItem" />
</TabControl>
```

### 3. Connect Headless session hooks

File:

```text
tests/MyApp.UiTests.Headless/Infrastructure/HeadlessSessionHooks.cs
```

The generated hooks already call `HeadlessRuntime.SetSession(...)` through `MyAppAppLaunchHost.AvaloniaAppType`. Replace the placeholder app type in `TestHost`, then keep the generated hooks as-is unless your AUT needs custom session lifetime handling.

### 4. Describe page objects and shared scenarios

In the `Authoring` project you:

- declare `[UiControl(...)]` for simple controls;
- manually add composite abstractions if necessary;
- write shared scenarios once.

### Optional: bootstrap scenarios with the Avalonia recorder

If you want to reduce the first manual authoring pass, attach `AppAutomation.Recorder.Avalonia` to your AUT and let it generate `Authoring` partials instead of runtime-specific tests.

- Keep page classes `partial`.
- Keep the shared scenario base class `partial` too, because recorder output is emitted as an extra partial with `[Test]` methods.
- Prefer stable `AutomationId`; `Name` locators are opt-in and intentionally treated as a weaker fallback.
- `Save` writes into the canonical `Authoring` target, while `Export...` writes the same generated pair into a folder you pick from the overlay.
- Invalid or ambiguous steps can stay visible in overlay preview for debugging, but they are skipped on save and reported as `persisted/skipped`.
- The overlay keeps a step journal with `Remove`, `Ignore`, `Retry`, and `Copy` actions, so you can clean up a recording session without restarting it.
- Save and export are single-flight operations: while a save/export is running, the overlay shows a busy summary and blocks duplicate save/export clicks.
- The recorder UI is hosted in a separate opaque window, so it no longer follows or overlays the AUT window.
- Hotkeys, overlay behavior, selector validation, and custom assertion capture are configurable through `AppAutomationRecorderOptions`.

Reference smoke path in this repository:

```powershell
$env:APPAUTOMATION_RECORDER='1'
$env:APPAUTOMATION_RECORDER_SCENARIO='SmokeFlow'
dotnet run --project sample/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj -c Debug
```

The sample writes generated files to `sample/DotnetDebug.AppAutomation.Authoring/Recorded`. The overlay can start or stop capture, minimize or restore itself, save canonical partials, export the same output to another folder, keep a review-first step journal, and show either the latest AppAutomation DSL statement or the diagnostics that explain why a step is warning-only or invalid.

Custom assertion capture can be extended without forking the recorder:

```csharp
var recorderOptions = new AppAutomationRecorderOptions();
recorderOptions.AssertionExtractors.Add(new MyStatusBadgeAssertionExtractor());
AppAutomationRecorder.Attach(mainWindow, recorderOptions);
```

### 5. Stabilize `Headless` first, then enable `FlaUI`

Commands:

```powershell
dotnet test --project tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj -c Debug
dotnet test --project tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj -c Debug
```

To run the whole generated solution from the repo root:

```powershell
dotnet test --solution MyApp.sln -c Debug
```

If you see `Headless session is not initialized. Call HeadlessRuntime.SetSession from test hooks.`, verify:

- `tests/MyApp.UiTests.Headless/Infrastructure/HeadlessSessionHooks.cs` is still active in your test runner hooks;
- the hooks call `HeadlessRuntime.SetSession(...)` before tests and clear it afterwards;
- `MyAppAppLaunchHost.AvaloniaAppType` is no longer a placeholder;
- you're running the intended target with `dotnet test --project ...` or `dotnet test --solution MyApp.sln -c Debug`.

## What's already available out of the box

`AppAutomation` now covers typical integration gaps that consumers used to write manually:

- `dotnet new` template for canonical Avalonia topology;
- `appautomation doctor`;
- reusable `AppAutomation.TestHost.Avalonia`;
- desktop launch helpers with repo-root / project-path / build-before-launch;
- headless launch helpers on top of `BeforeLaunchAsync`, `CreateMainWindow`, `CreateMainWindowAsync`;
- adapter registration API via `WithAdapters(...)`;
- built-in composite abstraction `ISearchPickerControl` and `WithSearchPicker(...)`;
- package-based smoke path via `eng/smoke-consumer.ps1`.

## What remains consumer responsibility

The framework cannot automate these honestly and completely, so these things remain on the consumer side:

- domain-specific test data and permissions;
- auth bypass / login story;
- exact startup semantics of AUT;
- decision on which secondary controls are better simplified with data;
- adding `AutomationId` in the AUT itself.

## Do Not

- Do not pull `src/AppAutomation.*` into your consumer repo as source dependency unless there's an extreme reason.
- Do not duplicate tests from `Authoring` in runtime projects.
- Do not start with a complex end-to-end path. Start with one critical smoke scenario.
- Do not automate all controls before login / startup / settings path is stabilized.
- Do not hide repo-specific bootstrap inside a reusable framework package.

## Reference Implementation

Working reference in this repository:

- [sample/DotnetDebug.AppAutomation.Authoring](sample/DotnetDebug.AppAutomation.Authoring)
- [sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests](sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests)
- [sample/DotnetDebug.AppAutomation.FlaUI.Tests](sample/DotnetDebug.AppAutomation.FlaUI.Tests)
- [sample/DotnetDebug.AppAutomation.TestHost](sample/DotnetDebug.AppAutomation.TestHost)

## Next Steps

- Step-by-step consumer flow: [docs/appautomation/quickstart.md](docs/appautomation/quickstart.md)
- Pre-flight checklist: [docs/appautomation/adoption-checklist.md](docs/appautomation/adoption-checklist.md)
- Canonical project responsibilities: [docs/appautomation/project-topology.md](docs/appautomation/project-topology.md)
- Selector contract for both runtimes: [docs/appautomation/selector-contract.md](docs/appautomation/selector-contract.md)
- Advanced bootstrap and composite controls: [docs/appautomation/advanced-integration.md](docs/appautomation/advanced-integration.md)
- Packaging and release flow: [docs/appautomation/publishing.md](docs/appautomation/publishing.md)

---

# 🇷🇺 Русская версия

[English](#appautomation) | [Русский](#-русская-версия)

`AppAutomation` — это фреймворк для автоматизации пользовательского интерфейса настольных приложений на Avalonia. Типовой сценарий использования выглядит так:

1. вы подключаете фреймворк через NuGet, а не скачиваете исходный код;
2. одной командой создаёте стандартную структуру тестов;
3. один раз описываете объекты страниц и общие сценарии;
4. запускаете те же сценарии и в `Headless`, и в `FlaUI`.

Итоговая структура репозитория-потребителя должна выглядеть так:

```text
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

`Authoring` содержит объекты страниц и общие тесты. `Headless` и `FlaUI` только запускают эти же сценарии через разные адаптеры выполнения.

## Совместимость

Поддерживаемая базовая конфигурация:

| Компонент | Поддержка |
| --- | --- |
| `AppAutomation.Abstractions` | `net8.0+` |
| `AppAutomation.Session.Contracts` | `net8.0+` |
| `AppAutomation.TUnit` | `net8.0+` |
| `AppAutomation.TestHost.Avalonia` | `net8.0+` |
| `AppAutomation.Avalonia.Headless` | `net8.0`, `net10.0` |
| `AppAutomation.FlaUI` | `net8.0-windows7.0`, `net10.0-windows7.0` |
| Среда выполнения `FlaUI` | только Windows |
| Пакет шаблонов | `dotnet new` |
| Инструмент командной строки | `.NET tool`, команда `appautomation` |

Полная матрица: [docs/appautomation/compatibility.md](docs/appautomation/compatibility.md)

## Быстрый старт

Команды ниже используют последнюю доступную версию из настроенного feed.
Если нужна воспроизводимая установка конкретного релиза, используйте pinned-пример ниже.

### 1. Установите пакет шаблонов

```powershell
dotnet new install AppAutomation.Templates
```

### 2. Установите инструмент командной строки

Рекомендуемый локальный tool manifest в репозитории-потребителе:

```powershell
dotnet new tool-manifest
dotnet tool install AppAutomation.Tooling
```

Резервный глобальный вариант:

```powershell
dotnet tool install --global AppAutomation.Tooling
```

### 3. Сгенерируйте стандартную структуру тестов

Из корня вашего репозитория-потребителя:

```powershell
dotnet new appauto-avalonia --name MyApp
```

Pinned-пример для конкретного релиза:

```powershell
dotnet new install AppAutomation.Templates@2.1.0
dotnet tool install AppAutomation.Tooling --version 2.1.0
dotnet new appauto-avalonia --name MyApp --AppAutomationVersion 2.1.0
```

Шаблон создаст:

- `tests/MyApp.UiTests.Authoring`
- `tests/MyApp.UiTests.Headless`
- `tests/MyApp.UiTests.FlaUI`
- `tests/MyApp.AppAutomation.TestHost`
- `APPAUTOMATION_NEXT_STEPS.md`

### 4. Сразу запустите `doctor`

Если инструмент установлен через локальный manifest:

```powershell
dotnet tool run appautomation doctor --repo-root .
```

Если инструмент установлен глобально:

```powershell
appautomation doctor --repo-root .
```

`doctor` проверяет:

- существует ли стандартная структура тестов;
- не перешли ли вы на зависимость в виде исходного кода вместо `PackageReference`;
- содержит ли сгенерированный scaffold ещё неубранные placeholder-значения;
- совместимы ли `TargetFramework`;
- есть ли `NuGet.Config` где-либо под корнем репозитория;
- закреплён ли SDK через `global.json`.

## Что сделать в репозитории-потребителе после генерации

Шаблон создаёт правильную структуру, но не может знать особенности запуска вашего AUT. После генерации нужно сделать следующее.

### 1. Задать реальную логику запуска в `TestHost`

Файл:

```text
tests/MyApp.AppAutomation.TestHost/MyAppAppLaunchHost.cs
```

Нужно заменить значения-заглушки:

- имя файла решения;
- относительный путь к настольному `.csproj`;
- `TargetFramework` для AUT;
- имя исполняемого файла настольного приложения;
- `AvaloniaAppType`, который используют сгенерированные headless hooks;
- `CreateHeadlessLaunchOptions()` с реальным созданием `Window`.

Вспомогательные классы фреймворка, которые уже доступны:

- `AppAutomation.TestHost.Avalonia.AvaloniaDesktopLaunchHost`
- `AppAutomation.TestHost.Avalonia.AvaloniaHeadlessLaunchHost`
- `AppAutomation.TestHost.Avalonia.TemporaryDirectory`

### 2. Проставить `AutomationId` в приложении

Минимум для первой итерации:

- корневое окно;
- основные вкладки и опорные элементы навигации;
- критичные поля ввода, кнопки и элементы с результатами;
- ключевые дочерние элементы в составных элементах интерфейса.
- явный `AutomationProperties.Name` для тех элементов, которые будут участвовать в `WaitUntilName*`.

Пример:

```xml
<TabControl automation:AutomationProperties.AutomationId="MainTabs">
  <TabItem automation:AutomationProperties.AutomationId="SmokeTabItem" />
</TabControl>
```

### 3. Подключить обработчики сеанса `Headless`

Файл:

```text
tests/MyApp.UiTests.Headless/Infrastructure/HeadlessSessionHooks.cs
```

Сгенерированные hooks уже вызывают `HeadlessRuntime.SetSession(...)` через `MyAppAppLaunchHost.AvaloniaAppType`. Обычно достаточно заменить placeholder-типа приложения в `TestHost` и оставить hooks без изменений, если AUT не требует особого жизненного цикла сеанса.

### 4. Описать объекты страниц и общие сценарии

В проекте `Authoring` вы:

- объявляете `[UiControl(...)]` для простых элементов управления;
- при необходимости вручную добавляете составные абстракции;
- один раз пишете общие сценарии.

### Опционально: ускорить старт через Avalonia recorder

Если не хочется вручную проходить весь первый цикл authoring-кода, можно подключить `AppAutomation.Recorder.Avalonia` к AUT и генерировать partial-файлы прямо в `Authoring`, а не отдельные runtime-specific тесты.

- Классы страниц должны оставаться `partial`.
- Общий scenario base class тоже должен быть `partial`, потому что recorder добавляет новые `[Test]`-методы в отдельный partial.
- Основной контракт селекторов для recorder-а это `AutomationId`; `Name` включается только осознанно и считается более слабым fallback.
- `Save` пишет в каноническую директорию `Authoring`, а `Export...` сохраняет ту же пару generated partials в выбранную папку.
- Невалидные или неоднозначные шаги можно оставить в preview для отладки, но при сохранении они пропускаются и попадают в статус как `persisted/skipped`.
- Overlay держит step journal с действиями `Remove`, `Ignore`, `Retry` и `Copy`, так что плохой шаг можно выкинуть или отложить без полного перезапуска записи.
- `Save` и `Export...` теперь single-flight: пока идёт запись файлов, overlay показывает busy summary и не даёт запустить второй save/export поверх первого.
- Recorder UI теперь живёт в отдельном непрозрачном окне и больше не привязан к позиции или состоянию окна AUT.
- Hotkeys, поведение overlay, selector validation и кастомный assertion capture настраиваются через `AppAutomationRecorderOptions`.

Референсный smoke path в этом репозитории:

```powershell
$env:APPAUTOMATION_RECORDER='1'
$env:APPAUTOMATION_RECORDER_SCENARIO='SmokeFlow'
dotnet run --project sample/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj -c Debug
```

Sample сохраняет generated partials в `sample/DotnetDebug.AppAutomation.Authoring/Recorded`. Overlay позволяет запускать и останавливать запись, сворачиваться и восстанавливаться, сохранять канонические partials, экспортировать тот же output в другую директорию, просматривать и править session-level step journal и сразу видеть либо последний AppAutomation DSL-вызов, либо диагностику, почему конкретный шаг остался warning-only или invalid.

Кастомный assertion capture можно подключить без форка recorder-а:

```csharp
var recorderOptions = new AppAutomationRecorderOptions();
recorderOptions.AssertionExtractors.Add(new MyStatusBadgeAssertionExtractor());
AppAutomationRecorder.Attach(mainWindow, recorderOptions);
```

### 5. Сначала стабилизировать `Headless`, потом включать `FlaUI`

Команды:

```powershell
dotnet test --project tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj -c Debug
dotnet test --project tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj -c Debug
```

Чтобы запустить всё сгенерированное решение из корня репозитория:

```powershell
dotnet test --solution MyApp.sln -c Debug
```

Если вы видите `Headless session is not initialized. Call HeadlessRuntime.SetSession from test hooks.`, проверьте:

- что `tests/MyApp.UiTests.Headless/Infrastructure/HeadlessSessionHooks.cs` по-прежнему подключён в hooks тестового раннера;
- что hooks вызывают `HeadlessRuntime.SetSession(...)` до тестов и очищают его после завершения;
- что `MyAppAppLaunchHost.AvaloniaAppType` уже не содержит placeholder;
- что вы запускаете нужную цель через `dotnet test --project ...` или `dotnet test --solution MyApp.sln -c Debug`.

## Что уже доступно

`AppAutomation` уже закрывает типичные проблемы интеграции, которые раньше приходилось решать вручную:

- шаблон `dotnet new` для стандартной структуры проектов Avalonia;
- `appautomation doctor`;
- переиспользуемый `AppAutomation.TestHost.Avalonia`;
- вспомогательные средства запуска настольного приложения с `repo-root`, `project-path` и `build-before-launch`;
- вспомогательные средства запуска `Headless` поверх `BeforeLaunchAsync`, `CreateMainWindow`, `CreateMainWindowAsync`;
- API регистрации адаптеров через `WithAdapters(...)`;
- встроенная составная абстракция `ISearchPickerControl` и `WithSearchPicker(...)`;
- готовый сценарий быстрой проверки через `eng/smoke-consumer.ps1`.

## Что остаётся на стороне потребителя

Фреймворк не может полностью и надёжно автоматизировать следующие вещи, поэтому они остаются на стороне потребителя:

- предметно-ориентированные тестовые данные и права доступа;
- обход аутентификации и сценарий входа;
- точное поведение AUT при запуске;
- решение о том, какие второстепенные элементы лучше упростить данными;
- добавление `AutomationId` в самом AUT.

## Чего не делать

- Не подтягивайте `src/AppAutomation.*` в репозиторий-потребитель как зависимость в виде исходного кода, если для этого нет совсем крайней причины.
- Не дублируйте тесты из `Authoring` в проектах `Headless` и `FlaUI`.
- Не начинайте со сложного сквозного сценария. Сначала нужен один критичный сценарий быстрой проверки.
- Не автоматизируйте все элементы подряд, пока не стабилизированы вход, запуск и путь через настройки.
- Не прячьте специфичную для репозитория логику запуска внутрь переиспользуемого пакета фреймворка.

## Эталонная реализация

В этом репозитории есть рабочий пример:

- [sample/DotnetDebug.AppAutomation.Authoring](sample/DotnetDebug.AppAutomation.Authoring)
- [sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests](sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests)
- [sample/DotnetDebug.AppAutomation.FlaUI.Tests](sample/DotnetDebug.AppAutomation.FlaUI.Tests)
- [sample/DotnetDebug.AppAutomation.TestHost](sample/DotnetDebug.AppAutomation.TestHost)

## Дальше

- Пошаговый сценарий подключения: [docs/appautomation/quickstart.md](docs/appautomation/quickstart.md)
- Проверочный список перед стартом: [docs/appautomation/adoption-checklist.md](docs/appautomation/adoption-checklist.md)
- Роли проектов в стандартной структуре: [docs/appautomation/project-topology.md](docs/appautomation/project-topology.md)
- Контракт селекторов для обоих рантаймов: [docs/appautomation/selector-contract.md](docs/appautomation/selector-contract.md)
- Расширенная инициализация и составные элементы управления: [docs/appautomation/advanced-integration.md](docs/appautomation/advanced-integration.md)
- Упаковка и процесс выпуска: [docs/appautomation/publishing.md](docs/appautomation/publishing.md)
