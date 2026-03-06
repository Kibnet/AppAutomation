# Фаза 2: вынос `EasyUse.TUnit.Core` и `EasyUse.Session.Contracts`

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `refactor-architecture`
- Владелец: QA Automation / Platform Engineering
- Масштаб: large
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения:
  - Выполнить **прямую миграцию** потребителей на общие библиотеки.
  - Не использовать `TypeForwardedTo` и legacy thin-обёртки для старых namespace.
  - Не менять пользовательское поведение UI-сценариев и `automation-id`/селекторы.
  - Сохранить работоспособность обоих runtime: `Avalonia.Headless` и `FlaUI`.
- Связанные ссылки:
  - `specs/2026-03-05-ui-tests-shared-refactor-spec.md`
  - `specs/reports/flaui-manual-run-report.md`
  - `C:\Projects\My\Agents\instructions\core\quest-governance.md`
  - `C:\Projects\My\Agents\instructions\core\quest-mode.md`
  - `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`
  - `C:\Projects\My\Agents\instructions\profiles\refactor-architecture.md`
  - `C:\Projects\My\Agents\instructions\contexts\testing-dotnet.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-linter.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-rubric.md`

## 1. Overview / Цель
Выделить дублируемые базовые типы в два нейтральных проекта:
- `src/EasyUse.Session.Contracts` для launch/session-контрактов;
- `src/EasyUse.TUnit.Core` для общих TUnit-баз и assertion/waiting-утилит.

Результат: единый source of truth для базовых контрактов, а runtime-пакеты содержат только runtime-специфику.

## 2. Текущее состояние (AS-IS)
- В репозитории есть дублирование базовых типов между runtime-ветками:
  - TUnit-слой:
    - `src/FlaUI.EasyUse.TUnit/DesktopUiTestBase.cs`
    - `src/FlaUI.EasyUse.TUnit/UiAssert.cs`
    - `src/Avalonia.Headless.EasyUse.TUnit/DesktopUiTestBase.cs`
    - `src/Avalonia.Headless.EasyUse.TUnit/UiAssert.cs`
  - Session-контракты:
    - `src/FlaUI.EasyUse/Session/DesktopProjectLaunchOptions.cs`
    - `src/FlaUI.EasyUse/Session/DesktopAppLaunchOptions.cs`
    - `src/Avalonia.Headless.EasyUse/Session/DesktopSession.cs` (содержит те же контрактные типы)
- В `Avalonia.Headless` уже есть переходный слой `*.New.cs`, который дублирует API в новых namespace и повышает сложность сопровождения.
- Общие UI-сценарии из фазы 1 уже вынесены в `tests/DotnetDebug.UiTests.Shared`, но всё ещё зависят от старых namespace (`FlaUI.EasyUse.TUnit`, `FlaUI.EasyUse.Session`).
- Архитектурно нет отдельного слоя контрактов и core-утилит; runtime-пакеты смешивают контракт/инфраструктуру/адаптацию.

Ограничения и проблемы:
- Высокая стоимость изменений (правки делаются в нескольких местах).
- Риск дрейфа API/дефолтов между runtime-реализациями.
- Неочевидная граница между контрактом и реализацией.

## 3. Проблема
Отсутствует единый базовый слой контрактов и TUnit-core, из-за чего дублируются типы и растёт риск регрессий при развитии двух runtime.

## 4. Цели дизайна
- Разделение ответственности: контракт/ядро отдельно от runtime-реализаций.
- Повторное использование: один источник для launch-контрактов и TUnit-core.
- Тестируемость: shared UI-сценарии запускаются без расхождений на headless/FlaUI.
- Консистентность: одинаковые дефолты и единые сигнатуры.
- Обратная совместимость в рамках репозитория: все текущие тесты продолжают проходить.

## 5. Non-Goals (чего НЕ делаем)
- Не добавляем новые пользовательские UI-сценарии.
- Не меняем бизнес-логику приложения `DotnetDebug.Avalonia`.
- Не переписываем генераторы контролов (`*.Generators`) сверх нужного для компиляции.
- Не оставляем compatibility-слой через `TypeForwardedTo` или legacy thin-обёртки.
- Не меняем протокол debug/MCP и runbook-флоу.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/EasyUse.Session.Contracts/*`
  - `DesktopProjectLaunchOptions`
  - `DesktopAppLaunchOptions`
  - Единые дефолты и контрактные инварианты launch-настроек.
- `src/EasyUse.TUnit.Core/*`
  - `UiAssert`
  - `UiWait`, `UiWaitOptions`, `UiWaitResult`
  - runtime-agnostic базовый класс для UI-тестового lifecycle.
- `src/FlaUI.EasyUse/*`
  - Только FlaUI runtime-реализация запуска/сессии.
  - Использование контрактов из `EasyUse.Session.Contracts`.
- `src/Avalonia.Headless.EasyUse/*`
  - Только headless runtime-реализация/адаптеры.
  - Использование тех же контрактов из `EasyUse.Session.Contracts`.
- `tests/*`
  - Прямое использование `EasyUse.TUnit.Core` и `EasyUse.Session.Contracts`.

### 6.2 Детальный дизайн
1. Новый проект `EasyUse.Session.Contracts`
- Добавить проект `src/EasyUse.Session.Contracts/EasyUse.Session.Contracts.csproj`.
- Вынести туда контрактные типы launch-опций (из `FlaUI.EasyUse.Session` / `DesktopSession.cs`).
- Namespace: `EasyUse.Session.Contracts`.
- Сохранить поведение/дефолты полей без изменений.

2. Новый проект `EasyUse.TUnit.Core`
- Добавить проект `src/EasyUse.TUnit.Core/EasyUse.TUnit.Core.csproj`.
- Вынести туда:
  - `UiAssert`;
  - waiting-типы (`UiWait`, `UiWaitOptions`, `UiWaitResult`);
  - runtime-agnostic test-base через явный контракт сессии/launch:
    - `IUiTestSession : IDisposable`;
    - `UiTestBase<TSession, TPage> where TSession : class, IUiTestSession`;
    - в `UiTestBase` обязательные абстракции:
      - `CreateLaunchOptions()`;
      - `LaunchSession(DesktopProjectLaunchOptions options)`;
      - `CreatePage(TSession session)`.
- Namespace: `EasyUse.TUnit.Core`.

3. Перевод runtime-проектов на контракты
- `src/FlaUI.EasyUse`:
  - `DesktopAppSession` принимает типы из `EasyUse.Session.Contracts`.
  - Локальные дубли контрактных типов удаляются.
- `src/Avalonia.Headless.EasyUse`:
  - `DesktopSession` принимает те же типы из `EasyUse.Session.Contracts`.
  - Локальные дубли контрактных типов удаляются.

4. Прямая миграция потребителей
- Обновить `using` и project references в `tests/*` и `src/*` на новые нейтральные библиотеки.
- Удалить/переписать legacy-файлы, которые дублируют `UiAssert`/`DesktopUiTestBase` и launch-контракты.
- Переходный слой `*.New.cs` схлопнуть до одного канонического набора файлов (без параллельной модели legacy/new).

### 6.3 API Migration Map (обязательный)
| Старый API | Новый API | Статус | Примечание |
| --- | --- | --- | --- |
| `FlaUI.EasyUse.Session.DesktopProjectLaunchOptions` | `EasyUse.Session.Contracts.DesktopProjectLaunchOptions` | replace | Полная замена using и сигнатур |
| `FlaUI.EasyUse.Session.DesktopAppLaunchOptions` | `EasyUse.Session.Contracts.DesktopAppLaunchOptions` | replace | Полная замена using и сигнатур |
| `FlaUI.EasyUse.Waiting.UiWait` | `EasyUse.TUnit.Core.Waiting.UiWait` | replace | Семантика polling/timeout неизменна |
| `FlaUI.EasyUse.Waiting.UiWaitOptions` | `EasyUse.TUnit.Core.Waiting.UiWaitOptions` | replace | Дефолты должны совпасть |
| `FlaUI.EasyUse.Waiting.UiWaitResult<T>` | `EasyUse.TUnit.Core.Waiting.UiWaitResult<T>` | replace | Без изменений контрактной формы |
| `FlaUI.EasyUse.TUnit.UiAssert` | `EasyUse.TUnit.Core.UiAssert` | replace | Полная замена using |
| `FlaUI.EasyUse.TUnit.DesktopUiTestBase<TPage>` | `EasyUse.TUnit.Core.UiTestBase<TSession, TPage>` | redesign+replace | Переход на runtime-agnostic базу |
| `Avalonia.Headless.EasyUse.TUnit.HeadlessUiTestBase<TPage>` | `EasyUse.TUnit.Core.UiTestBase<TSession, TPage>` + runtime fixture | redesign+replace | Runtime-часть остаётся в headless-проекте |

Обязательный deliverable миграции:
- создать файл `specs/reports/2026-03-05-phase2-api-migration-map.md` с фактической таблицей замен по всем затронутым проектам.

### 6.4 Политика breaking-изменений
- Миграция считается breaking для внешних потребителей пакетов.
- Обязательные артефакты в этом же PR:
  - `specs/reports/2026-03-05-phase2-migration-guide.md`;
  - `specs/reports/2026-03-05-phase2-release-notes.md`;
  - фиксация major-version bump для затронутых publishable пакетов.

5. Схема зависимостей (MUST для `refactor-architecture`)
- До:
  - `tests` -> `FlaUI.EasyUse.TUnit` + `FlaUI.EasyUse.Session` (через runtime-пакеты)
  - `Avalonia.Headless.EasyUse*` содержит дубли namespace `FlaUI.EasyUse.*`
  - Контракты и core-утилиты дублируются в нескольких проектах
- После:
  - `tests` -> `EasyUse.TUnit.Core` + runtime session API
  - `FlaUI.EasyUse` и `Avalonia.Headless.EasyUse` -> `EasyUse.Session.Contracts`
  - Один экземпляр core/assert/wait API + один экземпляр launch-контрактов

Обработка ошибок:
- Ошибки сборки при наличии дубликатов публичных типов считаются обязательным стоп-сигналом.
- Любые расхождения discovery shared-сценариев между runtime блокируют merge.

Производительность:
- Runtime-поведение не меняется; изменения касаются архитектуры/зависимостей.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Инвариант 1: значения по умолчанию launch-опций идентичны до/после миграции.
- Инвариант 2: polling/timeout логика `UiAssert` и waiting совпадает по семантике.
- Инвариант 3: один канонический тип на один контракт (без дублирования по runtime-проектам).
- Инвариант 4: после миграции в кодовой базе нет классов-обёрток, которые только делегируют вызовы старого API в новый без runtime-логики.

## 8. Точки интеграции и триггеры
- `DesktopAppSession.LaunchFromProject(...)` в обеих runtime-реализациях.
- Базовый lifecycle UI-тестов (`[Before(Test)]/[After(Test)]`) через core test-base.
- `HeadlessSessionHooks` остаётся точкой инициализации headless runtime.

## 9. Изменения модели данных / состояния
- Persisted-данные: нет.
- Runtime state: без изменения доменной модели; только реорганизация библиотечных контрактов и зависимостей.

## 10. Миграция / Rollout / Rollback
- Rollout:
  1. Зафиксировать checkpoint перед миграцией (tag/commit marker: `phase2-pre-migration`).
  2. Добавить `EasyUse.Session.Contracts` и `EasyUse.TUnit.Core`.
  3. Перевести runtime-проекты на новые библиотеки.
  4. Перевести тестовые проекты и shared-тесты на новые namespace/ссылки.
  5. Удалить legacy-дубли и переходные параллельные файлы.
  6. Прогнать build + targeted/full test verification.
  7. Обновить migration guide/release notes + version bump.
- Rollback:
  1. Вернуться к checkpoint `phase2-pre-migration`.
  2. Откатить project references на старые проекты.
  3. Вернуть удалённые дублирующиеся файлы.
  4. Удалить новые проекты из `DotnetDebug.sln`.
  5. Подтвердить восстановление `dotnet build` + targeted UI test runs.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `DesktopProjectLaunchOptions` и `DesktopAppLaunchOptions` определены только в `EasyUse.Session.Contracts`.
  - `UiAssert` и core test-base определены только в `EasyUse.TUnit.Core`.
  - В `src/FlaUI.EasyUse*` и `src/Avalonia.Headless.EasyUse*` отсутствуют дубли удалённых core/contract типов.
  - Runtime-agnostic test-base реализован через формальный контракт (`IUiTestSession` + `UiTestBase<TSession, TPage>`), и оба runtime подключены к нему без копирования lifecycle-кода.
  - Headless suite проходит полностью.
  - FlaUI suite проходит полностью на Windows.
  - Discovery shared-сценариев полностью совпадает между headless/FlaUI.
  - Нет `TypeForwardedTo` и legacy thin-обёрток.
  - Сформированы `API migration map`, `migration guide`, `release notes` и major-version bump для publishable пакетов.
- Какие тесты добавить/изменить:
  - Обновить существующие UI-тесты на новые namespace/контракты.
  - Сохранить `tests/Verify-UiScenarioDiscoveryParity.ps1` как обязательный regression-check.
  - Добавить обязательные unit-тесты на сохранение семантики:
    - дефолты `DesktopProjectLaunchOptions`/`DesktopAppLaunchOptions`;
    - поведение `UiWait` (`Timeout`, `PollInterval`, отмена через `CancellationToken`);
    - базовый lifecycle `UiTestBase` (setup/cleanup и dispose сессии).
- Команды для проверки:
  - `dotnet build DotnetDebug.sln`
  - `dotnet test tests/DotnetDebug.Tests/DotnetDebug.Tests.csproj`
  - `dotnet test tests/DotnetDebug.UiTests.Avalonia.Headless/DotnetDebug.UiTests.Avalonia.Headless.csproj`
  - `dotnet test tests/DotnetDebug.UiTests.FlaUI.EasyUse/DotnetDebug.UiTests.FlaUI.EasyUse.csproj`
  - `pwsh -File tests/Verify-UiScenarioDiscoveryParity.ps1`
  - `rg -n "record class DesktopProjectLaunchOptions|class DesktopAppLaunchOptions" src`
  - `rg -n "class UiAssert|class DesktopUiTestBase" src`
  - `rg -n "TypeForwardedTo" src`
  - `rg -n "namespace\\s+FlaUI\\.EasyUse\\.(TUnit|Session)" src/Avalonia.Headless.EasyUse src/Avalonia.Headless.EasyUse.TUnit src/EasyUse.*`

## 12. Риски и edge cases
- Риск: breaking changes по namespace/type identity при прямой миграции.
  - Митигация: мигрировать всех внутренних потребителей в одном PR, не оставляя смешанного состояния.
  - Митигация 2: выпускать major-version bump + migration guide + release notes в том же PR.
- Риск: скрытая зависимость на старые namespace в генераторах или тестовых утилитах.
  - Митигация: обязательный `rg`-поиск старых `using`/namespace + компиляционный gate.
- Риск: циклические зависимости между новыми и старыми проектами.
  - Митигация: зафиксировать направленность зависимостей (runtime -> contracts, tests -> core/contracts/runtime).
- Риск: flakiness FlaUI при полном прогоне solution.
  - Митигация: обязательно прогонять целевые UI-проекты и фиксировать отдельный run-report для FlaUI.
- Риск: неполная карта `old -> new` API.
  - Митигация: обязательная проверка migration map в code review, блокирующая merge при пропусках.

## 13. План выполнения
1. Добавить проекты `EasyUse.Session.Contracts` и `EasyUse.TUnit.Core` в `src` и `DotnetDebug.sln`.
2. Перенести launch-контракты и core TUnit-утилиты в новые проекты.
3. Обновить runtime-проекты (`FlaUI`/`Headless`) на использование новых контрактов.
4. Перевести тестовые проекты и shared-сценарии на новые namespace/ссылки.
5. Удалить legacy-дубли и переходные параллельные файлы, больше не нужные после миграции.
6. Выполнить build/test/discovery проверки из раздела 11.
7. Обновить документацию (`README`, при необходимости runbook) по новым зависимостям.
8. Сформировать и приложить артефакты breaking-миграции (migration map/guide/release notes).

## 14. Открытые вопросы
- Блокирующих открытых вопросов нет.
- Принято проектное решение: прямой переход на общие библиотеки без слоя совместимости.

## 15. Соответствие профилю
- Профиль:
  - `dotnet-desktop-client`
  - `refactor-architecture`
- Выполненные требования профиля:
  - Зафиксирована схема зависимостей до/после.
  - Определены публичные API и точки миграции.
  - Описан rollout/rollback.
  - Подтверждение через `dotnet build` + `dotnet test` включено.
  - Стабильность UI-сценариев/селекторов сохранена как инвариант.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/EasyUse.Session.Contracts/EasyUse.Session.Contracts.csproj` | new | Общий проект контрактов launch/session |
| `src/EasyUse.Session.Contracts/*.cs` | new | Единые `DesktopProjectLaunchOptions` и `DesktopAppLaunchOptions` |
| `src/EasyUse.TUnit.Core/EasyUse.TUnit.Core.csproj` | new | Общий TUnit core проект |
| `src/EasyUse.TUnit.Core/*.cs` | new | `UiAssert`, waiting, базовый test lifecycle |
| `specs/reports/2026-03-05-phase2-api-migration-map.md` | new | Фактическая карта old->new API |
| `specs/reports/2026-03-05-phase2-migration-guide.md` | new | Инструкция миграции для потребителей |
| `specs/reports/2026-03-05-phase2-release-notes.md` | new | Фиксация breaking changes и version bump |
| `src/FlaUI.EasyUse/Session/DesktopAppSession.cs` | update | Переход на `EasyUse.Session.Contracts` |
| `src/FlaUI.EasyUse/Session/DesktopProjectLaunchOptions.cs` | delete | Удаление дубля контракта |
| `src/FlaUI.EasyUse/Session/DesktopAppLaunchOptions.cs` | delete | Удаление дубля контракта |
| `src/Avalonia.Headless.EasyUse/Session/DesktopSession.cs` | update | Переход на общий контрактный слой |
| `src/FlaUI.EasyUse.TUnit/*` | update/delete | Удаление дублей core-типов после миграции |
| `src/Avalonia.Headless.EasyUse.TUnit/*` | update/delete | Удаление дублей core-типов после миграции |
| `tests/DotnetDebug.UiTests.Shared/*` | update | Использование новых core/contracts namespace |
| `tests/DotnetDebug.UiTests.Avalonia.Headless/*` | update | Использование новых core/contracts namespace |
| `tests/DotnetDebug.UiTests.FlaUI.EasyUse/*` | update | Использование новых core/contracts namespace |
| `DotnetDebug.sln` | update | Подключение новых проектов и удаление legacy-проектов/ссылок (если опустеют) |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Launch контракты | Дубли в runtime-проектах | Единый проект `EasyUse.Session.Contracts` |
| TUnit core | Дубли `UiAssert`/base в двух проектах | Единый проект `EasyUse.TUnit.Core` |
| Namespace-стратегия | Смешение legacy/new и `*.New.cs` | Один канонический набор API |
| Runtime проекты | Содержат и контракты, и runtime | Содержат только runtime-специфику |
| Поддержка изменений | Риск дрейфа между runtime | Одна точка изменения для core/contract |
| Миграция потребителей | Неформализованная замена using/type | Формализованный API migration map + migration guide |

## 18. Альтернативы и компромиссы
- Вариант: оставить текущие дубли.
  - Плюсы: нулевая миграция.
  - Минусы: продолжение дрейфа и высокая стоимость изменений.
  - Почему не выбран: не решает корневую проблему.
- Вариант: миграция через `TypeForwardedTo`/обёртки.
  - Плюсы: мягкий переход.
  - Минусы: долгий хвост legacy-кода и двойная поддержка API.
  - Почему не выбран: противоречит зафиксированному решению по фазе 2.
- Вариант: объединить всё в один monolith-проект.
  - Плюсы: минимум ссылок.
  - Минусы: потеря границы “контракт vs runtime”, хуже масштабируемость.
  - Почему не выбран: ухудшает архитектурную декомпозицию.

## 19. Результат прогона линтера
### 19.1 SPEC Linter checklist (A–F)
| Пункт | Статус | Комментарий |
| --- | --- | --- |
| A1 Цель сформулирована | PASS | Раздел 1 |
| A2 AS-IS описан | PASS | Раздел 2 |
| A3 Проблема одна, корневая | PASS | Раздел 3 |
| A4 Non-Goals заданы | PASS | Раздел 5 |
| B1 Распределение ответственности | PASS | Раздел 6.1 |
| B2 Детальный дизайн | PASS | Раздел 6.2 |
| B3 Инварианты/правила | PASS | Раздел 7 |
| C1 Интеграционные точки | PASS | Раздел 8 |
| C2 Изменения состояния/данных | PASS | Раздел 9 |
| C3 Миграция и rollout | PASS | Раздел 10 |
| C4 Rollback | PASS | Раздел 10 |
| D1 Acceptance criteria измеримы | PASS | Раздел 11 |
| D2 Список проверок/команд | PASS | Раздел 11 |
| D3 Покрытие runtime smoke/full | PASS | Раздел 11 |
| E1 Риски перечислены | PASS | Раздел 12 |
| E2 Митигации даны | PASS | Раздел 12 |
| E3 Edge cases учтены | PASS | Раздел 12 |
| F1 Пошаговый план | PASS | Раздел 13 |
| F2 Открытые вопросы | PASS | Раздел 14 |
| F3 Таблицы трассируемости | PASS | Разделы 16-17 |

Итоговый статус SPEC Linter: `ГОТОВО` (в A/C/D нет FAIL).

### 19.2 SPEC Rubric (0/2/5)
- Ясность цели и границ: 5
- Понимание текущего состояния: 5
- Конкретность целевого дизайна: 5
- Безопасность (миграция, откат): 5
- Тестируемость: 5
- Готовность к автономной реализации: 5

Итог: **30/30**.

Слабые места:
- Прямая миграция остаётся high-impact для внешних потребителей; риск снижен обязательными migration-артефактами и major-version bump.

## Approval
Ожидается фраза: **"Спеку подтверждаю"**
