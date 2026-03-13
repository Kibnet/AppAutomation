# AppAutomation Publishing

Этот repo публикует не только runtime libraries, но и consumer-adoption assets.

## Packaged Artifacts

В локальный package folder попадают:

- `AppAutomation.Abstractions`
- `AppAutomation.Authoring`
- `AppAutomation.Session.Contracts`
- `AppAutomation.TUnit`
- `AppAutomation.Avalonia.Headless`
- `AppAutomation.FlaUI`
- `AppAutomation.TestHost.Avalonia`
- `AppAutomation.Tooling`
- `AppAutomation.Templates`

## Version Source

Локальный source of truth:

- [eng/Versions.props](../../eng/Versions.props)

GitHub release path:

- tag `<version>` или `appautomation-v<version>`

## Local Pack

```powershell
pwsh -File eng/pack.ps1 -Configuration Release
```

Artifacts:

```text
artifacts/packages/<version>/
```

## Consumer Smoke

```powershell
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```

Smoke now validates three things:

1. package-only authoring/runtime consumer can restore/build;
2. template package installs and generates canonical topology;
3. `appautomation doctor` succeeds against generated consumer repo.

## Publish

```powershell
pwsh -File eng/publish-nuget.ps1 `
  -Version 2.1.0 `
  -Source https://api.nuget.org/v3/index.json `
  -ApiKey <api-key>
```

Optional environment variables:

- `NUGET_SOURCE`
- `NUGET_API_KEY`
- `NUGET_SYMBOL_SOURCE`
- `NUGET_SYMBOL_API_KEY`

## Enterprise Feed Guidance

Если consumer organisation использует internal mirror:

- публикуйте packages в корпоративный feed;
- на consumer side настраивайте `NuGet.Config` / `packageSourceMapping`;
- не переходите на source dependency как основной delivery path.

## Release Checklist

```powershell
dotnet build AppAutomation.sln -c Release
dotnet test AppAutomation.sln -c Release
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```

Публикация без этих шагов не считается validated.
