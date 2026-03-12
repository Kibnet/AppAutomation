# AppAutomation

[![NuGet Version](https://img.shields.io/nuget/v/AppAutomation.Abstractions?label=NuGet%20(AppAutomation.Abstractions))](https://www.nuget.org/packages/AppAutomation.Abstractions)

`AppAutomation` это .NET framework для UI automation desktop-приложений. Основной сценарий использования такой:

1. вы добавляете в свой Avalonia solution несколько test-проектов;
2. один раз описываете page objects и общие сценарии;
3. запускаете те же самые тесты в `Headless` и в `FlaUI`.

Итоговая цель этого `README`: с нуля прийти к структуре, где shared tests лежат в одном проекте и выполняются из двух runtime-обёрток.

## Что должно получиться в конце

- `MyApp.UiTests.Authoring` содержит page objects и общие тестовые сценарии;
- `MyApp.UiTests.Headless` запускает эти сценарии внутри `Avalonia Headless`;
- `MyApp.UiTests.FlaUI` запускает те же сценарии через `FlaUI`;
- `MyApp.AppAutomation.TestHost` хранит repo-specific launch/bootstrap логику;
- в приложении есть стабильные `AutomationId`;
- вы пишете тест один раз и запускаете его в двух режимах.

## Требования

- `.NET SDK 10`;
- `Windows` для `FlaUI`;
- Avalonia-приложение, у которого есть `MainWindow` или другой root window для тестов.

## Итоговая структура solution

Рекомендуемая структура consumer-репозитория:

```text
src/
  MyApp.Desktop/
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

Минимальный набор для dual-runtime сценария:

- `MyApp.UiTests.Authoring`
- `MyApp.UiTests.Headless`
- `MyApp.UiTests.FlaUI`
- `MyApp.AppAutomation.TestHost`

## Шаг 1. Создайте проекты

Ниже пример с нуля. Команды можно запускать из корня вашего репозитория:

```powershell
dotnet new classlib -n MyApp.UiTests.Authoring -o tests/MyApp.UiTests.Authoring
dotnet new classlib -n MyApp.UiTests.Headless -o tests/MyApp.UiTests.Headless
dotnet new classlib -n MyApp.UiTests.FlaUI -o tests/MyApp.UiTests.FlaUI
dotnet new classlib -n MyApp.AppAutomation.TestHost -o tests/MyApp.AppAutomation.TestHost

dotnet sln add tests/MyApp.UiTests.Authoring/MyApp.UiTests.Authoring.csproj
dotnet sln add tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj
dotnet sln add tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj
dotnet sln add tests/MyApp.AppAutomation.TestHost/MyApp.AppAutomation.TestHost.csproj
```

После этого удалите из новых проектов шаблонные `Class1.cs`.

## Шаг 2. Подключите NuGet и ссылки между проектами

Ниже самый прямой стартовый вариант. Во всех примерах замените `x.y.z` на одну и ту же версию `AppAutomation.*`.

### `tests/MyApp.UiTests.Authoring/MyApp.UiTests.Authoring.csproj`

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

### `tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AppAutomation.Abstractions" Version="x.y.z" />
    <PackageReference Include="AppAutomation.Avalonia.Headless" Version="x.y.z" />
    <PackageReference Include="AppAutomation.TUnit" Version="x.y.z" />
    <PackageReference Include="Avalonia.Headless" Version="11.3.7" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.7" />
    <PackageReference Include="TUnit" Version="1.12.111" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyApp.UiTests.Authoring\MyApp.UiTests.Authoring.csproj" />
    <ProjectReference Include="..\MyApp.AppAutomation.TestHost\MyApp.AppAutomation.TestHost.csproj" />
    <ProjectReference Include="..\..\src\MyApp.Desktop\MyApp.Desktop.csproj" />
  </ItemGroup>
</Project>
```

### `tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AppAutomation.Abstractions" Version="x.y.z" />
    <PackageReference Include="AppAutomation.FlaUI" Version="x.y.z" />
    <PackageReference Include="AppAutomation.TUnit" Version="x.y.z" />
    <PackageReference Include="FlaUI.Core" Version="5.0.0" />
    <PackageReference Include="FlaUI.UIA3" Version="5.0.0" />
    <PackageReference Include="TUnit" Version="1.12.111" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyApp.UiTests.Authoring\MyApp.UiTests.Authoring.csproj" />
    <ProjectReference Include="..\MyApp.AppAutomation.TestHost\MyApp.AppAutomation.TestHost.csproj" />
  </ItemGroup>
</Project>
```

### `tests/MyApp.AppAutomation.TestHost/MyApp.AppAutomation.TestHost.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AppAutomation.Session.Contracts" Version="x.y.z" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MyApp.Desktop\MyApp.Desktop.csproj" />
  </ItemGroup>
</Project>
```

`AppAutomation.Authoring` подключается обычным `PackageReference`. Для NuGet-пакета не нужен отдельный `Analyzer`-режим через `OutputItemType="Analyzer"`.

## Шаг 3. Проставьте `AutomationId` в приложении

Фреймворк опирается на стабильные селекторы. Для Avalonia это обычно:

```xml
<Window
    xmlns="https://github.com/avaloniaui"
    xmlns:automation="clr-namespace:Avalonia.Automation;assembly=Avalonia.Controls">

  <TabControl automation:AutomationProperties.AutomationId="MainTabs">
    <TabItem Header="Login"
             automation:AutomationProperties.AutomationId="LoginTabItem">
      <StackPanel>
        <TextBox automation:AutomationProperties.AutomationId="UserNameInput" />
        <Button automation:AutomationProperties.AutomationId="LoginButton"
                Content="Login" />
        <TextBlock automation:AutomationProperties.AutomationId="StatusLabel" />
      </StackPanel>
    </TabItem>
  </TabControl>
</Window>
```

Правила:

- ставьте `AutomationId` на controls, с которыми реально будет работать тест;
- `AutomationId` должен быть стабильным, а не привязанным к видимому тексту;
- сначала размечайте критичный пользовательский путь, потом расширяйте покрытие;
- сегодня page object layer описывается вручную через `[UiControl(...)]`, автоматического scaffold tool в релизном flow пока нет.

## Шаг 4. Создайте `TestHost`

`TestHost` хранит всё, что относится к вашему репозиторию: как создать окно для `Headless`, где лежит `.exe`, какой `WorkingDirectory`, какие аргументы и переменные окружения нужны приложению.

`tests/MyApp.AppAutomation.TestHost/MyAppLaunchHost.cs`:

```csharp
using AppAutomation.Session.Contracts;
using MyApp.Desktop;

namespace MyApp.AppAutomation.TestHost;

public static class MyAppLaunchHost
{
    public static HeadlessAppLaunchOptions CreateHeadlessLaunchOptions()
    {
        return new HeadlessAppLaunchOptions
        {
            CreateMainWindow = static () => new MainWindow()
        };
    }

    public static DesktopAppLaunchOptions CreateDesktopLaunchOptions(string configuration = "Debug")
    {
        var executablePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyApp.Desktop",
                "bin",
                configuration,
                "net10.0",
                "MyApp.Desktop.exe"));

        return new DesktopAppLaunchOptions
        {
            ExecutablePath = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            MainWindowTimeout = TimeSpan.FromSeconds(20),
            PollInterval = TimeSpan.FromMilliseconds(200)
        };
    }
}
```

Если приложению нужны startup args или env vars, добавьте их здесь же:

```csharp
return new DesktopAppLaunchOptions
{
    ExecutablePath = executablePath,
    WorkingDirectory = Path.GetDirectoryName(executablePath),
    Arguments = ["--automation"],
    EnvironmentVariables = new Dictionary<string, string?>
    {
        ["MYAPP_ENV"] = "Test"
    }
};
```

Если headless-режим требует сброса static-state или async bootstrap, используйте `BeforeLaunchAsync` и `CreateMainWindowAsync`.

## Шаг 5. Опишите page object в `Authoring`

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

После сборки `AppAutomation.Authoring` сгенерирует strongly typed свойства для этих контролов.

## Шаг 6. Напишите общие тесты один раз

Именно здесь лежит ваш общий сценарий, который потом выполняется и в `Headless`, и в `FlaUI`.

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
    [NotInParallel(DesktopUiConstraint)]
    public async Task Login_flow_is_reachable()
    {
        Page
            .SelectTabItem(static page => page.LoginTabItem)
            .EnterText(static page => page.UserNameInput, "alice")
            .ClickButton(static page => page.LoginButton)
            .WaitUntilNameContains(static page => page.StatusLabel, "alice");

        await Assert.That(Page.LoginTabItem.IsSelected).IsEqualTo(true);
    }
}
```

Это и есть главный принцип фреймворка: сценарии общие, runtime-specific только запуск и resolver.

## Шаг 7. Подключите `Headless`

Сначала создайте TUnit hooks, которые поднимут `Avalonia.Headless` session.

`tests/MyApp.UiTests.Headless/Infrastructure/HeadlessSessionHooks.cs`:

```csharp
using Avalonia.Headless;
using AppAutomation.Avalonia.Headless.Session;
using MyApp.Desktop;
using TUnit.Core;

namespace MyApp.UiTests.Headless.Infrastructure;

public static class HeadlessSessionHooks
{
    private static HeadlessUnitTestSession? _session;

    [Before(TestSession)]
    public static void SetupSession()
    {
        _session = HeadlessUnitTestSession.StartNew(typeof(App));
        HeadlessRuntime.SetSession(_session);
    }

    [After(TestSession)]
    public static void CleanupSession()
    {
        HeadlessRuntime.SetSession(null);
        _session?.Dispose();
        _session = null;
    }
}
```

Потом добавьте runtime-обёртку:

`tests/MyApp.UiTests.Headless/Tests/MainWindowHeadlessTests.cs`:

```csharp
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using MyApp.AppAutomation.TestHost;
using MyApp.UiTests.Authoring.Pages;
using MyApp.UiTests.Authoring.Tests;
using TUnit.Core;

namespace MyApp.UiTests.Headless.Tests;

[InheritsTests]
public sealed class MainWindowHeadlessTests
    : MainWindowScenariosBase<MainWindowHeadlessTests.HeadlessRuntimeSession>
{
    protected override HeadlessRuntimeSession LaunchSession()
    {
        return new HeadlessRuntimeSession(
            DesktopAppSession.Launch(MyAppLaunchHost.CreateHeadlessLaunchOptions()));
    }

    protected override MainWindowPage CreatePage(HeadlessRuntimeSession session)
    {
        return new MainWindowPage(new HeadlessControlResolver(session.Inner.MainWindow));
    }

    public sealed class HeadlessRuntimeSession : IUiTestSession
    {
        public HeadlessRuntimeSession(DesktopAppSession inner)
        {
            Inner = inner;
        }

        public DesktopAppSession Inner { get; }

        public void Dispose()
        {
            Inner.Dispose();
        }
    }
}
```

## Шаг 8. Подключите `FlaUI`

`tests/MyApp.UiTests.FlaUI/Tests/MainWindowFlaUiTests.cs`:

```csharp
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using AppAutomation.TUnit;
using MyApp.AppAutomation.TestHost;
using MyApp.UiTests.Authoring.Pages;
using MyApp.UiTests.Authoring.Tests;
using TUnit.Core;

namespace MyApp.UiTests.FlaUI.Tests;

[InheritsTests]
public sealed class MainWindowFlaUiTests
    : MainWindowScenariosBase<MainWindowFlaUiTests.FlaUiRuntimeSession>
{
    protected override FlaUiRuntimeSession LaunchSession()
    {
        return new FlaUiRuntimeSession(
            DesktopAppSession.Launch(MyAppLaunchHost.CreateDesktopLaunchOptions()));
    }

    protected override MainWindowPage CreatePage(FlaUiRuntimeSession session)
    {
        return new MainWindowPage(
            new FlaUiControlResolver(session.Inner.MainWindow, session.Inner.ConditionFactory));
    }

    public sealed class FlaUiRuntimeSession : IUiTestSession
    {
        public FlaUiRuntimeSession(DesktopAppSession inner)
        {
            Inner = inner;
        }

        public DesktopAppSession Inner { get; }

        public void Dispose()
        {
            Inner.Dispose();
        }
    }
}
```

На этом этапе у вас уже есть один общий тестовый класс и две тонкие runtime-обёртки.

## Шаг 9. Соберите и запустите

Из корня consumer-репозитория:

```powershell
dotnet restore
dotnet build
dotnet test tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj -c Debug
dotnet test tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj -c Debug
```

Если `FlaUI` не поднимает приложение:

- проверьте путь к `.exe` в `MyAppLaunchHost.CreateDesktopLaunchOptions`;
- проверьте, что desktop-приложение реально собрано под нужный `TargetFramework`;
- проверьте `WorkingDirectory`, startup args и env vars;
- убедитесь, что у нужных элементов есть `AutomationId`.

## Что считать правильным результатом

После выполнения шагов выше у вас должно быть именно это:

- page objects и сценарии живут только в `MyApp.UiTests.Authoring`;
- `Headless` и `FlaUI` не дублируют тестовую логику, а только запускают один и тот же `MainWindowScenariosBase<TSession>`;
- один и тот же сценарий можно вызвать двумя командами: `dotnet test tests/MyApp.UiTests.Headless/...` и `dotnet test tests/MyApp.UiTests.FlaUI/...`.

## Живой пример в этом репозитории

Если нужен working reference, смотри готовую реализацию в этом repo:

- [sample/DotnetDebug.AppAutomation.Authoring](sample/DotnetDebug.AppAutomation.Authoring)
- [sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests](sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests)
- [sample/DotnetDebug.AppAutomation.FlaUI.Tests](sample/DotnetDebug.AppAutomation.FlaUI.Tests)
- [sample/DotnetDebug.AppAutomation.TestHost](sample/DotnetDebug.AppAutomation.TestHost)
- [sample/DotnetDebug.Avalonia](sample/DotnetDebug.Avalonia)

## Если нужен следующий уровень

- Более подробный старт: [docs/appautomation/quickstart.md](docs/appautomation/quickstart.md)
- Схема проектов: [docs/appautomation/project-topology.md](docs/appautomation/project-topology.md)
- Сложные интеграции и troubleshooting: [docs/appautomation/advanced-integration.md](docs/appautomation/advanced-integration.md)
- Сборка и публикация NuGet-пакетов: [docs/appautomation/publishing.md](docs/appautomation/publishing.md)
