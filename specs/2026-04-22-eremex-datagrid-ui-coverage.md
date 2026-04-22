# Eremex DataGrid в тестовом Avalonia-приложении

## 0. Метаданные
- Тип (профиль): dotnet-desktop-client + ui-automation-testing
- Владелец: AppAutomation
- Масштаб: medium
- Целевой релиз / ветка: текущая рабочая ветка `master`
- Ограничения:
  - До подтверждения спеки разрешены только изменения этой спеки.
  - Не менять публичный API AppAutomation без необходимости.
  - Не ломать существующие сценарии обычного Avalonia `DataGrid`.
  - Версии NuGet хранить централизованно в `Directory.Packages.props`.
- Связанные ссылки:
  - `ControlSupportMatrix.md`
  - `sample/DotnetDebug.Avalonia/MainWindow.axaml`
  - `sample/DotnetDebug.AppAutomation.Authoring/Tests/MainWindowScenariosBase.cs`
  - NuGet `Eremex.Avalonia.Controls` 1.3.62: https://www.nuget.org/packages/Eremex.Avalonia.Controls
  - NuGet `Eremex.Avalonia.Themes.DeltaDesign` 1.3.62: https://www.nuget.org/packages/Eremex.Avalonia.Themes.DeltaDesign
  - Eremex DataGrid docs: https://eremexcontrols.net/controls/datagrid/
  - TUnit running tests: https://tunit.dev/docs/getting-started/running-your-tests
  - TUnit test filters: https://tunit.dev/docs/execution/test-filters/

## 1. Overview / Цель
Добавить в тестовое Avalonia-приложение отдельный Eremex DataGrid и покрыть его UI-тестами, чтобы AppAutomation имел реальный smoke/characterization сценарий для стороннего grid-контрола, а не только для стандартного `Avalonia.Controls.DataGrid`.

## 2. Текущее состояние (AS-IS)
- В `sample/DotnetDebug.Avalonia/MainWindow.axaml` уже есть вкладка `DataGridTabItem` со стандартным Avalonia `DataGrid` и `AutomationId="DemoDataGrid"`.
- В `sample/DotnetDebug.Avalonia/MainWindowViewModel.cs` есть `ObservableCollection<DataGridRowViewModel> DataGridRows`, выбор строки и label-состояния для обычного grid-сценария.
- В `sample/DotnetDebug.AppAutomation.Authoring/Pages/MainWindowPage.cs` обычный grid объявлен как `[UiControl("DemoDataGrid", UiControlType.Grid, "DemoDataGrid")]`.
- В `sample/DotnetDebug.AppAutomation.Authoring/Tests/MainWindowScenariosBase.cs` есть общий сценарий `DataGrid_BuildSelectClear_ShowsRowsSelectionAndValidation()`, который проверяет business-flow через controls/labels, но не проверяет сторонний Eremex grid.
- В `ControlSupportMatrix.md` статус устарел: там всё ещё указано, что DataGrid отсутствует в UI и нет покрытия строк/ячеек.
- В `Directory.Packages.props` Avalonia пакеты закреплены на `11.3.7`.
- По NuGet на 2026-04-22:
  - `Eremex.Avalonia.Controls` latest `1.3.62`.
  - `Eremex.Avalonia.Themes.DeltaDesign` latest `1.3.62`.
  - Эти пакеты требуют `Avalonia >= 11.3.8`, поэтому текущие `11.3.7` нужно поднять минимум до `11.3.8`.

## 3. Проблема
В тестовом приложении нет Eremex DataGrid и нет UI-теста, который показывает, как AppAutomation/FlaUI видит сторонний grid-контрол в реальном desktop UIA дереве.

## 4. Цели дизайна
- Разделение ответственности: стандартный Avalonia `DataGrid` остается базовым typed-grid сценарием, Eremex DataGrid добавляется как отдельный сторонний showcase.
- Повторное использование: Eremex grid использует существующие `DataGridRows`, кнопки построения/очистки и детерминированные значения строк.
- Тестируемость: добавить стабильные `AutomationId` для root/контейнера Eremex grid и UI-тесты, не завязанные на визуальный стиль.
- Консистентность: сохранить существующий стиль `UiControl` declarations, TUnit, shared page object и desktop availability guard.
- Обратная совместимость: не менять существующие имена `AutomationId` и поведение текущих тестов.

## 5. Non-Goals (чего НЕ делаем)
- Не добавляем полноценную поддержку Eremex DataGrid как нового typed-control в AppAutomation Abstractions.
- Не меняем контракты `IGridControl`, `IGridRowControl`, `IGridCellControl`.
- Не переписываем стандартный Avalonia `DataGrid` сценарий.
- Не добавляем визуальные snapshot-тесты.
- Не добавляем зависимость Eremex в production-библиотеки AppAutomation; зависимость нужна только sample-приложению.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `Directory.Packages.props` -> централизованные версии `Eremex.Avalonia.Controls`, `Eremex.Avalonia.Themes.DeltaDesign`, минимально совместимое обновление Avalonia до `11.3.8`.
- `sample/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj` -> package reference на Eremex controls/theme для sample app.
- `sample/DotnetDebug.Avalonia/App.axaml` -> подключение Eremex DeltaDesign theme/resource according to package requirements while preserving existing Fluent/DataGrid styles.
- `sample/DotnetDebug.Avalonia/MainWindow.axaml` -> добавить Eremex DataGrid рядом с существующим DataGrid или отдельным блоком внутри `DataGridTabItem`, с отдельным `AutomationId`.
- `sample/DotnetDebug.AppAutomation.Authoring/Pages/MainWindowPage.cs` -> добавить page-object entry для Eremex grid root как `UiControlType.AutomationElement`.
- `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/FlaUiControlResolverTests.cs` или новый FlaUI test file -> desktop UIA characterization: Eremex root находится, после `Build grid` в UIA tree видны ожидаемые row/cell texts либо фиксируется устойчивый fallback-уровень.
- `sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/Tests/HeadlessControlResolverTests.cs` -> headless smoke: Eremex root с automation id резолвится как generic automation element, чтобы проверить XAML/theme load без desktop UIA.
- `ControlSupportMatrix.md` -> обновить статус DataGrid/Eremex покрытия после реализации.

### 6.2 Детальный дизайн
- Данные:
  - Eremex grid использует тот же `ItemsSource="{Binding DataGridRows}"`.
  - Колонки повторяют существующую модель: `Row`, `Value`, `Parity`.
  - Детерминированные значения остаются прежними: для 5 строк третья строка имеет `Row=R3`, `Value=13`, `Parity=Odd`.
- UI:
  - Существующий `DataGridTabItem` остается точкой входа.
  - Новый Eremex root получает стабильный id, например `EremexDemoDataGrid`.
  - Если Eremex требует namespace `mxdg`, XAML root добавляет `xmlns:mxdg="clr-namespace:Eremex.AvaloniaUI.Controls.DataGrid;assembly=Eremex.Avalonia.Controls"` или фактический namespace из пакета.
  - Если фактическое имя control/theme отличается, выбрать официальное имя из установленного пакета и отразить его в итоговом journal/post-EXEC.
- UI automation:
  - Для Eremex root использовать `UiControlType.AutomationElement`, потому что на старте неизвестно, отдаёт ли Eremex стандартный UIA `GridPattern`.
  - FlaUI test не должен требовать `AsGrid()` до подтверждения, что Eremex реально экспонируется как стандартный grid.
  - Фактическая desktop UIA характеристика после EXEC: Eremex `DataGridControl` не отдаёт стабильный `AutomationId`/row/cell descendants; тест фиксирует fallback через anchor `EremexDemoDataGrid` и business labels после построения rows.
  - Для recorder flow добавлен alias `EremexDemoDataGridControl` -> `EremexDemoDataGrid`, чтобы запись assertion по визуальному Eremex grid не генерировала неисполняемый FlaUI locator.
  - Для предотвращения ложной атрибуции стандартному Avalonia `DataGrid` у модели добавлены Eremex-only display properties `EX-*`; они остаются диагностическим UI-сигналом, но не используются как обязательный assert из-за текущей root-only экспозиции Eremex в UIA.
- Ошибки:
  - Build/restore ошибки из-за dependency mismatch решаются минимальным подъемом Avalonia пакетов до `11.3.8`.
  - Если theme include ломает стандартные controls, сначала проверить минимальное подключение только Eremex theme/resources.
- Производительность:
  - Использовать малый набор строк в тестах (`5`), не добавлять heavy virtualization сценарии.

## 7. Бизнес-правила / Алгоритмы (если есть)
- `BuildGrid(5)` должен создать 5 строк.
- Третья строка модели при индексе `2`:
  - `Row = R3`
  - `Value = 13`
  - `Parity = Odd`
- `ClearGrid()` очищает общий источник данных; оба grid-представления должны стать пустыми с точки зрения модели/labels.

## 8. Точки интеграции и триггеры
- `OnBuildGridClick` продолжает вызывать `_viewModel.BuildGrid(requestedRows)`.
- `OnClearGridClick` продолжает вызывать `_viewModel.ClearGrid()`.
- Eremex grid обновляется автоматически через binding к `DataGridRows`.
- UI-тесты триггерят существующие кнопки `BuildGridButton` / `ClearGridButton`, а не отдельную тестовую логику.

## 9. Изменения модели данных / состояния
- Новых persisted данных нет.
- Новых view-model полей не требуется, если Eremex grid использует существующую `DataGridRows`.
- Допускается добавить только read-only helper property/label в sample app, если Eremex binding API объективно требует отдельной формы данных. По умолчанию этого не делать.

## 10. Миграция / Rollout / Rollback
- Миграция данных не требуется.
- Rollout: sample app получает новые NuGet references и UI element.
- Rollback:
  - удалить Eremex package references/version entries;
  - убрать Eremex theme include;
  - удалить Eremex XAML block/page-object/test additions;
  - вернуть Avalonia package versions к прежним `11.3.7`, если другие изменения не требуют `11.3.8`.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Sample app собирается с Eremex packages.
  - В `DataGridTabItem` отображается Eremex DataGrid с `AutomationId="EremexDemoDataGrid"` или другим явно зафиксированным id.
  - Существующий сценарий стандартного `DemoDataGrid` продолжает проходить.
  - FlaUI UI-тест открывает `DataGridTabItem`, строит 5 строк и проверяет Eremex fallback anchor + business state; отсутствие стабильных Eremex row/cell descendants зафиксировано в `ControlSupportMatrix.md`.
  - Headless smoke-тест подтверждает, что Eremex root можно найти по `AutomationId` как generic control.
  - Recorder-generated fallback assertion для Eremex использует `EremexDemoDataGrid`, а не неработающий desktop locator `EremexDemoDataGridControl`.
  - `ControlSupportMatrix.md` обновлена и больше не утверждает, что DataGrid отсутствует в UI.
- Тесты добавить/изменить:
  - Eremex-specific FlaUI test.
  - Eremex generic headless resolver smoke.
  - При необходимости скорректировать shared DataGrid scenario только для устойчивости ожиданий, не расширяя его Eremex-specific логикой.
- Characterization:
  - Зафиксировать фактический уровень UIA поддержки Eremex: `GridPattern`/cell descendants/fallback root-only.
- Команды проверки:
  - `dotnet restore`
  - `dotnet build .\\AppAutomation.sln`
  - `dotnet test .\\sample\\DotnetDebug.AppAutomation.Avalonia.Headless.Tests\\DotnetDebug.AppAutomation.Avalonia.Headless.Tests.csproj`
  - `dotnet test .\\sample\\DotnetDebug.AppAutomation.FlaUI.Tests\\DotnetDebug.AppAutomation.FlaUI.Tests.csproj`
  - Если desktop UI недоступен, FlaUI tests должны быть skipped через существующий `DesktopUiAvailabilityGuard`, а это нужно явно указать в EXEC-отчёте.
- TUnit single-test запуск:
  - TUnit работает поверх Microsoft Testing Platform и не использует VSTest `--filter`.
  - Для точечного запуска использовать `dotnet run --project <test.csproj> -- --treenode-filter "/*/*/<ClassName>/<TestName>"`.
  - Для текущего Eremex FlaUI-теста: `dotnet run --project .\\sample\\DotnetDebug.AppAutomation.FlaUI.Tests\\DotnetDebug.AppAutomation.FlaUI.Tests.csproj -- --treenode-filter "/*/*/FlaUiControlResolverTests/EremexDataGridBridge_ByAutomationId_ReadsDesktopRowsAndCells"`.
  - Для всех тестов класса: `dotnet run --project <test.csproj> -- --treenode-filter "/*/*/<ClassName>/*"`.

## 12. Риски и edge cases
- Eremex 1.3.62 требует Avalonia `>= 11.3.8`; текущие `11.3.7` дадут restore downgrade/conflict без обновления.
- Подключение Eremex theme может повлиять на визуальный стиль стандартных controls; тесты должны проверять поведение, а не styling.
- Eremex DataGrid может не отдавать стандартный UIA `GridPattern`; поэтому initial mapping должен быть generic `AutomationElement`.
- В headless runtime Eremex grid может резолвиться только как generic Avalonia control; это допустимо, если desktop FlaUI test закрывает фактическую UIA характеристику.
- Если virtualization скрывает часть строк, тест использует малый row count и проверяет видимые deterministic rows.

## 13. План выполнения
1. Добавить централизованные версии Eremex packages и минимально обновить Avalonia packages до `11.3.8`.
2. Добавить package references в sample Avalonia app.
3. Подключить Eremex theme/resources в `App.axaml`.
4. Добавить Eremex DataGrid block в `MainWindow.axaml` с отдельным `AutomationId`.
5. Добавить `UiControl` entry для Eremex root в `MainWindowPage`.
6. Добавить FlaUI characterization UI test.
7. Добавить Headless generic root smoke test.
8. Обновить `ControlSupportMatrix.md`.
9. Запустить targeted tests, затем build/test команды из секции 11.
10. Выполнить post-EXEC review и исправить найденные отклонения.

## 14. Открытые вопросы
Блокирующих вопросов нет.

Неблокирующее уточнение для EXEC: фактический namespace/theme API Eremex будет проверен после restore пакетов. Если он отличается от документации/ожиданий, реализация должна использовать фактические типы из пакета без изменения целей спеки.

## 15. Соответствие профилю
- Профиль: dotnet-desktop-client, ui-automation-testing
- Выполненные требования профиля:
  - Изменение ограничено sample desktop app и UI tests.
  - UI controls получают стабильные `AutomationId`.
  - Проверки включают headless и desktop/FlaUI пути.
  - Desktop UI tests учитывают guard на доступность окружения.
  - Не добавляется новая product dependency в runtime-библиотеки AppAutomation.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `Directory.Packages.props` | Добавить Eremex package versions, обновить Avalonia до `11.3.8` | Совместимость с Eremex dependency range и central package management |
| `sample/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj` | Добавить Eremex package references | Доступ к Eremex controls/theme в sample app |
| `sample/DotnetDebug.Avalonia/App.axaml` | Подключить Eremex theme/resources | Корректный runtime rendering Eremex controls |
| `sample/DotnetDebug.Avalonia/App.axaml.cs` | Добавить recorder locator alias для Eremex visual control | Записанные recorder-сценарии должны использовать рабочий desktop anchor |
| `sample/DotnetDebug.Avalonia/MainWindow.axaml` | Добавить Eremex DataGrid root/columns/automation id | Новый UI showcase |
| `sample/DotnetDebug.Avalonia/DataGridRowViewModel.cs` | Добавить Eremex-only display properties `EX-*` | Диагностически отделить Eremex rows от стандартного DataGrid |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` | Добавить `RecorderLocatorAlias` | Конфигурация замены recorder locator на стабильный locator |
| `src/AppAutomation.Recorder.Avalonia/RecorderSelectorResolver.cs` | Применять locator aliases при записи | Не сохранять known-bad locator стороннего visual control |
| `sample/DotnetDebug.AppAutomation.Authoring/Pages/MainWindowPage.cs` | Добавить `EremexDemoDataGrid` generic control | Page object selector для тестов |
| `sample/DotnetDebug.AppAutomation.Authoring/Tests/MainWindowScenariosBase.cs` | Добавить generated-flow style сценарий для Eremex fallback anchor | Проверка, что recorder-like сценарий исполняется в headless/FlaUI |
| `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/*` | Добавить Eremex desktop UIA test | Проверка реального UIA поведения стороннего grid |
| `sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/Tests/*` | Добавить headless root smoke | Проверка XAML/theme/load и automation id |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | Добавить tests на locator alias и generated source | Проверка recorder pipeline запись -> генерация |
| `ControlSupportMatrix.md` | Обновить статус DataGrid/Eremex | Документация фактического покрытия |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Sample grid controls | Только стандартный Avalonia `DataGrid` | Standard `DataGrid` + Eremex `DataGridControl` |
| Eremex packages | Отсутствуют | Package refs только в sample app |
| DataGrid matrix | Устаревшее "нет DataGrid в UI" | Актуальный статус standard/Eremex coverage |
| UI tests | Shared standard DataGrid flow | Standard flow + Eremex fallback characterization/smoke |
| AppAutomation API | Без изменений | Без изменений, Eremex root generic |

## 18. Альтернативы и компромиссы
- Вариант: сразу маппить Eremex как `UiControlType.Grid`.
- Плюсы: typed grid API и cell access выглядят единообразно.
- Минусы: высокий риск, если Eremex не отдает стандартный UIA `GridPattern`.
- Почему выбранное решение лучше в контексте этой задачи: generic root + characterization test сначала фиксируют реальное поведение стороннего контрола; typed support можно добавить позже на основании фактов.

- Вариант: заменить существующий Avalonia `DataGrid` на Eremex.
- Плюсы: меньше UI.
- Минусы: потеря стабильного baseline для собственного headless grid wrapper.
- Почему выбранное решение лучше: оба сценария нужны, потому что standard Avalonia grid и Eremex grid закрывают разные риски.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, данные, rollback и ошибки описаны. |
| C. Безопасность изменений | 11-13 | PASS | Есть acceptance criteria, risk handling и пошаговый план. |
| D. Проверяемость | 14-16 | PASS | Блокирующих вопросов нет, команды проверки и таблица файлов есть. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, альтернативы и review зафиксированы. |
| F. Соответствие профилю | 20 | PASS | Журнал действий есть; профили отражены в секции 15. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope ограничен sample app, UI tests и matrix update. |
| 2. Понимание текущего состояния | 5 | Зафиксированы существующий DataGrid, tests, packages и устаревшая matrix. |
| 3. Конкретность целевого дизайна | 5 | Указаны файлы, ids, package strategy и test strategy. |
| 4. Безопасность (миграция, откат) | 5 | Есть минимальный package update и rollback plan. |
| 5. Тестируемость | 5 | Есть targeted commands, acceptance criteria и characterization path. |
| 6. Готовность к автономной реализации | 5 | Открытых блокеров нет, план достаточно детальный. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: учтен dependency mismatch Eremex -> Avalonia `>= 11.3.8`; Eremex mapping сделан generic до фактической проверки UIA `GridPattern`.
- Что осталось на решение пользователя: подтвердить запуск EXEC-фазы.

### EXEC Verification
| Команда | Результат |
|---|---|
| `dotnet restore .\AppAutomation.sln` | PASS; остались предупреждения `NU1903` по `Tmds.DBus.Protocol 0.21.2` |
| `dotnet build .\AppAutomation.sln --no-restore` | PASS; 5 warnings, 0 errors |
| `dotnet test --project .\tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj --no-restore` | PASS; 26/26 |
| `dotnet test --project .\tests\AppAutomation.Abstractions.Tests\AppAutomation.Abstractions.Tests.csproj --no-restore` | PASS; 23/23 |
| `dotnet test --project .\sample\DotnetDebug.AppAutomation.Avalonia.Headless.Tests\DotnetDebug.AppAutomation.Avalonia.Headless.Tests.csproj --no-build` | PASS; 36/36 |
| `dotnet test --project .\sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj --no-restore` | PASS; 23/23 |
| `dotnet test --solution .\AppAutomation.sln --no-build` | PASS; 156/156 |
| `git diff --check` | PASS; whitespace errors 0; Git warned only about future CRLF normalization |

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: root-only fallback заменен на recorder bridge/provider; recorder assertions по Eremex root/cell генерируют `WaitUntilGridRowsAtLeast` и `WaitUntilGridCellEquals`; FlaUI читает bridge rows/cells из UIA descendants, Headless использует visual descendants или `ItemsSource` fallback.
- Что проверено дополнительно для refactor / comments: Eremex dependency осталась только в sample app; прямой `Microsoft.CodeAnalysis.CSharp.Scripting 4.11.0` reference оставлен как NuGet conflict override между Eremex transitive Roslyn 4.3.0 и recorder Roslyn 4.11.0; `AutomationId` существующего `DemoDataGrid` не изменен; `ControlSupportMatrix.md` больше не описывает Eremex как только root-only для recorder playback; recorder-generated style сценарий проходит в Headless/FlaUI.
- Остаточные риски / follow-ups: нативный Eremex `DataGridControl` всё ещё не экспонирует стабильные row/cell descendants или `GridPattern` в desktop UIA; полноценность обеспечивается bridge/provider-слоем, а не native Eremex automation peer.

## 19.1 Scope Amendment: recorder bridge/provider для Eremex DataGrid
- Статус: пользователь расширил задачу после EXEC-проверок и попросил "сделать адаптер или провайдер", чтобы UI-тесты с Eremex DataGrid можно было полноценно записывать и воспроизводить.
- Изменение границ:
  - Supersedes Non-Goals из секции 5 в части typed recorder support для Eremex DataGrid.
  - Production-библиотеки AppAutomation по-прежнему не получают прямую зависимость от Eremex.
  - Поддержка реализуется через explicit recorder bridge/hint + стандартный `IGridControl`, а не через нативный Eremex UIA `GridPattern`, потому что фактическая проверка desktop UIA показала root-only поведение.
- Дизайн:
  - В sample app рядом с Eremex `DataGridControl` добавляется visual-grid automation bridge с `AutomationId="EremexDemoDataGridAutomationBridge"`, привязанный к тем же `DataGridRows` и Eremex-only display properties `EremexRow`, `EremexValue`, `EremexParity`.
  - Bridge размечает строки и ячейки стабильными ids: `EremexDemoDataGridAutomationBridge_Row{index}` и `..._Cell{columnIndex}`.
  - Recorder получает `RecorderGridHint`, который связывает source locator `EremexDemoDataGridControl` с bridge locator `EremexDemoDataGridAutomationBridge` и списком property names для снимка ячеек.
  - FlaUI resolver получает visual-grid provider: если `UiControlType.Grid` не поддерживает native `GridPattern`, строки/ячейки читаются из UIA descendants по bridge ids.
  - Headless resolver получает аналогичный provider: сначала visual descendants, затем fallback на `ItemsSource` bridge-контрола, потому что headless Avalonia может не материализовать item-template visual rows.
  - Recorder-generated code получает новые assertion actions:
    - `Page.WaitUntilGridRowsAtLeast(...)`
    - `Page.WaitUntilGridCellEquals(...)`
  - Shared abstraction `UiPageExtensions` получает grid wait helpers, чтобы сгенерированный код был runtime-neutral и работал в Headless/FlaUI через существующий `IGridControl`.
- Acceptance Criteria amendment:
  - Запись assertion по Eremex grid root генерирует `WaitUntilGridRowsAtLeast` на bridge-grid, а не generic enabled fallback.
  - Запись assertion по визуальному cell-like child с `DataContext` строки генерирует `WaitUntilGridCellEquals` на bridge-grid.
  - Сценарий authoring tests проверяет rows + deterministic Eremex cell values `EX-R3`, `EX-13`, `EX-Odd`.
  - Headless/FlaUI smoke tests подтверждают, что bridge резолвится как `IGridControl` и дает доступ к rows/cells.
  - `ControlSupportMatrix.md` отражает bridge/provider уровень поддержки, а не root-only fallback.

## Approval
Спека подтверждена пользователем фразой "Спеку подтверждаю"; последующее расширение scope на recorder bridge/provider принято в EXEC как уточнение цели.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Контекст sample app и UI tests | 0.9 | Нет | Создать рабочую спеку | Нет | Нет | В репозитории уже есть стандартный DataGrid; Eremex нужен отдельным showcase | `MainWindow.axaml`, `MainWindowPage.cs`, `MainWindowScenariosBase.cs`, `Directory.Packages.props`, `ControlSupportMatrix.md` |
| SPEC | NuGet/dependency discovery | 0.9 | Точный runtime namespace Eremex будет проверен после restore | Зафиксировать package/version strategy | Нет | Нет | Eremex 1.3.62 требует Avalonia >= 11.3.8, текущий repo на 11.3.7 | NuGet metadata, `Directory.Packages.props` |
| SPEC | Quality gate | 0.95 | Нет | Ждать подтверждения `Спеку подтверждаю` | Да | Нет | QUEST требует явного approval перед EXEC | `specs/2026-04-22-eremex-datagrid-ui-coverage.md` |
| EXEC | TUnit single-test command discovery | 0.95 | Нет | Использовать `dotnet run --project ... -- --treenode-filter` для точечных запусков | Нет | Да, пользователь запросил найти и записать инструкцию | TUnit не поддерживает VSTest `--filter`; официальный путь — tree-node filter | `specs/2026-04-22-eremex-datagrid-ui-coverage.md` |
| EXEC | Eremex UI implementation | 0.85 | Нет | Запустить полный headless/FlaUI набор и solution build | Нет | Нет | Eremex grid добавлен в sample app; desktop UIA показал root-only/fallback поведение | `Directory.Packages.props`, `DotnetDebug.Avalonia.csproj`, `App.axaml`, `MainWindow.axaml`, `DataGridRowViewModel.cs`, `MainWindowPage.cs`, UI tests, `ControlSupportMatrix.md` |
| EXEC | Verification and post-EXEC review | 0.95 | Нет | Финальный отчет пользователю | Нет | Нет | Restore/build/headless/FlaUI/full solution проверки прошли; остаточный риск Eremex UIA зафиксирован как follow-up | `specs/2026-04-22-eremex-datagrid-ui-coverage.md`, `ControlSupportMatrix.md`, UI tests |
| EXEC | Recorder Eremex fallback hardening | 0.9 | Нет | Повторить full build/test | Нет | Да, пользователь уточнил, будут ли работать recorder-тесты | Добавлен locator alias `EremexDemoDataGridControl` -> `EremexDemoDataGrid`; generated-flow сценарий и recorder tests подтверждают исполнимую fallback-запись | `AppAutomationRecorderOptions.cs`, `RecorderSelectorResolver.cs`, `App.axaml.cs`, `RecorderTests.cs`, `MainWindowScenariosBase.cs`, spec/matrix |
| EXEC | Recorder bridge/provider completion | 0.94 | Нет | Запустить full solution test и post-EXEC review | Нет | Да, пользователь попросил "дотащить" через adapter/provider | Bridge переведен на deterministic visual-grid; FlaUI читает UIA row/cell descendants, Headless имеет visual + ItemsSource fallback; recorder генерирует grid waits на bridge | `MainWindow.axaml`, `DataGridRowViewModel.cs`, `FlaUiControlResolver.cs`, `HeadlessControlResolver.cs`, `ControlSupportMatrix.md`, UI tests |
