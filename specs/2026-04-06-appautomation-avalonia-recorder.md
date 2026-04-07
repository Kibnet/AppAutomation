# BRD: Внедрение Avalonia Recorder для генерации AppAutomation-сценариев

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: `AppAutomation maintainers`
- Масштаб: `large`
- Целевой релиз / ветка: `Unreleased` / `main`
- Ограничения:
  - без прямого копирования DSL и API из `Avalonia.TestRecorder`; переносим поведение, но адаптируем под архитектуру `AppAutomation`
  - без breaking changes в существующих runtime-пакетах и текущем consumer flow `Authoring -> Headless -> FlaUI -> TestHost`
  - v1 записывает только те действия и проверки, которые могут быть честно выражены текущим `AppAutomation` DSL или его небольшим расширением
  - на фазе v1 не добавляем позиционные/tree-path селекторы как сохраняемый контракт автотестов
- Связанные ссылки:
  - `C:\Projects\My\Agents\AGENTS.md`
  - `README.md`
  - `sample/DotnetDebug.Avalonia`
  - `sample/DotnetDebug.AppAutomation.Authoring`
  - `D:\YandexDisk\Projects\ИЗП\Sources\Avalonia.TestRecorder`
- Instruction stack (quest):
  - `quest-governance`
  - `quest-mode`
  - `collaboration-baseline`
  - `testing-baseline`
  - `testing-dotnet`
  - `dotnet-desktop-client`
  - `ui-automation-testing`
  - `spec-linter`
  - `spec-rubric`
  - `review-loops`

## 1. Overview / Цель
### 1.1 Бизнес-контекст (BRD)
Сейчас вход в `AppAutomation` для UI-тестов остаётся ручным: нужно отдельно описывать `[UiControl(...)]`, вручную писать методы сценариев и многократно прогонять `Headless`/`FlaUI`, чтобы понять, где именно расходятся локаторы, поведение DSL или runtime-адаптеры.

### 1.2 Стейкхолдеры
- Maintainers `AppAutomation`
- Команды-потребители `Avalonia` desktop AUT
- Разработчики, использующие sample `DotnetDebug` для отладки framework-контрактов

### 1.3 Бизнес-цель
Добавить в репозиторий собственный рекордер для `Avalonia`, который:
- записывает пользовательские действия поверх живого окна AUT;
- генерирует код не для стороннего headless DSL, а для канонического `AppAutomation` authoring-слоя;
- сокращает стоимость первых smoke/regression сценариев;
- ускоряет локальную отладку проблем в селекторах, DSL и runtime-резолверах.

## 2. Текущее состояние (AS-IS)
- В текущем `AppAutomation` уже есть:
  - каноническая consumer topology: `Authoring`, `Headless`, `FlaUI`, `TestHost`;
  - source generator для `[UiControl(...)]`;
  - runtime-резолверы `Headless` и `FlaUI`;
  - fluent DSL в `src/AppAutomation.Abstractions/UiPageExtensions.cs`;
  - sample-реализация `DotnetDebug`, на которой можно проверять framework end-to-end.
- В текущем репозитории нет интерактивного способа:
  - записать действия пользователя поверх `Avalonia`-окна;
  - автоматически получить partial-файлы для `Authoring`;
  - быстро проверить, что локатор и тип контрола корректно отображаются в `AppAutomation`.
- Во внешнем `Avalonia.TestRecorder` уже реализованы полезные подсистемы:
  - overlay с управлением записью;
  - захват кликов, текста, отдельных hotkey-based assertions;
  - selector resolution с приоритетом `AutomationId`;
  - codegen и preview статуса.
- Но внешний рекордер генерирует код под `Avalonia.HeadlessTestKit.Ui`, а не под `AppAutomation`. Это делает прямое переиспользование архитектурно неверным:
  - не используется `UiPage`/`[UiControl]`;
  - не переиспользуются existing runtime wrappers `Headless` и `FlaUI`;
  - допускаются fallback-стратегии (например, tree-path), которые противоречат текущему selector contract `AppAutomation`.

## 3. Проблема
Корневая проблема: `AppAutomation` уже умеет исполнять UI-сценарии через два runtime-а, но не умеет быстро производить эти сценарии из реального пользовательского поведения, из-за чего стоимость старта и диагностики остаётся слишком высокой.

## 4. Цели дизайна
- Сгенерировать артефакты, которые сразу ложатся в каноническую `Authoring`-модель, а не обходят её.
- Жёстко приоритизировать стабильные селекторы (`AutomationId`, при явном opt-in `Name`), не сохраняя ложный "рабочий" контракт через дерево/координаты.
- Сохранять совместимость с текущими runtime wrapper-ами, чтобы записанные сценарии автоматически запускались и в `Headless`, и в `FlaUI`.
- Давать полезную диагностику, когда контрол не может быть честно записан в текущий DSL/framework contract.
- Минимизировать ручной merge: recorder должен уметь добавлять только недостающие controls/test methods в уже существующие partial-классы.
- Не блокировать UI-поток долгими синхронными операциями при сохранении артефактов.

## 5. Non-Goals (чего НЕ делаем)
- Не внедряем универсальный рекордер для `WPF`/`WinUI`; scope ограничен `Avalonia`.
- Не добавляем raw record/replay для `RightClick`, `DoubleClick`, `Hover`, `Scroll`, координатных кликов и tree-path navigation в v1.
- Не пытаемся автоматически выводить сложные composite abstractions вроде `ISearchPickerControl`; для v1 рекордер работает с примитивами и поддерживаемыми framework control types.
- Не перепроектируем `AppAutomation.Tooling` CLI в полноценный orchestrator записи.
- Не требуем от consumer template автоматической интеграции recorder-а в AUT; интеграция остаётся явной opt-in в приложении.
- Не расширяем контракт `IUiControl` полем `IsVisible`; значит, честный `AssertVisible` остаётся вне scope v1.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/AppAutomation.Recorder.Avalonia/*`:
  - session lifecycle;
  - overlay UI;
  - event capture;
  - selector stabilization;
  - AppAutomation-oriented code generation;
  - сохранение generated artifacts.
- `src/AppAutomation.Abstractions/*`:
  - только минимальные additive-расширения DSL, нужные для честного assertion capture recorder-а.
- `sample/DotnetDebug.Avalonia/*`:
  - debug-only reference integration recorder-а в sample AUT.
- `sample/DotnetDebug.AppAutomation.Authoring/*` и template content:
  - приведение authoring base-классов к `partial`, чтобы recorder мог безопасно добавлять test methods в partial file.
- `tests/AppAutomation.Recorder.Avalonia.Tests/*`:
  - regression suite на capture, selector policy и codegen.
- `README.md` и consumer docs:
  - инструкция по opt-in интеграции recorder-а и его ограничениям.

### 6.2 Детальный дизайн
#### 6.2.1 Новый пакет и публичный API
- Добавить новый пакет `AppAutomation.Recorder.Avalonia`.
- Публичная точка входа: статический attach API уровня `AppAutomationRecorder.Attach(Window, AppAutomationRecorderOptions?)`.
- Attach возвращает session interface уровня `IAppAutomationRecorderSession` c операциями:
  - `Start()`
  - `Stop()`
  - `Clear()`
  - `ExportPreview()`
  - `SaveAsync()` / `SaveToDirectoryAsync(...)`
- API additive и не меняет существующие public surface area других пакетов.

#### 6.2.2 Целевой формат генерации: canonical partials
- Recorder генерирует не runtime-specific тесты, а partial-артефакты для уже существующего `Authoring`-проекта.
- Базовый output mode v1: `CanonicalPartials`.
- В этом режиме сохраняются два C#-файла:
  - partial для page class c `[UiControl(...)]`, содержащий только новые/ещё не объявленные контролы;
  - partial для scenario base class c новым `[Test]`-методом, использующим `UiPageExtensions`.
- Ключевой выбор дизайна:
  - page class уже `partial`, поэтому наращивание `[UiControl(...)]` естественно вписывается в source-generator contract;
  - scenario base class в sample/template должен стать `partial`, чтобы новый test method автоматически подхватывался существующими `Headless`/`FlaUI` wrappers.
- За счёт этого записанный сценарий начинает выполняться в обоих runtime-ах без генерации отдельных wrapper-файлов.

#### 6.2.3 Discovery и merge с существующим authoring-кодом
- Recorder options должны уметь указывать:
  - директорию authoring-проекта;
  - namespace и имя page class;
  - namespace и имя scenario base class.
- Если опции не заданы явно, используются canonical defaults:
  - root namespace строится от entry assembly name;
  - page class = `<WindowTypeName>Page`;
  - scenario base = `<WindowTypeName>ScenariosBase`;
  - authoring root = `<AppName>.UiTests.Authoring`.
- На `SaveAsync()` recorder сканирует существующие source files целевого authoring-проекта и извлекает:
  - уже объявленные `[UiControl(...)]`;
  - существование/partial-статус target classes;
  - уже занятые property/method names.
- Для этого допускается использовать Roslyn-based source scan, потому что merge должен быть синтаксически устойчивым, а не regex-best-effort.
- Правила merge:
  - уже существующие controls не дублируются;
  - при конфликте по property name, но другом locator-е генерируется новый уникальный name с детерминированным suffix;
  - при отсутствии target partial class recorder не сохраняет "ложно готовый" код, а пишет диагностическую ошибку в overlay/status и в save result.

#### 6.2.4 Политика селекторов
- Основной локатор v1: `AutomationId`.
- Опциональный fallback: `Name`, если это явно разрешено опцией `AllowNameLocators`.
- Tree path, визуальные координаты и иные positional selectors не сохраняются в generated code.
- Если у контрола нет допустимого стабильного локатора:
  - шаг не генерируется как исполнимый код;
  - overlay показывает причину (`missing AutomationId`, `Name locator disallowed`, `ambiguous control`);
  - save result помечает сценарий как требующий ручной стабилизации.
- Это осознанное расхождение с внешним `Avalonia.TestRecorder`: здесь мы жертвуем "записалось хоть как-то" ради честности test contract.

#### 6.2.5 Capture model: только high-level AppAutomation actions
- Recorder должен писать высокоуровневые действия, а не низкоуровневые pointer events.
- Поддерживаемые v1 interaction steps:
  - `TextBox` -> `EnterText(...)`
  - `Button` -> `ClickButton(...)`
  - `CheckBox` -> `SetChecked(...)`
  - `RadioButton` -> `SetChecked(...)`
  - `ToggleButton` -> `SetToggled(...)`
  - `ComboBox` -> `SelectComboItem(...)`
  - `Slider` -> `SetSliderValue(...)`
  - spinner-like input / `TextBox` numeric entry -> `SetSpinnerValue(...)` только когда control type/config явно указывает на такой mapping
  - `TabItem` -> `SelectTabItem(...)`
  - `Tree` selection -> `SelectTreeItem(...)`
  - `DateTimePicker` / `Calendar` -> `SetDate(...)`
- Поддерживаемые v1 assert/wait steps:
  - text equality / contains for text-bearing controls;
  - `IsChecked`;
  - `IsToggled`;
  - `IsSelected` для radio/tab/tree scenarios;
  - `IsEnabled`.
- Неподдерживаемые действия должны не "молчаливо понижаться" до неподходящего DSL вызова, а явно отражаться как unsupported.

#### 6.2.6 Минимальные additive-расширения DSL
- Чтобы assertion capture не генерировал фальшивый код, добавить в `UiPageExtensions` только недостающие честные waits:
  - `WaitUntilIsEnabled(...)` для `IUiControl`
  - `WaitUntilIsChecked(...)` для `ICheckBoxControl`
  - `WaitUntilTextEquals(...)` и `WaitUntilTextContains(...)` для `ILabelControl`/`ITextBoxControl`
- Не добавлять generic raw-click/raw-visible API только ради parity с внешним recorder-ом.
- Расширения должны остаться runtime-agnostic и работать одинаково в `Headless` и `FlaUI`.

#### 6.2.7 Overlay и UX записи
- Использовать знакомую модель overlay как в исходном recorder-е:
  - отдельное topmost окно/overlay;
  - кнопки `Start/Stop`, `Clear`, `Save`;
  - step counter;
  - status/preview line с последним сгенерированным AppAutomation вызовом либо диагностикой.
- Сохранить hotkeys для start/stop/save/assert capture, но команды должны отражать AppAutomation semantics.
- Overlay должен:
  - показывать последний успешно построенный DSL шаг;
  - показывать причину, почему шаг не может быть сохранён;
  - не делать тяжёлый синтаксический merge на каждый клик; только lightweight preview during recording и full merge на save.

#### 6.2.8 Threading и производительность
- Снятие UI-событий и классификация контрола выполняются на UI thread.
- Полноценный save pipeline (`scan -> merge plan -> file write`) переносится в background, кроме минимального чтения необходимых UI-свойств.
- Любые обращения к Avalonia visual tree делаются только на UI thread или через dispatcher.

#### 6.2.9 Reference integration в sample
- В `sample/DotnetDebug.Avalonia/App.axaml.cs` добавить debug-only opt-in интеграцию recorder-а через env var наподобие:
  - `APPAUTOMATION_RECORDER=1`
  - `APPAUTOMATION_RECORDER_SCENARIO=<name>` optional
  - path/namespace options задаются кодом sample integration
- Generated output sample по умолчанию направляется в `sample/DotnetDebug.AppAutomation.Authoring/Recorded`.
- Это даёт maintainers локальный ручной smoke path для записи и отладки framework без consumer-репозитория.

#### 6.2.10 Template и sample adjustments
- `MainWindowScenariosBase<TSession>` в sample и в template content становится `partial`.
- Page classes уже `partial`; их контракт не меняется.
- Документация явно фиксирует, что для recorder-based augmentation scenario base class должен быть `partial`.

## 7. Бизнес-правила / Алгоритмы
- `FR-1`: Recorder генерирует артефакты, совместимые с канонической `Authoring`-моделью `AppAutomation`.
- `FR-2`: Сохранённый recorded scenario автоматически исполним существующими runtime wrapper-ами после попадания partial-файлов в `Authoring` проект.
- `FR-3`: Recorder не сохраняет tree-path/coordinate selectors как тестовый контракт.
- `FR-4`: При отсутствии стабильного локатора recorder выдаёт диагностику, а не псевдо-рабочий код.
- `FR-5`: Для уже объявленных `[UiControl(...)]` recorder не дублирует control declarations.
- `FR-6`: При конфликте по имени control property recorder создаёт детерминированное уникальное имя и сообщает об этом в save result.
- `FR-7`: Поддерживаемые действия конвертируются в высокоуровневые AppAutomation DSL-вызовы, а не в низкоуровневые pointer operations.
- `FR-8`: Assertion capture использует только те проверки, которые честно поддерживаются текущим abstraction contract.
- `FR-9`: Sample app можно запустить в recorder mode без ручного патча исходников перед каждым прогоном.
- `NFR-1`: Recorder не должен вносить breaking changes в existing runtime packages.
- `NFR-2`: Save pipeline не должен длительно блокировать UI thread.
- `NFR-3`: Изменения покрываются regression-тестами и полным `dotnet test` по решению.

## 8. Точки интеграции и триггеры
- `sample/DotnetDebug.Avalonia/App.axaml.cs`
- `src/AppAutomation.Abstractions/UiPageExtensions.cs`
- новый пакет `src/AppAutomation.Recorder.Avalonia/*`
- `sample/DotnetDebug.AppAutomation.Authoring/Tests/MainWindowScenariosBase.cs`
- `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/tests/SampleApp.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`
- `README.md`
- solution/project graph (`AppAutomation.sln`, `Directory.Packages.props`, package metadata)

## 9. Изменения модели данных / состояния
- Persisted state:
  - recorder создаёт generated source files в target authoring project;
  - опционально сохраняет sidecar debug artifact с metadata записи (`.json`/`.txt`) только как developer aid, без обязательного участия в runtime contract.
- Runtime state:
  - текущая session state recorder-а (`Off` / `Recording`);
  - список recorded actions;
  - карта encountered controls c классификацией в `UiControlType`, locator quality и save eligibility;
  - overlay status / warnings.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - добавить новый пакет и новый test project;
  - включить sample integration только через env var opt-in;
  - сделать authoring scenario base partial в sample/template;
  - задокументировать manual opt-in integration для consumer AUT.
- Совместимость:
  - существующие проекты `Headless`, `FlaUI`, `Authoring`, `TestHost` остаются работоспособны без подключения recorder-а;
  - additive DSL методы не ломают existing tests.
- Rollback:
  - новый recorder package можно удалить из solution без затрагивания core runtime-пакетов;
  - sample integration отключается удалением env var либо откатом одного файла;
  - partial change в templates/sample обратно совместим и может быть оставлен даже при rollback recorder-а.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - из sample AUT можно записать smoke-сценарий и сохранить partial-файлы в `sample/DotnetDebug.AppAutomation.Authoring/Recorded`;
  - generated partial page file содержит только недостающие controls и использует `UiControlType`/`UiLocatorKind`, совместимые с `AppAutomation`;
  - generated scenario partial добавляет новый `[Test]`-метод в existing partial scenario base и запускается существующими runtime wrappers;
  - unsupported controls/actions не превращаются в ложный рабочий код и явно диагностируются;
  - при отсутствии `AutomationId` шаг помечается как requiring stabilization, а не сохраняется через tree path;
  - новые waits/assert helpers покрыты regression-тестами и проходят в обоих runtime-совместимых unit-level контрактах;
  - template/sample authoring scenario base declared as `partial`;
  - README содержит инструкцию по включению recorder-а и его ограничениям.
- Тесты:
  - добавить `tests/AppAutomation.Recorder.Avalonia.Tests`
  - покрыть selector policy:
    - `AutomationId` first
    - `Name` fallback only when allowed
    - no tree-path persistence
  - покрыть codegen:
    - page partial generation
    - scenario partial generation
    - merge with existing `[UiControl]`
    - conflict renaming
    - fail-fast when target scenario base is not partial
  - покрыть session capture на headless `Avalonia` controls для поддерживаемых действий
  - расширить tests на `UiPageExtensions` для новых wait helpers
  - обновить template/build tests, чтобы проверить `partial` на scenario base и docs contract
- Команды проверки:
  - `dotnet build AppAutomation.sln -c Debug`
  - `dotnet test tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -c Debug`
  - `dotnet test tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj -c Debug`
  - `dotnet test tests/AppAutomation.Build.Tests/AppAutomation.Build.Tests.csproj -c Debug`
  - `dotnet test AppAutomation.sln -c Debug`
  - manual smoke:
    - `$env:APPAUTOMATION_RECORDER='1'`
    - `dotnet run --project sample/DotnetDebug.Avalonia/DotnetDebug.Avalonia.csproj -c Debug`

## 12. Риски и edge cases
- Roslyn-based source scan увеличит вес recorder package и save latency.
- Существующие consumer repos могут иметь scenario base class без `partial`; recorder должен диагностировать это явно.
- Некоторые `Avalonia` controls могут визуально выглядеть как один widget, но технически приходить как inner templated child; потребуется подъём к ближайшему осмысленному control/locator owner.
- Name-based locators могут быть локализуемыми и хрупкими; поэтому они остаются opt-in и помечаются warning-ами.
- Для composite/custom controls recorder может записать только primitive-level взаимодействия; это честное ограничение v1.
- Debug-only sample integration не должна случайно попасть в release behaviour приложения.

## 13. План выполнения
1. Добавить рабочую спецификацию и утвердить целевой `CanonicalPartials` output mode.
2. Создать новый пакет `AppAutomation.Recorder.Avalonia` с session, overlay, step model и selector policy.
3. Реализовать AppAutomation-oriented code generation и source scan/merge для `Authoring` partial files.
4. Добавить минимальные DSL wait helpers в `AppAutomation.Abstractions`.
5. Включить sample integration и изменить sample/template scenario base classes на `partial`.
6. Добавить regression tests recorder-а, DSL и build/template contract.
7. Обновить README с recorder opt-in workflow и ограничениями.

## 14. Открытые вопросы
- Нет блокирующих открытых вопросов.
- Осознанный выбор сделан в пользу `CanonicalPartials`, а не генерации отдельных runtime test wrappers:
  - это лучше соответствует текущей архитектуре репозитория;
  - это даёт автоматическое исполнение в обоих runtime-ах;
  - это минимизирует объём generated code и число мест для merge-конфликтов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`
- Выполненные требования профиля:
  - сохранён приоритет стабильных селекторов и UI-contract consistency;
  - предусмотрены обновления UI/integration тестов и smoke path;
  - учтены desktop threading constraints и недопустимость длительной блокировки UI thread;
  - дизайн ориентирован на пользовательские сценарии, а не на внутренние детали runtime-а.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Recorder.Avalonia/*` | новый пакет recorder-а | основная функциональность записи и codegen |
| `tests/AppAutomation.Recorder.Avalonia.Tests/*` | новый regression test project | покрытие capture/codegen/merge policy |
| `src/AppAutomation.Abstractions/UiPageExtensions.cs` | additive wait helpers | честный assertion capture для recorder-а |
| `sample/DotnetDebug.Avalonia/App.axaml.cs` | debug-only recorder integration | reference workflow для maintainers |
| `sample/DotnetDebug.AppAutomation.Authoring/Tests/MainWindowScenariosBase.cs` | `partial` | recorder должен добавлять test methods через partial |
| `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/tests/SampleApp.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` | `partial` | consumer template совместим с recorder workflow |
| `README.md` | инструкция по recorder workflow | снизить порог входа и зафиксировать ограничения |
| `Directory.Packages.props` | новые package versions при необходимости | централизованное управление зависимостями |
| `AppAutomation.sln` | включение новых проектов | build/test coverage solution-wide |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Создание сценариев | полностью вручную | запись действий + генерация authoring partials |
| Повторное использование runtime wrappers | только для hand-written tests | recorder-generated tests тоже исполняются в `Headless` и `FlaUI` |
| Selector fallback | вручную решается разработчиком | recorder жёстко навязывает стабильный contract и диагностику |
| Отладка framework | много ручных циклов через sample | интерактивная запись и preview AppAutomation DSL |
| Scenario base contract | обычный abstract class | `partial abstract class`, пригодный для recorder augmentation |

## 18. Альтернативы и компромиссы
- Вариант: просто встроить внешний `Avalonia.TestRecorder` почти без изменений.
  - Плюсы: быстрее старт.
  - Минусы: generated code несовместим с `AppAutomation`, selector policy не совпадает, архитектурный drift.
- Вариант: генерировать отдельные `Headless` и `FlaUI` runtime tests.
  - Плюсы: можно обойтись без изменения scenario base на `partial`.
  - Минусы: дублирование сценариев, уход от канонического `Authoring`-слоя, больше merge surface.
- Вариант: сохранять tree-path как fallback, чтобы "записывалось всё".
  - Плюсы: выше apparent coverage.
  - Минусы: нестабильные тесты, конфликт с UI automation profile, ложное чувство завершённости.
- Выбранный путь:
  - переносит полезные идеи внешнего recorder-а, но подчиняет их архитектуре `AppAutomation`;
  - даёт лучший leverage на существующую topology;
  - делает generated output реально поддерживаемым, а не только демонстрационным.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Распределение ответственности, output mode, selector policy, threading и rollout описаны |
| C. Безопасность изменений | 11-13 | PASS | additive rollout, rollback и ограничения v1 заданы |
| D. Проверяемость | 14-16 | PASS | Acceptance, тест-план и команды верификации определены |
| E. Готовность к автономной реализации | 17-19 | PASS | План этапов есть, блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | Требования `dotnet-desktop-client` и `ui-automation-testing` учтены |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | v1 scope и жёсткие Non-Goals явно определены |
| 2. Понимание текущего состояния | 5 | AS-IS привязан и к текущему репозиторию, и к внешнему recorder-у |
| 3. Конкретность целевого дизайна | 5 | Выбран точный output mode, merge model и API-level shape |
| 4. Безопасность (миграция, откат) | 5 | Rollout/rollback и backward compatibility описаны |
| 5. Тестируемость | 5 | Есть acceptance, dedicated tests и manual smoke path |
| 6. Готовность к автономной реализации | 5 | Решение декомпозировано, доминирующий вариант выбран |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - зафиксирован один доминирующий output mode `CanonicalPartials`, чтобы не расползтись между несколькими генераторами;
  - явно вынесены ограничения v1 по unsupported gestures/assertions;
  - добавлено требование сделать scenario base class `partial`, чтобы recorder не плодил runtime wrapper duplication.
- Что осталось на решение пользователя:
  - ничего блокирующего; пользовательское подтверждение требуется только как переход из `SPEC` в `EXEC`.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `SPEC` | `delivery-task` / новый recorder для Avalonia | `0.86` | Нужно было понять обязательный instruction stack и ограничения QUEST | Собрать central instructions и локальный контекст | `Нет` | `Нет` | Без корректной маршрутизации нельзя переходить к изменениям кода | `AGENTS.md`, `C:\Projects\My\Agents\instructions\*` |
| `SPEC` | Анализ источника и целевой архитектуры | `0.89` | Нужно было сравнить внешний recorder с текущим `Authoring`/runtime contract | Сформировать дизайн, совместимый с `AppAutomation` | `Нет` | `Нет` | Прямое копирование оказалось неверным из-за другой DSL и selector policy | `D:\YandexDisk\Projects\ИЗП\Sources\Avalonia.TestRecorder\*`, `src/AppAutomation.*`, `sample/DotnetDebug.*` |
| `SPEC` | Подготовка спеки и quality gate | `0.92` | Блокирующих неизвестных не осталось | Запросить подтверждение спеки | `Да` | `Да, ожидается фраза "Спеку подтверждаю"` | По правилам QUEST переход в `EXEC` возможен только после явного подтверждения пользователя | `specs/2026-04-06-appautomation-avalonia-recorder.md` |
| `EXEC` | Создание базового recorder package skeleton | `0.78` | Нужно было проверить фактические API Avalonia и совместимость с solution conventions | Прогнать первую сборку и устранить компиляционные ошибки | `Нет` | `Да, пользователь подтвердил переход в EXEC` | Сначала собран минимально цельный каркас пакета и генератора, чтобы дальше развивать поведение на стабильной структуре проектов | `src/AppAutomation.Recorder.Avalonia/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj` |
| `EXEC` | Стабилизация базовой сборки recorder-а | `0.84` | Оставалось подтвердить корректный traversal visual tree и компиляцию под `net8.0`/`net10.0` | Перейти к solution wiring, regression tests и sample/template integration | `Нет` | `Нет` | Сначала снят compile blocker в selector resolver, чтобы дальнейшие изменения опирались на собираемый базис, а не на черновик | `src/AppAutomation.Recorder.Avalonia/RecorderSelectorResolver.cs`, `src/AppAutomation.Recorder.Avalonia/*.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/*` |
| `EXEC` | Доведение recorder workflow до вертикального среза | `0.91` | Нужно было подтвердить, что generated DSL опирается на реальные wait helpers и что sample/template готовы к partial augmentation | Добавить solution wiring, прогнать integration/smoke/full verification и сделать post-EXEC review | `Нет` | `Нет` | В этом блоке закрыты additive waits, regression tests, sample env-var integration, `partial` contract в sample/template и документация workflow | `src/AppAutomation.Abstractions/UiPageExtensions.cs`, `tests/AppAutomation.Abstractions.Tests/UiPageExtensionsTests.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/*`, `sample/DotnetDebug.Avalonia/*`, `sample/DotnetDebug.AppAutomation.Authoring/Tests/MainWindowScenariosBase.cs`, `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/tests/SampleApp.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`, `README.md`, `AppAutomation.sln` |
| `EXEC` | Финальная верификация и post-EXEC review | `0.94` | Оставалось подтвердить отсутствие solution-level регрессий и критичных review-находок | Подготовить итоговый отчёт пользователю | `Нет` | `Нет` | Последовательно выполнены targeted tests, build всего решения, headless smoke-suite, полный `dotnet test --solution`, затем sanity-pass по diff и hygiene-проверка | `tests/AppAutomation.Abstractions.Tests/*`, `tests/AppAutomation.Recorder.Avalonia.Tests/*`, `sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/*`, `AppAutomation.sln`, `README.md`, `sample/DotnetDebug.Avalonia/*` |
