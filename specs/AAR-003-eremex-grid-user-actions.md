# AAR-003 Eremex Grid User Actions

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: AppAutomation
- Задача: `AAR-003`
- Масштаб: medium
- Целевой релиз / ветка: `feat/arm-paritet`
- Основание: `ControlSupportMatrix.md`, строки Arm.Srv по Eremex `DataGridControl` и grid behaviors
- Статус спеки: auto-approved в рамках `gap-resolution.md`
- Ограничения:
  - Не добавлять зависимость от Arm.Srv или Eremex в production packages.
  - Не пытаться угадать нативное UIA-дерево Eremex `DataGridControl`; runtime execution должен быть provider-neutral и явно диагностировать unsupported cases.
  - Не реализовывать in-grid editors; это отдельная задача `AAR-004`.
  - Не менять существующие row/cell assertion flows (`WaitUntilGridRowsAtLeast`, `WaitUntilGridCellEquals`).

## 1. Overview / Цель
Добавить в AppAutomation явные primitives для grid user actions, которые встречаются в Arm.Srv list pages: открыть строку, отсортировать колонку, доскроллить/загрузить ещё, скопировать ячейку и запустить export. Рекордер должен уметь сохранять эти действия из opt-in hints, а generated code должен компилироваться в authoring сценариях.

## 2. Текущее состояние (AS-IS)
- `IGridControl` поддерживает только чтение строк/ячеек.
- `UiPageExtensions` умеет только grid assertions: `WaitUntilGridRowsAtLeast` и `WaitUntilGridCellEquals`.
- Recorder `GridHint` мапит Eremex visual grid на bridge и умеет писать row/cell assertions.
- Recorder не имеет action kinds для grid user workflows и поэтому сохраняет export/copy/open/sort/load-more как generic clicks либо не сохраняет вовсе.
- Headless/FlaUI уже читают bridge rows/cells, но не заявляют generic support для Eremex grid gestures.

## 3. Проблема
Arm.Srv list-page workflows завязаны на пользовательские действия вокруг grid, а текущий authoring/runtime contract описывает только состояние таблицы. Без явных action primitives generated scenarios теряют intent, а unsupported Eremex gestures не имеют понятной диагностики.

## 4. Цели дизайна
- Ввести additive public API без breaking changes.
- Сохранить provider-neutral слой: recorder/runtime не зависят от Eremex.
- Сделать recorder opt-in через hints, чтобы не записывать случайные pointer events как grid actions.
- Для unsupported runtime execution выдавать диагностичный `UiOperationException`, а не silent no-op.
- Покрыть generated code и runtime diagnostics автоматическими тестами.

## 5. Non-Goals
- Не реализовывать provider-specific Eremex header/cell/scroll adapter.
- Не реализовывать OS clipboard/export folder dialogs.
- Не добавлять низкоуровневую mouse/keyboard DSL.
- Не менять Arm.Srv XAML и не генерировать bridge controls в consumer repo.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
| Компонент/файл | Ответственность |
| --- | --- |
| `AppAutomation.Abstractions` | Public `IGridUserActionControl` и fluent methods `OpenGridRow`, `SortGridByColumn`, `ScrollGridToEnd`, `CopyGridCell`, `ExportGrid` |
| `AppAutomation.Recorder.Avalonia` options/model | Opt-in `RecorderGridActionHint`, enum action hints и `RecordedActionKind` values |
| `RecorderStepFactory` | Сопоставить source locator с grid action hint, вычислить row/cell metadata где возможно, создать descriptor целевого `UiControlType.Grid` |
| `RecorderSession` | Сначала пытаться записать grid action для configured button/pointer/Enter actions, затем fallback на существующее поведение |
| `AuthoringCodeGenerator` | Генерировать `Page.<GridAction>(...)` statements |
| Tests | Проверить API, diagnostics, factory/session/codegen |

### 6.2 Детальный дизайн
Public runtime contract:

```csharp
public interface IGridUserActionControl : IGridControl
{
    void OpenRow(int rowIndex);
    void SortByColumn(string columnName);
    void ScrollToEnd();
    string CopyCell(int rowIndex, int columnIndex);
    void Export();
}
```

`UiPageExtensions` добавляет fluent methods:
- `OpenGridRow(selector, rowIndex, timeoutMs = 5000)`
- `SortGridByColumn(selector, columnName, timeoutMs = 5000)`
- `ScrollGridToEnd(selector, timeoutMs = 5000)`
- `CopyGridCell(selector, rowIndex, columnIndex, timeoutMs = 5000)`
- `ExportGrid(selector, timeoutMs = 5000)`

Если resolved grid не реализует `IGridUserActionControl`, method бросает `UiOperationException` с adapter id, page/control context и сообщением вида `Grid '<id>' does not support user action '<action>' in adapter '<adapter>'`.

Recorder contract:

```csharp
public enum RecorderGridUserActionKind
{
    OpenRow = 0,
    SortByColumn = 1,
    ScrollToEnd = 2,
    CopyCell = 3,
    Export = 4
}

public sealed record RecorderGridActionHint(
    string SourceLocatorValue,
    string TargetGridLocatorValue,
    RecorderGridUserActionKind ActionKind,
    UiLocatorKind SourceLocatorKind = UiLocatorKind.AutomationId,
    UiLocatorKind TargetGridLocatorKind = UiLocatorKind.AutomationId,
    bool TargetFallbackToName = false,
    string? ColumnName = null,
    int? RowIndex = null,
    int? ColumnIndex = null);
```

Recorder metadata rules:
- `OpenRow` requires row index from hint or grid row/cell context.
- `SortByColumn` requires `ColumnName` from hint or source display text.
- `CopyCell` requires row and column indexes from hint or grid cell context.
- `ScrollToEnd` and `Export` require only target grid locator.
- When metadata cannot be derived, recorder returns unsupported with explicit message and does not persist an invalid guessed step.

## 7. Бизнес-правила / Алгоритмы
- Grid action hint matching is exact by `SourceLocatorKind` + trimmed `SourceLocatorValue`.
- Matching walks source, visual parent, logical parent and templated parent chain, same as existing interaction owner logic.
- Existing `RecorderGridHint` and `ColumnPropertyNames` are reused to derive row/cell indexes from source `DataContext` and visual `_RowN_CellM` automation ids.
- Generated scenario stores explicit row/column/columnName values; no runtime lookup by transient selected row.

## 8. Точки интеграции и триггеры
- `RecorderSession.OnButtonClick`: configured grid action wins over generic `ClickButton`.
- `RecorderSession.OnPointerPressed`: configured non-button grid action can be recorded from a row/cell/header source.
- `RecorderSession.OnKeyDown`: Enter on a configured row/cell source can record `OpenGridRow`.
- `AuthoringCodeGenerator.GenerateStepStatement`: new action kinds render fluent grid methods.

## 9. Изменения модели данных / состояния
- Additive public types only.
- Internal `RecordedStep` reuses existing `StringValue`, `RowIndex`, `ColumnIndex`.
- No persisted storage changes outside generated source files.

## 10. Миграция / Rollout / Rollback
- Existing tests and generated scenarios continue to compile because all changes are additive.
- Without `RecorderGridActionHint`, recorder behavior is unchanged.
- Rollback: remove new hints/actions and generated methods; existing grid assertions remain unaffected.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- Runtime extension methods call `IGridUserActionControl` when supported.
- Runtime extension methods produce diagnostic `UiOperationException` when a grid lacks user-action support.
- Recorder factory creates all five grid action kinds from explicit hints.
- Recorder derives row/cell indexes for `OpenRow`/`CopyCell` from existing grid hint context.
- Code generator emits `Page.OpenGridRow`, `Page.SortGridByColumn`, `Page.ScrollGridToEnd`, `Page.CopyGridCell`, `Page.ExportGrid`.
- Existing grid assertion tests remain green.

Commands:
- `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj`
- `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj`
- `dotnet build .\AppAutomation.sln`
- `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release`
- `dotnet test --solution .\AppAutomation.sln --no-build`

## 12. Риски и edge cases
- Native Eremex UIA может не отдавать headers/rows/cells; this task does not hide that. Provider-specific support remains later work.
- Copy/export may require clipboard and dialogs; this task models the intent and diagnostics, not OS dialog automation.
- Header text can be localized; consumers should prefer explicit `ColumnName` in hints.
- Pointer recording is gated by hints to avoid noisy recordings.

## 13. План выполнения
1. Add `IGridUserActionControl` and fluent grid action methods with diagnostics.
2. Add recorder grid action options/model/action kinds.
3. Implement factory matching and metadata derivation.
4. Hook session button/pointer/Enter paths with configured-action-first behavior.
5. Add codegen statements and tests.
6. Run targeted tests, build, full tests, post-EXEC review.

## 14. Открытые вопросы
Нет блокирующих вопросов. Provider-specific Eremex gestures остаются явно вне scope.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - Поведение покрывается автоматическими тестами.
  - UI contract remains selector/automation-id based.
  - Unsupported runtime behavior returns diagnostics instead of silent failure.
  - Перед завершением запускаются `dotnet build` и `dotnet test`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Abstractions/UiControlContracts.cs` | Add `IGridUserActionControl` | Runtime action contract |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` | Add fluent grid actions and diagnostics | Replay API |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` | Add `GridActionHints` and public hint types | Recorder opt-in configuration |
| `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs` | Add grid action recorded kinds | Internal model |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs` | Create grid action steps | Recorder capture logic |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | Hook button/pointer/Enter capture | User event integration |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepValidator.cs` | Validate new action kinds | Review/persist safety |
| `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs` | Render fluent statements | Generated scenarios |
| `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` | Runtime API tests | Diagnostics and supported path |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | Recorder/codegen tests | Capture and generated output |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Grid replay | Только assertions | Explicit action API plus unsupported diagnostics |
| Recorder grid actions | Generic click/no capture | Opt-in typed recorded actions |
| Eremex provider support | Неявный gap | Явный unsupported boundary until provider adapter exists |

## 18. Альтернативы и компромиссы
- Вариант: сразу реализовать FlaUI mouse/keyboard gestures для Eremex.
- Плюсы: ближе к реальному Arm.Srv runtime.
- Минусы: требует runtime UIA проверки Arm.Srv и зависит от Eremex internals.
- Почему выбранное решение лучше: создаёт стабильный authoring/recorder contract сейчас и не маскирует отсутствие provider-specific gesture support.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals и Non-Goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | API, recorder flow, migration и rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Additive API, opt-in recorder, no Eremex dependency |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, commands and file table present |
| E. Готовность к автономной реализации | 17-19 | PASS | Tradeoff and quality gate documented |
| F. Соответствие профилю | 20 | PASS | .NET desktop/UI automation requirements captured |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Пять actions и non-goals перечислены |
| 2. Понимание текущего состояния | 5 | Existing grid assertions/bridge/recorder gaps описаны |
| 3. Конкретность целевого дизайна | 5 | Public API, hints and generation rules заданы |
| 4. Безопасность (миграция, откат) | 5 | Additive opt-in behavior and rollback описаны |
| 5. Тестируемость | 5 | Targeted and full commands plus criteria есть |
| 6. Готовность к автономной реализации | 5 | Нет открытых вопросов |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: scope ограничен typed API/recorder hints/diagnostics; provider-specific Eremex gestures вынесены из задачи.
- Что осталось на решение пользователя: ничего; пользователь разрешил auto-approval для `gap-resolution.md`.

## Approval
Спека считается подтверждённой автоматически по разрешению пользователя для `gap-resolution.md`.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Спецификация AAR-003 | 0.86 | Runtime UIA Arm.Srv/Eremex не проверялось | EXEC implementation | Нет | Нет, auto-approval разрешён | Выбран additive provider-neutral contract с явными unsupported diagnostics | `tasks.md`, `specs/AAR-003-eremex-grid-user-actions.md` |
| EXEC | Реализация AAR-003 | 0.9 | Runtime UIA Arm.Srv/Eremex не проверялось | Commit AAR-003 | Нет | Нет | Добавлены typed grid actions, opt-in recorder hints, generated code и explicit unsupported boundary для runtimes без action adapter | `src/AppAutomation.Abstractions/UiControlContracts.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderModels.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderStepValidator.cs`, `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |

## 21. EXEC Verification
| Команда | Результат |
| --- | --- |
| `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj --no-restore` | PASS, 25/25 |
| `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj --no-restore` | PASS, 38/38 |
| `dotnet build .\AppAutomation.sln` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet test --solution .\AppAutomation.sln --no-build` | PASS, 170/170 |
| `git diff --check -- <AAR-003 files>` | PASS; Git warned only about future CRLF normalization |

## 22. Post-EXEC Review
- Статус: PASS.
- Что проверено: public grid user action API is additive; recorder grid action hints are opt-in and exact-match; row/cell derivation reuses existing grid hint context; generated code emits five fluent grid methods; unsupported runtime path reports `UiOperationException` instead of silent no-op.
- Остаточный риск: Headless/FlaUI do not yet implement provider-specific Eremex gestures; consumer/provider must implement `IGridUserActionControl` or an adapter for actual execution. This is the intended boundary for AAR-003.
