# AppAutomation

`AppAutomation` это .NET framework для UI automation desktop-приложений на Avalonia. Его целевой consumer flow такой:

1. вы подключаете framework через NuGet, а не через скачивание исходников;
2. одной командой создаёте canonical test topology;
3. пишете page objects и shared scenarios один раз;
4. запускаете те же самые сценарии и в `Headless`, и в `FlaUI`.

Итоговая структура consumer-репозитория должна выглядеть так:

```text
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

`Authoring` владеет page objects и shared tests. `Headless` и `FlaUI` только запускают эти же сценарии через разные runtime adapters.

## Compatibility

Поддерживаемый базовый путь:

| Компонент | Поддержка |
| --- | --- |
| `AppAutomation.Abstractions` | `net8.0+` |
| `AppAutomation.Session.Contracts` | `net8.0+` |
| `AppAutomation.TUnit` | `net8.0+` |
| `AppAutomation.TestHost.Avalonia` | `net8.0+` |
| `AppAutomation.Avalonia.Headless` | `net8.0`, `net10.0` |
| `AppAutomation.FlaUI` | `net8.0-windows7.0`, `net10.0-windows7.0` |
| `FlaUI` runtime | только Windows |
| Template package | `dotnet new` |
| CLI tool | `.NET tool`, команда `appautomation` |

Полная матрица: [docs/appautomation/compatibility.md](docs/appautomation/compatibility.md)

## Fast Path

Замените `1.1.0` на нужную версию пакетов.

### 1. Установите template package

```powershell
dotnet new install AppAutomation.Templates::1.1.0
```

### 2. Установите CLI tool

Локально в repo:

```powershell
dotnet tool install --tool-path .\.tools AppAutomation.Tooling --version 1.1.0
```

Или глобально:

```powershell
dotnet tool install --global AppAutomation.Tooling --version 1.1.0
```

### 3. Сгенерируйте canonical topology

Из корня вашего consumer-репозитория:

```powershell
dotnet new appauto-avalonia --name MyApp --AppAutomationVersion 1.1.0
```

Шаблон создаст:

- `tests/MyApp.UiTests.Authoring`
- `tests/MyApp.UiTests.Headless`
- `tests/MyApp.UiTests.FlaUI`
- `tests/MyApp.AppAutomation.TestHost`
- `APPAUTOMATION_NEXT_STEPS.md`

### 4. Сразу прогоните doctor

Если tool установлен в локальную папку:

```powershell
.\.tools\appautomation doctor --repo-root .
```

Если tool установлен глобально:

```powershell
appautomation doctor --repo-root .
```

`doctor` проверяет:

- есть ли canonical topology;
- не ушли ли вы в source dependency вместо `PackageReference`;
- совместимы ли `TargetFramework`;
- есть ли `NuGet.Config`;
- закреплён ли SDK через `global.json`.

## Что сделать в consumer repo после генерации

Шаблон создаёт правильную topology, но не может знать ваш AUT-specific bootstrap. Дальше нужно сделать ровно следующие вещи.

### 1. Вписать реальный launch/bootstrap в `TestHost`

Файл:

```text
tests/MyApp.AppAutomation.TestHost/MyAppAppLaunchHost.cs
```

Нужно заменить placeholder values:

- solution file name;
- relative path до desktop `.csproj`;
- `TargetFramework` AUT;
- имя desktop executable;
- `CreateHeadlessLaunchOptions()` с реальным созданием `Window`.

Framework helpers, которые для этого уже есть из коробки:

- `AppAutomation.TestHost.Avalonia.AvaloniaDesktopLaunchHost`
- `AppAutomation.TestHost.Avalonia.AvaloniaHeadlessLaunchHost`
- `AppAutomation.TestHost.Avalonia.TemporaryDirectory`

### 2. Проставить `AutomationId` в приложении

Минимум для первой итерации:

- root window;
- main tabs / navigation anchors;
- критичные input/button/result controls;
- ключевые child controls у composite widgets.

Пример:

```xml
<TabControl automation:AutomationProperties.AutomationId="MainTabs">
  <TabItem automation:AutomationProperties.AutomationId="SmokeTabItem" />
</TabControl>
```

### 3. Подключить Headless session hooks

Файл:

```text
tests/MyApp.UiTests.Headless/Infrastructure/HeadlessSessionHooks.cs
```

Там нужно запустить ваш `Avalonia.Headless` session и зарегистрировать его через `HeadlessRuntime.SetSession(...)`.

### 4. Описать page objects и shared scenarios

В `Authoring` проекте вы:

- объявляете `[UiControl(...)]` для простых controls;
- при необходимости вручную добавляете composite abstractions;
- пишете shared scenarios один раз.

### 5. Сначала стабилизировать `Headless`, потом включать `FlaUI`

Команды:

```powershell
dotnet test tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj -c Debug
dotnet test tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj -c Debug
```

## Что уже есть из коробки

`AppAutomation` теперь закрывает типовые integration gaps, которые раньше consumer писал вручную:

- `dotnet new` template для canonical Avalonia topology;
- `appautomation doctor`;
- reusable `AppAutomation.TestHost.Avalonia`;
- desktop launch helpers с repo-root / project-path / build-before-launch;
- headless launch helpers поверх `BeforeLaunchAsync`, `CreateMainWindow`, `CreateMainWindowAsync`;
- adapter registration API через `WithAdapters(...)`;
- built-in composite abstraction `ISearchPickerControl` и `WithSearchPicker(...)`;
- package-based smoke path через `eng/smoke-consumer.ps1`.

## Что остаётся consumer responsibility

Framework не может автоматизировать это честно и полностью, поэтому эти вещи остаются на стороне consumer-а:

- domain-specific test data и permissions;
- auth bypass / login story;
- точная startup semantics AUT;
- решение, какие secondary controls лучше упростить данными;
- добавление `AutomationId` в самом AUT.

## Do Not

- Не тяните `src/AppAutomation.*` в consumer repo как source dependency, если нет совсем крайней причины.
- Не дублируйте tests из `Authoring` в runtime projects.
- Не начинайте со сложного end-to-end path. Сначала один критичный smoke scenario.
- Не автоматизируйте все controls подряд до того, как стабилизирован login / startup / settings path.
- Не прячьте repo-specific bootstrap внутрь reusable framework package.

## Reference Implementation

Working reference в этом репозитории:

- [sample/DotnetDebug.AppAutomation.Authoring](sample/DotnetDebug.AppAutomation.Authoring)
- [sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests](sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests)
- [sample/DotnetDebug.AppAutomation.FlaUI.Tests](sample/DotnetDebug.AppAutomation.FlaUI.Tests)
- [sample/DotnetDebug.AppAutomation.TestHost](sample/DotnetDebug.AppAutomation.TestHost)

## Дальше

- Пошаговый consumer flow: [docs/appautomation/quickstart.md](docs/appautomation/quickstart.md)
- Pre-flight checklist: [docs/appautomation/adoption-checklist.md](docs/appautomation/adoption-checklist.md)
- Canonical project responsibilities: [docs/appautomation/project-topology.md](docs/appautomation/project-topology.md)
- Advanced bootstrap and composite controls: [docs/appautomation/advanced-integration.md](docs/appautomation/advanced-integration.md)
- Packaging and release flow: [docs/appautomation/publishing.md](docs/appautomation/publishing.md)
