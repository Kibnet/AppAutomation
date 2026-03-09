# AppAutomation Advanced Integration

Этот документ покрывает integration cases, которые выходят за пределы quickstart happy path:

- solution lives below repo root;
- repo-specific launch/bootstrap;
- stateful Avalonia apps;
- repeated headless launches в одном процессе;
- readiness/retry patterns;
- stable selectors beyond visible text.

## 1. Nested solution layout

Поддерживаемый сценарий:

```text
repo/
  src/
    MyApp.sln
    MyApp/
  tests/
    MyApp.UiTests.Authoring/
    MyApp.UiTests.Headless/
    MyApp.UiTests.FlaUI/
    MyApp.AppAutomation.TestHost/
```

Рекомендуемый подход:

- выберите один anchor: `repo root` или `solution directory`;
- держите поиск этого anchor только в `MyApp.AppAutomation.TestHost`;
- от этого anchor стройте пути до AUT, temp folders и generated outputs;
- не зашивайте layout-specific knowledge в `AppAutomation.*` packages.

## 2. Что должен делать TestHost

`MyApp.AppAutomation.TestHost` это repo-only infrastructure layer.

Обычно он отвечает за:

- поиск `repo root` / `.sln`;
- build or launch prerequisites;
- вычисление `bin/<Configuration>/<TFM>` paths;
- isolated `Settings.json`, temp dirs, seed data;
- формирование `DesktopAppLaunchOptions`;
- формирование `HeadlessAppLaunchOptions`.

### Desktop launch options

```csharp
using AppAutomation.Session.Contracts;

return new DesktopAppLaunchOptions
{
    ExecutablePath = executablePath,
    WorkingDirectory = workingDirectory,
    Arguments = ["--automation", "--profile", "smoke"],
    EnvironmentVariables = new Dictionary<string, string?>
    {
        ["MYAPP_ENV"] = "Test",
        ["MYAPP_SETTINGS_PATH"] = settingsPath
    }
};
```

Используйте `Arguments` и `EnvironmentVariables`, если AUT требует startup flags, alternate config path или isolated runtime environment.

## 3. Stateful apps и repeated headless launches

Если приложение хранит существенное состояние в static singletons, повторные headless session startup-ы почти всегда потребуют явного reset path.

Framework даёт для этого стандартные hooks:

- `BeforeLaunchAsync`
- `CreateMainWindow`
- `CreateMainWindowAsync`

Рекомендуемое разделение ответственности:

- `BeforeLaunchAsync`: reset static state, temp files, seed data, isolated settings;
- `CreateMainWindowAsync`: async bootstrap, который должен завершиться созданием `Avalonia.Controls.Window`;
- `CreateMainWindow`: простой sync path для apps без сложного bootstrap.

Пример:

```csharp
using AppAutomation.Session.Contracts;

return new HeadlessAppLaunchOptions
{
    BeforeLaunchAsync = async cancellationToken =>
    {
        await ResetGlobalStateAsync(cancellationToken);
        PrepareIsolatedWorkspace();
    },
    CreateMainWindowAsync = cancellationToken =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<object>(MyAppAutomationBootstrap.CreateMainWindow());
    }
};
```

Что остаётся consumer responsibility:

- знать, какие static fields/singletons нужно сбросить;
- изолировать filesystem state между тестами;
- выбирать, какие сценарии уместны для in-process headless, а какие лучше держать только в desktop runtime.

## 4. Readiness и retry patterns

`UiTestBase` предоставляет reusable helpers:

- `WaitUntil(Func<bool> condition, ...)`
- `WaitUntil<T>(Func<T> valueFactory, Predicate<T> condition, ...)`
- `WaitUntilAsync<T>(...)`
- `RetryUntil(Func<bool> attempt, ...)`

Используйте их для:

- app readiness после launch;
- ожидания заполнения tree/grid/list;
- transient UI transitions, где первый interaction может прийти слишком рано.

Пример:

```csharp
protected override MainWindowPage CreatePage(HeadlessSession session)
{
    var page = new MainWindowPage(new HeadlessControlResolver(session.Session.MainWindow));

    WaitUntil(
        () => page.MainTabs.IsEnabled,
        timeout: TimeSpan.FromSeconds(10),
        because: "Main tabs should become interactive before scenarios continue.");

    return page;
}
```

Граница применения:

- если проблема в том, что control находится только по тексту, сначала улучшайте `AutomationId` и page model;
- retry нужен для readiness и transient state, а не как замена плохим locator-ам.

## 5. Stable selectors для tab navigation

Text-based tab navigation остаётся рабочей:

```csharp
Page.SelectTabItem(static candidate => candidate.MainTabs, "Tasks");
```

Но рекомендуемый path для production suite:

```csharp
Page
    .SelectTabItem(static candidate => candidate.TasksTabItem)
    .WaitUntilIsSelected(static candidate => candidate.TasksTabItem);
```

Для этого:

- дайте `AutomationId` самому `TabItem`;
- объявите его в authoring layer как `UiControlType.TabItem`;
- используйте selector на `ITabItemControl`, а не только header text.

Такой path устойчивее к локализации и copy changes.

## 6. Troubleshooting

### Headless launch падает сразу

Проверьте, что в `HeadlessAppLaunchOptions` задан хотя бы один factory:

- `CreateMainWindow`
- `CreateMainWindowAsync`

### Первый headless test проходит, следующие падают

Обычно это означает residual app state. Проверьте:

- все ли static singletons/reset paths вызываются в `BeforeLaunchAsync`;
- не переиспользуется ли один и тот же temp/settings path между тестами;
- не хранится ли window/session в global state между запусками.

### Tab item не выбирается через stable selector

Проверьте:

- у `TabItem` есть стабильный `AutomationId`;
- selector указывает именно на `UiControlType.TabItem`;
- control действительно является частью `TabControl`, а не отдельным декоративным element-ом.

### Desktop AUT требует startup flags или env vars

Не обходите framework собственным `ProcessStartInfo`, если это не нужно. Сначала используйте:

- `DesktopAppLaunchOptions.Arguments`
- `DesktopAppLaunchOptions.EnvironmentVariables`

### Retry приходится писать в каждом тесте

Поднимите этот код в base class поверх `UiTestBase` и используйте `WaitUntil` / `RetryUntil`, а не ad-hoc циклы внутри каждого сценария.
