# AppAutomation Advanced Integration

**English** | [Русский](#русская-версия)

This document covers cases that go beyond the quickstart.

## 1. Nested solution and repo-root discovery

If the solution is under `src/`, do not embed layout knowledge into reusable packages. Keep it in `*.AppAutomation.TestHost`.

Use `AvaloniaDesktopAppDescriptor` + `AvaloniaDesktopLaunchHost`:

```csharp
private static readonly AvaloniaDesktopAppDescriptor DesktopApp = new(
    solutionFileNames: ["MyApp.sln"],
    desktopProjectRelativePaths: ["src\\MyApp.Desktop\\MyApp.Desktop.csproj"],
    desktopTargetFramework: "net8.0",
    executableName: "MyApp.Desktop.exe");
```

## 2. Repeated headless launches

If AUT holds static state, use:

- `BeforeLaunchAsync` for reset;
- `CreateMainWindowAsync` for async bootstrap;
- `TemporaryDirectory` for isolated files.

Example:

```csharp
return AvaloniaHeadlessLaunchHost.Create(
    async cancellationToken =>
    {
        await ResetStaticStateAsync(cancellationToken);
        return MyBootstrap.CreateMainWindow();
    },
    beforeLaunchAsync: cancellationToken =>
    {
        PrepareIsolatedWorkspace();
        return ValueTask.CompletedTask;
    });
```

## 3. Isolated settings and temp files

`TemporaryDirectory` is needed for:

- temporary settings json;
- transient database/filesystem state;
- per-run artifacts.

Example:

```csharp
using var temp = TemporaryDirectory.Create("MyAppAutomation");
var settingsPath = temp.WriteTextFile("settings\\Settings.json", json);
```

## 4. Launch scenarios and preflight

If your smoke path needs deterministic signed-in or server-backed state, use `AutomationLaunchScenario<TPayload>` and read it via `AutomationLaunchContext`.

Desktop example:

```csharp
var scenario = new AutomationLaunchScenario<MyLaunchState>("SignedInSmoke", state);
return AvaloniaDesktopLaunchHost.CreateLaunchOptions(
    desktopAppDescriptor,
    scenario,
    new AvaloniaDesktopLaunchOptions());
```

Headless example:

```csharp
var scenario = new AutomationLaunchScenario<MyLaunchState>("SignedInSmoke", state);
return AvaloniaHeadlessLaunchHost.Create(
    static () => MyAppBootstrap.CreateMainWindow(),
    scenario);
```

Read `AutomationLaunchContext` inside `BeforeLaunchAsync`, `CreateMainWindow`, or `CreateMainWindowAsync`. In headless mode the ambient override is scoped to launch callbacks so parallel scenarios do not leak into each other.

For required inputs, aggregate diagnostics before launch:

```csharp
AutomationPreflight.Create("MyApp login smoke")
    .RequireEnvironmentVariable("MYAPP_TEST_SERVER_URL")
    .RequireEnvironmentVariable("MYAPP_TEST_LOGIN", secret: true)
    .RequireEnvironmentVariable("MYAPP_TEST_PASSWORD", secret: true)
    .ThrowIfInvalid();
```

If launch fails, AppAutomation rethrows the original launch exception. If cleanup also fails, the secondary failure is attached via `exception.Data["AppAutomation.CleanupException"]`.

## 5. Composite controls

If a widget doesn't fit into built-in `UiControlType`, don't rewrite the runtime resolver entirely.

Correct order:

1. try to solve the scenario by simplifying data;
2. if not possible, use `WithAdapters(...)`;
3. if the scenario is similar to search + select, first use `WithSearchPicker(...)`.

Built-in path example:

```csharp
var resolver = new FlaUiControlResolver(window, conditionFactory)
    .WithSearchPicker(
        "ServerPicker",
        SearchPickerParts.ByAutomationIds(
            "ServerPickerInput",
            "ServerPickerResults",
            applyButtonAutomationId: "ServerPickerApply"));
```

Page property example:

```csharp
private static UiControlDefinition ServerPickerDefinition { get; } =
    new("ServerPicker", UiControlType.AutomationElement, "ServerPicker", UiLocatorKind.AutomationId, FallbackToName: false);

public ISearchPickerControl ServerPicker => Resolve<ISearchPickerControl>(ServerPickerDefinition);
```

## 6. Dynamic selectors and selector contract

Keep one selector contract for both runtimes:

- `AutomationId` is mandatory for primary controls;
- `AutomationProperties.Name` is required for `WaitUntilName*`;
- repeated entities should use parameterized ids such as `MessageItem_{id}` or `Row_{key}`.

Full contract: [selector-contract.md](selector-contract.md)

## 7. Internal feeds and package-source strategy

If direct `nuget.org` is prohibited in your organization:

- configure internal mirror in `NuGet.Config`;
- keep `PackageReference` to `AppAutomation.*`;
- don't switch to source dependency just because the feed is not configured.

`appautomation doctor` should see a valid `NuGet.Config` before starting integration work.

## 8. Readiness and retry

Use framework helpers:

- `WaitUntil(...)`
- `WaitUntilAsync(...)`
- `RetryUntil(...)`

But don't substitute them for bad selectors. If a control is consistently found only through retry, fix the `AutomationId` first.

## 9. Isolated desktop build output

If design-time tools or parallel runners lock the AUT output directory, opt into isolated desktop build output:

```csharp
return AvaloniaDesktopLaunchHost.CreateLaunchOptions(
    desktopAppDescriptor,
    new AvaloniaDesktopLaunchOptions
    {
        UseIsolatedBuildOutput = true
    });
```

The isolated mode is opt-in and should be used only when the default in-place build path is operationally noisy.

## 10. When headless shouldn't cover everything

If the application is hard to reset in-process, a normal strategy is:

- `Headless` covers smoke + critical deterministic flows;
- desktop-only unstable paths remain in `FlaUI`;
- after every added headless scenario, re-check repeated launch stability.

---

<a id="русская-версия"></a>

## Русская версия

[English](#appautomation-advanced-integration) | **Русский**

Этот документ описывает случаи, которые выходят за рамки краткого руководства.

## 1. Вложенное решение и поиск корня репозитория

Если решение лежит в `src/`, не встраивайте сведения о структуре каталогов в переиспользуемые пакеты. Храните это в `*.AppAutomation.TestHost`.

Используйте `AvaloniaDesktopAppDescriptor` + `AvaloniaDesktopLaunchHost`:

```csharp
private static readonly AvaloniaDesktopAppDescriptor DesktopApp = new(
    solutionFileNames: ["MyApp.sln"],
    desktopProjectRelativePaths: ["src\\MyApp.Desktop\\MyApp.Desktop.csproj"],
    desktopTargetFramework: "net8.0",
    executableName: "MyApp.Desktop.exe");
```

## 2. Повторные запуски `Headless`

Если AUT хранит статическое состояние, используйте:

- `BeforeLaunchAsync` для сброса;
- `CreateMainWindowAsync` для асинхронной инициализации;
- `TemporaryDirectory` для изолированных файлов.

Пример:

```csharp
return AvaloniaHeadlessLaunchHost.Create(
    async cancellationToken =>
    {
        await ResetStaticStateAsync(cancellationToken);
        return MyBootstrap.CreateMainWindow();
    },
    beforeLaunchAsync: cancellationToken =>
    {
        PrepareIsolatedWorkspace();
        return ValueTask.CompletedTask;
    });
```

## 3. Изолированные настройки и временные файлы

`TemporaryDirectory` нужен для:

- временного файла настроек JSON;
- временного состояния базы данных и файловой системы;
- артефактов отдельного запуска.

Пример:

```csharp
using var temp = TemporaryDirectory.Create("MyAppAutomation");
var settingsPath = temp.WriteTextFile("settings\\Settings.json", json);
```

## 4. Launch scenarios и preflight

Если вашему smoke-сценарию нужен детерминированный signed-in или server-backed state, используйте `AutomationLaunchScenario<TPayload>` и читайте его через `AutomationLaunchContext`.

Пример для desktop:

```csharp
var scenario = new AutomationLaunchScenario<MyLaunchState>("SignedInSmoke", state);
return AvaloniaDesktopLaunchHost.CreateLaunchOptions(
    desktopAppDescriptor,
    scenario,
    new AvaloniaDesktopLaunchOptions());
```

Пример для headless:

```csharp
var scenario = new AutomationLaunchScenario<MyLaunchState>("SignedInSmoke", state);
return AvaloniaHeadlessLaunchHost.Create(
    static () => MyAppBootstrap.CreateMainWindow(),
    scenario);
```

Читайте `AutomationLaunchContext` внутри `BeforeLaunchAsync`, `CreateMainWindow` или `CreateMainWindowAsync`. В headless-режиме ambient override ограничен launch-callback'ами, чтобы параллельные сценарии не перетекали друг в друга.

Для обязательных входов собирайте одну агрегированную диагностику до запуска:

```csharp
AutomationPreflight.Create("MyApp login smoke")
    .RequireEnvironmentVariable("MYAPP_TEST_SERVER_URL")
    .RequireEnvironmentVariable("MYAPP_TEST_LOGIN", secret: true)
    .RequireEnvironmentVariable("MYAPP_TEST_PASSWORD", secret: true)
    .ThrowIfInvalid();
```

Если запуск падает, AppAutomation повторно выбрасывает исходное исключение запуска. Если cleanup тоже падает, вторичная ошибка прикрепляется через `exception.Data["AppAutomation.CleanupException"]`.

## 5. Составные элементы управления

Если виджет не укладывается во встроенный `UiControlType`, не переписывайте весь резолвер среды выполнения.

Правильный порядок:

1. попробовать решить сценарий упрощением данных;
2. если нельзя, использовать `WithAdapters(...)`;
3. если сценарий похож на поиск с выбором, сначала использовать `WithSearchPicker(...)`.

Пример встроенного пути:

```csharp
var resolver = new FlaUiControlResolver(window, conditionFactory)
    .WithSearchPicker(
        "ServerPicker",
        SearchPickerParts.ByAutomationIds(
            "ServerPickerInput",
            "ServerPickerResults",
            applyButtonAutomationId: "ServerPickerApply"));
```

Пример свойства страницы:

```csharp
private static UiControlDefinition ServerPickerDefinition { get; } =
    new("ServerPicker", UiControlType.AutomationElement, "ServerPicker", UiLocatorKind.AutomationId, FallbackToName: false);

public ISearchPickerControl ServerPicker => Resolve<ISearchPickerControl>(ServerPickerDefinition);
```

## 6. Динамические селекторы и selector contract

Держите один selector contract для обоих runtime:

- `AutomationId` обязателен для primary controls;
- `AutomationProperties.Name` нужен для `WaitUntilName*`;
- повторяющиеся сущности должны использовать параметризованные id вроде `MessageItem_{id}` или `Row_{key}`.

Полный контракт: [selector-contract.md](selector-contract.md)

## 7. Внутренние источники пакетов и стратегия выбора источников

Если в организации запрещён прямой `nuget.org`:

- настройте внутреннее зеркало в `NuGet.Config`;
- держите `PackageReference` на `AppAutomation.*`;
- не переходите на зависимость через исходный код только потому, что источник пакетов не настроен.

`appautomation doctor` должен видеть корректный `NuGet.Config` до начала работ по интеграции.

## 8. Готовность и повторные попытки

Используйте вспомогательные методы фреймворка:

- `WaitUntil(...)`
- `WaitUntilAsync(...)`
- `RetryUntil(...)`

Но не подменяйте ими плохие селекторы. Если элемент стабильно находится только через повторные попытки, сначала исправьте `AutomationId`.

## 9. Изолированный desktop build output

Если design-time tooling или параллельные раннеры блокируют каталог вывода AUT, включайте isolated desktop build output:

```csharp
return AvaloniaDesktopLaunchHost.CreateLaunchOptions(
    desktopAppDescriptor,
    new AvaloniaDesktopLaunchOptions
    {
        UseIsolatedBuildOutput = true
    });
```

Этот режим opt-in и нужен только там, где стандартная in-place сборка создаёт операционный шум.

## 10. Когда `Headless` не должен покрывать всё

Если приложение трудно сбрасывать внутри процесса, нормальная стратегия такая:

- `Headless` покрывает сценарии быстрой проверки и критичные детерминированные сценарии;
- нестабильные пути, доступные только в настольном режиме, остаются в `FlaUI`;
- после каждого добавленного сценария `Headless` заново проверяйте стабильность повторных запусков.
