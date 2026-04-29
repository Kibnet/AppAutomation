# DotnetDebug Recorder Desktop Smoke

## 0. Метаданные
- Тип (профиль): `ui-automation-testing` + `dotnet-desktop-client`
- Владелец: Codex
- Масштаб: medium
- Целевой релиз / ветка: `feat-arm-paritet`
- Ограничения:
  - До подтверждения спеки изменяется только этот файл.
  - Тесты должны использовать существующий `FlaUI` desktop harness, без нового launcher stack.
  - Проверка должна идти через реальный `DotnetDebug.Avalonia` desktop startup path с `APPAUTOMATION_RECORDER=1`, а не через headless attach.
  - Recorder smoke должен запускать `DotnetDebug.Avalonia` именно в `Debug` build configuration, потому что attach recorder-а находится под `#if DEBUG`.
  - Desktop smoke tests должны быть сериализованы через `[NotInParallel("DesktopUi")]` / `DesktopUiConstraint`, чтобы hotkey-save и focus не пересекались с другими UI tests.
  - Сохранённый recorder output должен писаться во временную директорию вне authoring project, чтобы не загрязнять `sample/DotnetDebug.AppAutomation.Authoring`.
  - В пакет не входит полное e2e-покрытие всех recorder features; нужен целевой smoke для поддержанных recorder-aware контролов.
- Связанные ссылки:
  - `sample/DotnetDebug.Avalonia/App.axaml.cs`
  - `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/MainWindowFlaUiRuntimeTests.cs`
  - `sample/DotnetDebug.AppAutomation.Authoring/Pages/MainWindowPage.cs`
  - `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`
  - `specs/2026-04-26-wrapper-control-proxy-support.md`

## 1. Overview / Цель
Добавить реальные desktop e2e/smoke тесты для `DotnetDebug`, которые:
- запускают `DotnetDebug.Avalonia` с включённым recorder через environment variables;
- выполняют пользовательские действия по целевым контролам;
- инициируют `Save` recorder-а;
- проверяют содержимое фактически сгенерированного scenario-файла.

Цель пакета: доказать, что recorder не только локально проходит unit/contract tests, но и реально работает в desktop startup flow sample-приложения.

## 2. Текущее состояние (AS-IS)
- `DotnetDebug.Avalonia` уже умеет включать recorder в `DEBUG`, если задан `APPAUTOMATION_RECORDER=1`; в `Release` этот attach path compile-time отсутствует.
- Recorder стартует автоматически через `AppAutomationRecorder.Attach(mainWindow, options); session.Start();`.
- В sample сейчас уже настроены:
  - `RecorderControlHint` для `MixCountSpinner`;
  - `RecorderLocatorAlias` + `RecorderGridHint` для grid automation bridges;
  - `RecorderSearchPickerHint` для `ArmSearchPicker` и `ArmServerSearchPicker`.
- В sample recorder configuration пока нет hints для `ArmDialog`, `ArmNotification`, `ArmShellNavigation`; их нужно добавить явно, потому что smoke scope требует composite collapse.
- В sample `FlaUI` runtime tests уже умеют запускать `DotnetDebug.Avalonia` через `DesktopAppSession.Launch(...)` и работать с `MainWindowPage`.
- Существующие inherited UI scenarios сериализованы через `DesktopUiConstraint`; новый recorder smoke должен использовать тот же desktop constraint.
- В `MainWindowPage` composite controls (`ArmSearchPicker`, `ArmDialog`, `ArmNotification`, `ArmShellNavigation` и др.) описаны вручную как logical properties, а не через `[UiControl(...)]` attribute.
- `AuthoringProjectScanner` при сохранении recorder output видит только `[UiControl(...)]` attributes и не знает о ручных logical properties в page class.
- Из-за этого прямое сохранение recorder output в `sample/DotnetDebug.AppAutomation.Authoring/Recorded` рискованно:
  - оно оставляет мусорные generated файлы в репозитории;
  - generated controls partial может дублировать logical property names на уровне generator input.

## 3. Проблема
Сейчас в репозитории нет ни одного реального desktop autotest-а, который подтверждает, что `DotnetDebug` с включённым recorder действительно записывает корректные шаги по целевым контролам и сохраняет правильный generated scenario file.

## 4. Цели дизайна
- Использовать существующий `DotnetDebug` desktop startup flow без специального тестового обхода recorder-а.
- Проверять результат через реальный `Save`, а не через внутренние тестовые hooks session-а.
- Изолировать generated artifacts от репозитория и гарантировать cleanup.
- Проверять не только наличие ожидаемых DSL-строк, но и отсутствие очевидного primitive leakage для composite workflows.
- Сохранить стабильные `automation-id` и существующий `FlaUI` test harness.

## 5. Non-Goals (чего НЕ делаем)
- Не строим новый универсальный recorder e2e framework поверх всех sample apps.
- Не добавляем headless recorder smoke как substitute для desktop path.
- Не покрываем в этом пакете все composite/runtime families из backlog.
- Не меняем `AuthoringProjectScanner`, чтобы он понимал ручные logical properties в `MainWindowPage`.
- Не добавляем compile-time verification generated output в authoring project как часть этого пакета.
- Не пытаемся закрыть `DateRangeFilter`, `NumericRangeFilter`, `FolderExport` recorder e2e, если у них нет завершённого recorder-specific collapse flow в sample config.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `sample/DotnetDebug.Avalonia/App.axaml.cs`
  - расширить sample recorder configuration:
    - добавить env override для recorder output directory;
    - добавить обязательные recorder hints для composite controls из smoke scope:
      - `RecorderDialogHint("ArmDialog", DialogControlParts.ByAutomationIds("ArmDialogMessage", "ArmDialogConfirmButton", cancelButtonAutomationId: "ArmDialogCancelButton", dismissButtonAutomationId: "ArmDialogDismissButton"))`;
      - `RecorderNotificationHint("ArmNotification", NotificationControlParts.ByAutomationIds("ArmNotificationText", dismissButtonAutomationId: "ArmNotificationDismissButton"))`;
      - `RecorderShellNavigationHint("ArmShellNavigation", ShellNavigationParts.ByAutomationIds("ArmShellNavigationList", paneTabsAutomationId: "ArmShellPaneTabs", activePaneLabelAutomationId: "ArmShellActivePaneLabel", navigationKind: ShellNavigationSourceKind.ListBox))`.
- `sample/DotnetDebug.AppAutomation.FlaUI.Tests/...`
  - добавить desktop recorder smoke tests;
  - добавить helper для запуска app в `Debug` configuration с recorder env vars, уникальным scenario name и temp output dir;
  - добавить polling/read helpers для сохранённых `.g.cs` файлов и гарантированного cleanup.

### 6.2 Детальный дизайн
- Тестовый пакет остаётся в `sample/DotnetDebug.AppAutomation.FlaUI.Tests`, потому что:
  - нужен реальный desktop `DotnetDebug.Avalonia`;
  - уже есть `DesktopUiAvailabilityGuard`;
  - уже есть runtime page-object wiring через `FlaUiControlResolver`.
- Для включения recorder тест создаёт launch options на базе `DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(buildConfiguration: "Debug", ...)`.
  - `Debug` должен быть зафиксирован явно: иначе `#if DEBUG` в sample app не подключит recorder даже при наличии env vars.
  - Helper должен сохранить `ExecutablePath`, `WorkingDirectory`, `Arguments`, `DisposeCallback`, `MainWindowTimeout` и `PollInterval` из базовых options и только расширить `EnvironmentVariables`.
- Затем helper добавляет recorder env vars:
  - `APPAUTOMATION_RECORDER=1`
  - `APPAUTOMATION_RECORDER_SCENARIO=<unique>`
  - новый sample-only override, например `APPAUTOMATION_RECORDER_OUTPUT_DIRECTORY=<abs-temp-dir>`
- В `App.axaml.cs` sample читает override output path и пробрасывает его в `AppAutomationRecorderOptions.OutputSubdirectory`.
  - Здесь намеренно используется абсолютный путь.
  - `AuthoringCodeGenerator` строит output через `Path.Combine(projectDirectory, options.OutputSubdirectory)`, а абсолютный второй аргумент переопределяет базовый путь и позволяет писать вне project directory.
- Тест выполняет пользовательские действия через существующий `MainWindowPage` и `FlaUiControlResolver`, чтобы recorder видел реальный UI input path.
- `Save` инициируется через recorder hotkey `Ctrl+Shift+S` с фокусом на основном окне.
  - Это лучше, чем искать overlay window по UIA:
    - меньше desktop flakiness;
    - проверяется реальный recorder command path из `RecorderSession.OnKeyDown(...)`.
- После `Save` тест polling-ом ждёт появление файла:
  - `MainWindowScenariosBase.<ScenarioName>.*.g.cs`
  - и при необходимости page controls partial:
    - `MainWindowPage.<ScenarioName>.controls.g.cs`
- Проверка корректности делается по содержимому scenario source:
  - наличие ожидаемых typed/generated statements;
  - отсутствие undesired primitive leakage по внутренним control ids / raw button ids.

### 6.3 Минимальный целевой smoke scope
Первый пакет закрывает только те recorder-aware flows, для которых уже есть или легко добавляется честная sample configuration:
1. `MixCountSpinner`
   - ожидается `Page.SetSpinnerValue(...)`, а не raw `EnterText(...)`.
2. `ArmSearchPicker`
   - ожидается `Page.SearchAndSelect(...)`;
   - не должно быть raw `ArmSearchInput` / `ArmSearchApplyButton`.
3. `ArmServerSearchPicker`
   - ожидается `Page.SearchAndSelect(...)`;
   - не должно быть raw `ArmServerPickerOpenButton`.
4. `ArmDialog`
   - после добавления sample recorder hint ожидается `Page.ConfirmDialog(...)`;
   - не должно быть raw `ArmDialogConfirmButton`.
5. `ArmNotification`
   - после добавления sample recorder hint ожидается `Page.DismissNotification(...)`;
   - не должно быть raw `ArmNotificationDismissButton`.
6. `ArmShellNavigation`
   - после добавления sample recorder hint ожидается `Page.OpenOrActivateShellPane(...)` или `Page.ActivateShellPane(...)`;
   - не должно быть raw selection leakage по pane-tab control ids.

### 6.4 Формат тестов
- Предпочтительный набор:
  - один тест на spinner;
  - один тест на search picker collapse;
  - один тест на dialog/notification/shell composite capture.
- Все три теста:
  - `DesktopUiAvailabilityGuard.SkipIfUnavailable();`
  - `[NotInParallel("DesktopUi")]` или `[NotInParallel(DesktopUiConstraint)]`, если тестовый класс наследуется от `UiTestBase`;
  - создают уникальный scenario name;
  - используют temp output dir;
  - очищают temp artifacts в `finally`.

### 6.5 Почему не проверяем overlay UI напрямую
- Overlay является отдельным desktop window и создаёт лишнюю хрупкость при поиске top-level windows в CI/interactive desktop.
- Нам важнее проверить:
  - attach recorder-а;
  - запись steps;
  - hotkey command path;
  - сохранение generated output.
- Поэтому scenario-file assertions дают лучший signal-to-noise ratio, чем UI-проверка текста в overlay.

## 7. Бизнес-правила / Алгоритмы
- Desktop recorder smoke считается успешным только если:
  - приложение реально запущено через desktop launcher;
  - desktop launcher использует `Debug` build configuration;
  - recorder реально включён через env vars;
  - сценарий реально сохранён через `Save`;
  - сохранённый `.g.cs` содержит ожидаемые typed steps.
- Если тест проверяет composite flow, он обязан также проверять хотя бы один negative assertion на primitive leakage.
- Все generated artifacts должны жить вне authoring project directory и быть удалены после теста независимо от исхода.

## 8. Точки интеграции и триггеры
- `DotnetDebug.Avalonia.App.OnFrameworkInitializationCompleted()`
  - attach recorder-а при запуске приложения.
- `RecorderSession.OnKeyDown(...)`
  - обработка hotkey `Ctrl+Shift+S`.
- `AuthoringCodeGenerator.SaveAsync(...)`
  - фактическое создание scenario `.g.cs`.
- `sample/DotnetDebug.AppAutomation.FlaUI.Tests`
  - точка orchestration desktop smoke flow.

## 9. Изменения модели данных / состояния
- persisted state приложения не меняется.
- Добавляется только sample-level конфигурация окружения:
  - env override для recorder output directory.
- Временные generated artifacts создаются во внешней temp directory и удаляются тестом.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - добавить env override в sample app;
  - при необходимости добавить missing sample recorder hints;
  - добавить `FlaUI` desktop smoke tests;
  - прогнать targeted tests и full solution tests.
- Rollback:
  - удалить новый env override и desktop recorder smoke tests;
  - убрать добавленные sample recorder hints, если они были введены только ради smoke scope.
- Обратная совместимость:
  - запуск sample без env override должен остаться прежним;
  - existing runtime/UI tests не должны измениться по поведению.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - desktop test реально запускает `DotnetDebug.Avalonia` в `Debug` configuration с `APPAUTOMATION_RECORDER=1`;
  - recorder `Save` вызывается через hotkey path;
  - scenario file создаётся во внешней temp directory;
  - recorder smoke tests сериализованы через desktop `NotInParallel` constraint;
  - spinner smoke даёт `Page.SetSpinnerValue(static page => page.MixCountSpinner, ...)`;
  - search picker smoke даёт `Page.SearchAndSelect(static page => page.ArmSearchPicker, ...)` и не содержит raw `ArmSearchInput` / `ArmSearchApplyButton`;
  - server search picker smoke даёт `Page.SearchAndSelect(static page => page.ArmServerSearchPicker, ...)` и не содержит raw `ArmServerPickerOpenButton`;
  - dialog/notification/shell smoke даёт соответствующие composite DSL statements и не содержит raw primitive leakage по `ArmDialogConfirmButton`, `ArmNotificationDismissButton`, `ArmShellNavigationList`, `ArmShellPaneTabs` и pane item ids;
  - temp artifacts удаляются тестом.
- Какие тесты добавить/изменить:
  - новый test file в `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/` для recorder desktop smoke;
  - при необходимости обновить `sample/DotnetDebug.Tests` или `tests/AppAutomation.Recorder.Avalonia.Tests`, только если появится новый sample env-override helper logic и её нужно отдельно зафиксировать unit-level test-ом.
- Characterization tests / contract checks:
  - сохранить существующие `MainWindowFlaUiRuntimeTests` и `RecorderTests` без ослабления.
  - если helper для recorder launch options клонирует базовые `DesktopAppLaunchOptions`, unit-level test должен проверить сохранение `DisposeCallback` и merge env vars, чтобы isolated build cleanup не потерялся.
- Команды для проверки:
  - `dotnet test --project sample/DotnetDebug.AppAutomation.FlaUI.Tests/DotnetDebug.AppAutomation.FlaUI.Tests.csproj`
  - `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj`
  - если recorder overlay tests падают на Avalonia metadata race при параллельном TUnit run: `dotnet run --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -- --maximum-parallel-tests 1`
  - `dotnet build`
  - `dotnet test --solution AppAutomation.sln`
  - если full solution run падает только на ту же recorder overlay parallel race: `dotnet test --solution AppAutomation.sln -- --maximum-parallel-tests 1`
  - `git diff --check`

## 12. Риски и edge cases
- Desktop UI tests требуют интерактивной Windows-сессии; на non-interactive среде тесты должны быть skipped, а не падать ложным failure.
- Recorder attach находится под `#if DEBUG`; случайный `Release` launch даст ложный failure с отсутствующим output.
  - Смягчение: helper всегда вызывает desktop launch host с `buildConfiguration: "Debug"` и acceptance проверяет env-enabled Debug startup.
- Hotkey и focus являются процессно-глобальными для desktop session.
  - Смягчение: каждый recorder smoke помечается `NotInParallel("DesktopUi")` / `DesktopUiConstraint`.
- Overlay окно может кратковременно перехватывать фокус.
  - Смягчение: перед hotkey-save явно возвращать фокус в main window или выполнять действие через focused app control.
- `AuthoringProjectScanner` не видит ручные logical properties.
  - Смягчение: писать output во внешнюю temp directory и не полагаться на compileability generated artifacts внутри sample authoring project.
- Save происходит асинхронно.
  - Смягчение: polling file creation с детерминированным timeout.
- Если hotkey path окажется нестабильным в desktop test env, fallback-вариант остаётся через поиск overlay window по title `"AppAutomation Recorder"`, но это не должно быть основным design path в первом пакете.

## 13. План выполнения
1. Добавить рабочую spec для desktop recorder smoke.
2. Добавить в `DotnetDebug.Avalonia` env override для recorder output directory.
3. Добавить обязательные sample recorder hints для `ArmDialog`, `ArmNotification`, `ArmShellNavigation`.
4. Добавить `FlaUI` desktop recorder smoke tests с `Debug` launch, desktop `NotInParallel` constraint, temp output dir и cleanup.
5. Прогнать targeted `FlaUI` + recorder tests.
6. Прогнать `dotnet build`, `dotnet test AppAutomation.sln`, `git diff --check`.
7. Выполнить post-EXEC review и закрыть пакет.

## 14. Открытые вопросы
Блокирующих вопросов нет.

Есть одно осознанное ограничение:
- пакет проверяет recorder correctness по содержимому сохранённого scenario-файла, а не по визуальному состоянию overlay.

## 15. Соответствие профилю
- Профиль: `ui-automation-testing` + `dotnet-desktop-client`
- Выполненные требования профиля:
  - используется существующий `FlaUI` desktop suite;
  - проверка строится на стабильных `automation-id`;
  - проверяется реальный UI behavior / recorder flow;
  - desktop UI smoke сериализован через принятый `DesktopUi` constraint;
  - в финале должны быть прогнаны релевантные UI tests и полный solution test run.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-04-27-dotnetdebug-recorder-desktop-smoke.md` | Новая рабочая спека | QUEST gate перед кодовыми изменениями |
| `sample/DotnetDebug.Avalonia/App.axaml.cs` | Будет добавлен env override и обязательные composite recorder hints для `ArmDialog`, `ArmNotification`, `ArmShellNavigation` | Реальный desktop recorder smoke, artifact isolation и composite collapse |
| `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/*` | Будут добавлены desktop recorder smoke tests с `Debug` launch и `DesktopUi` serialization | E2E-проверка attach/record/save flow без Release/focus flakiness |
| `tests/AppAutomation.Recorder.Avalonia.Tests/*` | Опционально, только если потребуется точечная фиксация нового sample config behavior | Сохранить coverage для новой конфигурации |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Recorder в sample app | Есть только unit/contract confidence | Появляется реальный desktop smoke с сохранением scenario file |
| Recorder artifacts в tests | Нет real save-output verification | Проверяется фактический `.g.cs` output |
| Artifact isolation | Sample save идёт в project-relative `Recorded` | Tests направляют output во внешнюю temp directory |
| Composite recorder flows | Доказаны только локальными tests | Часть flows подтверждается через реальный `DotnetDebug` desktop startup path |

## 18. Альтернативы и компромиссы
- Вариант: тестировать recorder только через `RecorderSession` unit hooks.
  - Плюсы: быстро, детерминированно.
  - Минусы: не проверяет sample app startup, hotkeys, desktop input и реальный `Save`.
  - Почему выбранное решение лучше: пользователь явно просит автотесты на запуск `DotnetDebug` с включённым recorder.
- Вариант: искать overlay window и нажимать `Save` по UIA.
  - Плюсы: ближе к overlay UX.
  - Минусы: выше flakiness, сложнее окно-менеджмент, слабее сигнал по recorder command path.
  - Почему выбранное решение лучше: hotkey путь проще, стабильнее и всё ещё полностью реальный.
- Вариант: писать save-output в `sample/DotnetDebug.AppAutomation.Authoring/Recorded`.
  - Плюсы: нулевая дополнительная конфигурация.
  - Минусы: грязные generated files в repo, риск конфликтов с ручными composite properties и последующими builds.
  - Почему выбранное решение лучше: temp output dir делает тесты повторяемыми и безопасными.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, границы и non-goals определены. |
| B. Качество дизайна | 6-10 | PASS | Зафиксированы Debug startup path, hotkey save, temp output strategy, обязательные composite hints и smoke scope. |
| C. Безопасность изменений | 11-13 | PASS | Rollback и artifact isolation описаны; repo pollution исключён по дизайну. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria покрывают Debug launch, desktop serialization, output file и negative assertions. |
| E. Готовность к автономной реализации | 17-19 | PASS | Блокирующих открытых вопросов нет; обязательные hints и launch constraints определены. |
| F. Соответствие профилю | 20 | PASS | Используется существующий UI suite, stable automation ids и desktop `NotInParallel` constraint. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Пакет ограничен desktop recorder smoke для конкретных target flows. |
| 2. Понимание текущего состояния | 5 | Учитывает текущий sample startup, recorder config и scanner limitation. |
| 3. Конкретность целевого дизайна | 5 | Детально описаны Debug launch, env overrides, save trigger, composite hints, file assertions и cleanup. |
| 4. Безопасность (миграция, откат) | 5 | Изменение additive; artifacts вынесены во внешнюю temp dir. |
| 5. Тестируемость | 5 | Проверка основана на реальном scenario output и конкретных negative assertions. |
| 6. Готовность к автономной реализации | 5 | План прямой, без продуктовых развилок. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - из scope сразу исключена overlay-UI-centric проверка как более хрупкая;
  - зафиксирован temp output dir, чтобы не загрязнять authoring project;
  - отражён scanner limitation для ручных composite properties;
  - smoke scope ограничен теми control families, для которых есть честный recorder collapse path;
  - по review дополнительно зафиксированы `Debug` launch из-за `#if DEBUG`, desktop `NotInParallel` constraint и обязательные hints для `ArmDialog`, `ArmNotification`, `ArmShellNavigation`.
- Что осталось на решение пользователя:
  - подтвердить переход в EXEC фразой `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения:
  - добавлен sample env override для внешней recorder output directory;
  - добавлены обязательные sample hints для `ArmDialog`, `ArmNotification`, `ArmShellNavigation`;
  - добавлены `FlaUI` recorder smoke tests с `Debug` launch, temp output dir, hotkey save и negative assertions;
  - добавлена unit-style проверка, что recorder launch helper сохраняет базовые launch options и merge-ит env vars;
  - общий `FlaUI` page-object wiring вынесен в factory, чтобы новый smoke и существующие runtime tests использовали одинаковые adapters;
  - новые test method names переименованы без `_`, чтобы не добавлять новые CA1707 analyzer warnings.
- Остаточные риски / follow-ups:
  - в текущей среде нет доступа к interactive input desktop, поэтому новые real desktop smoke tests были skipped guard-ом; их нужно выполнить в интерактивной Windows-сессии;
  - стандартный parallel run recorder suite и full solution run проявляют существующую Avalonia overlay metadata race; sequential TUnit run с `--maximum-parallel-tests 1` проходит.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Формирование пакета desktop recorder smoke для `DotnetDebug` | 0.96 | Подтверждение пользователя для EXEC | Ожидать фразу `Спеку подтверждаю` | Да | Да, требуется подтверждение | QUEST-процесс требует SPEC-first перед кодом; отдельная спека нужна из-за нового desktop e2e scope | `specs/2026-04-27-dotnetdebug-recorder-desktop-smoke.md` |
| SPEC | Исправление review-находок спеки | 0.98 | Подтверждение пользователя для EXEC | Ожидать фразу `Спеку подтверждаю` | Да | Да, пользователь попросил исправить review findings | Уточнены условия, без которых desktop recorder smoke мог быть ложнопадающим или flaky: `Debug` launch, `DesktopUi` serialization и обязательные composite hints | `specs/2026-04-27-dotnetdebug-recorder-desktop-smoke.md` |
| EXEC | Переход к реализации после подтверждения спеки | 0.98 | Нет | Внести кодовые изменения в границах утверждённой спеки | Нет | Да, пользователь подтвердил фразой `спеку подтверждаю` | QUEST-переход в EXEC разрешён; дальше допустимы изменения sample app и тестов из таблицы файлов | `specs/2026-04-27-dotnetdebug-recorder-desktop-smoke.md` |
| EXEC | Реализация recorder config и FlaUI smoke tests | 0.92 | Результат компиляции и targeted tests | Запустить сборку/targeted проверки и исправить compile/runtime issues | Нет | Нет | Добавлены env override, composite hints, общий FlaUI page factory, recorder desktop smoke tests и проверка merge launch options | `sample/DotnetDebug.Avalonia/App.axaml.cs`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/MainWindowFlaUiRuntimeTests.cs`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/MainWindowFlaUiPageFactory.cs`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/DotnetDebugRecorderDesktopSmokeTests.cs` |
| EXEC | Проверки и post-EXEC review | 0.9 | Интерактивная desktop-сессия для фактического выполнения FlaUI smoke | Завершить отчёт пользователю | Нет | Нет | Targeted FlaUI и build проходят; real desktop smoke skipped guard-ом в текущей среде; recorder/full solution проходят при sequential TUnit run, а parallel run выявляет существующую Avalonia overlay race | `specs/2026-04-27-dotnetdebug-recorder-desktop-smoke.md` |
