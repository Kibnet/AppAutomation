# Исправление падающих тестов решения

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: medium
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения:
  - До фазы `EXEC` менять только эту спецификацию.
  - Не менять публичный API без отдельного согласования.
  - Любой bugfix сопровождать regression-тестом.
  - Перед завершением обязателен полный прогон `dotnet build` и `dotnet test`.
- Связанные ссылки:
  - `C:\Projects\My\Agents\instructions\core\quest-governance.md`
  - `C:\Projects\My\Agents\instructions\core\quest-mode.md`
  - `C:\Projects\My\Agents\instructions\core\collaboration-baseline.md`
  - `C:\Projects\My\Agents\instructions\core\testing-baseline.md`
  - `C:\Projects\My\Agents\instructions\contexts\testing-dotnet.md`
  - `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`
  - `C:\Projects\My\Agents\instructions\profiles\ui-automation-testing.md`

## 1. Overview / Цель
Нужно прогнать весь тестовый набор `AppAutomation.sln`, локализовать реальные падения, исправить код или тесты в пределах текущего контракта и завершить работу зелёным полным прогоном.

## 2. Текущее состояние (AS-IS)
- Решение содержит набор библиотек и тестовых проектов вокруг `AppAutomation`, включая desktop/Avalonia и UI automation сценарии.
- Код и тесты распределены между `src/`, `tests/` и `sample/`.
- Диагностический прогон `dotnet test --solution AppAutomation.sln` под pinned SDK `10.0.103` дал итог: `140` тестов, `126` passed, `14` failed, `0` skipped.
- Падают только два тестовых assembly:
  - `sample/DotnetDebug.Tests`: `DotnetDebugDesktopLaunchOptions_UseIsolatedBuildOutput_OnlyWhenBuilding`.
  - `sample/DotnetDebug.AppAutomation.FlaUI.Tests`: `13` тестов, включая `CollectAsync_ReturnsFlaUiSpecificArtifacts`, `SelectListBoxItem_ByCapability_SelectsDesktopItem`, `Hierarchy_SelectTreeItem_ShowsSelectionInResult`, `Calculate_Min_RespectsAbsoluteCheckbox`, `Calculate_Lcm_UsesNegativeAndAbsoluteOption`, `Calculate_Gcd_WithDefaultSettings_ShowsResultStepsAndHistory`, `DateTime_InvalidRange_ShowsValidation`.
- Все зафиксированные падения сходятся к одной ошибке: вложенный desktop build внутри `AvaloniaDesktopLaunchHost.RunBuild(...)` падает с `ExitCode=-2147450725`, потому что вызывается `dotnet build` через `FileName = "dotnet"` и не наследует корректный путь к pinned SDK.
- В репозитории дополнительно обнаружен инфраструктурный дефект локального toolchain: `.dotnet\\sdk\\10.0.103` указывает на несуществующий `C:\\Program Files\\dotnet\\sdk\\10.0.104`. Для диагностики использован временно установленный вне репозитория SDK `10.0.103`.
- После исправления nested build CLI и восстановления локального `.dotnet` plain repo-local `dotnet` прогон проявил вторую независимую проблему: два FlaUI теста (`SelectListBoxItem_ByCapability_SelectsDesktopItem`, `Hierarchy_SelectTreeItem_ShowsSelectionInResult`) стабильно падали на подтверждении выбора узла `DemoTree`.
- После завершения `EXEC` plain `dotnet build AppAutomation.sln` проходит без ошибок, а plain `dotnet test --solution AppAutomation.sln` даёт итог: `142` теста, `142` passed, `0` failed, `0` skipped.
- Переход в `EXEC` подтверждён пользователем и завершён.

## 3. Проблема
В решении есть один или несколько падающих тестов, из-за чего текущая ревизия не проходит полный локальный quality gate.

## 4. Цели дизайна
- Разделение ответственности: исправления должны затрагивать только код, реально связанный с падениями.
- Повторное использование: использовать существующую тестовую инфраструктуру без параллельных обходных механизмов.
- Тестируемость: для каждого багфикса оставить воспроизводящий или регрессионный тест.
- Консистентность: сохранить текущие контракты тестов, селекторов и launch workflow, если иное не требуется для корректности.
- Обратная совместимость (если применимо): не ломать публичные API и существующие automation-id/селекторы без явной необходимости.

## 5. Non-Goals (чего НЕ делаем)
- Не выполняем несвязанный рефакторинг.
- Не меняем публичные API ради косметики.
- Не перерабатываем тестовую архитектуру шире, чем требуется для устранения конкретных падений.
- Не трогаем документацию, кроме случаев, когда она непосредственно мешает тестам, и только после входа в `EXEC`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `specs/2026-04-20-fix-test-failures.md` -> рамки задачи, диагностика, quality gate, журнал действий.
- `src/AppAutomation.TestHost.Avalonia/AvaloniaDesktopLaunchHost.cs` -> точечное исправление резолва `dotnet` для вложенного билда desktop sample.
- `src/AppAutomation.Abstractions/UiPageExtensions.cs` -> более устойчивое подтверждение tree selection по identity выбранного узла.
- `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs` -> runtime-specific fallback выбора tree node через descendant/header interaction для Avalonia/FlaUI.
- `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs` и `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` -> regression-покрытие для обоих bugfix'ов.
- `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Properties/AssemblyInfo.cs` -> assembly-level сериализация desktop UI прогона в проблемном FlaUI проекте.

### 6.2 Детальный дизайн
- Выполнить полный диагностический прогон `dotnet test AppAutomation.sln` с выводом артефактов вне репозитория.
- Зафиксировать в спецификации:
  - какие тестовые проекты упали;
  - симптомы и место вероятной причины;
  - какие regression-тесты уже существуют и где нужен дополнительный reproducer.
- На фазе `EXEC` исправить резолв `dotnet` так, чтобы `AvaloniaDesktopLaunchHost.RunBuild(...)` использовал тот же CLI/toolchain, который доступен текущему тестовому процессу, вместо безусловного `"dotnet"` из PATH.
- На фазе `EXEC` сначала оценить, достаточно ли текущих падающих тестов как reproducer. Если нет, добавить минимальный regression-тест на резолв build CLI, затем внести минимальный фикс.
- После устранения первой причины повторить plain repo-local `dotnet` прогон, чтобы поймать вторичные дефекты, не видимые на временном SDK run.
- Для FlaUI tree selection усилить две точки:
  - `UiPageExtensions.SelectTreeItem(...)` должен подтверждать выбранный узел не только по `Text`, но и по identity уже найденного target (`AutomationId` / `Name`).
  - `FlaUiTreeItemControl.SelectNode()` должен уметь кликнуть не только сам `TreeItem`, но и релевантный descendant/header element, если контейнер selection не принимает.
- Для стабилизации desktop UI прогона в полном suite сериализовать `sample/DotnetDebug.AppAutomation.FlaUI.Tests` через assembly-level `NotInParallel`.
- После таргетной проверки прогнать полный `dotnet build` и `dotnet test`.
- Для UI/desktop сценариев сохранить стабильность automation-контрактов и не маскировать падения отключением тестов.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Падающий существующий тест считается допустимым reproducer, если он однозначно фиксирует дефект и после исправления становится зелёным без ослабления проверки.
- Если падение вызвано некорректным тестом, исправление теста допустимо только при явном подтверждении, что продуктовый контракт не нарушался.
- Если причина одна и влияет на несколько тестов, фикс должен устранять первопричину, а не добавлять частные обходы.

## 8. Точки интеграции и триггеры
- `dotnet test AppAutomation.sln` -> основной источник фактического списка падений.
- Таргетные `dotnet test <project>` / `--filter` -> ускорение цикла отладки после локализации.
- `dotnet build AppAutomation.sln` -> финальная проверка сборки.
- `AvaloniaDesktopLaunchHost.RunBuild(...)` -> точка, где вложенный desktop build теряет корректный CLI.
- `DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(...)` и `AppAutomation.TUnit.UiTestBase` -> upstream вызовы, через которые дефект проявляется в UI/FlaUI тестах.

## 9. Изменения модели данных / состояния
- Новые persisted-данные не планируются.
- Возможны изменения runtime-состояния только внутри кода, затронутого багфиксом.

## 10. Миграция / Rollout / Rollback
- Миграция данных не требуется.
- Rollout: обычное включение исправлений в текущую ветку.
- Rollback: откатить точечные изменения файлов, внесённых в рамках задачи.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Полный `dotnet test AppAutomation.sln` проходит без падений.
  - Все изменённые bugfix-сценарии покрыты существующими или новыми regression-тестами.
  - Полный `dotnet build AppAutomation.sln` проходит.
  - Ни один UI automation тест не отключён и не ослаблен без доказанной необходимости.
- Какие тесты добавить/изменить:
  - Минимум использовать как regression existing failures:
    - `LaunchOptionsDefaultsTests.DotnetDebugDesktopLaunchOptions_UseIsolatedBuildOutput_OnlyWhenBuilding`
    - `FlaUiArtifactCollectorTests.CollectAsync_ReturnsFlaUiSpecificArtifacts`
    - один из `FlaUiControlResolverTests` / `MainWindowFlaUiRuntimeTests` как smoke для запуска desktop UI сценария.
  - Добавить точечный test на резолв CLI для `AvaloniaDesktopLaunchHost`:
    - `LaunchContractTests.AvaloniaDesktopLaunchHost_BuildUsesDotnetHostPath_WhenPathDoesNotContainDotnet`
  - Добавить unit regression-тест на tree-selection identity fallback:
    - `UiPageExtensionsTests.SelectTreeItem_UsesSelectedItemIdentity_WhenSelectedTextIsUnavailable`
- Characterization tests / contract checks для текущего поведения (если применимо):
  - Уже падающие тесты выступили valid reproducer для двух независимых причин: невозможность выполнить nested `dotnet build` при вызове launch host и нестабильный tree selection в Avalonia/FlaUI runtime.
- Базовые замеры до/после для performance tradeoff (если применимо):
  - Не ожидаются.
- Команды для проверки:
  - `<temp-dotnet> test --solution AppAutomation.sln`
  - `dotnet test --project tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj`
  - `dotnet test --project sample/DotnetDebug.AppAutomation.FlaUI.Tests/DotnetDebug.AppAutomation.FlaUI.Tests.csproj`
  - `dotnet build AppAutomation.sln`
  - `dotnet test --solution AppAutomation.sln`

## 12. Риски и edge cases
- Падения могут зависеть от UI/desktop окружения и быть чувствительны к отсутствию platform prerequisites.
- Один симптом может скрывать несколько независимых причин в разных тестовых проектах.
- Полный тестовый прогон может быть долгим; нужен переход на targeted-итерации после первичной диагностики.
- Исправление теста вместо кода несёт риск скрыть реальный регресс.
- Если fix будет завязан только на внешний PATH, он останется хрупким для pinned/local SDK сценариев.
- Если резолв `dotnet` будет слишком агрессивно привязан к конкретной инсталляции, можно сломать обычный запуск на машинах, где системный `dotnet` валиден.

## 13. План выполнения
1. Создать и заполнить рабочую спецификацию.
2. Выполнить диагностический полный прогон тестов с артефактами вне репозитория.
3. Уточнить в спецификации фактические падения, зону причины и набор целевых файлов.
4. Выполнить post-SPEC review и quality gate.
5. Запросить у пользователя фразу `Спеку подтверждаю`.
6. На фазе `EXEC` исправить резолв `dotnet` в desktop launch host, добавить/уточнить reproducer при необходимости и внести минимальный фикс.
7. Прогнать targeted tests.
8. Прогнать полный `dotnet build` и полный `dotnet test`.
9. Выполнить post-EXEC review и подготовить итоговый отчёт.

## 14. Открытые вопросы
- Нет блокирующих вопросов. Обе фактические причины локализованы и устранены без необходимости продуктового решения пользователя.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`
- Выполненные требования профиля:
  - Выполнены полный plain `dotnet build` и полный plain `dotnet test` на repo-local toolchain.
  - Для UI automation контрактов селекторы и тесты не ослаблялись и не отключались.
  - Выполнены targeted и full проверки, включая отдельный FlaUI project run и unit regression run для abstractions.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-04-20-fix-test-failures.md` | Спецификация, диагностика, результаты EXEC и review | Требование `QUEST`-процесса |
| `src/AppAutomation.TestHost.Avalonia/AvaloniaDesktopLaunchHost.cs` | Добавлен резолв подходящего `dotnet` host по env/parent process/pinned SDK; вложенный build теперь наследует `DOTNET_HOST_PATH` и `DOTNET_ROOT` | Исправить nested `dotnet build` в тестовом desktop launch workflow |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` | `SelectTreeItem(...)` теперь подтверждает выбор узла не только по `Text`, но и по identity найденного target (`AutomationId` / `Name`), с более информативным `lastObservedValue` | Устранить ложные timeout'ы на runtimes, где выбранный tree item плохо репортит `Text` |
| `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs` | `FlaUiTreeItemControl.SelectNode()` получил descendant/header fallback и interaction-backed selected state; fallback подбирается по тексту узла и типу clickable descendant | Сделать выбор узла дерева устойчивым для Avalonia/FlaUI, где клик по самому `TreeItem` не всегда меняет selection |
| `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs` | Добавлен unit regression-тест на identity-based tree selection verification | Зафиксировать bugfix в слое абстракций |
| `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs` | Добавлен regression-тест на build через `DOTNET_HOST_PATH` при пустом `PATH` и вспомогательные test helpers | Закрепить багфикс точечным unit/integration reproducer |
| `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Properties/AssemblyInfo.cs` | Добавлен assembly-level `[NotInParallel]` | Убрать дополнительную межтестовую конкуренцию desktop UI сценариев в полном suite |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Тестовый статус решения | Диагностика начиналась с `140` total / `14` failed / `126` passed, а после первого фикса plain repo-local run продолжал ловить `2` FlaUI failures | Финальный plain прогон: `142` total / `0` failed / `142` passed |
| Nested build CLI | Всегда `FileName = "dotnet"` без проверки host/toolchain | Выбирается подходящий `dotnet` host с учётом `DOTNET_HOST_PATH`, SDK resolver env, parent process и pinned SDK |
| Tree selection verification | `SelectTreeItem(...)` полагался в основном на `IsSelected` / `SelectedTreeItem.Text` и падал на Avalonia/FlaUI | Проверка учитывает identity выбранного узла, а FlaUI runtime умеет выбирать header-descendant, если сам `TreeItem` не принимает selection |
| Regression coverage | Только падающие sample-тесты | Падающие sample-тесты + точечные regression-тесты в `AppAutomation.TestHost.Avalonia.Tests` и `AppAutomation.Abstractions.Tests` |
| Локальный toolchain | Repo-local `.dotnet` был сломан, plain `dotnet` в корне решения не давал полноценную валидацию | `.dotnet` переустановлен через `eng/install-dotnet.ps1`, финальные `build`/`test` подтверждены обычным `dotnet` |

## 18. Альтернативы и компромиссы
- Вариант: чинить только самый первый упавший тест, не прогоняя full suite повторно.
- Плюсы: быстрее локальный цикл.
- Минусы: высокий риск оставить скрытые падения в других проектах.
- Почему выбранное решение лучше в контексте этой задачи:
  - Пользователь запросил прогон всех тестов и исправление падений, поэтому full-suite диагностика и финальная full-suite валидация обязательны.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals определены. |
| B. Качество дизайна | 6-10 | PASS | После диагностики зафиксирована одна доминирующая точка исправления и набор reproducer-тестов. |
| C. Безопасность изменений | 11-13 | PASS | Границы, rollback и риск подмены продуктового фикса тестовым ослаблением зафиксированы. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria и команды проверки определены. |
| E. Готовность к автономной реализации | 17-19 | PASS | План есть, блокирующих вопросов нет, target files определены. |
| F. Соответствие профилю | 20 | PASS | Учтены требования .NET desktop и UI automation профилей. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Цель и границы bugfix-работы заданы явно. |
| 2. Понимание текущего состояния | 5 | Полный список падающих assembly, симптомы и вероятная первопричина зафиксированы. |
| 3. Конкретность целевого дизайна | 5 | Описан конкретный путь исправления nested build CLI resolution и проверки результата. |
| 4. Безопасность (миграция, откат) | 5 | Определены ограничения, rollback и запрет на лишние изменения. |
| 5. Тестируемость | 5 | Есть критерии приёмки, targeted/full проверки и regression-требование. |
| 6. Готовность к автономной реализации | 5 | Для старта EXEC после диагностики и подтверждения пользователя информации достаточно. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - Явно добавлен запрет на ослабление UI automation тестов.
  - Зафиксированы фактические падающие тесты, реальная точка дефекта и инфраструктурный нюанс со сломанным локальным SDK junction.
- Что осталось на решение пользователя:
  - Подтверждение перехода в `EXEC` фразой `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения:
  - `AvaloniaDesktopLaunchHost.RunBuild(...)` больше не использует безусловный `"dotnet"` и выбирает host, который реально содержит pinned SDK.
  - Вложенный build теперь прокидывает `DOTNET_HOST_PATH` и `DOTNET_ROOT` в дочерний процесс.
  - После восстановления repo-local `.dotnet` выявлен и устранён второй дефект: FlaUI tree selection в Avalonia runtime не всегда проходил через прямой `TreeItem` selection.
  - `UiPageExtensions.SelectTreeItem(...)` и `FlaUiTreeItemControl.SelectNode()` усилены так, чтобы общая API-проверка оставалась переносимой, а runtime-specific interaction был устойчивым для Avalonia/FlaUI.
  - Добавлены regression-тесты на сценарий с недоступным `PATH`, но валидным `DOTNET_HOST_PATH`, и на identity-based tree selection verification.
  - FlaUI desktop assembly сериализован через `[assembly: NotInParallel]`.
- Что проверено дополнительно для refactor / comments:
  - Лишний refactor не выполнялся.
  - Публичный API не менялся.
  - Комментарии и docstring не устарели.
- Остаточные риски / follow-ups:
  - В solution остались не связанные с задачей предупреждения анализаторов и `NU1903` по `Tmds.DBus.Protocol 0.21.2`.
  - Interaction-backed `IsSelected` в FlaUI по-прежнему best-effort и опирается на то, что последующие waits/asserts валидируют реальный UI side effect.

## Approval
Подтверждено пользователем фразой: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Маршрутизация и выбор instruction stack | 0.91 | Нет route-specific данных о падениях | Создать рабочую спецификацию | Нет | Нет | Без спеки нельзя переходить к реализации по `QUEST` | `specs/2026-04-20-fix-test-failures.md` |
| SPEC | Подготовка рамок bugfix-задачи | 0.88 | Фактический список падающих тестов | Выполнить диагностический full test run и обновить spec | Нет | Нет | Нужно сначала локализовать реальные падения, затем фиксировать область изменений | `specs/2026-04-20-fix-test-failures.md` |
| SPEC | Диагностика toolchain и full test run | 0.95 | Нужно понять, единая ли это первопричина | Обновить spec фактическими падениями и точкой дефекта | Нет | Нет | Системный и локальный repo `dotnet` не дали стабильного pinned SDK, поэтому диагностика выполнена временным SDK вне репозитория | `specs/2026-04-20-fix-test-failures.md` |
| SPEC | Локализация bugfix scope | 0.94 | Нужен только переход в `EXEC` | Запросить подтверждение спеки у пользователя | Да | Да, ожидается фраза `Спеку подтверждаю` | Падающие тесты сводятся к nested `dotnet build` в `AvaloniaDesktopLaunchHost.RunBuild(...)` | `specs/2026-04-20-fix-test-failures.md` |
| EXEC | Фикс nested build CLI resolution | 0.93 | Нужно подтвердить, какие источники host реально доступны в test process | Исправить `AvaloniaDesktopLaunchHost` и прогнать targeted tests | Нет | Да, пользователь подтвердил переход в EXEC | На практике `DOTNET_HOST_PATH` оказался не всегда доступен, поэтому добавлен более надёжный multi-source host resolution с проверкой pinned SDK | `src/AppAutomation.TestHost.Avalonia/AvaloniaDesktopLaunchHost.cs`, `specs/2026-04-20-fix-test-failures.md` |
| EXEC | Regression coverage | 0.96 | Нет | Добавить точечный тест и повторить targeted validation | Нет | Нет | Существующие sample-тесты уже ловили дефект, но отдельный regression-тест на host resolution уменьшает риск повторной поломки | `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs`, `specs/2026-04-20-fix-test-failures.md` |
| EXEC | Восстановление repo-local toolchain и повторная plain validation | 0.95 | Нужно проверить, остались ли скрытые дефекты после первого фикса | Переустановить `.dotnet`, повторить plain `dotnet` прогон и локализовать новые падения | Нет | Нет | Пользователь просил прогнать все тесты, поэтому итог должен быть подтверждён обычным repo-local `dotnet`, а не только временным SDK вне репозитория | `global.json`, локальная `.dotnet/` (не коммитится), `specs/2026-04-20-fix-test-failures.md` |
| EXEC | Фикс FlaUI tree selection и дополнительное regression coverage | 0.92 | Нужно понять, selection ломается в verification или в runtime interaction | Усилить tree selection в abstractions/FlaUI, добавить unit regression, сериализовать FlaUI assembly и повторить targeted run | Нет | Нет | Plain `dotnet` показал второй независимый дефект: direct `TreeItem` selection в Avalonia/FlaUI нестабилен, но downstream UI side effect после корректного header interaction воспроизводим и проверяем | `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Properties/AssemblyInfo.cs`, `specs/2026-04-20-fix-test-failures.md` |
| EXEC | Финальная валидация и review | 0.99 | Нет | Зафиксировать результаты build/test и завершить задачу | Нет | Нет | Plain `dotnet build AppAutomation.sln` и plain `dotnet test --solution AppAutomation.sln` зелёные; итоговый статус `142/142` | `specs/2026-04-20-fix-test-failures.md` |
