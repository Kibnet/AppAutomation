# AppAutomation Quickstart

Этот quickstart показывает минимальный working setup для внешнего solution, который хочет подключить `AppAutomation` через NuGet и начать писать UI-тесты.

Для nested solution, repo-specific bootstrap, stateful headless apps и troubleshooting смотрите [advanced-integration.md](advanced-integration.md).

## 1. Рекомендуемая topology

Минимальный practical layout:

```text
src/
  MyApp/
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/          # optional, Windows only
  MyApp.AppAutomation.TestHost/ # optional, repo-specific launch/bootstrap
```

Обязательный минимум:

- `MyApp.UiTests.Authoring`
- один runtime-specific test project: `MyApp.UiTests.Headless` или `MyApp.UiTests.FlaUI`

## 2. Package matrix

| Проект | Пакеты |
| --- | --- |
| `MyApp.UiTests.Authoring` | `AppAutomation.Abstractions`, `AppAutomation.Authoring`, `AppAutomation.TUnit`, `TUnit.Assertions`, `TUnit.Core` |
| `MyApp.UiTests.Headless` | `AppAutomation.Abstractions`, `AppAutomation.Avalonia.Headless`, `AppAutomation.TUnit`, `TUnit` + `ProjectReference` на `MyApp.UiTests.Authoring` |
| `MyApp.UiTests.FlaUI` | `AppAutomation.Abstractions`, `AppAutomation.FlaUI`, `AppAutomation.TUnit`, `TUnit` + `ProjectReference` на `MyApp.UiTests.Authoring` |
| `MyApp.AppAutomation.TestHost` | обычно `AppAutomation.Session.Contracts`, иногда runtime package, если нужен repo-specific bootstrap |

Все `AppAutomation.*` пакеты держите в одной версии.

## 3. Создайте authoring project

`tests/MyApp.UiTests.Authoring/MyApp.UiTests.Authoring.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AppAutomation.Abstractions" Version="x.y.z" />
    <PackageReference Include="AppAutomation.Authoring" Version="x.y.z" />
    <PackageReference Include="AppAutomation.TUnit" Version="x.y.z" />
    <PackageReference Include="TUnit.Assertions" Version="1.12.111" />
    <PackageReference Include="TUnit.Core" Version="1.12.111" />
  </ItemGroup>
</Project>
```

`AppAutomation.Authoring` подключается обычным `PackageReference`. Дополнительный `OutputItemType="Analyzer"` для NuGet-пакета не нужен.

## 4. Опишите page object

`tests/MyApp.UiTests.Authoring/Pages/MainWindowPage.cs`:

```csharp
using AppAutomation.Abstractions;

namespace MyApp.UiTests.Authoring.Pages;

[UiControl("MainTabs", UiControlType.Tab, "MainTabs")]
[UiControl("LoginTabItem", UiControlType.TabItem, "LoginTabItem")]
[UiControl("UserNameInput", UiControlType.TextBox, "UserNameInput")]
[UiControl("LoginButton", UiControlType.Button, "LoginButton")]
[UiControl("StatusLabel", UiControlType.Label, "StatusLabel")]
public sealed partial class MainWindowPage : UiPage
{
    public MainWindowPage(IUiControlResolver resolver) : base(resolver)
    {
    }
}
```

После сборки generator создаст:

- `MainWindowPageDefinitions`
- strongly typed properties `MainTabs`, `LoginTabItem`, `UserNameInput`, `LoginButton`, `StatusLabel`
- generated locator manifest provider в namespace `<AssemblyName>.Generated`

Для production-grade navigation предпочитайте stable controls с `AutomationId`, например `LoginTabItem`, а не выбор tab-а только по тексту header-а.

## 5. Вынесите shared scenarios

`tests/MyApp.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`:

```csharp
using AppAutomation.Abstractions;
using AppAutomation.TUnit;
using MyApp.UiTests.Authoring.Pages;
using TUnit.Assertions;
using TUnit.Core;

namespace MyApp.UiTests.Authoring.Tests;

public abstract class MainWindowScenariosBase<TSession> : UiTestBase<TSession, MainWindowPage>
    where TSession : class, IUiTestSession
{
    [Test]
    public async Task Login_flow_is_reachable()
    {
        Page
            .SelectTabItem(static candidate => candidate.LoginTabItem)
            .EnterText(static candidate => candidate.UserNameInput, "alice")
            .ClickButton(static candidate => candidate.LoginButton)
            .WaitUntilNameContains(static candidate => candidate.StatusLabel, "alice");

        await Assert.That(Page.LoginTabItem.IsSelected).IsEqualTo(true);
    }
}
```

Именно этот проект должен быть owner-ом page objects и shared scenarios. Не дублируйте их через `Compile Include` в runtime test projects.

## 6. Создайте headless runtime test project

`tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AppAutomation.Abstractions" Version="x.y.z" />
    <PackageReference Include="AppAutomation.Avalonia.Headless" Version="x.y.z" />
    <PackageReference Include="AppAutomation.TUnit" Version="x.y.z" />
    <PackageReference Include="TUnit" Version="1.12.111" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\\MyApp.UiTests.Authoring\\MyApp.UiTests.Authoring.csproj" />
    <ProjectReference Include="..\\MyApp.AppAutomation.TestHost\\MyApp.AppAutomation.TestHost.csproj" />
  </ItemGroup>
</Project>
```

Минимальный runtime wrapper:

```csharp
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using MyApp.AppAutomation.TestHost;
using MyApp.UiTests.Authoring.Pages;
using MyApp.UiTests.Authoring.Tests;
using TUnit.Core;

namespace MyApp.UiTests.Headless;

[InheritsTests]
public sealed class MainWindowHeadlessTests : MainWindowScenariosBase<MainWindowHeadlessTests.HeadlessSession>
{
    protected override HeadlessSession LaunchSession() => HeadlessSession.Start();

    protected override MainWindowPage CreatePage(HeadlessSession session)
    {
        return new MainWindowPage(new HeadlessControlResolver(session.Session.MainWindow));
    }

    public sealed class HeadlessSession : IUiTestSession
    {
        private HeadlessSession(DesktopAppSession session)
        {
            Session = session;
        }

        public DesktopAppSession Session { get; }

        public static HeadlessSession Start()
        {
            return new HeadlessSession(DesktopAppSession.Launch(MyAppLaunchHost.CreateHeadlessLaunchOptions()));
        }

        public void Dispose()
        {
            Session.Dispose();
        }
    }
}
```

## 7. Добавьте Windows runtime при необходимости

`MyApp.UiTests.FlaUI` нужен, если вы тестируете настоящий desktop executable под Windows.

Базовый набор:

```xml
<PackageReference Include="AppAutomation.Abstractions" Version="x.y.z" />
<PackageReference Include="AppAutomation.FlaUI" Version="x.y.z" />
<PackageReference Include="AppAutomation.TUnit" Version="x.y.z" />
<PackageReference Include="TUnit" Version="1.12.111" />
```

и `ProjectReference` на `MyApp.UiTests.Authoring` и `MyApp.AppAutomation.TestHost`.

## 8. Когда нужен repo-specific TestHost

Если ваш runtime test project должен:

- искать `.sln` или repo root;
- собирать AUT перед запуском;
- вычислять пути в `bin/<Configuration>/<TFM>`;
- формировать `DesktopAppLaunchOptions` / `HeadlessAppLaunchOptions`;
- подготавливать temp dirs, test data или isolated settings;

выносите это в отдельный repo-only project, аналогичный `src/DotnetDebug.AppAutomation.TestHost`.

### Пример desktop launch options с аргументами и env vars

```csharp
using AppAutomation.Session.Contracts;

return new DesktopAppLaunchOptions
{
    ExecutablePath = executablePath,
    WorkingDirectory = appWorkingDirectory,
    Arguments = ["--automation", "--profile", "smoke"],
    EnvironmentVariables = new Dictionary<string, string?>
    {
        ["MYAPP_ENV"] = "Test",
        ["MYAPP_SETTINGS_PATH"] = settingsPath
    }
};
```

### Пример advanced headless bootstrap

```csharp
using AppAutomation.Session.Contracts;

return new HeadlessAppLaunchOptions
{
    BeforeLaunchAsync = async cancellationToken =>
    {
        await ResetStaticStateAsync(cancellationToken);
        PrepareIsolatedFiles();
    },
    CreateMainWindowAsync = cancellationToken =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<object>(MyAppBootstrap.CreateMainWindowForAutomation());
    }
};
```

`BeforeLaunchAsync` используйте для repo/app-specific reset logic. `CreateMainWindowAsync` используйте, когда создание окна требует async/bootstrap шага. Для простых приложений достаточно старого `CreateMainWindow`.

## 9. Readiness и retry helpers

`UiTestBase` теперь даёт framework-level helpers для readiness logic:

- `WaitUntil(...)`
- `WaitUntil<T>(...)`
- `WaitUntilAsync<T>(...)`
- `RetryUntil(...)`

Пример:

```csharp
protected override MainWindowPage CreatePage(HeadlessSession session)
{
    var page = new MainWindowPage(new HeadlessControlResolver(session.Session.MainWindow));

    WaitUntil(
        () => page.LoginButton.IsEnabled,
        timeout: TimeSpan.FromSeconds(10),
        because: "Main window should become interactive before tests continue.");

    return page;
}
```

Используйте эти helpers для readiness и transient transitions. Если без retry нельзя даже стабильно найти control, сначала улучшайте locator strategy и `AutomationId`.

## 10. Запуск

```powershell
dotnet restore
dotnet build
dotnet test --project tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj
dotnet test --project tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj
```

Если хотите проверить именно package install story, используйте локальный smoke path из этого репозитория:

```powershell
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```
