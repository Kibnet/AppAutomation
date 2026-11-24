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

If a custom popup does not expose a standard `ComboBox` or `ListBox`, register its logical picker hint and a confirmed-selection source before attaching Recorder:

```csharp
var selectionSource = new RecorderSearchPickerSelectionSource();
options.SearchPickerHints.Add(new RecorderSearchPickerHint(
    "CustomPicker",
    SearchPickerParts.ByAutomationIds("CustomPickerInput", "CustomPickerResults")));
options.SearchPickerSelectionSources.Add(selectionSource);

// Call synchronously on the UI thread after commit and before popup cleanup.
selectionSource.ConfirmSelection(searchInput, resultsRoot, selectedDisplayText);
```

Typing or closing the popup does not call `ConfirmSelection`. Replay still resolves the logical `ISearchPickerControl`; a Headless consumer can provide it directly from the ViewModel through `IUiControlAdapter` without popup controls.

Composite parts inherit the logical control's `Scope`; part locators are not permission to search another card. A detached popup must have an existing explicit scope or a provider-proven owner. If ownership cannot be established inside the declared scope, resolution fails instead of selecting the first matching control in the window.

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

`UiWait` checks cancellation before the first read and after callbacks. Polling pauses are capped by the remaining monotonic timeout. A callback already in progress cannot be interrupted, but a result returned after the deadline is not success; no further read starts after timeout or cancellation. `TryUntil` reports failure, while `Until` throws `TimeoutException`; cancellation remains `OperationCanceledException`.

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

## 11. Recorder persistence and generated pages

Recorder captures UI-affine context and a fixed step snapshot before scheduling project scanning, C# generation and file writes on a worker. Save and autosave remain serialized. A successful Save acknowledges only its captured content revision: a newer recording change keeps its pending autosave; unchanged validation does not create a new dirty revision. Recovery validates the payload structure before replacing the journal; existing valid autosave files keep the same format.

The `[UiControl]` source generator supports non-generic, non-file-local, top-level `partial` pages derived from `UiPage`. It preserves their accessibility and abstract/sealed intent. Generic, file-local, nested and non-partial declarations receive diagnostics instead of a generated incompatible type. Pages with the same short name in different namespaces have distinct deterministic source hints, and locator strings are emitted as escaped C# literals.

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

Если нестандартный popup не предоставляет обычный `ComboBox` или `ListBox`, до подключения Recorder зарегистрируйте hint логического picker и источник подтверждённого выбора:

```csharp
var selectionSource = new RecorderSearchPickerSelectionSource();
options.SearchPickerHints.Add(new RecorderSearchPickerHint(
    "CustomPicker",
    SearchPickerParts.ByAutomationIds("CustomPickerInput", "CustomPickerResults")));
options.SearchPickerSelectionSources.Add(selectionSource);

// Вызвать синхронно в UI-потоке после commit и до очистки popup.
selectionSource.ConfirmSelection(searchInput, resultsRoot, selectedDisplayText);
```

При простом вводе или закрытии popup `ConfirmSelection` не вызывается. Воспроизведение по-прежнему разрешает логический `ISearchPickerControl`; Headless-потребитель может связать его с ViewModel через `IUiControlAdapter` без popup-контролов.

Части составного контрола наследуют `Scope` логического владельца: locator части не разрешает поиск в другой карточке. Для detached popup нужен существующий явный scope либо доказанная провайдером принадлежность. Если принадлежность в объявленном scope установить нельзя, поиск завершается отказом, а не выбирает первое совпадение окна.

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

`UiWait` проверяет отмену до первого чтения и после callbacks. Пауза ограничивается остатком монотонного таймаута. Уже начатый callback принудительно не прерывается, но его поздний результат не считается успехом; новое чтение после deadline или отмены не начинается. `TryUntil` возвращает неуспех, `Until` бросает `TimeoutException`, отмена остаётся `OperationCanceledException`.

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

## 11. Строки без бизнес-Id и текст сложных ячеек

### Ключ конкретного объекта строки

Если строки имеют устойчивый бизнес-Id, продолжайте использовать `IdentifyRowsBy("Id")`.
Для пустых или одинаковых строк без такого Id доступен явный opt-in:

```csharp
var grid = GridAutomationDefinition.ByAutomationIds("ComponentsGrid", "ComponentsGrid", "ComponentsGrid")
    .WithColumns(
        GridColumnDefinition.ObjectKey("RowKey"),
        GridColumnDefinition.Auto("Product"),
        GridColumnDefinition.Auto("Volume").AsValue(GridCellValueKind.Number))
    .IdentifyRowsBy("RowKey");
```

`RowKey` — логическое имя скрытого источника, не новое свойство модели. `GridRowObjectKey.Get(row)` выдаёт ключ по ссылке
на объект; одинаковые `record`-значения не объединяются. Редактирование, перестановка и пересоздание визуального контейнера
не меняют ключ. Registry не добавляет данные в модель/JSON и не удерживает удалённые объекты.

В приложении включите `GridRowObjectKeyPublication.IsEnabled` на **реальных контейнерах строк этой таблицы**.
Механизм из `AppAutomation.Recorder.Avalonia` публикует ключ текущего `DataContext` в `AutomationProperties.ItemStatus`
и не требует активной записи. Не применяйте его к заголовкам, ячейкам или строкам с уже опубликованным бизнес-Id.
Обычный binding-конвертер для этой задачи недостаточен: смена контекста на равный `record` может не вызвать уведомление binding.

Ключ не исправляет ошибочный контекст самого native control. Если после переработки контейнера его индекс уже относится к новой
строке, а `CellData.Row`/`DataContext` всё ещё содержат прежний объект, библиотека должна отказать, а не выбрать value-equal
соседа. Такой случай обнаружен у Eremex при переработке большого набора одинаковых пустых records; исправлением Eremex этот
механизм не является. Корректное соответствие row context фактическому объекту остаётся условием интеграции.

Если Recorder подключён условно, регистрируйте соответствующий row style в automation-bootstrap под тем же
`#if APPAUTOMATION_RECORDER`. Не добавляйте безусловную XAML-ссылку на отсутствующий assembly в обычную сборку приложения.
Например, к уже существующему scoped row style добавляется:

```csharp
#if APPAUTOMATION_RECORDER
rowStyle.Setters.Add(new Setter(GridRowObjectKeyPublication.IsEnabledProperty, true));
#endif
```

Recorder, Headless и UIA-публикация должны использовать этот **общий** owner ключа.
Если приложение публикует полный выбор/текущий view через `GridRowSelectionValue.ToAutomationValue`, для `ObjectKey`-колонки
оно также вызывает `GridRowObjectKey.Get(item)`, а не читает одноимённое свойство VM. Logical column в snapshot и каталоге должна совпадать.
Полный текущий view позволяет FlaUI проверить отсутствие дубликатов без обхода всех viewport.
Для записи множественного выбора со строками вне viewport публикуйте полный выбор на корне грида. Recorder сверяет его
с фактическим набором выбранных объектов. У материализованных строк (роль `DataItem` или включённая публикация ключа)
ключ проверяется на самом контейнере. Панель деталей с тем же `DataContext` контейнером строки не считается.
Нет полного доказательства текущей выборки — нет успешной операции по предположению об уникальности.

В начале каждого запуска тест получает новый runtime selector:

```csharp
var previous = GridRowKeyReader.CaptureFirst(Page.ComponentsGrid);
// После штатного добавления одной пустой строки:
var added = GridRowKeyReader.CaptureFirst(Page.ComponentsGrid, new[] { previous });
```

Исключать нужно все прежние строки, а не только одну, если их несколько. Ключи не переносятся между процессами и новыми
экземплярами после reload/клонирования. Старый selector должен вернуть not found, а не выбрать похожую строку.
Для нового объекта требуется явный новый capture. Два появления того же reference в текущем view неоднозначны.

В Recorder задайте явный источник runtime-строки через Remember → Edit → `Current table row`.
Последующие Check/Copy/Edit/Select/Open и формулы используют этот anchor. Без такого источника новый ObjectKey workflow
не экспортирует GUID из записи и не подставляет произвольную первую строку. Сами object keys никогда не являются replay-литералами.

### Отображаемый текст отдельно от значения

Если приложение уже публикует пользовательский текст реальной ячейки в UIA `Name`, его можно читать одинаково в Recorder,
Headless и FlaUI:

```csharp
GridColumnDefinition.Auto("Components").AtRuntime("Компоненты").ReadDisplayedText();
GridColumnDefinition.Auto("ProductionRate").AtRuntime("Производительность")
    .AsValue(GridCellValueKind.Text).ReadDisplayedText();
```

Формат, единицы, подписи и порядок элементов определяет приложение. Библиотека не склеивает произвольные дочерние `TextBlock`
и не считает `collection.ToString()` отображением шаблона. Пустой опубликованный `Name` допустим; отсутствие доступной
ячейки/источника или неоднозначность не подменяются пустым результатом. Неправильно опубликованный пустой `Name` должен
выявляться интеграционными тестами приложения.

В этом режиме `DisplayValueFrom`/`FormatWith` не применяются одновременно и колонка не используется как identity.
Доступный `RawValue` сохраняется независимо от `DisplayText`: например, `12.345m` и `12,3 кг/час`.
Чтение ничего не округляет и не записывает в бизнес-модель. FlaUI не восстанавливает исходную точность из округлённой подписи:
если отдельного semantic UIA-значения нет, это display-only представление. Для строгих числовых проверок используйте доступный
семантический источник, а не подпись с единицей. Обычные Number/Date/DateTime/Reference/Boolean колонки без opt-in не меняются.

### Миграция адаптера рецептов ARM

Подключение выполняет владелец ARM; изменения его кода не входят в реализацию библиотеки.

1. В таблице компонентов заменить скрытую колонку `AutomationRowKey` на `ObjectKey("RowKey")` и identity на `RowKey`;
   публикацию на реальном row заменить общим `GridRowObjectKeyPublication`. В основной таблице рецептов оставить настоящий `Id`.
2. Согласовать `GridSelectionAutomation`/полный UIA snapshot с новым источником и логическим именем ключа.
3. Только колонки `Components` и `ProductionRate` перевести на `ReadDisplayedText`, убрав их прежние `DisplayValueFrom`/`FormatWith`.
4. Сохранить UI-форматирование производительности (`CustomColumnDisplayText`/`UnitDisplayTextFormatter`) и публикацию этого текста в `Name`.
5. Убрать зависимость `RecipeGridCellDisplayTextConverter` от `AutomationComponentsText`: публиковать подписи списка через UI/automation-слой,
   используя то же представление, что показывает шаблон. **Сам converter не удалять**, пока его не заменяет проверенная эквивалентная UIA-публикация.
6. После проверки нового подключения удалить из VM `AutomationComponentsText`, `AutomationProductionRateText`, `AutomationRowKey`,
   старый helper `PresentationRowIdentity` и только соответствующие им исключения в `ComparisonUtils`.
7. Проверить обычную сборку без Recorder, UI шаблонов и существующие ARM сценарии записи/Headless/FlaUI.
   Зелёные собственные тесты AppAutomation сами по себе не подтверждают такую интеграцию.

### Составной редактор времени

Для подтверждаемого `TimePickerPresenter`, который открывается нажатием на input,
см. [parts, точное UI-значение и протокол владения popup](time-pickers.md).
Это opt-in общего `WithTimePicker`; подключение конкретного host и его UIA-публикации
остаётся отдельным этапом интеграции.

## 12. Сохранение Recorder и генерируемые страницы

Recorder фиксирует UI-контекст и снимок шагов до передачи scan проекта, генерации C# и записи файлов рабочему потоку. Save и autosave по-прежнему выполняются последовательно. Успешный Save подтверждает только захваченную ревизию содержимого: новое действие сохраняет ожидающий autosave, а неизменная повторная валидация не создаёт dirty-ревизию. Структура recovery payload проверяется до замены журнала; формат валидных прежних autosave не меняется.

Генератор `[UiControl]` поддерживает top-level `partial` страницы `UiPage`, не являющиеся generic или file-local, сохраняя accessibility и намеренные abstract/sealed. Для generic, file-local, nested и non-partial выдаётся диагностика, а не несовместимый тип. Страницы с одинаковым коротким именем в разных namespace получают разные детерминированные hint-имена; locator-строки выводятся с корректным C#-экранированием.
