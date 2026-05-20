# Окно настроек глобальных клавиш рекордера

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: AppAutomation.Recorder.Avalonia
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до подтверждения спеки менять только этот файл; публичный API не ломать; настройки хранить в пользовательском стандартном каталоге ОС; изменения клавиш применять сразу
- Связанные ссылки: `src/AppAutomation.Recorder.Avalonia`, `tests/AppAutomation.Recorder.Avalonia.Tests`

Если секция не применима, явно укажите `Не применимо` и короткую причину, вместо заполнения нерелевантными деталями.

## 1. Overview / Цель
Добавить в overlay рекордера окно настроек для назначения горячих клавиш команд рекордера. Пользователь должен менять сочетания без перезапуска приложения: новая карта клавиш сразу используется обработчиком команд и сразу отображается в панели подсказок.

Outcome contract:
- Success means:
  - В overlay есть доступная кнопка/команда открытия настроек горячих клавиш.
  - Окно настроек показывает все команды из `RecorderCommandKind` и текущие сочетания.
  - Сохранение пишет настройки в стандартный пользовательский каталог ОС через `Environment.SpecialFolder.ApplicationData`.
  - После сохранения активная `RecorderSession` использует новую карту клавиш без пересоздания сессии.
  - `ShortcutText` в overlay сразу показывает обновленную легенду.
  - Невалидные или дублирующиеся сочетания не применяются и отображаются пользователю как ошибка.
- Итоговый артефакт / output: код фичи, тесты, обновленная рабочая спецификация с EXEC-журналом и результатами проверок.
- Stop rules:
  - На SPEC остановиться после quality gate и запроса подтверждения.
  - На EXEC остановиться после реализации, таргетных тестов, `dotnet build`, полного `dotnet test` или объясненного fallback.

## 2. Текущее состояние (AS-IS)
- Горячие клавиши задаются только через `AppAutomationRecorderOptions.Hotkeys`.
- `RecorderHotkeyMap.Create(options.Hotkeys)` строит immutable map в `RecorderSession` и в `RecorderOverlay.Attach`.
- `RecorderSession.OnKeyDown` проверяет `_hotkeyMap.TryGetCommand(...)`, затем вызывает `HandleRecorderCommand(...)`.
- `RecorderOverlay` один раз записывает легенду в `ShortcutText` при `Attach`.
- UI overlay находится в `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml` и code-behind.
- Есть тест `HotkeyMap_UsesConfiguredGestures_AndBuildsLegend` в `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`.
- Persisted пользовательских настроек горячих клавиш сейчас нет.

Скрытые зависимости / инварианты:
- Команды клавиш должны оставаться совместимыми с `RecorderCommandKind`.
- Существующая настройка через `AppAutomationRecorderOptions.Hotkeys` должна остаться источником дефолтов и не должна ломаться.
- Обновление клавиш не должно блокировать UI-поток длительным IO.
- Overlay и session должны получать один и тот же актуальный набор клавиш.

## 3. Проблема
Пользователь не может назначить горячие клавиши из самого рекордера, а текущая карта клавиш фиксируется при создании session/overlay и не обновляет подсказки после изменения.

## 4. Цели дизайна
- Разделение ответственности: парсинг/валидация, persistence и UI-настройки разделены.
- Повторное использование: существующий `RecorderHotkeyMap` используется и для обработки команд, и для легенды.
- Тестируемость: валидация, путь хранения, merge defaults + saved settings и runtime update покрываются unit/headless тестами.
- Консистентность: стиль UI следует существующему overlay без новых внешних UI-библиотек.
- Обратная совместимость: существующие consumer-настройки через `AppAutomationRecorderOptions.Hotkeys` продолжают работать как дефолты.

## 5. Non-Goals (чего НЕ делаем)
- Не добавлять OS-level registration через Win32 `RegisterHotKey`; текущие "глобальные" клавиши остаются scoped к окну приложения/рекордера, как существующий обработчик `KeyDown`.
- Не менять публичную форму `RecorderHotkeys` без необходимости.
- Не добавлять cloud/sync настроек.
- Не менять генерацию сценариев, capture logic или validation pipeline.
- Не коммитить видео-артефакты в репозиторий.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `RecorderHotkeyMap.cs` -> расширить модель доступа к shortcuts: перечисление команд, получение display text, валидация дубликатов.
- Новый `RecorderHotkeySettingsStore` -> читать/писать пользовательский JSON в `%APPDATA%/AppAutomation/Recorder/hotkeys.json` на Windows и соответствующий `ApplicationData` на других ОС.
- Новый `RecorderHotkeySettings` / snapshot type -> хранить строки сочетаний по командам и строить `RecorderHotkeys` поверх дефолтов.
- `RecorderSession` -> заменить readonly `_hotkeyMap` на обновляемый map и добавить internal/public-internal метод применения нового hotkey snapshot.
- `RecorderOverlay` -> добавить кнопку `Settings`, открыть dialog/window настроек, применить результат к session и обновить `ShortcutText`.
- Новый `RecorderHotkeySettingsWindow.axaml(.cs)` или аналогичный control -> UI редактирования сочетаний.
- `RecorderTests.cs` -> unit tests для store/validation/map и overlay/session update без полноценного video, если runner не поддерживает запись.

### 6.2 Детальный дизайн
- Потоки данных:
  1. При attach создать effective hotkeys = `options.Hotkeys` + saved user overrides.
  2. Session получает effective map.
  3. Overlay показывает legend из той же effective map.
  4. Пользователь открывает Settings, меняет строки, нажимает Apply/Save.
  5. Dialog валидирует parse + duplicates.
  6. Store пишет JSON в пользовательский `ApplicationData`.
  7. Overlay вызывает применение на session и обновляет legend.
- Контракты / API:
  - Не ломать `AppAutomationRecorderOptions.Hotkeys`.
  - Для тестируемости допустимы `internal` типы и `InternalsVisibleTo` уже используется тестовым проектом через assembly access, если он есть; иначе тестировать через public/internal существующие точки.
- Output contract / evidence rules:
  - Тесты должны подтвердить, что сохраненный override меняет command resolution и legend без пересоздания session.
  - Тесты должны подтвердить, что invalid/duplicate gestures не сохраняются.
- Visual planning artifact для UI-facing изменений:

```text
Recorder Overlay top row
+ Record + Clear + Save + Export... + Settings + Minimize + 0 steps + VALID + status...

Hotkey Settings dialog
+---------------------------------------------------------+
| Hotkeys                                           [x]    |
| Start/Stop recording      [ Ctrl+Shift+R          ]      |
| Save scenario             [ Ctrl+Shift+S          ]      |
| Export output             [ Ctrl+Shift+X          ]      |
| Clear steps               [ Ctrl+Shift+C          ]      |
| Assert auto               [ Ctrl+Shift+A          ]      |
| Assert text               [ Ctrl+Shift+T          ]      |
| Assert enabled            [ Ctrl+Shift+E          ]      |
| Assert checked            [ Ctrl+Shift+K          ]      |
| Minimize/restore overlay  [ Ctrl+Shift+M          ]      |
|                                                         |
| Validation/error text                                   |
|                              [Reset defaults] [Cancel] [Save] |
+---------------------------------------------------------+
```

- UI test video evidence для UI automation задач: fallback. В репозитории есть UI/headless тесты, но текущий TUnit/Avalonia headless workflow в обнаруженных тестах не показывает встроенный механизм записи видео. Next-best evidence: targeted tests, screenshots/manual smoke только если появится доступный runner.
- Границы сохранения поведения:
  - Если saved file отсутствует или поврежден, используются дефолты из options, recorder остается работоспособным.
  - Ошибка записи настроек показывается в dialog/status и не меняет active map.
- Обработка ошибок:
  - Невалидный gesture: не закрывать dialog, показать строку ошибки.
  - Дубликат gesture: не закрывать dialog, показать обе конфликтующие команды.
  - IO exception: показать ошибку и оставить текущую карту.
- Производительность:
  - JSON небольшой; запись допустима по Save action. Не выполнять периодический IO.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Effective hotkeys:
  - Base = `options.Hotkeys`.
  - User override заменяет только непустые поля.
  - Reset defaults очищает override и применяет `options.Hotkeys`.
- Gesture validation:
  - Пустое значение означает отключенную команду только если это уже поддержано `RecorderHotkeyMap` через `TryParse` false; UI должен явно позволять пустое значение как disabled или запретить пустые значения. Выбранный вариант: разрешить пустое значение как "disabled" для отдельной команды.
  - Непустые значения должны успешно парситься `RecorderShortcut.TryParse`.
  - Два одинаковых непустых сочетания в effective map запрещены.
- Путь хранения:
  - `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppAutomation", "Recorder", "hotkeys.json")`.
  - Директория создается при записи.

## 8. Точки интеграции и триггеры
- `AppAutomationRecorder.Attach` / `RecorderSession` constructor: загрузить effective settings.
- `RecorderOverlay.Attach`: показать legend из effective settings, подписаться на изменение settings/session.
- `SettingsButton.Click`: открыть settings dialog.
- `Save` в dialog: validate -> store write -> apply to session -> update legend/status.
- `Reset defaults`: удалить/очистить override -> apply default map -> update legend/status.

## 9. Изменения модели данных / состояния
- Новое persisted state: JSON-файл пользовательских overrides горячих клавиш.
- Calculated state: effective `RecorderHotkeyMap`.
- Runtime state: текущая session map и overlay legend.
- Формат JSON должен быть простым и tolerant к отсутствующим полям.

## 10. Миграция / Rollout / Rollback
- Первый запуск: файла нет, используются дефолты.
- Поврежденный JSON: игнорировать с диагностикой в status/dialog, не падать.
- Rollback: удалить `%APPDATA%/AppAutomation/Recorder/hotkeys.json` или нажать Reset defaults.
- Обратная совместимость: consumers, передающие `options.Hotkeys`, сохраняют тот же baseline; user overrides применяются поверх baseline.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Overlay содержит кнопку настроек hotkeys.
  - Dialog отображает все команды и текущие сочетания.
  - Save в dialog сохраняет JSON в пользовательский стандартный каталог или тестовый injected path.
  - После Save новая комбинация сразу срабатывает в `RecorderSession`.
  - `ShortcutText` сразу показывает новую комбинацию.
  - Дубликаты/невалидные сочетания не сохраняются и не применяются.
  - Reset defaults возвращает дефолтные сочетания и обновляет подсказку.
- Какие тесты добавить/изменить:
  - `RecorderHotkeyMap` duplicate/validation/effective legend tests.
  - `RecorderHotkeySettingsStore` read/write/missing/corrupt file tests через test path provider.
  - `RecorderSession` apply hotkeys test через internal testing hook, если возможно.
  - `RecorderOverlay` или settings dialog test для обновления legend, если headless UI test pattern позволяет без brittle selectors.
- Characterization tests / contract checks:
  - Существующий `HotkeyMap_UsesConfiguredGestures_AndBuildsLegend` оставить и расширить без смены semantics.
- Visual acceptance:
  - Сравнить итоговый UI со схемой из секции 6.2: кнопка `Settings` в верхнем ряду, dialog с таблицей command -> gesture и кнопками Save/Cancel/Reset.
- UI video evidence:
  - Fallback: runner video evidence не обнаружен в текущих тестах. Команда проверки: targeted TUnit tests + build/full test. Next-best evidence: тесты dialog/control state и при необходимости manual screenshot local-only.
- Базовые замеры до/после для performance tradeoff: Не применимо, изменение не performance-sensitive.
- Команды для проверки:
  - `dotnet run --project tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*Hotkey*"`
  - `dotnet build`
  - `dotnet test`
- Stop rules для test/retrieval/tool/validation loops:
  - Если targeted hotkey tests проходят, переходить к build.
  - Если build проходит, запускать full tests.
  - Если full tests невозможны по внешней причине, зафиксировать причину и выполнить максимально близкую проверку.

## 12. Риски и edge cases
- Конфликт user overrides с consumer-provided `options.Hotkeys`: mitigated by documented overlay precedence.
- Поврежденный JSON не должен ломать attach.
- Сочетания с разным порядком модификаторов (`Ctrl+Alt+E` vs `Alt+Ctrl+E`) должны считаться конфликтом после parse.
- Пустые commands могут убрать важную команду из legend; это допустимо как explicit disable, но dialog должен показывать понятное состояние.
- Settings dialog должен работать, когда overlay скрыт/минимизирован.

## 13. План выполнения
1. Добавить model/store/validation для hotkey settings.
2. Расширить hotkey map API без изменения существующего поведения.
3. Добавить runtime apply в session.
4. Добавить settings UI и кнопку в overlay.
5. Связать save/reset с store, session и legend refresh.
6. Добавить/обновить тесты.
7. Запустить targeted tests, build, full tests.
8. Выполнить post-EXEC review-loop и обновить журнал.

## 14. Открытые вопросы
Нет блокирующих вопросов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`; overlay: `ui-automation-testing`.
- Выполненные требования профиля:
  - UI-поток не блокируется длительными операциями; settings IO запускается только по user action.
  - Стабильные имена контролов в XAML будут сохранены/добавлены для тестируемости.
  - Планируются automated tests для behavior.
  - Video evidence: fallback указан из-за отсутствия обнаруженного recorder mechanism в текущем test suite.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Recorder.Avalonia/RecorderHotkeyMap.cs` | API для snapshot/legend/validation | Единая логика сочетаний |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` | при необходимости опции для settings store/test path | Интеграция persistence без ломки API |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | обновляемая hotkey map и apply hook | Мгновенное применение |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml` | кнопка Settings | Вход в настройки |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml.cs` | открытие dialog, refresh legend | UI flow |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderHotkeySettingsWindow.axaml(.cs)` | новое окно настроек | Назначение клавиш |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | новые tests | Regression coverage |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Назначение клавиш | Только через `options.Hotkeys` до attach | Через settings dialog + persisted overrides |
| Runtime apply | Только при создании session | Сразу после Save/Reset |
| Legend | Однократный текст при `Attach` | Обновляется при изменении hotkeys |
| Persistence | Нет | User ApplicationData JSON |
| Ошибки ввода | Не применимо | Inline validation в dialog |

## 18. Альтернативы и компромиссы
- Вариант: хранить настройки в working directory.
  - Плюсы: проще найти файл.
  - Минусы: не стандартное пользовательское место, зависит от запуска.
  - Почему не выбран: противоречит требованию хранить в стандартном для системы месте.
- Вариант: OS-level global hotkeys.
  - Плюсы: работают без фокуса окна.
  - Минусы: platform-specific, повышенный риск конфликтов и permissions, меняет существующий контракт.
  - Почему не выбран: задача просит настройки существующих глобальных клавиш; текущий recorder уже использует Avalonia key handling.
- Вариант: сохранять все effective hotkeys.
  - Плюсы: проще чтение.
  - Минусы: consumer default changes перестают влиять после первого сохранения.
  - Почему выбран override-only: лучше сохраняет обратную совместимость с `options.Hotkeys`.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals описаны |
| B. Качество дизайна | 6-10 | PASS | Ответственность, flow, persistence, ошибки и rollback указаны |
| C. Безопасность изменений | 11-13 | PASS | Данные, совместимость, rollback и этапы зафиксированы |
| D. Проверяемость | 14-16 | PASS | Acceptance, tests, команды и file table есть |
| E. Готовность к автономной реализации | 17-19 | PASS | Открытых блокеров нет, альтернативы и review заполнены |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation требования отражены, video fallback объяснен |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Требования и Non-Goals проверяемы |
| 2. Понимание текущего состояния | 5 | Указаны текущие классы и одноразовая hotkey map |
| 3. Конкретность целевого дизайна | 5 | Есть flow, UI wireframe, persistence и validation |
| 4. Безопасность (миграция, откат) | 5 | Первый запуск, corrupt JSON, reset/rollback описаны |
| 5. Тестируемость | 5 | Есть targeted/full команды и конкретные тесты |
| 6. Готовность к автономной реализации | 5 | Блокирующих вопросов нет, этапы и файлы определены |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-05-19-recorder-hotkey-settings.md`; instruction stack: `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`; selected profile: `dotnet-desktop-client`; open questions: нет; planned changed files перечислены в секции 16
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: просмотрены текущие `RecorderHotkeyMap.cs`, `RecorderSession.cs`, `RecorderOverlay.axaml(.cs)`, `AppAutomationRecorderOptions.cs`, `RecorderTests.cs`, test csproj и `git status --short`
  - Contract pass: спека покрывает пользовательское требование про окно настроек, пользовательское хранилище, мгновенное применение и обновление подсказок; Non-Goals ограничивают OS-level registration
  - Adversarial risk pass: проверены edge cases duplicate normalized shortcuts, corrupt JSON, reset defaults, overlay/session divergence и отсутствие video runner
  - Re-review after fixes / Fix and re-review: дополнительных исправлений после review не потребовалось
  - Stop decision: PASS, можно остановиться до фразы `Спеку подтверждаю`
- Evidence inspected: команды чтения файлов и `rg` по hotkey/overlay/session; `git status --short` показал постороннее удаление `AGENTS.md`
- Depth checklist:
  - Scope drift / unrelated changes: обнаружено unrelated `D AGENTS.md`, не входит в задачу
  - Acceptance criteria: все критерии из запроса представлены
  - Validation evidence: команды проверки определены, EXEC еще не выполнялся
  - Unsupported claims: claims основаны на просмотренных файлах
  - Regression / edge case: corrupt file, duplicates, empty gestures, precedence covered
  - Comments/docs/changelog: changelog не планируется до отдельного релизного требования
  - Hidden contract change: OS-level global hotkeys явно вынесены в Non-Goals
  - Manual-review challenge: наиболее вероятная находка - ambiguity слова "глобальные"; mitigated Non-Goal и сохранением текущей scoped semantics
- No-findings justification: спека содержит owner-documents, visual artifact, acceptance, tests, risks, rollback и не имеет блокирующих вопросов

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | wording | Термин "глобальные клавиши" может означать OS-level hotkeys, но текущий recorder использует window-scoped Avalonia KeyDown | Зафиксировать Non-Goal OS-level registration и scoped continuation | fixed |

- Fixed before continuing: добавлен явный Non-Goal про Win32/OS-level registration
- Checks rerun: SPEC linter/rubric повторно просмотрены после уточнения Non-Goals
- Needs human: требуется только подтверждение спеки фразой `Спеку подтверждаю`
- Residual risks / follow-ups: если нужен настоящий OS-level global hotkey вне фокуса окна, потребуется отдельная спека с platform-specific реализацией

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, `git diff --stat -- src tests specs`, relevant diff for recorder hotkey/session/overlay/settings/tests, targeted tests, build, full test rerun, `git diff --check`
- Decision: можно завершать
- Review passes:
  - Scope/Evidence pass: проверены изменённые recorder-файлы, новый settings store/window, tests, spec journal, status и diff; unrelated `D AGENTS.md` отделён от задачи
  - Contract pass: реализация соответствует acceptance criteria: settings button/dialog, persisted user ApplicationData JSON, duplicate/invalid validation, immediate session apply, legend refresh, reset defaults
  - Adversarial risk pass: проверены corrupt JSON fallback, validation-before-persist, normalized duplicate shortcuts, disabled empty gesture, overlay/session divergence и desktop smoke hotkey behavior
  - Re-review after fixes / Fix and re-review: после review исправлены validation-before-persist и status при corrupt persisted settings; повторены targeted hotkey tests, build и full tests
  - Stop decision: PASS после успешного полного `dotnet test --no-build`
- Evidence inspected:
  - `dotnet run --project tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*Hotkey*"`: PASS, 6/6
  - `dotnet build`: PASS
  - `dotnet test`: первый full run дал один flaky FlaUI smoke failure; упавший test затем прошёл targeted
  - `dotnet run --project sample\DotnetDebug.AppAutomation.FlaUI.Tests\DotnetDebug.AppAutomation.FlaUI.Tests.csproj -- --treenode-filter "/*/*/DotnetDebugRecorderDesktopSmokeTests/RecorderSmokeGridEditAndUserActionsSaveGridSteps"`: PASS, 1/1
  - `dotnet test --no-build`: PASS, 273/273
  - Повтор после review-fix: targeted hotkey PASS 6/6, `dotnet build` PASS, `dotnet test --no-build` PASS 273/273
  - `git diff --check`: PASS, только Git line-ending warnings
  - UX follow-up после вопроса пользователя:
    - `dotnet run --project tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*Hotkey*"`: PASS, 7/7
    - `dotnet build`: PASS
    - `dotnet test --no-build`: first rerun after edge-case fix failed 1 flaky FlaUI smoke (`RecorderSmokeGridEditAndUserActionsSaveGridSteps`); isolated rerun passed 1/1
    - `dotnet test --no-build`: final rerun PASS, 275/275
    - `git diff --check`: PASS, только Git line-ending warnings
  - Layout-independent hotkey follow-up:
    - `dotnet run --project tests\AppAutomation.Recorder.Avalonia.Tests\AppAutomation.Recorder.Avalonia.Tests.csproj -- --treenode-filter "/*/*/RecorderTests/*Hotkey*"`: PASS, 8/8
    - `dotnet build`: PASS
    - `git diff --check`: PASS, только Git line-ending warnings
    - `dotnet test --no-build`: sandbox run failed from NuGet SSL in FlaUI setup; escalated rerun reached tests, recorder tests PASS, full run failed 2 unrelated FlaUI scenarios (`ArmDesktop_PrimitivesWrappersAndSearch_Work`, `Hierarchy_SelectTreeItem_ShowsSelectionInResult`)
- Depth checklist:
  - Scope drift / unrelated changes: unrelated `D AGENTS.md` существует до задачи и не тронут
  - Acceptance criteria: covered by code and tests
  - Validation evidence: targeted, build, full tests and diff check recorded
  - Unsupported claims: no unsupported behavioral claims left
  - Regression / edge case: duplicate normalized shortcuts, corrupt JSON, empty disabled gestures and session apply covered
  - Comments/docs/changelog: new code uses self-explanatory names; changelog not required by spec
  - Hidden contract change: no public API break; OS-level hotkey registration remains Non-Goal
  - Manual-review challenge: likely concerns are user-file corruption visibility and invalid persist ordering; both fixed before final
- No-findings justification: final review after fixes found no remaining actionable defects in scoped diff

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | persistence | Overlay could persist an invalid result if a future caller bypassed dialog validation | Validate result again before store write | fixed |
| LOW | status | Corrupt persisted hotkey file fallback was silent | Surface fallback in recorder status while keeping defaults | fixed |

- Fixed before final report: validation-before-persist; corrupt persisted settings status; hotkey fields now capture routed tunnel `KeyDown` with `handledEventsToo`, use `PhysicalKey.ToQwertyKey()` for layout-independent Alt/letter/digit/cyrillic-layout capture, allow modified Backspace/Delete gestures, ignore `Key.None`, and use `Press shortcut` watermark instead of `Disabled`
- Checks rerun: targeted hotkey tests, `dotnet build`, isolated FlaUI smoke after one flaky full failure, `dotnet test --no-build`, `git diff --check`
- Validation evidence: latest targeted hotkey run PASS 8/8; latest build PASS; latest diff-check PASS; latest full `dotnet test --no-build` did not fully pass because of unrelated FlaUI smoke failures listed above
- Unrelated changes: `D AGENTS.md` remains unrelated and untouched
- Needs human: нет
- Residual risks / follow-ups: UI video evidence fallback remains because current suite does not provide safe video recording; full FlaUI smoke passed as next-best evidence

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор контекста | 0.9 | Нет | Создать рабочую спецификацию | Нет | Нет | Найдены текущие точки hotkey/session/overlay и тесты | `RecorderHotkeyMap.cs`, `RecorderSession.cs`, `RecorderOverlay.axaml(.cs)`, `RecorderTests.cs` |
| SPEC | Спецификация и quality gate | 0.9 | Подтверждение пользователя | Ожидать `Спеку подтверждаю` | Да | Да, запрашивается подтверждение | QUEST требует остановки до EXEC | `specs/2026-05-19-recorder-hotkey-settings.md` |
| EXEC | Реализация hotkey settings | 0.8 | Результат тестов | Запустить targeted hotkey tests | Нет | Пользователь подтвердил спеки фразой `спеку подтверждаю` | Добавлены settings model/store, обновляемая hotkey map/session, окно настроек и overlay-кнопка | `RecorderHotkeyMap.cs`, `RecorderHotkeySettings.cs`, `RecorderSession.cs`, `RecorderOverlay.axaml(.cs)`, `RecorderHotkeySettingsWindow.axaml(.cs)`, `RecorderTests.cs` |
| EXEC | Валидация и review | 0.95 | Нет | Финальный отчёт | Нет | Нет | Targeted/build/full tests прошли; post-EXEC review fixes применены и перепроверены | `specs/2026-05-19-recorder-hotkey-settings.md`, changed recorder/test files |
| EXEC | UX-доработка hotkey capture | 0.9 | Результат повторных тестов | Запустить targeted hotkey tests и build | Нет | Пользователь указал, что `Disabled` watermark и ручной ввод сочетаний не соответствуют ожиданию | Поля настроек теперь перехватывают `KeyDown`, нормализуют сочетания и очищаются через Backspace/Delete; watermark заменён на `Press shortcut` | `RecorderHotkeySettingsWindow.axaml.cs`, `RecorderTests.cs`, `specs/2026-05-19-recorder-hotkey-settings.md` |
| EXEC | Fix hotkey capture edge cases | 0.98 | Нет | Финальный отчёт | Нет | Пользователь попросил исправить review-findings | Capture переведён на routed tunnel handler с `handledEventsToo`; `Ctrl+Delete`/`Ctrl+Backspace` теперь назначаемы; `Key.None` игнорируется; targeted/build/full проверки прошли | `RecorderHotkeySettingsWindow.axaml.cs`, `RecorderTests.cs`, `specs/2026-05-19-recorder-hotkey-settings.md` |
| EXEC | Layout-independent hotkey capture | 0.95 | Нет | Финальный отчёт | Нет | Пользователь перечислил `Alt+Z`, одиночные буквы/цифры и кириллицу | Capture и runtime matching теперь используют `PhysicalKey.ToQwertyKey()`; цифры нормализуются как `1`, а не `D1`; targeted/build проверки прошли, full run заблокирован unrelated FlaUI failures | `RecorderHotkeyMap.cs`, `RecorderSession.cs`, `RecorderHotkeySettingsWindow.axaml.cs`, `RecorderTests.cs`, `specs/2026-05-19-recorder-hotkey-settings.md` |
