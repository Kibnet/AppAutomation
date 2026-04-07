# BRD: Доведение AppAutomation Avalonia Recorder до полезного паритета с исходным рекордером

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-feature-parity`
- Владелец: `AppAutomation maintainers`
- Масштаб: `large`
- Целевой релиз / ветка: `Unreleased` / `main`
- Ограничения:
  - сохраняем канонический output mode через `Authoring` partials; не возвращаемся к генерации отдельного runtime DSL
  - не добавляем tree-path, координатные и иные нестабильные селекторы в persisted test contract
  - не переносим в текущий recorder unsupported low-level gestures (`RightClick`, `DoubleClick`, `Hover`, `Scroll`, `KeyPress`) до появления честного `AppAutomation` abstraction contract
  - не добавляем `AssertVisible`, пока в `AppAutomation` нет согласованного visibility contract
  - при необходимости расширения public API выбираем additive путь без silent-breaking изменений существующих consumer-контрактов
- Связанные ссылки:
  - `C:\Projects\My\Agents\AGENTS.md`
  - `specs/2026-04-06-appautomation-avalonia-recorder.md`
  - `src/AppAutomation.Recorder.Avalonia/*`
  - `tests/AppAutomation.Recorder.Avalonia.Tests/*`
  - `D:\YandexDisk\Projects\ИЗП\Sources\Avalonia.TestRecorder`
- Instruction stack (quest):
  - `quest-governance`
  - `quest-mode`
  - `collaboration-baseline`
  - `testing-baseline`
  - `testing-dotnet`
  - `dotnet-desktop-client`
  - `ui-feature-parity`
  - `spec-linter`
  - `spec-rubric`
  - `review-loops`

## 1. Overview / Цель
В репозитории уже есть рабочий `AppAutomation.Recorder.Avalonia`, который записывает действия и генерирует канонические `Authoring` partial-файлы. Но после первичного внедрения сохранился заметный parity-gap с исходным `Avalonia.TestRecorder` в трёх зонах: доверие к записанным шагам, удобство отладки через overlay и расширяемость под реальные кастомные UI-контролы.

Цель этой follow-up спеки: забрать из исходного рекордера только те идеи, которые усиливают текущий `AppAutomation` workflow, не ломая его архитектурные решения. Итогом должен стать recorder, которому можно больше доверять при записи и проще адаптировать под живые приложения, а не только под sample.

## 2. Текущее состояние (AS-IS)
- В текущем `AppAutomation` recorder уже есть:
  - attach API и session lifecycle;
  - overlay с `Record`, `Clear`, `Save`, `Hide`;
  - capture поддерживаемых high-level действий;
  - генерация `Authoring` partial-файлов;
  - базовые regression tests на selector policy и save/codegen.
- В текущей реализации отсутствуют несколько сильных сторон исходного рекордера:
  - нет round-trip проверки, что выбранный селектор действительно находит тот же control owner;
  - шаги добавляются без отдельной validation phase и без явного статуса качества;
  - overlay после `Hide` фактически теряет явный restore UX и даёт мало диагностического контекста;
  - hotkeys зашиты в код и не настраиваются;
  - assertions захватываются только жёстко зашитой логикой;
  - не покрыт сценарий `ListBox` selection;
  - regression suite почти не покрывает overlay, hotkeys и validation logic.
- В исходном `Avalonia.TestRecorder` уже есть полезные идеи:
  - selector validation и step validation;
  - richer overlay с minimized mode, restore и status bar;
  - configurable hotkeys и callback-based save/minimize hooks;
  - assertion extractor pipeline;
  - `ListBox` selection capture;
  - отдельные tests на keyboard save, overlay minimize/restore, status bar и validation.
- При этом часть фич исходника не подходит текущему репозиторию как есть:
  - tree-path fallback противоречит `AppAutomation` selector policy;
  - codegen под `xUnit/NUnit` runtime DSL обходит `Authoring` слой;
  - шаги `AssertVisible`, `RightClick`, `DoubleClick`, `Hover`, `Scroll`, `KeyPress` не соответствуют текущему abstraction contract.

## 3. Проблема
Корневая проблема: текущий recorder уже умеет быстро генерировать канонический тестовый код, но пока недостаточно надёжен и неудобен для отладки, из-за чего maintainer-ы всё ещё вынуждены вручную проверять корректность селекторов, бороться с урезанным overlay UX и дорабатывать recorder под кастомные контроли точечными хардкодами.

## 4. Цели дизайна
- Повысить доверие к записанным шагам через явную validation phase без отхода от стабильного selector contract.
- Выравнять overlay UX с наиболее полезной частью исходного рекордера: restore, minimized mode, richer status, hotkey discoverability.
- Убрать необходимость в жёстких частных хардкодах для assertion capture и custom controls там, где достаточно extensibility points.
- Сохранить backward compatibility текущего `Authoring`-based workflow и existing runtime wrappers.
- Декомпозировать parity work по приоритетам, чтобы сначала закрыть trust/diagnostics gap, а public API расширять только там, где это действительно нужно.

## 5. Non-Goals (чего НЕ делаем)
- Не переносим tree-path fallback и не сохраняем нестабильные селекторы "лишь бы записалось".
- Не переписываем recorder обратно в генератор самостоятельных `Headless`/`FlaUI` тестов.
- Не реализуем pause/resume state, если он не даёт отдельной практической ценности сверх существующего `Off/Recording`.
- Не добавляем неподдерживаемые low-level interaction steps ради номинального parity.
- Не расширяем overlay до полноценного IDE/inspector-инструмента с visual tree browser в рамках этой итерации.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/AppAutomation.Recorder.Avalonia/RecorderSelectorResolver.cs`
  - стабилизация селектора;
  - round-trip validation against live visual tree;
  - формирование диагностик по ambiguous/invalid locator cases.
- `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`
  - orchestration capture/validation flow;
  - применение configurable hotkeys;
  - сохранение enriched step/validation status для overlay preview.
- `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`
  - hardcoded built-ins для базовых controls;
  - plug-in pipeline для assertion extractors;
  - явное различение supported/unsupported capture cases.
- `src/AppAutomation.Recorder.Avalonia/UI/*`
  - minimized/restore overlay UX;
  - richer status presentation;
  - help/shortcut legend;
  - save/export affordances, адаптированные под dual-file `Authoring` save model.
- `src/AppAutomation.Abstractions/*`, `src/AppAutomation.Avalonia.Headless/*`, `src/AppAutomation.FlaUI/*`
  - additive capability contract для interactive list selection, если включаем `ListBox` recording;
  - runtime support без ломки существующих page property types.
- `tests/AppAutomation.Recorder.Avalonia.Tests/*`, `tests/AppAutomation.Abstractions.Tests/*`
  - parity regression suite на validation, overlay и new capture capability.

### 6.2 Детальный дизайн
#### 6.2.1 Карта parity-gap'ов и приоритетов
| Приоритет | Gap | Текущее состояние | Что забираем из исходника | Адаптация под AppAutomation |
| --- | --- | --- | --- | --- |
| `P0` | Selector/step validation | шаги пишутся сразу после resolution | selector validation + step validation | валидируем selector equivalence и action applicability, но не replay'им шаги повторно в live AUT |
| `P0` | Overlay trust/diagnostics UX | `Hide` без явного restore UX, мало статуса | minimized mode, restore, richer status | overlay остаётся лёгким и focused на canonical save/preview |
| `P1` | Configurability | hotkeys и UI callbacks зашиты | configurable hotkeys, callback model | вводим recorder options для команд, не меняя existing default flow |
| `P1` | Assertion extensibility | hardcoded logic only | assertion extractor pipeline | built-ins остаются, кастомизация приходит через additive extension point |
| `P2` | List selection capture | `ListBox` не пишется | `ListBox` selection recording | нужен additive capability contract, не прямое изменение существующего property type |
| `P2` | Regression coverage parity | узкий suite | keyboard/overlay/status/validation tests | тесты адаптируются под current dual-file save model и selector policy |

#### 6.2.2 Selector validation вместо "первого подошедшего" селектора
- После первичного resolution recorder выполняет validation round-trip:
  - по candidate selector ищется control через live visual tree resolver;
  - найденный control приводится к тому же semantic owner, который был выбран при capture;
  - если найден другой control или несколько equivalence-кандидатов, шаг помечается как validation failed.
- Validation model должна отличаться от исходника:
  - мы не исполняем action повторно на живом приложении ради проверки;
  - мы не подмешиваем tree-path fallback;
  - мы валидируем две вещи:
    - locator equivalence;
    - применимость action/assertion к control type.
- Результат validation сохраняется в session state как structured status:
  - `Valid`
  - `Warning`
  - `Invalid`
- Для `Warning` examples:
  - `Name` locator разрешён, но хрупок;
  - control найден, но locator quality ниже preferred policy.
- Для `Invalid` examples:
  - locator ambiguous;
  - resolved control не совпадает с captured owner;
  - для control type нет честного AppAutomation mapping.

#### 6.2.3 Enriched recorded-step metadata
- Внутренняя модель recorded step получает поля уровня diagnostics:
  - `ValidationStatus`
  - `ValidationMessage`
  - `PreviewText`
  - `CanPersist`
- Это runtime-only metadata; persisted C# output не меняется по структуре.
- `CanPersist = false` означает:
  - шаг может быть показан пользователю в preview;
  - но не должен молча попадать в generated partials.
- Save result агрегирует:
  - `persisted step count`
  - `skipped step count`
  - список skipped diagnostics с привязкой к шагу.

#### 6.2.4 Overlay v2: restore, minimized mode, richer feedback
- Overlay переводится из модели "окно пропало" в модель двух состояний:
  - `Expanded`
  - `Minimized`
- `Minimized` state должен:
  - оставаться видимым;
  - показывать краткий статус (`Recording`, `Last warning`, `Saved to ...`);
  - иметь явную кнопку `Restore`.
- `Expanded` state должен показывать:
  - step counter;
  - last preview;
  - validation badge/text;
  - shortcut legend/help;
  - кнопки `Record/Stop`, `Clear`, `Save`, `Export...`, `Minimize`.
- `Save` сохраняет в configured canonical authoring target.
- `Export...` открывает folder picker и использует `SaveToDirectoryAsync(...)`, чтобы адаптировать идею "save via dialog" к dual-file output model.
- Overlay не должен рекламировать неподдерживаемые assertions или gestures.

#### 6.2.5 Configurable hotkeys и callback model
- В `AppAutomationRecorderOptions` добавляется структура наподобие:
  - `RecorderHotkeys`
  - `OverlayOptions`
  - `ValidationOptions`
- Hotkeys конфигурируются, но по умолчанию сохраняют текущую семантику:
  - `StartStop`
  - `Save`
  - `Clear`
  - `CaptureAssertAuto`
  - `CaptureAssertText`
  - `CaptureAssertEnabled`
  - `CaptureAssertChecked`
  - `ToggleOverlayMinimize`
  - опционально `Export`
- Отдельный `PauseResume` не добавляется, пока нет реального отдельного state machine и UX выигрыша.
- Session/overlay interaction переводится на callbacks/events:
  - `SaveRequested`
  - `ExportRequested`
  - `MinimizeRequested`
  - `RestoreRequested`
- Это упрощает unit-тестирование overlay и убирает жёсткую привязку UI к внутренней save логике.

#### 6.2.6 Assertion extractor pipeline
- Вместо полного hardcode-only подхода вводится additive extension point:
  - `IRecorderAssertionExtractor`
- Built-in extractors закрывают текущие типовые controls:
  - text-bearing controls;
  - checked/toggled controls;
  - enabled-state capture.
- Порядок работы:
  - сначала built-ins со stable mappings;
  - затем user-provided extractors из options;
  - при отсутствии match возвращается явный unsupported diagnostic.
- `ControlHints` сохраняются для дешёвых declarative mappings вроде spinner-like input.
- Важное ограничение:
  - extractor pipeline решает, как получить assertion value и suitable action kind;
  - он не имеет права обходить selector policy или генерировать несуществующий `AppAutomation` DSL.

#### 6.2.7 ListBox recording через additive capability contract
- `ListBox` selection стоит переносить, но не через ломающую правку `IListBoxControl`.
- Доминирующий вариант:
  - добавить новый additive capability interface наподобие `ISelectableListBoxControl`;
  - runtime-реализации в `Headless` и `FlaUI` начинают его поддерживать;
  - recorder-generated scenario использует новый extension method уровня `UiPageExtensions.SelectListBoxItem(...)`, который работает с `IListBoxControl`, но требует runtime capability check под капотом.
- Это даёт:
  - сохранение существующего property type на page objects;
  - отсутствие compile break для consumer-кода, который зависит только от `IListBoxControl`;
  - честный runtime error, если capability недоступна.
- Recorder начинает подписываться на `ListBox.SelectionChanged` только после появления этого capability contract в обоих runtime-ах.

#### 6.2.8 Тестовая матрица parity
- Recorder test suite должен получить отдельные блоки:
  - selector validation success/failure;
  - name-fallback warning path;
  - overlay minimize/restore state transitions;
  - save/export command routing;
  - hotkey dispatch;
  - assertion extractor precedence;
  - skipped invalid steps on save.
- `Abstractions` / runtime tests должны покрыть:
  - additive listbox selection capability;
  - new `UiPageExtensions.SelectListBoxItem(...)`;
  - capability missing path;
  - headless/flaUI runtime implementations.
- Tree-path-specific tests из исходника не переносятся.

#### 6.2.9 Документация и consumer guidance
- README/workflow docs должны объяснить:
  - разницу между `Save` и `Export...`;
  - что invalid шаги могут остаться только в preview и не попасть в output;
  - как регистрировать custom assertion extractor;
  - почему tree-path и `AssertVisible` по-прежнему вне контракта.

## 7. Бизнес-правила / Алгоритмы
- `FR-1`: шаг может быть persisted только если `ValidationStatus != Invalid` и `CanPersist = true`.
- `FR-2`: `AutomationId` остаётся preferred locator; `Name` допустим только при explicit opt-in и всегда маркируется warning-ом.
- `FR-3`: recorder не добавляет fallback selector другого класса, если stable locator невалиден.
- `FR-4`: overlay обязан показывать причину skip/invalid, а не просто уменьшать число сохранённых шагов без объяснения.
- `FR-5`: `Save` пишет в canonical authoring target; `Export...` пишет в выбранную пользователем директорию без изменения canonical target configuration.
- `FR-6`: user-provided assertion extractor не может вернуть шаг, которого нет в `RecordedActionKind` или который не умеет генерировать current codegen.
- `FR-7`: `ListBox` recording включается только после того, как обе runtime-реализации поддерживают additive selection capability.
- `FR-8`: parity work не должен ухудшить существующий flow записи для уже поддерживаемых controls.

## 8. Точки интеграции и триггеры
- `RecorderSession.RegisterRecordedStep(...)`
  - вместо прямого `_steps.Add(...)` должен запускать validation pipeline и обновлять session diagnostics.
- `RecorderSelectorResolver.Resolve(...)`
  - после resolution вызывает validator и возвращает enriched result.
- `RecorderOverlay`
  - реагирует на session status changes;
  - маршрутизирует `Save`, `Export`, `Minimize`, `Restore`.
- `AppAutomationRecorder.Attach(...)`
  - создаёт overlay window/state coordination;
  - поддерживает toggle minimized/expanded по hotkey/event.
- `RecorderStepFactory.TryCreateAssertionStep(...)`
  - использует extractor pipeline.
- `RecorderSession.SubscribeControlHandlers(...)`
  - получает `ListBox` hook только после ввода capability contract.
- `UiPageExtensions`
  - получает additive `SelectListBoxItem(...)` helper при включении `P2`.

## 9. Изменения модели данных / состояния
- Runtime state:
  - enriched session diagnostics;
  - overlay presentation state (`Expanded` / `Minimized`);
  - effective hotkey map;
  - optional last export directory.
- Persisted state:
  - generated C# output остаётся прежнего класса: page partial + scenario partial;
  - optional config/metadata sidecar не требуется и не является частью design.
- Public surface:
  - `AppAutomationRecorderOptions` расширяется additive options-группами;
  - возможен новый public capability interface для interactive listbox selection;
  - существующие page property types и canonical generated attributes не меняются.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - `P0`: validation + overlay diagnostics + save/export UX + tests;
  - `P1`: hotkey configurability + assertion extractor pipeline + docs;
  - `P2`: additive listbox capability across abstractions/runtimes + recording/tests.
- Совместимость:
  - текущий default recorder workflow остаётся рабочим без дополнительной конфигурации;
  - existing generated partials не требуют миграции;
  - consumer apps без custom options продолжают использовать defaults.
- Rollback:
  - `P0/P1` могут быть откатены локально в recorder package и docs;
  - `P2` затрагивает public abstraction surface, поэтому должен быть изолирован в отдельном коммите/этапе для безопасного rollback.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - при записи шага recorder показывает, прошёл ли selector/action validation, и не сохраняет invalid шаг молча;
  - overlay можно свернуть и явно восстановить без потери статуса сессии;
  - `Save` и `Export...` различаются по поведению и покрыты тестами;
  - hotkeys можно переназначить через options без изменения code path session logic;
  - custom assertion extractor можно зарегистрировать без правок ядра recorder-а;
  - `ListBox` selection начинает записываться только после появления additive runtime capability и соответствующего DSL helper;
  - tree-path fallback по-прежнему не появляется в generated output;
  - regression suite покрывает validation, overlay, hotkeys, extractor pipeline и listbox capability.
- Какие тесты добавить/изменить:
  - `tests/AppAutomation.Recorder.Avalonia.Tests`
    - selector validation success
    - selector validation mismatch
    - invalid step skipped on save
    - overlay minimize/restore
    - save/export command routing
    - configurable hotkeys
    - assertion extractor precedence
  - `tests/AppAutomation.Abstractions.Tests`
    - `SelectListBoxItem(...)` happy path
    - `SelectListBoxItem(...)` missing capability failure
  - runtime-level tests:
    - `Headless` listbox capability implementation
    - `FlaUI` listbox capability implementation
  - full solution regression:
    - current recorder save/codegen tests stay green
- Команды для проверки:
  - `dotnet build AppAutomation.sln -c Debug`
  - `dotnet test tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -c Debug`
  - `dotnet test tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj -c Debug`
  - `dotnet test AppAutomation.sln -c Debug`

## 12. Риски и edge cases
- Validation, завязанная на live visual tree, может ложно ругаться на templated children, если semantic owner resolution будет недостаточно аккуратным.
- Overlay UX легко перегрузить диагностикой; нужно сохранить короткий, читаемый signal, а не превращать его в log viewer.
- Configurable hotkeys могут конфликтовать с hotkeys AUT; нужна явная валидация/документация defaults и конфликтов.
- `ListBox` capability затрагивает public abstractions и требует особенно аккуратного additive дизайна.
- User-provided extractors могут возвращать внутренне противоречивые результаты; нужно валидировать их output тем же pipeline, что и built-ins.

## 13. План выполнения
1. Реализовать `P0`: selector/action validation, session diagnostics, invalid-step skip policy.
2. Доработать overlay до `Expanded/Minimized` модели и развести `Save` / `Export...`.
3. Ввести configurable hotkeys и callback-based overlay/session coordination.
4. Добавить assertion extractor pipeline и документацию по кастомизации.
5. Спроектировать и внедрить additive listbox capability contract в abstractions/runtimes.
6. Подключить `ListBox` recording после готовности capability и покрыть tests.
7. Добить parity regression suite и обновить README/workflow docs.

## 14. Открытые вопросы
- Нет блокирующих открытых вопросов.
- Осознанно не включены в scope:
  - `AssertVisible`
  - tree-path fallback
  - low-level gesture parity
- Доминирующее решение для `ListBox` parity выбрано заранее:
  - additive capability interface лучше, чем изменение существующего `IListBoxControl`, потому что не ломает существующий consumer surface.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-feature-parity`
- Выполненные требования профиля:
  - составлен список parity-gap-ов и их приоритетов (`P0/P1/P2`);
  - описана целевая UI-структура recorder overlay и поведенческие сценарии `Save/Export/Minimize/Restore`;
  - зафиксированы guards и условия доступа для `Name` locators, invalid steps и `ListBox` capture;
  - определён пошаговый план parity work;
  - сохранён акцент на стабильные selectors и desktop UI-thread constraints.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs` | новые option groups для hotkeys/validation/overlay | configurable behaviour без code edits |
| `src/AppAutomation.Recorder.Avalonia/RecorderSelectorResolver.cs` | round-trip selector validation | повысить trust к записанным шагам |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | validation orchestration, diagnostics, save/export routing, hotkeys | основной parity uplift |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs` | assertion extractor pipeline | extensibility вместо hardcode-only логики |
| `src/AppAutomation.Recorder.Avalonia/UI/RecorderOverlay.axaml*` | minimized/restore UX, richer status, help/commands | parity с полезной частью исходного overlay |
| `src/AppAutomation.Abstractions/*` | additive listbox capability + DSL helper | поддержка `ListBox` recording без breaking change |
| `src/AppAutomation.Avalonia.Headless/*` | runtime implementation listbox capability | честное исполнение записанных шагов |
| `src/AppAutomation.FlaUI/*` | runtime implementation listbox capability | честное исполнение записанных шагов |
| `tests/AppAutomation.Recorder.Avalonia.Tests/*` | parity regression suite | защитить validation/overlay/hotkey behaviour |
| `tests/AppAutomation.Abstractions.Tests/*` | DSL and capability tests | проверить additive public contract |
| `README.md` | workflow docs по validation/export/custom extractors | снизить порог входа и зафиксировать ограничения |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Доверие к записанному шагу | selector берётся best-effort и сразу добавляется | шаг проходит validation и получает явный статус |
| Overlay после hide | окно просто исчезает | есть minimized mode и явный restore |
| Save workflow | только canonical save | canonical `Save` + `Export...` в выбранную директорию |
| Hotkeys | жёстко зашиты | configurable через options |
| Assertion capture | только built-in hardcode | built-ins + custom extractor pipeline |
| ListBox selection | не записывается | записывается после additive capability contract |
| Regression coverage | в основном codegen/merge | validation + overlay + hotkeys + extensibility + list capability |

## 18. Альтернативы и компромиссы
- Вариант: перенести весь исходный recorder почти без адаптации.
  - Плюсы:
    - быстрее получить формальный parity checklist.
  - Минусы:
    - возврат к чужому DSL;
    - tree-path и unsupported gestures снова попадут в design pressure;
    - конфликт с текущим `Authoring`-first подходом.
  - Почему выбранное решение лучше в контексте этой задачи:
    - переносит только ценные идеи, не разрушая уже принятое архитектурное направление.
- Вариант: оставить текущий recorder как есть и закрывать gaps точечными хардкодами.
  - Плюсы:
    - минимальный объём ближайших изменений.
  - Минусы:
    - ambiguity и UX gaps продолжат копиться;
    - каждая новая кастомизация снова будет ad-hoc.
  - Почему выбранное решение лучше в контексте этой задачи:
    - даёт системные extension points и диагностику вместо наращивания случайных исключений.
- Вариант: добавить `ListBox` selection прямой правкой `IListBoxControl`.
  - Плюсы:
    - проще реализация.
  - Минусы:
    - ломает возможных внешних implementer-ов public interface.
  - Почему выбранное решение лучше в контексте этой задачи:
    - additive capability interface сохраняет совместимость и даёт тот же функциональный результат.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и жёсткие границы follow-up scope описаны |
| B. Качество дизайна | 6-10 | PASS | Ответственности, parity-priorities, validation, overlay UX, API expansion и rollout определены |
| C. Безопасность изменений | 11-13 | PASS | Rollout/rollback и ограничения по public API и selector policy зафиксированы |
| D. Проверяемость | 14-16 | PASS | Acceptance, тест-план и проверочные команды заданы |
| E. Готовность к автономной реализации | 17-19 | PASS | Есть приоритетный план, блокирующих вопросов нет, масштаб указан |
| F. Соответствие профилю | 20 | PASS | Выполнены требования `dotnet-desktop-client` и `ui-feature-parity` |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Чётко отделено, что переносим, а что осознанно оставляем вне scope |
| 2. Понимание текущего состояния | 5 | AS-IS описывает и текущий recorder, и ценную часть исходного parity baseline |
| 3. Конкретность целевого дизайна | 5 | Для validation, overlay, hotkeys, extractors и listbox capability выбран конкретный design path |
| 4. Безопасность (миграция, откат) | 5 | Rollout разбит по приоритетам, public API risk локализован в `P2` |
| 5. Тестируемость | 5 | Acceptance criteria и test matrix покрывают все существенные parity-улучшения |
| 6. Готовность к автономной реализации | 5 | План пошаговый, архитектурно доминирующие решения выбраны заранее |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - вынесены parity-gap-ы в явные приоритеты `P0/P1/P2`, чтобы не смешивать trust fixes с API-расширением;
  - сохранено архитектурное расхождение с исходником по tree-path и unsupported gestures;
  - для `ListBox` зафиксирован additive capability path вместо прямой правки существующего интерфейса.
- Что осталось на решение пользователя:
  - ничего блокирующего; пользовательское подтверждение требуется только как переход из `SPEC` в `EXEC`.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `SPEC` | `delivery-task` / parity-follow-up для recorder-а | `0.90` | Нужно было понять точные разрывы между текущей реализацией и исходным рекордером | Сформировать список worth-taking improvements и отфильтровать неподходящие идеи | `Нет` | `Нет` | Прямой перенос исходника был бы архитектурно неверен, поэтому сначала отобраны только совместимые parity-gap'ы | `src/AppAutomation.Recorder.Avalonia/*`, `D:\YandexDisk\Projects\ИЗП\Sources\Avalonia.TestRecorder\*` |
| `SPEC` | Маршрутизация `QUEST` и выбор профиля | `0.94` | Нужно было подтвердить canonical template и profile contract для parity-задачи | Собрать spec по шаблону и зафиксировать instruction stack | `Нет` | `Нет` | Для follow-up задачи выбран профиль `dotnet-desktop-client + ui-feature-parity`, потому что user запросил именно parity-анализ и план внедрения | `C:\Projects\My\Agents\instructions\*`, `C:\Projects\My\Agents\templates\specs\_template.md` |
| `SPEC` | Подготовка рабочей спеки и quality gate | `0.96` | Блокирующих неизвестных не осталось | Запросить подтверждение спеки | `Да` | `Да, ожидается фраза "Спеку подтверждаю"` | По правилам `QUEST` дальнейшая реализация возможна только после явного перехода пользователя в `EXEC` | `specs/2026-04-07-appautomation-recorder-parity-followup.md` |
| `EXEC` | `P0/P1` parity uplift recorder-а | `0.91` | Нужно было подтвердить, что validation и overlay UX можно встроить без слома current authoring flow | Довести recorder package до компилируемого состояния и закрыть regression tests на validation/hotkeys/overlay/save skip policy | `Нет` | `Да, пользователь перевёл задачу в EXEC фразой "Спеку подтверждаю"` | Сначала закрыт trust gap: selector/action validation, enriched step metadata, save/export routing, minimized overlay и configurable hotkeys дают наибольший практический выигрыш | `src/AppAutomation.Recorder.Avalonia/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/*` |
| `EXEC` | `P2` additive listbox capability | `0.93` | Нужно было проверить, что новый selection contract можно ввести без breaking change для existing page properties | Добавить `ISelectableListBoxControl`, `SelectListBoxItem(...)`, runtime implementations и smoke-level runtime tests | `Нет` | `Нет` | Выбран additive capability path вместо прямой правки `IListBoxControl`, чтобы сохранить совместимость и при этом включить честный `ListBox` recording | `src/AppAutomation.Abstractions/*`, `src/AppAutomation.Avalonia.Headless/*`, `src/AppAutomation.FlaUI/*`, `sample/DotnetDebug.AppAutomation.*.Tests/*` |
| `EXEC` | Финальная верификация и документация | `0.98` | Существенных неизвестных не осталось после зелёных targeted runs | Закрыть README, прогнать full solution build/test и сделать post-EXEC review | `Нет` | `Нет` | После таргетированных прогонов выполнен полный regression sweep; блокирующих замечаний post-EXEC review не осталось | `README.md`, `tests/AppAutomation.Abstractions.Tests/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/*`, `sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/*`, `sample/DotnetDebug.AppAutomation.FlaUI.Tests/*`, `AppAutomation.sln` |
