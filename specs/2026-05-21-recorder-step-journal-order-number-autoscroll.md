# Номера, порядок и автопрокрутка шагов recorder

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + overlay `ui-automation-testing`
- Владелец: AppAutomation
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка после обновления на текущий `master` / `origin/master`; на момент SPEC read-only inspection: `master=741f52c`; after follow-up rebase: `origin/master=8dff2fe`
- Ограничения: до подтверждения спеки менять только этот файл; actual rebase/worktree update выполнять только после `Спеку подтверждаю`; не менять generated DSL/output order; не менять публичный recorder session contract без необходимости
- Связанные ссылки: `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml`, `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Сделать журнал шагов в overlay recorder понятным как preview будущего теста: каждый шаг показывает номер, список идет в том же порядке, в котором шаги сохраняются/генерируются в финальном коде, а при добавлении нового шага overlay прокручивает журнал к последнему шагу. Новые изменения `master` уже перевели журнал на natural order и добавили reorder buttons; эта задача должна учесть их, не откатывая.

Outcome contract:
- Success means:
  - После обновления рабочей базы на `master` `Recorded Steps` сохраняет natural order из `IAppAutomationRecorderSessionDetails.StepJournal`, то есть тот порядок, который используется для save/autosave/codegen и меняется кнопками reorder.
  - Каждый видимый item показывает номер шага, начиная с 1, рассчитанный от текущего порядка journal, без изменения `RecorderStepJournalEntry` public record.
  - При увеличении количества journal entries `ScrollViewer` прокручивается вниз после re-render, чтобы новый шаг был виден.
  - Move earlier/Move later/Remove/Ignore/Retry/Copy продолжают работать по `StepId`, а не по визуальному номеру.
  - Поведение покрыто regression-тестами существующего recorder test suite.
- Итоговый артефакт / output: изменения XAML/code-behind overlay и tests в границах таблицы файлов, плюс отчет о targeted/build/full проверках.
- Stop rules:
  - На SPEC остановиться после spec quality gate и запроса подтверждения.
  - На EXEC первым шагом безопасно перевести detached worktree на текущий `master` / `origin/master`: проверить, что до sync есть только untracked spec, сохранить spec, выполнить `git switch --detach master` или создать именованную рабочую ветку от `master`, затем подтвердить `git status --short --branch`, что HEAD master-based и spec сохранена. Только после этого реализовывать поверх новых reorder/autosave изменений, выполнить targeted recorder tests, `dotnet build`, full test attempt и post-EXEC review.
  - Не расширять scope на изменение codegen, persistence, recorder capture pipeline или публичный session API, если implementation не требует этого.

## 2. Текущее состояние (AS-IS)
- Current detached worktree находится на `9fbd9e9` и содержит только untracked spec; `master`/`origin/master` уже на `741f52c` с PR #22 `recorder-diagnostics-autosave-reorder`.
- На old detached base `RecorderOverlay.RenderStepJournal()` еще делает `Reverse().Take(12)`.
- На inspected `master=741f52c` это уже изменено: `RenderStepJournal()` берет `_sessionDetails?.StepJournal?.ToArray()` без `Reverse()` и без `Take(12)`, поэтому visual order уже соответствует session/codegen order.
- На `master` добавлен internal `IRecorderStepReorderSessionDetails`, `RecorderStepMoveDirection`, session-level `CanMoveStep/MoveStep`, autosave after changes, and overlay action buttons `↑` / `↓`.
- `RecorderSession.StepJournal` возвращает `_steps.Select(CreateJournalEntry).ToArray()` в текущем порядке `_steps`; `SaveCoreAsync` и `AutosaveCoreAsync` передают `_steps.Where(!IsIgnored)` в том же порядке.
- `RecorderStepJournalEntry` не содержит display number; overlay item показывает status badge (`VALID`, `WARN`, `INVALID`, `IGNORED`) и preview без номера.
- `RecorderOverlay.axaml` на `master` всё еще содержит unnamed `ScrollViewer` вокруг `StepJournalPanel`; code-behind не может управлять его scroll position.
- Existing master test `Overlay_RendersStepJournal_BusySummary_AndReviewActions` уже учитывает reorder buttons and natural order action indexes, но не проверяет display numbers или autoscroll.

Скрытые зависимости и инварианты:
- Кнопки действий, включая новые `↑`/`↓`, должны продолжать брать `StepId` из `Tag`.
- Визуальная нумерация не должна становиться persisted state, иначе remove/ignore может создать расхождение с фактическим order.
- `StepJournal` public record лучше не менять для small UI fix: добавление positional parameter стало бы source-breaking для тестовых/внешних конструкторов.

## 3. Проблема
На текущем `master` порядок отображения уже нормализован и может дополнительно меняться reorder-кнопками, но строки журнала всё еще не имеют номера, а при росте списка новый шаг может оказаться вне видимой области без автопрокрутки. Если реализовывать поверх старой detached base, есть риск откатить уже добавленный reorder/natural-order код.

## 4. Цели дизайна
- Разделение ответственности: session хранит порядок, reorder и идентификаторы; overlay отвечает за display numbering и scroll.
- Повторное использование: оставить существующий `StepJournal`, master reorder buttons and action buttons.
- Тестируемость: обновить/добавить in-memory overlay tests на порядок после master/reorder, номер и action routing.
- Консистентность: визуальный порядок равен generated code order.
- Обратная совместимость: не менять public record/interface и generated output.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем порядок сохранения/export/codegen.
- Не меняем `RecorderSession.StepJournal` contract.
- Не добавляем persisted `StepNumber` в `RecordedStep` или `RecorderStepJournalEntry`.
- Не откатываем master changes: autosave, natural journal order, `IRecorderStepReorderSessionDetails`, `↑`/`↓` action buttons.
- Не меняем capture/debounce/validation behavior.
- Не перепроектируем overlay layout beyond minimal journal row changes.
- Не добавляем video artifact в репозиторий.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml` -> дать `ScrollViewer` имя `StepJournalScrollViewer`.
- `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs` -> хранить reference на scroll viewer, сохранить master natural-order rendering, считать display number через `Select((entry, index) => ...)`, скроллить вниз только когда количество entries увеличилось, не ломать reorder buttons.
- `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` -> обновить overlay regression на master shape: порядок first/second, номер `#1`/`#2`, move/action buttons still hit expected `StepId`; добавить проверку, что refresh после добавления не переворачивает список.

### 6.2 Детальный дизайн
- Поток данных:
  1. `OnSessionChanged` вызывает `Refresh()`.
  2. `Refresh()` вызывает `RenderStepJournal()`.
  3. `RenderStepJournal()` на `master` уже берет `StepJournal` без `Reverse()` и без `Take(12)`; implementation должна сохранить это поведение.
  4. Для каждого entry создается item с display number `index + 1`.
  5. Если `entries.Length > _renderedJournalEntryCount`, после добавления controls вызывается `ScrollToEnd()` через `Dispatcher.UIThread.Post(..., DispatcherPriority.Background)`.
  6. `_renderedJournalEntryCount` обновляется текущей длиной; remove/clear/ignore/retry/reorder без увеличения count не инициируют autoscroll.
- Контракты / API:
  - Public API не меняется.
  - `CreateStepJournalItem` получает display number отдельным параметром.
- Output contract / evidence rules:
  - UI item должен показывать номер рядом со status badge, например `#1 VALID`, без изменения preview text.
  - Action buttons continue using `entry.StepId`.
- Visual planning artifact для UI-facing изменений:
  ```text
  Recorded Steps
  [#1 VALID] Ready to persist.
  Page.EnterText(static page => page.SearchBox, "Alpha");
  [↑] [↓] [Remove] [Ignore] [Retry] [Copy]

  [#2 INVALID] Selector is ambiguous.
  Page.ClickButton(static page => page.RunButton);
  [↑] [↓] [Remove] [Ignore] [Retry] [Copy]
  ```
- UI test video evidence для UI automation задач: fallback. В репозитории есть deterministic TUnit/Avalonia overlay tests, но не найден runner/harness, сохраняющий video artifact для overlay unit tests. Next-best evidence: targeted `RecorderTests` + build/full test attempt.
- Границы сохранения поведения:
  - `StepCounter`, `SessionSummary`, validation badge и preview не меняют semantics.
  - Ignored steps остаются видимыми с тем же status logic, только с номером.
- Обработка ошибок:
  - Если scroll viewer не найден, rendering остается рабочим без autoscroll.
  - Empty journal сбрасывает rendered count в 0 и показывает empty text.
- Производительность:
  - На `master` full journal rendering уже принят вместе с reorder; добавление номера и one-shot `ScrollToEnd()` не меняет асимптотику.

## 7. Бизнес-правила / Алгоритмы (если есть)
| Событие | Порядок отображения | Нумерация | Scroll |
| --- | --- | --- | --- |
| Initial attach с existing journal | Natural order | 1..N | Scroll to end допустим, потому что count вырос с 0 до N |
| Добавлен новый шаг | Natural order | 1..N+1 | Scroll to end |
| Move earlier/later | Reordered `StepJournal` order | Recalculated 1..N | Не принудительно scroll |
| Ignore/restore/retry без изменения count | Current `StepJournal` order | 1..N | Не принудительно scroll |
| Remove step | Natural order remaining entries | 1..N-1 | Не принудительно scroll |
| Clear | Empty | Не применимо | Count reset to 0 |

## 8. Точки интеграции и триггеры
- `IAppAutomationRecorderSessionDetails.SessionChanged` -> `RecorderOverlay.Refresh()` -> `RenderStepJournal()`.
- User clicks action buttons -> existing `OnMoveStepEarlierClick`, `OnMoveStepLaterClick`, `OnRemoveStepClick`, `OnIgnoreStepClick`, `OnRetryStepClick`, `OnCopyStepPreviewClick` use `StepId`.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Новое private state в overlay: `ScrollViewer? _stepJournalScrollViewer`, `int _renderedJournalEntryCount`.
- Public `RecorderStepJournalEntry` остается прежним.

## 10. Миграция / Rollout / Rollback
- Миграция не нужна.
- Обратная совместимость: generated tests and session API unchanged.
- Rollback: удалить scroll viewer field/name и number display/autoscroll, откатить tests; не возвращать old reverse/take because master intentionally removed it.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- Overlay renders journal entries in the same order as `StepJournal`.
- First entry displays `#1`, second displays `#2`; numbering recalculates from current visible/natural order.
- Existing move/action buttons still target the same `StepId` as their row after numbering changes.
- After a move earlier/later refresh, numbering follows the new `StepJournal` order and does not keep stale row numbers.
- When `StepJournal` count increases and session raises `SessionChanged`, overlay schedules scroll-to-end through the named `ScrollViewer`.
- Empty journal still shows `No recorded steps yet.` and resets internal rendered count so the next added row is `#1` and can trigger the count-increase scroll path again.

Какие тесты добавить/изменить:
- Update `Overlay_RendersStepJournal_BusySummary_AndReviewActions`:
  - assert `journalPanel.Children[0]` contains `#1` and first preview.
  - assert `journalPanel.Children[1]` contains `#2` and second preview.
  - click move/remove/ignore/retry actions on the intended visual row and assert the same row `StepId`, taking master `↑`/`↓` button indexes into account.
- Add/extend helper checks for child text traversal if needed.
- Add mandatory refresh/addition/autoscroll regression:
  - assert `StepJournalScrollViewer` exists;
  - start with one entry, attach overlay, add a second entry, raise `SessionChanged`, assert the second row is last and displays `#2`;
  - clear/reset the journal, raise `SessionChanged`, then add one entry and assert the only row displays `#1`;
  - direct scroll offset assertion is optional only if the unit layout surface cannot produce a reliable measured scroll extent; the count-increase and reset path still must be exercised.
- Add or extend reorder test if practical: move a step through fake `IRecorderStepReorderSessionDetails`, raise refresh and assert numbers are recalculated for the new order.

Characterization tests / contract checks:
- On inspected `master`, order/reorder tests already pass without this change; new characterization is that updated tests expecting `#1`/`#2` and named scroll viewer should fail before implementation.

Visual acceptance для UI-facing изменений:
- Layout follows the text artifact in section 6.2: number appears in the journal header before status, preview/actions stay below.

UI video evidence:
- Fallback: targeted TUnit overlay tests; no video-capable harness identified for this small overlay unit surface.

Базовые замеры performance:
- Не применимо; expected journal size small and no performance-sensitive loop added.

Команды для проверки:
- Targeted:
  - `dotnet test tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/Overlay_RendersStepJournal_BusySummary_AndReviewActions"`
  - `dotnet test tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*"`
- Build:
  - `dotnet build AppAutomation.sln`
- Full:
  - `dotnet test AppAutomation.sln`

Stop rules для test/retrieval/tool/validation loops:
- Если targeted overlay test падает по layout traversal, исправить test helper или UI tree according to accepted design and rerun targeted.
- Если full solution tests fail on known unrelated desktop/FlaUI environment issue, record exact failure and rely on targeted/build evidence for scoped change.
- Не добавлять new public API solely to make tests easier.

## 12. Риски и edge cases
- Риск: implementation поверх old detached base может случайно потерять master reorder/autosave changes. Mitigation: after approval, first update/rebase onto current `master` and implement only on top of the master overlay shape.
- Риск: autoscroll на every refresh раздражает при review old steps. Mitigation: scroll only when entry count increases, not on ignore/retry/status refresh.
- Риск: numbering ignored/invalid steps может восприниматься как persisted code line number. Mitigation: number is display-only and follows current journal order; ignored status remains explicit.
- Риск: tests cannot directly observe `ScrollToEnd()` offset without real measure pass. Mitigation: mandatory test verifies named scroll viewer plus count-increase/reset path; direct offset assertion remains optional only if Avalonia unit layout cannot make it deterministic; build validates the API call.

## 13. План выполнения
1. После подтверждения безопасно синхронизировать detached worktree:
   - проверить `git status --short --branch` and ensure only the spec is untracked;
   - preserve the spec while switching bases;
   - run `git switch --detach master` or create a named branch from `master` if delivery needs a branch;
   - confirm `git status --short --branch` shows master-based HEAD and the spec still present.
2. Обновить overlay tests на master natural/reorder order, numbers and row action routing.
3. Изменить XAML: name the journal `ScrollViewer`.
4. Изменить code-behind: add fields, preserve master natural order/reorder buttons, add display numbering, schedule `ScrollToEnd()` on count increase.
5. Запустить targeted recorder tests.
6. Запустить `dotnet build AppAutomation.sln`.
7. Запустить full test attempt.
8. Выполнить post-EXEC review и исправить findings в рамках спеки.

## 14. Открытые вопросы
Нет блокирующих вопросов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля: stable selectors/automation ids не меняются; UI-facing behavior covered by existing recorder/Avalonia tests; validation plan includes targeted UI tests, build and full test attempt; video fallback documented.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml` | Add `x:Name="StepJournalScrollViewer"` to journal scroll viewer | Enable code-behind autoscroll |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs` | Preserve master natural/reorder order, display `#N`, track count, call `ScrollToEnd()` after additions | Match final generated code order and keep new steps visible |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | Update/add overlay regression assertions | Lock order, numbering and action routing behavior |
| `specs/2026-05-21-recorder-step-journal-order-number-autoscroll.md` | Working QUEST artifact and EXEC journal | Required by central instructions |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Journal order on old detached base | Newest first via `Reverse().Take(12)` | After master update: already recorded/codegen order; this task preserves it |
| Journal reorder on master | `↑`/`↓` buttons change `StepJournal` order, but rows have no numbers | Reordered rows are renumbered from current order |
| Step identity in UI | Status + preview only | Display number + status + preview |
| New step visibility | Depends on current scroll position | Scrolls to bottom when count increases |
| Public session contract | Unchanged | Unchanged |

## 18. Альтернативы и компромиссы
- Вариант: добавить `StepNumber` в `RecorderStepJournalEntry`.
- Плюсы: number available to all consumers.
- Минусы: changes public positional record constructor for a display-only concern.
- Почему выбранное решение лучше в контексте этой задачи: numbering is overlay-specific and can be derived from ordered journal without public API churn.

- Вариант: повторно менять порядок списка в этой задаче.
- Плюсы: могло бы закрыть старую detached-base реализацию.
- Минусы: на `master` порядок уже исправлен и связан с reorder/autosave tests; повторная правка повышает риск regression.
- Почему выбранное решение лучше в контексте этой задачи: после rebase implementation должна сохранить master order and only add numbering/autoscroll.

- Вариант: always scroll on every refresh.
- Плюсы: simplest implementation.
- Минусы: makes reviewing older steps difficult during validation updates.
- Почему выбранное решение лучше в контексте этой задачи: count-increase scroll matches "при добавлении шагов" and avoids unnecessary movement.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заполнены |
| B. Качество дизайна | 6-10 | PASS | Ответственность overlay/session разделена; master reorder/autosave preserved; public state unchanged; rollback covered |
| C. Безопасность изменений | 11-13 | PASS | Scope small; no migration/data/API changes; risks and edge cases listed |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, targeted/build/full commands and UI fallback evidence listed |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan concrete; open blockers absent; alternatives reviewed |
| F. Соответствие профилю | 20 | PASS | Dotnet desktop + UI automation requirements reflected; video fallback documented |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Outcome and Non-Goals are concrete |
| 2. Понимание текущего состояния | 5 | Old detached reverse/take behavior and new master natural/reorder/autosave behavior are identified |
| 3. Конкретность целевого дизайна | 5 | File-level implementation and scroll/numbering algorithm are specified |
| 4. Безопасность (миграция, откат) | 5 | No migration; rollback and no public API churn are explicit |
| 5. Тестируемость | 5 | Targeted tests and acceptance are measurable |
| 6. Готовность к автономной реализации | 5 | No open blockers; plan and stop rules are complete |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-05-21-recorder-step-journal-order-number-autoscroll.md`, central instruction stack (`model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `quest-mode`, `spec-linter`, `spec-rubric`, `review-loops`), selected profiles, no open questions, planned files in section 16, read-only diff `HEAD..master`.
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: inspected old `RecorderOverlay.axaml`, `RecorderOverlay.axaml.cs`, `RecorderSession.StepJournal`/`SaveCoreAsync`, existing overlay test surface, and read-only `HEAD..master` diff showing natural order, reorder buttons and autosave additions.
  - Contract pass: spec preserves public session/codegen contracts, preserves master reorder/autosave behavior, addresses requested number/order/autoscroll, includes acceptance criteria and validation plan.
  - Adversarial risk pass: checked public record break risk, over-scrolling risk, old-base regression risk, `Take(12)`/master full-journal tradeoff, action button `StepId` routing, visual/video evidence requirement.
  - Re-review after fixes / Fix and re-review: after user asked to rebase on master, AS-IS/TO-BE/tests/risks were updated to account for `master=741f52c`; after review findings, detached sync steps and mandatory autoscroll regression were tightened; linter/rubric rechecked.
  - Stop decision: PASS; ready for user approval before EXEC.
- Evidence inspected:
  - `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml`
  - `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - `src/AppAutomation.Recorder.Avalonia/IAppAutomationRecorderSessionDetails.cs`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - read-only `git diff HEAD..master -- src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs src/AppAutomation.Recorder.Avalonia/IAppAutomationRecorderSessionDetails.cs src/AppAutomation.Recorder.Avalonia/RecorderSession.cs tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
- Depth checklist:
  - Scope drift / unrelated changes: only spec file changed in SPEC phase.
  - Acceptance criteria: concrete and testable.
  - Validation evidence: commands planned; no EXEC validation yet.
  - Unsupported claims: current behavior claims are tied to inspected files.
  - Regression / edge case: public API churn, action routing, master reorder/autosave preservation, long journal and scroll behavior considered.
  - Comments/docs/changelog: docs/changelog not needed for scoped overlay behavior fix.
  - Hidden contract change: no public API or generated output change planned.
  - Manual-review challenge: reviewer likely asks whether master already fixed order and whether numbering survives reorder; both answered in AS-IS/design/tests.
- No-findings justification: Не применимо: review findings are listed below and fixed before approval request.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | branch-sync | Sync step was under-specified for current detached worktree and could lead to an ineffective rebase or edits on the old base. | Make EXEC sync explicit: verify status, preserve spec, `git switch --detach master` or create branch from master, then confirm master-based HEAD before source edits. | fixed |
| MEDIUM | tests | Autoscroll coverage was optional despite being an acceptance criterion. | Make named scroll viewer, count-increase path, and clear/reset numbering regression mandatory; keep direct offset assertion optional only if layout is nondeterministic. | fixed |
| LOW | branch-sync | Current worktree is detached behind `master`; implementation on old base could conflict with or undo PR #22 reorder/autosave changes. | After approval, update to current `master` first and implement only numbering/autoscroll on top. | fixed |

- Fixed before continuing: Chose display-derived numbering over public record changes; constrained autoscroll to count increase; updated spec for master reorder/autosave changes; made detached master sync explicit; made autoscroll regression mandatory.
- Checks rerun: SPEC linter/rubric self-check after review edits.
- Needs human: Требуется фраза `Спеку подтверждаю` для перехода в EXEC
- Residual risks / follow-ups: Future virtualization/page-size option may be useful if recorder sessions routinely exceed hundreds of steps.

### Post-EXEC Review
- Статус: PASS with unrelated full-suite failures documented
- Scope reviewed: `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml`, `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, this spec
- Decision: изменения соответствуют утвержденной спеки; дополнительных правок по scope не требуется
- Review passes:
  - Scope/Evidence pass: diff ограничен overlay XAML/code-behind, recorder tests and spec; public session/codegen contracts untouched.
  - Contract pass: journal order stays natural from `StepJournal`; row actions still use `StepId`; display number is derived UI state only.
  - Adversarial risk pass: checked count reset after empty journal, no scroll on same-count refresh/reorder, no public `RecorderStepJournalEntry` constructor change, no restore of old `Reverse().Take(12)` behavior.
  - Re-review after fixes / Fix and re-review: after the initial `DrainUiAsync` hang during implementation, helper was changed to `Dispatcher.UIThread.RunJobs()` and the new regression test passed.
  - Stop decision: PASS; final report can cite scoped green validation and unrelated full-suite failures.
- Evidence inspected:
  - `git diff -- src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
  - `git diff --check`
  - targeted red characterization: `Overlay_RendersStepJournal_BusySummary_AndReviewActions` failed before implementation because rows lacked `#1`/`#2`
  - targeted green: `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/Overlay_RendersStepJournal_BusySummary_AndReviewActions"`
  - targeted green: `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/Overlay_RendersStepNumbers_AndResetsAutoscrollStateAfterEmptyJournal"`
  - scoped green: `dotnet test --no-restore --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*"` -> 88 passed, 0 failed
  - build green: `dotnet build AppAutomation.sln`
  - full attempt: `dotnet test --project AppAutomation.sln`; sandbox attempt failed on `NU1301`, escalated attempt ran tests and failed 4 unrelated sample tests while `AppAutomation.Recorder.Avalonia.Tests` passed
  - follow-up rebase evidence: fetched `origin/master=8dff2fe`, applied changes over PR #23 `remove-recorder-minimize`, resolved one conflict in `RecorderOverlay.axaml.cs` by keeping master minimize removal and retaining journal numbering/autoscroll.
  - follow-up green: `dotnet build AppAutomation.sln` -> passed with existing warnings only.
  - follow-up green: `dotnet test --no-restore --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*"` -> 89 passed, 0 failed.
  - review-fix evidence: added deterministic autoscroll regression through an internal test seam because detached unit layout does not produce a real `ScrollViewer` extent without a full visual root; targeted `Overlay_AutoscrollsStepJournal_WhenEntryCountIncreases` passed.
  - review-fix green: `dotnet test --no-restore --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*"` -> 90 passed, 0 failed.
  - review-fix green: `dotnet build AppAutomation.sln` -> passed with existing warnings only.
- Depth checklist:
  - Scope drift / unrelated changes: PASS; only three implementation/test files plus spec changed.
  - Acceptance criteria: PASS; numbers, natural order, count-increase scroll path and empty reset are covered.
  - Validation evidence: PASS; targeted/scoped/build green; full-suite unrelated failures captured.
  - Unsupported claims: PASS; implementation statements tied to diff and test output.
  - Regression / edge case: PASS; empty journal reset and no API churn reviewed.
  - Comments/docs/changelog: PASS; no product docs/changelog needed for scoped recorder UI behavior.
  - Hidden contract change: PASS; no public interface/record/codegen output changes.
  - Manual-review challenge: PASS; likely questions around master order and autoscroll determinism are answered by diff and tests.
- No-findings justification: scoped review found no actionable defects in the implemented change.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | full-suite | Full solution test attempt fails in sample projects outside this change: 3 `DotnetDebug.Tests` failures cannot find desktop app executable, 1 `DotnetDebug.AppAutomation.FlaUI.Tests` assertion expected an exception but got null. | Document as unrelated validation risk; do not expand scope into sample/FlaUI fixes. | documented |

- Fixed before final report: `DrainUiAsync` changed to synchronous `Dispatcher.UIThread.RunJobs()` after async dispatcher drain hung in the new test; recorder suite rerun passed.
- Checks rerun: `git diff --check`; targeted recorder tests; scoped `RecorderTests` suite; `dotnet build AppAutomation.sln`; full `dotnet test --project AppAutomation.sln` attempt; after follow-up master rebase, reran `dotnet build AppAutomation.sln` and scoped `RecorderTests` suite; after review fix, reran targeted autoscroll test, scoped `RecorderTests` suite and `dotnet build AppAutomation.sln`.
- Validation evidence: targeted/scoped/build PASS; full-suite FAIL with unrelated sample failures listed above.
- Unrelated changes: none in git diff; full-suite failures are not from modified files.
- Needs human: no
- Residual risks / follow-ups: Direct pixel/offset proof of `ScrollToEnd()` is not asserted because the unit test surface does not perform a deterministic measured scroll extent; the named scroll viewer and count-increase/reset path are covered.

## Approval
Получено: пользователь написал `спеку подтверждаю`.

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Контекст и маршрутизация | 0.95 | Нет | Проанализировать recorder overlay | Нет | Нет | Прочитаны central instructions, выбран delivery-task stack с desktop/UI automation профилями | `C:\Users\Kibnet\.codex\agents\AGENTS.md`, central instruction docs |
| SPEC | Анализ кода | 0.92 | Нет | Создать рабочую spec | Нет | Нет | Найдены reverse/take journal rendering, natural session/codegen order and existing overlay tests | `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml`, `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| SPEC | Quality gate | 0.94 | Пользовательское подтверждение | Запросить `Спеку подтверждаю` | Да | Да, ожидается решение пользователя | SPEC linter, rubric and post-SPEC review заполнены; код менять нельзя до approval | `specs/2026-05-21-recorder-step-journal-order-number-autoscroll.md` |
| SPEC | Master sync analysis | 0.94 | Пользовательское подтверждение | Запросить `Спеку подтверждаю` | Да | Да, пользователь попросил rebase на master | Rebase не выполнен из-за SPEC gate; read-only `HEAD..master` показал, что master уже добавил natural order, reorder buttons and autosave, поэтому spec обновлена под новую базу | `specs/2026-05-21-recorder-step-journal-order-number-autoscroll.md`, `master=741f52c` |
| SPEC | Review fixes | 0.95 | Пользовательское подтверждение | Запросить `Спеку подтверждаю` | Да | Да, пользователь попросил внести review fixes | EXEC sync для detached worktree сделан конкретным; autoscroll regression стал обязательным с named scroll viewer, count-increase and reset path | `specs/2026-05-21-recorder-step-journal-order-number-autoscroll.md` |
| EXEC | Approval received | 1.0 | Нет | Синхронизировать detached worktree с master | Нет | Да, пользователь написал `спеку подтверждаю` | QUEST gate открыт; можно менять код в границах утверждённой спеки | `specs/2026-05-21-recorder-step-journal-order-number-autoscroll.md` |
| EXEC | Master sync | 0.98 | Нет | Обновить regression tests | Нет | Нет | Worktree safely switched to detached `master=741f52c`; status shows only the spec as untracked | `specs/2026-05-21-recorder-step-journal-order-number-autoscroll.md`, git worktree |
| EXEC | Implementation | 0.94 | Нет | Запустить targeted validation | Нет | Нет | Added named step journal scroll viewer, derived `#N` display badge, count-increase `ScrollToEnd()`, and regression tests for natural order, numbering and empty reset | `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml`, `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Targeted validation | 0.96 | Нет | Запустить build/full validation | Нет | Нет | Targeted red before implementation confirmed missing numbers; after implementation specific tests and full `RecorderTests` suite passed, including 88/88 scoped recorder tests | `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Build and full validation | 0.88 | Full-suite sample failures are unrelated but still need reporting | Выполнить final diff review | Нет | Нет | `dotnet build AppAutomation.sln` passed; full solution test attempt ran after sandbox restore retry and failed only in unrelated sample desktop/FlaUI tests while recorder tests passed | solution validation output |
| EXEC | Post-EXEC review | 0.93 | Нет | Финальный ответ | Нет | Нет | Diff reviewed against spec; no public contract changes or scope drift found; unrelated full-suite failures documented | implementation diff, `specs/2026-05-21-recorder-step-journal-order-number-autoscroll.md` |
| EXEC | Actual master rebase | 0.95 | Нет | Финальный статус | Нет | Да, пользователь попросил отребейзить на актуальный master | Fetched `origin/master=8dff2fe`, moved detached HEAD there, reapplied local changes, resolved one `RecorderOverlay.axaml.cs` conflict while preserving PR #23 minimize removal, reran build and scoped recorder tests | git worktree, `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs`, validation output |
| EXEC | Review fix | 0.93 | Нет | Финальный статус | Нет | Да, пользователь попросил добавить тест | Added `Overlay_AutoscrollsStepJournal_WhenEntryCountIncreases` with an internal scroll seam to prove `ScrollToEnd` is scheduled on entry-count growth and not on same-count refresh; targeted test, scoped suite and build passed | `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, validation output |
