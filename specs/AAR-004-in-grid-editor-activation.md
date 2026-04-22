# AAR-004 In-Grid Editor Activation

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: AppAutomation
- Задача: `AAR-004`
- Масштаб: medium
- Целевой релиз / ветка: `feat/arm-paritet`
- Основание: `ControlSupportMatrix.md`, gap по Eremex grid cell editors
- Статус спеки: auto-approved в рамках `gap-resolution.md`
- Ограничения:
  - Не добавлять dependency на Arm.Srv или Eremex.
  - Не реализовывать recorder capture/codegen для grid edit в этом task.
  - Не эмулировать FlaUI mouse/keyboard gestures без runtime UIA проверки Arm.Srv/Eremex.
  - Не ломать существующие grid read/assertion flows и AAR-003 grid user actions.

## 1. Overview / Цель
Добавить provider-neutral API для редактирования ячейки grid и реализовать поддерживаемый путь в headless runtime для visual-grid bridge ячеек с stable automation id вида `<GridAutomationId>_RowN_CellM`. Сценарии должны покрывать text, spin/number, date, combo и server-search как typed editor kinds, а неподдерживаемые runtime должны получать явную диагностику.

## 2. Текущее состояние (AS-IS)
- `IGridControl` умеет читать строки и ячейки.
- AAR-003 добавил user actions вокруг grid, но не редактирование cell editors.
- Headless visual grid читает bridge rows/cells по automation id.
- FlaUI visual grid читает bridge rows/cells, но не имеет проверенного способа activation/commit Eremex editors.
- Existing page extensions могут редактировать standalone `TextBox`, `Spinner`, `DateTimePicker`, `ComboBox`, `SearchPicker`, но не editor внутри grid cell.

## 3. Проблема
Arm.Srv использует Eremex in-grid editors для изменения значений прямо в строках таблиц. Без typed edit-cell abstraction сценарии вынуждены обращаться к внутренним visual controls или низкоуровневым gestures, что нестабильно и не отражает intent.

## 4. Цели дизайна
- Добавить additive public contract без breaking changes.
- Представить editor kind явно: `Text`, `Number`, `Date`, `ComboBox`, `SearchPicker`.
- Поддержать `Commit` и `Cancel` как часть request.
- В headless поддержать visual-grid bridge cells через automation id и text-like/editor descendants.
- В unsupported runtimes выдавать `UiOperationException` с context и причиной.

## 5. Non-Goals
- Не реализовывать настоящие Eremex runtime gestures в FlaUI.
- Не менять recorder model/action kinds.
- Не добавлять validation правил по бизнес-типам колонок Arm.Srv.
- Не автоматизировать popup dialogs server-search вне самой ячейки.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Public abstractions
Добавить в `AppAutomation.Abstractions`:

```csharp
public enum GridCellEditorKind
{
    Text = 0,
    Number = 1,
    Date = 2,
    ComboBox = 3,
    SearchPicker = 4
}

public enum GridCellEditCommitMode
{
    Commit = 0,
    Cancel = 1
}

public sealed record GridCellEditRequest(
    int RowIndex,
    int ColumnIndex,
    string Value,
    GridCellEditorKind EditorKind = GridCellEditorKind.Text,
    GridCellEditCommitMode CommitMode = GridCellEditCommitMode.Commit,
    string? SearchText = null);

public interface IEditableGridControl : IGridControl
{
    void EditCell(GridCellEditRequest request);
}
```

### 6.2 Fluent API
Добавить `UiPageExtensions`:
- `EditGridCell(...)` generic method.
- `EditGridCellText(...)`.
- `EditGridCellNumber(...)` with invariant number formatting.
- `EditGridCellDate(...)` with `yyyy-MM-dd` formatting.
- `SelectGridCellComboItem(...)`.
- `SearchAndSelectGridCell(...)`.

Все методы валидируют row/column/value, вызывают `IEditableGridControl.EditCell`, затем проверяют outcome:
- `Commit`: cell value equals requested `Value`.
- `Cancel`: cell value remains equal to value before edit.

Если resolved grid не реализует `IEditableGridControl`, extension wraps `NotSupportedException` in `UiOperationException`.

### 6.3 Headless provider implementation
`HeadlessVisualGridControl` реализует `IEditableGridControl`.

Алгоритм:
1. Найти visual cell descendant по automation id prefix `<AutomationId>_Row{rowIndex}_Cell{columnIndex}`.
2. Для `Cancel` только проверить, что cell exists, и не менять value.
3. Для `Commit` записать значение в первый подходящий control:
   - `TextBox.Text`.
   - `TextBlock.Text`.
   - `Label.Content`.
   - `ComboBox.SelectedItem/SelectedIndex` по text match.
   - `DatePicker.SelectedDate` для `Date`.
4. Если typed editor не найден, использовать text-like fallback (`TextBlock`/`Label`) внутри cell.
5. Если value cannot be applied, throw `InvalidOperationException` with row/column/editor context.

### 6.4 FlaUI boundary
`FlaUiGridControl` и `FlaUiVisualGridControl` не реализуют `IEditableGridControl` в этом task. Public extension поэтому возвращает diagnostics through `UiOperationException`. Это честнее, чем mouse/keyboard gestures без проверки реального Eremex UIA.

## 7. Бизнес-правила / Алгоритмы
- Row/column indexes are zero-based.
- `SearchPicker` stores selected item in `Value`; `SearchText` is optional and is preserved for future providers.
- `Number` uses invariant culture.
- `Date` uses `yyyy-MM-dd` in generated/request value.
- `Cancel` must not mutate observed cell value.
- Unsupported provider/cell/editor cases fail loudly.

## 8. Точки интеграции и триггеры
- `UiPageExtensions` is the only public execution entry point.
- `HeadlessControlResolver.ResolveGrid` returns `HeadlessVisualGridControl` for non-native grid bridge controls; that class becomes editable.
- Existing `WaitUntilGridCellEquals` remains unchanged and is reused for post-edit checks conceptually.

## 9. Изменения модели данных / состояния
- Additive public enum/record/interface only.
- No persisted recorder scenario model changes.
- No manifest schema changes.

## 10. Миграция / Rollout / Rollback
- Existing consumers keep compiling because API additions are additive.
- Headless visual-grid bridge gains new capability automatically.
- Rollback removes new abstractions/extensions/headless implementation/tests; existing read-only grid behavior remains intact.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- Public API calls `IEditableGridControl.EditCell` with correct request metadata.
- Public API throws diagnostic `UiOperationException` when runtime does not support editable grids.
- Public API supports commit and cancel semantics in tests.
- Headless visual-grid bridge can edit a cell by `_RowN_CellM` automation id and post-read returns committed value.
- Headless visual-grid cancel leaves the original value unchanged.
- Existing AAR-003 grid user action tests remain green.

Commands:
- `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj`
- `dotnet test --project .\tests\AppAutomation.TestHost.Avalonia.Tests\AppAutomation.TestHost.Avalonia.Tests.csproj`
- `dotnet build .\AppAutomation.sln`
- `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release`
- `dotnet test --solution .\AppAutomation.sln --no-build`

## 12. Риски и edge cases
- Visual bridge may expose text-only cells, not real active editors; headless support writes display/editor controls rather than proving Eremex gesture parity.
- FlaUI remains runtime-only gap until real UIA tree can be verified.
- Date/number formatting may differ in consumer UI; public API uses deterministic invariant values.
- Virtualized rows not present in visual tree cannot be edited by headless visual path and must fail explicitly.

## 13. План выполнения
1. Add public edit-cell request types and `IEditableGridControl`.
2. Add fluent edit-cell methods and diagnostics in `UiPageExtensions`.
3. Implement headless visual-grid edit support for text-like/editor descendants.
4. Add abstraction tests for request/diagnostics/commit/cancel.
5. Add headless integration tests for commit/cancel on bridge visual cells.
6. Run targeted tests, build, full tests, post-EXEC review.

## 14. Открытые вопросы
Нет блокирующих вопросов. FlaUI Eremex gesture implementation remains explicitly out of scope.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - Public API is selector/automation-id based.
  - Unsupported runtime behavior returns diagnostics.
  - Headless behavior is covered by automated tests.
  - Build/test gates are defined.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Abstractions/UiControlContracts.cs` | Add edit-cell enums/request/interface | Runtime contract |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` | Add fluent edit-cell methods and diagnostics | Replay API |
| `src/AppAutomation.Avalonia.Headless/Automation/HeadlessControlResolver.cs` | Implement editable visual-grid bridge support | Headless provider path |
| `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` | API/diagnostic tests | Contract coverage |
| `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs` | Headless visual-grid edit tests | Provider coverage |
| `tasks.md` | Track AAR-004 state | Workflow idempotency |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Grid edit API | Нет | `IEditableGridControl` + fluent methods |
| Headless visual grid | Read-only bridge cells | Edit/cancel visual cells by stable row/cell ids |
| FlaUI visual grid | Read-only bridge cells | Explicit unsupported diagnostics for edit-cell |

## 18. Альтернативы и компромиссы
- Вариант: сразу реализовать FlaUI double-click/type/enter gestures.
- Плюсы: ближе к реальному desktop runtime.
- Минусы: высокий риск flaky behavior без проверки Arm.Srv/Eremex UIA tree.
- Выбранное решение: закрепляет contract и добавляет проверяемый headless путь, не скрывая остаточный FlaUI gap.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals and non-goals есть |
| B. Качество дизайна | 6-10 | PASS | API, headless provider, migration and rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Additive API, scoped provider implementation, no Eremex dependency |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, commands and file table present |
| E. Готовность к автономной реализации | 17-19 | PASS | Tradeoff and unsupported boundary documented |
| F. Соответствие профилю | 20 | PASS | .NET desktop/UI automation requirements captured |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Headless edit path and FlaUI boundary explicit |
| 2. Понимание текущего состояния | 5 | Existing read-only grid and AAR-003 context captured |
| 3. Конкретность целевого дизайна | 5 | Public types, fluent API and provider algorithm defined |
| 4. Безопасность (миграция, откат) | 5 | Additive only, rollback clear |
| 5. Тестируемость | 5 | Contract and headless provider tests listed |
| 6. Готовность к автономной реализации | 5 | No blocking questions |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS.
- Что исправлено: scope ограничен runtime contract/headless implementation; recorder and unverified FlaUI gestures left out of task.
- Что осталось на решение пользователя: ничего; пользователь разрешил auto-approval для `gap-resolution.md`.

## Approval
Спека считается подтверждённой автоматически по разрешению пользователя для `gap-resolution.md`.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Спецификация AAR-004 | 0.84 | Runtime UIA Arm.Srv/Eremex не проверялось; FlaUI gesture path не подтверждён | EXEC implementation | Нет | Нет, auto-approval разрешён | Выбран provider-neutral contract плюс проверяемый headless visual-grid path и явный unsupported boundary для FlaUI | `tasks.md`, `specs/AAR-004-in-grid-editor-activation.md` |
| EXEC | Реализация AAR-004 | 0.88 | Runtime UIA Arm.Srv/Eremex не проверялось; FlaUI gesture path не подтверждён | Commit AAR-004 | Нет | Нет | Добавлен edit-cell contract, fluent API, headless visual-grid commit/cancel support and explicit unsupported diagnostics for non-editable grids | `src/AppAutomation.Abstractions/UiControlContracts.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Avalonia.Headless/Automation/HeadlessControlResolver.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs`, `tasks.md` |

## 21. EXEC Verification
| Команда | Результат |
| --- | --- |
| `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj` | PASS, 28/28 |
| `dotnet test --project .\tests\AppAutomation.TestHost.Avalonia.Tests\AppAutomation.TestHost.Avalonia.Tests.csproj --no-restore` | PASS, 13/13 |
| `dotnet build .\AppAutomation.sln --no-restore` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release --no-restore` | PASS, 0 errors; existing NU1903 warning |
| `dotnet test --solution .\AppAutomation.sln --no-build` | PASS, 176/176 |
| `git diff --check -- <AAR-004 files>` | PASS; Git warned only about future CRLF normalization |

## 22. Post-EXEC Review
- Статус: PASS.
- Что проверено: public edit-cell API is additive; `EditGridCell*` methods produce typed requests and verify commit/cancel outcome; unsupported grids are wrapped into `UiOperationException`; headless visual-grid bridge can commit text/date/combo values by `_RowN_CellM` automation ids and cancel without mutation.
- Остаточный риск: FlaUI still does not implement `IEditableGridControl`; real Eremex activation/commit gestures require runtime UIA validation and remain a scoped follow-up.
