# FlaUI Window Placement For Deterministic Desktop Tests

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая ветка `feat/multi-monitor`
- Ограничения:
  - До подтверждения спеки изменяется только этот файл.
  - Переход к реализации разрешён только после фразы пользователя `Спеку подтверждаю`.
  - Решение должно быть additive: существующие FlaUI-тесты без новой настройки запускаются как раньше.
  - Headless runtime не должен получать desktop-only placement behavior.
  - FlaUI остаётся Windows-only; cross-platform contracts package не должен получать зависимость от WinForms, FlaUI или Win32-specific типов.
  - README нужно обновить в английской и русской частях, так как текущий README двуязычный.
- Связанные ссылки:
  - `src/AppAutomation.Session.Contracts/DesktopAppLaunchOptions.cs`
  - `src/AppAutomation.TestHost.Avalonia/AvaloniaDesktopLaunchOptions.cs`
  - `src/AppAutomation.TestHost.Avalonia/AvaloniaDesktopLaunchHost.cs`
  - `src/AppAutomation.FlaUI/Session/DesktopAppSession.cs`
  - `sample/DotnetDebug.AppAutomation.TestHost/DotnetDebugAppLaunchHost.cs`
  - `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/tests/SampleApp.AppAutomation.TestHost/SampleAppAppLaunchHost.cs`
  - `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/tests/SampleApp.UiTests.FlaUI/Tests/MainWindowFlaUiTests.cs`
  - `README.md`

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Добавить удобную настройку размера, положения и целевого монитора для окна AUT при запуске `FlaUI`-тестов.

Цель - сделать desktop UI tests более детерминированными на машинах с несколькими мониторами и снизить помехи основной работе пользователя: тестовое окно можно заранее отправить на выбранный монитор, задать стабильный размер и разместить его предсказуемо внутри рабочей области.

Outcome contract:
- Success means:
  - потребитель может указать placement в `CreateDesktopLaunchOptions(...)` без ручных Win32/FlaUI вызовов в каждом тесте;
  - `DesktopAppSession.Launch(...)` применяет placement перед возвратом session и перед пользовательскими действиями теста;
  - при `WindowPlacement = null` поведение полностью совпадает с текущим;
  - некорректный explicit monitor/geometry даёт понятную ошибку; fallback разрешён только для missing monitor и только при явной настройке;
  - README показывает короткий пример для dedicated monitor/size/position в английской и русской частях.
- Итоговый артефакт / output:
  - public contract для placement в `AppAutomation.Session.Contracts`;
  - проброс placement через Avalonia test host helper;
  - FlaUI implementation с Win32 monitor/window placement;
  - focused unit/contract tests и, где возможно, guarded FlaUI runtime smoke;
  - README-документация.
- Stop rules:
  - не расширять задачу до parallel test isolation, input focus isolation или headless replacement;
  - не менять consumer page object API;
  - не менять default launch behavior;
  - не продолжать подбор monitor API, если достаточно Win32 `EnumDisplayMonitors` / `GetMonitorInfo` / `SetWindowPos` для текущих Windows FlaUI targets.

## 2. Текущее состояние (AS-IS)
- `DesktopAppLaunchOptions` содержит путь к executable, working directory, arguments, environment variables, dispose callback, main window timeout и poll interval.
- `AvaloniaDesktopLaunchOptions` содержит build-related настройки, arguments/environment variables, timeout/poll interval и isolated build output.
- `AvaloniaDesktopLaunchHost.CreateLaunchOptions(...)` строит `DesktopAppLaunchOptions`, но не передаёт никаких сведений о желаемом окне.
- `DesktopAppSession.Launch(...)`:
  - запускает процесс через `Application.Launch(startInfo)`;
  - создаёт `UIA3Automation`;
  - ждёт main window через `application.GetMainWindow(automation)`;
  - ждёт готовность automation tree;
  - возвращает `DesktopAppSession`.
- Sample и template FlaUI tests вызывают `DesktopAppSession.Launch(<AppLaunchHost>.CreateDesktopLaunchOptions())`.
- README уже описывает шаг `Stabilize Headless first, then enable FlaUI`, но не объясняет, как стабилизировать desktop window geometry на нескольких мониторах.

Скрытые зависимости и инварианты:
- FlaUI runtime project target-ится как `net8.0-windows7.0;net10.0-windows7.0`, поэтому Win32 P/Invoke допустим в `AppAutomation.FlaUI`.
- `AppAutomation.Session.Contracts` target-ится как plain `net8.0`, поэтому contract types должны быть простыми managed value objects без Windows-only API dependencies.
- Для deterministic UIA lookup важно применить placement до первых user actions; желательно также до финального ожидания automation tree, чтобы layout уже соответствовал целевому размеру.
- Real FlaUI tests всё равно работают в interactive desktop session и могут получать focus; placement снижает вмешательство, но не делает запуск полностью background/headless.

## 3. Проблема
Сейчас FlaUI-тесты запускают AUT в положении и размере, которые решает само приложение или Windows. На multi-monitor машинах это приводит к недетерминированному layout, попаданию окна на рабочий монитор пользователя и случайным помехам другим задачам.

## 4. Цели дизайна
- Разделение ответственности:
  - contracts описывают desired placement;
  - Avalonia test host только пробрасывает его;
  - FlaUI session применяет его через Windows desktop APIs;
  - README объясняет consumer-facing сценарий.
- Повторное использование: одна настройка в `TestHost` вместо ручного move/resize в каждом test class.
- Тестируемость: geometry calculation и option propagation покрываются unit/contract tests без реального desktop UI; runtime application test guarded existing desktop availability guard.
- Консистентность: naming и option style должны соответствовать существующим launch options.
- Обратная совместимость: `WindowPlacement = null` сохраняет старое поведение; существующие constructors/call sites не ломаются.

## 5. Non-Goals (чего НЕ делаем)
- Не обещаем запуск FlaUI без фокуса, без foreground window или без влияния на interactive desktop.
- Не добавляем поддержку Linux/macOS для FlaUI placement.
- Не меняем `HeadlessAppLaunchOptions` и headless session behavior.
- Не добавляем управление DPI awareness процесса AUT.
- Не добавляем персистентные user settings или конфигурационные файлы.
- Не реализуем автоматический выбор "свободного" монитора по активности пользователя.
- Не меняем page objects, locators, generated scenario authoring API.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/AppAutomation.Session.Contracts/DesktopAppLaunchOptions.cs`
  - Добавить `DesktopWindowPlacement? WindowPlacement { get; init; }`.
  - Добавить простые contract-модели для monitor selection, size, offset/anchor и failure behavior.
- `src/AppAutomation.TestHost.Avalonia/AvaloniaDesktopLaunchOptions.cs`
  - Добавить `DesktopWindowPlacement? WindowPlacement { get; init; }`.
- `src/AppAutomation.TestHost.Avalonia/AvaloniaDesktopLaunchHost.cs`
  - Пробросить `launchOptions.WindowPlacement` в `DesktopAppLaunchOptions.WindowPlacement`.
- `src/AppAutomation.FlaUI/Session/DesktopAppSession.cs`
  - После получения main window и до возврата session применить placement.
  - Валидацию options оставить рядом с launch validation.
- Новый internal helper в `src/AppAutomation.FlaUI/Session/`, например `DesktopWindowPlacementService.cs`
  - Enumerate monitors.
  - Resolve selected monitor.
  - Calculate final outer window rectangle.
  - Apply rectangle через Win32 `SetWindowPos`.
  - Verify bounding rectangle with timeout/poll interval.
- Sample/template `*AppLaunchHost.cs`
  - Добавить удобный optional parameter `DesktopWindowPlacement? windowPlacement = null` и проброс в `AvaloniaDesktopLaunchOptions`.
- Tests
  - Добавить unit tests для contract validation / geometry calculation.
  - Добавить propagation test в `AppAutomation.TestHost.Avalonia.Tests`.
  - Добавить focused tests в sample FlaUI tests для option validation and process/session behavior; guarded runtime smoke only where existing desktop guard allows.
- `README.md`
  - Добавить короткий раздел/подраздел в English и Russian частях рядом с FlaUI launch instructions.

### 6.2 Детальный дизайн
#### Public contract
Предлагаемый API в `AppAutomation.Session.Contracts`:

```csharp
public sealed class DesktopAppLaunchOptions
{
    // existing properties
    public DesktopWindowPlacement? WindowPlacement { get; init; }
}

public sealed class DesktopWindowPlacement
{
    public DesktopMonitorSelector Monitor { get; init; } = DesktopMonitorSelector.Primary;
    public DesktopWindowSize? Size { get; init; }
    public DesktopWindowAnchor Anchor { get; init; } = DesktopWindowAnchor.Center;
    public DesktopWindowOffset Offset { get; init; } = DesktopWindowOffset.Zero;
    public bool UseWorkingArea { get; init; } = true;
    public DesktopWindowPlacementUnavailableBehavior UnavailableBehavior { get; init; } =
        DesktopWindowPlacementUnavailableBehavior.Fail;
}
```

Supporting contract types:
- `DesktopMonitorSelector`
  - static `Primary`;
  - static `FromIndex(int index)`;
  - static `FromDeviceName(string deviceName)`;
  - index is zero-based over Win32 monitor enumeration sorted primary first, then by virtual-screen coordinates;
  - `DeviceName` matches Win32 names like `\\.\DISPLAY2`;
  - `FromIndex(index)` must reject negative values with `ArgumentOutOfRangeException`;
  - `FromDeviceName(deviceName)` must reject null/empty/whitespace values with `ArgumentException`.
- `DesktopWindowSize`
  - `Width` and `Height` in Windows desktop pixels;
  - values must be positive.
- `DesktopWindowOffset`
  - `X` and `Y` in Windows desktop pixels relative to the chosen anchor inside selected monitor bounds.
- `DesktopWindowAnchor`
  - `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`, `Center`.
- `DesktopWindowPlacementUnavailableBehavior`
  - `Fail`: throw when selected monitor is unavailable or placement cannot be applied;
  - `UsePrimaryMonitor`: fall back to primary monitor only when monitor resolution fails;
  - no silent fallback by default.

Contract helper factories should make common consumer code compact:

```csharp
WindowPlacement = DesktopWindowPlacement.Centered(
    monitor: DesktopMonitorSelector.FromIndex(1),
    width: 1280,
    height: 900);
```

If helper factories become too much public API for this increment, the minimal acceptable shape is object initializer plus static monitor selector factories. The selected final API must keep XML docs with coordinate semantics.

#### Placement algorithm
1. If `options.WindowPlacement is null`, do nothing.
2. Resolve target monitor:
   - enumerate monitors with Win32 `EnumDisplayMonitors`;
   - read monitor bounds and working area with `GetMonitorInfo`;
   - sort primary first, then `Top`, `Left`, `Bottom`, `Right` for stable index semantics;
   - resolve by primary/index/device name.
3. If monitor resolution fails:
   - `Fail`: throw `InvalidOperationException` with requested selector and available monitors;
   - `UsePrimaryMonitor`: use primary monitor and include fallback detail in exception message only if primary is also missing.
4. Choose placement area:
   - `UseWorkingArea = true`: use monitor work area excluding taskbar/docked shell;
   - `false`: use full monitor bounds.
5. Resolve target size:
   - if `Size` is set, use it;
   - otherwise keep current outer window size from `Window.BoundingRectangle`;
   - explicit size larger than selected area is invalid and must throw an actionable error instead of silent clamping.
6. Resolve target `X/Y` by anchor and offset:
   - `Center`: center in selected area, then add offset;
   - corner anchors: place the corresponding window corner at area corner plus inward/outward offset semantics documented in XML docs.
   - the resulting rectangle must fully fit inside the selected area; offset-caused overflow is invalid by default and must throw an actionable error instead of placing the window partially offscreen.
7. Ensure window is normal/restored before placement when possible:
   - use UIA window pattern if available;
   - otherwise call Win32 `ShowWindow(SW_RESTORE)` against main window handle.
8. Validate enum values before applying:
   - unknown `DesktopWindowAnchor` values are invalid;
   - unknown `DesktopWindowPlacementUnavailableBehavior` values are invalid.
9. Apply with `SetWindowPos`.
10. Poll until `mainWindow.BoundingRectangle` is within a small tolerance of requested rectangle or timeout expires.
11. Continue existing `WaitForAutomationTree`.

The session should use `application.MainWindowHandle` first. If handle is zero, fall back to UIA native handle only if exposed by FlaUI; otherwise throw a clear placement error. Do not use window title matching.

#### Error/output contract
Placement failures must include:
- requested selector, size, anchor, offset, working-area flag;
- available monitor list: index, primary flag, device name, bounds, working area;
- application executable path and process id when available;
- whether fallback was attempted.

Expected errors:
- invalid monitor index/device name;
- invalid size, anchor, fallback behavior or offset-caused out-of-area rectangle;
- no window handle;
- Win32 `SetWindowPos` failure;
- bounding rectangle did not converge before timeout.

Cleanup invariant:
- If placement fails after the AUT process has launched but before `DesktopAppSession` is returned, launch failure cleanup must behave like any other launch exception:
  - dispose `UIA3Automation`;
  - terminate and dispose `Application`;
  - invoke `DesktopAppLaunchOptions.DisposeCallback`;
  - attach cleanup exceptions to the primary placement exception through the existing cleanup exception mechanism.

#### README contract
Add consumer-facing examples in both language sections near FlaUI launch instructions:

```csharp
public static DesktopAppLaunchOptions CreateDesktopLaunchOptions(
    string? buildConfiguration = null,
    DesktopWindowPlacement? windowPlacement = null)
{
    return AvaloniaDesktopLaunchHost.CreateLaunchOptions(
        DesktopApp,
        new AvaloniaDesktopLaunchOptions
        {
            BuildConfiguration = buildConfiguration ?? BuildConfigurationDefaults.ForAssembly(typeof(MyAppAppLaunchHost).Assembly),
            WindowPlacement = windowPlacement
        });
}
```

FlaUI test example:

```csharp
DesktopAppSession.Launch(MyAppAppLaunchHost.CreateDesktopLaunchOptions(
    windowPlacement: DesktopWindowPlacement.Centered(
        monitor: DesktopMonitorSelector.FromIndex(1),
        width: 1280,
        height: 900)));
```

README must explicitly state:
- monitor index is zero-based after stable ordering: primary first, then desktop coordinates;
- `UseWorkingArea = true` avoids taskbar/docked shell area by default;
- FlaUI is still interactive desktop automation and may take focus;
- `WindowPlacement = null` preserves old behavior.

## 7. Бизнес-правила / Алгоритмы
1. Placement is opt-in.
2. Explicit missing monitor fails fast by default.
3. `UsePrimaryMonitor` fallback is allowed only when consumer opts into it.
4. Size and offsets are Windows desktop pixels, not Avalonia DIPs.
5. Monitor-relative placement uses selected monitor work area by default.
6. The final rectangle must be applied before the session is handed to test scenario code.
7. The final rectangle must fully fit inside the selected area; partial offscreen placement is not allowed in this increment.
8. Placement failure during launch must run the same cleanup path as main-window timeout or automation-tree readiness failure.
9. Diagnostic messages must not hide fallback behavior, public option validation failures or explicit geometry failures.

## 8. Точки интеграции и триггеры
- `DesktopAppSession.Launch(DesktopAppLaunchOptions options)` triggers placement after main window discovery.
- `AvaloniaDesktopLaunchHost.CreateLaunchOptions(...)` maps `AvaloniaDesktopLaunchOptions.WindowPlacement` to `DesktopAppLaunchOptions.WindowPlacement`.
- Consumer `TestHost` can expose placement as optional parameter so all FlaUI tests share the same setup.
- README examples should guide consumers to configure placement in `TestHost`, not inside each page object or scenario.

## 9. Изменения модели данных / состояния
- Новые public in-memory option/model types in `AppAutomation.Session.Contracts`.
- No persisted data.
- No scenario payload or generated authoring file format changes.
- No environment variable contract in this increment.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - add nullable placement property;
  - default null keeps behavior unchanged;
  - update sample/template hosts to demonstrate optional placement parameter.
- Backward compatibility:
  - source compatibility preserved for existing object initializers and method calls;
  - binary compatibility impact is limited to adding members/types in packages.
- Rollback:
  - remove usage from sample/template/README;
  - keep or remove additive public types before release depending on package policy;
  - no data migration required.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- `DesktopAppLaunchOptions.WindowPlacement` exists and is documented.
- `AvaloniaDesktopLaunchOptions.WindowPlacement` exists and is copied into `DesktopAppLaunchOptions`.
- `DesktopAppSession.Launch` applies requested placement for FlaUI sessions before returning.
- Invalid monitor selector produces an actionable exception listing available monitors.
- Invalid public model values fail fast: negative monitor index, empty device name, non-positive size and unknown enum values.
- Offset-caused out-of-area placement fails fast; the window is not intentionally placed partially offscreen.
- Placement failure after process launch cleans up automation, application process and dispose callback.
- `WindowPlacement = null` preserves old launch behavior.
- Sample/test template `CreateDesktopLaunchOptions` exposes optional placement configuration.
- README has English and Russian sections explaining monitor/size/position usage and limitations.

Tests to add/change:
- `AppAutomation.TestHost.Avalonia.Tests`
  - `AvaloniaDesktopLaunchHost_CreateLaunchOptions_CopiesWindowPlacement`.
- `DotnetDebug.AppAutomation.FlaUI.Tests`
  - unit tests for `DesktopWindowPlacement` validation/factory behavior if public factories exist;
  - internal geometry tests for monitor work area, anchor/offset, explicit oversized geometry failure and stable monitor ordering;
  - internal geometry test for offset-caused out-of-area failure;
  - invalid monitor resolution test with fake monitor source or service abstraction;
  - launch cleanup regression test where placement throws after process launch and verifies process/session cleanup path through a controllable fake service or narrow integration seam;
  - guarded runtime smoke: launch sample with primary monitor placement and assert bounding rectangle approximately matches requested size/area, skipped when desktop UI unavailable.
- Existing `DesktopAppLaunchProcessStartInfoTests` should remain unchanged unless adding placement validation belongs there.
- Build/template docs tests may need update if README/template content checks exist.

Commands for EXEC verification:
```powershell
dotnet test --project .\tests\AppAutomation.TestHost.Avalonia.Tests\AppAutomation.TestHost.Avalonia.Tests.csproj --no-restore
dotnet test --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-restore
dotnet test --project .\tests\AppAutomation.Build.Tests\AppAutomation.Build.Tests.csproj --no-restore
dotnet build .\AppAutomation.sln
dotnet test --solution .\AppAutomation.sln --no-build
```

Stop rules для test/retrieval/tool/validation loops:
- If guarded FlaUI runtime smoke cannot run because no interactive desktop is available, record skip reason and rely on unit/contract tests plus build.
- If monitor placement is flaky because a real user's desktop state changes during the run, do not broaden the feature; tighten helper diagnostics/tolerance and keep deterministic geometry tests separate from runtime smoke.
- Stop after full solution test pass or after isolating unrelated pre-existing FlaUI UIA failure with evidence.

## 12. Риски и edge cases
- DPI mismatch: Windows desktop pixels differ from Avalonia DIPs. Mitigation: document pixel semantics and assert outer window bounds, not app content DIPs.
- Monitor ordering can surprise users. Mitigation: primary-first stable ordering and support device-name selector.
- Requested size can exceed monitor work area. Mitigation: fail fast for explicit size with requested size and selected area in the error.
- Offset can push an otherwise valid size outside the selected area. Mitigation: fail fast; no partial offscreen placement in this increment.
- Some windows may be non-resizable. Mitigation: placement failure reports `SetWindowPos`/bounds convergence details.
- AUT may change size after startup. Mitigation: apply placement after main window discovery and verify before returning; if AUT later changes itself, that remains app behavior.
- Placement can fail after process start. Mitigation: placement runs inside launch exception cleanup, so failed setup does not leave AUT processes or automation resources alive.
- Interactive desktop tests may still steal focus. Mitigation: README names this limitation and positions feature as interference reduction, not elimination.
- Running tests in parallel on the same desktop can still collide. Mitigation: non-goal; consumers should serialize FlaUI tests or use dedicated desktop sessions.

## 13. План выполнения
1. Add contract types and `DesktopAppLaunchOptions.WindowPlacement` with XML docs.
2. Add `AvaloniaDesktopLaunchOptions.WindowPlacement` and map it in `AvaloniaDesktopLaunchHost`.
3. Implement internal monitor enumeration, geometry calculation and placement application in `AppAutomation.FlaUI`.
4. Integrate placement into `DesktopAppSession.Launch` between main window discovery and automation tree readiness.
5. Update sample and template `CreateDesktopLaunchOptions` to accept optional placement.
6. Add focused unit/contract tests.
7. Add guarded FlaUI runtime smoke if reliable in current test infrastructure.
8. Update README English and Russian sections.
9. Run targeted tests, build and full solution tests.
10. Perform post-EXEC review and update this spec journal/result.

## 14. Открытые вопросы
Нет блокирующих вопросов.

Design decisions for approval:
- Default behavior is opt-in only: no placement unless `WindowPlacement` is set.
- Explicit missing monitor fails by default; fallback to primary requires opt-in `UsePrimaryMonitor`.
- Coordinates and sizes use Windows desktop pixels.
- README will document that FlaUI remains interactive automation and may take focus.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
  - No blocking UI-thread work in AUT; placement is outside AUT through FlaUI/Win32 after launch.
  - User flow selectors/page objects remain unchanged.
  - Desktop-specific code stays outside shared contracts except plain option models.
- Профиль: `ui-automation-testing`
  - Feature directly improves deterministic FlaUI UI test setup.
  - Tests include contract/unit checks and guarded desktop UI verification.
  - Stable selectors are unaffected.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Session.Contracts/DesktopAppLaunchOptions.cs` | Add `WindowPlacement` and placement contract types/XML docs | Public launch contract for deterministic FlaUI window geometry |
| `src/AppAutomation.TestHost.Avalonia/AvaloniaDesktopLaunchOptions.cs` | Add `WindowPlacement` | Let Avalonia consumers configure placement through existing launch helper |
| `src/AppAutomation.TestHost.Avalonia/AvaloniaDesktopLaunchHost.cs` | Map placement into `DesktopAppLaunchOptions` | Preserve current helper flow |
| `src/AppAutomation.FlaUI/Session/DesktopAppSession.cs` | Apply placement during launch | Make placement automatic for every FlaUI session |
| `src/AppAutomation.FlaUI/Session/DesktopWindowPlacementService.cs` | New internal monitor/geometry/Win32 placement helper | Isolate Windows-specific behavior and make it testable |
| `sample/DotnetDebug.AppAutomation.TestHost/DotnetDebugAppLaunchHost.cs` | Add optional `windowPlacement` parameter | Demonstrate consumer pattern in sample |
| `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/tests/SampleApp.AppAutomation.TestHost/SampleAppAppLaunchHost.cs` | Add optional `windowPlacement` parameter | Generated projects expose the new convenience path |
| `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs` | Add propagation test | Verify test host contract mapping |
| `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/*` | Add placement unit/guarded smoke tests | Verify FlaUI behavior and validation |
| `README.md` | Document feature in English/Russian sections | User-facing discoverability |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| FlaUI window position | OS/AUT decides | Test host can select monitor and placement |
| FlaUI window size | OS/AUT/default app size | Optional fixed outer window size |
| Multi-monitor determinism | Not documented/configured | Primary/index/device selector with explicit semantics |
| Interference with user's work | Test window can appear anywhere | Consumers can route tests to dedicated monitor/work area |
| README guidance | Run FlaUI after Headless | Run FlaUI with optional deterministic window placement |
| Existing tests | Unchanged behavior | Unchanged unless `WindowPlacement` is provided |

## 18. Альтернативы и компромиссы
- Вариант: pass environment variables to AUT and let Avalonia app place itself.
  - Плюсы: no Win32 resize after launch; app can set DIPs before first render.
  - Минусы: требует consumer app code/bootstrap support; не универсально для existing AUT; сложнее для generated tests.
  - Почему не выбран: задача про удобную framework-level возможность для FlaUI tests.
- Вариант: use only FlaUI `Window.Move(...)`.
  - Плюсы: меньше Win32 code.
  - Минусы: FlaUI Core exposes move but not enough resize/monitor APIs for full requirement.
  - Почему не выбран: нужен размер, монитор и рабочая область.
- Вариант: add per-test fluent API on `DesktopAppSession`.
  - Плюсы: можно менять placement внутри сценария.
  - Минусы: encourages manual setup in each test and applies too late for deterministic initial layout.
  - Почему не выбран: placement belongs to launch configuration.
- Вариант: silently fallback to primary monitor when requested monitor missing.
  - Плюсы: fewer broken runs on laptops/docking changes.
  - Минусы: может неожиданно мешать пользователю на primary monitor.
  - Почему не выбран: fail-fast default safer; fallback remains opt-in.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели дизайна и Non-Goals описаны. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, API, алгоритм, integration points, state и rollout указаны. |
| C. Безопасность изменений | 11-13 | PASS | Additive contract, null-default compatibility, rollback и desktop limitations зафиксированы. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, cleanup regression, geometry edge tests и команды проверки перечислены. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, alternatives и design decisions не содержат блокирующих вопросов. |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation constraints отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Цель ограничена launch-time FlaUI window placement and README docs; unrelated isolation excluded. |
| 2. Понимание текущего состояния | 5 | Existing launch contract, Avalonia host mapping and FlaUI session flow captured. |
| 3. Конкретность целевого дизайна | 5 | Public API, monitor selection, geometry algorithm, errors and README contract defined. |
| 4. Безопасность (миграция, откат) | 5 | Additive nullable options; no persisted state; rollback simple. |
| 5. Тестируемость | 5 | Unit, contract, cleanup regression, geometry edge tests and guarded runtime tests plus commands listed. |
| 6. Готовность к автономной реализации | 5 | Execution steps, file list and tradeoffs are explicit; no blocking questions. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - Зафиксировано, что feature снижает interference, но не обещает background FlaUI без focus.
  - Уточнено, что shared contracts не получают Windows-only dependencies.
  - Добавлены fail-fast semantics для missing explicit monitor и opt-in fallback.
  - Explicit oversized geometry заменена с clamping на fail-fast, чтобы не скрывать ошибку настройки.
  - По review исправлены out-of-bounds semantics: final rectangle must fit selected area, offset overflow fails fast.
  - По review добавлен cleanup invariant для placement failure после запуска AUT.
  - По review добавлена явная validation policy для malformed public model values.
  - Уточнены coordinate units: Windows desktop pixels, not Avalonia DIPs.
  - Добавлен README contract для обеих языковых частей.
- Что осталось на решение пользователя: подтвердить спецификацию фразой `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS после follow-up исправлений
- Реализация соответствует подтверждённой спецификации:
  - `DesktopAppLaunchOptions` and `AvaloniaDesktopLaunchOptions` получили nullable `WindowPlacement`, default `null` сохраняет прежний launch behavior;
  - `AvaloniaDesktopLaunchHost` пробрасывает placement в runtime launch options;
  - FlaUI placement применяется внутри `DesktopAppSession.Launch` до возврата session and before automation tree readiness handoff;
  - monitor selection supports primary/index/device name/last available, stable ordering, work area by default, fail-fast invalid geometry, explicit primary fallback and actionable monitor diagnostics;
  - native window placement and verification use Win32 outer window rectangle, not UIA `BoundingRectangle`, because UIA bounds can exclude invisible window frame;
  - placement failure after AUT launch remains inside the existing launch cleanup path and has a dedicated cleanup regression test;
  - sample/template `CreateDesktopLaunchOptions` expose optional `windowPlacement`;
  - README documents the feature in English and Russian.
- Что исправлено до завершения:
  - Runtime smoke initially showed UIA bounds differ from native outer bounds; placement verification was moved to Win32 `GetWindowRect`.
  - Added explicit cleanup regression for placement failure after process start.
  - Added executable path and process id context to runtime placement failure messages.
  - Removed a new nullable warning in `DesktopWindowPlacementService`.
  - Follow-up review fixes: recorder launch option cloning now preserves `WindowPlacement`; placement runtime smoke now skips on desktop sessions whose primary working area cannot fit the requested smoke geometry.
  - Centralized sample/template FlaUI defaults now use `DesktopMonitorSelector.LastAvailable`, so UI tests that call the shared launch host run on the last monitor in the stable order without per-test placement setup.
  - Clarified and tested that central default placement omits `Size`, so the app's current outer size is preserved unless the caller passes an explicit size.
- Verification:
  - `dotnet test --project .\tests\AppAutomation.TestHost.Avalonia.Tests\AppAutomation.TestHost.Avalonia.Tests.csproj --no-build` -> PASS, 15/15.
  - `dotnet run --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-build -- --treenode-filter "/*/*/DesktopWindowPlacementTests/*" --no-progress --output Detailed --timeout 180s` -> PASS, 9/9.
  - `dotnet test --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-build` -> PASS, 41/41 before cleanup regression; final full solution run covered 42/42 in this assembly.
  - `dotnet test --project .\tests\AppAutomation.Build.Tests\AppAutomation.Build.Tests.csproj --no-build` -> PASS, 19/19.
  - `dotnet build .\AppAutomation.sln --no-restore` -> PASS, 0 errors; existing warnings remain (`NU1903` for `Tmds.DBus.Protocol`, existing analyzer warnings in `AppAutomation.FlaUI`).
  - `dotnet test --solution .\AppAutomation.sln --no-build` -> PASS, 266/266.
  - Follow-up review verification:
    - `dotnet build .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-restore` -> PASS.
    - `dotnet run --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-build -- --treenode-filter "/*/*/DesktopWindowPlacementTests/*" --no-progress --output Detailed --timeout 180s` -> PASS, 9/9.
    - `dotnet run --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-build -- --treenode-filter "/*/*/DotnetDebugRecorderDesktopSmokeTests/RecorderLaunchOptionsMergeRecorderEnvironmentAndPreserveBaseOptions" --no-progress --output Detailed --timeout 180s` -> PASS, 1/1.
    - `dotnet test --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-build` -> PASS, 42/42.
  - Central last-monitor verification:
    - `dotnet build .\AppAutomation.sln --no-restore` -> PASS, 0 errors; existing warnings remain.
    - `dotnet test --project .\sample\DotnetDebug.Tests\DotnetDebug.Tests.csproj --no-build` -> PASS, 21/21.
    - `dotnet test --project .\tests\AppAutomation.Build.Tests\AppAutomation.Build.Tests.csproj --no-build` -> PASS, 19/19.
    - `dotnet test --project .\tests\AppAutomation.TestHost.Avalonia.Tests\AppAutomation.TestHost.Avalonia.Tests.csproj --no-build` -> PASS, 15/15.
    - `dotnet run --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-build -- --treenode-filter "/*/*/DesktopWindowPlacementTests/*" --no-progress --output Detailed --timeout 180s` -> PASS, 10/10.
    - `dotnet test --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-build` -> PASS, 43/43.
    - First `dotnet test --solution .\AppAutomation.sln --no-build` attempt failed once in unrelated `AppAutomation.Recorder.Avalonia.Tests/Overlay_Attach_AppliesDarkPaletteResources` with Avalonia concurrent collection error; immediate recorder assembly rerun passed 68/68.
    - Repeated `dotnet test --solution .\AppAutomation.sln --no-build` -> PASS, 267/267.
  - Explicit-size follow-up verification:
    - `dotnet test --project .\sample\DotnetDebug.Tests\DotnetDebug.Tests.csproj` -> PASS, 21/21.
    - `dotnet build .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-restore` -> PASS.
    - `dotnet run --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-build -- --treenode-filter "/*/*/DesktopWindowPlacementTests/*" --no-progress --output Detailed --timeout 180s` -> PASS.
- Остаточные риски / follow-ups:
  - Existing dependency/analyzer warnings remain outside this change.

## Approval
Подтверждено пользователем фразой: "спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Анализ инструкций и маршрутизация QUEST | 0.96 | Нет | Собрать контекст проекта | Нет | Нет | Central rules require SPEC-first and only current spec mutation before approval | `C:\Projects\My\Agents\AGENTS.md`, central QUEST docs |
| SPEC | Анализ текущего FlaUI launch pipeline | 0.9 | Нет | Создать рабочую спеку | Нет | Нет | Existing contracts and session flow show the right extension points: launch options, Avalonia host mapping and FlaUI session placement before scenario code | `src/AppAutomation.Session.Contracts/DesktopAppLaunchOptions.cs`, `src/AppAutomation.TestHost.Avalonia/*`, `src/AppAutomation.FlaUI/Session/DesktopAppSession.cs`, `README.md` |
| SPEC | Проектирование placement API, tests and README contract | 0.92 | Подтверждение пользователя для EXEC | Ожидать подтверждение спеки | Да | Нет | Additive nullable launch option gives deterministic placement without changing existing test behavior | `specs/2026-05-09-flaui-window-placement.md` |
| SPEC | Post-SPEC review и уточнение fail-fast геометрии | 0.95 | Подтверждение пользователя для EXEC | Ожидать подтверждение спеки | Да | Нет | Убрана неоднозначность clamping vs fail-fast: explicit oversized geometry теперь считается ошибкой настройки | `specs/2026-05-09-flaui-window-placement.md` |
| SPEC | Review fixes после запроса пользователя | 0.96 | Подтверждение пользователя для EXEC | Ожидать подтверждение спеки | Да | Да, пользователь попросил исправить review findings | Зафиксированы fit-inside-area semantics, cleanup invariant для placement failure и validation policy для malformed public options | `specs/2026-05-09-flaui-window-placement.md` |
| EXEC | Старт реализации после подтверждения | 0.94 | Нет | Добавить contract types и host propagation | Нет | Да, пользователь подтвердил спеку | Approval получен, можно менять код, тесты и README строго в рамках спеки | `specs/2026-05-09-flaui-window-placement.md` |
| EXEC | Public contract и Avalonia host propagation | 0.93 | Нет | Реализовать FlaUI placement service | Нет | Нет | Добавлены placement value objects, nullable `WindowPlacement` in `DesktopAppLaunchOptions` and `AvaloniaDesktopLaunchOptions`, host maps option through existing launch helper | `src/AppAutomation.Session.Contracts/*`, `src/AppAutomation.TestHost.Avalonia/*` |
| EXEC | FlaUI placement service, sample/template hooks, tests and README draft | 0.88 | Результаты компиляции и targeted tests | Запустить targeted build/tests и исправить ошибки | Нет | Нет | Реализован Win32 monitor/window placement, nullable sample/template parameter, geometry/validation tests, guarded runtime smoke and bilingual README docs | `src/AppAutomation.FlaUI/Session/*`, `sample/DotnetDebug.AppAutomation.*`, `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs`, `README.md` |
| EXEC | Runtime smoke correction and cleanup regression | 0.93 | Результаты full verification | Повторить full build/test и выполнить post-EXEC review | Нет | Нет | UIA bounds did not equal native outer bounds, so placement verification moved to `GetWindowRect`; added cleanup regression and richer placement failure context | `src/AppAutomation.FlaUI/Session/DesktopWindowPlacementService.cs`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/DesktopWindowPlacementTests.cs` |
| EXEC | Финальная проверка и post-EXEC review | 0.97 | Нет | Завершить задачу | Нет | Нет | Targeted tests, build tests, solution build and full solution tests passed; follow-up review findings were handled in the next EXEC step | `specs/2026-05-09-flaui-window-placement.md`, changed code/tests/docs |
| EXEC | Follow-up review fixes | 0.96 | Нет | Завершить задачу | Нет | Да, пользователь подтвердил исправление review findings | Preserved `WindowPlacement` through recorder option cloning and made runtime placement smoke tolerant of small primary work areas; targeted and full FlaUI assembly checks passed | `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/DotnetDebugRecorderDesktopSmokeTests.cs`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/DesktopWindowPlacementTests.cs`, `src/AppAutomation.FlaUI/Session/DesktopWindowPlacementService.cs`, `specs/2026-05-09-flaui-window-placement.md` |
| EXEC | Central last-monitor UI default | 0.95 | Нет | Завершить задачу | Нет | Да, пользователь попросил централизованно запускать UI tests on last available monitor | Added `DesktopMonitorSelector.LastAvailable`, resolved it in FlaUI using existing stable monitor ordering, and made sample/template desktop launch hosts use it as the central default while preserving per-call overrides | `src/AppAutomation.Session.Contracts/DesktopWindowPlacement.cs`, `src/AppAutomation.FlaUI/Session/DesktopWindowPlacementService.cs`, `sample/DotnetDebug.AppAutomation.TestHost/DotnetDebugAppLaunchHost.cs`, `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/tests/SampleApp.AppAutomation.TestHost/SampleAppAppLaunchHost.cs`, `README.md`, tests |
| EXEC | Explicit size invariant follow-up | 0.97 | Нет | Обновить PR branch | Нет | Да, пользователь уточнил поведение when caller does not pass explicit size | Added tests and README wording that central last-monitor placement does not set `Size`; resolver preserves current outer size unless caller passes explicit size | `sample/DotnetDebug.Tests/LaunchOptionsDefaultsTests.cs`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/DesktopWindowPlacementTests.cs`, `README.md`, `specs/2026-05-09-flaui-window-placement.md` |
