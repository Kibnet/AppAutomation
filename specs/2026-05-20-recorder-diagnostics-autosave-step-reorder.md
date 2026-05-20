# Recorder diagnostics autosave and step reorder

## 0. Метаданные
- Тип (профиль): delivery-task; profiles `dotnet-desktop-client`, `ui-automation-testing`; context `testing-dotnet`
- Владелец: AppAutomation recorder
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до подтверждения этой спеки не менять код; сохранить публичные API-контракты без breaking changes; использовать существующие Avalonia/TUnit patterns
- Связанные ссылки: `src/AppAutomation.Recorder.Avalonia`, `tests/AppAutomation.Recorder.Avalonia.Tests`

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Нужно улучшить recorder UX:
- по умолчанию писать diagnostic log в файл;
- при активной записи автоматически сохранять сценарий после изменений шагов;
- дать пользователю стрелки для изменения порядка записанных шагов перед save/export.

Outcome contract:
- Success means: новый recorder session стартует с включенным file diagnostics; записанный/изменённый шаг во время `Recording` планирует autosave текущего сценария; overlay позволяет перемещать шаги вверх/вниз и порядок отражается в generated scenario.
- Итоговый артефакт / output: изменения в recorder options/session/overlay и регрессионные TUnit-тесты.
- Stop rules: остановиться после целевых тестов, build и полного test run; если full run технически невозможен, зафиксировать причину и next-best evidence.

## 2. Текущее состояние (AS-IS)
- `RecorderDiagnosticLogOptions.WriteToFile` сейчас имеет bool-default `false`; `RecorderSession` читает `options.DiagnosticLog.WriteToFile` в конструкторе и создаёт header только если опция включена.
- `RecorderSession.SaveAsync()` и `SaveToDirectoryAsync()` работают через single-flight `RunManagedOperationAsync`; параллельная операция отклоняется.
- Шаги добавляются в `RecorderSession.AddStep(...)`; remove/ignore/retry уже обновляют `_steps`, preview/status и вызывают `SessionChanged`.
- `IAppAutomationRecorderSessionDetails` публично отдаёт `StepJournal`, remove/ignore/retry/diagnostics, но не содержит reorder API.
- `RecorderOverlay.RenderStepJournal()` сейчас показывает последние 12 шагов в обратном порядке (`StepJournal.Reverse().Take(12)`) и рендерит текстовые action-кнопки `Remove`, `Ignore/Restore`, `Retry`, `Copy`.
- Тесты recorder используют TUnit в `tests/AppAutomation.Recorder.Avalonia.Tests` и fake session для overlay tests.
- Скрытая зависимость: `AuthoringCodeGenerator.SaveAsync` определяет canonical output paths; autosave должен использовать тот же путь, что и обычный `SaveAsync`, без нового output contract.

## 3. Проблема
Recorder требует ручных действий для диагностики, сохранения текущего сценария и исправления порядка шагов, из-за чего пользователь легко теряет диагностический контекст, последние записанные действия или получает сценарий с неправильной последовательностью.

## 4. Цели дизайна
- Разделение ответственности: options задают default diagnostics; session владеет autosave/reorder/state; overlay только вызывает session capability и отражает состояние.
- Повторное использование: autosave использует существующий save pipeline и generated scenario contract.
- Тестируемость: изменения покрываются session tests и overlay tests без ручного UI.
- Консистентность: busy/single-flight поведение остаётся единым для save/export/autosave.
- Обратная совместимость: не расширять публичный `IAppAutomationRecorderSessionDetails`; reorder capability добавить внутренним контрактом для overlay/session.

## 5. Non-Goals (чего НЕ делаем)
- Не добавлять новый persistence format или отдельный autosave-файл.
- Не менять generated scenario API, naming или code generation semantics вне порядка шагов.
- Не добавлять user setting для включения/выключения autosave.
- Не переписывать overlay layout целиком и не менять hotkey model.
- Не коммитить video artifacts.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `AppAutomationRecorderOptions.cs` -> default `RecorderDiagnosticLogOptions.WriteToFile = true`.
- `RecorderSession.cs` -> autosave queue while recording; reorder operations over `_steps`; status/session notifications.
- `IAppAutomationRecorderSessionDetails.cs` или новый файл рядом -> internal reorder capability (`IRecorderStepReorderSessionDetails`) без изменения public details interface.
- `RecorderOverlay.axaml.cs` -> рендер стрелок и вызов reorder capability; disabled states при busy/границах списка.
- `RecorderTests.cs` -> регрессионные tests для default diagnostics, autosave, reorder и overlay buttons.
- `sample/DotnetDebug.Avalonia/App.axaml.cs` -> recorder launch env must not override the default diagnostics-on behavior unless diagnostics are explicitly disabled.

### 6.2 Детальный дизайн
- Diagnostic default:
  - поменять default `RecorderDiagnosticLogOptions.WriteToFile` на `true`;
  - сохранить возможность явно отключить запись через `new RecorderDiagnosticLogOptions { WriteToFile = false }`;
  - sample recorder env `APPAUTOMATION_RECORDER_DIAGNOSTICS` трактовать как explicit opt-out (`0`/`false`), а не opt-in;
  - обновить текущий toggle test, который рассчитывал на default `false`.
- Autosave:
  - после успешного добавления, удаления, ignore/restore, retry validation и reorder шагов вызвать `RequestAutosaveIfRecording()`;
  - autosave запускается только при `_state == RecorderSessionState.Recording`;
  - autosave использует обычный save pipeline с `outputDirectory: null`;
  - если save/export/autosave уже идёт, пометить pending autosave и выполнить его после завершения активной операции, если запись всё ещё включена;
  - manual save/export не должны запускать рекурсивный бесконечный autosave; queued autosave должен сбрасываться после фактического запуска.
- Reorder:
  - добавить internal enum/capability для перемещения шага на одну позицию earlier/later в execution order;
  - `RecorderSession` меняет порядок элементов `_steps`, обновляет preview на актуальный последний неignored шаг, выставляет status и вызывает `SessionChanged`;
  - при невозможном move возвращать `false` без изменения состояния.
- Overlay:
  - показывать журнал в execution order для понятности стрелок: верхний шаг выполняется раньше нижнего;
  - рядом с действиями добавить compact arrow buttons `↑`/`↓`; они disabled при busy, отсутствии reorder capability или границе списка;
  - action-кнопки остаются без изменения смысла.
- Output contract / evidence rules:
  - generated scenario должен сохранять новый порядок `_steps`;
  - `CurrentScenarioFilePath` продолжает обновляться через обычный save result.
- Visual planning artifact для UI-facing изменений:
  ```text
  Recorded Steps
  [VALID] Ready to persist.
  Page.EnterText(...)
  [↑ disabled] [↓] [Remove] [Ignore] [Retry] [Copy]

  [WARN] Selector warning.
  Page.ClickButton(...)
  [↑] [↓ disabled] [Remove] [Ignore] [Retry] [Copy]
  ```
- UI test video evidence:
  - Не применимо до EXEC: в репозитории релевантная проверка overlay сейчас реализована TUnit/Avalonia object-level tests без настроенного video capture harness.
  - Fallback evidence: automated overlay assertions over `StepJournalPanel`, button enabled states and session method calls.
- Границы сохранения поведения:
  - manual `Save`/`Export` остаются single-flight;
  - recorder hotkeys не меняются;
  - public interface `IAppAutomationRecorderSessionDetails` не получает новые members.
- Обработка ошибок:
  - autosave использует существующую обработку `RunManagedOperationAsync`/`ApplySaveResult`;
  - queued autosave не должен бросать исключения в UI/event pipeline.
- Производительность:
  - autosave выполняется single-flight; burst изменений схлопывается pending-флагом в один последующий save.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Diagnostic file default:
  - default options => diagnostics file enabled;
  - explicit `WriteToFile = false` => diagnostics file disabled until user toggles it on.
  - sample recorder launch without `APPAUTOMATION_RECORDER_DIAGNOSTICS` => diagnostics file enabled; `0`/`false` => disabled.
- Autosave:
  - `state != Recording` => no autosave;
  - `state == Recording && !IsBusy` => start autosave;
  - `state == Recording && IsBusy` => set pending autosave;
  - active operation completed && pending autosave && state still Recording => run one autosave.
- Reorder:
  - moving earlier from index `0` is a no-op/false;
  - moving later from last index is a no-op/false;
  - moving any valid middle step swaps with adjacent step.

## 8. Точки интеграции и триггеры
- `RecorderSession.AddStep` после `_steps.Add(recordedStep)`.
- `RecorderSession.RemoveStep`.
- `RecorderSession.SetStepIgnored`.
- `RecorderSession.RetryStepValidation`.
- New internal `MoveStep`.
- `RecorderOverlay.CreateStepJournalItem` / action rendering.
- Existing overlay refresh path through `SessionChanged`.

## 9. Изменения модели данных / состояния
- Новое private состояние в `RecorderSession`: pending autosave flag.
- Нового persisted состояния нет.
- Новый internal reorder contract не влияет на serialized/generated output кроме порядка шагов.

## 10. Миграция / Rollout / Rollback
- Первый запуск с default options начнёт создавать diagnostic log file в output directory или temp fallback.
- Пользовательский код может явно передать `DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false }` для прежнего runtime behavior.
- Rollback: вернуть default diagnostics в `false`, удалить autosave calls/internal reorder capability и overlay arrow buttons.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - default `AppAutomationRecorderOptions` включает diagnostic file logging and header creation;
  - explicit diagnostics off still prevents file creation until toggle;
  - recording a step while `Recording` invokes save operation automatically;
  - multiple changes while busy collapse into one queued autosave after active operation;
  - reorder changes `StepJournal` order and generated preview/save order;
  - overlay renders arrow buttons and calls reorder capability with correct disabled states.
- Какие тесты добавить/изменить:
  - update diagnostic toggle/default tests in `RecorderTests.cs`;
  - add session autosave tests using injected `saveOperation`;
  - add session reorder test over `StepJournal`;
  - update overlay journal test to assert arrow buttons and fake session calls.
- Characterization tests / contract checks:
  - keep existing `RecorderSession_SaveAsync_IsSingleFlight` passing;
  - keep existing code generation save tests passing.
- Visual acceptance:
  - overlay step journal shows compact arrows before existing actions;
  - first execution-order item has up disabled, last has down disabled;
  - no text overlap in existing panel width constraints.
- UI video evidence:
  - Fallback evidence as above; no configured safe video harness in current TUnit overlay tests.
- Команды для проверки:
  - `dotnet test tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -c Release`
  - `dotnet build AppAutomation.sln -c Release`
  - `dotnet test AppAutomation.sln -c Release`
- Stop rules для test/retrieval/tool/validation loops:
  - targeted recorder tests pass;
  - build succeeds;
  - full test run succeeds or blocker is documented with exact failing command/evidence.

## 12. Риски и edge cases
- Autosave может перезаписать status сразу после step capture; это приемлемо, но tests должны проверять operation count/order, а не transient status.
- Autosave во время export может записать default scenario path после export; pending autosave запускается только если запись всё ещё активна и был change во время busy.
- Diagnostic default может увеличить filesystem writes; явное отключение остаётся доступно.
- Overlay order change с newest-first на execution-order может изменить привычный просмотр; это осознанно ради понятности reorder стрелок и generated scenario order.

## 13. План выполнения
1. Обновить default diagnostics и связанные tests.
2. Добавить internal reorder capability и session move logic.
3. Добавить autosave queue в `RecorderSession` и покрыть tests.
4. Обновить overlay journal rendering/actions and fake session tests.
5. Запустить targeted recorder tests, build, full test run.
6. Выполнить post-EXEC review по diff/tests/status и исправить находки.

## 14. Открытые вопросы
Нет блокирующих вопросов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования профиля:
  - UI thread не блокируется: autosave async/single-flight.
  - UI behavior покрывается существующим TUnit/Avalonia test pattern.
  - Stable selectors/automation-id не меняются.
  - Planned validation включает targeted tests, build и full test run.
  - Visual planning artifact зафиксирован; video evidence fallback обоснован.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` | default diagnostics on | выполнить требование diagnostics по умолчанию |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | autosave queue, reorder methods | сохранить сценарий при записи и менять порядок шагов |
| `src/AppAutomation.Recorder.Avalonia/IAppAutomationRecorderSessionDetails.cs` или новый internal файл | internal reorder capability | не ломать public details interface |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs` | arrow buttons and execution-order rendering | дать пользователю управление порядком |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | regression tests | покрыть новое behavior |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Diagnostics | file log off by default | file log on by default, explicit off supported |
| Save during recording | только manual hotkey/button/export | autosave after recorded/reviewed/reordered changes |
| Step order | remove/ignore/retry/copy only | add up/down reorder controls |
| Overlay journal order | newest-first | execution-order for reorder clarity |

## 18. Альтернативы и компромиссы
- Вариант: добавить reorder methods прямо в public `IAppAutomationRecorderSessionDetails`.
- Плюсы: проще wiring.
- Минусы: breaking change for external implementers.
- Почему выбранное решение лучше в контексте этой задачи: internal capability даёт overlay нужное поведение без расширения публичного интерфейса.

- Вариант: autosave silently skip if save busy.
- Плюсы: проще.
- Минусы: можно потерять последнее изменение, сделанное во время долгого save/export.
- Почему выбранное решение лучше в контексте этой задачи: pending autosave обеспечивает eventual save и сохраняет single-flight.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заданы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, алгоритмы, integration points, state and rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Migration/rollback, edge cases and compatibility covered |
| D. Проверяемость | 14-16 | PASS | Acceptance, tests, commands and visual fallback specified |
| E. Готовность к автономной реализации | 17-19 | PASS | План, альтернативы и review заполнены; открытых вопросов нет |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation profile requirements reflected |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Три доработки перечислены и ограничены Non-Goals |
| 2. Понимание текущего состояния | 5 | Указаны session/options/overlay/tests и single-flight invariant |
| 3. Конкретность целевого дизайна | 5 | Описаны default, autosave queue, reorder capability and UI behavior |
| 4. Безопасность (миграция, откат) | 5 | Есть explicit off для diagnostics, rollback and API compatibility |
| 5. Тестируемость | 5 | Есть acceptance criteria and exact commands |
| 6. Готовность к автономной реализации | 5 | Нет открытых вопросов; план и files scoped |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-05-20-recorder-diagnostics-autosave-step-reorder.md`, central stack (`model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `spec-linter`, `spec-rubric`, `review-loops`), selected profiles, open questions, planned changed files
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: просмотрены relevant recorder session/options/interface/overlay/test snippets and spec sections.
  - Contract pass: spec covers requested outcomes, Non-Goals, acceptance, UI artifact, testing and no public API break.
  - Adversarial risk pass: identified busy autosave loss, public interface break risk, diagnostics filesystem side effect and overlay order tradeoff.
  - Re-review after fixes / Fix and re-review: initial design adjusted to use internal reorder capability and queued autosave; affected sections rechecked.
  - Stop decision: PASS; no blocker/high findings remain.
- Evidence inspected: `RecorderSession` save/add/remove/retry flow; `RecorderDiagnosticLogOptions`; `RecorderOverlay` journal rendering; current recorder tests around diagnostics, overlay and single-flight.
- Depth checklist:
  - Scope drift / unrelated changes: scope limited to recorder diagnostics/autosave/reorder and tests.
  - Acceptance criteria: explicit and mapped to tests.
  - Validation evidence: planned commands are concrete; not run in SPEC phase.
  - Unsupported claims: claims are based on inspected code paths.
  - Regression / edge case: busy autosave and explicit diagnostics off covered.
  - Comments/docs/changelog: no docs/changelog required for small runtime UX behavior unless EXEC reveals public docs impact.
  - Hidden contract change: public interface expansion avoided; overlay order change disclosed.
  - Manual-review challenge: reviewer would likely challenge autosave during busy/export and public API shape; both are addressed.
- No-findings justification: remaining tradeoffs are documented and have tests/evidence plan.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | UX | Changing journal display from newest-first to execution-order may surprise existing users | Document in spec and cover with overlay tests | accepted-risk |

- Fixed before continuing: internal reorder capability chosen instead of public interface change; pending autosave added to design.
- Checks rerun: spec linter/rubric/review sections re-evaluated manually after design adjustment.
- Needs human: approval phrase required by QUEST gate.
- Residual risks / follow-ups: no blocker; video evidence unavailable in current TUnit overlay harness.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, `git diff --stat`, relevant diff for recorder/session/overlay/tests/FlaUI smoke helper, targeted test output, solution build output, full test output
- Decision: можно завершать
- Review passes:
  - Scope/Evidence pass: reviewed changed files and validation outputs; no unrelated tracked files beyond planned recorder/test/spec scope.
  - Contract pass: diagnostics default is on with explicit opt-out; autosave is recording-only and single-flight queued; reorder capability is internal; overlay renders arrows; tests cover requested behavior.
  - Adversarial risk pass: checked stale autosave scenario read in desktop smoke, single-flight synchronous completion, UI-thread unsafe `Task.Yield`, busy queued autosave, and public API expansion risk.
  - Re-review after fixes / Fix and re-review: fixed `Task.Yield` by pre-registering `TaskCompletionSource`; fixed FlaUI smoke helper to wait for scenario files newer than pre-save state; reran targeted and full validation.
  - Stop decision: PASS after targeted tests, build and full tests passed.
- Evidence inspected:
  - `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -c Release --no-restore` -> 88/88 passed.
  - `dotnet restore AppAutomation.sln` -> succeeded; existing NU1903 warnings for `Tmds.DBus.Protocol`.
  - `dotnet build AppAutomation.sln -c Release --no-restore` -> 0 errors, existing analyzer/vulnerability warnings.
  - `dotnet test --project sample/DotnetDebug.AppAutomation.FlaUI.Tests/DotnetDebug.AppAutomation.FlaUI.Tests.csproj -c Release --no-restore -- --treenode-filter "/*/*/DotnetDebugRecorderDesktopSmokeTests/RecorderSmokeSpinnerSavesTypedSpinnerStep"` -> 1/1 passed.
  - `dotnet test --project AppAutomation.sln -c Release --no-restore --no-build` -> 303/303 passed.
- Depth checklist:
  - Scope drift / unrelated changes: no unrelated tracked modifications in `git status --short`; local pinned SDK `.dotnet` and build outputs are ignored.
  - Acceptance criteria: all criteria covered by code and tests.
  - Validation evidence: targeted, build and full run completed successfully.
  - Unsupported claims: implementation claims are backed by diff and test output.
  - Regression / edge case: stale autosave file read in FlaUI smoke was found and fixed; pending autosave while busy is covered.
  - Comments/docs/changelog: no new comments needed; changelog not required by scoped runtime/test change.
  - Hidden contract change: public `IAppAutomationRecorderSessionDetails` unchanged; reorder contract is internal.
  - Manual-review challenge: reviewer would likely inspect autosave races and manual save freshness; both addressed by single-flight refactor and smoke helper freshness filter.
- No-findings justification: final diff matches spec, tests pass, and residual warnings pre-exist outside this change.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | regression | Autosave-created scenario file could be read by desktop smoke before manual save flushed typed spinner step | Wait for a scenario file newer than the pre-save timestamp | fixed |
| MEDIUM | correctness | `Task.Yield` in managed operation could move `SaveCore/FlushPendingState` away from the caller/UI context in tests | Register active operation through `TaskCompletionSource` before executing operation, without `Task.Yield` | fixed |

- Fixed before final report: stale scenario wait; managed operation single-flight implementation.
- Checks rerun: targeted recorder tests, targeted FlaUI smoke, solution build, full solution tests.
- Validation evidence: 88/88 recorder tests, 303/303 full tests, build 0 errors.
- Unrelated changes: none in tracked files.
- Needs human: нет.
- Residual risks / follow-ups: existing NU1903 and analyzer warnings remain outside this task.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Context discovery | 0.86 | Нет | Создать рабочую спецификацию | Нет | Нет | Прочитаны центральные инструкции и recorder code paths для bounded design | `src/AppAutomation.Recorder.Avalonia/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| SPEC | Spec authoring and review | 0.9 | Требуется только approval gate | Ожидать `Спеку подтверждаю` | Да | Да, запрос подтверждения будет отправлен пользователю | QUEST запрещает кодовые изменения до подтверждения спеки | `specs/2026-05-20-recorder-diagnostics-autosave-step-reorder.md` |
| EXEC | Approval received | 0.95 | Нет | Начать реализацию в границах спеки | Нет | Да, пользователь подтвердил спеки фразой `спеку подтверждаю` | Переход в EXEC разрешён; scope ограничен recorder diagnostics/autosave/reorder | `specs/2026-05-20-recorder-diagnostics-autosave-step-reorder.md` |
| EXEC | Implementation | 0.86 | Нет | Запустить targeted tests | Нет | Нет | Реализованы default diagnostics, recording-only autosave queue, internal reorder capability and overlay arrows | `src/AppAutomation.Recorder.Avalonia/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Targeted validation fixes | 0.88 | Нет | Запустить build/full tests | Нет | Нет | Targeted failures выявили async/threading риск; single-flight переписан без `Task.Yield`; recorder targeted suite прошёл | `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Full validation and review | 0.94 | Нет | Финальный отчёт | Нет | Нет | Full run выявил stale autosave read в FlaUI smoke; helper исправлен, targeted smoke and full solution tests прошли | `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/DotnetDebugRecorderDesktopSmokeTests.cs`, `specs/2026-05-20-recorder-diagnostics-autosave-step-reorder.md` |
