# Recorder Composite Desktop Capture Follow-Up

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: AppAutomation + Arm.Srv
- Масштаб: medium
- Целевой релиз / ветка: текущая ветка `feat-arm-paritet`
- Ограничения:
  - До подтверждения спеки изменяется только этот файл.
  - Источник симптомов: `sample/DotnetDebug.AppAutomation.Authoring/Recorded/RecordedSmoke.20260423-234409.recorder-diagnostics.log`.
  - Реализация охватывает текущий репозиторий `AppAutomation` и внешний consumer-репозиторий `C:\Projects\ИЗП\Sources\Arm.Srv`.
  - Не добавлять dependency между репозиториями.
  - Не маскировать реальные app-side проблемы вроде duplicate/missing `AutomationId` под recorder-side workaround.
  - Для `Arm.Srv` использовать минимально-достаточные изменения automation contract и сопровождать их regression-тестами.
- Связанные ссылки:
  - `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderSelectorResolver.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderCaptureDiagnostics.cs`
  - `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs`
  - `src/AppAutomation.Abstractions/UiControlAdapters.cs`
  - `src/AppAutomation.Abstractions/UiPageExtensions.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\OrdersControl.axaml`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\OrderCardControl.axaml`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\MiniControls\ServerSearchComboBox.axaml`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\MainWindow.axaml`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\NotificationManagerWrapper.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\OrdersPage.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI`

## 1. Overview / Цель
Убрать ложные recorder failures на Arm-style composite desktop controls и одновременно поправить automation contract в `Arm.Srv` там, где лог явно фиксирует дефект самого приложения, а не только пробел recorder-а.

Цель этого task:
- записывать configured search picker flows без raw `SetToggled`/`EnterText` мусора для popup/list implementations;
- записывать configured dialog / notification / shell-navigation interactions как high-level intent;
- улучшить диагностику для wrapper/editor cases, которые по-прежнему требуют app-side stable parts и не могут быть честно поддержаны generic recorder logic.
- устранить в `Arm.Srv` app-side blockers:
  - duplicate `AutomationId` у кнопки создания заказа;
  - отсутствие стабильных ids у dialog/notification buttons;
  - отсутствие stable part ids на интерактивных частях `ServerSearchComboBox` и связанных editor wrappers, где это мешает deterministic capture/replay.

## 2. Текущее состояние (AS-IS)
- Runtime layer уже умеет:
  - `ISearchPickerControl` / `SearchAndSelect`;
  - `IDialogControl` / `ConfirmDialog` / `CancelDialog` / `DismissDialog`;
  - `INotificationControl` / `DismissNotification`;
  - `IShellNavigationControl` / `NavigateShellPane` / `OpenOrActivateShellPane`.
- Recorder layer умеет:
  - `SearchAndSelect`, но только при coalescing `TextBox + ComboBox` через `RecorderSearchPickerHint`.
  - primitive `Button`, `ToggleButton`, `TextBox`, `ListBox`, `TabControl` capture.
- Recorder revalidation в `RecorderSession.RevalidateStep(...)` пере-резолвит descriptor locator и затем повторно валидирует step against matched owner control, а не against originally interacted part.
- В логе Arm зафиксированы характерные сбои:
  - `SelectorValidationFailed` из-за app-side duplicate `AutomationId` (`CreateOrderButton`);
  - `ActionValidationFailed` для `ServerSearchComboBox`, `ComboBoxEditor`, `ContentControl`, когда raw popup-part interaction revalidates against wrapper owner;
  - `CaptureFailed` для dialog/toast buttons without stable ids;
  - `CaptureFailed: TabControl does not expose a selected TabItem.` for docking shell container.
- Existing AppAutomation specs already closed runtime API gaps:
  - `AAR-002` search picker flow;
  - `AAR-006` dialog/notification/export;
- `AAR-007` shell navigation.
  Но recorder capture/codegen пока не дотягивает эти abstractions до реального Arm desktop flow.
- В `Arm.Srv` уже есть UI test consumer contract для orders flow:
  - `tests/Arm.UiTests.Authoring/Pages/OrdersPage.cs`
  - `tests/Arm.UiTests.Headless/*`
  - `tests/Arm.UiTests.FlaUI/*`
  При этом FlaUI infrastructure явно использует fallback на `PART_RealEditor` и `PART_PopupOpenButton`, что подтверждает слабый app-side contract для record-and-playback без специальных обходов.

## 3. Проблема
Recorder capture pipeline still assumes that the stable locator used for generated code directly identifies the actionable primitive control. On Arm composite desktop controls the stable locator usually belongs to a wrapper/composite owner, while the real user interaction happens on internal text/input/list/button parts. At the same time, some failures are not recorder-side at all: `Arm.Srv` exposes duplicate selectors or no stable selectors for transient UI. As a result, recorder either produces false-invalid primitive steps, or correctly flags an app-side automation-contract defect, but today these two classes of problems are not separated well enough to make the workflow reliable.

## 4. Цели дизайна
- Сохранять user intent на уровне уже существующих typed AppAutomation workflows, а не на raw template parts.
- Делать additive changes в recorder/config/codegen без vendor dependency.
- Делать в `Arm.Srv` только те app-side selector/part-contract changes, которые прямо нужны для deterministic automation.
- Оставлять explicit boundary between recorder-side support and app-side automation contract defects.
- Поддержать both `ComboBox` and `ListBox` search-picker result surfaces.
- Не ухудшить существующий primitive recorder flow для non-composite controls.

## 5. Non-Goals (чего НЕ делаем)
- Не переделываем массово весь automation contract `Arm.Srv`; правим только конкретные зафиксированные hotspots.
- Не делаем generic support for any arbitrary `DateEditor` / `ComboBoxEditor` / `PopupEditor` wrapper without stable part contract.
- Не пытаемся автоматически записывать OS-native dialogs / folder pickers.
- Не скрываем duplicate selector problems workaround’ами в recorder; они должны быть исправлены на стороне `Arm.Srv`.
- Не добавляем hard-coded Arm-specific locator names в core recorder.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `AppAutomation.Abstractions`
  - расширить composite parts/config там, где runtime contract already exists but current shape is too narrow for recorder/runtime parity.
- `AppAutomation.Recorder.Avalonia`
  - добавить recorder hints and capture logic for existing composite workflows;
  - suppress implementation-detail template-part clicks when a matching composite hint exists;
  - improve unsupported diagnostics for wrapper cases without matching composite hints.
- `Arm.Client`
  - устранить duplicate/missing selectors и добавить stable part ids на реально интерактивные элементы problematic composite controls.
- `Arm` UI tests
  - обновить authoring constants / headless / FlaUI regression checks под новый selector contract.
- Tests
  - покрыть recorder capture, codegen и revalidation behavior.

### 6.2 Детальный дизайн
#### 6.2.1 Search picker parity for popup/list implementations
- Расширить `SearchPickerParts`, чтобы results surface мог быть:
  - `ComboBox` (existing behavior);
  - `ListBox` (new behavior).
- `RecorderSearchPickerHint` продолжает ссылаться на `SearchPickerParts`, но recorder matching должен уметь:
  - coalesce `TextBox + ComboBox` selection;
  - coalesce `TextBox + ListBox` selection;
  - suppress raw click on configured `ExpandButtonLocator`, because open/expand is implementation detail, not final intent.
- Runtime `SearchPickerControlAdapter` должен поддерживать both result kinds so generated `Page.SearchAndSelect(...)` remains executable for configured list-backed pickers.

#### 6.2.2 Recorder capture for dialog and notification composites
- Добавить recorder hints for existing composite controls:
  - `RecorderDialogHint`
  - `RecorderNotificationHint`
- Button clicks on configured dialog action parts should record high-level steps instead of raw button clicks:
  - `ConfirmDialog`
  - `CancelDialog`
  - `DismissDialog`
- Button clicks on configured notification dismiss parts should record `DismissNotification`.
- Descriptor locator must be the stable composite owner locator, not inner button locator.
- Code generation should call existing page extension methods instead of inventing new runtime API.

#### 6.2.3 Recorder capture for shell navigation
- Добавить `RecorderShellNavigationHint`, reusing existing shell-navigation model.
- Selection on configured navigation source should record `OpenOrActivateShellPane(...)` using observed pane text.
- Selection on configured pane tabs should record `ActivateShellPane(...)`.
- This closes the current gap where generic `TabControl` capture fails on docking containers because the selected item is not `TabItem`, while the runtime shell abstraction already models the user intent.

#### 6.2.4 Arm.Srv automation-contract fixes
- `OrdersControl.axaml`
  - развести duplicate `CreateOrderButton` ids на разные стабильные identifiers.
- `MainWindow.axaml`
  - добавить stable `AutomationId` для кнопок `AskViewModel` dialog actions.
- `NotificationManagerWrapper` and/or related notification visual hookup
  - добавить stable ids на action/close buttons notification UI where runtime object becomes available.
- `ServerSearchComboBox`
  - exposed part ids should include deterministic input/open-button identifiers derived from the host control id.
- Related order-card wrappers
  - where outer wrapper keeps business locator (`OrderCustomerSearch`, `OrderDeliveryAddressCombo`, etc.), the actual interactive parts must become discoverable/stable enough for recorder/runtime composed parts.

#### 6.2.5 Wrapper-aware diagnostics and revalidation boundary
- For configured composite recorder hints, `RevalidateStep(...)` must validate against the composite descriptor semantics, not against the inner wrapper owner’s primitive CLR type.
- For non-configured wrapper/template-part cases, diagnostics should explicitly say that:
  - a stable composite hint is missing; or
  - stable app-side part locators are required.
- This diagnostic improvement is especially important for Arm `ContentControl + DateEditor + PART_RealEditor` and popup buttons, where the current message "not compatible with control 'ContentControl'" is technically true but not actionable enough.

### 6.3 Предлагаемые public/config changes
- `AppAutomation.Abstractions`
  - add `SearchPickerResultsKind` (or equivalent) to `SearchPickerParts`.
- `AppAutomation.Recorder.Avalonia`
  - extend `AppAutomationRecorderOptions` with:
    - `IList<RecorderDialogHint> DialogHints`
    - `IList<RecorderNotificationHint> NotificationHints`
    - `IList<RecorderShellNavigationHint> ShellNavigationHints`
- `RecorderModels` / `AuthoringCodeGenerator`
  - add recorded actions for dialog, notification and shell capture, mapped to existing page extension methods.
- `RecorderStepValidator` / `RecorderCommandRuntimeValidator`
  - accept these high-level actions against corresponding composite `UiControlType`.
- `Arm.UiTests.Authoring`
  - update constants/types if host locators change.
- `Arm.UiTests.Headless` / `Arm.UiTests.FlaUI`
  - add or update regression tests that fail before the app-side selector fix and pass after it.

### 6.4 Границы совместимости
- Existing search-picker configs with combo-box results must remain valid unchanged.
- Existing primitive recorder behavior must remain the fallback when no composite hint matches.
- Existing invalid selector diagnostics for duplicate locators must remain unchanged in recorder; the targeted `Arm.Srv` duplicate case should disappear because the app contract is fixed.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Search picker:
  - open/expand click is never persisted when it belongs to a configured composite picker;
  - `SearchAndSelect` is emitted only after both search text and selected result are observed.
- Dialog:
  - clicked action part determines exact dialog action; no implicit fallback between confirm/cancel/dismiss.
- Notification:
  - only explicit configured dismiss action is recorded; passive text assertions remain manual/user-driven.
- Shell navigation:
  - navigation source selection records `OpenOrActivate`;
  - pane-tab selection records `Activate`.
- Wrapper diagnostics:
  - if source matches known popup/editor template-part shape but no composite hint matches, failure message should name the missing configuration/stable-part boundary.

## 8. Точки интеграции и триггеры
- `RecorderSession.OnButtonClick(...)`
  - search-picker expand suppression;
  - dialog/notification composite capture.
- `RecorderSession.RecordListBoxSelection(...)`
  - search-picker listbox coalescing;
  - shell navigation source or pane capture where configured.
- `RecorderSession.RecordComboBoxSelection(...)`
  - existing search-picker coalescing must keep working.
- `RecorderSession.RevalidateStep(...)`
  - composite-aware revalidation.
- `AuthoringCodeGenerator`
  - emit existing page extension calls for new high-level recorded actions.
- `Arm.Client` order and shell/dialog notification views
  - expose stable selectors required by the above capture/runtime flows.

## 9. Изменения модели данных / состояния
- Additive recorder configuration types.
- Additive recorded action kinds in recorder model.
- No breaking change to existing saved steps is intended; old actions keep their numeric meaning and old configs remain valid.
- If enum values are extended, append-only ordering is required.
- `Arm.Srv` selector contract changes are intentional test-surface updates; related authoring constants/tests must be updated in the same change set.

## 10. Миграция / Rollout / Rollback
- Consumers opt in to new recorder behavior by adding composite hints.
- Existing consumers without hints keep current primitive capture behavior.
- `Arm.Srv` rollout is local to automation contract for existing UI; rollback is reverting selector/part-id changes plus test updates.
- Rollback is straightforward: revert recorder hint types / capture logic / codegen / tests; runtime composite APIs remain intact because they were introduced earlier.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria
  - Configured search picker with `ListBox` results records a single `SearchAndSelect` step and does not persist raw expand-button click.
  - Existing combo-box search picker recording continues to pass.
  - Configured dialog action buttons record high-level dialog steps and generate `Page.ConfirmDialog(...)` / `Page.CancelDialog(...)` / `Page.DismissDialog(...)`.
  - Configured notification dismiss button records `Page.DismissNotification(...)`.
  - Configured shell navigation source / pane tabs record shell-navigation steps and generate existing page extension calls.
  - Duplicate selector issues still surface as invalid selector diagnostics.
  - Wrapper/editor cases without matching composite hint emit more actionable diagnostics than the current raw owner-type mismatch.
  - In `Arm.Srv`, `CreateOrderButton` duplicate selector ambiguity is removed.
  - In `Arm.Srv`, dialog/notification actions and targeted order composite parts expose stable selectors consumed by tests.
- Какие тесты добавить/изменить
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - if needed, targeted authoring/codegen tests where previews or generated source change.
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\*`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\*`
- Characterization tests / contract checks
  - preserve current combo-box search-picker recording;
  - preserve primitive button/list/tab capture when no composite hint matches.
  - add regression around previously ambiguous/missing selectors on Arm order/dialog/notification flow.
- Команды для проверки
  - `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj`
  - `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj`
  - `dotnet test --project .\tests\AppAutomation.Authoring.Tests\AppAutomation.Authoring.Tests.csproj`
  - `dotnet build .\AppAutomation.sln --no-restore`
  - `dotnet test --solution .\AppAutomation.sln --no-build`
  - `dotnet test C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj`
  - `dotnet test C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj`

## 12. Риски и edge cases
- Over-eager composite matching can hide legitimate primitive user intent if hints are too broad.
- ListBox-backed search pickers may emit transient selection noise; matching must stay anchored to configured search input/results pair.
- Shell navigation panes with duplicate captions remain ambiguous; current normalization rules should be reused and ambiguity kept explicit.
- Dialog/notification composites still depend on stable application-side parts; no hint can fix missing anchors that are not exposed at all.
- Standalone date-editor wrappers remain partly unsupported without stable inner part contract; this task improves diagnostics but does not claim generic replay support.
- `Arm.Srv` notification library may limit where ids can be attached; if so, fallback is explicit unsupported/partial support instead of fake recorder success.

## 13. План выполнения
1. Update spec for dual-repo scope and get approval.
2. Implement AppAutomation recorder/runtime parity changes.
3. Add/adjust AppAutomation tests.
4. Implement minimal `Arm.Srv` selector/part-contract fixes.
5. Add/update Arm regression tests.
6. Run targeted tests in both repos, then broader builds/tests.
7. Post-EXEC review.

## 14. Открытые вопросы
- Выбрать scope реализации:
  - Вариант 1. Только `AppAutomation`.
    - Плюсы: меньше риск, быстрее.
    - Минусы: останутся app-side дефекты `Arm.Srv`, часть capture failures не уйдёт.
  - Вариант 2. Только `Arm.Srv`.
    - Плюсы: чинит duplicate/missing selectors в приложении.
    - Минусы: recorder всё равно не начнёт полноценно записывать dialog/notification/shell/search intent.
  - Вариант 3. Комбинированный пакет `AppAutomation + Arm.Srv`.
    - Плюсы: закрывает и recorder parity, и app-side selector blockers.
    - Минусы: самый широкий объём и longest verification path.
- Рекомендуемый вариант: Вариант 3.
- Отдельный технический вопрос: standalone date-editor wrapper capture оставляем вне scope реализации и ограничиваемся улучшенной диагностикой.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - Recorder logic uses stable selectors / configured composite hints instead of position/text heuristics.
  - Scope keeps UI automation flows deterministic and test-driven.
  - User-facing workflow changes require automated tests and generated-code checks.
  - Arm-side selector changes are accompanied by UI regression tests.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Abstractions/UiControlAdapters.cs` | Extend `SearchPickerParts` runtime support for list-backed results | Runtime parity with recorder capture |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` | Add composite recorder hint types | Consumer opt-in capture config |
| `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs` | Add high-level recorded actions | Persist user intent |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | Composite capture/coalescing/revalidation updates | Capture pipeline |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs` | Create composite steps and list-backed search-picker steps | Step creation |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepValidator.cs` | Validate new high-level actions | Recorder validation |
| `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs` | Runtime readiness validation for new actions | Diagnostics |
| `src/AppAutomation.Recorder.Avalonia/RecorderCaptureDiagnostics.cs` | More actionable wrapper diagnostics | Operator feedback |
| `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs` | Generate existing page extension calls | Authoring output |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | Recorder capture/regression tests | Regression safety |
| `tests/AppAutomation.Abstractions.Tests/*` | Search-picker runtime parity tests if adapter shape changes | Runtime contract safety |
| `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\OrdersControl.axaml` | Remove duplicate selector | App-side automation contract fix |
| `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\MainWindow.axaml` | Stable dialog action ids | App-side automation contract fix |
| `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\NotificationManagerWrapper.cs` | Stable notification action/close ids where feasible | App-side automation contract fix |
| `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\MiniControls\ServerSearchComboBox.*` | Stable part ids for interactive parts | App-side automation contract fix |
| `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client.ViewModel\OrderPositions\OrderPositionsForSweepViewModel.cs` | Snapshot enumeration in `ApplyOrderUpdate()` | Live-backend concurrency fix found during final validation |
| `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client.Tests\OrderPositionTests.cs` | Regression test for collection mutation during order update mapping | Safety net for live SignalR update flow |
| `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.*` | Regression tests / contract updates | Consumer verification |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Search picker with popup list | Raw textbox/list/button noise or invalid capture | Single `SearchAndSelect` step with configured list-backed parts |
| Dialog button click | Raw button click or capture failure | High-level dialog action capture |
| Notification dismiss | Raw button click or capture failure | `DismissNotification` capture |
| Docking shell selection | Unsupported generic `TabControl` capture | High-level shell navigation capture |
| Wrapper diagnostics | Generic owner-type mismatch | Actionable "missing composite hint / stable part locator" message |
| Arm order create button | Duplicate `AutomationId` ambiguity | Distinct stable selectors |
| Arm dialog/notification buttons | No stable selectors | Stable ids or explicit documented unsupported boundary |

## 18. Альтернативы и компромиссы
- Вариант: исправить только diagnostics and keep recorder behavior unchanged.
  - Плюсы: маленький объём, почти нет риска.
  - Минусы: не решает реальную authoring problem for Arm desktop flows.
- Вариант: исправить только `Arm.Srv` selectors and parts.
  - Плюсы: снимает наиболее явные app-side blockers.
  - Минусы: не закрывает recorder/codegen gap для existing high-level runtime APIs.
- Вариант: сделать generic support for any popup editor wrapper by heuristics over `PART_RealEditor` / `PART_PopupOpenButton`.
  - Плюсы: может покрыть больше UI без config.
  - Минусы: высокий риск ложных срабатываний и vendor-specific assumptions.
- Почему выбранное решение лучше в контексте этой задачи:
  - Комбинированный вариант использует уже существующие typed runtime abstractions;
  - остаётся opt-in and provider-neutral в `AppAutomation`;
  - при этом честно исправляет app-side selector defects в `Arm.Srv`, которые recorder принципиально не должен workaround’ить.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals and non-goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Responsibilities, integration points, additive model and rollback described |
| C. Безопасность изменений | 11-13 | PASS | Scope stays inside current repo and keeps app-side selector defects explicit |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, target tests and commands listed |
| E. Готовность к автономной реализации | 17-19 | PASS | File table, alternatives and bounded plan are present |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation constraints captured |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Scope limited to recorder/composite capture follow-up in current repo |
| 2. Понимание текущего состояния | 5 | Existing runtime abstractions, recorder gaps and concrete Arm log symptoms captured |
| 3. Конкретность целевого дизайна | 5 | Config, capture flow, codegen and diagnostics changes are explicit |
| 4. Безопасность (миграция, откат) | 5 | Additive config/action model and rollback path described |
| 5. Тестируемость | 5 | Targeted and full verification commands plus characterization checks are listed |
| 6. Готовность к автономной реализации | 5 | No unresolved blocking dependency inside current repo scope |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - Scope broadened to dual-repo package with explicit separation between recorder-side and app-side fixes.
  - Added explicit user-choice options for `AppAutomation` only / `Arm.Srv` only / combined package.
  - Standalone date-editor wrapper capture kept as explicit non-goal/open-question boundary.
  - Search-picker runtime parity and composite recorder capture were described together to avoid half-fix.
- Что осталось на решение пользователя:
  - Choose implementation option; recommended `Вариант 3`.
  - Confirm that standalone date-editor wrapper capture stays out of this task.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения:
  - Recorder/runtime package в `AppAutomation` доведён до зелёного состояния повторным прогоном обоих тестовых проектов.
  - App-side selector fixes в `Arm.Srv` доведены до работающего состояния через headless regression tests.
  - Дополнительно исправлен runtime-defect в `ServerSearchComboBox`: `UpdateState()` теперь сам marshals на `Dispatcher.UIThread`, чтобы headless flow не падал из-за invalid thread access.
  - Во время live-backend прогона найден и исправлен ещё один реальный дефект `Arm.Srv`: `OrderPositionsForSweepViewModel.ApplyOrderUpdate()` перечислял `Items` без snapshot и мог падать `Collection was modified` при мутации коллекции из mapper/signal updates.
- Что проверено дополнительно для refactor / comments:
  - Подтверждён рабочий `TUnit` tree-node filter format `/*/*/Class/Test` для адресного прогона automation-contract тестов.
  - Полный `Arm.UiTests.Headless` на подключённом backend зелёный: `6/6`.
  - Полный `Arm.UiTests.FlaUI` на подключённом backend зелёный: `4/4`.
  - Для `FlaUI` отдельно восстановлен локальный `Microsoft.WindowsDesktop.App 8.0.26` runtime рядом с repo-local `.dotnet`, чтобы тестовый процесс вообще запускался.
  - Live-only regression по `ApplyOrderUpdate_ShouldUseSnapshot_WhenMapperMutatesItemsCollection` сначала подтверждён в красном состоянии, затем переведён в зелёное после фикса прод-кода.
- Остаточные риски / follow-ups:
  - Standalone generic capture для произвольных wrapper/date editors остаётся вне scope этой задачи и закрыт только improved diagnostics.
  - Повторяемость full end-to-end прогонов по-прежнему зависит от доступности dev-backend `Arm.Srv`, RavenDB/LDAP контуров и тестовых учётных данных.

## Approval
- Получено подтверждение пользователя: `Выбираю вариант 3. Спеку подтверждаю`
- Переход в фазу: `EXEC`

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Recorder composite desktop capture follow-up | 0.87 | Нужно подтверждение user scope по standalone date-editor wrapper cases | Запросить подтверждение спеки | Да | Нет | Current repo already has runtime abstractions for search/dialog/notification/shell; spec focuses on recorder parity and explicit diagnostics | `specs/2026-04-24-recorder-composite-desktop-capture-followup.md`, recorder/runtime source files, Arm diagnostic log |
| SPEC | Расширение scope на `Arm.Srv` | 0.9 | Нужен явный выбор между AppAutomation-only, Arm-only и combined вариантом | Показать варианты и запросить подтверждение спеки | Да | Да, пользователь попросил включить `Arm.Srv` | Broadening to app-side selector fixes removes defects recorder should not workaround and aligns with Arm repo test/selector contract | `specs/2026-04-24-recorder-composite-desktop-capture-followup.md`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\*`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.*` |
| EXEC | Старт реализации после подтверждения варианта 3 | 0.92 | Нужно локально подтвердить instruction stack и текущее состояние тестов/грязного дерева | Начать с regression-тестов и recorder/runtime parity в `AppAutomation`, затем перейти к `Arm.Srv` | Нет | Да, пользователь подтвердил combined scope фразой `Выбираю вариант 3. Спеку подтверждаю` | Реализация идёт в границах non-goals: composite capture + app-side selector fixes, без generic поддержки произвольных wrapper editors | `specs/2026-04-24-recorder-composite-desktop-capture-followup.md`, `C:\Projects\My\Agents\instructions\*`, `C:\Projects\ИЗП\Sources\Arm.Srv\Agents.md` |
| EXEC | Recorder/runtime parity в `AppAutomation` | 0.95 | Для `TUnit` нельзя использовать `dotnet test --filter`; точечная валидация выполняется либо полным проектом, либо через native test-app параметры | Перейти к app-side automation-contract фиксам и regression-тестам в `Arm.Srv` | Нет | Нет | Добавлены list-backed `SearchPicker`, composite recorder hints/actions для dialog/notification/shell, suppression implementation-detail button clicks и более actionable wrapper diagnostics; оба затронутых тестовых проекта зелёные | `src/AppAutomation.Abstractions/UiControlAdapters.cs`, `src/AppAutomation.Recorder.Avalonia/*`, `tests/AppAutomation.Abstractions.Tests/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/*` |
| EXEC | Arm app-side selectors и regression safety net | 0.94 | Нужно подтвердить, что новые automation ids реально видны в visual tree и доступны authoring/runtime слоям | Прогнать headless automation-contract тесты и затем сделать более широкий smoke | Нет | Нет | В `Arm.Srv` исправлены duplicate/missing selectors, добавлены stable part ids для popup editors, dialog/notification ids и consumer hints для `AppAutomation` authoring flows | `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\OrdersControl.axaml`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\MainWindow.axaml`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\NotificationManagerWrapper.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\MiniControls\ServerSearchComboBox.axaml.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\Views\OrderCardControl.axaml.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\App.axaml.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.*` |
| EXEC | Regression failure investigation in `ServerSearchComboBox` | 0.97 | Нужно определить, является ли падение headless regression теста проблемой теста или реальным thread-safety дефектом контрола | Исправить прод-код контрола минимальным safe change и повторить таргетный прогон | Нет | Нет | Первый адресный headless тест воспроизвёл `Call from invalid thread`; причина оказалась в `UpdateState()` без гарантии UI-thread. Фикс выполнен в самом контроле, после чего оба automation-contract теста стали зелёными | `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\MiniControls\ServerSearchComboBox.axaml.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\Tests\OrderHeadlessTests.cs` |
| EXEC | Финальная валидация по двум репозиториям | 0.91 | Полные end-to-end сценарии зависят от внешнего сервера и desktop runtime, поэтому нужно отделить code regression от env blockers | Перезапустить полные и таргетные наборы, зафиксировать что именно зелёное, а что блокируется инфраструктурой | Нет | Нет | `AppAutomation` тесты зелёные (`52 + 56`), `Arm.UiTests.Headless` regression tests зелёные, полный `Arm.UiTests.Headless` и полный `Arm.UiTests.FlaUI` воспроизводимо падают на сценариях без соединения с сервером; для `FlaUI` дополнительно восстановлен локальный `WindowsDesktop.App 8.0.26` runtime чтобы тестовый host запускался корректно | `tests/AppAutomation.Abstractions.Tests/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/*`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\*`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\*`, `C:\Projects\My\AppAutomation-worktrees\feat-arm-paritet\.dotnet\shared\Microsoft.WindowsDesktop.App\8.0.26` |
| EXEC | Поднятие live backend и расследование full-suite failure | 0.96 | Нужно отделить инфраструктурную проблему от реального prod-багa, который проявляется только при SignalR/live updates | Поднять `Arm.Srv`, воспроизвести падение, написать regression test, исправить код и заново прогнать full suites | Нет | Нет | После запуска backend на `http://localhost:5000` `Headless` воспроизвёл `InvalidOperationException: Collection was modified` в `ApplyOrderUpdate()`. Добавлен красный regression test, прод-код переведён на snapshot enumeration, после чего адресный тест стал зелёным | `C:\Projects\My\AppAutomation-worktrees\feat-arm-paritet\.tmp_arm_srv\*`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client.ViewModel\OrderPositions\OrderPositionsForSweepViewModel.cs`, `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client.Tests\OrderPositionTests.cs` |
| EXEC | Финальная live validation после backend bugfix | 0.98 | Нужна явная проверка полного сценарного контура, а не только таргетных контрактных тестов | Перепрогнать полный `Headless`, затем полный `FlaUI` на подключённом backend и зафиксировать результат | Нет | Нет | После фикса `ApplyOrderUpdate()` полный `Arm.UiTests.Headless` прошёл `6/6`, а полный `Arm.UiTests.FlaUI` прошёл `4/4`; для console summary `FlaUI` запускался через `dotnet Arm.UiTests.FlaUI.dll` | `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\bin\Debug\net8.0\Arm.UiTests.Headless.exe`, `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\bin\Debug\net8.0-windows7.0\Arm.UiTests.FlaUI.dll`, `C:\Projects\My\AppAutomation-worktrees\feat-arm-paritet\.tmp_tunit_full_headless_live_2`, `C:\Projects\My\AppAutomation-worktrees\feat-arm-paritet\.tmp_tunit_full_flaui_live_dll` |
