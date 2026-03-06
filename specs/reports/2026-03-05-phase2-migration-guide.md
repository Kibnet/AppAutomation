# Phase 2 Migration Guide

Дата: 2026-03-06

## 1. Что изменилось

- Launch-контракты вынесены в `EasyUse.Session.Contracts`.
- Общий TUnit-core (`UiAssert`, `UiWait*`, `UiTestBase`) вынесен в `EasyUse.TUnit.Core`.
- Legacy проекты `FlaUI.EasyUse.TUnit` и `Avalonia.Headless.EasyUse.TUnit` удалены.
- `Avalonia.Headless` runtime session API использует `Avalonia.Headless.EasyUse.Session`.

## 2. Что нужно поменять в потребителе

1. Обновить project references:
   - добавить `EasyUse.Session.Contracts`
   - добавить `EasyUse.TUnit.Core`
   - убрать `FlaUI.EasyUse.TUnit`/`Avalonia.Headless.EasyUse.TUnit`
2. Обновить `using`:
   - `FlaUI.EasyUse.TUnit` -> `EasyUse.TUnit.Core`
   - `FlaUI.EasyUse.Session` launch options -> `EasyUse.Session.Contracts`
3. Перевести базовый тест-класс:
   - было: `DesktopUiTestBase<TPage>`
   - стало: `UiTestBase<TSession, TPage>`
   - добавить runtime adapter, реализующий `IUiTestSession`.
4. Для headless runtime:
   - использовать `Avalonia.Headless.EasyUse.Session.HeadlessRuntime` в session hooks.

## 3. Пример миграции fixture

```csharp
public sealed class MainWindowFlaUiRuntimeTests : MainWindowScenariosBase<FlaUiRuntimeSession>
{
    protected override DesktopProjectLaunchOptions CreateLaunchOptions() => new()
    {
        SolutionFileName = "DotnetDebug.sln",
        ProjectRelativePath = Path.Combine("src", "DotnetDebug.Avalonia", "DotnetDebug.Avalonia.csproj"),
        BuildConfiguration = "Debug",
        TargetFramework = "net9.0"
    };

    protected override FlaUiRuntimeSession LaunchSession(DesktopProjectLaunchOptions options)
        => new(DesktopAppSession.LaunchFromProject(options));

    protected override MainWindowPage CreatePage(FlaUiRuntimeSession session)
        => new(session.Inner.MainWindow, session.Inner.ConditionFactory);
}
```

## 4. Проверка после миграции

- `dotnet build DotnetDebug.sln`
- `dotnet test tests/DotnetDebug.Tests/DotnetDebug.Tests.csproj`
- `dotnet test tests/DotnetDebug.UiTests.Avalonia.Headless/DotnetDebug.UiTests.Avalonia.Headless.csproj`
- `dotnet test tests/DotnetDebug.UiTests.FlaUI.EasyUse/DotnetDebug.UiTests.FlaUI.EasyUse.csproj`
- `pwsh -File tests/Verify-UiScenarioDiscoveryParity.ps1`

## 5. Breaking notes

- Старые TUnit namespace/проекты удалены, обратной совместимости через thin wrapper нет.
- Миграция должна выполняться атомарно: references + using + fixture base.
