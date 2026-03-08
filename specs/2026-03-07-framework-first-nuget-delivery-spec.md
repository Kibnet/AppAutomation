# Framework-First Repo Positioning, Consumer Onboarding and NuGet Delivery

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `refactor-architecture`
- Владелец: Framework Maintainers
- Масштаб: medium
- Целевая ветка: текущая рабочая ветка
- Ограничения:
  - Не менять `PackageId`, публичные namespace и рабочий сценарный DSL без отдельного согласования.
  - Не менять пользовательское поведение `DotnetDebug.Avalonia`.
  - Не ухудшать текущую работоспособность runtime-адаптеров `AppAutomation.FlaUI` и `AppAutomation.Avalonia.Headless`.
  - Перед завершением сохранить зелёными `dotnet build`, `dotnet test`, `dotnet pack` и package smoke.
  - Репозиторий публикуется из GitHub-remote `git@github.com:Kibnet/DotnetDebug.git`.
- Связанные ссылки:
  - `README.md`
  - `Directory.Build.props`
  - `eng/Versions.props`
  - `src/AppAutomation.Abstractions/AppAutomation.Abstractions.csproj`
  - `src/AppAutomation.Authoring/AppAutomation.Authoring.csproj`
  - `src/AppAutomation.Session.Contracts/AppAutomation.Session.Contracts.csproj`
  - `src/AppAutomation.TUnit/AppAutomation.TUnit.csproj`
  - `src/AppAutomation.FlaUI/AppAutomation.FlaUI.csproj`
  - `src/AppAutomation.Avalonia.Headless/AppAutomation.Avalonia.Headless.csproj`
  - `tests/DotnetDebug.AppAutomation.Authoring/DotnetDebug.AppAutomation.Authoring.csproj`
  - `tests/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/DotnetDebug.AppAutomation.Avalonia.Headless.Tests.csproj`
  - `tests/DotnetDebug.AppAutomation.FlaUI.Tests/DotnetDebug.AppAutomation.FlaUI.Tests.csproj`
  - `specs/2026-03-06-framework-industrialization-spec.md`
  - `specs/2026-03-07-solution-topology-and-naming-alignment-spec.md`
  - `C:\Projects\My\Agents\instructions\core\quest-governance.md`
  - `C:\Projects\My\Agents\instructions\core\collaboration-baseline.md`
  - `C:\Projects\My\Agents\instructions\core\testing-baseline.md`
  - `C:\Projects\My\Agents\instructions\contexts\testing-dotnet.md`
  - `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`
  - `C:\Projects\My\Agents\instructions\profiles\refactor-architecture.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-linter.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-rubric.md`

## 1. Overview / Цель
Переориентировать репозиторий так, чтобы `AppAutomation` был его главным продуктом, а `DotnetDebug` и demo-тесты выступали reference consumer-ом и внутренним стендом.

Практический результат инициативы:
- у репозитория появляется framework-first entrypoint в документации;
- появляется чёткая consumer story: какие проекты создать у себя, какие пакеты подключить и как начать писать тесты;
- packable-пакеты становятся действительно publish-ready для NuGet;
- появляется repeatable локальная и GitHub-based публикация пакетов;
- репозиторий умеет проверять, что пакетами реально можно воспользоваться из внешнего проекта.

## 2. Текущее состояние (AS-IS)
Фактическое состояние на 7 марта 2026:

1. Репозиторий уже содержит правильное техническое ядро framework:
   - `AppAutomation.Abstractions`
   - `AppAutomation.Authoring`
   - `AppAutomation.Session.Contracts`
   - `AppAutomation.TUnit`
   - `AppAutomation.FlaUI`
   - `AppAutomation.Avalonia.Headless`

2. Рабочая reference topology уже есть, но она видна только из исходников и тестов:
   - `tests/DotnetDebug.AppAutomation.Authoring` хранит page objects и shared scenarios;
   - runtime-specific test projects наследуют общий набор сценариев;
   - repo-only `DotnetDebug.AppAutomation.TestHost` подготавливает launch options для demo AUT.

3. Главная документация репозитория до сих пор описывает проект как учебно-практический репозиторий про отладку и тестирование `.NET`, а не как поставляемый automation framework.

4. Нет отдельной инструкции для внешнего потребителя:
   - какие проекты создать в своей solution;
   - какие NuGet-пакеты ставить в какой проект;
   - как подключать `AppAutomation.Authoring` как source generator;
   - как выглядят минимальные page object, session и runtime-specific tests.

5. NuGet delivery infrastructure неполная:
   - в репозитории отсутствуют pack/publish scripts;
   - нет GitHub workflow для packing/publishing;
   - нет package smoke, который подтверждает install story на локальном feed.

6. Package metadata и packaging discipline недостроены:
   - `RepositoryUrl` в packable `csproj` стоит как заглушка `https://github.com`;
   - не у всех publishable пакетов заполнены `Description`, `PackageTags` и related metadata;
   - `dotnet pack` предупреждает об отсутствии package readme.

7. Централизованная версия сейчас фактически не работает:
   - `Directory.Build.props` задаёт `PackageVersion` только при условии `$(IsPackable) == 'true'`;
   - в момент импорта `Directory.Build.props` свойство `IsPackable` ещё не выставлено в большинстве `csproj`;
   - в результате `dotnet pack` собирает пакеты с версией `1.0.0`, а не `2.1.0` из `eng/Versions.props`.

8. `AppAutomation.Authoring` пока не является корректным NuGet source-generator package:
   - текущий `.nupkg` кладёт `AppAutomation.Authoring.dll` только в `lib/netstandard2.0/`;
   - в пакете нет `analyzers/dotnet/cs/`;
   - package-consumer не получит source generator просто через `PackageReference`.

## 3. Проблема
Фреймворк уже технически существует, но как продукт он ещё не доведён: у него нет ясной consumer entrypoint-документации, install story через NuGet неполная, а packaging/publishing discipline пока не гарантирует, что внешний проект действительно сможет подключить пакеты и получить ожидаемый DX.

## 4. Цели дизайна
- Сделать `AppAutomation` главным объектом позиционирования репозитория.
- Зафиксировать понятную consumer topology для внешних решений.
- Дать пошаговую инструкцию установки через NuGet и минимальный onboarding path.
- Исправить packaging так, чтобы все publishable проекты давали корректные `.nupkg`/`.snupkg`.
- Сделать `AppAutomation.Authoring` настоящим analyzer/source-generator package.
- Добавить repeatable pack/publish scripts и GitHub automation для релизов.
- Добавить package smoke, подтверждающий, что инструкция установки действительно работает.

## 5. Non-Goals
- Не перепроектировать API framework заново.
- Не менять runtime capabilities, control matrix и сценарный DSL сверх того, что нужно для packaging/onboarding.
- Не вводить новый runtime beyond `FlaUI` и `Avalonia.Headless`.
- Не строить отдельный docs-site или маркетинговый сайт.
- Не публиковать новый feed-specific compatibility layer.
- Не переносить demo-приложение из репозитория и не убирать его как reference consumer.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Repo Positioning
`README.md` перестаёт быть обзором “про всё” и становится framework-first entrypoint:

1. В первом экране README объясняется, что `AppAutomation` это reusable framework для desktop UI automation, а `DotnetDebug` служит demo AUT и reference consumer.
2. Сразу после overview README даёт package map:
   - что делает каждый `AppAutomation.*` пакет;
   - какой пакет обязателен, какой опционален;
   - какой runtime для какого сценария нужен.
3. Demo-проекты и внутренние тесты переносятся ниже как reference implementation, а не как главная сущность репозитория.

### 6.2 Consumer Documentation
В репозитории появляется отдельный набор consumer-facing документов.

Минимальный состав:
- `docs/appautomation/quickstart.md`
- `docs/appautomation/project-topology.md`
- `docs/appautomation/publishing.md`

Содержание `quickstart.md`:
1. Предпосылки:
   - `.NET SDK`;
   - Windows requirement для `FlaUI`;
   - headless vs desktop distinction.
2. Минимальная topology consumer solution:
   - `<MyApp>`: AUT/project under test;
   - `<MyApp>.UiTests.Authoring`: page objects + shared scenarios;
   - `<MyApp>.UiTests.Headless`: optional whitebox/headless runtime tests;
   - `<MyApp>.UiTests.FlaUI`: optional Windows desktop runtime tests.
3. PackageReference matrix по проектам:
   - `AppAutomation.Abstractions`
   - `AppAutomation.Authoring`
   - `AppAutomation.TUnit`
   - `AppAutomation.Avalonia.Headless`
   - `AppAutomation.FlaUI`
   - `AppAutomation.Session.Contracts`, если consumer хочет работать напрямую с launch contracts.
4. Пошаговый bootstrap:
   - создать authoring project;
   - добавить `PackageReference` на `AppAutomation.Authoring`;
   - объявить `partial UiPage` c `[UiControl(...)]`;
   - создать runtime-specific test project;
   - поднять session и `IUiControlResolver`;
   - унаследоваться от `UiTestBase<TSession, TPage>`.
5. Минимальный working sample c кодом:
   - page object;
   - shared scenario base;
   - headless runtime test;
   - FlaUI runtime test.

Содержание `project-topology.md`:
- когда нужен только один test project;
- когда нужен shared authoring project;
- как разложить AUT, test host и runtime adapters;
- что остаётся repo-specific responsibility и не поставляется пакетами.

Содержание `publishing.md`:
- как локально собрать и проверить пакеты;
- как выпустить prerelease/stable;
- какие переменные окружения и feed URL нужны для публикации.

### 6.3 Packaging Baseline
Все publishable пакеты приводятся к одному packaging contract.

Норматив:
1. Общая версия пакетов берётся из `eng/Versions.props` и реально применяется к `dotnet pack`.
2. Все publishable проекты получают консистентные metadata:
   - `RepositoryUrl`
   - `RepositoryType`
   - `PackageProjectUrl`
   - `Authors`
   - `Description`
   - `PackageTags`
   - `PackageReadmeFile`
   - symbol/source packaging.
3. В пакетах используется реальный GitHub URL репозитория, а не заглушка.
4. Package readme входит в каждый `.nupkg`.
5. Pack output попадает в предсказуемый каталог, пригодный для локального smoke и публикации.

Техническое правило для versioning:
- централизованную версию нужно задавать так, чтобы она не зависела от позднего определения `IsPackable`;
- `dotnet pack` для любого publishable `csproj` должен выпускать `$(EasyUseVersion)$(EasyUsePrereleaseSuffix)`, а не fallback `1.0.0`.

### 6.4 `AppAutomation.Authoring` as Real Analyzer Package
`AppAutomation.Authoring` должен поставляться как analyzer/source-generator package, пригодный для обычного `PackageReference`.

Норматив:
1. `AppAutomation.Authoring.dll` пакуется в `analyzers/dotnet/cs/`.
2. Runtime assets не публикуются как обычная library dependency, если они не нужны consumer runtime.
3. После `PackageReference Include="AppAutomation.Authoring"` consumer-authoring project получает source generation без `ProjectReference OutputItemType="Analyzer"`.
4. Package smoke явно проверяет, что generated properties и generated manifest/provider появляются именно из NuGet-пакета.

### 6.5 Pack / Publish Automation
В репозитории добавляется стандартный набор release scripts.

Минимальный состав:
- `eng/pack.ps1`
- `eng/publish-nuget.ps1`
- `eng/smoke-consumer.ps1`

Поведение:
1. `eng/pack.ps1`
   - pack only publishable `AppAutomation.*` projects;
   - пишет `.nupkg` и `.snupkg` в `artifacts/packages/<version>/`;
   - умеет включать prerelease suffix из `eng/Versions.props` или параметра.
2. `eng/publish-nuget.ps1`
   - публикует только packable packages;
   - принимает feed URL и API key через параметры/env vars;
   - умеет `--skip-duplicate` style publish.
3. `eng/smoke-consumer.ps1`
   - создаёт временный consumer workspace;
   - поднимает локальный NuGet feed из freshly packed artifacts;
   - создаёт minimal authoring project и хотя бы один runtime-specific test project;
   - выполняет `dotnet restore` + `dotnet build`;
   - подтверждает, что `AppAutomation.Authoring` сработал как source generator из NuGet.

### 6.6 GitHub Automation
В репозитории появляется GitHub workflow для релизного пути.

Минимальный workflow:
1. Trigger:
   - `workflow_dispatch`;
   - tag push для stable/prerelease tags.
2. Этапы:
   - restore;
   - build;
   - test;
   - pack;
   - smoke-consumer;
   - publish to configured feed.
3. Workflow использует secrets для API key и не дублирует бизнес-логику, уже вынесенную в `eng/*.ps1`.

### 6.7 Validation Strategy
Новый delivery path валидируется не только сборкой solution, но и consumer smoke.

Обязательные проверки:
1. `dotnet pack` выдаёт правильную версию из `eng/Versions.props`.
2. `AppAutomation.Authoring` `.nupkg` содержит `analyzers/dotnet/cs/AppAutomation.Authoring.dll`.
3. Local-feed smoke solution успешно restore/build-ится на пакетах из `artifacts/packages`.
4. Quickstart-документация совпадает с реально проверенным smoke path.

## 7. Compatibility, Rollout, Rollback

### 7.1 Совместимость
- Публичные `PackageId` и namespace family остаются прежними.
- Existing repo consumers продолжают работать через `ProjectReference`.
- Новая NuGet install story добавляет корректный путь для внешних consumers, не ломая внутренний repo flow.
- Изменения в package metadata и publish scripts не должны менять поведение runtime adapters.

### 7.2 Rollout
1. Сначала привести в порядок metadata/versioning/packaging.
2. Затем исправить `AppAutomation.Authoring` packaging и добавить smoke.
3. Потом обновить README и consumer docs по уже подтверждённому smoke path.
4. В конце добавить publish scripts и GitHub workflow.

### 7.3 Rollback
- Если analyzer packaging ломает consumer build, откат ограничивается `AppAutomation.Authoring.csproj` и pack scripts.
- Если GitHub workflow даёт сбой, локальные `eng/*.ps1` остаются source of truth и workflow может быть временно отключён без отката docs/packaging.
- Если README/docs расходятся с фактическим smoke path, откат делается правкой docs, а не отменой package infrastructure.

## 8. Acceptance Criteria
1. `README.md` позиционирует репозиторий вокруг `AppAutomation`, а `DotnetDebug` описан как demo/reference consumer.
2. В репозитории есть consumer-facing документация с явным ответом:
   - какие проекты создать;
   - какие пакеты ставить;
   - как начать писать page objects и тесты.
3. Все publishable `AppAutomation.*` пакеты пакуются с версией из `eng/Versions.props`, а не `1.0.0`.
4. Все publishable пакеты содержат корректный `RepositoryUrl` на `https://github.com/Kibnet/DotnetDebug` и package readme.
5. `AppAutomation.Authoring` `.nupkg` содержит analyzer layout, пригодный для обычного `PackageReference`.
6. В репозитории есть рабочие `eng/pack.ps1`, `eng/publish-nuget.ps1`, `eng/smoke-consumer.ps1`.
7. В репозитории есть GitHub workflow, использующий эти скрипты для pack/publish path.
8. Package smoke проходит локально и подтверждает generation/build из локального NuGet feed.
9. Полные проверки зелёные:
   - `dotnet build DotnetDebug.sln`
   - `dotnet test --solution DotnetDebug.sln`
   - `dotnet pack DotnetDebug.sln`
   - `pwsh -File eng/smoke-consumer.ps1`

## 9. Проверки и команды
Точечные проверки:

```powershell
dotnet pack src/AppAutomation.Authoring/AppAutomation.Authoring.csproj -c Release -o artifacts/spec-check
dotnet pack src/AppAutomation.Abstractions/AppAutomation.Abstractions.csproj -c Release -o artifacts/spec-check
pwsh -File eng/pack.ps1
pwsh -File eng/smoke-consumer.ps1
```

Структурные проверки:

```powershell
rg -n "https://github.com" src -g "*.csproj"
rg -n "PackageReadmeFile|RepositoryUrl|PackageProjectUrl|RepositoryType" src -g "*.csproj"
```

Проверка содержимого `nupkg`:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::OpenRead("artifacts/packages/<version>/AppAutomation.Authoring.<version>.nupkg").Entries |
  Select-Object FullName
```

Полный прогон:

```powershell
dotnet restore
dotnet build DotnetDebug.sln -c Release
dotnet test --solution DotnetDebug.sln -c Release
dotnet pack DotnetDebug.sln -c Release
pwsh -File eng/smoke-consumer.ps1
```

## 10. Открытые вопросы
- Основной publish target лучше сделать параметризуемым: `nuget.org` vs `GitHub Packages`. Это не блокирует реализацию, если `eng/publish-nuget.ps1` принимает feed URL как параметр.
- Если для `AppAutomation.Authoring` потребуется дополнительная packaging-настройка для analyzer dependencies, это будет решено в рамках этой же инициативы без изменения public API.
- Отдельный consumer sample repository не входит в этот changeset; верификацию берём на себя через package smoke внутри текущего репозитория.

## 11. Результат прогона линтера
### 11.1 SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Repo positioning, consumer docs, packaging, publish scripts и smoke описаны конкретно |
| C. Безопасность изменений | 11-13 | PASS | Совместимость, rollout и rollback зафиксированы |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria и команды валидации измеримы |
| E. Готовность к автономной реализации | 17-19 | PASS | Объём ограничен, блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | Спека соответствует `dotnet-desktop-client` + `refactor-architecture` |

Итог: `ГОТОВО`

### 11.2 SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | --- | --- |
| 1. Ясность цели и границ | 5 | Инициатива сфокусирована на framework-first positioning и NuGet delivery |
| 2. Понимание текущего состояния | 5 | Зафиксированы конкретные факты по README, pack output и analyzer packaging |
| 3. Конкретность целевого дизайна | 5 | TO-BE задаёт docs, packaging, smoke, scripts и workflow |
| 4. Безопасность (миграция, откат) | 5 | Rollout/rollback локализованы и не требуют API churn |
| 5. Тестируемость | 5 | Есть измеримые acceptance criteria и проверяемый smoke path |
| 6. Готовность к автономной реализации | 5 | Изменения среднего масштаба и технически последовательны |

Итоговый балл: `30 / 30`
Зона: `готово к автономному выполнению`

Слабые места:
- analyzer packaging через NuGet у source generator-пакетов легко сломать неверной `csproj`-конфигурацией, поэтому smoke обязателен;
- из-за mix `net8.0`/`net10.0` consumer docs придётся сформулировать аккуратно, чтобы не обещать одинаковый runtime story для всех пакетов;
- publish workflow должен быть достаточно параметризуемым, чтобы не зашивать один feed и не ломать локальный сценарий.

## 12. Approval
Ожидается фраза: **"Спеку подтверждаю"**
