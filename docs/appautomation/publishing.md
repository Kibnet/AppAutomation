# AppAutomation Publishing

**English** | [Русский](#русская-версия)

This repo publishes not only runtime libraries, but also consumer-adoption assets.

## Packaged Artifacts

The following go into the local package folder:

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

Local source of truth:

- [eng/Versions.props](../../eng/Versions.props)

GitHub release path:

- tag `<version>` or `appautomation-v<version>`

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

If the consumer organization uses an internal mirror:

- publish packages to the corporate feed;
- on the consumer side, configure `NuGet.Config` / `packageSourceMapping`;
- don't switch to source dependency as the primary delivery path.

## Release Checklist

```powershell
dotnet build AppAutomation.sln -c Release
dotnet test AppAutomation.sln -c Release
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```

Publishing without these steps is not considered validated.

---

<a id="русская-версия"></a>

## Русская версия

[English](#appautomation-publishing) | **Русский**

Этот репозиторий публикует не только библиотеки среды выполнения, но и материалы для подключения проекта-потребителя.

## Публикуемые артефакты

В локальный каталог пакетов попадают:

- `AppAutomation.Abstractions`
- `AppAutomation.Authoring`
- `AppAutomation.Session.Contracts`
- `AppAutomation.TUnit`
- `AppAutomation.Avalonia.Headless`
- `AppAutomation.FlaUI`
- `AppAutomation.TestHost.Avalonia`
- `AppAutomation.Tooling`
- `AppAutomation.Templates`

## Источник версии

Локальный источник истины:

- [eng/Versions.props](../../eng/Versions.props)

Тег релиза в GitHub:

- тег `<version>` или `appautomation-v<version>`

## Локальная упаковка

```powershell
pwsh -File eng/pack.ps1 -Configuration Release
```

Артефакты:

```text
artifacts/packages/<version>/
```

## Быстрая проверка потребителя

```powershell
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```

Текущая быстрая проверка подтверждает три вещи:

1. потребитель, работающий только через пакеты для `Authoring` и сред выполнения, может восстановить зависимости и собрать решение;
2. пакет шаблонов устанавливается и создаёт стандартную структуру;
3. `appautomation doctor` успешно проходит на сгенерированном репозитории-потребителе.

## Публикация

```powershell
pwsh -File eng/publish-nuget.ps1 `
  -Version 2.1.0 `
  -Source https://api.nuget.org/v3/index.json `
  -ApiKey <api-key>
```

Необязательные переменные окружения:

- `NUGET_SOURCE`
- `NUGET_API_KEY`
- `NUGET_SYMBOL_SOURCE`
- `NUGET_SYMBOL_API_KEY`

## Рекомендации для корпоративного источника пакетов

Если организация-потребитель использует внутреннее зеркало:

- публикуйте пакеты в корпоративный источник;
- на стороне потребителя настраивайте `NuGet.Config` / `packageSourceMapping`;
- не переходите на зависимость через исходный код как на основной способ поставки.

## Проверочный список перед выпуском

```powershell
dotnet build AppAutomation.sln -c Release
dotnet test AppAutomation.sln -c Release
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```

Публикация без этих шагов не считается подтверждённой.
