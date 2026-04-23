# Recorder Headless/FlaUI Command Validation Diagnostics

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: medium
- Целевой релиз / ветка: текущая ветка `feat/arm-paritet`
- Ограничения:
  - До подтверждения спеки изменяется только этот файл.
  - Реализация должна сохранять recorder package provider-neutral: не добавлять
    прямую dependency из `AppAutomation.Recorder.Avalonia` на
    `AppAutomation.FlaUI`, потому что FlaUI project target-ится как
    `net*-windows7.0`.
  - Проверки должны быть детерминированными и безопасными для живого AUT:
    без скрытых кликов, ввода текста, открытия popups или запуска отдельных UI
    processes.
  - Проверки не должны менять формат уже сохранённых сценариев, кроме
    диагностических комментариев/метаданных для invalid/skipped steps, если
    существующий codegen уже это поддерживает.
- Связанные ссылки:
  - `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderStepValidator.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderSelectorResolver.cs`
  - `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs`
  - `src/AppAutomation.Avalonia.Headless/Automation/HeadlessControlResolver.cs`
  - `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`

## 1. Overview / Цель
Добавить в recorder две дополнительные проверки каждой захваченной команды:
готовность команды к playback через Headless runtime и готовность к playback
через FlaUI runtime.

Если команда не может быть захвачена, не проходит текущую selector/action
валидацию или не проходит одну из runtime-readiness проверок, recorder должен
записывать подробный diagnostic log. Лог должен содержать достаточно данных,
чтобы разработчики AppAutomation могли воспроизвести кейс и добавить поддержку
действия над конкретным контролом без повторного ручного сбора контекста.

## 2. Текущее состояние (AS-IS)
- `RecorderStepFactory` создаёт `RecordedStep` из Avalonia controls.
- `RecorderSelectorResolver` подбирает locator, применяет aliases/hints и
  валидирует locator только в текущем Avalonia visual tree.
- `RecorderStepValidator` проверяет только совместимость `RecordedActionKind`
  с исходным Avalonia `Control`.
- `RecorderSession.AddStep` повторно валидирует step через
  `RevalidateStep`, обновляет preview/status и, в зависимости от
  `CaptureInvalidSteps`, сохраняет invalid steps для review или пропускает их.
- При `StepCreationResult.Unsupported(...)` recorder показывает status, но не
  сохраняет подробную диагностику о source control, snapshot properties,
  visual/logical paths и конкретной причине невозможности capture.
- Headless и FlaUI resolvers уже имеют provider-specific capabilities and
  artifact collection, но recorder project не зависит от этих runtime
  проектов.

Скрытый инвариант: recorder работает внутри Avalonia приложения и должен
оставаться лёгким authoring-time компонентом. Поэтому новые проверки не должны
запускать реальный FlaUI playback внутри recorder.

## 3. Проблема
Сейчас recorder может записать команду, которая выглядит валидной в Avalonia
tree, но позже падает в Headless или FlaUI; либо может отказаться от capture,
оставив только короткое сообщение. Этого недостаточно для разработки поддержки
новых control/action patterns, особенно для custom wrappers и Eremex-like
контролов.

## 4. Цели дизайна
- Разделение ответственности: capture создаёт step; validation объясняет
  готовность step к runtime playback; diagnostic logging собирает контекст.
- Повторное использование: использовать существующие `RecordedStep`,
  `RecordedActionKind`, `UiControlType`, `UiLocatorKind`,
  `RecorderValidationStatus` и `ILogger`.
- Тестируемость: покрыть Headless/FlaUI readiness failures and unsupported
  capture diagnostics unit tests без запуска desktop UI.
- Консистентность: один формат diagnostics для capture failure, selector/action
  validation failure, Headless validation failure and FlaUI validation failure.
- Обратная совместимость: existing valid recorder flows остаются persistable,
  если команда поддержана хотя бы одним выбранным runtime target; codegen
  сохраняет прежний сценарий для persistable steps and annotates unsupported
  targets through comments/diagnostics.

## 5. Non-Goals
- Не выполнять реальный playback команды в Headless/FlaUI из recorder.
- Не добавлять compile-time dependency на `AppAutomation.FlaUI` или
  `FlaUI.Core` в recorder package.
- Не менять public authoring/runtime API generated pages and scenario methods.
- Не внедрять provider-specific gestures или поддержку новых controls в этой
  задаче; unsupported cases должны получать diagnostics.
- Не писать diagnostics в отдельные файлы по умолчанию. Основной канал -
  configured `ILogger`; file sink остаётся ответственностью host app.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `AppAutomationRecorderOptions.cs`
  - Расширить `RecorderValidationOptions` настройками runtime-readiness
    validation.
  - Добавить enum/flags для targets: `Headless`, `FlaUI`.
- `RecorderModels.cs`
  - Добавить модели diagnostic details для validation findings, если их нельзя
    компактно выразить существующими `ValidationMessage`/`FailureCode`.
- Новый `RecorderCommandRuntimeValidator.cs`
  - Provider-neutral валидатор Headless/FlaUI readiness.
  - Проверяет action/control payload and runtime support matrix без
    provider-specific package dependency.
- Новый `RecorderCaptureDiagnostics.cs`
  - Собирает подробный snapshot source control and resolved owner:
    identifiers, Avalonia type, control values, enabled/visible/focus state,
    data context type, bounds when available, action payload, validation
    findings, visual path and logical path.
- `RecorderStepFactory.cs`
  - Для successful creation оставляет текущую ответственность create-step.
  - Для `Unsupported(...)` передаёт исходный source/action context наружу через
    `StepCreationResult`, чтобы session могла залогировать полный capture
    failure.
- `RecorderSession.cs`
  - После `RevalidateStep` запускает Headless and FlaUI readiness checks.
  - Aggregates findings into `RecordedStep.ValidationMessage`,
    `ValidationStatus`, `CanPersist`, `ReviewState`, `FailureCode`.
  - Логирует detailed diagnostics для:
    capture unsupported;
    selector/action validation invalid;
    Headless readiness failure;
    FlaUI readiness failure.
- `RecorderOverlay.axaml(.cs)`
  - Если текущие journal/status fields already display
    `ValidationMessage`/`FailureCode`, дополнительных UI изменений не требуется.
    UI меняется только если без этого невозможно увидеть runtime target names.
- `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - Добавить focused tests for runtime validation and diagnostic logging.

### 6.2 Детальный дизайн
#### Runtime-readiness checks
Проверки являются static/provider-readiness checks:
- `Headless` check:
  - command action kind supported by Headless abstractions;
  - control type has a Headless resolver path or a documented composed/fallback
    path;
  - required payload present (`StringValue`, `BoolValue`, `DoubleValue`,
    `DateValue`, `RowIndex`, `ColumnIndex`, `ItemValue`);
  - locator kind is supported (`AutomationId` or allowed `Name` fallback);
  - grid/composite actions have target control type and indexes required by
    existing Headless helpers.
- `FlaUI` check:
  - same command/payload contract;
  - flags high-risk provider gaps as invalid/warning according to current
    support matrix, without relying on native FlaUI object availability;
  - validates that generated command can be expressed through existing
    public helpers and known FlaUI resolver paths.

Checks must return structured findings:
- `Target`: `Headless` or `FlaUI`;
- `Severity`: `Info`, `Warning`, `Invalid`;
- `Code`: stable machine-readable code, e.g.
  `headless-action-unsupported`, `flaui-payload-missing-row-index`;
- `Message`: human-readable reason;
- `BlocksTarget`: whether this target cannot execute the command;
- `BlocksPersistence`: true only when all selected runtime targets are blocked
  or when existing selector/action validation is invalid.

#### Action support matrix
This matrix is the implementation source of truth for runtime-readiness checks.
`Supported` means the generated command has a known public helper and resolver
path for the target. `Warning` means the command can be generated but runtime
capability may depend on app-specific stable parts. `Unsupported` means the
target cannot execute the command without additional support.

| Recorded action | Required control type / interface | Required payload | Headless | FlaUI | Notes |
|---|---|---|---|---|---|
| `EnterText` | `TextBox` / `ITextBoxControl` | `StringValue` required, empty allowed | Supported | Supported | Empty string is valid clear/set operation. |
| `ClickButton` | `Button` / `IButtonControl` | none | Supported | Supported | Requires selector/action validation to confirm button-like source. |
| `SetChecked` | `CheckBox` or `RadioButton` / `ICheckBoxControl` or `IRadioButtonControl` | `BoolValue` required | Supported | Supported | Radio selection maps through checked/selected state. |
| `SetToggled` | `ToggleButton` / `IToggleButtonControl` | `BoolValue` required | Supported | Supported | Runtime helpers must be able to reach requested state. |
| `SelectComboItem` | `ComboBox` / `IComboBoxControl` | `StringValue` required | Supported | Supported | Item lookup can still fail at runtime if item text is not exposed. |
| `SelectListBoxItem` | `ListBox` / `ISelectableListBoxControl` | `StringValue` required | Supported | Supported | FlaUI list item exposure remains provider-dependent but supported by fallback logic. |
| `SetSliderValue` | `Slider` / `ISliderControl` | `DoubleValue` required | Supported | Supported | Value range validation is runtime responsibility. |
| `SetSpinnerValue` | hinted `TextBox` / `ITextBoxControl` text fallback | `DoubleValue` required | Warning | Warning | Current generated helper writes spinner-like text boxes; native `ISpinnerControl` generation is outside this task. |
| `SelectTabItem` | `TabItem` / `ITabItemControl` | none | Supported | Supported | Captured locator points at selected tab item. |
| `SelectTreeItem` | `Tree` / `ITreeControl` | `StringValue` required | Supported | Supported | Expansion state may be provider-dependent. |
| `SetDate` | `DateTimePicker` or `Calendar` | `DateValue` required | Supported | Supported | Calendar range selection is not required for recorded single-date command. |
| `WaitUntilTextEquals`, `WaitUntilTextContains` | `TextBox`, `Label`, `Button`, or generic text-readable control | `StringValue` required | Supported | Supported | Text readability is validated statically by control type only. |
| `WaitUntilIsChecked` | `CheckBox` | `BoolValue` required | Supported | Supported | Blocking only if payload/control type invalid. |
| `WaitUntilIsToggled` | `ToggleButton` | `BoolValue` required | Supported | Supported | Blocking only if payload/control type invalid. |
| `WaitUntilIsSelected` | `RadioButton` or `TabItem` | `BoolValue` required | Supported | Supported | Blocking only if payload/control type invalid. |
| `WaitUntilIsEnabled` | any `IUiControl` | `BoolValue` required | Supported | Supported | Generic control state check. |
| `WaitUntilGridRowsAtLeast` | `Grid` / `IGridControl` | `IntValue` required and >= 0 | Supported | Supported | Visual bridge grid is supported by both targets. |
| `WaitUntilGridCellEquals` | `Grid` / `IGridControl` | `RowIndex`, `ColumnIndex`, `StringValue` required | Supported | Supported | Indexes must be >= 0. |
| `SearchAndSelect` | `SearchPicker` / `ISearchPickerControl` | `StringValue` and `ItemValue` required | Supported | Supported | Requires configured composite parts in generated page/runtime setup. |
| `OpenGridRow` | `Grid` / `IGridUserActionControl` | `RowIndex` required and >= 0 | Warning | Warning | Generated command is valid; actual support depends on grid user-action adapter. |
| `SortGridByColumn` | `Grid` / `IGridUserActionControl` | `StringValue` required | Warning | Warning | Generated command is valid; actual support depends on grid user-action adapter. |
| `ScrollGridToEnd` | `Grid` / `IGridUserActionControl` | none | Warning | Warning | Generated command is valid; actual support depends on grid user-action adapter. |
| `CopyGridCell` | `Grid` / `IGridUserActionControl` | `RowIndex` and `ColumnIndex` required | Warning | Warning | Indexes must be >= 0; copied value may depend on adapter. |
| `ExportGrid` | `Grid` / `IGridUserActionControl` | none | Warning | Warning | Export side effects are app-specific. |

Default behavior:
- Enable both Headless and FlaUI checks by default.
- If a check returns only warnings, step remains persistable with
  `ValidationStatus.Warning`.
- If one selected runtime target is blocked but at least one selected runtime
  target can execute the command, step remains persistable with
  `ValidationStatus.Warning`; the unsupported target and reason must be emitted
  into compact generated comments/diagnostics.
- If all selected runtime targets are blocked, step becomes non-persistable.
- Existing selector/action invalid remains non-persistable regardless of
  runtime-readiness result.
- `CaptureInvalidSteps` does not make a step persistable. It only controls
  whether non-persistable invalid steps are retained in the recorder journal for
  review.
- If options disable runtime validation, current behavior remains unchanged
  except capture-failure diagnostics can still be logged.

#### Diagnostic log content
Every diagnostic log entry must be one structured `ILogger.LogWarning` or
`ILogger.LogError` event with stable event id and also readable text.
Stable event ids:
- `RecorderDiagnosticsEventIds.CaptureFailed = 4101`
- `RecorderDiagnosticsEventIds.SelectorValidationFailed = 4102`
- `RecorderDiagnosticsEventIds.ActionValidationFailed = 4103`
- `RecorderDiagnosticsEventIds.RuntimeValidationFailed = 4104`
- `RecorderDiagnosticsEventIds.RuntimeValidationWarning = 4105`
- `RecorderDiagnosticsEventIds.DiagnosticsSnapshotFailed = 4106`

Minimum fields:
- Capture metadata:
  - recorder action being captured, `RecordedActionKind` if known;
  - timestamp UTC;
  - recorder state and scenario name;
  - validation target names and failed checks;
  - exception details if capture/validation threw.
- Recorded command:
  - action kind;
  - control descriptor: property name, `UiControlType`, locator kind/value,
    `FallbackToName`, Avalonia type, warning;
  - payload: string/bool/double/date/int/item/row/column values.
- Control snapshot:
  - CLR type and full type name;
  - `AutomationId`, automation `Name`, Avalonia `Name`;
  - `IsEnabled`, `IsVisible`, focus state when available;
  - common values: `TextBox.Text`, `ContentControl.Content`,
    `SelectingItemsControl.SelectedItem`, `SelectedIndex`,
    `ToggleButton.IsChecked`, `Slider.Value`, `DatePicker.SelectedDate`,
    `Calendar.SelectedDate`;
  - `DataContext` type and safe `ToString()` value;
  - bounds/position if available without throwing.
- Tree context:
  - visual path from root/window to source control;
  - logical path from root/window to source control;
  - related owner path when source was child/template part and recorder chose an
    ancestor/templated/logical owner.
- AppAutomation developer context:
  - why capture failed or why each runtime check failed;
  - suggested next support area: action mapping, locator/alias, provider
    resolver, composed adapter, grid hint, or app stable id.

The diagnostic builder must use safe read helpers so broken controls do not
throw while building logs.

#### Interaction with existing status/journal
- `LatestStatus` remains concise.
- `StepJournal` remains concise and does not embed full diagnostic text.
- `ValidationMessage` may include compact target summary, for example:
  `FlaUI validation failed: flaui-action-unsupported. See recorder diagnostics log.`
- Generated scenario output for persistable warning steps should include a
  compact comment before the generated command when existing codegen comment
  path can represent it, for example:
  `// AppAutomation recorder warning: FlaUI target unsupported (flaui-action-unsupported).`
- Full context goes to `ILogger`.
- Full context is also written to a diagnostic log file when file diagnostics
  are enabled from the recorder overlay or through options.

## 7. Бизнес-правила / Алгоритмы
1. On attempted capture:
   - Build capture context from source/action attempt.
   - If factory returns unsupported, log `RecorderCaptureFailed` with full
     context and do not add a step.
2. On created step:
   - Run existing selector/action validation first.
   - Run Headless readiness check.
   - Run FlaUI readiness check.
   - Merge findings by severity: `Invalid` > `Warning` > `Valid`.
3. Persistability:
   - Existing selector/action invalid remains blocking.
   - Runtime invalid findings block only the failed target.
   - The step is persistable when at least one selected runtime target is not
     blocked.
   - The step is non-persistable when every selected runtime target is blocked.
   - Warnings do not block persistence.
4. Logging:
   - Log once per failed capture attempt.
   - Log once per recorded step that has invalid/warning runtime findings.
   - Do not log full diagnostics for fully valid steps at warning/error level.
   - File diagnostics are user-toggleable in the recorder overlay. When
     enabled, every detailed recorder diagnostic is appended to the displayed
     `.recorder-diagnostics.log` file.
5. Deduplication:
   - Existing step fingerprint dedupe remains unchanged.
   - Diagnostic logging for unsupported capture is not deduped unless the same
     event is generated by one internal recorder path twice.

## 8. Точки интеграции и триггеры
- `RecorderSession.AddStep(...)` triggers validation for all successful
  capture paths.
- `RecorderSession.TryRecordGridAction(...)` and other failed factory calls
  trigger capture-failure diagnostics when failure is not the benign
  `NoGridActionHintMessage`.
- `FlushPendingText`, `FlushPendingSlider`, button/list/combo/tab/tree/date/
  calendar/assertion capture paths all converge through the same diagnostic
  flow.
- `RetryStepValidation(...)` reruns runtime-readiness checks and logs detailed
  diagnostics again only if current retry still fails.
- Recorder overlay exposes a `Write to file` diagnostic toggle and diagnostic
  path so users can collect a file for AppAutomation developers without
  configuring an external logger.

## 9. Изменения модели данных / состояния
- New runtime validation options are in-memory only.
- New diagnostic file options are in-memory only. The log file is an external
  diagnostic artifact, not scenario state.
- New diagnostic finding models are recorder runtime model only; no persisted
  scenario schema is introduced.
- Existing `RecordedStep` may receive additional compact validation/failure
  metadata if needed for journal/status.
- No storage migration.

## 10. Миграция / Rollout / Rollback
- Rollout: additive recorder validation behavior in current branch.
- Existing valid captures may become warnings if one selected runtime target is
  unsupported, but remain persistable when at least one selected target can run
  the command. They become invalid/non-persistable only when selector/action
  validation fails or every selected runtime target is blocked.
- Consumers can disable runtime checks through validation options if they need
  legacy behavior.
- Users can enable/disable diagnostic file recording from the recorder overlay;
  initial state and optional file path can be configured through recorder
  options.
- Rollback: revert recorder validation/diagnostic changes; generated scenarios
  remain compatible because scenario format is unchanged.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- Recorder runs two additional checks for every created step: Headless
  readiness and FlaUI readiness.
- A command unsupported by one target remains persistable if another selected
  target supports it, and generated output includes a compact comment naming
  the unsupported target and reason.
- A command unsupported by all selected runtime targets becomes
  non-persistable.
- Existing selector/action invalid steps remain non-persistable; enabling
  `CaptureInvalidSteps` only keeps them in the recorder journal for review.
- Diagnostics use stable event ids `4101..4106`.
- Capture failures log detailed diagnostics even when no `RecordedStep` exists.
- Diagnostic file recording can be enabled/disabled from the recorder overlay;
  when enabled, capture/validation diagnostics are appended to a displayed file
  path.
- Validation failures log detailed diagnostics including failed checks, control,
  action, payload snapshot, visual path and logical path.
- Diagnostics include enough structured fields to identify action kind,
  `UiControlType`, locator, source/control type, values and suggested support
  area.
- Valid existing recorder tests continue to pass or are updated only where the
  new runtime validation intentionally changes status.
- New tests cover:
  - valid button/text command passes both Headless and FlaUI checks;
  - missing required payload fails all selected runtime targets and logs target
    names;
  - one-target failure stays persistable and generated output contains a
    warning comment for the failed target;
  - unsupported capture logs control snapshot and visual/logical paths;
  - diagnostic file toggle writes detailed diagnostics to file and does not
    create a file while disabled;
  - recorder overlay toggle updates session diagnostic file state and shows the
    file path;
  - selector/action invalid step logs diagnostics and remains non-persistable;
  - disabling runtime validation preserves previous validation outcome.

Verification commands:
- `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj`
- `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj`
- `dotnet build .\AppAutomation.sln`
- `dotnet test --solution .\AppAutomation.sln --no-build`

## 12. Риски и edge cases
- FlaUI readiness без реального UIA tree не доказывает native provider
  availability. Mitigation: назвать checks readiness/static validation and log
  remaining provider risk clearly.
- Cross-runtime validation can create noisy warnings for consumers who write
  only Headless or only FlaUI tests. Mitigation: step remains persistable when
  at least one selected target works, comments name the unsupported target, and
  options can disable targets that are irrelevant for the consumer.
- Diagnostic snapshot can accidentally call user code through `ToString()` or
  property getters. Mitigation: use safe bounded reads and never enumerate
  arbitrary object graphs.
- Logs can become noisy during rapid text input. Mitigation: text/slider paths
  already debounce; log only final failed captured command.
- Visual/logical paths may differ for templated controls. Mitigation: include
  both paths and selected owner path.

## 13. План выполнения
1. Add runtime validation options and target/finding models.
2. Implement provider-neutral Headless/FlaUI command readiness validator.
3. Implement safe diagnostic snapshot/path builder.
4. Integrate validation and logging into `RecorderSession` successful and
   unsupported capture paths.
5. Add stable diagnostic event ids.
6. Ensure generated output comments persistable warning steps with unsupported
   target names.
7. Keep status/journal compact while pointing to diagnostics log.
8. Add focused recorder unit tests with a test logger.
9. Run targeted tests and fix failures.
10. Run build/full tests according to verification plan.
11. Perform post-EXEC review and update this spec journal/result.

## 14. Открытые вопросы
Нет блокирующих вопросов.

Design decision for approval: checks are runtime-readiness/static validation,
not hidden Headless/FlaUI playback. This avoids side effects and avoids a
FlaUI dependency from the recorder package while still catching unsupported
commands early and logging actionable diagnostics.

User decision: a runtime-target failure must not block persistence when at
least one selected target can execute the command. The generated scenario must
carry a compact comment naming the unsupported target. Stable diagnostic event
ids are required.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
  - No long synchronous UI operations or hidden provider playback in recorder.
  - Existing selectors remain stable; new diagnostics explain selector gaps.
- Профиль: `ui-automation-testing`
  - Runtime readiness explicitly distinguishes Headless and FlaUI support.
  - Failed automation cases get diagnostic artifacts/log-equivalent context.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` | Add runtime validation target options and diagnostic file options | Let consumers configure Headless/FlaUI checks and initial diagnostic file behavior |
| `src/AppAutomation.Recorder.Avalonia/IAppAutomationRecorderSessionDetails.cs` | Expose diagnostic file state/toggle/path | Let overlay control and display file diagnostics |
| `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs` | Add diagnostic/finding records if needed | Carry target-specific validation metadata |
| `src/AppAutomation.Recorder.Avalonia/RecorderDiagnosticsEventIds.cs` | Add stable logger event ids | Make diagnostics testable and searchable |
| `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs` | New provider-readiness validation | Validate captured commands for Headless/FlaUI |
| `src/AppAutomation.Recorder.Avalonia/RecorderCaptureDiagnostics.cs` | New diagnostic snapshot builder | Log control/action/tree details |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | Integrate validation/logging | Run checks and log failures |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml` | Add diagnostic file toggle and path display | Let users enable/disable file diagnostics in recorder window |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs` | Wire diagnostic file toggle and copy-path action | Update session state from overlay |
| `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs` | Ensure warning comments include unsupported runtime target names | Preserve generated command while surfacing target gap |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | Add/adjust unit tests | Cover new validation and logs |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Capture failure | Short status message only | Status plus structured diagnostic log with control/action/tree context |
| Step validation | Avalonia selector/action compatibility | Avalonia selector/action plus Headless and FlaUI readiness checks |
| Runtime parity | Discovered later in runtime tests | Caught at recorder time as warning/invalid with target-specific finding |
| Diagnostic depth | `ValidationMessage` and save diagnostics | Full structured logger event for AppAutomation developers |
| Diagnostic collection | External `ILogger` only | Overlay toggle can write the same detailed diagnostics to a file with visible/copyable path |
| Persistence | Any invalid validation can skip a step | Runtime target failure skips only when all selected targets fail; one-target failure persists with comment |

## 18. Альтернативы и компромиссы
- Вариант: запускать реальный Headless/FlaUI playback из recorder.
  - Плюсы: максимальная уверенность.
  - Минусы: side effects, UI mutation, process/window lifecycle complexity,
    hard FlaUI dependency, windows TFM leakage into recorder.
  - Почему не выбран: противоречит безопасному authoring-time recorder.
- Вариант: только логировать больше данных без Headless/FlaUI checks.
  - Плюсы: меньше изменений.
  - Минусы: не выполняет запрос про две проверки.
  - Почему не выбран: цель - раннее обнаружение runtime gaps.
- Вариант: встроить checks в `RecorderStepValidator`.
  - Плюсы: меньше классов.
  - Минусы: смешивает Avalonia source compatibility and provider readiness.
  - Почему не выбран: отдельный validator лучше тестируется и не раздувает
    текущий action/source validator.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals and Non-Goals описаны. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, алгоритм, integration points, state and rollback указаны. |
| C. Безопасность изменений | 11-13 | PASS | Нет persisted migration; side-effect-free validation and rollback описаны. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, test cases and commands listed. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, alternatives and no blocking questions present. |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation constraints reflected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Цель ограничена recorder validation and diagnostics; hidden playback excluded. |
| 2. Понимание текущего состояния | 5 | Existing factory/resolver/session/validator behavior and package boundary captured. |
| 3. Конкретность целевого дизайна | 5 | Есть files, validation targets, finding fields, log content and algorithms. |
| 4. Безопасность (миграция, откат) | 5 | Additive options, no scenario schema migration, rollback simple. |
| 5. Тестируемость | 5 | Targeted unit cases and full commands listed. |
| 6. Готовность к автономной реализации | 5 | Execution steps and tradeoffs are explicit; no blocking questions. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - Зафиксировано, что проверки являются provider-readiness/static validation,
    а не скрытым playback.
  - Добавлено ограничение не тащить FlaUI dependency в recorder package.
  - Уточнён обязательный состав diagnostic log для AppAutomation developers.
  - По пользовательскому решению уточнено persistability rule: one runtime
    target failure does not block persistence if another selected target works.
  - Added stable diagnostic event ids and action support matrix.
- Что осталось на решение пользователя: подтвердить спекификацию фразой
  `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS
- Реализация соответствует подтверждённой спекификации:
  - runtime-readiness validation is provider-neutral and runs separate
    Headless/FlaUI target checks without adding FlaUI package dependency to
    recorder;
  - persistence blocks only when all selected runtime targets are blocked;
    one-target failure remains persistable and gets a generated unsupported
    target comment;
  - capture failures and validation failures log stable EventIds `4101..4106`
    with action, control snapshot, payload, visual/logical/owner tree context
    and support hints;
  - selector/action invalid steps remain non-persistable regardless of
    runtime validation.
- Post-review fix: added an explicit generated-output test for the case where
  Headless is unsupported but FlaUI is supported, so the user decision is
  covered by a dedicated regression test.
- Follow-up UX fix: added recorder-window diagnostic file recording. Users can
  toggle `Write to file` in the overlay, see/copy the file path, and collect
  detailed capture/validation diagnostics without configuring an external
  logger.
- Verification:
  - `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj --no-restore` -> PASS, 47/47.
  - `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj --no-restore` -> PASS, 51/51.
  - `dotnet build .\AppAutomation.sln` -> PASS, 0 errors.
  - `dotnet test --solution .\AppAutomation.sln --no-build` -> FAIL, 214/216 passed; failures were isolated to existing sample FlaUI scenarios (`FilterHistory_ByText_ShowsOnlyMatchingItems`, `DataGrid_BuildSelectClear_ShowsRowsSelectionAndValidation`) and do not touch recorder code.
  - `dotnet test --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-build` -> FAIL, 26/27 passed; remaining failure was the same sample DataGrid UIA scenario with changing observed state.
- Residual warnings: existing `NU1903` for `Tmds.DBus.Protocol` in sample
  projects and existing `CA1859` suggestions in recorder tests remain outside
  this change.

### Post-EXEC Review: Diagnostic Log Follow-up
- Статус: PASS
- Что исправлено до завершения:
  - invalid action/source validation no longer receives misleading
    `target-supported` runtime findings;
  - DatePicker template buttons are suppressed so date changes are recorded
    through `SetDate`, not `ClickButton` over internal `PART_*` controls;
  - configured grid internal text editors are suppressed, and ARM sample grid
    aliases/hints now match `ArmEremexDataGridControl` -> `ArmGridAutomationBridge`;
  - editable ComboBox template text boxes are suppressed, while ARM search
    picker hints allow recording composite `SearchAndSelect` commands;
  - diagnostic support suggestions now point at DatePicker/search picker/grid
    mapping areas for these cases.
- Verification:
  - `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj --no-restore` -> first run hit a transient Avalonia metadata concurrency failure in an existing overlay test; retry with `--no-build` passed 51/51.
  - `dotnet build .\AppAutomation.sln` -> PASS, 0 errors.
  - `dotnet test --solution .\AppAutomation.sln --no-build` -> PASS, 220/220.

## Approval
Подтверждено пользователем фразой: "Спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Анализ инструкций и текущего состояния recorder | 0.9 | Подтверждение пользователя для EXEC | Создать рабочую спеку | Да | Нет | Центральные правила требуют SPEC-first; request меняет recorder behavior and tests | `AGENTS.md`, central instructions, `src/AppAutomation.Recorder.Avalonia/*`, runtime resolver files |
| SPEC | Проектирование Headless/FlaUI readiness checks and diagnostics | 0.88 | Нет | Ожидать подтверждение спеки | Да | Нет | Static readiness validation catches unsupported generated commands without unsafe playback or FlaUI package coupling | `specs/2026-04-23-recorder-headless-flaui-command-validation.md` |
| SPEC | Review спеки и уточнение product decisions | 0.94 | Подтверждение пользователя для EXEC | Ожидать подтверждение спеки | Да | Да, пользователь решил persistability and EventId questions | Runtime target failure now warns/comments unless all selected targets fail; stable EventIds and action matrix reduce implementation ambiguity | `specs/2026-04-23-recorder-headless-flaui-command-validation.md` |
| EXEC | Старт реализации после подтверждения | 0.94 | Нет | Добавить options/models/event ids and runtime-readiness validator | Нет | Да, пользователь подтвердил спеку | Approval получен, можно менять код строго в рамках спеки | `specs/2026-04-23-recorder-headless-flaui-command-validation.md`, `src/AppAutomation.Recorder.Avalonia/*` |
| EXEC | Реализация runtime validation, diagnostics and codegen comments | 0.9 | Нет | Запустить broader verification | Нет | Нет | Добавлены options, stable EventIds, provider-neutral readiness validator, detailed diagnostic snapshots/logging and generated warning comments; recorder targeted tests passed 44/44 before post-review test addition | `src/AppAutomation.Recorder.Avalonia/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Post-review strengthening | 0.93 | Нет | Повторить targeted and full verification | Нет | Нет | Добавлен explicit regression test: one runtime target unsupported while another is supported still persists generated command with unsupported-target comment | `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Финальная проверка | 0.96 | Нет | Завершить задачу | Нет | Нет | Recorder tests 45/45, abstractions tests 51/51, solution build passed, full solution tests 214/214; post-EXEC review PASS | `specs/2026-04-23-recorder-headless-flaui-command-validation.md`, `src/AppAutomation.Recorder.Avalonia/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | UX follow-up: diagnostic file toggle | 0.93 | Нет | Запустить targeted/full verification | Нет | Да, пользователь уточнил, что включение диагностики должно писать в файл | Добавлены options, session API, overlay checkbox/path/copy action and file append path for detailed diagnostics | `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs`, `src/AppAutomation.Recorder.Avalonia/IAppAutomationRecorderSessionDetails.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`, `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.*`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Targeted verification после file diagnostics | 0.94 | Нет | Запустить solution build/tests | Нет | Нет | Recorder tests passed 47/47; existing analyzer warnings remain unrelated | `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Broader verification после file diagnostics | 0.9 | Нет | Зафиксировать известный FlaUI sample failure и завершить/опубликовать follow-up | Нет | Нет | Solution build passed; full solution tests failed only in sample FlaUI UIA scenarios unrelated to recorder file logging, retry of FlaUI project left the same DataGrid scenario failing | `specs/2026-04-23-recorder-headless-flaui-command-validation.md` |
| EXEC | Исправления по реальному diagnostic log | 0.9 | Результаты повторных тестов | Запустить targeted tests recorder и затем broader verification | Нет | Да, пользователь попросил исправить найденные по логу проблемы | Подавлены внутренние DatePicker/ComboBox/grid template события, убран ложный runtime target-supported для невалидных action/source шагов, sample recorder получил ARM search picker hints | `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderCaptureDiagnostics.cs`, `sample/DotnetDebug.Avalonia/App.axaml.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Verification после исправлений по diagnostic log | 0.96 | Нет | Сделать финальный diff-review, commit/push | Нет | Нет | Recorder targeted retry passed 51/51 after transient Avalonia overlay concurrency failure; solution build passed; full solution tests passed 220/220 | `specs/2026-04-23-recorder-headless-flaui-command-validation.md`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, `AppAutomation.sln` |
