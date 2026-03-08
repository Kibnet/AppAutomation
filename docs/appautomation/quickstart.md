# AppAutomation Quickstart

Этот quickstart показывает минимальный working setup для внешнего solution, который хочет писать UI-тесты на `AppAutomation`.

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
- strongly typed properties `UserNameInput`, `LoginButton`, `StatusLabel`
- generated locator manifest provider в namespace `<AssemblyName>.Generated`

## 5. Вынесите shared scenarios

`tests/MyApp.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`:

```csharp
using MyApp.UiTests.Authoring.Pages;
using AppAutomation.TUnit;
using TUnit.Assertions;
using TUnit.Core;

namespace MyApp.UiTests.Authoring.Tests;

public abstract class MainWindowScenariosBase<TSession> : UiTestBase<TSession, MainWindowPage>
    where TSession : class, IUiTestSession
{
    [Test]
    public async Task Login_button_is_reachable()
    {
        await Assert.That(Page.LoginButton.AutomationId).IsEqualTo("LoginButton");
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
    <ProjectReference Include="..\MyApp.UiTests.Authoring\MyApp.UiTests.Authoring.csproj" />
  </ItemGroup>
</Project>
```

Минимальный runtime wrapper:

```csharp
using MyApp.UiTests.Authoring.Pages;
using MyApp.UiTests.Authoring.Tests;
using AppAutomation.Abstractions;
using AppAutomation.TUnit;
using TUnit.Core;

namespace MyApp.UiTests.Headless;

[InheritsTests]
public sealed class MainWindowHeadlessTests : MainWindowScenariosBase<MainWindowHeadlessTests.FakeSession>
{
    protected override FakeSession LaunchSession() => new();

    protected override MainWindowPage CreatePage(FakeSession session)
    {
        return new MainWindowPage(new FakeResolver());
    }

    public sealed class FakeSession : IUiTestSession
    {
        public void Dispose()
        {
        }
    }

    private sealed class FakeResolver : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("headless");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            throw new NotImplementedException();
        }
    }
}
```

В реальном solution вместо `FakeResolver` вы передаёте runtime-specific resolver, например `HeadlessControlResolver`.

## 7. Добавьте Windows runtime при необходимости

`MyApp.UiTests.FlaUI` нужен, если вы тестируете настоящий desktop executable под Windows.

Базовый набор:

```xml
<PackageReference Include="AppAutomation.Abstractions" Version="x.y.z" />
<PackageReference Include="AppAutomation.FlaUI" Version="x.y.z" />
<PackageReference Include="AppAutomation.TUnit" Version="x.y.z" />
<PackageReference Include="TUnit" Version="1.12.111" />
```

и `ProjectReference` на `MyApp.UiTests.Authoring`.

## 8. Когда нужен repo-specific TestHost

Если ваш runtime test project должен:

- искать `.sln`;
- собирать AUT перед запуском;
- вычислять пути в `bin/<Configuration>/<TFM>`;
- формировать `DesktopAppLaunchOptions` / `HeadlessAppLaunchOptions`;

выносите это в отдельный repo-only project, аналогичный `src/DotnetDebug.AppAutomation.TestHost`.

Это не часть reusable framework contract. Это ваша локальная инфраструктура solution.

## 9. Запуск

```powershell
dotnet restore
dotnet build
dotnet test tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj
dotnet test tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj
```

Если хотите проверить именно package install story, используйте локальный smoke path из этого репозитория:

```powershell
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```
