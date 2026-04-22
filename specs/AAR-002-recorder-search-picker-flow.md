# AAR-002 Recorder Search Picker Flow

## 0. Метаданные
- Тип: `dotnet-desktop-client` + recorder support + source generation
- Задача: `AAR-002`
- Масштаб: medium
- Основание: `ControlSupportMatrix.md`, Arm.Srv rows for `miniControls:SearchControl` and `client:ServerSearchComboBox`
- Статус спеки: auto-approved в рамках `gap-resolution.md`
- Ограничения:
  - Не добавлять зависимость от Arm.Srv или Eremex.
  - Не реализовывать полноценный Arm.Srv popup/search adapter; это будет отдельный provider-specific слой.
  - Не ломать существующие `SearchPickerParts`, `ISearchPickerControl` и `WithSearchPicker`.
  - Не удалять raw `EnterText` / `SelectComboItem`; search-picker запись включается только для явно настроенных hints.

## 1. Цель
Научить recorder сохранять configured composite search-picker flow как один user-level шаг:

```csharp
Page.SearchAndSelect(static page => page.HistoryOperationPicker, "least", "Least Common Multiple");
```

В `AppAutomation.Abstractions` runtime API уже есть (`ISearchPickerControl`, `SearchPickerParts`, `SearchAndSelect`, `WithSearchPicker`), но recorder/generated page contract пока не умеет создавать search-picker property и action.

## 2. AS-IS
- `UiControlType` не содержит `SearchPicker`.
- Source generator не умеет генерировать `ISearchPickerControl` property из `[UiControl]`.
- `RecordedActionKind` не содержит `SearchAndSelect`.
- `RecorderStepFactory` пишет `TextBox` как `EnterText` и `ComboBox` как `SelectComboItem`.
- `RecorderSession.RecordComboBoxSelection` перед combo selection flush-ит pending text, поэтому search/select flow дробится на primitive steps.

## 3. Проблема
Arm.Srv search pickers (`SearchControl`, `ServerSearchComboBox`) являются бизнесовым composite-действием: пользователь вводит фильтр/поисковую строку и выбирает результат. Если recorder сохраняет это как raw text + combo/list steps, generated scenario теряет intent и становится хрупким для popup/editor реализации.

## 4. Non-Goals
- Не автоматизировать Eremex `PopupEditor` и async popup list на этом шаге.
- Не поддерживать fuzzy/history/delete-history flows Arm.Srv `SearchControl`.
- Не добавлять низкоуровневые mouse/keyboard gestures.
- Не менять runtime adapters кроме enum/source-generator compatibility, нужной для generated code.

## 5. TO-BE
### Public API
- Добавить `UiControlType.SearchPicker = 24`.
- Source generator должен мапить `UiControlType.SearchPicker` в `ISearchPickerControl`.
- Добавить `RecorderSearchPickerHint` и `AppAutomationRecorderOptions.SearchPickerHints`.

Предлагаемый shape:

```csharp
public sealed record RecorderSearchPickerHint(
    string LocatorValue,
    SearchPickerParts Parts,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = false);
```

`LocatorValue` задаёт generated search-picker property/root locator. `Parts` переиспользует уже существующий runtime contract.

### Recorder model/codegen
- Добавить `RecordedActionKind.SearchAndSelect`.
- Расширить internal `RecordedStep` вторым string-полем для выбранного item text.
- `AuthoringCodeGenerator` должен генерировать:
  - `[UiControl("HistoryOperationPicker", UiControlType.SearchPicker, "HistoryOperationPicker", ...)]`
  - `Page.SearchAndSelect(static page => page.HistoryOperationPicker, "<search>", "<item>");`

### Capture behavior
- `RecorderStepFactory.TryCreateSearchPickerStep(TextBox searchInput, ComboBox results)`:
  - ищет `RecorderSearchPickerHint`, где `Parts.SearchInputLocator` и `Parts.ResultsLocator` совпадают с locators controls;
  - берёт `searchInput.Text` как search text;
  - берёт selected combo item text как item text;
  - создаёт descriptor `UiControlType.SearchPicker` по hint locator;
  - возвращает unsupported, если нет hint, пустой search text или item text.
- `RecorderSession.RecordComboBoxSelection`:
  - если есть pending text box и configured search-picker step успешно создан, recorder сохраняет один `SearchAndSelect` step и очищает pending text;
  - иначе сохраняет текущее поведение raw `EnterText` / `SelectComboItem`.

## 6. Тестирование
Добавить/обновить tests:
- `tests/AppAutomation.Authoring.Tests`:
  - source generator emits `ISearchPickerControl` accessor for `UiControlType.SearchPicker`.
- `tests/AppAutomation.Recorder.Avalonia.Tests`:
  - factory creates `SearchAndSelect` from configured `RecorderSearchPickerHint`.
  - save output contains `UiControlType.SearchPicker` and `Page.SearchAndSelect(...)`.
  - no hint keeps current unsupported path for search-picker factory.

Команды проверки:
- `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj`
- `dotnet test --project .\tests\AppAutomation.Authoring.Tests\AppAutomation.Authoring.Tests.csproj`
- `dotnet build .\AppAutomation.sln`
- `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release`
- `dotnet test --solution .\AppAutomation.sln --no-build`

## 7. Критерии готовности
- Recorder can produce and persist one configured `SearchAndSelect` step.
- Generated code uses `Page.SearchAndSelect`.
- Source generator produces `ISearchPickerControl` accessor for generated search-picker controls.
- Existing primitive combo/text recording remains available when no search-picker hint exists.
- Full solution tests pass.

## 8. Риски
- Generated search-picker property requires runtime resolver to be wrapped with `WithSearchPicker`; this is already the existing runtime contract and must be configured by consumer tests.
- Arm.Srv `ServerSearchComboBox` may use popup/list controls rather than native `ComboBox`; this task only creates provider-neutral recorder plumbing. Provider-specific popup handling remains for later tasks.

## 9. План выполнения
1. Add `SearchPicker` to `UiControlType` and source-generator mapping.
2. Add recorder hint/model/action fields.
3. Add `TryCreateSearchPickerStep` and session coalescing for pending text + combo selection.
4. Add generator output for `SearchAndSelect`.
5. Add targeted tests and run verification commands.

## 10. SPEC Review
- Полнота: PASS, границы и integration points зафиксированы.
- Safety: PASS, behavior opt-in через hints; existing primitive recording remains fallback.
- Проверяемость: PASS, есть source-generator, recorder factory/save and full solution checks.
- Готовность к EXEC: PASS.

Итог: ГОТОВО. Спека считается подтверждённой автоматически по разрешению пользователя для `gap-resolution.md`.

## 11. EXEC Verification
| Команда | Результат |
|---|---|
| `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj` | PASS, 34/34 |
| `dotnet test --project .\tests\AppAutomation.Authoring.Tests\AppAutomation.Authoring.Tests.csproj` | PASS, 2/2 |
| `dotnet build .\AppAutomation.sln` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release` | PASS, 0 errors; required for existing Release launch-option tests |
| `dotnet test --solution .\AppAutomation.sln --no-build` | PASS, 164/164 |
| `git diff --check -- <AAR-002 files>` | PASS; Git warned only about future CRLF normalization |

## 12. Post-EXEC Review
- Статус: PASS.
- Что проверено: `UiControlType.SearchPicker` maps to `ISearchPickerControl`; recorder search-picker hints produce `RecordedActionKind.SearchAndSelect`; generated page/scenario output uses `UiControlType.SearchPicker` and `Page.SearchAndSelect`; session coalesces pending text + combo selection only when a configured hint matches.
- Что исправлено во время review: recorder fingerprint now includes the selected item text, so repeated search-picker steps with the same search text but different selected results are not deduplicated.
- Остаточный риск: runtime execution still requires consumer-side `WithSearchPicker(...)` registration. Arm.Srv-specific Eremex popup/list behavior remains in later tasks.
