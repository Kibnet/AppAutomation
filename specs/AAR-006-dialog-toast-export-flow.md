# AAR-006 Dialog Toast And Export Flow

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: AppAutomation
- Задача: `AAR-006`
- Масштаб: medium
- Целевой релиз / ветка: `feat/arm-paritet`
- Основание: `ControlSupportMatrix.md`, gap по `DialogHost`, notifications, folder picker/export
- Статус спеки: auto-approved в рамках `gap-resolution.md`
- Ограничения:
  - Не добавлять dependency на Arm.Srv, Avalonia controls или Eremex.
  - Не реализовывать OS-native folder picker automation через mouse/keyboard gestures без runtime UIA проверки.
  - Не менять persisted recorder scenario model в этом task.
  - Не трогать `ControlSupportMatrix.md` до dedicated AAR-008.

## 1. Overview / Цель
Добавить provider-neutral поддержку пользовательских workflows: modal dialog confirmation/cancel/dismiss, toast/status assertion/dismiss, and folder export selection. Сценарии должны выражать intent через typed API и composite adapters, собранные из стабильных частей (`Button`, `TextBox`, `Label`), а headless/FlaUI должны получать поддерживаемый путь через существующие primitive controls или явную diagnostic ошибку при неправильной конфигурации.

## 2. Текущее состояние (AS-IS)
- Есть primitive controls для `Button`, `TextBox`, `Label` and typed page extensions for common interactions.
- AAR-002 and AAR-005 established composite adapter pattern: `Parts` records + `With...` resolver extensions + fluent API diagnostics.
- AAR-003 добавил grid export trigger как часть `IGridUserActionControl.Export()`, но нет folder/path confirmation workflow.
- В recorder UI есть собственный export/save flow and status fields, но это не runtime abstraction для тестируемого приложения.
- Нет typed abstraction для modal dialogs, notifications/toasts or folder export pickers.

## 3. Проблема
Arm.Srv использует modal confirmations, toast/status messages and folder export selection. Без typed controls сценарии вынуждены кликать отдельные кнопки и labels вручную, что теряет пользовательский intent, усложняет recorder/headless/FlaUI support matrix and makes unsupported OS/native picker cases look like flaky low-level failures.

## 4. Цели дизайна
- Добавить additive public contracts for dialog, notification and folder export workflows.
- Повторить existing composite adapter style over stable primitive controls.
- Поддержать explicit dialog actions: confirm, cancel, dismiss.
- Поддержать notification text assertion and optional dismiss action.
- Поддержать folder export trigger + path input + select/cancel buttons + optional status label.
- Дать `UiPageExtensions` helpers with `UiOperationException` diagnostics for unsupported/misconfigured flows.
- Оставить native OS picker automation as unsupported unless app exposes stable in-app parts.

## 5. Non-Goals
- Не реализовывать real OS-native folder picker automation.
- Не добавлять recorder step kinds/codegen for dialog/toast/export in this task.
- Не внедрять provider-specific FlaUI gestures for Avalonia `DialogHost` or platform dialogs.
- Не менять grid export action from AAR-003.
- Не обновлять matrix documentation до AAR-008.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Public abstractions
Добавить в `AppAutomation.Abstractions`:

```csharp
public enum DialogActionKind
{
    Confirm = 0,
    Cancel = 1,
    Dismiss = 2
}

public enum FolderExportCommitMode
{
    Select = 0,
    Cancel = 1
}

public interface IDialogControl : IUiControl
{
    string MessageText { get; }
    void Complete(DialogActionKind actionKind = DialogActionKind.Confirm);
}

public interface INotificationControl : IUiControl
{
    string Text { get; }
    void Dismiss();
}

public interface IFolderExportControl : IUiControl
{
    string? SelectedFolderPath { get; }
    string? StatusText { get; }
    void SelectFolder(string folderPath, FolderExportCommitMode commitMode = FolderExportCommitMode.Select);
}
```

### 6.2 UiControlType and authoring
Добавить:
- `UiControlType.Dialog = 27`
- `UiControlType.Notification = 28`
- `UiControlType.FolderExport = 29`

Обновить source generator mapping:
- `Dialog` -> `IDialogControl`
- `Notification` -> `INotificationControl`
- `FolderExport` -> `IFolderExportControl`

Обновить legacy FlaUI `PageObjects.UiControlType` enum для numeric parity.

### 6.3 Composite adapter parts
Добавить records:
- `DialogControlParts`
- `NotificationControlParts`
- `FolderExportControlParts`

Parts requirements:
- Dialog: message label locator, confirm button locator, optional cancel button locator, optional dismiss button locator.
- Notification: text label locator, optional dismiss button locator.
- Folder export: open/export button locator, folder path text input locator, select button locator, cancel button locator, optional status label locator.
- Все parts поддерживают common `LocatorKind` and `FallbackToName`.
- Добавить `ByAutomationIds(...)` helpers.

Добавить resolver extensions:
- `WithDialog(propertyName, parts)`
- `WithNotification(propertyName, parts)`
- `WithFolderExport(propertyName, parts)`

### 6.4 Adapter behavior
Adapter rules:
1. Intercepts only matching property name and requested interface.
2. Resolves inner primitive parts lazily on operation/property access because modal/popup content can appear after trigger.
3. Dialog `Complete` invokes the configured button for selected action; missing optional action button throws `NotSupportedException` with property/action context.
4. Notification `Dismiss` invokes optional dismiss button; missing dismiss button throws `NotSupportedException`.
5. Folder export `SelectFolder` invokes open button first.
6. For `Select`, folder export writes `folderPath` to path input and invokes select button.
7. For `Cancel`, folder export invokes cancel button after opening; it does not require path mutation.
8. Status text is returned only when a status locator is configured.

### 6.5 Fluent API
Добавить `UiPageExtensions`:
- `CompleteDialog(selector, actionKind, expectedMessageContains, timeoutMs)`.
- Convenience wrappers: `ConfirmDialog`, `CancelDialog`, `DismissDialog`.
- `WaitUntilNotificationContains(selector, expectedText, timeoutMs)`.
- `DismissNotification(selector, timeoutMs)`.
- `SelectExportFolder(selector, folderPath, commitMode, expectedStatusContains, timeoutMs)`.

Rules:
- Wait until target control is enabled before operation where applicable.
- Validate required string inputs (`expectedText`, `folderPath`) early.
- Wrap unsupported/misconfigured adapter errors into `UiOperationException`.
- For dialog expected text, check `MessageText.Contains(expectedMessageContains, OrdinalIgnoreCase)` before completing.
- For notification/status expected text, wait until contains match succeeds or timeout expires.

## 7. Бизнес-правила / Алгоритмы
- Dialog actions are explicit; fallback from missing cancel/dismiss to confirm is forbidden.
- Notification assertion uses contains semantics to tolerate timestamps/prefixes.
- Folder export `Select` requires a non-empty folder path.
- Folder export `Cancel` is a supported explicit outcome and should not verify selected path.
- Native OS picker automation is supported only if the application exposes stable path input/select/cancel primitives.

## 8. Точки интеграции и триггеры
- `UiControlAdapters.cs`: parts + adapters + resolver extension methods.
- `UiControlContracts.cs`: new public interface/enum types.
- `UiPageExtensions.cs`: fluent workflow operations and diagnostics.
- `UiControlType.cs` + `UiControlSourceGenerator.cs`: authoring support.
- No direct provider changes are required; headless/FlaUI use existing primitive resolvers.

## 9. Изменения модели данных / состояния
- Additive enum/interface changes.
- Manifest contract remains version `1`; generated controls can now include new control types.
- No persisted recorder scenario model changes.
- No changes to recorder export/save state.

## 10. Миграция / Rollout / Rollback
- Existing consumers keep compiling because changes are additive.
- New consumers opt in by annotating/generated properties and configuring resolver adapters.
- Unsupported native picker scenarios fail with explicit diagnostics instead of pretending to be supported.
- Rollback removes new contracts/adapters/extensions/generator mapping/tests; existing primitive controls and grid export action remain unaffected.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- Dialog adapter exposes message text and invokes confirm/cancel/dismiss buttons.
- Dialog page extension validates expected message and wraps missing optional action into `UiOperationException`.
- Notification adapter exposes text and supports optional dismiss.
- Notification page extension waits for expected text and wraps timeout/misconfiguration into `UiOperationException`.
- Folder export adapter invokes open, writes folder path and invokes select for select mode.
- Folder export adapter invokes open and cancel for cancel mode without requiring path mutation.
- Source generator emits accessors for `Dialog`, `Notification` and `FolderExport`.

Commands:
- `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj`
- `dotnet test --project .\tests\AppAutomation.Authoring.Tests\AppAutomation.Authoring.Tests.csproj`
- `dotnet build .\AppAutomation.sln --no-restore`
- `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release --no-restore`
- `dotnet test --solution .\AppAutomation.sln --no-build`

## 12. Риски и edge cases
- Real `DialogHost`/toast visual trees may not expose stable automation ids; this task requires stable primitive parts or returns diagnostics.
- OS-native folder pickers may not expose path input/select controls consistently through headless/FlaUI; native picker gestures remain out of scope.
- Notification text can change asynchronously; contains wait with timeout mitigates this for stable status labels.
- Dismiss buttons may not exist for auto-expiring notifications; API supports assertion without dismiss.

## 13. План выполнения
1. Add public dialog/notification/folder export interfaces and enums.
2. Add control type values and source generator/FlaUI enum parity.
3. Add composite parts, adapters and resolver extension methods.
4. Add fluent page extension methods with diagnostics.
5. Add abstraction adapter/page tests and authoring generator tests.
6. Run targeted tests/build/full tests and post-EXEC review.

## 14. Открытые вопросы
Нет блокирующих вопросов. Пользователь разрешил auto-approval; native OS picker and provider-specific gestures remain explicitly out of scope.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - Public API is selector/automation-id based.
  - Unsupported/misconfigured runtime behavior returns diagnostics.
  - Stable selectors are required by adapter parts.
  - Automated tests cover supported behavior.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Abstractions/UiControlContracts.cs` | Add dialog/notification/folder export enums and interfaces | Runtime contract |
| `src/AppAutomation.Abstractions/UiControlType.cs` | Add `Dialog`, `Notification`, `FolderExport` | Authoring contract |
| `src/AppAutomation.Abstractions/UiControlAdapters.cs` | Add composite parts/adapters/extensions | Composite support |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` | Add fluent workflow helpers and diagnostics | Replay API |
| `src/AppAutomation.Authoring/UiControlSourceGenerator.cs` | Map new control types to interfaces | Generated page support |
| `src/AppAutomation.FlaUI/PageObjects/UiControlType.cs` | Keep enum parity | Legacy page object parity |
| `tests/AppAutomation.Abstractions.Tests/UiControlAdapterTests.cs` | Composite adapter tests | Adapter coverage |
| `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` | Fluent API diagnostics tests | Contract coverage |
| `tests/AppAutomation.Authoring.Tests/UiControlSourceGeneratorTests.cs` | Generator mapping tests | Authoring coverage |
| `tasks.md` | Track AAR-006 state | Workflow idempotency |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Modal dialog | Manual button/label actions | `IDialogControl` + `WithDialog` + dialog page helpers |
| Toast/status | Manual label assertion | `INotificationControl` + `WithNotification` + wait/dismiss helpers |
| Folder export | Manual open/path/select actions or unsupported OS picker | `IFolderExportControl` + `WithFolderExport` + `SelectExportFolder` |
| Unsupported native picker | Low-level flaky failure | Explicit typed diagnostic |

## 18. Альтернативы и компромиссы
- Вариант: реализовать direct FlaUI gestures for `DialogHost` and OS folder picker.
- Плюсы: может покрыть больше реальных desktop flows без app-side test hooks.
- Минусы: высокий риск flaky behavior без проверенного Arm.Srv UIA tree and platform-specific picker differences.
- Выбранное решение: закрепляет provider-neutral typed contract and composition model over stable primitives; provider-specific runtime gestures can be added later without changing high-level tests.

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
| 1. Ясность цели и границ | 5 | Dialog, notification and folder export flows scoped explicitly |
| 2. Понимание текущего состояния | 5 | Primitive support, recorder boundary and composite adapter pattern captured |
| 3. Конкретность целевого дизайна | 5 | Public types, parts, adapters and fluent API defined |
| 4. Безопасность (миграция, откат) | 5 | Additive only, rollback clear |
| 5. Тестируемость | 5 | Adapter, page extension and generator tests listed |
| 6. Готовность к автономной реализации | 5 | No blocking questions |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS.
- Что исправлено: scope ограничен provider-neutral adapter/helper implementation; native picker and unverified provider gestures left out of task.
- Что осталось на решение пользователя: ничего; пользователь разрешил auto-approval для `gap-resolution.md`.

## Approval
Спека считается подтверждённой автоматически по разрешению пользователя для `gap-resolution.md`.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Спецификация AAR-006 | 0.86 | Runtime UIA Arm.Srv/DialogHost/native picker tree не проверялось | EXEC implementation | Нет | Нет, auto-approval разрешён | Выбран composite adapter pattern поверх primitive controls, потому он совместим с headless/FlaUI and avoids unsupported native picker gestures | `tasks.md`, `specs/AAR-006-dialog-toast-export-flow.md` |
| EXEC | Реализация AAR-006 | 0.86 | Runtime UIA Arm.Srv/DialogHost/native picker tree не проверялось | Targeted tests | Нет | Нет | Добавлены typed contracts, `UiControlType`/generator mapping, composite adapters, fluent helpers and contract tests without vendor/native picker dependency | `src/AppAutomation.Abstractions/UiControlContracts.cs`, `src/AppAutomation.Abstractions/UiControlType.cs`, `src/AppAutomation.Abstractions/UiControlAdapters.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.Authoring/UiControlSourceGenerator.cs`, `src/AppAutomation.FlaUI/PageObjects/UiControlType.cs`, `tests/AppAutomation.Abstractions.Tests/UiControlAdapterTests.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `tests/AppAutomation.Authoring.Tests/UiControlSourceGeneratorTests.cs`, `tasks.md` |

## 21. EXEC Verification
| Команда | Результат |
| --- | --- |
| `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj` | PASS, 45/45 |
| `dotnet test --project .\tests\AppAutomation.Authoring.Tests\AppAutomation.Authoring.Tests.csproj --no-restore` | PASS, 2/2 |
| `dotnet build .\AppAutomation.sln --no-restore` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release --no-restore` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet test --solution .\AppAutomation.sln --no-build` | PASS, 193/193 |
| `git diff --check -- <AAR-006 files>` | PASS; Git warned only about future CRLF normalization |

## 22. Post-EXEC Review
- Статус: PASS.
- Что проверено: public API is additive; generated accessors map to `IDialogControl`, `INotificationControl` and `IFolderExportControl`; composite adapters resolve only configured stable primitive parts; dialog helpers validate optional message text and wrap unsupported actions; notification helpers support contains wait and optional dismiss; folder export helper supports select/cancel and optional status wait.
- Остаточный риск: real Arm.Srv `DialogHost`, toast and native picker UIA exposure is still runtime-dependent; this task supports stable in-app primitive parts and returns diagnostics for unsupported/native picker cases.
