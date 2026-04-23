# AAR-007 Shell Docking Navigation

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: AppAutomation
- Задача: `AAR-007`
- Масштаб: medium
- Целевой релиз / ветка: `feat/arm-paritet`
- Основание: `ControlSupportMatrix.md`, gap по Eremex docking shell
- Статус спеки: auto-approved в рамках `gap-resolution.md`
- Ограничения:
  - Не добавлять dependency на Arm.Srv или Eremex docking internals.
  - Не реализовывать raw dock drag/drop, floating windows, pin/unpin and layout persistence.
  - Не менять recorder model/action kinds в этом task.
  - Не трогать `ControlSupportMatrix.md` до dedicated AAR-008.

## 1. Overview / Цель
Добавить typed workflow для shell/docking navigation: открыть бизнес-страницу по стабильному navigation anchor и активировать уже открытую pane/tab. API должен выражать intent `open`, `activate`, `open-or-activate` через provider-neutral shell control, который можно собрать из существующих primitive controls (`Tree`, selectable `ListBox`, `Tab`, `Label`). Headless/FlaUI получают поддерживаемый путь через уже существующие primitive adapters или explicit diagnostics, если shell не экспонирует стабильные anchors.

## 2. Текущее состояние (AS-IS)
- Есть `ITabControl`, `ITabItemControl`, `ITreeControl` and page helpers `SelectTabItem`, `SelectTreeItem`.
- Recorder already records tab/tree selection for standard Avalonia controls.
- Headless/FlaUI support tab/tree selection for the sample app where stable automation ids exist.
- Нет единого high-level shell/docking abstraction для сценария "открыть/переключить бизнес-страницу", а Eremex docking shell нельзя безопасно автоматизировать через vendor gestures без runtime UIA tree.

## 3. Проблема
Arm.Srv открывает рабочие страницы через docking shell. Если тесты используют только отдельные tab/tree clicks, матрица поддержки не различает provider-neutral supported path and unverified Eremex docking internals. Нужен typed API, который фиксирует user intent и выдаёт явную диагностику для runtime, где нет стабильных anchors.

## 4. Цели дизайна
- Добавить additive public shell navigation contract.
- Поддержать modes: `Open`, `Activate`, `OpenOrActivate`.
- Поддержать navigation source kinds: tree, selectable list box, tab control.
- Поддержать optional pane tabs for activation and open-pane enumeration.
- Поддержать optional active pane label for verification.
- Дать `UiPageExtensions` helpers with `UiOperationException` diagnostics.
- Обновить authoring/source generator and legacy FlaUI enum parity.

## 5. Non-Goals
- Не автоматизировать Eremex docking drag/drop/floating/pinning.
- Не добавлять provider-specific runtime adapter for Eremex docking in this task.
- Не менять existing tab/tree/list helpers.
- Не добавлять recorder capture/codegen for shell navigation.
- Не обновлять support matrix до AAR-008.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Public abstractions
Добавить в `AppAutomation.Abstractions`:

```csharp
public enum ShellPaneNavigationMode
{
    OpenOrActivate = 0,
    Open = 1,
    Activate = 2
}

public enum ShellNavigationSourceKind
{
    Tree = 0,
    ListBox = 1,
    Tab = 2
}

public sealed record ShellPaneNavigationRequest(
    string PaneName,
    ShellPaneNavigationMode Mode = ShellPaneNavigationMode.OpenOrActivate);

public interface IShellNavigationControl : IUiControl
{
    string? ActivePaneName { get; }
    IReadOnlyList<string> OpenPaneNames { get; }
    void OpenOrActivate(ShellPaneNavigationRequest request);
}
```

### 6.2 UiControlType and authoring
Добавить:
- `UiControlType.ShellNavigation = 30`

Обновить source generator mapping:
- `ShellNavigation` -> `IShellNavigationControl`

Обновить legacy FlaUI `PageObjects.UiControlType` enum для numeric parity.

### 6.3 Composite adapter parts
Добавить `ShellNavigationParts`:
- `NavigationLocator`: required source for opening pages.
- `PaneTabsLocator`: optional tab control for already-open panes.
- `ActivePaneLabelLocator`: optional label with current active pane title.
- `NavigationKind`: tree/list box/tab.
- `LocatorKind`, `FallbackToName`.

Static helper:
- `ByAutomationIds(navigationAutomationId, paneTabsAutomationId = null, activePaneLabelAutomationId = null, navigationKind = Tree)`.

Resolver extension:
- `WithShellNavigation(propertyName, parts)`.

### 6.4 Adapter behavior
Adapter:
1. Intercepts only matching property name and requested `IShellNavigationControl`.
2. Lazily resolves parts.
3. `OpenPaneNames` returns pane tab item names when `PaneTabsLocator` is configured, otherwise empty list.
4. `ActivePaneName` uses active pane label when configured; otherwise selected pane tab name when pane tabs exist; otherwise `null`.
5. `Open` selects `PaneName` through `NavigationLocator`.
6. `Activate` selects `PaneName` through `PaneTabsLocator`; missing pane tabs throws `NotSupportedException`.
7. `OpenOrActivate` activates an already-open matching pane tab when found; otherwise opens through navigation source.
8. Tree navigation uses `ITreeControl` and recursive selection by text/name.
9. List navigation requires `ISelectableListBoxControl`; otherwise throws `NotSupportedException`.
10. Tab navigation uses `ITabControl.SelectTabItem`.

### 6.5 Fluent API
Добавить `UiPageExtensions`:
- `OpenShellPane(selector, paneName, timeoutMs)`.
- `ActivateShellPane(selector, paneName, timeoutMs)`.
- `OpenOrActivateShellPane(selector, paneName, timeoutMs)`.
- General `NavigateShellPane(selector, paneName, mode, timeoutMs)`.

Rules:
- Validate `paneName` early.
- Wait until shell control is enabled before navigation.
- Wrap unsupported/misconfigured adapter errors into `UiOperationException`.
- After navigation, verify success when `ActivePaneName` or `OpenPaneNames` are available:
  - active pane matches normalized `paneName`; or
  - open panes contain normalized `paneName`.
- If neither active nor open panes are observable, completion means the configured navigation action returned without provider error.

## 7. Бизнес-правила / Алгоритмы
- Pane matching is case-insensitive and uses the same alphanumeric normalization style as existing tab/tree helpers.
- `Open` never tries to activate pane tabs first.
- `Activate` never falls back to navigation source; missing tab is unsupported.
- `OpenOrActivate` prefers already-open pane tabs to avoid duplicate pages.
- Empty/whitespace pane names are invalid.

## 8. Точки интеграции и триггеры
- `UiControlContracts.cs`: new enums, request and interface.
- `UiControlType.cs` + `UiControlSourceGenerator.cs`: generated property support.
- `UiControlAdapters.cs`: parts + adapter + resolver extension.
- `UiPageExtensions.cs`: fluent navigation helpers and diagnostics.
- Provider implementations remain unchanged; headless/FlaUI reuse primitive controls.

## 9. Изменения модели данных / состояния
- Additive enum/record/interface/control-type changes.
- Manifest contract remains version `1`.
- No recorder persisted model changes.

## 10. Миграция / Rollout / Rollback
- Existing consumers keep compiling because changes are additive.
- Consumers opt in by declaring/generated `ShellNavigation` property and configuring adapter parts.
- Unsupported Eremex-specific docking cases fail through `UiOperationException`/`NotSupportedException` instead of low-level flaky gestures.
- Rollback removes new contracts/adapters/extensions/generator mapping/tests; existing tab/tree/list behavior remains unaffected.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- Shell navigation adapter opens a page through tree navigation.
- Shell navigation adapter activates an already-open pane through pane tabs.
- `OpenOrActivate` prefers pane activation when pane tab exists.
- Missing pane tabs for `Activate` produce explicit unsupported diagnostics.
- Page extensions wrap runtime shell navigation failures into `UiOperationException`.
- Source generator emits accessor for `ShellNavigation`.

Commands:
- `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj`
- `dotnet test --project .\tests\AppAutomation.Authoring.Tests\AppAutomation.Authoring.Tests.csproj`
- `dotnet build .\AppAutomation.sln --no-restore`
- `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release --no-restore`
- `dotnet test --solution .\AppAutomation.sln --no-build`

## 12. Риски и edge cases
- Real Eremex docking shell may not expose pane tabs or navigation anchors with stable automation ids; adapter requires stable primitive parts.
- Duplicate pane captions can make activation ambiguous; first normalized match wins.
- `Activate` cannot work without observable pane tabs.
- If active/open pane state is not observable, helper cannot verify post-state and relies on action success.

## 13. План выполнения
1. Add public shell navigation enums/request/interface and control type value.
2. Add `ShellNavigationParts`, adapter and resolver extension.
3. Add fluent shell navigation page extension methods.
4. Update source generator and legacy FlaUI enum.
5. Add abstraction adapter/page tests and authoring generator tests.
6. Run targeted tests/build/full tests and post-EXEC review.

## 14. Открытые вопросы
Нет блокирующих вопросов. Пользователь разрешил auto-approval; Eremex-specific docking gestures remain out of scope until runtime UIA tree is available.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - Public API is selector/automation-id based.
  - Stable anchors are required by adapter parts.
  - Unsupported runtime behavior returns diagnostics.
  - Automated tests cover supported behavior and unsupported boundary.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Abstractions/UiControlContracts.cs` | Add shell navigation enums/request/interface | Runtime contract |
| `src/AppAutomation.Abstractions/UiControlType.cs` | Add `ShellNavigation` | Authoring contract |
| `src/AppAutomation.Abstractions/UiControlAdapters.cs` | Add shell navigation parts/adapter/extension | Composite support |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` | Add fluent shell navigation helpers and diagnostics | Replay API |
| `src/AppAutomation.Authoring/UiControlSourceGenerator.cs` | Map new control type to interface | Generated page support |
| `src/AppAutomation.FlaUI/PageObjects/UiControlType.cs` | Keep enum parity | Legacy page object parity |
| `tests/AppAutomation.Abstractions.Tests/UiControlAdapterTests.cs` | Shell adapter tests | Adapter coverage |
| `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` | Shell helper diagnostics tests | Contract coverage |
| `tests/AppAutomation.Authoring.Tests/UiControlSourceGeneratorTests.cs` | Generator mapping tests | Authoring coverage |
| `tasks.md` | Track AAR-007 state | Workflow idempotency |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Shell page open | Manual tree/list/tab operations | `IShellNavigationControl` + `OpenShellPane` |
| Pane activation | Manual tab selection | `ActivateShellPane` with pane tab diagnostics |
| Open-or-switch flow | Consumer-specific branching | `OpenOrActivateShellPane` |
| Unsupported docking internals | Flaky low-level failure | Explicit typed diagnostic |

## 18. Альтернативы и компромиссы
- Вариант: реализовать Eremex-specific FlaUI docking gestures.
- Плюсы: closer to real Arm.Srv dock behavior.
- Минусы: high flakiness risk without verified UIA tree and vendor dependency pressure.
- Выбранное решение: provider-neutral shell intent over stable anchors; provider-specific gestures can be added later behind the same high-level contract.

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
| 1. Ясность цели и границ | 5 | Shell navigation modes and docking non-goals explicit |
| 2. Понимание текущего состояния | 5 | Existing tab/tree/list support and recorder boundary captured |
| 3. Конкретность целевого дизайна | 5 | Public types, parts, adapter behavior and fluent API defined |
| 4. Безопасность (миграция, откат) | 5 | Additive only, rollback clear |
| 5. Тестируемость | 5 | Adapter, page extension and generator tests listed |
| 6. Готовность к автономной реализации | 5 | No blocking questions |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS.
- Что исправлено: scope ограничен provider-neutral shell intent over stable primitive anchors; Eremex-specific docking gestures left out of task.
- Что осталось на решение пользователя: ничего; пользователь разрешил auto-approval для `gap-resolution.md`.

## Approval
Спека считается подтверждённой автоматически по разрешению пользователя для `gap-resolution.md`.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Спецификация AAR-007 | 0.84 | Runtime UIA Arm.Srv/Eremex docking tree не проверялось | EXEC implementation | Нет | Нет, auto-approval разрешён | Выбран composite shell navigation adapter over existing tree/list/tab/label primitives to avoid vendor docking dependency | `tasks.md`, `specs/AAR-007-shell-docking-navigation.md` |
| EXEC | Реализация AAR-007 | 0.86 | Runtime UIA Arm.Srv/Eremex docking tree не проверялось | Targeted tests | Нет | Нет | Добавлены shell navigation contracts, `ShellNavigation` control type, composite adapter over tree/list/tab/label parts, fluent helpers and contract tests | `src/AppAutomation.Abstractions/UiControlContracts.cs`, `src/AppAutomation.Abstractions/UiControlType.cs`, `src/AppAutomation.Abstractions/UiControlAdapters.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Authoring/UiControlSourceGenerator.cs`, `src/AppAutomation.FlaUI/PageObjects/UiControlType.cs`, `tests/AppAutomation.Abstractions.Tests/UiControlAdapterTests.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `tests/AppAutomation.Authoring.Tests/UiControlSourceGeneratorTests.cs`, `tasks.md` |

## 21. EXEC Verification
| Команда | Результат |
| --- | --- |
| `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj` | PASS, 51/51 |
| `dotnet test --project .\tests\AppAutomation.Authoring.Tests\AppAutomation.Authoring.Tests.csproj --no-restore` | PASS, 2/2 |
| `dotnet build .\AppAutomation.sln --no-restore` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release --no-restore` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet test --solution .\AppAutomation.sln --no-build` | PASS, 199/199 |
| `git diff --check -- <AAR-007 files>` | PASS; Git warned only about future CRLF normalization |

## 22. Post-EXEC Review
- Статус: PASS.
- Что проверено: public API is additive; generated accessors map to `IShellNavigationControl`; adapter supports tree, selectable list and tab/pane-tab navigation through stable primitives; `Open`, `Activate` and `OpenOrActivate` modes are explicit; helpers wrap unsupported runtime behavior into `UiOperationException`; no provider/vendor dependency was added.
- Остаточный риск: real Arm.Srv/Eremex docking UIA exposure is still runtime-dependent; this task supports stable exposed anchors and leaves raw docking gestures/floating layout operations out of scope.
