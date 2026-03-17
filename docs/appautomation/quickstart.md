# AppAutomation Quickstart

**English** | [Русский](#русская-версия)

This quickstart describes an opinionated consumer flow from scratch for an existing Avalonia application.

## 1. Don't start with tests

First, prepare deterministic prerequisites:

- test account / auth path;
- test data / permissions path;
- isolated settings file;
- fixed startup screen;
- disabled update/background jobs.

If this is not stabilized, do not proceed to page objects.

## 2. Install template and tool

```powershell
dotnet new install AppAutomation.Templates::2.1.0
dotnet tool install --tool-path .\.tools AppAutomation.Tooling --version 2.1.0
```

## 3. Generate canonical topology

```powershell
dotnet new appauto-avalonia --name MyApp --AppAutomationVersion 2.1.0
```

The template will create:

```text
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

## 4. Check repo via doctor

```powershell
.\.tools\appautomation doctor --repo-root .
```

If `doctor` warns about source dependency, fix it before starting authoring.

## 5. Complete `TestHost`

File:

```text
tests/MyApp.AppAutomation.TestHost/MyAppAppLaunchHost.cs
```

Use built-in helpers:

- `AvaloniaDesktopLaunchHost`
- `AvaloniaHeadlessLaunchHost`
- `TemporaryDirectory`

Typical desktop path:

```csharp
return AvaloniaDesktopLaunchHost.CreateLaunchOptions(
    desktopAppDescriptor,
    new AvaloniaDesktopLaunchOptions
    {
        BuildConfiguration = BuildConfigurationDefaults.ForAssembly(typeof(MyAppAppLaunchHost).Assembly)
    });
```

Typical headless path:

```csharp
return AvaloniaHeadlessLaunchHost.Create(
    static () => MyAppBootstrap.CreateMainWindow());
```

## 6. Set up minimum `AutomationId` contract

The first iteration should cover only controls from the critical smoke path:

- window root;
- main tabs / navigation anchors;
- important text boxes;
- important buttons;
- labels/results;
- child anchors inside composite widgets.

Don't try to mark up the entire application at once.

## 7. Describe page object

Simple controls are described via `[UiControl(...)]`:

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

## 8. For composite controls, first use the built-in adapter path

If a control doesn't fit into simple `[UiControl(...)]`, don't rewrite the resolver entirely. First use:

- `IUiControlResolver.WithAdapters(...)`
- `IUiControlResolver.WithSearchPicker(...)`
- `ISearchPickerControl`

Example:

```csharp
var resolver = new HeadlessControlResolver(session.Inner.MainWindow)
    .WithSearchPicker(
        "HistoryOperationPicker",
        SearchPickerParts.ByAutomationIds(
            "HistoryFilterInput",
            "OperationCombo",
            applyButtonAutomationId: "ApplyFilterButton"));
```

## 9. Shared scenarios are written only in `Authoring`

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

Runtime projects should not duplicate these methods.

## 10. Runtime projects remain thin wrappers

`Headless` and `FlaUI` should only:

- start the runtime session;
- create runtime resolver;
- provide shared page object;
- inherit tests via `[InheritsTests]`.

## 11. First stabilize `Headless`

```powershell
dotnet test tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj -c Debug
```

When `Headless` is stable, enable desktop runtime:

```powershell
dotnet test tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj -c Debug
```

## 12. What to do if integration grows again

Stop and check:

- whether bootstrap code has moved from `TestHost` to test projects;
- whether you're trying to automate a secondary control instead of simplifying test data;
- whether you've switched to source dependency;
- whether tests are duplicated between runtime projects.

More details: [advanced-integration.md](advanced-integration.md)

---

<a id="русская-версия"></a>

## Русская версия

[English](#appautomation-quickstart) | **Русский**

Этот quickstart описывает opinionated consumer flow с нуля для существующего Avalonia-приложения.

## 1. Не начинайте с тестов

Сначала подготовьте deterministic prerequisites:

- test account / auth path;
- test data / permissions path;
- isolated settings file;
- фиксированный startup screen;
- отключённые update/background jobs.

Если это не стабилизировано, не переходите к page objects.

## 2. Установите template и tool

```powershell
dotnet new install AppAutomation.Templates::2.1.0
dotnet tool install --tool-path .\.tools AppAutomation.Tooling --version 2.1.0
```

## 3. Сгенерируйте canonical topology

```powershell
dotnet new appauto-avalonia --name MyApp --AppAutomationVersion 2.1.0
```

Шаблон создаст:

```text
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

## 4. Проверьте repo через doctor

```powershell
.\.tools\appautomation doctor --repo-root .
```

Если `doctor` предупреждает про source dependency, исправьте это до начала authoring.

## 5. Допишите `TestHost`

Файл:

```text
tests/MyApp.AppAutomation.TestHost/MyAppAppLaunchHost.cs
```

Используйте built-in helpers:

- `AvaloniaDesktopLaunchHost`
- `AvaloniaHeadlessLaunchHost`
- `TemporaryDirectory`

Типовой desktop path:

```csharp
return AvaloniaDesktopLaunchHost.CreateLaunchOptions(
    desktopAppDescriptor,
    new AvaloniaDesktopLaunchOptions
    {
        BuildConfiguration = BuildConfigurationDefaults.ForAssembly(typeof(MyAppAppLaunchHost).Assembly)
    });
```

Типовой headless path:

```csharp
return AvaloniaHeadlessLaunchHost.Create(
    static () => MyAppBootstrap.CreateMainWindow());
```

## 6. Проставьте minimum `AutomationId` contract

Первая итерация должна покрыть только controls из critical smoke path:

- window root;
- main tabs / navigation anchors;
- важные text boxes;
- важные buttons;
- labels/results;
- child anchors inside composite widgets.

Не пытайтесь сразу размечать всё приложение.

## 7. Опишите page object

Простые controls описываются через `[UiControl(...)]`:

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

## 8. Для composite controls сначала используйте built-in adapter path

Если control не укладывается в простые `[UiControl(...)]`, не переписывайте resolver целиком. Сначала используйте:

- `IUiControlResolver.WithAdapters(...)`
- `IUiControlResolver.WithSearchPicker(...)`
- `ISearchPickerControl`

Пример:

```csharp
var resolver = new HeadlessControlResolver(session.Inner.MainWindow)
    .WithSearchPicker(
        "HistoryOperationPicker",
        SearchPickerParts.ByAutomationIds(
            "HistoryFilterInput",
            "OperationCombo",
            applyButtonAutomationId: "ApplyFilterButton"));
```

## 9. Shared scenarios пишутся только в `Authoring`

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

Runtime projects не должны дублировать эти методы.

## 10. Runtime projects остаются thin wrappers

`Headless` и `FlaUI` должны только:

- поднять runtime session;
- создать runtime resolver;
- отдать shared page object;
- унаследовать tests через `[InheritsTests]`.

## 11. Сначала стабилизируйте `Headless`

```powershell
dotnet test tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj -c Debug
```

Когда `Headless` стабилен, подключайте desktop runtime:

```powershell
dotnet test tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj -c Debug
```

## 12. Что делать, если integration снова разрастается

Остановитесь и проверьте:

- не ушёл ли bootstrap code из `TestHost` в test projects;
- не пытаетесь ли вы автоматизировать secondary control вместо упрощения test data;
- не ушли ли вы в source dependency;
- не дублируются ли tests между runtime projects.

Подробнее: [advanced-integration.md](advanced-integration.md)
