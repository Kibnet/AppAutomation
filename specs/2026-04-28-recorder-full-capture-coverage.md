# Recorder Full Capture Coverage

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: large
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `feat-arm-paritet`
- Ограничения:
  - До подтверждения спеки изменяется только этот файл.
  - Реализация ограничена текущим репозиторием `AppAutomation`; внешний `Arm.Srv` не меняется.
  - Не добавлять зависимость `AppAutomation.Recorder.Avalonia` от `AppAutomation.FlaUI`, Eremex или consumer-specific assemblies.
  - Не записывать low-level gestures там, где публичный runtime DSL уже имеет typed intent.
  - Не считать все значения `UiControlType` одинаково action-capable: read-only и container-only controls покрываются assertion/diagnostic rows, а не искусственными click/edit actions.
  - Новые recorder actions должны быть additive: существующие generated scenarios и runtime APIs не ломаются.
  - Desktop recorder tests должны использовать существующий `DotnetDebug` FlaUI harness, `Debug` startup path, temp output directory и `[NotInParallel("DesktopUi")]`.
  - Известная проблема parallel-run overlay tests в `tests/AppAutomation.Recorder.Avalonia.Tests` не является целью этой спеки, но проверки должны использовать стабильный sequential runner, если full parallel solution run снова воспроизводит этот независимый race.
- Связанные ссылки:
  - `ControlSupportMatrix.md`
  - `tasks.md`
  - `specs/AAR-003-eremex-grid-user-actions.md`
  - `specs/AAR-004-in-grid-editor-activation.md`
  - `specs/AAR-005-popup-date-range-filters.md`
  - `specs/AAR-006-dialog-toast-export-flow.md`
  - `specs/AAR-007-shell-docking-navigation.md`
  - `specs/2026-04-27-dotnetdebug-recorder-desktop-smoke.md`
  - `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs`
  - `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`
  - `src/AppAutomation.Abstractions/UiControlType.cs`
  - `src/AppAutomation.Abstractions/UiPageExtensions.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - `sample/DotnetDebug.AppAutomation.Authoring/Pages/MainWindowPage.cs`
  - `sample/DotnetDebug.Avalonia/App.axaml.cs`

## 1. Overview / Цель
Закрыть recorder coverage для всех допустимых пользовательских действий и assertion capture сценариев по всем `UiControlType`, которые уже представлены в AppAutomation runtime DSL или recorder model.

Outcome contract:
- Success means:
  - в репозитории есть явная recorder coverage matrix для `UiControlType` -> supported capture actions / assertions / unsupported-by-design rows;
  - каждый поддержанный capture row имеет автоматический тест на factory/session capture, codegen и runtime validation readiness;
  - публичные typed workflows из AAR-003..AAR-007, которые сейчас доступны в runtime DSL, но не имеют recorder capture/codegen, либо получают recorder support, либо получают явный diagnostic/unsupported contract с тестом и обоснованием;
  - sample desktop smoke покрывает representative real recorder flows по primitive, composite, grid и assertion families;
  - проверки показывают, какие gaps устранены, а какие являются честными product/runtime boundaries.
- Итоговый артефакт / output:
  - обновлённые recorder tests;
  - при необходимости additive recorder model/codegen/capture изменения;
  - обновлённая sample recorder configuration и grouped desktop recorder smoke tests;
  - эта спека с журналом EXEC и post-EXEC review.
- Stop rules:
  - если тест выявляет missing stable automation id в sample, сначала фиксируется app/sample automation contract, а не добавляется brittle locator workaround;
  - если новая строка требует public API shape, которого нельзя выбрать однозначно из существующих AAR contracts, остановиться и запросить решение;
  - если desktop input недоступен в текущей среде, desktop tests должны корректно skip-аться, а обязательная локальная проверка продолжается unit/headless/sequential suite.

## 2. Текущее состояние (AS-IS)
- `UiControlType` содержит 31 тип: primitives (`TextBox`, `Button`, `Label`, `ListBox`, `CheckBox`, `ComboBox`, `RadioButton`, `ToggleButton`, `Slider`, `ProgressBar`, `Calendar`, `DateTimePicker`, `Spinner`, `Tab`, `Tree`, `TreeItem`), grid/legacy grid values (`DataGridView`, `DataGridViewRow`, `DataGridViewCell`, `Grid`, `GridRow`, `GridCell`) и composites (`SearchPicker`, `DateRangeFilter`, `NumericRangeFilter`, `Dialog`, `Notification`, `FolderExport`, `ShellNavigation`).
- `RecordedActionKind` сейчас содержит 32 действия:
  - primitives: `EnterText`, `ClickButton`, `SetChecked`, `SetToggled`, `SelectComboItem`, `SelectListBoxItem`, `SetSliderValue`, `SetSpinnerValue`, `SelectTabItem`, `SelectTreeItem`, `SetDate`;
  - assertions: `WaitUntilTextEquals`, `WaitUntilTextContains`, `WaitUntilIsChecked`, `WaitUntilIsToggled`, `WaitUntilIsSelected`, `WaitUntilIsEnabled`, `WaitUntilGridRowsAtLeast`, `WaitUntilGridCellEquals`;
  - composites/grid: `SearchAndSelect`, `SearchAndSelectGridCell`, `OpenGridRow`, `SortGridByColumn`, `ScrollGridToEnd`, `CopyGridCell`, `ExportGrid`, `ConfirmDialog`, `CancelDialog`, `DismissDialog`, `DismissNotification`, `OpenOrActivateShellPane`, `ActivateShellPane`.
- Runtime DSL в `UiPageExtensions` уже шире recorder model:
  - есть `SetDateRangeFilter`, `SetNumericRangeFilter`, `SelectExportFolder`;
  - есть `WaitUntilProgressAtLeast`, `WaitUntilListBoxContains`, `WaitUntilHasItemsAtLeast`;
  - есть editable grid methods: `EditGridCell`, `EditGridCellText`, `EditGridCellNumber`, `EditGridCellDate`, `SelectGridCellComboItem`, `SearchAndSelectGridCell`;
  - есть shell aliases `NavigateShellPane`, `OpenShellPane`, `OpenOrActivateShellPane`, `ActivateShellPane`.
- Предыдущая smoke-спека добавила real desktop recorder smoke только для spinner, search picker, dialog, notification и shell navigation slices.
- `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` уже покрывает много отдельных capture/codegen случаев, но нет единого authoritative matrix, которая доказывает:
  - что каждый `RecordedActionKind` имеет тест;
  - что каждый `UiControlType` классифицирован;
  - что runtime-only workflows из AAR-004..AAR-006 больше не остаются recorder blind spot.
- `RecorderCommandRuntimeValidator` проверяет compatibility action/control/payload, но не является coverage oracle: если action kind отсутствует в recorder model, validator не может показать пробел.
- `sample/DotnetDebug.AppAutomation.Authoring/Pages/MainWindowPage.cs` содержит реальные controls для большинства типов, включая Arm-style composites, но часть logical composite properties объявлена вручную, а не через `[UiControl]`.
- `sample/DotnetDebug.Avalonia/App.axaml.cs` сейчас настраивает recorder hints для spinner, grid aliases/hints, search pickers, dialog, notification и shell navigation. Для date/numeric filters, folder export, grid action buttons, grid editor commits и richer assertions recorder hints ещё не полные.

## 3. Проблема
Recorder coverage сейчас фрагментарный: отдельные тесты подтверждают важные сценарии, но нет полной матрицы поддержанных capture paths. Из-за этого новые runtime workflows могут считаться "поддержанными" в матрице и authored tests, оставаясь невидимыми для recorder-а или записываясь как raw primitive steps.

## 4. Цели дизайна
- Разделение ответственности:
  - matrix определяет поддерживаемость;
  - `RecorderStepFactory` создаёт semantic step;
  - `RecorderSession` выбирает правильный capture trigger и suppress-ит implementation-detail steps;
  - `AuthoringCodeGenerator` рендерит DSL;
  - runtime validator проверяет action/control/payload readiness.
- Повторное использование:
  - переиспользовать существующие AAR contracts, parts records и payload concepts;
  - не создавать parallel recorder-only DSL, если уже есть `UiPageExtensions`.
- Тестируемость:
  - deterministic unit/contract tests являются основным gate;
  - desktop smoke остаётся representative e2e layer, а не единственным доказательством.
- Консистентность:
  - composite flows должны сохраняться как high-level intent;
  - unsupported rows должны иметь stable diagnostic, а не silent fallback.
- Обратная совместимость:
  - существующие action kinds и generated statements не меняют смысл;
  - новые action kinds добавляются additive в конец enum;
  - generated output остаётся C# source-only artifact без нового persisted binary/schema storage.

## 5. Non-Goals (чего НЕ делаем)
- Не автоматизируем OS-native dialogs, native file/folder picker windows и Eremex proprietary gestures без stable in-app parts.
- Не добавляем generic recognition любого custom wrapper без `AutomationId`, hints или adapter parts.
- Не меняем внешний consumer `Arm.Srv`.
- Не решаем layout persistence, docking drag/drop, floating/pin gestures.
- Не заменяем existing `RecorderTests.cs` wholesale, если можно добавить data-driven coverage рядом с текущими regression tests.
- Не требуем real desktop tests проходить в headless CI/неинтерактивной среде; они должны skip-аться по существующему guard.
- Не исправляем независимый parallel overlay race как часть этой задачи, если он не блокирует новые tests при sequential runner.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
| Компонент/файл | Ответственность |
| --- | --- |
| `tests/AppAutomation.Recorder.Avalonia.Tests` | Matrix tests, characterization tests, regression tests for capture/codegen/validator |
| `RecorderModels.cs` | Additive action kinds and payload fields only where runtime DSL already has a typed workflow |
| `RecorderStepFactory.cs` | Create semantic steps for every supported matrix row; return explicit unsupported diagnostics for non-capturable rows |
| `RecorderSession.cs` | Hook user-triggered capture paths in priority order: grid/composite/high-level first, primitive fallback last |
| `RecorderCommandRuntimeValidator.cs` | Validate each new action kind against target `UiControlType` and required payload |
| `AuthoringCodeGenerator.cs` | Render expected `Page.*` statements for every persistable action kind |
| `AppAutomationRecorderOptions.cs` | Add opt-in hints for new composite/grid editor capture rows if existing parts records are not enough |
| `sample/DotnetDebug.Avalonia/App.axaml.cs` | Wire recorder hints for DotnetDebug representative desktop flows |
| `sample/DotnetDebug.AppAutomation.FlaUI.Tests` | Add grouped real recorder smoke tests that save generated scenarios and assert source lines |
| `ControlSupportMatrix.md` | Optional update only if implementation changes current support status wording |

### 6.2 Детальный дизайн
#### 6.2.1 Coverage matrix as test data
Добавить единый источник truth для recorder coverage, например `RecorderCaptureCoverageMatrix` в test project.

Каждая row содержит:
- `UiControlType`;
- `RecordedActionKind` или explicit `UnsupportedByDesign`;
- capture source family (`Factory`, `Session`, `HotkeyAssertion`, `HintedComposite`, `DesktopSmoke`);
- required payload fields;
- expected generated DSL prefix/full line;
- runtime validation expectation (`Valid`, `Warning`, `Invalid`, target warning code);
- note/rationale.

Матрица должна покрыть все `RecordedActionKind` и все `UiControlType`. Для типов без user action фиксируются rows:
- `Label`: text assertions + enabled assertion, no direct click/edit action;
- `ProgressBar`: progress assertion only;
- `AutomationElement`: generic enabled/text assertions where text readable; no semantic action without hint/alias;
- `GridRow`, `GridCell`, `DataGridViewRow`, `DataGridViewCell`: not standalone page-object action targets in recorder; actions collapse to parent `Grid`/`DataGridView` where supported;
- `TreeItem`: direct `WaitUntilIsSelected` can be generated only when item is page-object target; normal tree selection records on parent `Tree`.

#### 6.2.2 Required recorder coverage by family
| Family | Existing / target action coverage |
| --- | --- |
| `TextBox` | `EnterText`, `SetSpinnerValue` through `RecorderActionHint.SpinnerTextBox`, `WaitUntilTextEquals`, `WaitUntilTextContains`, `WaitUntilIsEnabled` |
| `Button` | `ClickButton`, text assertions, enabled assertion; high-level composite/grid hints win over generic click |
| `CheckBox` | `SetChecked`, `WaitUntilIsChecked`, `WaitUntilIsEnabled` |
| `RadioButton` | `SetChecked`, `WaitUntilIsSelected`, `WaitUntilIsEnabled` |
| `ToggleButton` | `SetToggled`, `WaitUntilIsToggled`, `WaitUntilIsEnabled` |
| `ComboBox` | `SelectComboItem`, enabled assertion; search-picker composite collapse where configured |
| `ListBox` | `SelectListBoxItem`, `WaitUntilHasItemsAtLeast`, `WaitUntilListBoxContains`, shell/search-picker composite collapse where configured |
| `Slider` | `SetSliderValue`, enabled assertion |
| `ProgressBar` | `WaitUntilProgressAtLeast` via assertion capture |
| `Calendar`, `DateTimePicker` | `SetDate`, enabled assertion |
| `Spinner` | Native `Spinner` action if Avalonia source exposes a supported spinner control; existing text-box fallback remains supported |
| `Tab`, `TabItem` | `SelectTabItem`, `WaitUntilIsSelected`, shell pane activation when configured |
| `Tree`, `TreeItem` | `SelectTreeItem`, shell open/navigation when configured |
| `Grid`, `DataGridView` | row count/cell assertions; grid user actions; grid search picker; editable grid rows where configured |
| `SearchPicker` | `SearchAndSelect` for combo/list result surfaces |
| `DateRangeFilter` | `SetDateRangeFilter` for configured parts, apply/cancel modes |
| `NumericRangeFilter` | `SetNumericRangeFilter` for configured parts, apply/cancel modes |
| `Dialog` | `ConfirmDialog`, `CancelDialog`, `DismissDialog` |
| `Notification` | `WaitUntilNotificationContains`, `DismissNotification` |
| `FolderExport` | `SelectExportFolder` for select/cancel modes over stable in-app parts |
| `ShellNavigation` | `OpenOrActivateShellPane`, `ActivateShellPane`; `OpenShellPane` only if capture source unambiguously means open |

#### 6.2.3 Runtime DSL gaps to close
Если characterization matrix показывает отсутствие action kinds for public runtime workflows, добавить их additive:
- `WaitUntilProgressAtLeast`
- `WaitUntilListBoxContains`
- `WaitUntilHasItemsAtLeast`
- `WaitUntilNotificationContains`
- `SetDateRangeFilter`
- `SetNumericRangeFilter`
- `SelectExportFolder`
- grid edit actions covering:
  - text edit;
  - number edit;
  - date edit;
  - combo item select;
  - search picker select in a grid cell.

Payload model:
- Не сериализовать structured payload в ad hoc strings, если действие имеет typed runtime contract.
- Для range/filter/folder/grid-edit actions разрешены additive nullable fields in `RecordedStep`, например second date/double, commit mode, editor kind, search text.
- Existing generated statements for old actions remain byte-for-byte stable except where comments include new validation warnings for new rows.

#### 6.2.4 Capture priority and suppression rules
Recorder session order must stay high-level first:
1. configured grid user action / grid edit / grid search picker;
2. configured date/numeric filter / dialog / notification / folder export / shell navigation;
3. configured search picker;
4. primitive control capture.

Rules:
- Inner part interactions in a configured composite must not leak as raw `EnterText`/`ClickButton` if the final composite action can be built.
- If composite action cannot be built because payload is missing, recorder should record explicit unsupported diagnostic and avoid misleading primitive fallback for the same event.
- Generic primitive fallback remains for controls not covered by any matching hint.

#### 6.2.5 DotnetDebug desktop representative coverage
Desktop tests should be grouped by risk, not one process per action:
- primitive pack:
  - text, combo, checkbox, radio, toggle, slider, spinner, tab, tree, date/calendar and generated assertions;
- list/progress assertion pack:
  - list count/list contains/progress text/value assertion capture;
- grid pack:
  - grid row/cell assertions, user actions, grid edit, grid search picker if sample exposes stable parts;
- composite pack:
  - search picker, date range, numeric range, dialog confirm/cancel/dismiss, notification assert/dismiss, folder export select/cancel, shell open/activate.

Desktop smoke verifies generated source, not only UI outcome:
- expected `Page.*` statements exist;
- implementation-detail locators such as inner text boxes/buttons do not appear where high-level composite should be generated;
- generated files are written to temp output directory and cleaned up.

## 7. Бизнес-правила / Алгоритмы
- A recorder-supported action must satisfy all three:
  - can be inferred from an Avalonia user event or recorder assertion hotkey without guessing business state;
  - has a stable target descriptor through `[UiControl]`, alias, hint or composite parts;
  - can generate a public runtime DSL statement that is expected to compile.
- Unsupported-by-design means one of:
  - no user action exists for that control type (`ProgressBar` direct click/edit);
  - the action would require native OS/proprietary gestures not represented by stable parts;
  - source event cannot provide required payload deterministically.
- Hints are opt-in and exact by locator kind/value; no hard-coded DotnetDebug or Arm-specific locators in recorder core.
- Existing locator policy remains: `AutomationId` preferred; `Name` only when explicitly allowed/configured.
- Generated scenario must prefer semantic controls:
  - `SearchPicker`, `DateRangeFilter`, `NumericRangeFilter`, `Dialog`, `Notification`, `FolderExport`, `ShellNavigation`, `Grid`;
  - never internal implementation controls when a configured composite row succeeds.

## 8. Точки интеграции и триггеры
- `RecorderSession` handlers:
  - pointer/button events for click-like actions and composite action buttons;
  - text property changes for text/spinner/filter/grid edit payloads;
  - selection changes for combo/list/search/shell/tab/tree;
  - slider/date/calendar property changes;
  - hotkeys for assertions.
- `RecorderStepFactory` factory methods:
  - existing methods remain;
  - add focused methods for filter, folder export, notification assertion, progress/list assertions and editable grid where needed.
- `AuthoringCodeGenerator.GenerateStepStatement` renders every persistable action kind.
- `RecorderCommandRuntimeValidator.ValidateAction` must know every persistable action kind and payload requirement.
- `App.axaml.cs` sample recorder setup must mirror page factory composite setup where desktop recorder smoke depends on it.

## 9. Изменения модели данных / состояния
- Internal recorder model may gain additive action kinds and nullable payload fields.
- Public `AppAutomationRecorderOptions` may gain additive hint records for missing capture groups.
- No database, manifest version, binary persistence or external storage changes.
- Generated `.g.cs` source shape changes only for newly supported actions.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - additive code and tests only;
  - existing consumers keep compiling;
  - new recorder support is opt-in for composites via hints/parts.
- Rollback:
  - remove new action kinds/hints/factory paths/tests;
  - old primitive recorder behavior and existing smoke tests remain intact.
- Compatibility:
  - enum additions append at the end;
  - no existing action kind numeric values change;
  - old generated scenarios do not need migration.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- Matrix coverage:
  - every current and newly added `RecordedActionKind` has at least one test row;
  - every `UiControlType` has at least one classification row: supported action, assertion-only, parent-collapsed, legacy/runtime-only or unsupported-by-design.
- Capture:
  - factory/session tests cover each supported row;
  - composite/grid rows prove raw implementation-detail steps are suppressed when semantic capture succeeds.
- Codegen:
  - each supported action renders the expected `Page.*` statement;
  - generated scenario source compiles through existing project tests/build.
- Validation:
  - runtime validator accepts valid action/control/payload combinations;
  - missing payload and mismatched control type produce invalid diagnostics;
  - warning-only rows such as grid user action adapter requirement remain persistable when at least one selected target can handle them.
- Desktop:
  - DotnetDebug recorder smoke saves generated scenarios for representative primitive, assertion, grid and composite packs;
  - tests skip only when interactive desktop input is unavailable.
- Regression:
  - existing recorder, abstraction, authoring, headless/FlaUI sample tests remain green under the stable runner commands below.

Какие тесты добавить/изменить:
- `tests/AppAutomation.Recorder.Avalonia.Tests`
  - add data-driven coverage matrix tests for action/control/payload/codegen/validator;
  - add characterization tests for currently missing runtime DSL rows before fixing them;
  - add regression tests for composite suppression and unsupported diagnostics.
- `sample/DotnetDebug.AppAutomation.FlaUI.Tests`
  - extend recorder desktop smoke into grouped packs;
  - keep helper-based temp output and Debug launch behavior.
- `tests/AppAutomation.Abstractions.Tests` / `tests/AppAutomation.Authoring.Tests`
  - only if new public recorder-facing payload or source-generator mapping changes require compile/runtime proof.

Команды для проверки:
- `dotnet run --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --maximum-parallel-tests 1`
- `dotnet test --project sample/DotnetDebug.AppAutomation.FlaUI.Tests/DotnetDebug.AppAutomation.FlaUI.Tests.csproj`
- `dotnet test --project tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj`
- `dotnet test --project tests/AppAutomation.Authoring.Tests/AppAutomation.Authoring.Tests.csproj`
- `dotnet build`
- `dotnet test --solution AppAutomation.sln -- --maximum-parallel-tests 1`
- `git diff --check`

Stop rules для test/retrieval/tool/validation loops:
- До исправления каждого uncovered supported row сначала должен быть failing/characterization test.
- Если targeted test падает из-за новой логики, не переходить к full suite до устранения.
- Если full suite падает только из-за известного unrelated parallel overlay race, повторить documented sequential full suite и зафиксировать residual risk.
- Если desktop tests skip-нулись, не считать это провалом приёмки при условии, что non-desktop recorder coverage прошла.

## 12. Риски и edge cases
- Scope large: закрытие всех gaps может затронуть recorder model, codegen, factory, session and sample tests. Смягчение: matrix-first and phased implementation.
- Некоторые public DSL methods не имеют однозначного user-event capture (`OpenShellPane` vs `OpenOrActivateShellPane`). Смягчение: поддерживать только deterministic mapping, спорные aliases фиксировать как unsupported/alias rows.
- Date/numeric range and grid-edit payloads требуют structured fields. Смягчение: additive nullable fields, no ad hoc string serialization.
- UIA/Avalonia event ordering может давать raw primitive event before final composite action. Смягчение: suppression rules and debounce/pending-state tests.
- Desktop smoke can be slow/flaky. Смягчение: grouped tests, existing desktop availability guard, temp output, serialized `DesktopUi`.
- Generated output might collide with manual logical properties. Смягчение: temp output for desktop smoke and source-scanner tests that verify existing control reuse.

## 13. План выполнения
1. Добавить recorder coverage matrix test data and classification helpers.
2. Добавить failing/characterization tests for missing supported rows:
   - list/progress/notification assertions;
   - date/numeric filters;
   - folder export;
   - editable grid;
   - any missing validator/codegen rows.
3. Расширить recorder model/codegen/validator for required additive action kinds and payload fields.
4. Реализовать factory/session capture and suppression paths for new rows.
5. Обновить DotnetDebug recorder hints and desktop smoke grouped tests.
6. Запустить targeted tests after each block, затем full sequential solution suite.
7. Выполнить post-EXEC review and fix high-confidence findings before final report.

## 14. Открытые вопросы
Нет блокирующих вопросов до EXEC.

Если в процессе implementation появится действие, для которого есть несколько materially different public DSL forms and no uniquely best mapping, нужно остановиться и запросить решение пользователя. На момент SPEC единственный заранее известный спорный случай (`OpenShellPane` vs `OpenOrActivateShellPane`) имеет conservative mapping: recorder продолжает генерировать `OpenOrActivateShellPane` для navigation-source selection, а `ActivateShellPane` для pane-tab selection.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`
- Выполненные требования профиля:
  - UI behavior покрывается автоматическими UI/recorder tests.
  - Стабильные selectors остаются через `AutomationId`, hints and page object definitions.
  - Desktop tests используют существующий FlaUI suite and serialize real input.
  - Проверочные команды включают targeted tests, `dotnet build`, full solution test run.
  - Unsupported UI/runtime paths получают diagnostics instead of flaky gestures.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` или новые files рядом | Matrix/characterization/regression tests | Доказать полное recorder coverage |
| `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs` | Additive actions/payload fields if needed | Представить runtime DSL gaps в recorder model |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` | Additive hints if needed | Opt-in capture for composites/grid edit/folder/filter |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs` | New semantic step creation paths | Capture high-level intent |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | Event hooks and suppression order | Prevent primitive leakage |
| `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs` | Validation for every action kind | Runtime readiness diagnostics |
| `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs` | Generate DSL for new actions | Persist executable scenarios |
| `sample/DotnetDebug.Avalonia/App.axaml.cs` | Recorder hint setup for desktop smoke rows | Real sample capture coverage |
| `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/DotnetDebugRecorderDesktopSmokeTests.cs` | Grouped desktop recorder scenarios | Real startup/save verification |
| `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/MainWindowFlaUiPageFactory.cs` | Update only if new composite runtime wiring is needed | Keep desktop replay/helpers aligned |
| `ControlSupportMatrix.md` | Optional wording update | Keep support matrix truthful if recorder status changes |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Coverage visibility | Scattered recorder tests | Explicit matrix covering action/control/payload/codegen/validator |
| Runtime DSL parity | Some AAR workflows replay-only for recorder | Recorder captures supported typed workflows or emits tested unsupported diagnostics |
| Composite capture | Search/dialog/notification/shell slices only | Search, filters, dialog, notification, folder export, shell and grid composites classified and covered |
| Assertions | Text/checked/enabled/grid only | Adds list/progress/notification assertions where deterministic |
| Grid | Assertions/user actions/search picker partly covered | Includes editable grid capture rows where stable parts/payload exist |
| Desktop smoke | Medium targeted smoke | Grouped representative smoke across primitive/assertion/grid/composite families |

## 18. Альтернативы и компромиссы
- Вариант: ограничиться тестами текущего `RecordedActionKind`.
  - Плюсы: меньше изменений, быстрее.
  - Минусы: runtime DSL gaps останутся recorder blind spots; пользовательская цель "все допустимые действия" не закрывается.
  - Почему не выбран: текущая матрица поддержки уже говорит о typed workflows beyond recorder model.
- Вариант: делать только desktop e2e tests.
  - Плюсы: ближе к реальному recorder UX.
  - Минусы: медленно, flaky, не покрывает validator/codegen edge cases exhaustively.
  - Почему не выбран: unit/contract matrix надёжнее как coverage oracle, desktop smoke нужен как representative layer.
- Вариант: записывать все composite flows как primitive steps.
  - Плюсы: почти без model changes.
  - Минусы: теряется user intent, generated сценарии хрупкие, противоречит AAR typed API.
  - Почему не выбран: задача прямо про корректный recorder, а не raw event dump.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals описаны |
| B. Качество дизайна | 6-10 | PASS | Ответственность, алгоритмы, интеграции, модель и rollout раскрыты |
| C. Безопасность изменений | 11-13 | PASS | Additive model, rollback, no external consumer mutation |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, tests and commands are explicit |
| E. Готовность к автономной реализации | 17-19 | PASS | План фазовый; блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | .NET desktop/UI automation requirements учтены |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope tied to recorder capture coverage and current repo |
| 2. Понимание текущего состояния | 5 | AS-IS maps enums, runtime DSL, tests and sample wiring |
| 3. Конкретность целевого дизайна | 5 | Matrix, payload rules, capture priority and file responsibilities defined |
| 4. Безопасность (миграция, откат) | 5 | Additive actions/fields/hints and rollback path |
| 5. Тестируемость | 5 | Targeted, desktop and full verification commands included |
| 6. Готовность к автономной реализации | 5 | No blocking questions; decision rules for ambiguous mappings |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - scope расширен с "тесты" до "matrix-first tests + gap closure", чтобы соответствовать пользовательской цели довести recorder до working state;
  - добавлен conservative rule for `OpenShellPane` vs `OpenOrActivateShellPane`, чтобы не оставлять скрытый блокер;
  - добавлены assertion-only and unsupported-by-design rows для `ProgressBar`, `Label`, row/cell legacy types and generic containers.
- Что осталось на решение пользователя:
  - Ничего до EXEC. Подтверждение спеки является разрешением на additive recorder model/codegen changes в описанных границах.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Маршрутизация QUEST и inventory recorder contracts | 0.9 | Нет | Сформировать новую рабочую SPEC | Нет | Нет | Задача меняет поведение/тесты и требует SPEC-first; inventory показал gap между `RecordedActionKind` и runtime DSL | `AGENTS.md`, `C:\Projects\My\Agents\instructions\*`, `src/AppAutomation.Recorder.Avalonia/*`, `src/AppAutomation.Abstractions/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, `sample/DotnetDebug.*` |
| SPEC | Создание full coverage спеки | 0.88 | Подтверждение пользователя перед EXEC | Ожидать фразу `Спеку подтверждаю` | Да | Да, ожидается подтверждение | Scope large, поэтому зафиксированы matrix-first подход, additive model boundaries, тест-план и stop rules | `specs/2026-04-28-recorder-full-capture-coverage.md` |
| EXEC | Подтверждение спеки и старт реализации | 0.9 | Нужно снять фактические gaps тестами | Добавить matrix/characterization tests | Нет | Да, пользователь написал `спеку подтверждаю` | Фраза подтверждения переводит QUEST из SPEC в EXEC; дальше разрешены изменения кода в границах спеки | `specs/2026-04-28-recorder-full-capture-coverage.md` |
| EXEC | Matrix и factory/session coverage recorder actions | 0.82 | Нужно прогнать targeted tests, не только build | Добавить sample hints и desktop smoke | Нет | Нет | Добавлен contract test на все `RecordedActionKind`, codegen optional modes, factory capture для list/progress/notification/range/folder/grid-edit и session suppression от primitive leakage | `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderFullCaptureCoverageTests.cs`, `src/AppAutomation.Recorder.Avalonia/*` |
| EXEC | Desktop recorder smoke и sample automation contract | 0.86 | Нет | Запустить full FlaUI и full solution checks | Нет | Нет | Desktop smoke выявил реальные gaps: isolated build не находил authoring project, overlay мешал выбору main window, search picker терял pending text до selection, composite roots отсутствовали в visual tree. Исправлены env wiring, hotkey tunnel handling, diagnostic log, root `AutomationId` и реальные click/keyboard inputs в smoke | `sample/DotnetDebug.Avalonia/App.axaml.cs`, `sample/DotnetDebug.Avalonia/ArmDesktopControl.axaml`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/DotnetDebugRecorderDesktopSmokeTests.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderDiagnosticsEventIds.cs` |
| EXEC | Финальная валидация и post-EXEC review | 0.91 | Нет | Завершить отчёт пользователю | Нет | Нет | Targeted recorder, full FlaUI, affected test projects, `dotnet build`, full solution sequential test and `git diff --check` прошли; post-EXEC review не выявил критичных проблем, остались только существующие warnings | `AppAutomation.sln`, `specs/2026-04-28-recorder-full-capture-coverage.md` |
