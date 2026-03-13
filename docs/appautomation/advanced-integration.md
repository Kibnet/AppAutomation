# AppAutomation Advanced Integration

Этот документ покрывает cases, которые выходят за пределы quickstart.

## 1. Nested solution и repo-root discovery

Если solution лежит под `src/`, не встраивайте knowledge о layout в reusable packages. Держите это в `*.AppAutomation.TestHost`.

Используйте `AvaloniaDesktopAppDescriptor` + `AvaloniaDesktopLaunchHost`:

```csharp
private static readonly AvaloniaDesktopAppDescriptor DesktopApp = new(
    solutionFileNames: ["MyApp.sln"],
    desktopProjectRelativePaths: ["src\\MyApp.Desktop\\MyApp.Desktop.csproj"],
    desktopTargetFramework: "net8.0",
    executableName: "MyApp.Desktop.exe");
```

## 2. Repeated headless launches

Если AUT держит static state, используйте:

- `BeforeLaunchAsync` для reset;
- `CreateMainWindowAsync` для async bootstrap;
- `TemporaryDirectory` для isolated files.

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

## 3. Isolated settings и temp files

`TemporaryDirectory` нужен для:

- temporary settings json;
- transient database/filesystem state;
- per-run artifacts.

Пример:

```csharp
using var temp = TemporaryDirectory.Create("MyAppAutomation");
var settingsPath = temp.WriteTextFile("settings\\Settings.json", json);
```

## 4. Composite controls

Если виджет не укладывается в built-in `UiControlType`, не переписывайте runtime resolver целиком.

Правильный порядок:

1. попробовать решить сценарий упрощением данных;
2. если нельзя, использовать `WithAdapters(...)`;
3. если сценарий похож на search + select, сначала использовать `WithSearchPicker(...)`.

Пример built-in path:

```csharp
var resolver = new FlaUiControlResolver(window, conditionFactory)
    .WithSearchPicker(
        "ServerPicker",
        SearchPickerParts.ByAutomationIds(
            "ServerPickerInput",
            "ServerPickerResults",
            applyButtonAutomationId: "ServerPickerApply"));
```

Пример page property:

```csharp
private static UiControlDefinition ServerPickerDefinition { get; } =
    new("ServerPicker", UiControlType.AutomationElement, "ServerPicker", UiLocatorKind.AutomationId, FallbackToName: false);

public ISearchPickerControl ServerPicker => Resolve<ISearchPickerControl>(ServerPickerDefinition);
```

## 5. Internal feeds и package-source strategy

Если в организации запрещён прямой `nuget.org`:

- настройте internal mirror в `NuGet.Config`;
- держите `PackageReference` на `AppAutomation.*`;
- не переходите на source dependency только потому, что feed не настроен.

`appautomation doctor` должен видеть валидный `NuGet.Config` до начала integration work.

## 6. Readiness и retry

Используйте framework helpers:

- `WaitUntil(...)`
- `WaitUntilAsync(...)`
- `RetryUntil(...)`

Но не подменяйте ими плохие selectors. Если control стабильно ищется только через retry, сначала чините `AutomationId`.

## 7. Когда headless не должен покрывать всё

Если приложение трудно ресетить in-process, нормальная стратегия такая:

- `Headless` покрывает smoke + critical deterministic flows;
- desktop-only unstable paths остаются в `FlaUI`;
- after every added headless scenario re-check repeated launch stability.
