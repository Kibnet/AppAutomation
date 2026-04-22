# AAR-001 Recorder Custom Control Hints

## 0. Метаданные
- Тип: `dotnet-desktop-client` + recorder support
- Задача: `AAR-001`
- Масштаб: small
- Основание: `ControlSupportMatrix.md`, секция Arm.Srv consumer audit
- Статус спеки: auto-approved в рамках `gap-resolution.md`
- Ограничения:
  - Не добавлять зависимость от Eremex или Arm.Srv в production packages.
  - Не менять runtime Headless/FlaUI resolver behavior в этой задаче.
  - Не добавлять composite search/grid сценарии; они покрываются `AAR-002` и `AAR-003`.
  - Сохранить совместимость существующих `new RecorderControlHint(locator, actionHint)` вызовов.

## 1. Цель
Расширить `AppAutomation.Recorder.Avalonia` так, чтобы consumer мог явно подсказать рекордеру тип и locator metadata для custom или wrapped controls. Это нужно для Arm.Srv controls вроде Eremex editors, `CopyTextBox`, `SearchControl` и `ServerSearchComboBox`, где Avalonia visual source может быть generic/wrapper-элементом, а authoring contract должен быть `TextBox`, `ComboBox`, `AutomationElement` или другой уже существующий `UiControlType`.

## 2. AS-IS
- `RecorderControlHint` содержит только `LocatorValue` и `RecorderActionHint`.
- `RecorderStepFactory` использует `RecorderActionHint.SpinnerTextBox` для spinner-like `TextBox`.
- `RecorderSelectorResolver` умеет:
  - искать стабильный `AutomationId`;
  - опционально fallback-иться на `Name`;
  - применять `RecorderLocatorAlias`;
  - применять `RecorderGridHint` как alias на grid bridge.
- В `ControlSupportMatrix.md` Arm.Srv audit требует recorder hints для Eremex editors и wrapper-aware naming, но сейчас control type override нельзя задать через `ControlHints`.

## 3. Проблема
Для custom/wrapped controls рекордер может сохранить слишком общий или неверный `UiControlType`, даже если consumer заранее знает устойчивый authoring contract. Из-за этого generated page partial получает неподходящий тип свойства, а дальнейший generated scenario либо требует ручной правки, либо вообще не должен сохраняться.

## 4. Non-Goals
- Не реализовывать Eremex editor adapter.
- Не записывать `ServerSearchComboBox` как полноценный search-picker flow.
- Не менять contract `RecorderLocatorAlias`; alias продолжает отвечать за перенос на другой locator.
- Не гарантировать, что любой manually configured type/action pair будет исполнимым: consumer обязан задавать согласованный `UiControlType` для будущего runtime resolver-а.

## 5. TO-BE
### Public API
Расширить `RecorderControlHint` optional-полями:

```csharp
public sealed record RecorderControlHint(
    string LocatorValue,
    RecorderActionHint ActionHint,
    UiControlType? TargetControlType = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool? FallbackToName = null);
```

Семантика:
- `LocatorValue` + `LocatorKind` идентифицируют source locator, найденный рекордером.
- `ActionHint` сохраняет текущее поведение, включая `SpinnerTextBox`.
- `TargetControlType`, если задан, переопределяет `UiControlType` в `RecordedControlDescriptor`.
- `FallbackToName`, если задан, переопределяет generated locator metadata; если не задан, используется текущая политика resolver-а.
- Старые двухаргументные вызовы остаются валидными.

### Resolver behavior
- `RecorderSelectorResolver` после выбора source locator применяет matching `RecorderControlHint`.
- Matching точный: `LocatorKind` и trimmed `LocatorValue` должны совпасть.
- При применении type hint descriptor получает:
  - hinted `UiControlType`;
  - property name, рассчитанный уже от hinted type;
  - исходные `LocatorValue` и `LocatorKind`;
  - warning с текстом, что применён control hint.
- `RecorderLocatorAlias` остаётся отдельным механизмом для mapping на другой stable locator. Если consumer хочет и другой locator, и другой type, он должен использовать alias с `TargetControlType`.

### Step factory behavior
- `RecorderStepFactory` продолжает поддерживать старые action hints.
- Action hint lookup должен учитывать `LocatorKind`, чтобы Name-based hints не конфликтовали с AutomationId hints с тем же текстом.

## 6. Тестирование
Добавить/обновить tests в `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`:
- Backward compatibility: существующий spinner hint через двухаргументный constructor продолжает давать `SetSpinnerValue`.
- Resolver applies typed hint: generic/wrapper control с `AutomationId` получает hinted `UiControlType`.
- Resolver applies Name locator metadata: при `AllowNameLocators = true` Name-based hint матчится только как `UiLocatorKind.Name`, сохраняет `FallbackToName` и warning.
- Generated source uses hinted descriptor: `AuthoringCodeGenerator.SaveAsync` пишет `[UiControl(... UiControlType.<hint>, ..., LocatorKind = ..., FallbackToName = ...)]` из descriptor, полученного через hint.

Команды проверки:
- `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj`
- `dotnet build .\AppAutomation.sln`
- `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release`
- `dotnet test --solution .\AppAutomation.sln --no-build`

## 7. Критерии готовности
- `RecorderControlHint` поддерживает typed custom control hints без breaking changes.
- Generated controls используют hinted `UiControlType` и locator metadata.
- Старый spinner hint scenario остаётся зелёным.
- Нет Eremex/Arm.Srv dependency в production code.

## 8. Риски
- Неверная комбинация hinted `UiControlType` и recorded action может дать generated code, который не компилируется или не исполняется. Это ограничивается документацией semantics: `ControlHints` не заменяют future composite adapters.
- Если одновременно настроены `ControlHint` и `LocatorAlias`, alias остаётся authoritative для target locator/type. Это не ломает существующие grid hints и сохраняет прежнюю модель ответственности.

## 9. План выполнения
1. Расширить `RecorderControlHint` optional-полями.
2. Добавить matching control hints в `RecorderSelectorResolver`.
3. Обновить action hint lookup в `RecorderStepFactory` с учётом `UiLocatorKind`.
4. Добавить тесты на backward compatibility, type hint, Name locator metadata и generated attribute.
5. Прогнать targeted tests, затем build/full tests.

## 10. SPEC Review
- Полнота: PASS, указаны цель, границы, API, resolver behavior и tests.
- Safety: PASS, изменение additive и не требует runtime provider изменений.
- Проверяемость: PASS, критерии и команды заданы.
- Готовность к EXEC: PASS.

Итог: ГОТОВО. Спека считается подтверждённой автоматически по разрешению пользователя для `gap-resolution.md`.

## 11. EXEC Verification
| Команда | Результат |
|---|---|
| `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj` | PASS, 31/31 |
| `dotnet build .\AppAutomation.sln` | PASS, 0 errors; existing analyzer/NU1903 warnings |
| `dotnet build .\sample\DotnetDebug.Avalonia\DotnetDebug.Avalonia.csproj -c Release` | PASS, 0 errors; required because existing `DotnetDebug.Tests` verify Release launch options with `buildBeforeLaunch=false` |
| `dotnet test --solution .\AppAutomation.sln --no-build` | PASS, 161/161 |
| `git diff --check -- <AAR-001 files>` | PASS; Git warned only about future CRLF normalization |

## 12. Post-EXEC Review
- Статус: PASS.
- Что проверено: public `RecorderControlHint` remains backward-compatible; `RecorderSelectorResolver` applies exact locator-kind hints only when no locator alias owns the descriptor; `RecorderStepFactory` action hints now distinguish `AutomationId` and `Name`; generated authoring attributes receive hinted `UiControlType`, `LocatorKind` and `FallbackToName`.
- Остаточный риск: consumer can still configure an incoherent action/type pair; this remains documented because composite adapters are handled by later tasks.
