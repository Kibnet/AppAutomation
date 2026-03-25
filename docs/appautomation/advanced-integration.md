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

## 4. Composite controls

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

## 5. Internal feeds and package-source strategy

If direct `nuget.org` is prohibited in your organization:

- configure internal mirror in `NuGet.Config`;
- keep `PackageReference` to `AppAutomation.*`;
- don't switch to source dependency just because the feed is not configured.

`appautomation doctor` should see a valid `NuGet.Config` before starting integration work.

## 6. Readiness and retry

Use framework helpers:

- `WaitUntil(...)`
- `WaitUntilAsync(...)`
- `RetryUntil(...)`

But don't substitute them for bad selectors. If a control is consistently found only through retry, fix the `AutomationId` first.

## 7. When headless shouldn't cover everything

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

## 4. Составные элементы управления

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

## 5. Внутренние источники пакетов и стратегия выбора источников

Если в организации запрещён прямой `nuget.org`:

- настройте внутреннее зеркало в `NuGet.Config`;
- держите `PackageReference` на `AppAutomation.*`;
- не переходите на зависимость через исходный код только потому, что источник пакетов не настроен.

`appautomation doctor` должен видеть корректный `NuGet.Config` до начала работ по интеграции.

## 6. Готовность и повторные попытки

Используйте вспомогательные методы фреймворка:

- `WaitUntil(...)`
- `WaitUntilAsync(...)`
- `RetryUntil(...)`

Но не подменяйте ими плохие селекторы. Если элемент стабильно находится только через повторные попытки, сначала исправьте `AutomationId`.

## 7. Когда `Headless` не должен покрывать всё

Если приложение трудно сбрасывать внутри процесса, нормальная стратегия такая:

- `Headless` покрывает сценарии быстрой проверки и критичные детерминированные сценарии;
- нестабильные пути, доступные только в настольном режиме, остаются в `FlaUI`;
- после каждого добавленного сценария `Headless` заново проверяйте стабильность повторных запусков.
