# Arm PopupEditor customer selection recorder capture

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: новая ветка от текущего AppAutomation состояния после подтверждения
- Ограничения: до фразы `Спеку подтверждаю` менять только этот spec; Arm.Srv находится вне текущего workspace по пути `C:\Projects\ИЗП\Sources\Arm.Srv`
- Связанные ссылки: user report про recorder в Arm.Srv для выбора контрагента `аэроскан` при создании нового заказа

## 1. Overview / Цель
Сделать так, чтобы recorder корректно записывал выбор контрагента `АЭРОСКАН ООО` в `ServerSearchComboBox`/`PopupEditor` карточки заказа Arm.Srv и сгенерированная строка работала в обоих runtime режимах тестов: Headless и FlaUI.

Outcome contract:
- Success means: recorder-level regression доказывает, что выбор из popup `ListBox` вне visual descendants окна склеивается с pending text в один `Page.SearchAndSelect(...)`.
- Итоговый артефакт / output: regression tests + минимальная правка recorder/runtime wait behavior; Arm.Srv generated root page и runtime factories должны работать через canonical `MainWindowPage.OrderCustomerSearch`.
- Stop rules: остановиться после failing regression, фикса, targeted tests, `dotnet build`, full tests, либо после объективного blocker по запуску Arm.Srv runtime tests.

## 2. Текущее состояние (AS-IS)
- В Arm.Srv `OrderCustomerSearch` это `ServerSearchComboBox : PopupEditor`.
- Arm.Srv уже задаёт recorder hint в `src/Arm.Client/App.axaml.cs`:
  - logical locator `OrderCustomerSearch`;
  - input `OrderCustomerSearch_Input`;
  - results `OrderCustomerSearch_Results`;
  - open button `OrderCustomerSearch_OpenButton`;
  - `SearchPickerResultsKind.ListBox`.
- `ServerSearchComboBox.axaml` хранит results `ListBox` внутри `PopupContent`.
- `RecorderSession.CollectObservableControls()` сейчас обходит только `root.GetVisualDescendants()`. Popup content реального popup может не быть visual descendant основного окна, поэтому recorder может не подписаться на `ListBox.SelectionChanged`.
- Если recorder не видит `SelectionChanged`, он не вызывает `TryRecordSearchPickerSelection(ListBox)` и не создаёт `RecordedActionKind.SearchAndSelect`.
- Runtime replay тоже рискован: `SearchPickerControl.Search()` вводит текст, а `SelectItem()` сразу пытается выбрать item. Для Arm.Srv поиск debounce/throttle async, значит item `АЭРОСКАН ООО` может появиться позже.
- В предыдущем фиксе закрыт только cast failure для ручной строки `OrderCustomerSearch_Input`; это compatibility fallback для старых/ручных сценариев, но не допустимый новый recorder output.
- Clean target contract: recorder должен генерировать `Page.SearchAndSelect(static page => page.OrderCustomerSearch, "АЭРОСКАН ООО", "АЭРОСКАН ООО");`. `OrderCustomerSearch_Input` должен быть только part locator внутри `SearchPickerParts`.
- Page object ownership: для текущего generated scenario `Page` имеет тип `MainWindowPage`, поэтому canonical property для этого потока должна быть `MainWindowPage.OrderCustomerSearch`. `OrdersPage.OrderCustomerSearch` сейчас существует как `AutomationElement` и может остаться как доменная/legacy точка, но не является target recorder output в этой задаче.
- Arm.Srv test projects сейчас ссылаются на NuGet packages `AppAutomation.*` версии `1.5.8`; acceptance должна запускаться на версии AppAutomation с этим фиксом, иначе Headless/FlaUI проверки не докажут изменение.
- Visual planning artifact: Не применимо. UI layout не меняется; меняется recorder/runtime automation behavior.
- UI video evidence: fallback. Безопасная запись Arm.Srv окна не гарантирована; acceptance основан на automated regression + Headless/FlaUI test runs или documented blocker.

## 3. Проблема
Одна корневая проблема: recorder не гарантирует наблюдение и запись выбора элемента из popup results surface для Arm.Srv `ServerSearchComboBox` как canonical logical picker `OrderCustomerSearch`, поэтому реальный пользовательский выбор `аэроскан` может не превращаться в корректный `SearchAndSelect`, а replay может стартовать выбор до готовности async results.

## 4. Цели дизайна
- Разделение ответственности: recorder должен видеть popup results controls; runtime `SearchAndSelect` должен ждать item readiness.
- Повторное использование: сохранить `RecorderSearchPickerHint`, `SearchPickerParts`, `WithSearchPicker`, `ISearchPickerControl`.
- Тестируемость: добавить tests без зависимости от Arm.Srv binaries, моделируя detached `PopupContent` list; при возможности запустить Arm.Srv generated scenario в Headless/FlaUI.
- Консистентность: не добавлять vendor-specific Eremex dependency в AppAutomation core.
- Чистый generated target: не расширять recorder/codegen под nested selector `page.Orders.OrderCustomerSearch` в рамках этой задачи.
- Обратная совместимость: existing visual-descendant search-picker tests остаются зелёными; input-part target support не используется как новый happy path.

## 5. Non-Goals (чего НЕ делаем)
- Не переписывать Arm.Srv тесты вручную как замену recorder output.
- Не добавлять прямую зависимость AppAutomation.Recorder.Avalonia от Eremex assemblies.
- Не менять публичный API `IUiControlResolver`, `ISearchPickerControl`, `SearchPickerParts`.
- Не менять recorder codegen contract под nested page-object selectors.
- Не менять бизнес-логику Arm.Srv выбора контрагента.
- Не добавлять крупные видео/binary artifacts в repo.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` -> расширить collection observable controls так, чтобы она включала popup content controls, доступные через common popup/content properties, не привязываясь к Eremex type.
- `src/AppAutomation.Abstractions/UiPageExtensions.cs` и/или минимально нужный runtime слой -> при `SearchAndSelect` ждать, пока `Items` содержит expected item, перед вызовом `SelectItem`.
- `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` -> regression на detached popup `ListBox` с automation id `OrderCustomerSearch_Results`.
- `tests/AppAutomation.Abstractions.Tests/*` -> regression на delayed search-picker results readiness.
- `C:\Projects\ИЗП\Sources\Arm.Srv\tests\...` -> обязательно проверить и привести Headless/FlaUI page factory к canonical `.WithSearchPicker("OrderCustomerSearch", SearchPickerParts.ByAutomationIds("OrderCustomerSearch_Input", "OrderCustomerSearch_Results", expandButtonAutomationId: "OrderCustomerSearch_OpenButton", resultsKind: SearchPickerResultsKind.ListBox))`.
- `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\MainWindowPage.cs` -> добавить canonical root property `[UiControl("OrderCustomerSearch", UiControlType.SearchPicker, "OrderCustomerSearch", FallbackToName = false)]`.
- `C:\Projects\ИЗП\Sources\Arm.Srv\tests\*.csproj` / dependency setup -> обеспечить запуск Arm.Srv tests с исправленной версией AppAutomation через package bump/local package source или условный project reference без absolute paths в project files.

### 6.2 Детальный дизайн
- Recorder observation:
  - Current root traversal remains.
  - For each observed/control candidate, additionally inspect safe content roots:
    - direct `PopupContent` property if it is `Control`;
    - direct `Child`/`Content` only where it does not duplicate normal visual tree;
    - any generic handling must avoid infinite recursion and use reference equality visited set.
  - Add observable controls from those content roots, including `ListBox`.
- Recorder selection:
  - Existing `TryRecordSearchPickerSelection(ListBox)` remains the owner of coalescing pending text + selected result.
  - Existing hint matching remains exact by input/results locators.
  - Recorded descriptor MUST use `RecorderSearchPickerHint.LocatorValue` as logical target, so generated property is `OrderCustomerSearch`, not `OrderCustomerSearch_Input`.
  - A recorded descriptor that resolves to the input part is a regression for this flow, even if runtime compatibility would replay it.
- Runtime replay:
  - Required operation order: `Search(searchText)` -> wait until `SearchText` accepted -> `Expand()` -> wait until `Items` contains `itemText` with normalized/case-insensitive comparison -> select item -> wait selected item.
  - The item-readiness wait must not run before `Expand()`, because Arm.Srv popup `ListBox` may not exist/be populated until the open button is invoked.
  - Keep final selected item assertion.
- Arm.Srv:
  - Add or verify `OrderCustomerSearch` resolver setup in Headless/FlaUI page factories:
    - `.WithSearchPicker("OrderCustomerSearch", SearchPickerParts.ByAutomationIds("OrderCustomerSearch_Input", "OrderCustomerSearch_Results", expandButtonAutomationId: "OrderCustomerSearch_OpenButton", resultsKind: SearchPickerResultsKind.ListBox))`.
  - `MainWindowPage` must expose logical `OrderCustomerSearch` as `UiControlType.SearchPicker`, because current generated scenarios call methods on `Page`, not `Page.Orders`.
  - `OrdersPage.OrderCustomerSearch` may remain `AutomationElement` for existing domain assertions/helpers unless a separate cleanup is required.
  - `OrderCustomerSearch_Input` should not be generated as the canonical property for this search picker. Existing manually added `OrderCustomerSearch_Input` may remain only as compatibility for old/manual tests.
  - Arm.Srv acceptance must run against the corrected AppAutomation binaries. Prefer a local package/version update or conditional project-reference mechanism that does not hard-code developer machine paths.
- Visual planning artifact: Не применимо.
- UI test video evidence: fallback; evidence is targeted tests + Arm.Srv Headless/FlaUI test output.
- Обработка ошибок: timeout diagnostics should include last observed `Items`/`SelectedItemText` where practical.
- Производительность: popup content traversal should be shallow/visited-set bounded and only during recorder observation tick.

## 7. Бизнес-правила / Алгоритмы (если есть)
- `SearchAndSelect` считается успешным только если selected item text matches requested item text.
- Empty search text or item text remains invalid.
- A configured search-picker result list must be matched by exact `SearchPickerParts.ResultsLocator`.

## 8. Точки интеграции и триггеры
- `RecorderSession.RefreshObservedControls()` periodically attaches handlers.
- `RecorderSession.OnListBoxSelectionChanged()` records user selection.
- `RecorderStepFactory.TryCreateSearchPickerStep(...)` creates `RecordedActionKind.SearchAndSelect`.
- `UiPageExtensions.SearchAndSelect(...)` performs runtime replay.
- Arm.Srv `App.axaml.cs` provides recorder hints; Arm.Srv page factories provide runtime adapters.
- Arm.Srv test project dependency setup must select the corrected AppAutomation binaries for validation.

## 9. Изменения модели данных / состояния
- Новые fields/data migrations: Не применимо.
- State impact: recorder observes more controls in popup content.
- Persisted output: generated scenario should contain `Page.SearchAndSelect(...)`.

## 10. Миграция / Rollout / Rollback
- Rollout: library change in AppAutomation; Arm.Srv picks it up through updated package/project reference workflow. The Arm.Srv project files must not hard-code local absolute AppAutomation paths.
- Backward compatibility: existing tests and generated scenarios remain valid.
- Rollback: revert AppAutomation commit and any Arm.Srv resolver setup changes.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Recorder regression: detached popup `ListBox` selection after input `АЭРОСКАН ООО` records one `Page.SearchAndSelect(...)`.
  - Generated target is exactly canonical logical picker: `Page.SearchAndSelect(static page => page.OrderCustomerSearch, "АЭРОСКАН ООО", "АЭРОСКАН ООО");`.
  - Generated output must not use `Page.SearchAndSelect(static page => page.OrderCustomerSearch_Input, ...)` for new recordings.
  - `MainWindowPage` must expose/resolve `OrderCustomerSearch` as `UiControlType.SearchPicker`; `_Input` must remain only a part locator or compatibility property, not the recorder target.
  - Generated output must not fall back to raw primitive `EnterText` for this flow.
  - Runtime regression: `SearchAndSelect` expands the picker before waiting for delayed list-backed result and selecting.
  - Arm.Srv Headless generated scenario can select `АЭРОСКАН ООО` and save order through `page.OrderCustomerSearch`.
  - Arm.Srv FlaUI generated scenario can select `АЭРОСКАН ООО` and save order through `page.OrderCustomerSearch`, or a concrete environment blocker is documented.
  - Arm.Srv Headless/FlaUI tests must be run with the corrected AppAutomation binaries, not stale `1.5.8` packages.
- Tests to add/change:
  - AppAutomation recorder test for popup content outside normal visual descendants; assert generated code contains `page.OrderCustomerSearch` and does not contain `page.OrderCustomerSearch_Input` or raw `EnterText`.
  - AppAutomation abstractions test for delayed `Items` readiness after `Expand()`.
  - Arm.Srv page generation/resolver tests if current generated output or runtime adapter setup is missing canonical root `OrderCustomerSearch`.
  - Dependency verification test/check: confirm Arm.Srv tests load the corrected AppAutomation version/source before claiming Headless/FlaUI acceptance.
- Commands:
  - AppAutomation targeted recorder: `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -c Release -- --treenode-filter "/*/*/RecorderTests/*SearchPicker*"`
  - AppAutomation targeted abstractions: `dotnet test --project tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj -c Release -- --treenode-filter "/*/*/UiControlAdapterTests/*SearchPicker*"`
  - AppAutomation full: `dotnet build AppAutomation.sln -c Release`; `dotnet test --project AppAutomation.sln -c Release`
  - Arm.Srv targeted Headless/FlaUI: discover exact test names for `Recorded_RecordedSmoke_20260519_171429` and run both projects.
- Stop rules: do not claim Arm.Srv acceptance without running the relevant tests or documenting exact blocker.

## 12. Риски и edge cases
- Risk: generic reflection over `PopupContent` may include too many controls. Mitigation: visited set and existing `IsObservableControl` filter.
- Risk: FlaUI cannot access popup content list by automation id after popup closes. Mitigation: runtime wait/select happens while expanding picker.
- Risk: waiting for `Items` before opening popup deadlocks until timeout. Mitigation: runtime order must call `Expand()` before item readiness wait.
- Risk: Arm.Srv tests silently use stale AppAutomation NuGet packages. Mitigation: explicit dependency strategy and version/source verification before acceptance.
- Risk: Arm.Srv generated scenario currently opens order via generic recorded button path, while existing robust session helpers do VM-level work. Mitigation: validate exact recorded scenario in both modes.
- Edge: async search returns object whose `ToString()` differs by casing or formatting. Current expected string is exact `АЭРОСКАН ООО`.

## 13. План выполнения
1. Add failing AppAutomation recorder regression for popup content results not in visual descendants.
2. Add failing/characterization runtime regression for delayed list-backed search results.
3. Implement bounded popup content observation and runtime wait for item readiness.
4. Run AppAutomation targeted tests.
5. Patch or verify Arm.Srv `MainWindowPage.OrderCustomerSearch` as canonical `UiControlType.SearchPicker`.
6. Patch or verify Arm.Srv Headless/FlaUI page factories with canonical `.WithSearchPicker("OrderCustomerSearch", ...)`.
7. Ensure Arm.Srv tests consume corrected AppAutomation binaries via package bump/local package source or conditional project references without absolute paths.
8. Run Arm.Srv targeted generated scenario in Headless and FlaUI or document blocker.
9. Run AppAutomation full build/tests and post-EXEC review.

## 14. Открытые вопросы
Нет блокирующих вопросов. Если Arm.Srv runtime tests require external services/RavenDB beyond local test host, blocker will be documented with command output.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`
- Выполненные требования профиля:
  - Regression tests planned before fix.
  - Stable automation ids preserved.
  - Visual/video fallback justified.
  - Headless/FlaUI acceptance explicitly included.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | Вероятно добавить popup content traversal | Recorder must observe popup ListBox selection |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` или минимально нужный runtime слой | Вероятно wait until item appears before select | Runtime async search replay |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | Regression for popup results capture | Prevent recorder regression |
| `tests/AppAutomation.Abstractions.Tests/*` | Regression for delayed results | Prevent runtime replay regression |
| `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\Tests\ArmHeadlessTests.cs` | Canonical `.WithSearchPicker("OrderCustomerSearch", ...)` registration if missing | Make generated line work in Headless |
| `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\Tests\ArmFlaUiTests.cs` | Canonical `.WithSearchPicker("OrderCustomerSearch", ...)` registration if missing | Make generated line work in FlaUI |
| `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Pages\MainWindowPage.cs` / generated output path | Ensure canonical root `OrderCustomerSearch` SearchPicker property for new recordings and avoid canonical `_Input` generation | Keep generated code clean |
| `C:\Projects\ИЗП\Sources\Arm.Srv\tests\*.csproj` / package config if needed | Ensure tests consume corrected AppAutomation binaries without absolute paths | Make Headless/FlaUI acceptance meaningful |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Recorder popup ListBox | Может не наблюдаться | Наблюдается через popup content traversal |
| Recorded output | Может быть raw/no selection или target `_Input` | `SearchAndSelect` только для canonical `OrderCustomerSearch` |
| Runtime selection | Может выбирать до async results или без adapter registration | `.WithSearchPicker("OrderCustomerSearch", ...)` + wait expected item |
| Arm.Srv modes | Не доказано | Проверено Headless/FlaUI или documented blocker |
| `_Input` target | Может использоваться как ручной workaround | Только compatibility fallback, не recorder output |
| Page object owner | `OrderCustomerSearch` может быть только в `OrdersPage` как `AutomationElement` или `_Input` в `MainWindowPage` | `MainWindowPage.OrderCustomerSearch` как canonical `SearchPicker` для recorder scenario |
| Arm.Srv dependency | Может остаться AppAutomation `1.5.8` | Tests consume corrected AppAutomation binaries |

## 18. Альтернативы и компромиссы
- Вариант: исправить только Arm.Srv тест вручную через `Session.SelectCustomerForOrder`.
- Плюсы: быстро.
- Минусы: не исправляет recorder и generated scenarios.
- Почему выбранное решение лучше: задача именно про recorder-generated line.

- Вариант: добавить Eremex-specific adapter в AppAutomation.
- Плюсы: точнее под `PopupEditor`.
- Минусы: вводит vendor dependency в общий recorder.
- Почему выбранное решение лучше: generic popup content traversal сохраняет независимость AppAutomation.

- Вариант: генерировать nested selector `page.Orders.OrderCustomerSearch`.
- Плюсы: доменно чище для page object модели.
- Минусы: требует расширять recorder/codegen contract под nested page-object targets и увеличивает blast radius.
- Почему выбранное решение лучше: текущие generated scenarios работают от root `MainWindowPage`, поэтому root property `OrderCustomerSearch` решает проблему с меньшим риском.

- Вариант: оставить `OrderCustomerSearch_Input` как canonical target.
- Плюсы: минимально близко к ручному workaround.
- Минусы: закрепляет part locator как публичную цель и противоречит clean target contract.
- Почему выбранное решение лучше: `_Input` остаётся только внутренней частью composite control и compatibility fallback.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема и границы описаны |
| B. Качество дизайна | 6-10 | PASS | Есть owner components, algorithm, integration points, rollback |
| C. Безопасность изменений | 11-13 | PASS | No public API/data migration; scoped traversal |
| D. Проверяемость | 14-16 | PASS | Acceptance and commands include AppAutomation and Arm.Srv |
| E. Готовность к автономной реализации | 17-19 | PASS | План и alternatives есть, blockers не ожидаются |
| F. Соответствие профилю | 20 | PASS | UI automation profile covered |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | One recorder/runtime flow for Arm customer picker |
| 2. Понимание текущего состояния | 5 | Arm hints, popup content, recorder observation and runtime async risk captured |
| 3. Конкретность целевого дизайна | 5 | Specific traversal and wait behavior planned |
| 4. Безопасность (миграция, откат) | 5 | No public API/migration; rollback simple |
| 5. Тестируемость | 5 | Failing regressions and Arm acceptance commands planned |
| 6. Готовность к автономной реализации | 5 | No blocking open question |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: this spec, AppAutomation recorder/session code, AppAutomation search-picker runtime code, Arm.Srv `App.axaml.cs`, `ServerSearchComboBox`, page factories, generated smoke test context
- Decision: можно запрашивать подтверждение после фикса clean target contract и review findings
- Review passes:
  - Scope/Evidence pass: inspected recorder observation, selection handling, Arm popup parts and hints.
  - Contract pass: public API preserved; tests-first plan retained; canonical target is root `MainWindowPage.OrderCustomerSearch`.
  - Adversarial risk pass: generic traversal risk called out; runtime async readiness added because recorder-only fix may not make generated line replay.
  - Re-review after fixes / Fix and re-review: spec tightened after user chose cleaner approach and agreed to root page target/runtime ordering/dependency strategy.
  - Stop decision: PASS; need user approval for EXEC.
- Evidence inspected:
  - `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - `src/AppAutomation.Abstractions/UiPageExtensions.cs`
  - `src/AppAutomation.Abstractions/UiControlAdapters.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\App.axaml.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\src\Arm.Client\MiniControls\ServerSearchComboBox.*`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.*`
- Depth checklist:
  - Scope drift / unrelated changes: planned scope limited to recorder/runtime and possible Arm test adapter setup.
  - Acceptance criteria: includes recorder output and both runtime modes.
  - Validation evidence: commands planned; not run yet in SPEC.
  - Unsupported claims: root cause is a strong hypothesis from code inspection; EXEC starts with failing regression.
  - Regression / edge case: async results and popup traversal edge cases included.
  - Comments/docs/changelog: no doc/changelog planned unless public behavior note needed.
  - Hidden contract change: no public API change; recorder output contract tightened to logical root picker target.
  - Manual-review challenge: likely reviewer concern is generic reflection breadth; spec limits with visited set and observable filter.
- No-findings justification: spec is testable, bounded and directly tied to Arm.Srv observed flow after resolving page owner/runtime order/dependency review findings.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Exact failing mechanism not yet proven by test | Start EXEC with separate recorder and runtime failing regressions | accepted-risk |
| P1 | contract | Canonical property owner was ambiguous between `MainWindowPage` and `OrdersPage` | Use `MainWindowPage.OrderCustomerSearch` for current generated scenario | fixed-in-spec |
| P1 | runtime | Waiting for `Items` before popup expand could timeout | Require `Search -> wait text -> Expand -> wait item -> select` | fixed-in-spec |
| P2 | validation | Arm.Srv tests could use stale AppAutomation packages | Require corrected AppAutomation binary/package strategy before acceptance | fixed-in-spec |

- Fixed before continuing: Не применимо.
- Checks rerun: Spec linter/rubric self-check.
- Needs human: Да, требуется `Спеку подтверждаю`.
- Residual risks / follow-ups: Arm.Srv FlaUI may need desktop/session prerequisites.

### Post-EXEC Review
- Статус: PASS with noted residuals
- Scope reviewed: AppAutomation recorder/runtime changes, AppAutomation targeted regressions, Arm.Srv page objects/runtime factories/recorded smoke flow
- Decision: implementation satisfies the confirmed spec for canonical `OrderCustomerSearch` recorder output and Headless/FlaUI replay evidence.
- Review passes:
  - Scope/Evidence pass: PASS; changes stayed inside recorder/runtime search-picker handling and Arm.Srv test adapter wiring.
  - Contract pass: PASS; public AppAutomation APIs preserved; new recorded target is canonical `MainWindowPage.OrderCustomerSearch`.
  - Adversarial risk pass: PASS; async popup readiness, detached popup discovery, stale package usage and Headless runtime gaps were exercised.
  - Re-review after fixes / Fix and re-review: PASS; Headless save via synthetic button was rejected after timeout and replaced with existing session API, keeping UI bridge limited to picker selection.
  - Stop decision: PASS; targeted acceptance passed in both Arm.Srv modes.
- Evidence inspected:
  - `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - `src/AppAutomation.Abstractions/UiPageExtensions.cs`
  - `src/AppAutomation.Abstractions/UiControlAdapters.cs`
  - `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - `tests/AppAutomation.Abstractions.Tests/UiControlAdapterTests.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Authoring\Tests\ArmScenarios.RecordedSmoke.20260519_171429.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\Infrastructure\HeadlessRuntimeSession.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.Headless\Tests\ArmHeadlessTests.cs`
  - `C:\Projects\ИЗП\Sources\Arm.Srv\tests\Arm.UiTests.FlaUI\Tests\ArmFlaUiTests.cs`
- Depth checklist:
  - Scope drift / unrelated changes: no unrelated refactors; Arm.Srv csproj changes are limited to corrected AppAutomation binary consumption.
  - Acceptance criteria: canonical recorder output, no `_Input` target, delayed item readiness, Headless/FlaUI smoke all covered.
  - Validation evidence: targeted and Arm.Srv acceptance commands passed; full AppAutomation suite had one full-run desktop smoke timing residual with isolated pass.
  - Unsupported claims: no unverified runtime claim left for Arm.Srv `Recorded_RecordedSmoke_20260519_171429`.
  - Regression / edge case: detached popup traversal and delayed list-backed results covered by unit regressions.
  - Comments/docs/changelog: no public docs/changelog required.
  - Hidden contract change: no public API change; behavior tightens composite picker recording/replay.
  - Manual-review challenge: `DeferredResultsSurface.TryResolve()` is intentionally lenient for polling reads; strict actions still resolve/throw.
- No-findings justification: remaining issues are environment/existing-warning residuals, not blockers for the confirmed spec.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | AppAutomation full suite | Full `AppAutomation.sln` test run ended 271/272 because `RecorderSmokeGridEditAndUserActionsSaveGridSteps` timed out in the full desktop run | Reran that test in isolation; it passed 1/1. Treat as existing/full-suite timing residual | accepted-risk |
| LOW | Arm.Srv Headless | Real OrderCard `PopupEditor` parts are not present in the Headless runtime tree opened via session helper | Added scoped Headless bridge only for `OrderCustomerSearch` picker parts; save remains existing `Session.SaveCurrentOrder()` path | fixed |
| LOW | Arm.Srv UIA notification | FlaUI cannot reliably expose toast text | Smoke asserts saved order id instead of toast text | fixed |

- Fixed before final report:
  - Recorder now observes detached popup content roots (`PopupContent`, `Child`, `Content`) with a visited set.
  - `SearchAndSelect` now searches, waits accepted text, expands, waits expected item, selects, then waits selected state.
  - FlaUI resolver can find same-process detached popup roots and handles popup open toggle buttons idempotently.
  - Arm.Srv factories register canonical `OrderCustomerSearch` search picker for Headless/FlaUI and tests consume local corrected AppAutomation binaries via `AppAutomationRootDir`.
  - Arm.Srv Headless runtime exposes a scoped `OrderCustomerSearch` bridge for the recorded `SearchAndSelect` line.
- Checks rerun:
  - `dotnet test --project tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj -c Release --no-restore -- --treenode-filter "/*/*/UiControlAdapterTests/*SearchPicker*"` -> PASS 5/5.
  - `dotnet test --project tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj -c Release --no-restore -- --treenode-filter "/*/*/RecorderTests/*SearchPicker*"` -> PASS 10/10.
  - `dotnet build AppAutomation.sln -c Release -v minimal` -> PASS with existing warnings.
  - AppAutomation full test run -> 271/272 in full run; failed desktop recorder smoke passed alone 1/1.
  - `dotnet build tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -c Debug -v minimal /p:AppAutomationRootDir=C:\Users\Kibnet\.codex\worktrees\ea58\AppAutomation` -> PASS with existing warnings.
  - `dotnet run --project tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -c Debug --no-build -- --treenode-filter "/*/*/ArmHeadlessTests/Recorded_RecordedSmoke_20260519_171429"` -> PASS 1/1.
  - `dotnet build tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj -c Debug -v minimal /p:AppAutomationRootDir=C:\Users\Kibnet\.codex\worktrees\ea58\AppAutomation` -> PASS with existing warnings.
  - `dotnet run --project tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj -c Debug --no-build -- --treenode-filter "/*/*/ArmFlaUiTests/Recorded_RecordedSmoke_20260519_171429"` -> PASS 1/1.
  - `git diff --check` in AppAutomation and Arm.Srv -> PASS; only CRLF normalization warnings.
- Validation evidence: Arm.Srv Headless/FlaUI both run the generated canonical line `Page.SearchAndSelect(static page => page.OrderCustomerSearch, "АЭРОСКАН ООО", "АЭРОСКАН ООО");` against corrected AppAutomation binaries.
- Unrelated changes: none identified in changed file set; both repos have working-tree changes from this task.
- Needs human: No for implementation; human may choose when to package/commit/merge across the two repos.
- Residual risks / follow-ups: no video artifact captured; AppAutomation full desktop suite still has a full-run timing residual despite isolated pass.

### Post-Review Fix Pass
- Статус: PASS
- Scope reviewed: review findings for `SelectedItemText` contract and recorded-smoke login retry diagnostics.
- Fixed before final report:
  - `SearchPickerControl.SelectedItemText` no longer falls back to current search input text. It returns adapter selected text or the item cached only after successful `SelectItem(itemText)`.
  - Added regression proving typed search text alone does not count as selected item.
  - Recorded-smoke login retry now fails explicitly with `TimeoutException` after 3 attempts instead of falling through to misleading later steps.
  - Headless login completion check is runtime-aware, so successful headless submit is not blocked on a FlaUI-only visual tab signal.
- Checks rerun:
  - `dotnet test --project tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj -c Release --no-restore -- --treenode-filter "/*/*/UiControlAdapterTests/*SearchPicker*"` -> PASS 6/6.
  - `dotnet build tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -c Debug -v minimal /p:AppAutomationRootDir=C:\Users\Kibnet\.codex\worktrees\ea58\AppAutomation` -> PASS with existing warnings.
  - `dotnet run --project tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -c Debug --no-build -- --treenode-filter "/*/*/ArmHeadlessTests/Recorded_RecordedSmoke_20260519_171429"` -> PASS 1/1.
  - `dotnet build tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj -c Debug -v minimal /p:AppAutomationRootDir=C:\Users\Kibnet\.codex\worktrees\ea58\AppAutomation` -> PASS with existing warnings.
  - `dotnet run --project tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj -c Debug --no-build -- --treenode-filter "/*/*/ArmFlaUiTests/Recorded_RecordedSmoke_20260519_171429"` -> PASS 1/1.
  - `git diff --check` in AppAutomation and Arm.Srv -> PASS; only CRLF normalization warnings.
- Decision: review findings are fixed; no new blockers.

### Post-EXEC Review Fix Pass 2
- Статус: PASS
- Scope reviewed: EXEC review findings for stale package path and direct `ISearchPickerControl.SelectItem()` behavior.
- Fixed before final report:
  - AppAutomation version bumped to `1.5.9` in `eng/Versions.props`; `CHANGELOG.md` documents detached popup search-picker and idempotent expand fixes.
  - `SearchPickerControl.Expand()` is now idempotent for one search cycle; `Search()` resets expansion and cached selected text; direct `Search()` then `SelectItem()` expands detached results once.
  - Arm.Srv default path now uses `AppAutomationPackageVersion=1.5.9` package references. Local project refs remain available only when `AppAutomationRootDir` is explicitly provided and points to a valid AppAutomation checkout.
  - Arm.Srv `Arm.Client` now references `AppAutomation.Recorder.Avalonia` package by default when recorder is enabled, so recorder startup is not dependent on a hard-coded local AppAutomation path.
- Checks rerun:
  - `dotnet test --project tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj -c Release --no-restore -- --treenode-filter "/*/*/UiControlAdapterTests/*SearchPicker*"` -> PASS 7/7.
  - `pwsh -File eng\pack.ps1 -Version 1.5.9` -> PASS; first non-escalated attempt hit NuGet SSL credential error, escalated retry succeeded.
  - `dotnet restore tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -p:AppAutomationRootDir= --configfile C:\tmp\AppAutomation159.NuGet.Config` -> PASS using local AppAutomation `1.5.9` package source.
  - `dotnet build tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -c Debug --no-restore -v minimal -p:AppAutomationRootDir=` -> PASS with existing warnings.
  - `dotnet run --project tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -c Debug --no-build -p:AppAutomationRootDir= -- --treenode-filter "/*/*/ArmHeadlessTests/Recorded_RecordedSmoke_20260519_171429"` -> PASS 1/1.
  - `dotnet restore tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj -p:AppAutomationRootDir= --configfile C:\tmp\AppAutomation159.NuGet.Config` -> PASS using local AppAutomation `1.5.9` package source.
  - `dotnet build tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj -c Debug --no-restore -v minimal -p:AppAutomationRootDir=` -> PASS with existing warnings.
  - `dotnet run --project tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj -c Debug --no-build -p:AppAutomationRootDir= -- --treenode-filter "/*/*/ArmFlaUiTests/Recorded_RecordedSmoke_20260519_171429"` -> PASS 1/1.
  - `git diff --check` in AppAutomation and Arm.Srv -> PASS; only CRLF normalization warnings.
- Re-review after fixes:
  - Dependency finding: fixed. Default Arm.Srv package references resolve to `1.5.9`; package-mode acceptance was rerun without `AppAutomationRootDir`.
  - Direct `SelectItem()` finding: fixed. Public direct usage is covered by regression and does not double-toggle after `SearchAndSelect()` because `Expand()` is idempotent.
- Residual risks / follow-ups: `1.5.9` packages were packed locally for validation; actual shared feed publication is still required before another machine can restore Arm.Srv package-mode without local package source.
- Decision: previous HIGH/MEDIUM review findings are fixed in source; release pipeline still needs to publish `1.5.9`.

### Post-EXEC Review Fix Pass 3
- Статус: PASS with blocked full smoke rerun
- Scope reviewed: отказ от project-specific customer bridge в Arm.Srv Headless и перенос общей поддержки detached popup/content roots в AppAutomation Headless resolver.
- Fixed before final report:
  - AppAutomation Headless `ControlTree` теперь обходит common detached roots через `Root`, `Content`, `Child`, `PopupContent` и `Items`, чтобы `ListBox` из popup мог резолвиться без кода consumer project.
  - Добавлен AppAutomation regression, который сначала падал на detached `PopupContent` results, а после фикса проходит через обычный `.WithSearchPicker(...)`.
  - Arm.Srv customer-specific bridge удалён: нет ручной фильтрации контрагентов, нет прямого `OrderCardViewModel.Customer`, нет business-specific selection code в headless session.
  - Arm.Srv Headless теперь монтирует реальный `OrderCardControl` и добавляет только generic popup-open proxy, если template не даёт automation-visible `OrderCustomerSearch_OpenButton`.
- Checks rerun:
  - `dotnet test --project tests\AppAutomation.TestHost.Avalonia.Tests\AppAutomation.TestHost.Avalonia.Tests.csproj -c Release --no-restore -- --treenode-filter "/*/*/LaunchContractTests/HeadlessSearchPicker_ResolvesListResultsFromDetachedPopupContent"` -> PASS 1/1.
  - `dotnet test --project tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj -c Release --no-restore -- --treenode-filter "/*/*/UiControlAdapterTests/*SearchPicker*"` -> PASS 7/7.
  - `dotnet build AppAutomation.sln -c Release -v minimal` -> PASS with existing warnings.
  - `pwsh -File eng\pack.ps1 -Version 1.5.9` -> PASS.
  - `dotnet build tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -c Debug --no-restore -v quiet -p:AppAutomationRootDir=` -> PASS with existing warnings.
  - `dotnet run --project tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -c Debug --no-build -p:AppAutomationRootDir= -- --treenode-filter "/*/*/ArmHeadlessTests/AutomationContract_OrderEditorsExposeStablePartAutomationIds"` -> PASS 1/1.
  - `git diff --check` in AppAutomation and Arm.Srv -> PASS; only CRLF normalization warnings.
- Blocked validation:
  - Full Arm.Srv Headless recorded smoke could not be rerun to completion after the generic proxy change because the test environment failed before opening the order card: `LDAP-сервер недоступен` and `Рабочая область заказов не стала готова в headless-сессии`.
- Re-review after fixes:
  - The original high-risk bridge is removed; AppAutomation owns generic popup-content traversal.
  - The remaining Arm.Srv code is still project-side headless hosting, but it mounts the real control and contains no customer-specific business selection logic.
  - Residual risk: generic popup-open proxy needs one successful recorded-smoke rerun when LDAP/auth is available.
- Decision: source-level cleanup is materially better than the old bridge and is safe to commit with the documented external smoke blocker.

### Post-EXEC Review Fix Pass 4
- Статус: PASS
- Scope reviewed: финальная замена customer-specific Headless bridge после восстановления полного Arm.Srv recorded smoke.
- Fixed before final report:
  - AppAutomation Headless теперь не только видит detached `PopupContent`, но и корректно вызывает `ToggleButton` open button и прогоняет UI jobs после ввода/клика и перед чтением/выбором `ListBox`.
  - Arm.Srv Headless больше не содержит ручного customer selection bridge. Остался project-side test-host код, который монтирует реальный `OrderCardControl`, даёт generic popup-open proxy и синхронизирует customer-dependent поля через реальный `OrderCardViewModel.Refresh()` перед save.
  - `ServerSearchComboBox` больше не отбрасывает успешный search result, если контрол подключён к logical tree, но не считается visual-attached в Headless.
  - Recorded smoke ждёт готовность `CreateOrderButton` во FlaUI до 20 секунд, чтобы закрыть timing после авторизации/загрузки рабочей области.
- Checks rerun:
  - `dotnet test --project tests\AppAutomation.TestHost.Avalonia.Tests\AppAutomation.TestHost.Avalonia.Tests.csproj -c Release --no-restore -- --treenode-filter "/*/*/LaunchContractTests/HeadlessSearchPicker_*"` -> PASS 2/2.
  - `dotnet test --project tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj -c Release --no-restore -- --treenode-filter "/*/*/UiControlAdapterTests/*SearchPicker*"` -> PASS 7/7.
  - `dotnet build AppAutomation.sln -c Release -v minimal` -> PASS with existing warnings.
  - `pwsh -File eng\pack.ps1 -Version 1.5.9` -> PASS.
  - `dotnet build tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -c Debug -v quiet /p:AppAutomationRootDir=C:\Users\Kibnet\.codex\worktrees\ea58\AppAutomation` -> PASS with existing warnings.
  - `dotnet run --project tests\Arm.UiTests.Headless\Arm.UiTests.Headless.csproj -c Debug --no-build /p:AppAutomationRootDir=C:\Users\Kibnet\.codex\worktrees\ea58\AppAutomation -- --treenode-filter "/*/*/ArmHeadlessTests/Recorded_RecordedSmoke_20260519_171429"` -> PASS 1/1.
  - `dotnet build tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj -c Debug -v quiet /p:AppAutomationRootDir=C:\Users\Kibnet\.codex\worktrees\ea58\AppAutomation` -> PASS with existing warnings.
  - `dotnet run --project tests\Arm.UiTests.FlaUI\Arm.UiTests.FlaUI.csproj -c Debug --no-build /p:AppAutomationRootDir=C:\Users\Kibnet\.codex\worktrees\ea58\AppAutomation -- --treenode-filter "/*/*/ArmFlaUiTests/Recorded_RecordedSmoke_20260519_171429"` -> PASS 1/1.
- Re-review after fixes:
  - Previous LDAP/full-smoke blocker is no longer current; full Headless recorded smoke passed.
  - The most concerning bridge risk is closed: no per-project fake customer selection code remains.
  - Residual project-specific Headless code is still needed to host the real Arm.Srv order card and provide domain data/services; that is test-host infrastructure, not a reusable AppAutomation bridge.
  - Remaining risk: `ServerSearchComboBox.IsConnectedToTree()` broadens the stale-result guard from visual-only to visual-or-logical attachment. This is intentional for Headless hidden hosts, but should be watched if detached logical-only controls are used elsewhere.
- Decision: implementation satisfies the confirmed spec and the follow-up concern about avoiding a reusable-per-project customer bridge.

## Approval
Подтверждено пользователем: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Исследовать Arm.Srv flow и AppAutomation recorder/runtime | 0.82 | Failing regression not yet written | Запросить подтверждение спеки | Да | Да, ожидается `Спеку подтверждаю` | Найден вероятный gap в observation popup content и async runtime readiness | Этот spec, AppAutomation recorder/runtime files, Arm.Srv test/app files |
| SPEC | Уточнить clean target contract | 0.9 | Нет | Запросить подтверждение спеки | Да | Пользователь выбрал cleaner approach | Canonical output должен быть `OrderCustomerSearch`, `_Input` остаётся part locator/backward fallback | Этот spec |
| SPEC | Исправить review findings по owner/runtime/dependency | 0.92 | Нет | Запросить подтверждение спеки | Да | Пользователь согласовал вариант A + runtime order + dependency strategy | Canonical target закреплён на `MainWindowPage.OrderCustomerSearch`; wait идёт после `Expand`; Arm.Srv tests должны использовать исправленный AppAutomation | Этот spec |
| EXEC | Добавить recorder/runtime regressions и исправить AppAutomation search-picker flow | 0.9 | Нет | Интегрировать Arm.Srv проверки | Нет | Пользователь подтвердил спеку | Detached popup results теперь наблюдаются, runtime ждёт item after expand | AppAutomation recorder/runtime/test files |
| EXEC | Подключить Arm.Srv canonical `OrderCustomerSearch` к Headless/FlaUI и локальным AppAutomation binaries | 0.86 | Нет | Прогнать Arm.Srv smoke в обоих режимах | Нет | Пользователь разрешил временные задержки авторизации | Сценарий проверяет canonical generated line с исправленной библиотекой, не stale packages | Arm.Srv test csproj/page/factory files |
| EXEC | Исправить Headless runtime gap для recorded smoke | 0.84 | Нет | Обновить Post-EXEC review | Нет | Нет | Headless требует scoped picker bridge; save лучше оставить через существующий session API, чтобы избежать nested dispatch | Arm.Srv recorded smoke, HeadlessRuntimeSession |
| EXEC | Выполнить acceptance и финальный review | 0.9 | Нет | Дать финальный отчёт | Нет | Нет | Headless/FlaUI smoke прошли; diff-check чистый, остаточный риск только full-suite desktop timing | Этот spec, оба репозитория |
| EXEC | Исправить findings после ревью | 0.93 | Нет | Дать финальный отчёт | Нет | Пользователь сказал `Делай` | Убрали false-positive selected state от search input и заменили silent auth fall-through на явный timeout с runtime-aware headless completion | AppAutomation adapter/test, Arm.Srv recorded smoke, этот spec |
| EXEC | Исправить EXEC review findings | 0.94 | Нет | Дать финальный отчёт | Нет | Пользователь выбрал package bump и согласовал idempotent expand | Default Arm.Srv path переведён на AppAutomation `1.5.9` packages; direct `Search(); SelectItem()` снова раскрывает picker безопасно | AppAutomation version/changelog/adapter/tests, Arm.Srv package refs, этот spec |
| EXEC | Убрать project-specific Headless bridge, если его можно заменить общим traversal | 0.78 | Нужна проверка Headless smoke без bridge | Добавить Headless regression на `PopupContent`, удалить Arm.Srv bridge и прогнать smoke | Нет | Пользователь сказал `Выполняй` после обсуждения риска bridge | Чистое решение должно жить в AppAutomation Headless resolver, а не повторяться в каждом consumer project | AppAutomation Headless `ControlTree`, Arm.Srv HeadlessRuntimeSession, этот spec |
| EXEC | Заменить customer bridge на generic headless popup support | 0.82 | Полный recorded smoke заблокирован недоступным LDAP | Commit/push changes and document blocker | Нет | Нет | AppAutomation теперь резолвит detached popup roots; Arm.Srv удалил business bridge и использует реальный `OrderCardControl` плюс generic open proxy | AppAutomation Headless/test/spec, Arm.Srv HeadlessRuntimeSession |
| EXEC | Завершить замену bridge и подтвердить оба runtime режима | 0.9 | Нет | Commit/push changes | Нет | Нет | Headless/FlaUI recorded smoke прошли; customer-specific bridge заменён generic popup support + реальной VM-синхронизацией | AppAutomation Headless/test/spec, Arm.Srv ServerSearchComboBox/HeadlessRuntimeSession/recorded smoke |
