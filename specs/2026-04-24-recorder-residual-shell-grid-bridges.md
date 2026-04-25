# Recorder Residual Shell And Grid Bridges

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: AppAutomation + Arm.Srv
- Масштаб: medium
- Целевой релиз / ветка: текущая ветка `feat-arm-paritet`
- Ограничения:
  - До подтверждения спеки изменяется только этот файл.
  - Источник остаточных симптомов: `sample/DotnetDebug.AppAutomation.Authoring/Recorded/RecordedSmoke.20260423-234409.recorder-diagnostics.log`.
  - Эта спека не переоткрывает уже закрытый пакет из `2026-04-24-recorder-composite-desktop-capture-followup.md`; она покрывает только residual gaps, которые остались после него.
  - Не добавлять production dependency между `AppAutomation` и `Arm.Srv`.
  - Не возвращаться к generic поддержке произвольных wrapper/date-editor cases; эта задача ограничена docking shell activation и grid-embedded `OrderPositionProductEditor`.
  - Не добавлять vendor-specific mouse/keyboard gestures для Eremex docking в core runtimes.
- Связанные ссылки:
  - `sample/DotnetDebug.AppAutomation.Authoring/Recorded/RecordedSmoke.20260423-234409.recorder-diagnostics.log`
  - `src/AppAutomation.Abstractions/UiControlAdapters.cs`
  - `src/AppAutomation.Abstractions/UiPageExtensions.cs`
  - `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs`
  - `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`
  - `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\App.axaml.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\MainControl.axaml`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\PaneAdapter.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\MainWindowPage.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\OrdersPage.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI`

## 1. Overview / Цель
Закрыть два оставшихся класса событий из исходного diagnostic log, которые не ушли после предыдущего composite package:

- activation в Eremex docking shell, записанный с `TabbedGroupItemsControl`, но не переводимый в стабильный `ShellNavigation` authoring contract;
- `OrderPositionProductEditor` внутри grid, где пользовательский ввод идёт через search-picker editor, а deterministic replay должен идти через grid bridge и `SearchAndSelectGridCell(...)`.

Оба остатка имеют одну и ту же форму: пользователь взаимодействует не с тем control surface, по которому сценарий должен воспроизводиться. В этой спеке вводится явный capture-to-replay bridge для shell и grid editor flows, чтобы recorder писал высокоуровневый intent на стабильную replay surface, а не primitive действия по vendor/internal controls.

## 2. Текущее состояние (AS-IS)
- В `AppAutomation` уже реализованы:
  - list-backed `SearchPicker`;
  - `Dialog` / `Notification` capture;
  - `ShellNavigation` runtime and recorder hints;
  - grid read/user-action/edit runtime APIs, включая `SearchAndSelectGridCell(...)`.
- В `Arm.Srv` уже исправлены:
  - duplicate `CreateOrderButton`;
  - dialog/toast ids;
  - stable part ids для `ServerSearchComboBox` и targeted popup editors;
  - recorder config для `OrderCustomerSearch`, `OrderDeliveryAddressCombo`, `OrderContactPersonCombo`.
- Исходный лог теперь оставляет два residual gaps:
  - `FailureMessage: TabControl does not expose a selected TabItem.` на `Eremex.AvaloniaUI.Controls.Docking.Internal.TabbedGroupItemsControl`, то есть shell capture видит реальный interaction source, но current recorder shell path умеет читать activation только из `TabControl`.
  - `OrderPositionProductEditor` больше не упирается в отсутствие part ids, но до сих пор не оформлен как row-aware grid edit authoring step; current recorder умеет standalone `SearchAndSelect`, но не строит `SearchAndSelectGridCell(...)` из inner editor parts.
- Current code constraints:
  - `ShellNavigationParts` для runtime требуют primitive navigation source и `PaneTabsLocator`, который резолвится как `ITabControl`.
  - `RecorderShellNavigationHint` использует те же locator-ы и не умеет отдельно описывать capture surface vs replay surface.
  - `RecordedActionKind` пока не содержит `SearchAndSelectGridCell`.
  - `App.axaml.cs` в `Arm.Srv` не содержит `ShellNavigationHints`, а также не содержит grid-editor hint для `OrderPositionProductEditor`.
- Current authoring surface в `Arm.Srv` не экспонирует:
  - явный `MainShell` control как `UiControlType.ShellNavigation`;
  - deterministic recorder/runtime bridge для open/active panes в Eremex docking;
  - authoring contract, который связывает `OrderPositionProductEditor` с `OrderPositionsGridAutomationBridge`.

## 3. Проблема
Последние residual failures из исходного лога возникают потому, что recorder до сих пор слишком жёстко связывает capture surface и replay surface. Для двух оставшихся Arm flows это неверно:

- в docking shell пользователь кликает по vendor-specific pane host, а воспроизводить сценарий нужно через стабильный shell bridge;
- в grid product editor пользователь взаимодействует с inner search-picker parts, а воспроизводить изменение нужно через grid edit API с row/column metadata.

Пока эта развязка явно не описана в contract/config/model, recorder продолжает либо падать на CLR-type mismatch, либо сохранять primitive шаги без достаточного контекста для deterministic replay.

## 4. Цели дизайна
- Описать residual flows как high-level intent, а не как primitive tab/button/text noise.
- Разрешить recorder-у различать capture locator и replay locator для shell-related flows.
- Добавить grid-aware authoring step для search-picker editor inside grid, используя уже существующий runtime API.
- Сохранять provider-neutral runtime path: replay идёт через стабильные AppAutomation contracts, а не через Eremex gestures.
- Держать Arm-specific bridge/config в consumer repo, не захардкодив Arm names в core recorder.
- Не ломать уже закрытые кейсы из предыдущего пакета.

## 5. Non-Goals (чего НЕ делаем)
- Не переоткрываем generic поддержку произвольных docking vendors или arbitrary popup wrappers.
- Не добавляем универсальный Eremex docking adapter в `AppAutomation.FlaUI` или `AppAutomation.Avalonia.Headless`.
- Не переделываем весь `ShellNavigation` API; меняем только то, что нужно для activation-only bridge и capture/replay split.
- Не добавляем полный набор recorder actions для всех grid editor kinds; обязательный scope здесь только `SearchAndSelectGridCell`.
- Не маскируем отсутствие Arm-side bridge поверх эвристик recorder-а.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `AppAutomation.Abstractions`
  - разрешить activation-only shell bridge без обязательного open-source locator;
  - использовать существующий runtime API `SearchAndSelectGridCell(...)` без расширения бизнес-семантики.
- `AppAutomation.Recorder.Avalonia`
  - добавить capture-vs-replay split для shell hints;
  - добавить grid search-picker recorded action и coalescing path;
  - улучшить diagnostics для случаев, где bridge/hint не сконфигурирован.
- `Arm.Srv`
  - добавить явные automation bridge surfaces для shell panes;
  - сконфигурировать recorder hints для shell and grid product editor;
  - обновить page objects и regression tests.

### 6.2 Детальный дизайн
#### 6.2.1 Shell: separate capture surface from replay surface
Current `RecorderShellNavigationHint` связывает capture и replay через одни и те же locator-ы. Для Arm docking это недостаточно: capture идёт по Eremex `TabbedGroupItemsControl`, а replay должен идти по стабильному bridge control.

Изменения:
- Расширить `RecorderShellNavigationHint` optional capture locator-ами:
  - `NavigationCaptureLocator`
  - `PaneTabsCaptureLocator`
  - optional capture locator kind fields, если reuse текущего `LocatorKind` окажется недостаточно.
- Если capture locator не задан, current behavior stays unchanged: recorder использует runtime locator.
- Shell capture rules:
  - match navigation capture against arbitrary related control, не только `TreeView` / `ListBox` / `TabControl`;
  - match pane-tab activation capture against arbitrary related control, если сработал `PaneTabsCaptureLocator`;
  - pane name extraction:
    - сначала from source primitive selection when source is `TreeView` / `ListBox` / `TabControl`;
    - затем fallback через `ActivePaneLabelLocator` для любого activation capture source;
    - если и это невозможно, return unsupported with explicit bridge-missing diagnostic.
- `RecordedActionKind` for shell remains existing:
  - `OpenOrActivateShellPane`
  - `ActivateShellPane`
- `AuthoringCodeGenerator` continues to generate existing page methods.

#### 6.2.2 Shell runtime: activation-only bridge support
Current `ShellNavigationParts.NavigationLocator` is required even when сценарий uses only `ActivateShellPane`. Для residual docking gap это лишнее ограничение.

Изменения:
- Разрешить activation-only shell config:
  - `NavigationLocator` becomes optional in `ShellNavigationParts`;
  - `ActivateShellPane(...)` and read-only shell state may work with only `PaneTabsLocator` and/or `ActivePaneLabelLocator`;
  - `OpenShellPane(...)` or `OpenOrActivateShellPane(...)` without navigation source stay explicitly unsupported and throw diagnostic `UiOperationException` / `NotSupportedException`.
- `IShellNavigationControl.IsEnabled` should use:
  - navigation source when it exists;
  - otherwise pane-tabs bridge or active-pane label bridge as availability source.
- Existing consumers with non-empty `NavigationLocator` keep their behavior unchanged.

#### 6.2.3 Arm shell automation bridge
`Arm.Srv` should not expose raw Eremex docking internals as the replay contract. Instead it adds a small deterministic automation bridge in `MainControl`.

Required bridge surfaces:
- `MainShellPaneTabsCaptureHost`
  - stable locator on the visible Eremex card-pane host or its stable parent, used only for recorder capture.
- `MainShellPaneTabsBridge`
  - standard `TabControl` that mirrors open card panes and selected pane, used only for replay/runtime activation.
- `MainShellActivePaneLabel`
  - text control mirroring current active pane title, used for shell state verification and activation capture fallback.
- `MainShell`
  - authoring page property of type `UiControlType.ShellNavigation`, configured against the bridge surfaces.

Implementation boundary in `Arm.Srv`:
- Bridge state lives in the app layer and is synchronized from dock layout changes.
- Bridge controls may be non-user-facing, but they must remain automation-resolvable and stay in sync with the real dock manager.
- `App.axaml.cs` configures:
  - runtime shell parts against `MainShellPaneTabsBridge` and `MainShellActivePaneLabel`;
  - recorder shell hint with `PaneTabsCaptureLocator = MainShellPaneTabsCaptureHost`.

#### 6.2.4 Grid: search picker inside grid must author to grid edit action
Current recorder can build `SearchAndSelect` for standalone search pickers, but not `SearchAndSelectGridCell(...)` for grid-embedded editor surfaces.

Изменения:
- Add new recorded action:
  - `RecordedActionKind.SearchAndSelectGridCell`
- Add new recorder config:
  - `RecorderGridSearchPickerHint`
    - `SourceLocatorValue`
    - `TargetGridLocatorValue`
    - optional `ColumnName`
    - optional `ColumnIndex`
    - optional locator kind/fallback settings
- Capture flow:
  - when pending text comes from a search input inside a configured grid editor and the result selection comes from matching results surface, recorder emits a single `SearchAndSelectGridCell` step;
  - row and column are resolved from existing `RecorderGridHint` context and source `DataContext` / visual cell metadata;
  - if row/column cannot be derived from context and are not fixed in the hint, recorder returns unsupported with explicit message instead of falling back to raw primitive steps.
- Suppression rules:
  - expand/open button click inside configured grid search picker is suppressed;
  - raw inner `ListBox` or `ComboBox` selection is not persisted separately once the grid step is emitted.
- Code generation:
  - `SearchAndSelectGridCell(static page => page.<GridProperty>, rowIndex, columnIndex, "search", "selected")`
- Runtime validation:
  - validate the new recorded action against `UiControlType.Grid`.

#### 6.2.5 Arm grid editor wiring
`Arm.Srv` already exposes stable part ids for `OrderPositionProductEditor`; now it needs authoring wiring.

Required changes:
- In `App.axaml.cs` add `RecorderGridSearchPickerHint` for:
  - source editor `OrderPositionProductEditor`
  - target grid `OrderPositionsGridAutomationBridge`
  - explicit product column metadata if row/column auto-resolution is not sufficient or is ambiguous.
- In authoring/tests keep `OrderPositionsGridAutomationBridge` as the replay surface; do not introduce standalone page property for `OrderPositionProductEditor` unless it is needed for legacy/manual helpers.
- Add regression checks that the product editor capture now authors to grid edit flow rather than wrapper-level primitive actions.

#### 6.2.6 Diagnostics boundary
Residual unsupported cases must become explicit:
- shell capture without configured bridge:
  - diagnostic names missing `PaneTabsCaptureLocator` / `ActivePaneLabelLocator` boundary;
- grid product editor without grid hint or row context:
  - diagnostic names missing `RecorderGridSearchPickerHint` or unresolved row/column context;
- generic message forms like `TabControl does not expose a selected TabItem` or `SetToggled is not compatible with control 'ServerSearchComboBox'` should disappear for these two Arm flows.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Shell capture:
  - `ActivateShellPane` is emitted when the source matches pane-tab capture surface.
  - `OpenOrActivateShellPane` is emitted only for configured navigation source capture.
  - pane name resolution priority:
    1. source-selected primitive item;
    2. `ActivePaneLabelLocator`;
    3. explicit unsupported diagnostic.
- Shell runtime:
  - activation-only shell controls may omit navigation source;
  - open-style operations without navigation source remain unsupported by contract.
- Grid search picker:
  - step is emitted only after both search text and selected item are observed;
  - row/column resolution priority:
    1. source row/cell context via existing `RecorderGridHint`;
    2. explicit metadata from `RecorderGridSearchPickerHint`;
    3. unsupported diagnostic.
- No heuristic default row or default column is allowed.

## 8. Точки интеграции и триггеры
- `UiControlAdapters.cs`
  - `ShellNavigationParts`
  - shell bridge availability rules
- `UiPageExtensions.cs`
  - shell operation diagnostics for activation-only configs
- `AppAutomationRecorderOptions.cs`
  - new shell capture locator fields
  - new grid search-picker hint collection/type
- `RecorderModels.cs`
  - new `RecordedActionKind.SearchAndSelectGridCell`
- `RecorderSession.cs`
  - try grid-search-picker coalescing before standalone search-picker persistence
  - shell capture remains on selection events but uses new capture locators
- `RecorderStepFactory.cs`
  - shell capture/replay split
  - grid search-picker step creation
- `RecorderCommandRuntimeValidator.cs`
  - new recorded action validation
- `AuthoringCodeGenerator.cs`
  - grid-cell search-picker statement generation
- `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\MainControl.axaml`
  - shell bridge surfaces
- `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\App.axaml.cs`
  - shell and grid recorder/runtime hints
- `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\MainWindowPage.cs`
  - `MainShell` page property
- `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.*`
  - bridge contract and runtime regression tests

## 9. Изменения модели данных / состояния
- `AppAutomation`
  - additive recorder hint/config types;
  - additive recorded action kind;
  - additive shell runtime capability for activation-only bridge.
- `Arm.Srv`
  - additive in-memory bridge state for open pane names and active pane title;
  - no persisted data migration.

## 10. Миграция / Rollout / Rollback
- Existing `SearchPicker`, `Dialog`, `Notification`, `ShellNavigation` consumers remain compatible if new fields/hints are not used.
- Existing shell configs with populated `NavigationLocator` keep current semantics unchanged.
- `Arm.Srv` opts in by adding shell bridge surfaces and the new hints.
- Rollback:
  - revert new recorder action/hints and shell activation-only support in `AppAutomation`;
  - revert bridge controls and authoring config in `Arm.Srv`;
  - previously closed log items remain unaffected.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria
  - Recorder can emit `ActivateShellPane(...)` from an arbitrary capture host when `PaneTabsCaptureLocator` is configured and pane name is resolved through `ActivePaneLabelLocator`.
  - Existing shell recorder behavior for standard `TreeView` / `ListBox` / `TabControl` remains green.
  - Shell runtime supports `ActivateShellPane(...)` with activation-only bridge config and explicitly rejects open-style operations that lack navigation source.
  - Recorder can emit `SearchAndSelectGridCell(...)` from `OrderPositionProductEditor` inner parts and suppress raw expand/list steps.
  - Code generation emits `Page.SearchAndSelectGridCell(...)` for the recorded grid editor step.
  - Repeated authoring run for the original Arm scenario no longer contains:
    - the docking failure rooted in `TabbedGroupItemsControl`;
    - the `OrderPositionProductEditor` primitive compatibility failure.
  - If shell/grid bridge config is missing, diagnostics name the missing bridge/hint boundary instead of generic control-type mismatch.
- Какие тесты добавить/изменить
  - `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - `tests/AppAutomation.Authoring.Tests/*`, if generated source shape changes need direct coverage
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\Tests\OrderHeadlessTests.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\Tests\AuthorizeHeadlessTests.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\Tests\OrderFlaUiTests.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\MainWindowPage.cs`
- Characterization tests / contract checks
  - preserve existing recorder tests for standalone search pickers;
  - preserve existing shell capture from primitive controls;
  - preserve current Arm contract tests for dialog/notification/editor part ids.
- Команды для проверки
  - `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj`
  - `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj`
  - `dotnet test --project .\tests\AppAutomation.Authoring.Tests\AppAutomation.Authoring.Tests.csproj`
  - `dotnet build .\AppAutomation.sln --no-restore`
  - `dotnet test C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj`
  - `dotnet test C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj`
  - Повторить тот же authoring smoke flow, который сформировал `RecordedSmoke.20260423-234409.recorder-diagnostics.log`, и сравнить новый `.recorder-diagnostics.log` по residual пунктам.

## 12. Риски и edge cases
- Shell bridge surfaces в `Arm.Srv` должны реально синхронизироваться с dock manager; отставание bridge state сделает authoring misleading.
- Non-user-facing bridge controls должны оставаться automation-resolvable; полностью скрытые controls могут выпасть из automation tree.
- Grid row/column resolution может ломаться на виртуализации, если source control потеряет связь с row context; в таком случае нужен explicit unsupported path, а не guessed indexes.
- Если product editor column position в grid нестабилен, hint должен опираться на explicit column metadata, а не только на visual order.
- Этот task intentionally не закрывает generic docking automation for arbitrary consumer shells.

## 13. План выполнения
1. Утвердить spec scope по residual shell/grid bridges.
2. Реализовать в `AppAutomation` shell capture/replay split и activation-only shell runtime support.
3. Реализовать в `AppAutomation` `SearchAndSelectGridCell` recorder action and codegen.
4. Добавить AppAutomation regression tests.
5. Добавить shell bridge surfaces и new hints в `Arm.Srv`.
6. Обновить Arm page objects и targeted tests.
7. Повторить original authoring smoke flow и сравнить новый diagnostic log.

## 14. Открытые вопросы
Нет блокирующих вопросов. Решение уже сужено до детерминированного automation bridge, а не до vendor gesture automation.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`
- Выполненные требования профиля:
  - deterministic replay surfaces use stable selectors/contracts;
  - UI automation changes имеют обязательные regression tests;
  - unsupported runtime paths остаются явными и диагностичными;
  - scope ограничен изменениями пользовательского UI flow и authoring/runtime automation contract.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Abstractions/UiControlAdapters.cs` | activation-only shell bridge support | Runtime shell replay contract |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` | shell diagnostics for activation-only configs | Replay API clarity |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` | shell capture locators and grid-search-picker hint | Recorder opt-in config |
| `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs` | `SearchAndSelectGridCell` action kind | Persist grid edit intent |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | grid search-picker coalescing order and shell capture usage | Event pipeline |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs` | shell capture/replay split and grid step creation | Step creation |
| `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs` | validate new recorded action | Runtime readiness |
| `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs` | generate `Page.SearchAndSelectGridCell(...)` | Authoring output |
| `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` | shell activation-only contract tests | Runtime coverage |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | shell/grid recorder regression tests | Recorder coverage |
| `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\MainControl.axaml` | shell bridge controls and capture host ids | App-side automation bridge |
| `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\App.axaml.cs` | shell and grid recorder/runtime hints | Consumer config |
| `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\MainWindowPage.cs` | `MainShell` authoring property and constants | Authoring surface |
| `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\*` | shell/grid contract tests | Consumer regression safety |
| `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\*` | runtime replay regression tests | Consumer runtime safety |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Docking pane activation capture | generic tab capture fails on `TabbedGroupItemsControl` | recorder matches dedicated capture host and writes shell action |
| Docking pane replay | no deterministic bridge for activation-only path | activation through shell bridge `TabControl` + active label |
| Grid product editor recording | primitive inner editor interactions or unsupported diagnostic | single `SearchAndSelectGridCell` step with row/column metadata |
| Residual diagnostics | generic CLR-type mismatch | explicit bridge/hint diagnostics or green path |

## 18. Альтернативы и компромиссы
- Вариант: закрыть только diagnostics и оставить residual gaps unsupported.
  - Плюсы: маленький объём.
  - Минусы: исходный diagnostic log останется partially red по тем же пользовательским flows.
- Вариант: добавить vendor-specific Eremex gestures в runtimes.
  - Плюсы: можно было бы воспроизводить реальные docking interactions напрямую.
  - Минусы: высокий риск flaky behavior, tight coupling to vendor internals, broader scope than the residual issue deserves.
- Вариант: добавить standalone page property для `OrderPositionProductEditor` и писать `SearchAndSelect(...)`.
  - Плюсы: самый маленький change set.
  - Минусы: теряется row/column intent и replay остаётся зависимым от transient active editor state.
- Почему выбранное решение лучше в контексте этой задачи:
  - оно выражает один общий принцип для обоих residual gaps: explicit bridge between capture and replay;
  - reuse-ит existing runtime APIs instead of inventing parallel DSL;
  - держит vendor-specific concerns в consumer bridge/config, а core recorder/runtime — provider-neutral.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, корневая проблема, goals и non-goals зафиксированы для residual scope |
| B. Качество дизайна | 6-10 | PASS | Responsibilities, integration points, algorithms, diagnostics and rollout described |
| C. Безопасность изменений | 11-13 | PASS | Additive contract/config changes, explicit rollback, no vendor gestures in core |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, target tests and original-log recheck listed |
| E. Готовность к автономной реализации | 17-19 | PASS | File table, alternatives and bounded plan are explicit |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation constraints and deterministic selectors are preserved |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Scope сужен ровно до двух residual items from the original log |
| 2. Понимание текущего состояния | 5 | Current AppAutomation/Arm state and exact remaining gaps are captured |
| 3. Конкретность целевого дизайна | 5 | Shell capture split, activation-only bridge and grid recorded action are defined concretely |
| 4. Безопасность (миграция, откат) | 5 | Additive rollout and explicit rollback path are described |
| 5. Тестируемость | 5 | Unit/recorder/runtime tests and original-log replay validation are listed |
| 6. Готовность к автономной реализации | 5 | No blocking open questions remain |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - Scope narrowed from “остатки вообще” to one root cause: capture surface differs from replay surface.
  - Shell solution split into recorder capture locators plus runtime bridge, so capture and replay no longer compete for the same locator.
  - Grid solution explicitly uses `SearchAndSelectGridCell(...)` instead of a weaker standalone search-picker fallback.
  - Added mandatory re-run of the original authoring smoke log as a direct acceptance criterion.
- Что осталось на решение пользователя:
  - Подтвердить этот residual-follow-up scope для перехода к `EXEC`.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения:
  - `AppAutomation` доведён до explicit capture-to-replay bridge для shell activation и grid `SearchAndSelectGridCell(...)`, без возврата к vendor-specific gestures.
  - `Arm.Srv` получил shell bridge surfaces, grid search-picker wiring для `OrderPositionProductEditor` и deterministic order-card date bridge для стабильного live/FlaUI save path.
  - Для recorder smoke добавлен opt-in флаг `APPAUTOMATION_RECORDER_HIDE_OVERLAY`, чтобы overlay не перехватывал `FlaUI` main window и не искажал повтор исходного сценария.
- Что проверено дополнительно для refactor / comments:
  - Повторный recorder smoke с diagnostics включённым дал пустые `ResidualShellCheck.20260424-214142.recorder-diagnostics.log` и `ResidualOrderCheck.20260424-214228.recorder-diagnostics.log`; исходные residual signatures больше не воспроизводятся.
  - После последнего overlay/date-bridge кода повторены обязательные UI suites: `Arm.UiTests.Headless` `8/8` и `Arm.UiTests.FlaUI` `5/5`.
  - Проверено, что post-fix логи не содержат `TabbedGroupItemsControl`, `TabControl does not expose a selected TabItem`, `OrderPositionProductEditor`, `SetToggled is not compatible` или `ServerSearchComboBox` compatibility failures.
- Остаточные риски / follow-ups:
  - В targeted recorder-enabled `FlaUI` прогонах остаётся non-fatal `Application failed to exit`; на результат тестов и diagnostics log это не повлияло.
  - Build по-прежнему шумит историческими `NU1608` / `NU1903` / nullability warnings вне scope этой спеки.

## Approval
Подтверждено пользователем: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Анализ residual gaps после предыдущего composite package | 0.93 | Нужно было подтвердить, что current code уже закрыл dialog/notification/search-picker/date-editor bulk issues | Сформировать новую отдельную спеку по residual scope | Нет | Нет | Большая часть исходного лога уже закрыта; новая спека должна быть узкой и не дублировать завершённый пакет | `sample/DotnetDebug.AppAutomation.Authoring/Recorded/RecordedSmoke.20260423-234409.recorder-diagnostics.log`, `src/AppAutomation.*`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\*` |
| SPEC | Формирование target design по shell/grid capture bridges | 0.91 | Нужно было выбрать между diagnostics-only и полноценным bridge-based решением | Завершить спеку и встроить quality gate | Нет | Нет | Diagnostics-only вариант не закрывает исходный лог; выбран explicit capture-to-replay bridge using existing runtime APIs | `specs/2026-04-24-recorder-residual-shell-grid-bridges.md`, `src/AppAutomation.Abstractions/*`, `src/AppAutomation.Recorder.Avalonia/*`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\*` |
| SPEC | Самопроверка по SPEC Linter / Rubric / Post-SPEC review | 0.95 | Блокирующих данных больше не требуется; остался только пользовательский go/no-go по scope | Запросить подтверждение спеки | Да | Нет | После sanity-pass лучшим решением осталось bridge-based follow-up; quality gate встроен в документ и дополнительных правок не потребовалось | `specs/2026-04-24-recorder-residual-shell-grid-bridges.md`, `C:\Projects\My\Agents\instructions\governance\spec-linter.md`, `C:\Projects\My\Agents\instructions\governance\spec-rubric.md`, `C:\Projects\My\Agents\instructions\governance\review-loops.md` |
| EXEC | Переход из SPEC в EXEC после подтверждения пользователя | 0.98 | Нужен был только фактический go-ahead на реализацию в границах уже зафиксированного scope | Реализовать остаточные shell/grid bridges и закрыть regression tests | Нет | Да | Пользователь подтвердил ровно тот scope, который уже был проверен quality gate; дополнительных product/API вопросов не осталось | `specs/2026-04-24-recorder-residual-shell-grid-bridges.md`, `src/AppAutomation.Abstractions/*`, `src/AppAutomation.Recorder.Avalonia/*`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\*` |
| EXEC | Реализация shell/grid capture-to-replay bridge и consumer wiring | 0.94 | До завершения не хватало live/runtime подтверждения на реальном `Arm` flow | Прогнать targeted и full UI suites, затем повторить authoring smoke | Нет | Нет | Закрытие residual log-пунктов требовало не эвристики, а explicit bridge на runtime/recorder/app уровнях с regression-тестами | `src/AppAutomation.Abstractions/UiControlAdapters.cs`, `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs`, `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`, `tests/AppAutomation.Abstractions.Tests/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/*`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\App.axaml.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\MainControl.axaml`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\MainControl.axaml.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\MainWindowPage.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\*`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\*` |
| EXEC | Стабилизация live/FlaUI order flow для проверки residual order scenario | 0.92 | Нужен был deterministic commit path для planned delivery dates и recorder-friendly startup без overlay hijack | Добавить date bridge и overlay-hide toggle, затем повторить smoke и full suites | Нет | Нет | Без этого replay проходил нестабильно: `DateEditor` опирался на flaky vendor UIA path, а recorder overlay мог перехватывать `FlaUI` main window | `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Automation\OrderPositionAutomationBridge.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\OrderCardControl.axaml`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\App.axaml.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\OrdersPage.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\Tests\OrderHeadlessTests.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\Infrastructure\FlaUiOrderSession.cs` |
| EXEC | Повтор исходного authoring smoke и финальная валидация residual diagnostics | 0.99 | Данных больше не хватало; оставалось только убедиться, что новые diagnostics logs пустые, а full UI suites зелёные | Зафиксировать итоги в spec и подготовить финальный отчёт | Нет | Нет | Прямой replay smoke на live backend дал пустые `ResidualShellCheck`/`ResidualOrderCheck` diagnostics logs, а финальные `Headless 8/8` и `FlaUI 5/5` подтвердили отсутствие регрессии | `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Recorded\ResidualShellCheck.20260424-214142.recorder-diagnostics.log`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Recorded\ResidualOrderCheck.20260424-214228.recorder-diagnostics.log`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\bin\Debug\net8.0\Arm.UiTests.Headless.dll`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\bin\Debug\net8.0-windows7.0\Arm.UiTests.FlaUI.dll` |
