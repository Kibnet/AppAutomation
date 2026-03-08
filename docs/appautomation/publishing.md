# AppAutomation Publishing

Этот репозиторий публикует только packable `AppAutomation.*` packages.

## Источник версии

Lockstep-versioning живёт в [eng/Versions.props](../../eng/Versions.props):

- `EasyUseVersion`
- `EasyUsePrereleaseSuffix`

Все publishable пакеты читают версию оттуда.

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
pwsh -File eng/pack.ps1 -Configuration Release -VersionSuffix preview.1
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

- `workflow_dispatch`
- push tag `appautomation-v<version>`

Pipeline делает:

1. `dotnet restore`
2. `dotnet build`
3. `dotnet test`
4. `pwsh -File eng/pack.ps1`
5. `pwsh -File eng/smoke-consumer.ps1`
6. `pwsh -File eng/publish-nuget.ps1`

## Минимальный release checklist

```powershell
dotnet restore
dotnet build DotnetDebug.sln -c Release
dotnet test --solution DotnetDebug.sln -c Release
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```

Только после этого есть смысл пушить теги или запускать publish workflow.
