# AppAutomation Publishing

Этот репозиторий публикует только packable `AppAutomation.*` packages.

## Источник версии

Локальный fallback version живёт в [eng/Versions.props](../../eng/Versions.props):

- `AppAutomationVersion`

Локальные pack/publish сценарии используют это значение по умолчанию.

В GitHub Actions production source of truth другой:

- GitHub Release tag `appautomation-v<version>`

При публикации через `release.published` package version берётся из tag релиза и явно прокидывается в scripts.

## Локальная упаковка

```powershell
pwsh -File eng/pack.ps1 -Configuration Release
```

По умолчанию пакеты складываются в:

```text
artifacts/packages/<version>/
```

Пример:

```text
artifacts/packages/2.1.0/
```

Можно переопределить prerelease suffix на время упаковки:

```powershell
pwsh -File eng/pack.ps1 -Configuration Release -Version 2.1.0-preview.1
```

## Локальный smoke consumer

Перед публикацией стоит подтвердить, что пакетами реально можно пользоваться извне:

```powershell
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```

Этот скрипт:

- берёт пакеты из локального `artifacts/packages/<version>`;
- поднимает временный consumer workspace;
- собирает authoring project через `PackageReference` на `AppAutomation.Authoring`;
- проверяет, что source generator действительно сработал из NuGet package.

## Публикация в feed

```powershell
pwsh -File eng/publish-nuget.ps1 `
  -Version 2.1.0 `
  -Source https://api.nuget.org/v3/index.json `
  -ApiKey <api-key>
```

Можно использовать environment variables:

- `NUGET_SOURCE`
- `NUGET_API_KEY`
- `NUGET_SYMBOL_SOURCE`
- `NUGET_SYMBOL_API_KEY`

Если `PackagesPath` не указан, скрипт берёт:

```text
artifacts/packages/<version>
```

## GitHub workflow

Файл workflow:

```text
.github/workflows/publish-packages.yml
```

Trigger-ы:

- `release.published`
- `workflow_dispatch`

Pipeline делает:

1. `dotnet restore`
2. `dotnet build`
3. `dotnet test`
4. извлекает package version из release tag или manual input
5. `pwsh -File eng/pack.ps1 -Version <version>`
6. `pwsh -File eng/smoke-consumer.ps1 -Version <version>`
7. `pwsh -File eng/publish-nuget.ps1 -Version <version>`

## Минимальный release checklist

```powershell
dotnet restore
dotnet build AppAutomation.sln -c Release
dotnet test --solution AppAutomation.sln -c Release
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```

Только после этого есть смысл публиковать GitHub Release с tag `appautomation-v<version>` или запускать manual publish workflow с явным `version`.
