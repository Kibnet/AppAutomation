# API Migration Map (Phase 2)

Дата: 2026-03-06

## Old -> New

| Старый API | Новый API | Статус | Где обновлено |
| --- | --- | --- | --- |
| `FlaUI.EasyUse.Session.DesktopProjectLaunchOptions` | `EasyUse.Session.Contracts.DesktopProjectLaunchOptions` | replaced | `src/FlaUI.EasyUse/Session/DesktopAppSession.cs`, `src/Avalonia.Headless.EasyUse/Session/DesktopSession.cs`, `tests/*` |
| `FlaUI.EasyUse.Session.DesktopAppLaunchOptions` | `EasyUse.Session.Contracts.DesktopAppLaunchOptions` | replaced | `src/FlaUI.EasyUse/Session/DesktopAppSession.cs`, `src/Avalonia.Headless.EasyUse/Session/DesktopSession.cs` |
| `FlaUI.EasyUse.TUnit.UiAssert` | `EasyUse.TUnit.Core.UiAssert` | replaced | `tests/DotnetDebug.UiTests.Shared/Tests/MainWindowScenariosBase.cs` |
| `FlaUI.EasyUse.TUnit.DesktopUiTestBase<TPage>` | `EasyUse.TUnit.Core.UiTestBase<TSession, TPage>` | redesign+replaced | `tests/DotnetDebug.UiTests.Shared/Tests/MainWindowScenariosBase.cs`, runtime fixtures |
| `Avalonia.Headless.EasyUse.TUnit.HeadlessUiTestBase<TPage>` | `EasyUse.TUnit.Core.UiTestBase<TSession, TPage>` + headless runtime session adapter | redesign+replaced | `tests/DotnetDebug.UiTests.Avalonia.Headless/Tests/MainWindowHeadlessRuntimeTests.cs` |
| `FlaUI.EasyUse.Waiting.UiWait` | `EasyUse.TUnit.Core.Waiting.UiWait` | replaced (tests) | `tests/DotnetDebug.Tests/UiWaitTests.cs` |
| `FlaUI.EasyUse.Waiting.UiWaitOptions` | `EasyUse.TUnit.Core.Waiting.UiWaitOptions` | replaced (tests) | `tests/DotnetDebug.Tests/UiWaitTests.cs` |
| `FlaUI.EasyUse.Waiting.UiWaitResult<T>` | `EasyUse.TUnit.Core.Waiting.UiWaitResult<T>` | replaced (tests) | `tests/DotnetDebug.Tests/UiWaitTests.cs` |

## Удалено

- `src/FlaUI.EasyUse.TUnit/*` (legacy проект удалён, каталог оставлен пустым).
- `src/Avalonia.Headless.EasyUse.TUnit/*` (legacy проект удалён, каталог оставлен пустым).
- Переходные bridge-файлы `*.New.cs` в `src/Avalonia.Headless.EasyUse/*` для Session/TUnit/Waiting/Conditions/Definitions/PageObjects.

## Namespace/Dependency итог

- `Avalonia.Headless` session runtime теперь канонически в `Avalonia.Headless.EasyUse.Session`.
- Launch contracts определены только в `EasyUse.Session.Contracts`.
- Общая TUnit lifecycle/assert/wait API определена в `EasyUse.TUnit.Core`.
