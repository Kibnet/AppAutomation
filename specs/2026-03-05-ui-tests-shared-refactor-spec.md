# Рефакторинг UI-тестов: выделение общей части и runtime-специфики

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: QA Automation / Engineering
- Масштаб: medium
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения:
  - Не менять пользовательское поведение тестов.
  - Не менять DSL-стиль сценариев (`FlaUI.EasyUse.*`).
  - Не ломать совместимость headless/FlaUI запусков.
- Связанные ссылки:
  - `C:\Projects\My\Agents\instructions\core\quest-governance.md`
  - `C:\Projects\My\Agents\instructions\core\quest-mode.md`
  - `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`
  - `C:\Projects\My\Agents\instructions\profiles\ui-automation-testing.md`
  - `C:\Projects\My\Agents\instructions\contexts\testing-dotnet.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-linter.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-rubric.md`

## 1. Overview / Цель
Выделить общую часть UI-тестов в нейтральный shared-слой, чтобы:
- сценарии и page object поддерживались в одном месте;
- в каждом тестовом проекте оставалась только runtime-специфика (инициализация/запуск);
- исключить связь вида «один тестовый проект ссылается на файлы другого».

## 2. Текущее состояние (AS-IS)
- Общие сценарии и page object физически лежат в headless-проекте:
  - `tests/DotnetDebug.UiTests.Avalonia.Headless/Tests/MainWindowFlaUI.EasyUseTests.cs`
  - `tests/DotnetDebug.UiTests.Avalonia.Headless/Pages/MainWindowPage.cs`
- FlaUI-проект компилирует эти файлы линками:
  - `tests/DotnetDebug.UiTests.FlaUI.EasyUse/DotnetDebug.UiTests.FlaUI.EasyUse.csproj` (`Compile Include` на headless-пути).
- Headless имеет runtime hooks:
  - `tests/DotnetDebug.UiTests.Avalonia.Headless/Infrastructure/HeadlessSessionHooks.cs`
- В `src` есть частичное дублирование базовых TUnit-типов и session-контрактов между FlaUI и headless пакетами.

Ограничения и проблемы:
- Проект `FlaUI` зависит от структуры файлов `Headless` (хрупкая coupling по путям).
- Общая часть не выделена как доменный shared-layer.
- Runtime-логика и сценарная логика смешаны в одном тестовом классе.
- Поддержка усложняется при добавлении третьего runtime.

## 3. Проблема
Нет явной архитектурной границы «общие UI-сценарии vs runtime-адаптер», из-за чего растёт связанность тестовых проектов и стоимость сопровождения.

## 4. Цели дизайна
- Разделение ответственности: shared-сценарии отдельно, runtime-обвязка отдельно.
- Повторное использование: единый источник для page object и сценариев.
- Тестируемость: одинаковый набор сценариев запускается в headless и FlaUI.
- Консистентность: одинаковый DSL и namespace-контракт.
- Обратная совместимость: существующие тестовые имена и ожидания сохраняются.

## 5. Non-Goals (чего НЕ делаем)
- Не переписываем DSL или test API.
- Не меняем бизнес-логику приложения.
- Не делаем массовый рефактор всех UI-тестов за пределами текущего набора.
- Не меняем архитектуру debug/MCP процесса.
- Не выполняем фазу 2 (вынос `TUnit.Core` и `Session.Contracts`) в рамках этой спеки; это отдельная инициатива.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `tests/DotnetDebug.UiTests.Shared/*`:
  - Общий `MainWindowPage`.
  - Общая база сценариев (все `[Test]` кейсы).
- `tests/DotnetDebug.UiTests.Avalonia.Headless/*`:
  - Только headless runtime-файлы (`HeadlessSessionHooks`, concrete test fixture runtime part).
- `tests/DotnetDebug.UiTests.FlaUI.EasyUse/*`:
  - Только FlaUI runtime-файлы (concrete fixture runtime part).

### 6.2 Детальный дизайн
1. Shared test sources
- Создать каталог `tests/DotnetDebug.UiTests.Shared`.
- Перенести туда:
  - `Pages/MainWindowPage.cs`
  - `Tests/MainWindowScenariosBase.cs` (абстрактный базовый класс с текущими тестами).
- В `MainWindowScenariosBase` оставить только:
  - test-сценарии;
  - работу с `Page`, `UiAssert`, fluent DSL;
  - отсутствие runtime-инициализации.
- Namespace policy для shared-файлов:
  - сохранить текущие namespace `DotnetDebug.UiTests.FlaUI.EasyUse.*` для обратной совместимости и минимизации изменений;
  - не переименовывать типы в фазе 1.

2. Runtime-specific fixtures
- Headless:
  - `Tests/MainWindowHeadlessRuntimeTests.cs`:
    - `sealed class ... : MainWindowScenariosBase`
    - overrides `CreateLaunchOptions`, `CreatePage`.
  - `Infrastructure/HeadlessSessionHooks.cs` оставить без изменений.
- FlaUI:
  - `Tests/MainWindowFlaUiRuntimeTests.cs`:
    - `sealed class ... : MainWindowScenariosBase`
    - runtime-специфичный launch.

3. Подключение shared в оба проекта
- Удалить cross-link между двумя тестовыми проектами.
- Оба `.csproj` подключают `..\DotnetDebug.UiTests.Shared\**\*.cs` как `Compile Include`.
- В фазе 1 использовать явные `ItemGroup` в каждом `.csproj` (без отдельного `.props`), чтобы снизить риск скрытой магии на старте рефакторинга.

4. Follow-up (вне этой спеки)
- После стабилизации фазы 1 оформить отдельную спецификацию на вынос дублирования в `src`:
  - `src/EasyUse.TUnit.Core`;
  - `src/EasyUse.Session.Contracts`;
  - стратегия миграции: прямой перевод потребителей на общую библиотеку (без `TypeForwardedTo` и без legacy thin-обёрток).

Обработка ошибок:
- Сборка должна падать при отсутствии shared-файлов или дублирующихся классов.
- Отдельно валидировать, что нет двойного включения одного и того же test class.

Производительность:
- Нейтрально; влияет только на структуру исходников и сопровождение.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Инвариант 1: один набор UI-сценариев = один source of truth.
- Инвариант 2: runtime-specific код не содержит бизнес-assertion сценария.
- Инвариант 3: селекторы (`AutomationId`) остаются стабильными.

## 8. Точки интеграции и триггеры
- Триггер создания сессии:
  - Headless: `HeadlessSessionHooks` (`Before/After TestSession`).
  - FlaUI: существующий launch в `DesktopUiTestBase`.
- Триггер выполнения сценариев:
  - тестовый раннер TUnit через классы runtime-fixture.

## 9. Изменения модели данных / состояния
- Persisted данные: нет.
- Runtime state: только тестовая инфраструктура (session lifecycle) без изменения доменных моделей.

## 10. Миграция / Rollout / Rollback
- Rollout:
  1. Создать shared-каталог.
  2. Перенести общие файлы.
  3. Добавить runtime-specific fixture файлы по проектам.
  4. Обновить `.csproj` на shared include.
  5. Удалить старые cross-link и дубли.
- Rollback:
  1. Удалить shared `Compile Include` из обоих тестовых `.csproj`.
  2. Вернуть прежнюю структуру исходников (runtime+scenarios в текущих проектах, где они были до рефакторинга).
  3. Восстановить предыдущие link/include правила между тестовыми проектами, если откат частичный.
  4. Прогнать `dotnet build` и targeted `dotnet test` для подтверждения восстановления.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Оба проекта компилируют один и тот же shared-набор сценариев.
  - В тестовых проектах остаются только runtime-specific файлы.
  - Headless test suite проходит полностью.
  - FlaUI test suite (минимум smoke-набор shared-сценариев) обязательно проходит при ручном локальном запуске на Windows-машине (основной канал валидации).
  - Нет cross-link между `tests/...Headless` и `tests/...FlaUI` напрямую.
  - Набор имён shared-сценариев, обнаруженных тест-раннером, совпадает в обоих runtime (без потерь и дублей).
- Какие тесты добавить/изменить:
  - Не добавлять новые пользовательские сценарии.
  - Добавить обязательную проверку discovery-паритета shared-сценариев для headless/FlaUI.
  - Для FlaUI зафиксировать визуальную проверку: приложение открывается, сценарий выполняется, ожидаемые UI-изменения наблюдаемы.
  - Для ручного FlaUI прогона фиксировать run-report в отдельном файле `specs/reports/flaui-manual-run-report.md` (дата/время, Windows-машина, команда запуска, итог passed/failed, примечания по визуальному наблюдению).
- Команды для проверки:
  - `dotnet build DotnetDebug.sln`
  - `dotnet test tests/DotnetDebug.UiTests.Avalonia.Headless/DotnetDebug.UiTests.Avalonia.Headless.csproj`
  - `dotnet test tests/DotnetDebug.UiTests.FlaUI.EasyUse/DotnetDebug.UiTests.FlaUI.EasyUse.csproj` (ручной локальный запуск на Windows)
  - `pwsh -Command "New-Item -ItemType Directory -Path artifacts -Force | Out-Null"`
  - `dotnet test tests/DotnetDebug.UiTests.Avalonia.Headless/DotnetDebug.UiTests.Avalonia.Headless.csproj --list-tests > artifacts/headless-tests.txt`
  - `dotnet test tests/DotnetDebug.UiTests.FlaUI.EasyUse/DotnetDebug.UiTests.FlaUI.EasyUse.csproj --list-tests > artifacts/flaui-tests.txt` (ручной локальный запуск на Windows)
  - `pwsh -Command "$normalize={ param($path) Get-Content $path | Where-Object { $_ -match 'MainWindow' } | ForEach-Object { (($_.Trim() -split '\.')[-1] -replace '\(.*$','') } | Where-Object { $_ -ne '' } | Sort-Object -Unique }; $h=& $normalize 'artifacts/headless-tests.txt'; $f=& $normalize 'artifacts/flaui-tests.txt'; $diff=Compare-Object $h $f; if($diff){ $diff | Out-String | Write-Error; throw 'Discovery mismatch by scenario method names' }"`

## 12. Риски и edge cases
- Риск: двойное включение shared-файлов и локальных копий.
  - Митигация: удалить локальные дубли, проверить `git ls-files` и compilation errors.
- Риск: различия runtime могут потребовать разной инициализации.
  - Митигация: держать runtime-файлы отдельными.
- Риск: фаза 2 (базовые библиотеки) может внести breaking changes при прямой миграции потребителей.
  - Митигация: выполнить миграцию отдельным PR с явным migration guide и поэтапным обновлением зависимых проектов.

## 13. План выполнения
1. Создать `tests/DotnetDebug.UiTests.Shared` и перенести общие `Page` и сценарии.
2. Вынести runtime-specific часть тестового класса в отдельные файлы по проектам.
3. Обновить оба `.csproj` на shared include; убрать cross-link проект->проект.
4. Проверить отсутствие дублирующихся классов.
5. Прогнать build/test команды из раздела 11.
6. Подготовить отдельную спеку/PR на фазу 2 (`TUnit.Core` + `Session.Contracts`).

## 14. Открытые вопросы
- Блокирующих открытых вопросов нет.
- Решение владельца продукта зафиксировано: для фазы 2 использовать прямую миграцию на общую библиотеку.

## 15. Соответствие профилю
- Профиль:
  - `dotnet-desktop-client`
  - `ui-automation-testing`
- Выполненные требования профиля:
  - Стабильность `automation-id` сохранена (селекторы не меняются).
  - UI/integration тесты сохраняются и синхронизируются между runtime.
  - Верификация через `dotnet build` + `dotnet test` включена в AC.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `tests/DotnetDebug.UiTests.Shared/Pages/MainWindowPage.cs` | new | Единый page object |
| `tests/DotnetDebug.UiTests.Shared/Tests/MainWindowScenariosBase.cs` | new | Единый набор сценариев |
| `tests/DotnetDebug.UiTests.Avalonia.Headless/Tests/*Runtime*.cs` | new/update | Headless runtime-инициализация |
| `tests/DotnetDebug.UiTests.Avalonia.Headless/Infrastructure/HeadlessSessionHooks.cs` | keep | Session lifecycle headless |
| `tests/DotnetDebug.UiTests.Avalonia.Headless/*.csproj` | update | Подключение shared |
| `tests/DotnetDebug.UiTests.FlaUI.EasyUse/Tests/*Runtime*.cs` | new/update | FlaUI runtime-инициализация |
| `tests/DotnetDebug.UiTests.FlaUI.EasyUse/*.csproj` | update | Подключение shared, удаление cross-link |
| `specs/reports/flaui-manual-run-report.md` | new/update | Отдельный артефакт ручного FlaUI smoke-прогона |
| `src/EasyUse.TUnit.Core/*` | follow-up spec, new | Удаление дублирования базовых TUnit-типов |
| `src/EasyUse.Session.Contracts/*` | follow-up spec, new | Единые launch options контракты |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Источник тестовых сценариев | Файлы лежат в headless-проекте | Отдельный shared-каталог |
| Связь проектов | FlaUI ссылается на headless файлы | Оба проекта ссылаются на shared |
| Runtime и сценарии | Смешаны в одном классе | Разделены на base scenarios + runtime fixture |
| Масштабирование на новые runtime | Сложно | Добавляется новый runtime-fixture без копирования сценариев |
| TUnit/session контракты в `src` | Частичное дублирование | План фазы 2 на единые базовые библиотеки |

## 18. Альтернативы и компромиссы
- Вариант: оставить как есть (FlaUI линкует headless).
  - Плюсы: минимум изменений.
  - Минусы: архитектурная связность и хрупкие пути.
  - Почему не выбран: не решает корневую проблему.
- Вариант: shared class library вместо shared sources.
  - Плюсы: строгая сборочная граница.
  - Минусы: усложнение с test attributes/discovery и зависимостями раннера.
  - Почему не выбран: на текущем этапе избыточно; shared sources проще и прозрачнее.
- Вариант: единый мульти-таргет тестовый проект.
  - Плюсы: один `.csproj`.
  - Минусы: сложно развести runtime hooks и package graph.
  - Почему не выбран: выше риск нестабильности CI.

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
| D3 Покрытие runtime smoke/full | PASS | Раздел 11 (обязательный ручной Windows-run FlaUI, discovery parity по именам сценариев, run-report) |
| E1 Риски перечислены | PASS | Раздел 12 |
| E2 Митигации даны | PASS | Раздел 12 |
| E3 Edge cases учтены | PASS | Раздел 12 |
| F1 Пошаговый план | PASS | Раздел 13 |
| F2 Открытые вопросы | PASS | Раздел 14, блокирующих вопросов нет, решение по фазе 2 зафиксировано |
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
- Для фазы 2 потребуется аккуратное планирование breaking changes при прямой миграции потребителей.

## Approval
Ожидается фраза: **"Спеку подтверждаю"**
