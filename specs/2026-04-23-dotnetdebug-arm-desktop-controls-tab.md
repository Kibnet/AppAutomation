# DotnetDebug Arm Desktop Controls Tab

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: large
- Целевой релиз / ветка: текущая ветка `feat-arm-paritet`
- Ограничения:
  - До подтверждения спеки изменяется только этот файл.
  - Источник анализа: `C:\Projects\ИЗП\Sources\Arm.Srv`, прежде всего `src\Arm.Client` и `src\Arm.Client.Desktop`.
  - Цель реализации: sample `DotnetDebug.Avalonia` и связанные AppAutomation authoring/headless/FlaUI тесты.
  - Не переносить код Arm.Srv в sample как production dependency.
- Связанные ссылки:
  - `sample/DotnetDebug.Avalonia/MainWindow.axaml`
  - `sample/DotnetDebug.Avalonia/MainWindow.axaml.cs`
  - `sample/DotnetDebug.Avalonia/MainWindowViewModel.cs`
  - `sample/DotnetDebug.AppAutomation.Authoring/Pages/MainWindowPage.cs`
  - `sample/DotnetDebug.AppAutomation.Authoring/Tests/MainWindowScenariosBase.cs`
  - `sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/Tests/MainWindowHeadlessRuntimeTests.cs`
  - `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/MainWindowFlaUiRuntimeTests.cs`
  - `ControlSupportMatrix.md`

## 1. Overview / Цель
Добавить в `DotnetDebug.Avalonia` отдельную вкладку `Arm Desktop`, которая
показывает полный набор уникальных UI-control families и automation-сценариев,
встречающихся в desktop-клиенте Arm.Srv, и покрыть эту вкладку тестами через
AppAutomation.

Цель не в копировании каждого конкретного экрана Arm.Srv, а в наличии
исполняемого showcase для всех повторяющихся control patterns: Eremex grid,
editor/search wrappers, range/date filters, CRUD/save controls, loading/status,
approval, dialog/toast/export and shell navigation.

## 2. Текущее состояние (AS-IS)
- `DotnetDebug.Avalonia` уже содержит вкладки `Math`, `Control Mix`,
  `DateTime`, `Hierarchy`, `Data Grid`, `Calendar`.
- Sample уже зависит от `Eremex.Avalonia.Controls` и содержит Eremex
  `DataGridControl` плюс automation bridge.
- `MainWindowPage` экспонирует текущие demo controls через `UiControlAttribute`
  и один composite `HistoryOperationPicker`.
- Shared scenario base наследуется headless и FlaUI runtime tests.
- Новые контракты AAR-001..AAR-007 уже есть в AppAutomation:
  `ISearchPickerControl`, `IGridUserActionControl`, `IEditableGridControl`,
  `IDateRangeFilterControl`, `INumericRangeFilterControl`, `IDialogControl`,
  `INotificationControl`, `IFolderExportControl`, `IShellNavigationControl`.
- Новый анализ Arm.Srv показал:
  - `AutomationProperties.AutomationId` в Arm.Srv XAML: 0.
  - Частые controls/patterns: Eremex `DataGridControl`/`GridColumn`, Eremex
    editors, `CopyTextBox`, `SearchControl`, `ServerSearchComboBox`,
    `RangeFromToControl`, `DateRangeFilterControl`, `SaveCloseButtonsControl`,
    `CrudActionsControl`, `LoadingControl`, `StatusBar`, `Expander`,
    approval controls, dialog/toast/export, shell docking/navigation.

### 2.1 Инвентаризация Arm.Srv controls для scope
Термин "все встречающиеся controls" в этой задаче трактуется как все
scenario-facing control families из desktop UI, плюс явный учёт layout-only
элементов. Повторяющиеся экземпляры (`GridColumn`, `Button`, карточки разных
entity) не дублируются, если automation-сценарий одинаков.

| Категория из Arm.Srv | Примеры / факты анализа | Scope для вкладки |
|---|---|---|
| Layout/visual-only XAML | `Grid`, `StackPanel`, `Border`, `Style`, `DataTemplate`, `PathIcon`, `MaterialIcon`, `TextBlock` headings | Учитываются как контейнеры/визуальные элементы внутри секций; отдельный `UiControlType` и отдельный сценарий не требуются, если нет пользовательского действия или состояния. |
| Standard primitives | `Button`, `TextBox`, `CheckBox`, `ToggleButton`, `ListBox`, `ProgressBar`, `Label`, `SelectableTextBlock`, `MenuItem`/context menu | Покрыть через существующие primitive helpers and one explicit context/copy-like action. |
| Eremex grids | `DataGridControl`, `GridColumn`, `GridColumn.EditorProperties`, load-more/sort/copy/export/open/layout attached behaviors | Покрыть visual grid + automation bridge + user-action buttons and status assertions. |
| Eremex editors | `TextEditorProperties`, `ComboBoxEditor`, `ButtonEditor`, `SpinEditor`, `DateEditor`, `PopupEditor` | Покрыть через stable primitive/composite approximations and named parts; не требовать native Eremex UIA for every editor visual. |
| Custom wrappers | `CopyTextBox`, `SearchControl`, `ServerSearchComboBox`, `SaveCloseButtonsControl`, `CrudActionsControl`, `LoadingControl`, `StatusBar`, `ApprovalControl`, `ApprovalHighlightControl` | Покрыть отдельными controls/sections and user-level actions. |
| Dialog/export/notifications | `DialogHost`, `NotificationMessageContainer`, `ShowOpenFolderDialogAsync`, `ToastWithButton` | Покрыть deterministic composed parts without OS folder picker dependency. |
| Shell/docking | `DockGroup`, `DocumentGroup`, `DocumentPane`, stored layout extension | Покрыть navigation open/activate; layout persistence represented by status/assertion only. |

## 3. Проблема
AppAutomation уже имеет typed primitives для закрытых gaps, но `DotnetDebug`
не содержит единой исполняемой вкладки, которая демонстрирует Arm.Srv-like
controls и проверяет их в headless/FlaUI runtime tests. Из-за этого изменения
по recorder/headless/FlaUI нельзя быстро валидировать на реалистичном desktop
UI наборе.

## 4. Цели дизайна
- Разделение ответственности: sample показывает Arm-style UI patterns; core
  AppAutomation API не расширяется без отдельной причины.
- Повторное использование: использовать существующие composite adapters
  (`WithSearchPicker`, `WithDateRangeFilter`, `WithNumericRangeFilter`,
  `WithDialog`, `WithNotification`, `WithFolderExport`, `WithShellNavigation`)
  и grid helpers.
- Тестируемость: добавить shared сценарии в authoring base, чтобы они
  выполнялись минимум в headless и, где возможно, во FlaUI.
- Консистентность: все новые controls получают стабильные
  `AutomationProperties.AutomationId`.
- Обратная совместимость: существующие вкладки и тесты не должны менять
  поведение или selectors.

## 5. Non-Goals
- Не добавлять dependency на `Arm.Client` или `Arm.Client.Desktop`.
- Не копировать каждый XAML instance из Arm.Srv; покрываются уникальные control
  families и повторяющиеся user scenarios.
- Не добавлять отдельные AppAutomation controls/tests для чисто layout/visual
  constructs (`Grid`, `Border`, `Style`, `DataTemplate`, icon-only visuals),
  если у них нет собственного пользовательского действия или проверяемого
  состояния.
- Не реализовывать реальную Eremex docking layout persistence или OS folder
  picker automation; в sample используются deterministic anchors and composed
  parts.
- Не менять публичные AppAutomation contracts, если existing APIs достаточно.
- Не выполнять runtime UIA validation реального Arm.Srv приложения.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `sample/DotnetDebug.Avalonia/MainWindow.axaml`
  - Добавить `TabItem` `ArmDesktopTabItem`.
  - Внутри вкладки сгруппировать Arm-style controls по секциям.
- `sample/DotnetDebug.Avalonia/MainWindow.axaml.cs`
  - Добавить простые deterministic handlers для действий вкладки:
    search/select, grid actions, edit cell, filters, dialog, notification,
    export, shell navigation, loading/status/approval.
- `sample/DotnetDebug.Avalonia/MainWindowViewModel.cs`
  - Добавить состояние вкладки: rows, selected item, editor values, filter
    values, status/loading/notification/export/shell state.
- `sample/DotnetDebug.AppAutomation.Authoring/Pages/MainWindowPage.cs`
  - Добавить `UiControl` attributes for Arm Desktop controls.
  - Добавить composite properties configured through `UiControlDefinition`.
- `sample/DotnetDebug.AppAutomation.Authoring/Tests/MainWindowScenariosBase.cs`
  - Добавить user-level scenarios for the Arm Desktop tab.
- Runtime test classes
  - Подключить composite adapters in `CreatePage` for headless and FlaUI.

### 6.2 Детальный дизайн
Вкладка `Arm Desktop` должна включать следующие группы:

| Группа | Arm.Srv pattern | DotnetDebug representation | AppAutomation coverage |
|---|---|---|---|
| Layout/visual containers | `Grid`, `StackPanel`, `Border`, `ScrollViewer`, styles/templates/icons | Used to arrange sections and status visuals | accounted as non-scenario-facing; no separate page property unless used as anchor |
| Standard primitives | `Button`, `TextBox`, `CheckBox`, `ToggleButton`, `ListBox`, `ProgressBar`, `Label/TextBlock` | Existing/native controls in a compact section | existing primitive helpers |
| Copy wrapper | `miniControls:CopyTextBox` | `TextBox` + hidden/visible copy `Button` + result label | primitive + explicit action |
| Search controls | `SearchControl`, history/fuzzy options | input + result `ListBox` + apply/clear/options toggle | `ISearchPickerControl` plus primitive toggles |
| Server search combo | `ServerSearchComboBox` | input + popup/list + clear/open/select controls | `ISearchPickerControl` |
| Eremex editors | `ComboBoxEditor`, `ButtonEditor`, `SpinEditor`, `DateEditor`, `PopupEditor` | Eremex/native approximations with stable part ids where feasible | primitive or composite parts |
| Eremex grid | `DataGridControl`, `GridColumn`, copy/sort/open/load/export/layout | Eremex visual grid + automation bridge + action buttons | `Grid`, `IGridUserActionControl`, bridge assertions |
| Editable grid | cell text/date/combo/search editors | bridge-backed editable cells and editor controls | `IEditableGridControl` |
| Range/date filters | `RangeFromToControl`, `DateRangeFilterControl` | composed popup-like panels with from/to/apply/cancel controls | `IDateRangeFilterControl`, `INumericRangeFilterControl` |
| Dialog/toast/export | `DialogHost`, notification, folder export | deterministic dialog panel, toast panel and folder export panel | `IDialogControl`, `INotificationControl`, `IFolderExportControl` |
| Shell/navigation | Eremex document panes/docking navigation | stable tree/list/tab/pane anchors | `IShellNavigationControl` |
| Status/loading/approval | `LoadingControl`, `StatusBar`, `Expander`, approval controls | expander/status grid, reload, metadata toggle, approval toggle | primitives + explicit assertions |
| CRUD/save wrappers | `CrudActionsControl`, `SaveCloseButtonsControl` | add/edit/delete/close/save/save-close buttons | primitive button actions |

Все controls должны иметь predictable ids with `ArmDesktop` prefix, например:
- `ArmDesktopTabItem`
- `ArmSearchInput`, `ArmSearchResults`, `ArmSearchApplyButton`
- `ArmServerPickerInput`, `ArmServerPickerResults`, `ArmServerPickerOpenButton`
- `ArmGridAutomationBridge`, `ArmGridOpenButton`, `ArmGridSortButton`,
  `ArmGridLoadMoreButton`, `ArmGridCopyButton`, `ArmGridExportButton`
- `ArmDateRangeFilter`, `ArmNumericRangeFilter`
- `ArmDialog`, `ArmNotification`, `ArmFolderExport`
- `ArmShellNavigation`

### 6.3 Обязательные AppAutomation сценарии
Shared authoring scenario base должен получить отдельные тесты или компактные
тестовые методы, покрывающие следующие user flows:

| Тестовый поток | Проверяемые группы | Минимальные assertions |
|---|---|---|
| `ArmDesktop_PrimitivesWrappersAndSearch_Work` | standard primitives, `CopyTextBox`, `SearchControl`, `ServerSearchComboBox` | text entered/copied, fuzzy/history option toggled, search picker selected value visible, server picker clear/open/select works |
| `ArmDesktop_GridActionsAndEditableCells_Work` | Eremex grid, bridge, open/sort/load/copy/export, editable grid | bridge rows/cells visible, row open status, sort status, load more count, copied cell label, export status, edited cell commit/cancel |
| `ArmDesktop_FiltersDialogsNotificationsAndExport_Work` | date/numeric filters, dialog, notification, folder export | apply/cancel summaries, confirm/cancel/dismiss result, notification text/dismiss state, selected export folder/status |
| `ArmDesktop_ShellStatusLoadingApprovalAndCrud_Work` | shell navigation, loading/status/expander, approval, CRUD/save buttons | active/open pane state, loading/reload result, expander/metadata state, approval toggled, CRUD/save action status |

Tests should prefer typed AppAutomation page methods over direct control
inspection. Direct assertions are allowed for sample-specific status labels that
represent Arm.Srv wrapper behavior not modeled as a dedicated AppAutomation
contract.

## 7. Бизнес-правила / Алгоритмы
- Search/select sets selected result text and appends a history/status row.
- Grid build creates deterministic rows and bridge ids:
  `ArmGridAutomationBridge_Row{index}_Cell{column}`.
- Grid actions update an observable status label:
  open row, sort by column, load more, copy cell, export.
- Editable grid updates bridge cell value and supports commit/cancel result
  labels.
- Date/numeric filter apply updates filter summary; cancel leaves previous
  summary unchanged.
- Dialog confirm/cancel/dismiss updates dialog result.
- Notification dismiss changes visible/enabled state observable by adapters.
- Shell navigation opens/activates named panes and exposes active/open state.

## 8. Точки интеграции и триггеры
- Tab selection through `MainTabs`.
- Button click handlers in `MainWindow.axaml.cs`.
- Binding/state updates through `MainWindowViewModel`.
- Composite adapters configured in runtime `CreatePage` methods.

## 9. Изменения модели данных / состояния
Persisted state is not introduced. All new state is sample-only in-memory
observable state for testability:
- Arm desktop grid rows and selected/opened row.
- Search picker items and selected values.
- Filter summaries.
- Dialog/notification/export/shell/status labels.
- Approval/loading/status booleans.

## 10. Миграция / Rollout / Rollback
- Rollout: sample UI and tests are updated in the current branch.
- Existing selectors remain stable.
- Rollback: revert the sample/test commits; no persisted data migration exists.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- В `DotnetDebug.Avalonia` есть отдельная вкладка `Arm Desktop`.
- Вкладка покрывает все уникальные Arm.Srv desktop control families listed in
  section 6.2.
- Все new scenario-facing controls have stable `AutomationId`.
- `MainWindowPage` exposes the new controls and composites.
- Shared AppAutomation tests cover:
  - `ArmDesktop_PrimitivesWrappersAndSearch_Work`;
  - `ArmDesktop_GridActionsAndEditableCells_Work`;
  - `ArmDesktop_FiltersDialogsNotificationsAndExport_Work`;
  - `ArmDesktop_ShellStatusLoadingApprovalAndCrud_Work`.
- Layout-only elements from section 2.1 are explicitly represented as
  containers or visual children, but are not treated as missing AppAutomation
  controls.
- Headless runtime tests pass.
- FlaUI runtime tests either pass or skip only through existing desktop UI guard.
- Existing sample scenarios continue to pass.

Verification commands:
- `dotnet test .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj`
- `dotnet test .\sample\DotnetDebug.AppAutomation.Avalonia.Headless.Tests\DotnetDebug.AppAutomation.Avalonia.Headless.Tests.csproj`
- `dotnet test .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj`
- `dotnet build .\AppAutomation.sln`
- `dotnet test .\AppAutomation.sln --no-build`

## 12. Риски и edge cases
- FlaUI может не видеть часть Eremex runtime UIA; mitigate через explicit bridge
  and primitive composed parts.
- Слишком большая вкладка может стать шумной; mitigate через группы и stable ids.
- Если часть Eremex editor API в sample недоступна или нестабильна, допустимо
  заменить её native/composite approximation with the same automation scenario.
- If implementing all scenario groups in one `MainWindow.axaml` file makes the
  file difficult to maintain, split Arm Desktop content into a local sample
  `UserControl`; this does not change the automation ids or test scope.
- Long-running full solution tests may be slow; targeted tests run first, full
  run before completion.

## 13. План выполнения
1. Add `ArmDesktopTabItem` layout and stable controls to `MainWindow.axaml`.
2. Extend `MainWindowViewModel` with Arm Desktop state and deterministic row
   models if needed.
3. Add handlers in `MainWindow.axaml.cs`.
4. Extend `MainWindowPage` with `UiControl` attributes and composite
   definitions.
5. Configure composite adapters in headless and FlaUI `CreatePage`.
6. Add shared AppAutomation scenarios.
7. Run targeted tests; fix failures.
8. Run full build/test according to verification plan.
9. Post-EXEC review and final report.

## 14. Открытые вопросы
Нет блокирующих вопросов. Scope decision: "все controls из Arm desktop" means
all unique control families and user scenarios from the static Arm.Srv audit,
not every repeated `GridColumn`, `Button` or business screen instance.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
  - Stable automation selectors are required.
  - UI/integration tests are required for changed user flows.
- Профиль: `ui-automation-testing`
  - Tests use stable automation ids instead of text/position where possible.
  - UI smoke suite must pass before completion.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `sample/DotnetDebug.Avalonia/MainWindow.axaml` | Add Arm Desktop tab and controls | Showcase Arm.Srv control families |
| `sample/DotnetDebug.Avalonia/MainWindow.axaml.cs` | Add handlers for deterministic scenarios | Make controls executable |
| `sample/DotnetDebug.Avalonia/MainWindowViewModel.cs` | Add tab state | Bind testable UI state |
| `sample/DotnetDebug.Avalonia/*ArmDesktop*.axaml(.cs)` | Optional local `UserControl` split if `MainWindow.axaml` becomes too large | Keep sample maintainable without changing automation scope |
| `sample/DotnetDebug.AppAutomation.Authoring/Pages/MainWindowPage.cs` | Add controls/composites | Authoring contract |
| `sample/DotnetDebug.AppAutomation.Authoring/Tests/MainWindowScenariosBase.cs` | Add shared tests | Headless/FlaUI coverage |
| `sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/Tests/MainWindowHeadlessRuntimeTests.cs` | Configure adapters | Runtime support |
| `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/MainWindowFlaUiRuntimeTests.cs` | Configure adapters | Runtime support |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| DotnetDebug demo | Separate generic tabs | Added Arm Desktop tab for Arm.Srv-like controls |
| Arm.Srv gaps validation | Matrix/spec only | Executable sample scenarios |
| Runtime tests | Existing showcase flows | Arm Desktop flows in shared AppAutomation tests |

## 18. Альтернативы и компромиссы
- Вариант: добавить прямую зависимость sample на `Arm.Client`.
  - Плюсы: максимально близко к реальному UI.
  - Минусы: heavy coupling, external repo dependency, harder CI.
  - Почему не выбран: AppAutomation repo sample должен оставаться автономным.
- Вариант: перечислить каждый Arm.Srv XAML instance.
  - Плюсы: формально "все" instances.
  - Минусы: огромная и нестабильная вкладка, повторение сотен колонок.
  - Почему не выбран: полезнее покрыть unique control families and scenarios.
- Вариант: добавить только документацию.
  - Плюсы: быстро.
  - Минусы: нет executable validation.
  - Почему не выбран: запрос требует вкладку и тесты.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели дизайна и Non-Goals описаны; scope "все controls" уточнён через inventory section 2.1. |
| B. Качество дизайна | 6-10 | PASS | Ответственность по файлам, integration points, state, algorithms and rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Нет production dependency на Arm.Srv, persisted state не вводится, existing selectors не меняются. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, четыре обязательных AppAutomation flows and verification commands указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План этапов, alternatives, risks and file-change table enough for EXEC. |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client` and `ui-automation-testing` requirements reflected: stable ids and UI tests are mandatory. |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Уточнено, что покрываются all unique scenario-facing control families; layout-only controls accounted separately. |
| 2. Понимание текущего состояния | 5 | Описаны current DotnetDebug tabs/tests, AppAutomation contracts and Arm.Srv audit facts. |
| 3. Конкретность целевого дизайна | 5 | Есть inventory, control-group table, required ids and mandatory scenario flows. |
| 4. Безопасность (миграция, откат) | 5 | Sample-only changes, no persisted state, no Arm.Srv dependency, simple rollback. |
| 5. Тестируемость | 5 | Headless/FlaUI shared flows and concrete verification commands are listed. |
| 6. Готовность к автономной реализации | 5 | File responsibilities and execution plan are explicit; no blocking open questions. |

Итоговый балл: 30 / 30.
Зона: готово к автономному выполнению.

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - Scope narrowed from every repeated Arm.Srv instance to all unique
    scenario-facing control families/user scenarios.
  - Added explicit inventory for layout-only controls so "all controls" is not
    silently reduced to only AppAutomation typed controls.
  - Added four mandatory shared AppAutomation flows with minimum assertions.
  - Replaced short quality-gate summary with required linter/rubric format.
- Что осталось на решение пользователя: подтвердить спекификацию для перехода к
  EXEC.

## Approval
Подтверждено пользователем фразой: "Спеку подтверждаю".

## 20. EXEC Result
Статус: PASS

Реализовано:
- Добавлена отдельная вкладка `Arm Desktop` в `DotnetDebug.Avalonia`.
- Вкладка вынесена в `ArmDesktopControl` с автономным состоянием и стабильными
  `AutomationId`.
- Покрыты representative Arm.Srv control families/scenarios: primitives,
  wrappers, search/server pickers, Eremex-like grid with bridge, editable row
  value workflow, date/numeric filters, dialog, notification, folder export,
  shell pane activation, loading/status, approval and CRUD/save/close actions.
- Расширен `MainWindowPage` primitive/composite contract.
- Подключены composite adapters для headless and FlaUI runtime.
- Добавлены четыре shared AppAutomation сценария из acceptance criteria.

Важные runtime decisions:
- Eremex visual control во FlaUI не отдаёт стабильный UIA element by
  `AutomationId`, поэтому executable assertion использует stable host marker,
  а grid data/actions валидируются через bridge contract.
- Shell navigation для shared сценария использует observable pane tabs через
  `OpenOrActivate/Activate`; runtime-specific adapter setup оставляет headless
  navigation source as ListBox and FlaUI as Tab because FlaUI cannot reliably
  select Avalonia ListBox string/ListBoxItem entries in this sample.

Проверки:
- `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj` - PASS, 51/51.
- `dotnet test --project .\sample\DotnetDebug.AppAutomation.Avalonia.Headless.Tests\DotnetDebug.AppAutomation.Avalonia.Headless.Tests.csproj` - PASS, 40/40.
- `dotnet test --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj` - PASS, 27/27.
- `dotnet build .\AppAutomation.sln` - PASS.
- `dotnet test --solution .\AppAutomation.sln --no-build` - PASS, 207/207.

Примечание: локальный `dotnet test` требует `--project`/`--solution`, поэтому
команды со positional `.csproj`/`.sln` были запущены в поддерживаемой форме.

## 21. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Анализ инструкций и текущего состояния | 0.95 | Нет | Создать спекификацию | Да | Нет | Задача меняет UI и тесты, поэтому нужен SPEC gate | `AGENTS.md`, central instructions, sample/test files |
| SPEC | Анализ Arm.Srv controls and DotnetDebug sample | 0.9 | Runtime UIA реального Arm.Srv не проверялось | Запросить подтверждение спеки | Да | Нет | Static audit достаточен для проектирования sample tab; runtime Arm.Srv validation вне scope | `C:\Projects\ИЗП\Sources\Arm.Srv`, `sample/DotnetDebug.*` |
| SPEC | Review спеки и устранение замечаний | 0.95 | Нет | Ожидать подтверждение спеки | Да | Да, пользователь запросил review | Устранены неоднозначность "all controls", недостаточно подробные acceptance flows and compressed quality gate | `specs/2026-04-23-dotnetdebug-arm-desktop-controls-tab.md` |
| EXEC | Добавление Arm Desktop вкладки, Page Object, runtime adapters and shared tests | 0.95 | Нет | Завершить задачу | Нет | Да, пользователь подтвердил спеку | Реализация осталась sample-only, существующие public AppAutomation contracts не изменены, все targeted/full checks прошли | `sample/DotnetDebug.Avalonia/*`, `sample/DotnetDebug.AppAutomation.*`, `specs/2026-04-23-dotnetdebug-arm-desktop-controls-tab.md` |
