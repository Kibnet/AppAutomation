# AppAutomation

`AppAutomation` это reusable framework для desktop UI automation на .NET. Этот репозиторий содержит:

- publishable пакеты `AppAutomation.*`;
- demo AUT `DotnetDebug` / `DotnetDebug.Avalonia`;
- reference consumer topology, на которой framework сам же и проверяется.

`DotnetDebug` здесь вторичен: это внутренний стенд и пример того, как подключать framework в реальный solution.

## Пакеты

| Пакет | Назначение |
| --- | --- |
| `AppAutomation.Abstractions` | Канонические automation contracts, waits, locator manifest и diagnostics. |
| `AppAutomation.Authoring` | Source generator/analyzers для `[UiControl(...)]` page objects. |
| `AppAutomation.Session.Contracts` | Launch/session contracts для runtime adapters и repo-specific test hosts. |
| `AppAutomation.TUnit` | `UiTestBase` и общие test helpers для `TUnit`. |
| `AppAutomation.Avalonia.Headless` | In-process Avalonia Headless runtime. |
| `AppAutomation.FlaUI` | Windows desktop runtime adapter поверх FlaUI. |

## С чего начать

- Быстрый consumer setup: [docs/appautomation/quickstart.md](docs/appautomation/quickstart.md)
- Какая topology нужна в solution: [docs/appautomation/project-topology.md](docs/appautomation/project-topology.md)
- Как собирать и публиковать пакеты: [docs/appautomation/publishing.md](docs/appautomation/publishing.md)

Рекомендуемый минимальный набор проектов у consumer-а:

- `<MyApp>.UiTests.Authoring` для page objects и shared scenarios;
- `<MyApp>.UiTests.Headless` для headless runtime tests;
- `<MyApp>.UiTests.FlaUI` для Windows desktop tests;
- optional `<MyApp>.AppAutomation.TestHost` для repo-specific build/launch wiring.

## Reference Implementation In This Repo

| Проект | Роль |
| --- | --- |
| `src/AppAutomation.Abstractions` | Framework contracts |
| `src/AppAutomation.Authoring` | Generator/analyzers |
| `src/AppAutomation.Session.Contracts` | Launch/session contracts |
| `src/AppAutomation.TUnit` | Test base/helpers |
| `src/AppAutomation.Avalonia.Headless` | Headless runtime |
| `src/AppAutomation.FlaUI` | FlaUI runtime |
| `src/DotnetDebug.Avalonia` | Demo AUT |
| `src/DotnetDebug.AppAutomation.TestHost` | Repo-only launch/bootstrap layer |
| `tests/DotnetDebug.AppAutomation.Authoring` | Shared authoring project |
| `tests/DotnetDebug.AppAutomation.Avalonia.Headless.Tests` | Headless consumer example |
| `tests/DotnetDebug.AppAutomation.FlaUI.Tests` | FlaUI consumer example |

## Требования

- stable `.NET SDK 10.0.103`, закреплённый в [global.json](global.json);
- Windows для `AppAutomation.FlaUI`;
- любая ОС для contracts/generators и Avalonia Headless.

## Локальная разработка

```powershell
dotnet restore
dotnet build DotnetDebug.sln -c Release
dotnet test --solution DotnetDebug.sln -c Release
dotnet pack DotnetDebug.sln -c Release
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```

## Публикация пакетов

Локально:

```powershell
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/publish-nuget.ps1 -Source https://api.nuget.org/v3/index.json -ApiKey <key>
```

В GitHub:

- workflow: `.github/workflows/publish-packages.yml`;
- ручной запуск: `workflow_dispatch`;
- tag-based release: `appautomation-v<version>`.

## Demo AUT

CLI:

```powershell
dotnet run --project src/DotnetDebug/DotnetDebug.csproj -- 48 18 30
```

Avalonia UI:

```powershell
dotnet run --project src/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj
```
