# Recorder Button Text Assertions

## 0. Метаданные
- Тип (профиль): delivery-task; profiles: `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до подтверждения спеки менять только этот файл; реализация должна сохранить кликабельность кнопок в generated authoring page
- Связанные ссылки: `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`, `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs`

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Исправить recorder bug: при записи assertion рекордер должен сохранять реальный тип компонента независимо от операции. Сейчас текстовый assertion на `Button` генерирует control descriptor типа `Label`, после чего тот же control в page object больше нельзя использовать как кнопку для `ClickButton`.

Outcome contract:
- Success means: recorder descriptor reflects the actual control type first; текстовое assertion, записанное с кнопки, сохраняет `UiControlType.Button`; checkbox/radio/toggle сохраняют свои реальные типы; generated scenario не подменяет тип ради компиляции конкретной операции.
- Итоговый артефакт / output: код фикса, regression tests, successful targeted/full validation или явное объяснение недоступной проверки.
- Stop rules: остановиться после passing targeted tests + build/full tests либо при объективном blocker; не расширять задачу на unrelated recorder flows.

## 2. Текущее состояние (AS-IS)
- `RecorderStepFactory.TextAssertionExtractor` использует `ClassifyTextAssertionType`.
- `ClassifyTextAssertionType` возвращает `UiControlType.Label` для `TextBlock`, `Label` и `Button`.
- `ExtractTextValue` уже умеет читать `Button.Content`.
- `AuthoringCodeGenerator` переиспользует control по ключу locator kind/value, а тип property берет из первого generated descriptor или existing page descriptor.
- Если первым для locator кнопки записан text assertion, в generated page появляется `ILabelControl`; следующий `ClickButton` по этой же property не компилируется/не проигрывается как кнопка.
- В `AppAutomation.Abstractions` `WaitUntilTextEquals/Contains` есть для `ILabelControl` и `ITextBoxControl`, но не для `IButtonControl`.
- Runtime adapters must expose visible text through an explicit readable-text contract for typed controls; generic text assertions must not use locator/display `Name` as a hidden fallback.
- `CheckBox`, `RadioButton` and `ToggleButton` are button-derived controls; recorder must still preserve their specific DSL control types when recording assertions.

## 3. Проблема
Одна корневая проблема: recorder derives control type from the requested operation instead of the captured component identity, so readable actionable controls can be saved as read-only labels and lose later actions.

## 4. Цели дизайна
- Разделение ответственности: recorder классифицирует actual source control; abstractions provide provider-neutral text/name wait where operations need it; codegen keeps generated controls type-compatible with captured components, not with a single recorded action.
- Повторное использование: использовать существующие `WaitUntilText` helper overload patterns.
- Тестируемость: добавить regression tests на factory/codegen и abstraction-level button text wait.
- Консистентность: real controls keep their real DSL type for actions and assertions; `Label` используется только для actual read-only labels/text blocks, not as a compilation workaround.
- Обратная совместимость: no source-breaking interface changes; existing button click API remains unchanged.

## 5. Non-Goals (чего НЕ делаем)
- Не менять locator resolution, aliasing или naming strategy.
- Не менять generated source generator mapping для `UiControlType.Button`.
- Не добавлять новый `RecordedActionKind`.
- Не менять semantics label/textbox assertions.
- Не подменять реальные типы controls ради поддержки конкретного метода. Если operation/type pair не поддержан, это должно быть явным API/runtime gap, а не поводом записывать неверный `UiControlType`.
- Не добавлять app-specific hints для конкретного consumer UI.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs` -> text assertion classifier должен возвращать actual DSL type from captured control identity: `TextBox`, `Label`, `Button`, `CheckBox`, `RadioButton`, `ToggleButton` и т.д. по existing `ClassifyControlType`, а не operation-compatible surrogate type.
- `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs` -> при совпадении locator, но несовместимом `UiControlType`, не переиспользовать stale existing property; генерировать отдельную typed property с уникальным именем и diagnostic.
- `src/AppAutomation.Abstractions/UiPageExtensions.cs` -> добавить generic `WaitUntilTextEquals/Contains` overloads для `Expression<Func<TSelf, IUiControl>>`, читающие explicit visible text contract (`IReadableTextControl`) для typed controls без специализированного text contract.
- Runtime adapters -> controls with visible text expose it through specialized contracts or `IReadableTextControl`; unsupported typed controls fail explicitly instead of falling back to `Name`.
- `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` -> regression: regular button text assertion remains `UiControlType.Button`, checkbox/radio/toggle text assertions keep their specific types, generated scenario can also click same button, stale existing `Label` descriptor does not block generated `Button` property.
- `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` -> regression: generic `WaitUntilTextEquals` works for typed `IUiControl` descendants with visible text and does not pass when only `Name` matches.

### 6.2 Детальный дизайн
- Поток данных:
  1. Recorder captures text assertion from an Avalonia control.
  2. Recorder extracts display text from the source (`TextBox.Text`, `TextBlock.Text`, `Label.Content`, `Button.Content`, or fallback where already supported).
  3. Candidate control type is the actual DSL type from `ClassifyControlType`, not a label surrogate.
  4. Generated page property type matches the actual control: `IButtonControl` for `Button`, `ICheckBoxControl` for `CheckBox`, `IRadioButtonControl` for `RadioButton`, `IToggleButtonControl` for `ToggleButton`.
  5. Generated scenario compiles for generic text waits through `IUiControl` overloads and reads visible text through specialized visible-text contracts or explicit readable-text support; if a future control lacks this support, that is an explicit API/runtime gap.
  6. Later action uses the same correctly typed property, for example `ClickButton` for `IButtonControl`.
- Контракты / API: additive extension methods only; control interfaces remain unchanged.
- Output contract / evidence rules: include tests that prove descriptor type is actual component type, generated page attribute, generated scenario statements, stale descriptor mismatch handling, and generic text wait behavior.
- Visual planning artifact для UI-facing изменений: `Не применимо`; изменение не меняет layout, visual state или navigation, only recorder-generated DSL typing. Fallback flow: `Button(Content="Run", AutomationId="RunButton")` -> record text assertion -> generated `UiControlType.Button` -> `WaitUntilTextEquals` -> `ClickButton`.
- UI test video evidence для UI automation задач: fallback. Existing relevant coverage is in-memory recorder/unit coverage; no window/video runner is needed to prove this code path, and no visual acceptance state changes.
- Границы сохранения поведения: existing `TextBox`, `Label`, `TextBlock`, checkbox/radio/toggle button click behavior remains unchanged; readable actionable controls no longer generate `Label` unless the actual control is a label-like control.
- Обработка ошибок: if button text is empty, extractor continues returning unsupported as today for empty text.
- Производительность: no material impact; only type switch and property read.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Text assertion classification:
  - `TextBox` -> `UiControlType.TextBox`
  - `TextBlock` / `Label` -> `UiControlType.Label`
  - `Button` -> `UiControlType.Button`
  - `CheckBox` -> `UiControlType.CheckBox`
  - `RadioButton` -> `UiControlType.RadioButton`
  - `ToggleButton` -> `UiControlType.ToggleButton`
  - otherwise unsupported
- Button text read:
  - Recorder capture from Avalonia control: `Button.Content?.ToString()`
- Runtime playback assertion:
  - specialized text controls use existing specialized overloads.
  - other typed controls use generic `IUiControl` overloads backed by `IReadableTextControl.Text`.
  - value-only controls (`DateTimePicker`, `Calendar`, `DateRangeFilter`, `NumericRangeFilter`, `Spinner`, `Slider`, `ProgressBar`) are not readable text sources for `WaitUntilText...`; text assertions must fail explicitly instead of synthesizing date/number/progress strings.
- Existing descriptor reuse:
  - same locator + same control type -> reuse existing property
  - same locator + incompatible control type -> generate a new unique property for the requested type and emit a diagnostic instead of silently reusing a stale property
- Invariant: recorder never changes `UiControlType` only to make a recorded operation compile.

## 8. Точки интеграции и триггеры
- `RecorderSession.CaptureAssertion(RecorderAssertionMode.Text|Auto)` triggers `TryCreateAssertionStep`.
- `AuthoringCodeGenerator.SaveAsync` and `GeneratePreview` render `WaitUntilTextEquals`.
- Generated source generator keeps `UiControlType.Button` -> `IButtonControl`.
- Runtime adapters expose visible text through specialized contracts or `IReadableTextControl`; generic text assertion does not use inherited `Name`.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- `RecordedStep` shape не меняется.
- Public interfaces do not change; no migration.

## 10. Миграция / Rollout / Rollback
- При первом запуске нет migration.
- Existing source implementations of `IButtonControl` do not need contract updates.
- Rollback: вернуть old text assertion classifier behavior, удалить generic text wait overloads, stale descriptor mismatch handling and tests.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Text assertion from actual `Button` creates `RecordedStep` with `Control.ControlType == UiControlType.Button` and `StringValue` from button content.
  - Text assertion from actual `CheckBox`, `RadioButton`, and `ToggleButton` keeps `UiControlType.CheckBox`, `UiControlType.RadioButton`, and `UiControlType.ToggleButton` respectively.
  - Generated page attribute for each locator uses the actual control type, not an operation surrogate.
  - Generated scenario can include both `Page.WaitUntilTextEquals(static page => page.RunButton, "Run");` and `Page.ClickButton(static page => page.RunButton);`.
- `UiPageExtensions.WaitUntilTextEquals` works with typed controls through a generic `IUiControl` overload by reading `IReadableTextControl.Text` when no specialized text overload exists.
- Generic typed-control text assertion does not pass when only `Name` matches and no readable visible text contract exists.
  - Generic typed-control text assertion does not pass by formatting model values from value-only controls such as date range filters.
  - Runtime validator rejects `WaitUntilText...` on value-only controls instead of marking them supported.
  - Existing stale `Label` descriptor for the same locator does not force a later `ClickButton`/button assertion to reuse an incompatible label property; generator emits a typed button property with a unique name and diagnostic.
  - `CheckBox`, `RadioButton`, and `ToggleButton` are not classified as plain `Button` or `Label` by this fix.
  - Existing recorder text assertion behavior for labels/text boxes remains covered.
- Какие тесты добавить/изменить:
  - Add recorder regression test in `RecorderTests.cs`.
  - Add generator regression for stale existing label descriptor on the same button locator.
  - Add abstractions regression test in `UiPageExtensionsTests.cs`.
  - Add mandatory classifier guard tests for `CheckBox`, `RadioButton`, and `ToggleButton`.
- Characterization tests / contract checks для текущего поведения: first targeted test should fail before implementation because button text assertion currently returns `UiControlType.Label`.
- Visual acceptance для UI-facing изменений: `Не применимо`; no layout/render change.
- UI video evidence для UI-facing фич/багфиксов: fallback evidence is deterministic targeted tests; no visual flow or video harness is involved.
- Базовые замеры до/после для performance tradeoff: `Не применимо`; no performance-sensitive path.
- Команды для проверки:
  - `dotnet test tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*"`
  - `dotnet test tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj -- --treenode-filter "/*/*/UiPageExtensionsTests/*"`
  - `dotnet build AppAutomation.sln`
  - `dotnet test AppAutomation.sln`
- Stop rules для test/retrieval/tool/validation loops: if targeted regression and affected abstraction tests pass, run build/full tests once; only rerun targeted checks after touched-surface fixes.

## 12. Риски и edge cases
- Runtime visible text may differ from locator/display `Name`; generic text assertions must rely on explicit readable text support. If a specific type cannot expose visible text yet, add real adapter/API support later instead of changing recorder type or falling back to `Name`.
- `ToggleButton`, `CheckBox`, and `RadioButton` inherit from button-like controls; classification must preserve their specific control types.
- Existing page object with same locator already declared as `Label` can be present after a previous bad recorder run; codegen must avoid incompatible reuse and emit a diagnostic/new typed property.
- Generated duplicate typed properties with the same locator are acceptable only when their `UiControlType` differs; property names must remain unique.

## 13. План выполнения
1. Add failing/characterization recorder test for actual button text assertion and generated click replay surface.
2. Add mandatory classifier guard tests for `CheckBox`, `RadioButton`, and `ToggleButton`.
3. Add generator regression for stale existing `Label` property on the same locator.
4. Add abstraction test for generic `WaitUntilTextEquals` on `IUiControl` using readable visible text, plus a guard proving `Name` alone is not used as text.
5. Change recorder text assertion classification to preserve actual DSL control type.
6. Add codegen incompatible-type reuse guard and diagnostic.
7. Run targeted tests, then build/full tests.
8. Execute post-EXEC review loop and fix any findings inside approved scope.

## 14. Открытые вопросы
Нет блокирующих вопросов. Public interface contract is intentionally unchanged.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля: сохраняются stable selectors/automation ids; UI automation behavior covered by existing recorder/unit suite; before implementation planned targeted regression; validation commands include `dotnet build` and `dotnet test`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs` | Text assertions classify controls by actual DSL type (`Button`, `CheckBox`, `RadioButton`, `ToggleButton`, etc.) | Preserve component identity instead of using operation-compatible surrogates |
| `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs` | Avoid reusing same-locator existing controls when control type is incompatible; generate unique typed property and diagnostic | Prevent stale generated `Label` descriptor from breaking later button actions |
| `src/AppAutomation.Abstractions/UiControlContracts.cs` | Add additive `IReadableTextControl` for controls that expose visible text | Avoid using locator/display `Name` as text while keeping existing typed interfaces source-compatible |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` | Add generic `IUiControl` text wait overloads that read specialized visible-text contracts or `IReadableTextControl.Text`; remove synthetic date/number/progress/range formatting from `WaitUntilText...` | Generated `WaitUntilTextEquals` compiles for correctly typed controls and checks visible text only |
| `src/AppAutomation.Abstractions/UiControlAdapters.cs` | Implement `IReadableTextControl` for composite controls with real visible text (`SearchPicker`, `Dialog`, `Notification`, `FolderExport`, `ShellNavigation`) | Let generic text assertions read composite visible text without treating value-only filters as text |
| `src/AppAutomation.Avalonia.Headless/Internal/AutomationModel/AutomationElements.cs` | Expose button-like `Content` and tab header as `Text` in the headless automation model | Runtime generic text assertions can read actual Avalonia visible content |
| `src/AppAutomation.Avalonia.Headless/Automation/HeadlessControlResolver.cs` | Implement visible text support for button-like, combo, tab, tree and generic headless wrappers; reuse visible text reader for grid cells | Provide explicit visible text support for typed controls |
| `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs` | Implement visible text support for button-like, list/combo, tab, tree and generic FlaUI wrappers using UIA visible text sources without automation-id fallback | Provide explicit visible text support for typed controls |
| `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs` | Allow text assertion validation for readable control types and reject value-only controls for `WaitUntilText...` | Keep runtime validation aligned with visible-text support |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | Add regression tests for actual-type classification and stale descriptor handling | Prevent recorder from converting actionable controls to label or widening derived controls |
| `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` | Add generic typed-control text wait test | Cover additive extension-method contract |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Button text assertion descriptor | `UiControlType.Label` | `UiControlType.Button` |
| Check/radio/toggle text assertion descriptor | incidental button/label risk | actual `CheckBox` / `RadioButton` / `ToggleButton` type |
| Generated property type | `ILabelControl` for first text assertion | `IButtonControl` |
| Generated scenario | Text assertion can block later click | Text assertion and click can share the same button property |
| Runtime text read | Labels/textboxes only | Specialized visible-text contracts plus generic typed controls via `IReadableTextControl.Text` |
| Value-only controls in text waits | Date/number/progress/range values could be formatted as pseudo-text | No synthetic text; unsupported visible-text controls fail explicitly |
| Existing stale same-locator descriptor | Silently reused even if type incompatible | Not reused for incompatible action/control type; unique typed property generated |

## 18. Альтернативы и компромиссы
- Вариант: special-case code generator to render `WaitUntilNameEquals` for button descriptors.
  - Плюсы: no new `IButtonControl` member.
  - Минусы: changes semantics from text to name and relies on name matching; generated preview differs by control type for same action.
  - Почему выбранное решение лучше в контексте этой задачи: selected solution keeps recorded action `WaitUntilTextEquals` and preserves real control type; implementation reads visible text through explicit adapter support.
- Вариант: use `IUiControl.Name` as text fallback.
  - Плюсы: minimal implementation.
  - Минусы: checks locator/automation name, not necessarily visible text; violates `WaitUntilText...` semantics.
  - Почему не выбран: user expectation is visible text; unsupported text reading must be fixed explicitly, not hidden by `Name`.
- Вариант: add `Text` to `IButtonControl`.
  - Плюсы: explicit button text contract.
  - Минусы: source-breaking for external implementers of a public interface.
  - Почему выбранное решение лучше: additive `IReadableTextControl` avoids source-breaking existing typed interfaces while keeping `WaitUntilText...` tied to visible text.
- Вариант: keep button assertion as label and generate duplicate button property for click.
  - Плюсы: no abstraction extension overload.
  - Минусы: duplicate locator/properties in normal path, ambiguous authoring surface, still misclassifies source control.
  - Почему выбранное решение лучше: preserving actual source control type fixes the origin; duplicate property is reserved only for stale incompatible existing descriptors.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals и Non-Goals описаны |
| B. Качество дизайна | 6-10 | PASS | Ответственность, API, integration, rollout и compatibility risks описаны |
| C. Безопасность изменений | 11-13 | PASS | Есть acceptance criteria, risk list, scoped execution plan |
| D. Проверяемость | 14-16 | PASS | Указаны regression tests и команды targeted/full validation |
| E. Готовность к автономной реализации | 17-19 | PASS | Открытых блокеров нет; alternatives and file map documented |
| F. Соответствие профилю | 20 | PASS | .NET desktop/UI automation requirements учтены; video fallback объяснен |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Bug and non-goals are narrowly scoped |
| 2. Понимание текущего состояния | 5 | Identified exact classifier/generator/API interaction |
| 3. Конкретность целевого дизайна | 5 | Files and contracts are explicit |
| 4. Безопасность (миграция, откат) | 5 | Public interface break avoided; rollback is documented |
| 5. Тестируемость | 5 | Failing regression and validation commands are specified |
| 6. Готовность к автономной реализации | 5 | No blocking open questions; plan is ordered |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-05-20-recorder-button-text-assertions.md`; instruction stack: `model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`; selected profile: `dotnet-desktop-client` + `ui-automation-testing`; open questions: none; planned changed files listed in section 16
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: reviewed recorder factory classifier/extractor, session capture triggers, code generator reuse/type mismatch behavior, abstractions text wait overloads, readable visible text contract, related tests.
  - Contract pass: spec preserves actual control type before operation compatibility, avoids source-breaking interface changes, requires mandatory derived-control guard tests, includes regression tests and build/full-test commands.
  - Adversarial risk pass: checked source-breaking API risk, derived button controls, stale generated `Label` descriptors, runtime `Name` vs `Content` mismatch, and the risk of hiding unsupported methods by changing control type.
  - Re-review after fixes / Fix and re-review: fixed review findings by switching to generic visible-text overloads backed by `IReadableTextControl`, making actual-type classification the invariant, adding mandatory derived-control guard tests, and adding stale descriptor mismatch handling; reran spec linter/rubric self-check.
  - Stop decision: PASS; ready for human confirmation.
- Evidence inspected: `RecorderStepFactory.cs` methods `TryCreateAssertionStep`, `ClassifyTextAssertionType`, `ExtractTextValue`; `AuthoringCodeGenerator.cs` control reuse and `WaitUntilTextEquals` rendering; `UiPageExtensions.cs` text wait overloads; readable visible text contract; recorder/abstractions tests.
- Depth checklist:
  - Scope drift / unrelated changes: only spec file changed in SPEC phase.
  - Acceptance criteria: concrete and testable.
  - Validation evidence: commands planned; not run before EXEC.
  - Unsupported claims: existing typed interfaces are unchanged; runtime visible text is explicitly tied to specialized contracts or readable visible text support.
  - Regression / edge case: existing label/textbox behavior, actual types for button-derived controls, and stale same-locator label descriptors considered.
  - Comments/docs/changelog: no docs/changelog needed for small behavior fix unless build/test reveals public API docs requirement.
  - Hidden contract change: public interface change avoided; extension-method API addition is explicit.
  - Manual-review challenge: reviewer may question visible text extraction in adapters, unsupported operation/type pairs, and duplicate same-locator properties; spec documents truth-first typing, risks, tests and chosen rationale.
- No-findings justification: after review fixes, spec covers root cause, truth-first target design, visible text semantics, derived control guard, stale descriptor handling, tests and API compatibility without open blockers.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | classification | `Button -> UiControlType.Button` was too broad for `ToggleButton` family | Classify actual derived control types and add mandatory guard tests | fixed |
| HIGH | API | Proposed `IButtonControl.Text` was source-breaking for external implementers | Use additive `IReadableTextControl` text wait support; do not change existing typed interfaces | fixed |
| MEDIUM | codegen | Existing stale `Label` property with same locator could still break button action | Add codegen incompatible-type reuse guard and regression test | fixed |
| MEDIUM | evidence | Adapter text contract was under-specified | Tie generic typed-control text wait to explicit readable visible text support and test that `Name` alone is not accepted | fixed |
| MEDIUM | acceptance | Derived-control guard test was optional | Make `CheckBox`/`RadioButton`/`ToggleButton` classifier guard tests mandatory | fixed |
| MEDIUM | design | Type was still partially described around operation support | Make actual component type the invariant; unsupported methods become explicit API/runtime gaps | fixed |

- Fixed before continuing: actual-type classifier invariant, mandatory derived-control guard tests, no public interface change, stale descriptor mismatch handling, revised acceptance tests.
- Checks rerun: spec linter/rubric self-check completed after review edits.
- Needs human: approval phrase required
- Residual risks / follow-ups: custom controls that do not implement readable visible text support will fail explicitly until adapter/API support is added.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec; `git diff --stat`; relevant diff in `RecorderStepFactory.cs`, `AuthoringCodeGenerator.cs`, `UiPageExtensions.cs`, `UiControlContracts.cs`, `UiControlAdapters.cs`, headless/FlaUI adapters, runtime validator, `RecorderTests.cs`, `UiPageExtensionsTests.cs`; validation output.
- Decision: можно завершать с явным validation caveat по existing FlaUI window placement environment.
- Review passes:
  - Scope/Evidence pass: diff stays inside approved recorder/abstractions/adapter/test scope; no product UI layout/state flow changed; spec updated to include option 3 and actual validation evidence.
  - Contract pass: component type remains truth-first and independent of operation; existing typed public interfaces were not extended with breaking members; `IReadableTextControl` and generic `IUiControl` text waits are additive; `WaitUntilText...` reads visible text only through specialized text contracts or explicit readable-text support.
  - Adversarial risk pass: checked button-derived classification order, stale same-locator `Label` descriptor handling, `Name`-only false positives, synthetic date/range/number/progress text regressions, runtime validator whitelist, and unsupported value-only controls.
  - Re-review after fixes / Fix and re-review: after the P2 review, removed synthetic value branches, added value-only guard tests, aligned runtime validator, added `IReadableTextControl` to composite readable adapters, reran targeted/full affected tests and build.
  - Stop decision: PASS for code changes; full solution aggregate validation is not available because the sequential run timed out and the isolated FlaUI resolver suite fails before adapter assertions on an existing desktop window placement issue.
- Evidence inspected: final diff, `ReadVisibleText` switch, removed value-only formatting helpers, validator readable-type whitelist, adapter `IReadableTextControl` implementations, new regression tests, build/test output.
- Depth checklist:
  - Scope drift / unrelated changes: no intentional unrelated source changes; spec updated as required by QUEST.
  - Acceptance criteria: covered by recorder actual-type tests, stale descriptor codegen test, generic visible-text tests, `Name`-only guard, value-only no-synthetic guard, runtime validator value-only rejection.
  - Validation evidence: targeted and affected project tests passed; `dotnet build AppAutomation.sln` passed; Release sample build passed; headless sample UI tests passed.
  - Unsupported claims: full solution pass is not claimed in this run; FlaUI adapter runtime validation is limited by window placement failure before adapter assertions.
  - Regression / edge case: derived button controls, stale generated label descriptors, value-only text false positives, and visible-text-vs-Name semantics are covered.
  - Comments/docs/changelog: no docs/changelog needed for scoped bugfix; XML docs remain on new public extension overloads.
  - Hidden contract change: no existing public interface member added; new readable-text contract is additive.
  - Manual-review challenge: strongest challenge was P2 synthetic text; fixed by making value-only controls unsupported for `WaitUntilText...` unless a future adapter exposes real visible text.
- No-findings justification: after the P2 fix, code follows the approved truth-first typing invariant and visible-text semantics; remaining validation gap is environmental and not introduced by this change.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | visible-text semantics | `WaitUntilText...` could synthesize date/range/number/progress values as text | Remove synthetic value branches, reject value-only controls in validator, add regression tests | fixed |
| LOW | validation environment | `sample/DotnetDebug.AppAutomation.FlaUI.Tests` resolver filter failed before adapter assertions because desktop placement expected 1280x665 but window stayed 802x563 on the selected monitor | Treat as environment/infrastructure blocker; rely on build, headless UI suite and non-FlaUI tests for this change; report caveat | accepted-risk |
| LOW | validation environment | `dotnet test --project AppAutomation.sln --no-build --max-parallel-test-modules 1` timed out after 10 minutes without summary and left test child processes | Cleaned up current-worktree test processes; replaced with per-project validation evidence | accepted-risk |

- Fixed before final report: value-only synthetic text support removed; runtime validator whitelist narrowed; composite adapter readable-text implementations added; tab items without readable text now fail explicitly when read through a tab aggregate; failing recorder assertion updated to inspect finding details.
- Checks rerun:
  - `dotnet test --project tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj -- --treenode-filter "/*/*/UiPageExtensionsTests/*"` -> PASS, 33/33
  - `dotnet test --project tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj -- --treenode-filter "/*/*/UiPageExtensionsTests/*"` -> PASS, 33/33 after tab aggregate edge-case fix
  - `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*"` -> PASS, 66/66 after test assertion fix
  - `dotnet build AppAutomation.sln` -> PASS, warnings only
  - `dotnet build AppAutomation.sln --no-restore` -> PASS, warnings only after tab aggregate edge-case fix
  - `dotnet build sample/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj -c Release` -> PASS, warnings only
  - `dotnet test --project tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj --no-build` -> PASS, 62/62
  - `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj --no-build` -> PASS, 71/71
  - `dotnet test --project tests/AppAutomation.Authoring.Tests/AppAutomation.Authoring.Tests.csproj --no-build` -> PASS, 2/2
  - `dotnet test --project tests/AppAutomation.TestHost.Avalonia.Tests/AppAutomation.TestHost.Avalonia.Tests.csproj --no-build` -> PASS, 15/15
  - `dotnet test --project tests/AppAutomation.Build.Tests/AppAutomation.Build.Tests.csproj --no-build` -> PASS, 19/19
  - `dotnet test --project tests/AppAutomation.Tooling.Tests/AppAutomation.Tooling.Tests.csproj --no-build` -> PASS, 2/2
  - `dotnet test --project sample/DotnetDebug.Tests/DotnetDebug.Tests.csproj --no-build` -> PASS, 21/21
  - `dotnet test --project sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/DotnetDebug.AppAutomation.Avalonia.Headless.Tests.csproj --no-build` -> PASS, 40/40
  - `dotnet test --project sample/DotnetDebug.AppAutomation.FlaUI.Tests/DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-build -- --treenode-filter "/*/*/FlaUiControlResolverTests/*"` -> FAIL, 0/2, desktop window placement failed before adapter assertions
  - `dotnet test --project AppAutomation.sln --no-build --max-parallel-test-modules 1` -> timed out after 10 minutes, no summary
- Validation evidence: affected code and non-FlaUI suites passed; full aggregate/FlaUI desktop validation is blocked by existing environment behavior in this run.
- Unrelated changes: none intentionally introduced.
- Needs human: no.
- Residual risks / follow-ups: custom controls and value-only controls without explicit real visible text support will now fail `WaitUntilText...` until proper adapter/API support is added; FlaUI desktop placement failure should be handled separately from this bugfix.

## Approval
Получено: пользователь написал `спеку подтверждаю`.

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Контекст и диагностика | 0.88 | Нет | Создать spec и запросить подтверждение | Да | Нет | Найден корневой конфликт: `Button` text assertion записывается как `Label`, а codegen переиспользует locator property | `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`, `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` |
| SPEC | Спецификация и review | 0.92 | Подтверждение пользователя | Ждать фразу `Спеку подтверждаю`, затем перейти в EXEC | Да | Да, запрос подтверждения в ответе | Spec фиксирует additive extension methods, тесты и fallback для video evidence | `specs/2026-05-20-recorder-button-text-assertions.md` |
| SPEC | Правки после review | 0.94 | Подтверждение пользователя | Ждать фразу `Спеку подтверждаю`, затем перейти в EXEC | Да | Да, пользователь попросил внести изменения | Убрано source-breaking изменение `IButtonControl`, добавлен guard для button-derived controls и stale descriptor handling | `specs/2026-05-20-recorder-button-text-assertions.md` |
| SPEC | Truth-first уточнение | 0.95 | Подтверждение пользователя | Ждать фразу `Спеку подтверждаю`, затем перейти в EXEC | Да | Да, пользователь уточнил требование | Spec теперь фиксирует инвариант: component type определяется независимо от операции; неподдержанные методы не маскируются неверным типом | `specs/2026-05-20-recorder-button-text-assertions.md` |
| EXEC | Regression tests | 0.9 | Результат targeted test run | Запустить targeted tests, убедиться в reproducing failures, затем править implementation | Нет | Нет | Добавлены тесты на actual-type text assertions, stale same-locator descriptor и generic text wait | `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` |
| EXEC | Implementation | 0.86 | Результаты компиляции и тестов | Запустить targeted tests после фикса | Нет | Нет | Реализованы actual-type text assertion classifier, generic `IUiControl` text wait и codegen guard для несовместимого same-locator descriptor | `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs` |
| EXEC | Подтверждение спеки | 1.0 | Нет | Добавить regression tests до правки implementation | Нет | Да, пользователь написал `спеку подтверждаю` | QUEST gate открыт; можно менять код в границах спеки | `specs/2026-05-20-recorder-button-text-assertions.md` |
| EXEC | Initial validation | 0.98 | P2 visible-text review not applied yet | Выполнить post-EXEC review | Нет | Нет | Initial targeted recorder/abstractions tests and build passed before the later P2 correction; final validation evidence is captured in later journal rows | `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `AppAutomation.sln`, `sample/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj`, `sample/DotnetDebug.Tests/DotnetDebug.Tests.csproj` |
| EXEC | Post-EXEC review | 0.97 | Нет | Финальный ответ пользователю | Нет | Нет | Diff checked against approved scope; no code findings after validation; spec updated with actual evidence | `specs/2026-05-20-recorder-button-text-assertions.md`, `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`, `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs` |
| EXEC | Visible text correction | 0.99 | Нет | Запустить targeted validation и post-EXEC review | Нет | Да, пользователь указал, что `WaitUntilText...` должен проверять видимый текст, не `Name` | Generic text assertion reads specialized visible-text contracts or explicit `IReadableTextControl.Text`; synthetic date/range/number/progress text branches removed; adapter-level readable text added for composite controls where real visible text exists | `src/AppAutomation.Abstractions/UiControlContracts.cs`, `src/AppAutomation.Abstractions/UiControlAdapters.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Avalonia.Headless/Internal/AutomationModel/AutomationElements.cs`, `src/AppAutomation.Avalonia.Headless/Automation/HeadlessControlResolver.cs`, `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderCommandRuntimeValidator.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, `specs/2026-05-20-recorder-button-text-assertions.md` |
| EXEC | Post-P2 validation | 0.93 | Full aggregate/FlaUI desktop validation blocked by placement environment | Финальный ответ пользователю с validation caveat | Нет | Нет | Targeted and affected non-FlaUI suites passed; isolated FlaUI resolver suite fails before adapter assertions on existing desktop placement issue; full solution aggregate timed out and was replaced with per-project evidence | `specs/2026-05-20-recorder-button-text-assertions.md`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, `sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/DotnetDebug.AppAutomation.Avalonia.Headless.Tests.csproj`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests/DotnetDebug.AppAutomation.FlaUI.Tests.csproj`, `AppAutomation.sln` |
| EXEC | Final sanity fix | 0.96 | Нет | Финальный ответ пользователю | Нет | Нет | Manual review found that tab aggregates could silently join unsupported tab items as empty text; fixed to fail explicitly and reran affected tests plus solution build | `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `AppAutomation.sln`, `specs/2026-05-20-recorder-button-text-assertions.md` |
