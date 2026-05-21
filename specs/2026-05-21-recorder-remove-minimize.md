# Удаление минимизации recorder overlay

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: минимально-достаточная правка; не ломать публичные recorder options без отдельного согласования
- Связанные ссылки: `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml`, `src/AppAutomation.Recorder.Avalonia/RecorderHotkeyMap.cs`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
Убрать из user-facing recorder overlay нерабочую минимизацию: кнопку `Minimize`, свернутое состояние `Restore`, hotkey/legend/settings для toggle overlay и runtime-связку события toggle.

Outcome contract:
- Success means: overlay больше не предлагает и не выполняет minimize/restore, а старые публичные properties остаются source-compatible no-op.
- Итоговый артефакт / output: код, regression tests, README/changelog.
- Stop rules: остановиться после targeted recorder tests, `dotnet build`, full доступной проверки или явного объяснения, почему full run недоступен.

## 2. Текущее состояние (AS-IS)
- `RecorderOverlay.axaml` содержит `MinimizeButton`, `MinimizedPanel`, `RestoreButton`.
- `RecorderOverlay.axaml.cs` хранит `IsMinimized`, события `MinimizeRequested`/`RestoreRequested`, методы `Minimize`/`Restore`/`ToggleMinimized`.
- `RecorderHotkeyMap` включает `RecorderCommandKind.ToggleOverlayMinimize`, а settings window строит строку hotkey из `EnumerateCommands()`.
- `RecorderSession` поднимает `OverlayToggleRequested`, а `AppAutomationRecorder` вызывает `overlay.ToggleMinimized()`.
- Публичные `RecorderHotkeys.ToggleOverlayMinimize` и `RecorderOverlayOptions.StartMinimized` уже могут встречаться в consumer-коде.

## 3. Проблема
Recorder показывает и поддерживает minimize/restore, но функция не работает надежно и создает ложный пользовательский контракт.

## 4. Цели дизайна
- Разделение ответственности: overlay отвечает только за доступные UI-команды.
- Повторное использование: сохранить существующий overlay без новой модели состояния.
- Тестируемость: обновить unit/regression tests на отсутствие minimize UI и hotkey.
- Консистентность: убрать упоминание функции из README и changelog.
- Обратная совместимость: не удалять публичные option properties в этой правке.

## 5. Non-Goals (чего НЕ делаем)
- Не проектируем новую docking/minimize модель.
- Не меняем recorder capture logic, code generation, validation или export.
- Не удаляем публичные `RecorderHotkeys.ToggleOverlayMinimize` / `RecorderOverlayOptions.StartMinimized`.
- Не переписываем старые исторические specs.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `RecorderOverlay.axaml` -> toolbar без `Minimize`; удалить свернутую панель.
- `RecorderOverlay.axaml.cs` -> удалить состояние minimize/restore и связанные события.
- `RecorderHotkeyMap.cs` / `RecorderSession.cs` / `AppAutomationRecorder.cs` -> убрать user-facing hotkey command path.
- `RecorderTests.cs` -> обновить assertions под новый контракт.
- `README.md`, `CHANGELOG.md` -> убрать устаревшее описание функции.

### 6.2 Детальный дизайн
- Потоки данных: hotkey map больше не возвращает `ToggleOverlayMinimize`; persisted key остается десериализуемым через сохраненный enum member, но не участвует в `EnumerateCommands()`.
- Контракты / API: публичные properties остаются, но больше не влияют на UI.
- Output contract / evidence rules: targeted recorder tests + build; full test run если укладывается в доступную среду.
- Visual planning artifact для UI-facing изменений:

```text
Record | Clear | Save | Export... | Settings | 0 steps | VALID | status...
```

- UI test video evidence: `Не применимо`; в репозитории нет отдельного video-capable harness для recorder overlay window, next-best evidence - automated recorder overlay tests.
- Границы сохранения поведения: record/clear/save/export/settings, diagnostics, journal и shortcut legend продолжают работать.
- Обработка ошибок: не меняется.
- Производительность: не меняется.

## 7. Бизнес-правила / Алгоритмы
- Active commands in hotkey map are only commands returned by `RecorderHotkeyMap.EnumerateCommands()`.
- Obsolete persisted `ToggleOverlayMinimize` key must not break settings load.

## 8. Точки интеграции и триггеры
- `RecorderSession.HandleRecorderCommand` больше не должен инициировать overlay toggle.
- `AppAutomationRecorder.AttachOverlay` больше не должен подписывать overlay toggle.
- `RecorderHotkeySettingsWindow` автоматически теряет строку overlay toggle через обновленный `EnumerateCommands()`.

## 9. Изменения модели данных / состояния
- Новых полей нет.
- Persisted hotkey settings могут содержать старый key; он игнорируется.

## 10. Миграция / Rollout / Rollback
- Первый запуск: старые hotkey settings с `ToggleOverlayMinimize` не ломают загрузку.
- Обратная совместимость: публичные properties сохранены как no-op.
- Rollback: вернуть удаленные overlay controls/state и hotkey command inclusion.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В overlay XAML нет `MinimizeButton`, `RestoreButton`, `MinimizedPanel`.
  - `StartMinimized = true` не переводит overlay в скрытую/свернутую панель.
  - `ToggleOverlayMinimize` не появляется в legend/settings command enumeration и не резолвится hotkey map.
  - README и changelog не обещают minimize/restore.
- Какие тесты добавить/изменить:
  - Обновить `HotkeyMap_UsesConfiguredGestures_AndBuildsLegend`.
  - Заменить `Overlay_MinimizeRestore_UpdatesPresentationAndCounters` на проверку отсутствия minimize UI и сохранения counters.
- Characterization tests / contract checks: targeted `AppAutomation.Recorder.Avalonia.Tests`.
- Visual acceptance: toolbar соответствует low-fi artifact выше.
- UI video evidence: fallback на automated overlay tests, причина указана в 6.2.
- Команды для проверки:
  - `dotnet test tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -c Release`
  - `dotnet build AppAutomation.sln -c Release`
  - full test run по решению, если среда выдерживает время/GUI ограничения.
- Stop rules для loops: не расширять scope за пределы recorder minimize removal.

## 12. Риски и edge cases
- Старые settings-файлы с `ToggleOverlayMinimize` могут сломаться, если enum member удалить полностью; mitigation: оставить enum member, но исключить из active enumeration.
- Публичные properties могут выглядеть устаревшими; mitigation: сохранить source compatibility и не усиливать изменение до breaking API без отдельного решения.

## 13. План выполнения
1. Убрать XAML/code-behind minimize state из overlay.
2. Исключить overlay toggle из active hotkey map и session binding.
3. Обновить tests/docs/changelog.
4. Запустить targeted checks и build.

## 14. Открытые вопросы
Нет блокирующих вопросов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования профиля: сохраняются selectors для оставшихся controls; UI-facing изменение покрывается existing recorder tests; video fallback обоснован.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml` | Удалить minimize/restore controls | Убрать нерабочую функцию из UI |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs` | Удалить minimize state/event handlers | Убрать поведение |
| `src/AppAutomation.Recorder.Avalonia/RecorderHotkeyMap.cs` | Исключить toggle command из active commands | Убрать hotkey/legend/settings |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | Убрать overlay toggle action | Убрать runtime trigger |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorder.cs` | Убрать subscription на toggle | Убрать integration path |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | Обновить regression coverage | Проверить новый контракт |
| `README.md`, `CHANGELOG.md` | Обновить описание | Не обещать удаленную функцию |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Overlay UI | `Minimize` + `Restore` panel | Только основной overlay |
| Hotkeys | `Ctrl+Shift+M: Overlay` | Нет user-facing toggle |
| Public options | `ToggleOverlayMinimize`, `StartMinimized` действовали | Сохранены, но ignored/no-op |

## 18. Альтернативы и компромиссы
- Вариант: удалить публичные properties.
- Плюсы: API чище.
- Минусы: breaking change для consumers.
- Почему выбранное решение лучше: пользовательская проблема решается без source-breaking изменения.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, состояние и rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Acceptance, риски и план покрывают no-op совместимость. |
| D. Проверяемость | 14-16 | PASS | Тесты, команды и таблица файлов заданы. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, альтернатива и review заполнены. |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation требования учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope ограничен удалением minimize user-facing surface. |
| 2. Понимание текущего состояния | 5 | Названы overlay, hotkey map, session binding и tests. |
| 3. Конкретность целевого дизайна | 5 | Задан файл-by-файл дизайн и active command rule. |
| 4. Безопасность (миграция, откат) | 5 | Сохранена совместимость persisted enum/property. |
| 5. Тестируемость | 5 | Есть targeted tests и build commands. |
| 6. Готовность к автономной реализации | 5 | Открытых вопросов нет. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-05-21-recorder-remove-minimize.md`, instruction stack, selected profiles, open questions, planned changed files
- Decision: можно реализовывать по прямому запросу пользователя в текущем режиме выполнения
- Review passes:
  - Scope/Evidence pass: просмотрены найденные references по `Minimize`/`ToggleOverlayMinimize`/`StartMinimized`.
  - Contract pass: Non-Goals сохраняют public API, acceptance покрывает removal.
  - Adversarial risk pass: главный риск persisted settings снят сохранением enum member.
  - Re-review after fixes / Fix and re-review: не потребовалось.
  - Stop decision: PASS.
- Evidence inspected: `rg` по minimization references, `RecorderOverlay.axaml`, `RecorderOverlay.axaml.cs`, `RecorderHotkeyMap.cs`, `RecorderSession.cs`, tests.
- Depth checklist:
  - Scope drift / unrelated changes: границы заданы.
  - Acceptance criteria: проверяемые.
  - Validation evidence: команды заданы.
  - Unsupported claims: нет.
  - Regression / edge case: persisted settings учтены.
  - Comments/docs/changelog: README/changelog включены.
  - Hidden contract change: public API не удаляется.
  - Manual-review challenge: проверил бы, что active enumeration действительно убирает settings row и legend.
- No-findings justification: spec small, concrete, с прямыми file targets и tests.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence не применяется для overlay, нужен fallback | Указать reason и next-best tests | fixed |

- Fixed before continuing: fallback зафиксирован в 6.2 и 11.
- Checks rerun: self-review по spec.
- Needs human: нет.
- Residual risks / follow-ups: public no-op properties можно пометить obsolete отдельным API решением.

### Post-EXEC Review
- Статус: PASS с unrelated validation residual
- Scope reviewed: spec, `git status --short`, `git diff --stat`, relevant diff, references scan, targeted recorder tests, solution build, full solution test attempt
- Decision: можно завершать текущую recorder-задачу; unrelated FlaUI failure не исправлять в этом scope
- Review passes:
  - Scope/Evidence pass: изменения ограничены recorder overlay/hotkeys/session integration/tests/README/changelog/spec.
  - Contract pass: `MinimizeButton`, `RestoreButton`, `MinimizedPanel`, minimize events/state и runtime toggle удалены; public options сохранены как no-op.
  - Adversarial risk pass: проверен старый persisted `ToggleOverlayMinimize` key; active command map его игнорирует.
  - Re-review after fixes / Fix and re-review: после tests/build выполнены `rg` scan и diff review.
  - Stop decision: PASS для recorder scope; full-suite residual зафиксирован.
- Evidence inspected: `rg` references scan, diff по changed source/tests/docs, targeted test output, build output, full test output.
- Depth checklist:
  - Scope drift / unrelated changes: в `git status` только scoped files и текущая spec.
  - Acceptance criteria: выполнены; removed controls отсутствуют в source, tests проверяют отсутствие controls.
  - Validation evidence: targeted recorder tests PASS 86/86; solution build PASS; full solution test 306/307 PASS и 1 unrelated FlaUI failure.
  - Unsupported claims: нет; full-suite failure явно указан.
  - Regression / edge case: persisted deprecated hotkey covered by regression test.
  - Comments/docs/changelog: README и CHANGELOG обновлены; один compatibility comment оставлен у enum member.
  - Hidden contract change: public properties не удалены.
  - Manual-review challenge: проверил бы, что settings window rows строятся через `EnumerateCommands()` и больше не покажут overlay toggle.
- No-findings justification: recorder diff соответствует spec; единственный failing test вне recorder/minimize surface.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full solution test has unrelated persistent FlaUI failure `WaitUntilExists_DelayedStatusWithoutExistsWait_FailsOnTextAssertion`: expected not-found exception, got null. | Do not fix in recorder minimize scope; report residual. | accepted-risk |

- Fixed before final report: не требовалось.
- Checks rerun: targeted recorder tests, solution build, full solution test, failing FlaUI test targeted rerun.
- Validation evidence: `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -c Release` PASS 86/86; `dotnet build AppAutomation.sln -c Release` PASS; `dotnet test --solution AppAutomation.sln -c Release` FAIL 306/307 with unrelated FlaUI failure.
- Unrelated changes: нет.
- Needs human: нет для текущего scope.
- Residual risks / follow-ups: investigate/fix delayed-status FlaUI contrast test separately if full-suite green is required.

## Approval
Прямой пользовательский запрос: "Убрать из рекордера минимизацию, она всё равно не работает."

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор контекста и фиксация дизайна | 0.92 | Нет блокирующих данных | EXEC: правка overlay/hotkeys/tests/docs | Нет | Нет, прямой запрос пользователя принят как scope | Сохраняем public properties как no-op, чтобы убрать функцию без breaking API | `specs/2026-05-21-recorder-remove-minimize.md` |
| EXEC | Реализация удаления минимизации | 0.94 | Нет | Запустить targeted checks | Нет | Нет | Удалены minimize/restore controls, state, события и session binding; hotkey command исключен из active enumeration | `src/AppAutomation.Recorder.Avalonia/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Validation | 0.88 | Full suite имеет unrelated FlaUI residual | Выполнить post-EXEC review | Нет | Нет | Targeted recorder tests PASS 86/86, solution build PASS, full solution test FAIL 306/307 на existing delayed-status FlaUI contrast test | `AppAutomation.sln`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests`, `specs/2026-05-21-recorder-remove-minimize.md` |
| EXEC | Post-EXEC review | 0.9 | Нет | Финальный отчет | Нет | Нет | Diff/status/evidence проверены; recorder scope зеленый, unrelated full-suite failure задокументирован | `specs/2026-05-21-recorder-remove-minimize.md` |
