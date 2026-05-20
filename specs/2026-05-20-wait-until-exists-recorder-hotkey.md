# WaitUntilExists и отдельная hotkey в recorder

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + overlay `ui-automation-testing`
- Владелец: AppAutomation
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до подтверждения спеки менять только этот файл; сохранить существующий стиль fluent API и recorder pipeline; не ломать существующие `WaitUntil*` и recorder hotkeys
- Связанные ссылки: `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Recorder.Avalonia/*`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/*`

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Добавить fluent wait-метод для ожидания появления элемента, который не падает сразу, если control/property resolve сначала возвращает "not found", и добавить capture этого ожидания в Avalonia recorder через отдельную hotkey.

Outcome contract:
- Success means:
  - В `AppAutomation.Abstractions` есть публичный `WaitUntilExists<TSelf, TControl>(Expression<Func<TSelf, TControl>> selector, int timeoutMs = 5000) where TControl : class`.
  - Метод повторяет resolve selector до timeout и возвращает page, если элемент появился.
  - Если элемент не появился, выбрасывается `UiOperationException` с failure context, locator/property name и last observed вроде `<not found: ...>`.
  - Recorder умеет создавать `RecordedActionKind.WaitUntilExists` отдельной командой/hotkey и генерирует `Page.WaitUntilExists(static page => page.Control);`.
  - Recorder session-level path проверен целиком: hotkey/command -> `HandleRecorderCommand` -> `CaptureAssertion(Exists)` -> `StepJournal`/preview.
  - Новое поведение покрыто regression-тестами.
- Итоговый артефакт / output: изменения кода и тестов в границах таблицы файлов, плюс отчёт о targeted/full проверках.
- Stop rules:
  - На SPEC остановиться после готового spec quality gate и запроса подтверждения.
  - На EXEC остановиться после реализации, targeted tests, build/full test attempt и post-EXEC review.
  - Если `WaitUntilExitst` из запроса окажется требованием к точному typo-имени, это считается продуктовым API-решением; выбранное решение фиксирует корректное публичное имя `WaitUntilExists`.

## 2. Текущее состояние (AS-IS)
- `UiPageExtensions` содержит много `WaitUntil*`, но большинство методов сначала вызывают `Resolve(selector, page)` и только потом polling. Если control отсутствует в момент первого resolve, исключение возникает до ожидания.
- Private helper `WaitUntil` оборачивает timeout/read failures в `UiOperationException`, но не предназначен для "retry resolve after not found": исключение из condition сейчас завершает операцию.
- Recorder assertion pipeline:
  - `RecorderHotkeyMap` связывает hotkeys с `RecorderCommandKind`.
  - `RecorderSession.HandleRecorderCommand` переводит команды assert в `RecorderAssertionMode`.
  - `RecorderStepFactory.TryCreateAssertionStep` выбирает `RecorderAssertionCandidate`.
  - `RecordedActionKind` и `AuthoringCodeGenerator.GenerateStepStatement` определяют DSL-output.
  - `RecorderStepValidator` и `RecorderCommandRuntimeValidator` проверяют совместимость step.
- В тестах есть TUnit suites для abstractions и recorder, включая coverage matrix, которая требует, чтобы каждый `RecordedActionKind` рендерился и runtime-валидировался.

Скрытые зависимости и инварианты:
- Generated page properties используют `UiControlDefinition` и `Resolve<TControl>`, поэтому новый wait должен работать через существующий selector/page-property path.
- Failure diagnostics должны продолжать заполняться через `CreateUiOperationException`, включая artifacts collector.
- Значения enum нельзя перенумеровывать, чтобы не создавать лишний churn; новый `RecordedActionKind` нужно добавить в конец.

## 3. Проблема
Сейчас нельзя записать или написать стабильное ожидание появления поздно создаваемого UI элемента: page property throws "not found" до того, как wait успевает polling-ом дождаться появления.

## 4. Цели дизайна
- Разделение ответственности: low-level retry resolve остается в `UiPageExtensions`; recorder только генерирует новый DSL step.
- Повторное использование: использовать существующие diagnostics/failure-context helpers, `UiWaitOptions` и recorder validation/codegen patterns.
- Тестируемость: добавить unit/regression тест на initial resolve failure -> later success и recorder coverage.
- Консистентность: метод назвать `WaitUntilExists`, в одном стиле с `WaitUntilIsEnabled`, `WaitUntilNameEquals` и другими fluent methods.
- Обратная совместимость: существующие методы, enum значения и default hotkeys не менять; новая hotkey должна быть отдельной configurable option.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем семантику существующих `WaitUntilText*`, `WaitUntilIsEnabled`, `ClickButton` и action methods.
- Не добавляем generic retry-resolve во все существующие waits в этой задаче.
- Не меняем resolver contracts и platform adapters.
- Не добавляем visual/layout изменения overlay, кроме автоматического появления новой команды в существующей shortcut legend.
- Не добавляем misspelled public API `WaitUntilExitst`; запрос трактуется как очевидная опечатка имени `WaitUntilExists`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/AppAutomation.Abstractions/UiPageExtensions.cs` -> публичный `WaitUntilExists`, retry-resolve helper, diagnostics через существующий `CreateUiOperationException`.
- `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs` -> новый `RecordedActionKind.WaitUntilExists` и `RecorderAssertionMode.Exists`.
- `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` -> new `RecorderHotkeys.CaptureAssertExists`, default `Ctrl+Shift+F`.
- `src/AppAutomation.Recorder.Avalonia/RecorderHotkeyMap.cs` -> новая command kind, legend text `Assert Exists`.
- `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` -> route hotkey command to `CaptureAssertion(RecorderAssertionMode.Exists)`.
- `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs` -> exists assertion candidate для любого resolvable control через `ClassifyControlType(control)`.
- `src/AppAutomation.Recorder.Avalonia/RecorderStepValidator.cs` и `RecorderCommandRuntimeValidator.cs` -> новый step валиден для любого control без payload.
- `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs` -> render `Page.WaitUntilExists(static page => page.Property);`.
- Tests -> regression coverage для runtime wait и recorder/codegen/hotkey.

### 6.2 Детальный дизайн
- API:
  - `public static TSelf WaitUntilExists<TSelf, TControl>(this TSelf page, Expression<Func<TSelf, TControl>> selector, int timeoutMs = 5000) where TSelf : UiPage where TControl : class`
  - Метод возвращает `page` для fluent chaining.
  - Generic `TControl` нужен, потому что generated page properties для `UiControlType.GridRow/GridCell` имеют типы `IGridRowControl`/`IGridCellControl`, которые не наследуют `IUiControl`.
- Runtime algorithm:
  - Получить `startedAtUtc`, `timeout`, property name/definition через existing helpers.
  - На каждом poll вызвать compiled selector или `Resolve(selector, page)` equivalent.
  - Перехватывать и retry-ить только распознанные resolve failures со смыслом "элемент ещё не найден".
  - Type mismatch, bad selector, invalid cast, adapter/configuration errors и read failures, которые не являются not-found, fail fast через `UiOperationException` вместо ожидания timeout.
  - `IsRetryableResolveFailure(Exception ex)` contract:
    - Retryable только `InvalidOperationException`, если message содержит один из existing not-found markers: `Unknown control`, `not found`, `cannot be found`, `was not found`.
    - Non-retryable: type mismatch marker `not of expected type`, `InvalidCastException`, `ArgumentException`, `NotSupportedException`, adapter/configuration failures и read failures без not-found marker.
    - `OperationCanceledException` всегда пробрасывается.
    - Message matching считается bounded compatibility approach из-за отсутствия typed not-found exception в текущем resolver API; typed exception можно рассмотреть отдельным future API change.
  - Success: selector возвращает non-null `IUiControl`.
  - Timeout: бросить `UiOperationException` с expected `Exists=true` и last observed `<not found: message>` или resolved automation id.
  - OperationCanceledException не проглатывать.
- Recorder:
  - `RecorderAssertionMode.Exists` с отдельной command/hotkey.
  - Default hotkey `Ctrl+Shift+F`, потому что `Ctrl+Shift+E` уже занят `Assert Enabled`, `Ctrl+Shift+X` занят export.
  - Auto assertion поведение не менять: новая exists-команда доступна только явным hotkey/mode, чтобы не вытеснить text/enabled/checked heuristics.
- Output contract / evidence rules:
  - Codegen preview/source не должен содержать `Unsupported recorded action`.
  - Runtime validator не должен требовать `BoolValue`, `StringValue` или specific control type для exists step.
- Visual planning artifact для UI-facing изменений: `Не применимо` к layout, потому что меняется hotkey behavior и generated code, а не экранный layout. Fallback state description: overlay shortcut legend продолжает строиться тем же `BuildLegend()` и добавит item `Ctrl+Shift+F: Assert Exists` в существующую строку.
- UI test video evidence для UI automation задач: fallback. В репозитории найден TUnit/Avalonia recorder test suite, но не найден workflow записи безопасного video artifact для этих unit/integration tests. Next-best evidence: targeted TUnit tests для hotkey mapping/session capture/codegen и full test attempt.
- Границы сохранения поведения:
  - Existing hotkeys остаются прежними.
  - Existing recorder modes не меняют output.
  - Existing wait failure message style остается через `UiOperationException`.
- Обработка ошибок:
  - Timeout -> `UiOperationException`.
  - Bad selector/null args -> стандартные argument/selector exceptions по existing patterns.
  - Cancellation -> не перехватывать.
- Производительность:
  - Poll interval сохранить 100ms, как existing `WaitUntil` helper.
  - Resolve retry ограничен timeout; дополнительных фоновых задач нет.

## 7. Бизнес-правила / Алгоритмы (если есть)
| Состояние selector resolve | Действие |
| --- | --- |
| Возвращает non-null `IUiControl` | Success, вернуть page |
| Бросает retryable not-found resolve exception | Запомнить message как last observed и продолжить polling до timeout |
| Бросает type mismatch / bad selector / adapter configuration / non-not-found read exception | Fail fast через `UiOperationException` с исходной причиной |
| Не появился до timeout | `UiOperationException` с expected `Exists=true` |
| Бросает `OperationCanceledException` | Пробросить cancellation |

## 8. Точки интеграции и триггеры
- Тестовый код вызывает `Page.WaitUntilExists(static page => page.SomeControl)`.
- Recorder hotkey `CaptureAssertExists` вызывает `RecorderSession.HandleRecorderCommand`.
- `RecorderSession.CaptureAssertion(RecorderAssertionMode.Exists)` использует текущий hovered/focused control.
- `AuthoringCodeGenerator` генерирует method call при save/export/preview.

## 9. Изменения модели данных / состояния
- Новые persisted поля не добавляются.
- Enum расширяются новыми значениями в конце:
  - `RecordedActionKind.WaitUntilExists`
  - `RecorderAssertionMode.Exists`
  - internal `RecorderCommandKind.CaptureAssertExists`
- `RecorderHotkeys` получает новый init-only property `CaptureAssertExists`.

## 10. Миграция / Rollout / Rollback
- Поведение при первом запуске: новый default hotkey доступен через existing options default.
- Обратная совместимость: старые options initializers остаются валидными; старые enum numeric values не меняются.
- Rollback: удалить новый enum members/hotkey/method/tests; существующие сценарии unaffected, если новый API не использовался.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- `WaitUntilExists` возвращает page, если control уже существует.
- `WaitUntilExists` успешно дожидается control, если первые resolve попытки бросают "not found".
- `WaitUntilExists` на timeout бросает `UiOperationException` с `OperationName = "WaitUntilExists"`, property/locator context и last observed not found message.
- `WaitUntilExists` fail-fast выбрасывает `UiOperationException` до timeout для non-retryable resolve failures: wrong type, bad selector, adapter/configuration error.
- Recorder hotkey map распознаёт configurable exists hotkey и legend содержит `Assert Exists`.
- Recorder explicit exists assertion создаёт `RecordedActionKind.WaitUntilExists`.
- Recorder session-level path по exists-команде добавляет step в journal и preview содержит `Page.WaitUntilExists(...)`.
- Codegen рендерит `Page.WaitUntilExists(static page => page.Property);`.
- Coverage matrix обновлена: новый `RecordedActionKind` covered и валиден.

Какие тесты добавить/изменить:
- `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`
  - success when initially missing then appears
  - compile/runtime regression for generated-style `IGridCellControl` property
  - timeout diagnostics when still missing
  - non-retryable resolve failure fails fast and does not wait until timeout
- `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - hotkey map includes exists
  - `TryCreateAssertionStep(..., RecorderAssertionMode.Exists)` creates exists step
  - recorder session command/hotkey path captures exists step into `StepJournal`
- `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderFullCaptureCoverageTests.cs`
  - add exists step to all-actions matrix

Characterization tests / contract checks:
- Existing `WaitUntilIsEnabled_ThrowsUiOperationException_WithFailureContext` remains unchanged.
- Existing recorder hotkey tests still pass for old commands.

Visual acceptance для UI-facing изменений:
- Layout screenshot/video не применимы; acceptance проверяется string legend и generated DSL.

UI video evidence:
- Fallback: TUnit test evidence, потому что текущие recorder unit/integration tests не производят video artifacts.

Базовые замеры performance:
- Не применимо; изменение ограничено timeout-bound polling, уже используемым в wait helpers.

Команды для проверки:
- Targeted:
  - `dotnet test --project tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj -- --treenode-filter "/*/*/UiPageExtensionsTests/*"`
  - `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*"`
  - `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderFullCaptureCoverageTests/*"`
- Build:
  - `dotnet build AppAutomation.sln`
- Full:
  - `dotnet test --project AppAutomation.sln`

Stop rules для test/retrieval/tool/validation loops:
- Если targeted tests падают из-за implementation bug, исправить и повторить targeted.
- Если full tests не запускаются из-за локальной desktop/headless инфраструктуры, зафиксировать причину, targeted evidence и next-best command.
- Не расширять scope на все wait methods без отдельного решения.

## 12. Риски и edge cases
- Риск: exact typo `WaitUntilExitst` ожидался как имя API. Mitigation: spec явно выбирает корректное `WaitUntilExists`; подтверждение спеки подтверждает naming.
- Риск: compiled selector hides non-resolve exceptions. Mitigation: retry only recognized not-found resolve failures; fail fast for selector/type/adapter/read bugs; `OperationCanceledException` не проглатывать.
- Риск: not-found detection depends on resolver exception messages. Mitigation: keep markers explicit and conservative, cover retryable/non-retryable cases with tests, do not introduce typed exception in this task.
- Риск: default hotkey collision у consumer app. Mitigation: hotkey configurable через `RecorderHotkeys.CaptureAssertExists`.
- Риск: exists assertion in `Auto` could reduce richer assertions. Mitigation: not adding exists to Auto, only explicit mode.
- Риск: enum coverage test fails if not updated. Mitigation: update coverage matrix in same change.

## 13. План выполнения
1. Добавить failing/regression tests for `WaitUntilExists` initial missing -> success, timeout diagnostics и non-not-found fail-fast behavior.
2. Реализовать `WaitUntilExists` и helper для retrying resolve в `UiPageExtensions`.
3. Добавить recorder model/hotkey/session/factory/codegen/validator support.
4. Обновить recorder tests, включая session-level exists command path, and coverage matrix.
5. Запустить targeted tests, затем `dotnet build AppAutomation.sln`, затем full test attempt.
6. Выполнить post-EXEC review и исправить однозначные findings.

## 14. Открытые вопросы
Нет блокирующих вопросов. Naming decision: использовать корректное `WaitUntilExists`, а не typo `WaitUntilExitst`.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`; context `testing-dotnet`
- Выполненные требования профиля:
  - UI-thread blocking не добавляется: sync polling уже соответствует текущему style wait helpers; timeout bounded.
  - Изменение UI automation behavior покрывается TUnit tests.
  - Stable selectors сохраняются через existing page selectors/definitions.
  - Visual planning artifact: `Не применимо` к layout, fallback описан.
  - UI video evidence: fallback с причиной и next-best evidence описан.
  - Перед завершением EXEC запланированы targeted tests, build и full test attempt.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` | `WaitUntilExists` + retry resolve helper | Новый wait API с diagnostics |
| `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` | Regression tests for exists wait | Подтвердить initial missing и timeout |
| `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs` | New action/mode enum values | Recorder model support |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` | `CaptureAssertExists` default hotkey | Configurable separate hotkey |
| `src/AppAutomation.Recorder.Avalonia/RecorderHotkeyMap.cs` | Command mapping + legend text | Hotkey support |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | Route exists command to capture mode | Session integration |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs` | Exists assertion candidate | Capture step creation |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepValidator.cs` | Validate exists for any source | Recorder validation |
| `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs` | Runtime validation for exists | Runtime target compatibility |
| `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs` | Render `WaitUntilExists` | Generated scenario support |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | Hotkey/assertion/session-path tests | Recorder regression coverage |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderFullCaptureCoverageTests.cs` | Add action to coverage matrix | Keep all-actions contract green |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Fluent wait API | Нет wait, который переживает initial not found | `WaitUntilExists` retries resolve until appearance |
| Recorder assertions | Auto/Text/Enabled/Checked | Auto/Text/Enabled/Checked/Exists |
| Hotkeys | No exists assertion hotkey | Configurable `CaptureAssertExists`, default `Ctrl+Shift+F` |
| Codegen | No exists step render | `Page.WaitUntilExists(static page => page.Property);` |

## 18. Альтернативы и компромиссы
- Вариант: добавить typo-name `WaitUntilExitst`.
  - Плюсы: буквально совпадает с текстом запроса.
  - Минусы: закрепляет ошибку в public API.
  - Почему не выбран: existing API naming чистый и semantic; spec фиксирует corrected name.
- Вариант: изменить все существующие `WaitUntil*`, чтобы они retry-resolve.
  - Плюсы: шире покрывает late-created controls.
  - Минусы: меняет семантику многих existing methods и увеличивает blast radius.
  - Почему не выбран: запрос точечно про exists wait.
- Вариант: включить exists в Auto assertion.
  - Плюсы: проще discoverability.
  - Минусы: Auto начнёт создавать менее информативные assertions вместо text/enabled/checked.
  - Почему не выбран: отдельная hotkey/mode лучше соответствует запросу.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, integration points, error handling, compatibility и rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Границы, риски, rollback и staged plan указаны. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, tests и команды проверки перечислены. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, alternatives и review заполнены; блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client`, `ui-automation-testing`, `testing-dotnet` отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Цель и Non-Goals конкретны, typo naming decision явно зафиксирован. |
| 2. Понимание текущего состояния | 5 | Описаны текущие wait helpers и recorder pipeline. |
| 3. Конкретность целевого дизайна | 5 | API, hotkey, codegen, validation и tests заданы по файлам. |
| 4. Безопасность (миграция, откат) | 5 | Enum append-only, old hotkeys untouched, rollback описан. |
| 5. Тестируемость | 5 | Acceptance criteria и targeted/full команды указаны. |
| 6. Готовность к автономной реализации | 5 | План и границы достаточны, блокирующих вопросов нет. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-05-20-wait-until-exists-recorder-hotkey.md`; instruction stack: `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `spec-linter`, `spec-rubric`, `review-loops`; selected profile: `dotnet-desktop-client` + `ui-automation-testing`; open questions: none; planned changed files listed in section 16.
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: reviewed current wait code, recorder hotkey/session/factory/codegen/validator locations and test projects.
  - Contract pass: spec includes outcome, Non-Goals, acceptance criteria, validation commands, visual/video fallback, and exact stop rules.
  - Adversarial risk pass: checked likely pitfalls: typo API name, Auto assertion degradation, enum churn, hotkey collision, hidden layout/video requirement, over-broad exception retry, missing recorder session-level coverage.
  - Re-review after fixes / Fix and re-review: narrowed retry contract to not-found failures and added recorder session-level test/evidence requirements; rechecked acceptance criteria, test plan and file table.
  - Stop decision: PASS; ready for user approval before EXEC.
- Evidence inspected:
  - `src/AppAutomation.Abstractions/UiPageExtensions.cs`
  - `src/AppAutomation.Abstractions/UiWait.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderHotkeyMap.cs`
  - `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderStepValidator.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs`
  - `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`
  - `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderFullCaptureCoverageTests.cs`
- Depth checklist:
  - Scope drift / unrelated changes: scope ограничен wait exists + recorder capture; `git status --short` был пуст до создания спеки.
  - Acceptance criteria: concrete and testable.
  - Validation evidence: commands planned; no EXEC validation yet.
  - Unsupported claims: no current behavior claims without inspected files.
  - Regression / edge case: initial missing, timeout, hotkey collision and Auto behavior covered.
  - Comments/docs/changelog: no docs/changelog planned; public API covered by tests.
  - Hidden contract change: new public API and hotkey option explicit; old contracts unchanged.
  - Manual-review challenge: reviewer likely asks why not typo-name and why not Auto; both answered in Non-Goals/Alternatives.
- No-findings justification: spec contains concrete file-level design, tests, fallbacks, and no blocking open questions.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | tests | Test plan covered hotkey map and factory but not full recorder session command path. | Add session-level acceptance/test for exists command producing `StepJournal` preview. | fixed |
| MEDIUM | correctness | Retry contract for `WaitUntilExists` was too broad and could hide bad selectors/type/adapter failures until timeout. | Retry only recognized not-found resolve failures; fail fast for non-not-found bugs. | fixed |
| MEDIUM | tests | Fail-fast behavior was in the plan but not in acceptance criteria or detailed test list. | Add explicit fail-fast acceptance criterion and `UiPageExtensionsTests` case. | fixed |
| MEDIUM | design | Retryable not-found detection was under-defined and could become ad hoc message matching. | Define conservative `IsRetryableResolveFailure` markers and non-retryable exception classes. | fixed |
| LOW | risk | Exact typo `WaitUntilExitst` could be expected by external caller. | Treat approval of this spec as approval of corrected API name `WaitUntilExists`; adjust only if user rejects naming. | accepted-risk |

- Fixed before continuing: Added recorder session-level test requirement; narrowed retry/fail-fast semantics; added fail-fast acceptance/test; defined conservative `IsRetryableResolveFailure` marker contract.
- Checks rerun: SPEC linter/rubric self-check remains PASS after targeted spec edits; post-SPEC review rechecked affected design/acceptance/test/risk sections.
- Needs human: Требуется фраза `Спеку подтверждаю` для перехода в EXEC
- Residual risks / follow-ups: Full test run may be environment-dependent because solution contains desktop/headless tests.

### Post-EXEC Review
- Статус: PASS для изменений задачи; есть residual full-suite failure вне изменённого scope
- Scope reviewed: утверждённая spec, `git status --short`, `git diff --stat`, relevant diffs по runtime API/recorder/tests, targeted/build/full validation evidence, docs/changelog impact.
- Decision: можно финализировать результат с явной пометкой full-suite residual
- Review passes:
  - Scope/Evidence pass: PASS. Изменения ограничены `WaitUntilExists`, recorder exists assertion/hotkey/codegen/validation и соответствующими тестами; unrelated source files не изменялись.
  - Contract pass: PASS. Acceptance criteria покрыты: late resolve retry, timeout diagnostics, fail-fast non-retryable path, explicit recorder exists mode/hotkey, codegen и coverage matrix.
  - Adversarial risk pass: PASS. Проверены основные риски: typo-name не добавлен, old hotkeys не изменены, `Auto` assertion не деградирует, retry не проглатывает type mismatch, runtime validator не требует лишний payload.
  - Re-review after fixes / Fix and re-review: PASS. После implementation targeted suites/build прогнаны; full suite residual классифицирован отдельно от фичи.
  - Stop decision: PASS. Дальнейшие изменения не требуются в рамках спеки.
- Evidence inspected:
  - `src/AppAutomation.Abstractions/UiPageExtensions.cs`
  - `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs`
  - `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderHotkeyMap.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderStepValidator.cs`
  - `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderFullCaptureCoverageTests.cs`
  - `specs/2026-05-20-wait-until-exists-recorder-hotkey.md`
- Depth checklist:
  - Scope drift / unrelated changes: PASS. `git status --short` показывает только ожидаемые source/test файлы и spec.
  - Acceptance criteria: PASS. Все criteria из секции 11 имеют direct code/test coverage.
  - Validation evidence: PASS с residual. Targeted tests и build зелёные; full solution после Release sample prerequisite прошёл 273/274 и упал в unrelated FlaUI sample scenario.
  - Unsupported claims: PASS. Claims основаны на diff review и tool outputs.
  - Regression / edge case: PASS. Есть tests на initial missing -> success, timeout diagnostics и non-retryable fail-fast.
  - Comments/docs/changelog: PASS. Документация/changelog не требуются; spec обновлена как task artifact.
  - Hidden contract change: PASS. Новые public API/options/enum values append-only; old behavior untouched.
  - Manual-review challenge: PASS. Вероятные вопросы по message matching и corrected naming отражены в spec; implementation следует выбранному contract.
- No-findings justification: review не выявил blocking defects в изменённых runtime/recorder paths; residual full-suite failure находится в existing FlaUI sample scenario `Hierarchy_SelectTreeItem_ShowsSelectionInResult`, не связанном с новым exists wait или recorder command path.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | `dotnet test --project AppAutomation.sln` после подготовки Release sample executable завершился 273/274: упал `Hierarchy_SelectTreeItem_ShowsSelectionInResult` на timeout `WaitUntilHasItemsAtLeast` для `HierarchySelectionList`. | Зафиксировать как residual full-suite failure вне scope; не менять FlaUI sample в этой задаче. | accepted-risk |

- Fixed before final report: blocking implementation findings отсутствуют.
- Checks rerun:
  - `dotnet test --project tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj -- --treenode-filter "/*/*/UiPageExtensionsTests/*"` -> PASS 33/33 after review fix.
  - `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*"` -> PASS 65/65.
  - `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderFullCaptureCoverageTests/*"` -> PASS 5/5.
  - `dotnet build AppAutomation.sln` -> PASS, 0 errors, existing analyzer/NU1903 warnings.
  - `dotnet build sample/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj -c Release` -> PASS, 0 errors, existing analyzer/NU1903 warnings.
  - `dotnet test --project AppAutomation.sln` -> first sandbox run failed on NuGet `NU1301` TLS/credentials; escalated run before Release sample prerequisite failed in `sample/DotnetDebug.Tests` because Release executable was absent; final escalated run after prerequisite: 273/274 PASS, 1 unrelated FlaUI sample failure.
  - `git diff --check` -> PASS, only line-ending normalization warnings.
- Validation evidence: Targeted green and build green; full solution residual documented above.
- Unrelated changes: None detected in tracked files outside expected scope. Generated SDK/tooling artifacts from local setup are ignored/not present in `git status --short`.
- Needs human: Нет.
- Residual risks / follow-ups: investigate existing `Hierarchy_SelectTreeItem_ShowsSelectionInResult` FlaUI timing/selection issue separately if full-suite green is required.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Контекст и маршрутизация | 0.95 | Нет | Создать рабочую spec | Нет | Нет | Прочитаны central instructions, выбран delivery-task stack | `C:\Users\Kibnet\.codex\agents\AGENTS.md`, central instruction docs |
| SPEC | Анализ кода | 0.9 | Нет | Зафиксировать TO-BE и проверки | Нет | Нет | Найдены текущие wait helpers, recorder command pipeline и relevant tests | `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Recorder.Avalonia/*`, `tests/*` |
| SPEC | Quality gate | 0.9 | Пользовательское подтверждение | Запросить `Спеку подтверждаю` | Да | Да, ожидается решение пользователя | SPEC linter, rubric и post-SPEC review заполнены; код менять нельзя до approval | `specs/2026-05-20-wait-until-exists-recorder-hotkey.md` |
| SPEC | Review fixes | 0.95 | Пользовательское подтверждение | Запросить `Спеку подтверждаю` | Да | Да, пользователь согласовал правки review | Сужен retry-контракт до not-found failures и добавлена session-level проверка recorder command path | `specs/2026-05-20-wait-until-exists-recorder-hotkey.md` |
| SPEC | Review fixes | 0.95 | Пользовательское подтверждение | Запросить `Спеку подтверждаю` | Да | Да, пользователь согласовал правки review | Добавлены fail-fast acceptance/test и явный `IsRetryableResolveFailure` contract | `specs/2026-05-20-wait-until-exists-recorder-hotkey.md` |
| EXEC | Approval received | 0.95 | Нет | Добавить regression tests | Нет | Да, пользователь подтвердил спеку | Фраза подтверждения получена; можно менять код в рамках утверждённой спеки | `specs/2026-05-20-wait-until-exists-recorder-hotkey.md` |
| EXEC | Regression tests | 0.9 | Нет | Реализовать API и recorder поддержку | Нет | Нет | Добавлены tests для exists wait, fail-fast, hotkey/factory/session path и coverage matrix; красная фаза подтверждена compile error на отсутствующем API | `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderFullCaptureCoverageTests.cs` |
| EXEC | Implementation and targeted checks | 0.9 | Нет | Запустить build и full test attempt | Нет | Нет | Реализованы runtime API и recorder pipeline; targeted suites passed: `UiPageExtensionsTests` 32/32, `RecorderTests` 65/65, `RecorderFullCaptureCoverageTests` 5/5 | `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Recorder.Avalonia/*`, `tests/*`, `specs/2026-05-20-wait-until-exists-recorder-hotkey.md` |
| EXEC | Build and full validation | 0.85 | Полная suite имеет unrelated FlaUI residual | Выполнить post-EXEC review | Нет | Нет | Solution build зелёный; full test после Release sample prerequisite прошёл 273/274 и упал только в existing FlaUI sample hierarchy scenario, вне scope фичи | `AppAutomation.sln`, `sample/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests` |
| EXEC | Post-EXEC review | 0.9 | Нет | Финальный отчёт | Нет | Нет | Дифф и evidence проверены; blocking findings по `WaitUntilExists`/recorder support не найдено; residual full-suite failure задокументирован | `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Recorder.Avalonia/*`, `tests/*`, `specs/2026-05-20-wait-until-exists-recorder-hotkey.md` |
| EXEC | Review fix | 0.95 | Нет | Финальный отчёт | Нет | Да, пользователь попросил исправить finding | `WaitUntilExists` сделан generic по `TControl : class`, добавлен regression test на generated-style `IGridCellControl`; targeted abstractions 33/33 и solution build PASS | `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `specs/2026-05-20-wait-until-exists-recorder-hotkey.md` |
