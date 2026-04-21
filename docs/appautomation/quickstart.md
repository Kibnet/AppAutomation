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
dotnet new install AppAutomation.Templates
dotnet new tool-manifest
dotnet tool install AppAutomation.Tooling
```

These commands install the latest version from your configured feeds.
The generated AppAutomation package references also float to the latest available version by default.

## 3. Generate canonical topology

```powershell
dotnet new appauto-avalonia --name MyApp
```

Explicit floating package-version override:

```powershell
dotnet new appauto-avalonia --name MyApp --AppAutomationVersion "*"
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
dotnet tool run appautomation doctor --repo-root .
```

If `doctor` warns about source dependency, fix it before starting authoring.
If `doctor --strict` warns about unfinished scaffold markers, replace the generated placeholders before writing real scenarios.

## 5. Complete `TestHost`

File:

```text
tests/MyApp.AppAutomation.TestHost/MyAppAppLaunchHost.cs
```

Use built-in helpers:

- `AvaloniaDesktopLaunchHost`
- `AvaloniaHeadlessLaunchHost`
- `TemporaryDirectory`

Replace all generated placeholders in `SampleAppAppLaunchHost`, including `AvaloniaAppType`. The generated `HeadlessSessionHooks` already use that property and should not require manual reverse engineering.

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
- explicit `AutomationProperties.Name` for any control you assert through `WaitUntilName*`.

Don't try to mark up the entire application at once.
Selector contract details: [selector-contract.md](selector-contract.md)

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
dotnet test --project tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj -c Debug
```

When `Headless` is stable, enable desktop runtime:

```powershell
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

Это краткое руководство описывает рекомендуемый порядок подключения с нуля для существующего Avalonia-приложения.

## 1. Не начинайте с тестов

Сначала подготовьте детерминированные исходные условия:

- тестовую учётную запись и сценарий аутентификации;
- тестовые данные и права доступа;
- изолированный файл настроек;
- фиксированный стартовый экран;
- отключённые обновления и фоновые задания.

Если это не стабилизировано, не переходите к объектам страниц.

## 2. Установите шаблон и инструмент

```powershell
dotnet new install AppAutomation.Templates
dotnet new tool-manifest
dotnet tool install AppAutomation.Tooling
```

Эти команды ставят последнюю доступную версию из настроенных feed.
Сгенерированные `PackageReference` для AppAutomation по умолчанию тоже используют последнюю доступную версию.

## 3. Сгенерируйте стандартную структуру проектов

```powershell
dotnet new appauto-avalonia --name MyApp
```

Явный floating override для версии пакетов:

```powershell
dotnet new appauto-avalonia --name MyApp --AppAutomationVersion "*"
```

Шаблон создаст:

```text
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

## 4. Проверьте репозиторий через `doctor`

```powershell
dotnet tool run appautomation doctor --repo-root .
```

Если `doctor` предупреждает о зависимости через исходный код, исправьте это до начала работы с `Authoring`.
Если `doctor --strict` показывает неубранные маркеры scaffold, сначала замените placeholder-значения, а уже потом пишите реальные сценарии.

## 5. Допишите `TestHost`

Файл:

```text
tests/MyApp.AppAutomation.TestHost/MyAppAppLaunchHost.cs
```

Используйте встроенные вспомогательные классы:

- `AvaloniaDesktopLaunchHost`
- `AvaloniaHeadlessLaunchHost`
- `TemporaryDirectory`

Замените все placeholder-значения в `SampleAppAppLaunchHost`, включая `AvaloniaAppType`. Сгенерированные `HeadlessSessionHooks` уже используют это свойство и не требуют ручного поиска правильного pair API.

Типовой путь запуска настольного приложения:

```csharp
return AvaloniaDesktopLaunchHost.CreateLaunchOptions(
    desktopAppDescriptor,
    new AvaloniaDesktopLaunchOptions
    {
        BuildConfiguration = BuildConfigurationDefaults.ForAssembly(typeof(MyAppAppLaunchHost).Assembly)
    });
```

Типовой путь `Headless`:

```csharp
return AvaloniaHeadlessLaunchHost.Create(
    static () => MyAppBootstrap.CreateMainWindow());
```

## 6. Задайте минимальный контракт `AutomationId`

Первая итерация должна покрывать только элементы из критичного сценария быстрой проверки:

- корневое окно;
- основные вкладки и опорные элементы навигации;
- важные текстовые поля;
- важные кнопки;
- подписи и результаты;
- дочерние опорные элементы внутри составных виджетов.
- явный `AutomationProperties.Name` для тех элементов, которые будут участвовать в `WaitUntilName*`.

Не пытайтесь сразу размечать всё приложение.
Подробный контракт селекторов: [selector-contract.md](selector-contract.md)

## 7. Опишите объект страницы

Простые элементы управления описываются через `[UiControl(...)]`:

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

## 8. Для составных элементов управления сначала используйте встроенный путь адаптеров

Если элемент управления не укладывается в простые `[UiControl(...)]`, не переписывайте резолвер целиком. Сначала используйте:

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

## 9. Общие сценарии пишутся только в `Authoring`

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

Проекты выполнения не должны дублировать эти методы.

## 10. Проекты выполнения остаются тонкими обёртками

`Headless` и `FlaUI` должны только:

- запустить сеанс выполнения;
- создать резолвер среды выполнения;
- предоставить общий объект страницы;
- унаследовать тесты через `[InheritsTests]`.

## 11. Сначала стабилизируйте `Headless`

```powershell
dotnet test --project tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj -c Debug
```

Когда `Headless` стабилен, подключайте настольную среду выполнения:

```powershell
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

## 12. Что делать, если интеграция снова разрастается

Остановитесь и проверьте:

- не ушёл ли код запуска из `TestHost` в тестовые проекты;
- не пытаетесь ли вы автоматизировать второстепенный элемент вместо упрощения тестовых данных;
- не ушли ли вы в зависимость через исходный код;
- не дублируются ли тесты между проектами выполнения.

Подробнее: [advanced-integration.md](advanced-integration.md)
