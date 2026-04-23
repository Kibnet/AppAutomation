# AAR-005 Popup Date And Range Filters

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: AppAutomation
- Задача: `AAR-005`
- Масштаб: medium
- Целевой релиз / ветка: `feat/arm-paritet`
- Основание: `ControlSupportMatrix.md`, gap по `DateRangeFilterControl`, `RangeFromToControl`, Eremex `DateEditor` / `SpinEditor`
- Статус спеки: auto-approved в рамках `gap-resolution.md`
- Ограничения:
  - Не добавлять dependency на Arm.Srv или Eremex.
  - Не реализовывать raw mouse/keyboard gestures для Eremex popup без runtime UIA проверки.
  - Не менять persisted recorder scenario model в этом task.
  - Не трогать `ControlSupportMatrix.md` до AAR-008.

## 1. Overview / Цель
Добавить provider-neutral поддержку popup date/range filters как композитных контролов, собранных из стабильных частей: open trigger, from/to editors, apply and cancel buttons. Сценарии должны задавать date range и numeric range через typed fluent API, а headless/FlaUI должны переиспользовать уже существующие primitive controls (`DateTimePicker`, `Spinner`, `TextBox`, `Button`) вместо ad hoc interactions.

## 2. Текущее состояние (AS-IS)
- Есть standalone API для `DateTimePicker`, `Spinner`, spinner-like `TextBox` и `Button`.
- AAR-001 позволяет recorder hints маппить custom controls на intended `UiControlType`.
- AAR-002 добавил пример композитного control adapter (`SearchPickerParts` + `WithSearchPicker`).
- Нет typed abstraction для `DateRangeFilterControl` / `RangeFromToControl`; сценарии вынуждены вручную кликать popup parts.

## 3. Проблема
Arm.Srv использует popup filters с from/to date или numeric values. Без typed composite API replay не выражает пользовательский intent: "открыть фильтр, задать диапазон, применить/отменить". Это усложняет поддержку recorder/headless/FlaUI и повышает хрупкость локаторов.

## 4. Цели дизайна
- Добавить additive public contracts для date range и numeric range filter controls.
- Повторить паттерн `SearchPicker`: parts records + resolver adapters by page property name.
- Поддержать lazy resolution popup parts после `Open`, потому popup содержимое может появляться только после открытия.
- Поддержать editor kinds:
  - date: `DateTimePicker` или text editor with `yyyy-MM-dd`;
  - numeric: `Spinner` или text editor with invariant number.
- Поддержать explicit `Apply` and `Cancel` commit modes.
- Дать `UiPageExtensions` helpers с diagnostics через `UiOperationException`.

## 5. Non-Goals
- Не записывать новые recorder step kinds для range filter gestures.
- Не реализовывать Eremex-specific runtime adapter.
- Не делать clear/open-ended semantics для nullable bounds; `null` means "leave this bound unchanged".
- Не менять matrix documentation до dedicated AAR-008.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Public abstractions
Добавить в `AppAutomation.Abstractions`:

```csharp
public enum FilterPopupCommitMode
{
    Apply = 0,
    Cancel = 1
}

public enum FilterValueEditorKind
{
    TextBox = 0,
    DateTimePicker = 1,
    Spinner = 2
}

public sealed record DateRangeFilterRequest(
    DateTime? From,
    DateTime? To,
    FilterPopupCommitMode CommitMode = FilterPopupCommitMode.Apply);

public sealed record NumericRangeFilterRequest(
    double? From,
    double? To,
    FilterPopupCommitMode CommitMode = FilterPopupCommitMode.Apply);

public interface IDateRangeFilterControl : IUiControl
{
    DateTime? FromValue { get; }
    DateTime? ToValue { get; }
    void Open();
    void SetRange(DateRangeFilterRequest request);
}

public interface INumericRangeFilterControl : IUiControl
{
    double? FromValue { get; }
    double? ToValue { get; }
    void Open();
    void SetRange(NumericRangeFilterRequest request);
}
```

### 6.2 UiControlType and authoring
Добавить:
- `UiControlType.DateRangeFilter = 25`
- `UiControlType.NumericRangeFilter = 26`

Обновить source generator mapping:
- `DateRangeFilter` -> `IDateRangeFilterControl`
- `NumericRangeFilter` -> `INumericRangeFilterControl`

Обновить legacy FlaUI `PageObjects.UiControlType` enum для numeric parity.

### 6.3 Composite adapter parts
Добавить records:
- `DateRangeFilterParts`
- `NumericRangeFilterParts`

Обе конфигурации включают:
- `FromLocator`
- `ToLocator`
- `ApplyButtonLocator`
- `CancelButtonLocator`
- optional `OpenButtonLocator`
- common `LocatorKind`
- `FallbackToName`
- `EditorKind`

Static helper:
- `ByAutomationIds(...)`.

Добавить resolver extensions:
- `WithDateRangeFilter(propertyName, parts)`
- `WithNumericRangeFilter(propertyName, parts)`

### 6.4 Adapter behavior
Adapter:
1. Intercepts only matching property name and requested filter interface.
2. Returns lazy composite control without resolving popup inner parts immediately.
3. `Open()` invokes optional open button; if no open button is configured, it is a no-op.
4. `SetRange(request)`:
   - validates request;
   - calls `Open()`;
   - writes non-null `From` and `To` bounds;
   - invokes `Apply` or `Cancel` button based on commit mode.
5. Text date format is `yyyy-MM-dd` with invariant culture.
6. Numeric text format is `G17` with invariant culture.

### 6.5 Fluent API
Добавить `UiPageExtensions`:
- `SetDateRangeFilter(selector, from, to, commitMode, timeoutMs)`.
- `SetNumericRangeFilter(selector, from, to, commitMode, timeoutMs)`.

Rules:
- Wait until filter control is enabled before operation.
- Wrap unsupported/misconfigured adapter errors into `UiOperationException`.
- For `Apply`, verify non-null requested bounds are visible through the control after setting.
- For `Cancel`, do not require post-state equality because popup implementations can close or revert differently; completion means the configured cancel action was invoked without provider error.

## 7. Бизнес-правила / Алгоритмы
- Bounds are optional per side; `null` leaves the corresponding editor untouched.
- `Apply` and `Cancel` buttons are required parts.
- `OpenButtonLocator` is optional to support already-open popup roots or popup controls opened by surrounding flow.
- Date text is deterministic: `yyyy-MM-dd`.
- Numeric text is deterministic invariant `G17`.
- Unsupported editor kind for a filter type fails explicitly.

## 8. Точки интеграции и триггеры
- `UiControlAdapters.cs`: parts + adapters + resolver extensions.
- `UiControlContracts.cs`: new public request/interface types.
- `UiPageExtensions.cs`: fluent operations and diagnostics.
- `UiControlType.cs` + `UiControlSourceGenerator.cs`: authoring support.
- No direct provider changes are required; headless/FlaUI use existing primitive resolvers.

## 9. Изменения модели данных / состояния
- Additive enum/interface/record changes.
- Manifest contract remains version `1`; generated controls can now include new control types.
- No recorder persisted model changes.

## 10. Миграция / Rollout / Rollback
- Existing consumers keep compiling because changes are additive.
- New consumers opt in by annotating generated page controls or manually declaring properties and configuring resolver adapters.
- Rollback removes new contracts/adapters/extensions/generator mapping/tests; existing primitive controls remain unaffected.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- Date range adapter opens popup, writes from/to date values and invokes apply.
- Date range adapter supports cancel path and invokes cancel.
- Numeric range adapter writes spinner values and invokes apply.
- Numeric range adapter supports text editor fallback.
- Page extension wraps unsupported/misconfigured filter operation into `UiOperationException`.
- Source generator emits accessors for `DateRangeFilter` and `NumericRangeFilter`.

Commands:
- `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj`
- `dotnet test --project .\tests\AppAutomation.Authoring.Tests\AppAutomation.Authoring.Tests.csproj`
- `dotnet build .\AppAutomation.sln --no-restore`
- `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release --no-restore`
- `dotnet test --solution .\AppAutomation.sln --no-build`

## 12. Риски и edge cases
- Real Eremex `DateEditor` / `SpinEditor` may expose UIA patterns differently; this task relies on stable primitive mapping or text fallback, not raw Eremex gestures.
- Popup content may be virtualized or absent until open; lazy resolution mitigates this but cannot help if popup parts have no stable automation ids.
- Cancel outcome differs across UIs; this task guarantees invocation of configured cancel path, not provider-specific popup state rollback.
- Numeric parsing from text fallback returns `null` for empty/unparseable values.

## 13. План выполнения
1. Add public filter request/interface enums and control type values.
2. Add `DateRangeFilterParts`, `NumericRangeFilterParts`, adapters and resolver extension methods.
3. Add fluent page extension methods with diagnostics.
4. Update source generator and legacy FlaUI enum.
5. Add abstraction adapter/page tests and authoring generator tests.
6. Run targeted tests/build/full tests and post-EXEC review.

## 14. Открытые вопросы
Нет блокирующих вопросов. Пользователь разрешил auto-approval; Eremex-specific FlaUI gestures remain out of scope until runtime UIA tree is available.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - Public API is selector/automation-id based.
  - Unsupported/misconfigured runtime behavior returns diagnostics.
  - Provider dependencies are avoided.
  - Automated tests cover supported behavior.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Abstractions/UiControlContracts.cs` | Add filter enums, requests and interfaces | Runtime contract |
| `src/AppAutomation.Abstractions/UiControlType.cs` | Add `DateRangeFilter`, `NumericRangeFilter` | Authoring contract |
| `src/AppAutomation.Abstractions/UiControlAdapters.cs` | Add filter parts/adapters/extensions | Composite support |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` | Add fluent filter helpers and diagnostics | Replay API |
| `src/AppAutomation.Authoring/UiControlSourceGenerator.cs` | Map new control types to interfaces | Generated page support |
| `src/AppAutomation.FlaUI/PageObjects/UiControlType.cs` | Keep enum parity | Legacy page object parity |
| `tests/AppAutomation.Abstractions.Tests/UiControlAdapterTests.cs` | Composite adapter tests | Adapter coverage |
| `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` | Fluent API diagnostics tests | Contract coverage |
| `tests/AppAutomation.Authoring.Tests/UiControlSourceGeneratorTests.cs` | Generator mapping tests | Authoring coverage |
| `tasks.md` | Track AAR-005 state | Workflow idempotency |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Date range popup | Manual primitive actions | `IDateRangeFilterControl` + `WithDateRangeFilter` + `SetDateRangeFilter` |
| Numeric range popup | Manual primitive actions | `INumericRangeFilterControl` + `WithNumericRangeFilter` + `SetNumericRangeFilter` |
| Popup part resolution | Eager primitive resolution in scenarios | Lazy adapter resolution after open |
| Source generation | No typed filter property | Generated typed filter accessors |

## 18. Альтернативы и компромиссы
- Вариант: реализовать Eremex-specific FlaUI gestures.
- Плюсы: ближе к реальному desktop behavior.
- Минусы: высокий риск flaky behavior без Arm.Srv/Eremex UIA проверки и hard dependency на vendor internals.
- Выбранное решение: закрепляет provider-neutral typed contract and composition model; runtime-specific gesture work can be added later without changing tests that use the high-level API.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals and non-goals есть |
| B. Качество дизайна | 6-10 | PASS | API, adapters, generator and rollout described |
| C. Безопасность изменений | 11-13 | PASS | Additive API, no provider/vendor dependency |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, commands and file table present |
| E. Готовность к автономной реализации | 17-19 | PASS | Tradeoff and unsupported boundary documented |
| F. Соответствие профилю | 20 | PASS | .NET desktop/UI automation requirements captured |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Composite filters and non-goals explicit |
| 2. Понимание текущего состояния | 5 | Primitive support, hints and SearchPicker pattern captured |
| 3. Конкретность целевого дизайна | 5 | Public types, parts, adapters and fluent API defined |
| 4. Безопасность (миграция, откат) | 5 | Additive only, rollback clear |
| 5. Тестируемость | 5 | Adapter, page extension and generator tests listed |
| 6. Готовность к автономной реализации | 5 | No blocking questions |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS.
- Что исправлено: scope ограничен provider-neutral adapter/helper implementation; recorder model and unverified Eremex gestures left out of task.
- Что осталось на решение пользователя: ничего; пользователь разрешил auto-approval для `gap-resolution.md`.

## Approval
Спека считается подтверждённой автоматически по разрешению пользователя для `gap-resolution.md`.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Спецификация AAR-005 | 0.84 | Runtime UIA Arm.Srv/Eremex не проверялось; exact popup tree unknown | EXEC implementation | Нет | Нет, auto-approval разрешён | Выбран паттерн composite adapters поверх primitive controls, потому он уже используется для `SearchPicker` and avoids vendor dependency | `tasks.md`, `specs/AAR-005-popup-date-range-filters.md` |
| EXEC | Реализация AAR-005 | 0.88 | Runtime UIA Arm.Srv/Eremex не проверялось; exact popup tree unknown | Commit AAR-005 | Нет | Нет | Добавлены typed date/numeric range filter contracts, lazy composite adapters, fluent helpers and source-generator mapping without vendor dependency | `src/AppAutomation.Abstractions/UiControlContracts.cs`, `src/AppAutomation.Abstractions/UiControlType.cs`, `src/AppAutomation.Abstractions/UiControlAdapters.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Authoring/UiControlSourceGenerator.cs`, `src/AppAutomation.FlaUI/PageObjects/UiControlType.cs`, `tests/AppAutomation.Abstractions.Tests/UiControlAdapterTests.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `tests/AppAutomation.Authoring.Tests/UiControlSourceGeneratorTests.cs`, `tasks.md` |

## 21. EXEC Verification
| Команда | Результат |
| --- | --- |
| `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj` | PASS, 35/35 |
| `dotnet test --project .\tests\AppAutomation.Authoring.Tests\AppAutomation.Authoring.Tests.csproj --no-restore` | PASS, 2/2 |
| `dotnet build .\AppAutomation.sln --no-restore` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release --no-restore` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet test --solution .\AppAutomation.sln --no-build` | PASS, 183/183 |
| `git diff --check -- <AAR-005 files>` | PASS; Git warned only about future CRLF normalization |

## 22. Post-EXEC Review
- Статус: PASS.
- Что проверено: public API is additive; new `UiControlType` values map to generated `IDateRangeFilterControl` and `INumericRangeFilterControl`; adapters lazily resolve popup parts after open; date filters support `DateTimePicker` and text editor paths; numeric filters support `Spinner` and text editor paths; fluent helpers wrap failures into `UiOperationException` and support apply/cancel semantics.
- Остаточный риск: real Eremex `DateEditor` / `SpinEditor` UIA exposure is still runtime-dependent; this task provides stable high-level composition over primitive mappings and leaves vendor-specific gestures out of scope.
