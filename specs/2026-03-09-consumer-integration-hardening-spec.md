# AppAutomation Consumer Integration Hardening

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `refactor-architecture`
- Владелец: Framework Maintainers
- Масштаб: medium
- Целевая ветка: текущая рабочая ветка
- Ограничения:
  - Не ломать существующий working path для `DotnetDebug.AppAutomation.*` reference tests.
  - Не менять `PackageId` и корневые namespace family `AppAutomation.*`.
  - Не убирать текущие sync launch contracts без миграционного слоя.
  - Перед завершением сохранить зелёными `dotnet build`, `dotnet test`, `dotnet pack` и consumer smoke.
  - Любые новые API должны быть пригодны для внешнего consumer-а, а не только для demo-репозитория.
- Связанные ссылки:
  - `src/AppAutomation.Session.Contracts/DesktopAppLaunchOptions.cs`
  - `src/AppAutomation.Session.Contracts/HeadlessAppLaunchOptions.cs`
  - `src/AppAutomation.Abstractions/UiPageExtensions.cs`
  - `src/AppAutomation.TUnit/UiTestBase.cs`
  - `docs/appautomation/quickstart.md`
  - `docs/appautomation/project-topology.md`
  - `README.md`
  - `specs/2026-03-07-framework-first-nuget-delivery-spec.md`
  - user-provided `Unlimotion` integration report from 2026-03-09
  - `C:\Projects\My\Agents\instructions\core\quest-governance.md`
  - `C:\Projects\My\Agents\instructions\core\collaboration-baseline.md`
  - `C:\Projects\My\Agents\instructions\core\testing-baseline.md`
  - `C:\Projects\My\Agents\instructions\contexts\testing-dotnet.md`
  - `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`
  - `C:\Projects\My\Agents\instructions\profiles\refactor-architecture.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-linter.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-rubric.md`

## 1. Overview / Цель
Довести `AppAutomation` от “работает на reference repo и простом external smoke” до удобного и предсказуемого framework для stateful desktop/Avalonia-приложений, которые имеют:

- repo-specific launch/bootstrap;
- startup arguments и environment overrides;
- in-process headless lifecycle с повторными запусками;
- runtime flows, где one-shot UIA interaction недостаточно стабильна;
- требования к stable selectors beyond visible text.

Практический результат:
- desktop runtime может запускать AUT с аргументами и env vars;
- headless runtime получает first-class bootstrap story для repeated session startup;
- у consumer-а появляются framework-level readiness/retry helpers вместо ad-hoc циклов;
- tab/navigation interactions можно строить на stable locator path, а не только на UI text;
- документация покрывает не только happy path, но и advanced integration cases, уже выявленные на `Unlimotion`.

## 2. Текущее состояние (AS-IS)
Фактическое состояние после первой consumer-driven интеграции:

1. `AppAutomation` уже переносим в сторонний репозиторий:
   - consumer смог поднять `Authoring`, `Headless`, `FlaUI` и repo-specific `TestHost`;
   - desktop/FlaUI path стабилен на нескольких реальных сценариях;
   - headless path стабилен как smoke, но не как полноценная multi-test suite.

2. Current desktop launch contract слишком узкий:
   - `DesktopAppLaunchOptions` содержит только `ExecutablePath`, `WorkingDirectory`, `MainWindowTimeout`, `PollInterval`;
   - нет process arguments;
   - нет environment variables;
   - consumer с более сложным startup вынужден обходить это локальной инфраструктурой.

3. Current headless launch contract too minimal:
   - `HeadlessAppLaunchOptions` требует только sync `Func<object> CreateMainWindow`;
   - отсутствует явная story для async bootstrap;
   - отсутствует guidance для repeated in-process startup и state reset;
   - для stateful Avalonia apps integration вынужденно вскрывает app-specific bootstrap path.

4. `UiPageExtensions` хорошо покрывает базовые действия, но consumer-reported friction показывает пробелы:
   - `SelectTabItem` завязан на visible text;
   - нет first-class path для tab selection через stable tab-item control;
   - нет framework-level generic readiness/retry surface для brittle UIA transitions.

5. `AppAutomation.TUnit` даёт lifecycle base (`UiTestBase`), но не даёт общего набора protected wait/retry helpers для app readiness и transient failures.

6. Documentation описывает clean consumer topology, но не закрывает реальные integration branches:
   - solution below repo root;
   - repo-specific test host when app paths are non-trivial;
   - stateful app with static singletons and repeated headless sessions;
   - troubleshooting repeated headless startup;
   - desktop stabilization patterns for flaky UIA paths.

7. User-provided `Unlimotion` report подтверждает:
   - architecture split `Authoring` / runtime / `TestHost` выбрана правильно;
   - headless repeated startup остаётся главным pain point;
   - stable selector / readiness / lifecycle gaps реальны, а не теоретичны.

## 3. Проблема
`AppAutomation` уже имеет рабочую базовую consumer story, но её API и docs пока покрывают в основном happy path. Для stateful desktop-приложений framework всё ещё недодаёт launch flexibility, headless lifecycle contract, stable selector APIs и стандартные readiness/retry primitives, из-за чего consumers вынуждены писать свою инфраструктурную glue-логику и решать одинаковые проблемы вручную.

## 4. Цели дизайна
- Расширить desktop launch contract под реальные consumer startup needs.
- Дать headless bootstrap story, пригодную для repeated in-process launches.
- Снизить количество consumer-side ad-hoc wait/retry logic.
- Убрать зависимость tab navigation от visible text как единственного stable path.
- Документировать advanced integration patterns, уже выявленные на внешнем consumer-е.
- Сохранить текущий working path и backward compatibility для существующих consumers.

## 5. Non-Goals
- Не переписывать runtime adapters с нуля.
- Не делать глобальный redesign всех `UiPageExtensions`.
- Не добавлять новый runtime beyond `FlaUI` и `Avalonia.Headless`.
- Не переносить consumer-specific reset logic внутрь framework как knowledge про конкретное приложение.
- Не вводить тяжёлый orchestration layer или отдельный process host package.
- Не пытаться решить всю flaky UI automation универсально одним магическим API.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Desktop Launch Contract Hardening
`DesktopAppLaunchOptions` расширяется до practical desktop-consumer contract.

Новые возможности:
1. Optional command-line arguments.
2. Optional environment variable overrides.
3. Existing `WorkingDirectory`, `MainWindowTimeout`, `PollInterval` сохраняются.

Норматив:
- consumer должен мочь запускать AUT с тем же базовым contract, не падая в custom `ProcessStartInfo` boilerplate;
- API остаётся декларативным и простым для repo-specific test host;
- `DesktopAppSession.Launch` использует новые поля без breaking removal current ones.

Минимальный target shape:
- `IReadOnlyList<string>` или equivalent immutable collection для arguments;
- `IReadOnlyDictionary<string, string?>` или equivalent immutable collection для environment variables.

### 6.2 Headless Bootstrap and Repeated Launch Story
`HeadlessAppLaunchOptions` эволюционирует от “одно sync окно” к более реалистичному bootstrap contract.

Целевой дизайн:
1. Сохранить текущий sync path как простой happy path.
2. Добавить async-capable bootstrap path для apps, которым нужен setup перед созданием окна.
3. Явно выделить pre-launch hook для consumer-owned reset/bootstrap logic.

Норматив:
- framework не должен знать про конкретные static singletons consumer app;
- framework должен дать стандартное место, где consumer может выполнить reset/init перед созданием окна;
- repeated headless launch в одном test session/process должен быть testable и documented;
- sync-only consumer не должен быть вынужден мигрировать на async path.

Предлагаемая форма:
- оставить `CreateMainWindow`;
- добавить `CreateMainWindowAsync` и/или `BeforeLaunchAsync` как optional contract;
- `DesktopAppSession.Launch` для headless runtime использует async hook через safe sync bridge внутри runtime boundary.

### 6.3 Readiness and Retry Helpers
В `AppAutomation.TUnit` и/или abstraction-level helpers появляется first-class story для app readiness и transient interaction stabilization.

Цель:
- consumer не должен заново изобретать polling/retry loop для “app ready”, “tree settled”, “details pane active”.

Норматив:
1. Дать reusable protected helpers в `UiTestBase` или sibling helper type:
   - generic `WaitUntil`;
   - async/sync variants;
   - retry with timeout and stable diagnostics.
2. Helpers должны использовать существующую `UiWait`/`UiOperationException` model, а не создавать параллельную систему.
3. Helpers должны подходить как для general app readiness, так и для stabilization around brittle UIA transitions.

Важно:
- это не замена runtime-specific good selectors;
- это уменьшение consumer glue code там, где readiness/transience неизбежны.

### 6.4 Stable Tab Selection by Control, Not Only by Text
Tab navigation получает stable control-based path.

Проблема today:
- `SelectTabItem(page => page.MainTabs, "Tasks")` зависит от visible text;
- это хрупко для localization и content copy changes.

Целевой путь:
1. Поддержать navigation через `ITabItemControl`, который уже может быть объявлен в authoring layer как отдельный control с `AutomationId`.
2. Добавить соответствующие helper APIs в `UiPageExtensions`, например:
   - selection by tab-item selector;
   - wait for selected state by tab-item selector.

Норматив:
- text-based container API сохраняется для простых кейсов;
- stable control-based API становится рекомендуемым path для production-grade consumers.

### 6.5 Advanced Consumer Documentation
Docs расширяются beyond happy path.

Минимальные additions:
1. Раздел/документ про repo layouts, где solution lives below repo root.
2. Раздел про repo-specific `TestHost` responsibilities.
3. Раздел про stateful apps и repeated headless launches:
   - где делать reset;
   - что остаётся consumer responsibility;
   - какие hooks framework предоставляет.
4. Раздел про readiness/retry patterns:
   - когда это framework-approved;
   - где проходит граница между retry и плохим selector design.
5. Troubleshooting для headless repeated startup.

### 6.6 Reference Validation via Externalized Scenario
Инициатива должна быть проверена не только на `DotnetDebug`, но и на consumer-like scenario, повторяющем pain points из `Unlimotion`.

Минимальный validation contract в этом repo:
1. Unit tests на новые launch options.
2. Headless runtime tests на repeated startup path.
3. Regression tests на new control-based tab helpers.
4. Docs/examples синхронизированы с новыми API.

## 7. Compatibility, Rollout, Rollback

### 7.1 Совместимость
- Existing sync desktop/headless launch path остаётся рабочим.
- Existing text-based `SelectTabItem` API остаётся рабочим.
- Existing `UiTestBase` consumers не ломаются, если новые wait/retry helpers добавляются как additive surface.
- `DotnetDebug` reference tests должны продолжать проходить без forced migration.

### 7.2 Rollout
1. Сначала расширить launch contracts additive way.
2. Затем добавить runtime support и regression tests.
3. Потом ввести new helper APIs в `UiPageExtensions` / `UiTestBase`.
4. После этого обновить docs и examples на recommended stable path.

### 7.3 Rollback
- Если async headless bootstrap осложняет runtime stability, rollback ограничивается optional async/pre-launch hooks с сохранением sync path.
- Если new readiness helpers оказываются noisy или дублирующими, rollback ограничивается `AppAutomation.TUnit` surface без затрагивания launch contracts.
- Если docs overfit external consumer case, rollback делается на уровне docs/examples без отката API hardening.

## 8. Acceptance Criteria
1. `DesktopAppLaunchOptions` поддерживает startup arguments и environment overrides.
2. `AppAutomation.FlaUI` runtime реально использует эти новые launch fields.
3. `HeadlessAppLaunchOptions` предоставляет стандартный path для advanced bootstrap beyond single sync factory.
4. В repo есть regression test, подтверждающий multiple headless launches в одном процессе/тестовом прогоне на reference app.
5. `UiPageExtensions` предоставляет stable tab selection path через tab-item control selector.
6. Существующий text-based `SelectTabItem` остаётся совместимым.
7. `AppAutomation.TUnit` или sibling helper surface даёт reusable wait/retry helpers для consumer readiness logic.
8. Docs покрывают:
   - nested solution layout;
   - repo-specific test host;
   - stateful headless apps;
   - repeated headless startup troubleshooting.
9. Полные проверки зелёные:
   - `dotnet build DotnetDebug.sln`
   - `dotnet test --solution DotnetDebug.sln`
   - `dotnet pack DotnetDebug.sln`
   - `pwsh -File eng/smoke-consumer.ps1`

## 9. Проверки и команды
Точечные проверки:

```powershell
dotnet test tests/DotnetDebug.Tests/DotnetDebug.Tests.csproj --filter "FullyQualifiedName~LaunchOptions"
dotnet test tests/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/DotnetDebug.AppAutomation.Avalonia.Headless.Tests.csproj --filter "FullyQualifiedName~Headless"
dotnet test tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj --filter "FullyQualifiedName~Tab"
```

Структурные проверки:

```powershell
rg -n "CreateMainWindow|CreateMainWindowAsync|BeforeLaunch" src docs -g "*.cs" -g "*.md"
rg -n "SelectTabItem|ITabItemControl" src tests -g "*.cs"
rg -n "solution root|nested solution|stateful|headless" docs -g "*.md"
```

Полный прогон:

```powershell
dotnet build DotnetDebug.sln -c Release
dotnet test --solution DotnetDebug.sln -c Release
dotnet pack DotnetDebug.sln -c Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release
```

## 10. Открытые вопросы
- Стоит ли делать headless advanced bootstrap через два optional delegates (`BeforeLaunchAsync` + `CreateMainWindowAsync`) или через один unified async factory. Это не блокирует реализацию, если итоговый contract остаётся additive.
- Нужно ли readiness/retry surface жить именно в `UiTestBase` или лучше в отдельном helper type внутри `AppAutomation.TUnit`. Решение можно принять в EXEC после быстрой code-level оценки call ergonomics.
- Если repeated headless startup зависит не только от hooks, но и от reference app static-state topology, нужно ли в docs явно разделить “framework contract” и “consumer reset responsibility”. Предварительно: да.

## 11. Результат прогона линтера
### 11.1 SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Launch contracts, lifecycle, helpers и docs expansion описаны конкретно |
| C. Безопасность изменений | 11-13 | PASS | Совместимость, rollout и rollback заданы additive way |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria и команды проверки измеримы |
| E. Готовность к автономной реализации | 17-19 | PASS | Объём ограничен, открытые вопросы неблокирующие |
| F. Соответствие профилю | 20 | PASS | Спека соответствует `dotnet-desktop-client` + `refactor-architecture` |

Итог: `ГОТОВО`

### 11.2 SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | --- | --- |
| 1. Ясность цели и границ | 5 | Инициатива сфокусирована на consumer integration hardening, без расползания в общий redesign |
| 2. Понимание текущего состояния | 5 | AS-IS опирается на current API и внешний integration report |
| 3. Конкретность целевого дизайна | 5 | Зафиксированы launch, headless, selector, helper и docs направления |
| 4. Безопасность (миграция, откат) | 5 | Все ключевые изменения additive и локализованы |
| 5. Тестируемость | 5 | Есть измеримые acceptance criteria и команды валидации |
| 6. Готовность к автономной реализации | 5 | Изменения умеренного объёма и хорошо декомпозируются |

Итоговый балл: `30 / 30`
Зона: `готово к автономному выполнению`

Слабые места:
- repeated headless startup может раскрыть ограничения не только framework, но и reference app static-state design;
- выбор между unified async factory и hook-based bootstrap нужно будет аккуратно добить на уровне code ergonomics;
- readiness/retry helpers легко сделать слишком общими, поэтому важно удержать scope на реальных integration pain points.

## 12. Approval
Ожидается фраза: **"Спеку подтверждаю"**
