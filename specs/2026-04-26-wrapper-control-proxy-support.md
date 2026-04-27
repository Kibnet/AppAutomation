# Пакет 1: proxy-поддержка wrapper-контролов для recorder / headless / FlaUI

## 0. Метаданные
- Тип (профиль): `ui-automation-testing`
- Владелец: Codex
- Масштаб: medium
- Целевая ветка: `feat-arm-paritet`
- Ограничения:
  - До подтверждения спеки изменяется только этот файл.
  - Основа для выбора объёма: `docs/appautomation/component-coverage-gaps.md`
  - Цель текущего пакета: закрыть только wrapper/composite-root сценарии, которые можно свести к уже поддержанным примитивам.
  - Нативная поддержка `DataGridControl`, `TreeListControl`, `PropertyGridControl`, `RibbonControl`, `DockManager` в этот пакет не входит.
- Связанные ссылки:
  - `docs/appautomation/component-coverage-gaps.md`
  - `specs/2026-04-26-component-recorder-runtime-gap-analysis.md`
  - `src/AppAutomation.Abstractions/UiControlAdapters.cs`
  - `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs`

## 1. Overview / Цель
Сделать первый исполнимый пакет улучшений в коде AppAutomation для контролов, у которых пользовательская семантика живёт не в custom root, а во внутреннем уже поддержанном primitive/control-part.

В рамках пакета нужно:
- добавить runtime proxy-слой, который позволит `Headless` и `FlaUI` работать с wrapper-контролами через уже существующие primitive resolvers;
- добавить recorder-конфигурацию для устойчивого маппинга внутренних part-локаторов на логические page properties;
- покрыть это contract/unit tests.

## 2. Текущее состояние (AS-IS)
- В `AppAutomation.Abstractions` уже есть паттерн composite adapters: `SearchPicker`, `DateRangeFilter`, `NumericRangeFilter`, `Dialog`, `Notification`, `FolderExport`, `ShellNavigation`.
- `HeadlessControlResolver` и `FlaUiControlResolver` уже хорошо умеют primitive controls:
  - `TextBox`
  - `Button`
  - `ComboBox`
  - `ListBox`
  - `Spinner`
  - `DateTimePicker`
  - `Label`
- `RecorderSelectorResolver` уже умеет:
  - `RecorderControlHint` для переопределения `UiControlType`;
  - `RecorderLocatorAlias` для маппинга нестабильного/визуального локатора в стабильный логический locator.
- По анализу из `component-coverage-gaps.md` основной быстрый выигрыш лежит в wrapper roots:
  - `BaseEditor` family;
  - `ListViewControl`;
  - отдельные части `MxSplitButton`;
  - custom roots, которые могут быть описаны как `Button` / `TextBox` / `ComboBox` / `ListBox` / `Spinner` / `DateTimePicker`.

## 3. Проблема
Сейчас AppAutomation умеет работать либо с primitive controls напрямую, либо с заранее зашитыми composite adapters. Но нет общего механизма, который позволил бы:
- оставить в page object логический property на wrapper/root;
- а runtime-разрешение направить на внутренний part/primitive control;
- при этом recorder должен уметь записывать событие с part-а в тот же логический property, а не в случайный внутренний locator.

Из-за этого пользователь вынужден либо:
- использовать внутренние locators напрямую в authoring pages;
- либо писать проектно-специфичные adapters без общего API.

## 4. Цели дизайна
- Повторное использование существующих primitive runtimes вместо отдельной “нативной” поддержки wrapper roots.
- Единый API для runtime proxy support в `AppAutomation.Abstractions`.
- Единый API для recorder-конфигурации root <-> inner part mapping.
- Нулевая регрессия для существующих page objects, source generator и recorder codegen.
- Явный adoption path: proxy-поддержка подключается только через explicit resolver wiring, без скрытой магии.
- Явное отделение:
  - wrapper/proxy scenarios, которые закрываем сейчас;
  - rich surfaces, которые сознательно оставляем на отдельные пакеты.

## 5. Non-Goals (чего НЕ делаем)
- Не добавляем нативную typed-support модель для:
  - `DataGridControl`
  - `TreeListControl`
  - `PropertyGridControl`
  - `Toolbar` / `PopupMenu` / `RibbonControl`
  - `DockManager`
- Не вводим полноценный новый runtime control type для `MxSplitButton`.
- Не меняем source generator схему `UiControlType -> interface`.
- Не делаем новый UIA bridge для Eremex rich controls.
- Не делаем automatic discovery / auto-wiring proxy adapters во всех consumer runtimes.
- Consumer, использующий proxy API, обязан явно завернуть `IUiControlResolver` на этапе `CreatePage(...)`, фабрики page objects или DI-композиции.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Основная идея
Добавить в `AppAutomation.Abstractions` слой primitive proxy adapters:
- page property остаётся логическим и может использовать внешний locator/имя свойства;
- adapter перехватывает `Resolve<TControl>` по `PropertyName`;
- внутри adapter запрашивает у runtime уже поддержанный primitive control по внутреннему locator.

Параллельно добавить в recorder helper-API, который конфигурирует:
- `RecorderControlHint` для логического root locator;
- `RecorderLocatorAlias` для внутреннего part locator c тем же конечным `UiControlType`, что и у логического свойства;
- чтобы запись с inner control consistently приводилась к логическому page property.

Ключевое правило пакета:
- `RecorderSelectorResolver` не меняет текущий порядок `alias -> resolved control`.
- Поэтому proxy-helper обязан делать typed alias, а не рассчитывать на повторное применение `ControlHint` после alias.

### 6.2 Какие сценарии пакет должен закрыть
1. `BaseEditor` family:
   - логический `TextBox` / `ComboBox` / `Spinner` / `DateTimePicker` property на wrapper root;
   - runtime идёт во внутренний editor part.
2. `ListViewControl`-подобная обёртка над реальным list surface:
   - логический `ListBox` property;
   - runtime идёт во внутренний list surface, который уже разрешается как `UiControlType.ListBox`;
   - interactive selection входит в scope только если concrete runtime object реализует `ISelectableListBoxControl`.
3. `MxSplitButton`:
   - без нового composite типа;
   - как две отдельные logical button properties:
     - primary action button;
     - dropdown/open button;
   - обе разрешаются через proxy на внутренние parts.
4. Аналогичные wrapper cases для `Label` и `Button`, когда root semantic lives in inner primitive.

### 6.3 Изменения API в `AppAutomation.Abstractions`
Планируемые сущности:
- компактный config record для primitive proxy target, например:
  - `TargetLocatorValue`
  - `TargetControlType`
  - `TargetLocatorKind`
  - `FallbackToName`
- базовый extension method вида:
  - `WithProxy(string propertyName, PrimitiveProxyTarget target)`
- optional thin wrappers допустимы только как sugar поверх `WithProxy(...)`, например:
  - `WithTextBoxProxy(...)`
  - `WithButtonProxy(...)`
  - `WithComboBoxProxy(...)`
  - `WithListBoxProxy(...)`
  - `WithSpinnerProxy(...)`
  - `WithDateTimePickerProxy(...)`
  - `WithLabelProxy(...)`
- общий adapter implementation, который:
  - матчится по `definition.PropertyName`;
  - создаёт внутренний `UiControlDefinition` для target locator;
  - делегирует в уже существующий `innerResolver.Resolve<TPrimitive>()`.

Важно:
- `HeadlessControlResolver` и `FlaUiControlResolver` менять не требуется, если proxy сводится к уже поддержанным primitive definitions.
- Для `UiControlType.ListBox` proxy должен возвращать тот concrete runtime object, который отдаёт existing resolver.
  - В текущих `Headless` и `FlaUI` listbox-реализациях это `ISelectableListBoxControl`, поэтому `SelectListBoxItem(...)` продолжает работать, если inner target действительно является real listbox surface.
- Подключение proxy-support остаётся explicit:
  - `new HeadlessControlResolver(...).WithProxy(...)`
  - `new FlaUiControlResolver(...).WithProxy(...)`
  - либо эквивалентная DI/factory-композиция в consumer app.

### 6.4 Изменения API в `AppAutomation.Recorder.Avalonia`
Добавить recorder helper / configuration API для proxy-сценариев.

Предпочтительный вариант:
- один базовый helper вида
  - `ConfigureProxy(logicalLocatorValue, innerLocatorValue, targetControlType, ...)`
- optional typed wrappers допустимы только как sugar поверх него:
  - `ConfigureTextBoxProxy(...)`
  - `ConfigureButtonProxy(...)`
  - `ConfigureComboBoxProxy(...)`
  - `ConfigureListBoxProxy(...)`
  - `ConfigureSpinnerProxy(...)`
  - `ConfigureDateTimePickerProxy(...)`
  - `ConfigureLabelProxy(...)`

Поведение helper:
1. На логический locator добавляется `RecorderControlHint` с правильным `UiControlType`.
2. На внутренний part locator добавляется `RecorderLocatorAlias`, ведущий к логическому locator.
3. Этот alias обязан нести тот же `TargetControlType`, что и логический control type.
4. Если событие пришло с root и у root есть `AutomationId`, capture остаётся на root locator и использует `RecorderControlHint`.
5. Если событие пришло с inner part и у него свой locator, capture автоматически маппится обратно в логический locator через typed alias.

### 6.5 Что сознательно не меняем в рантаймах
- `HeadlessControlResolver.Resolve(...)` и `FlaUiControlResolver.Resolve(...)` остаются без новой ветки `UiControlType`, потому что proxy-слой должен работать поверх уже существующих `UiControlType`.
- `UiControlType` не расширяется.
- `AuthoringCodeGenerator` не меняется, если сохраняется существующая модель “property -> UiControlType -> standard operation”.
- `RecorderSelectorResolver` не меняет alias-precedence; корректность proxy capture обеспечивается конфигурацией typed alias.

## 7. Бизнес-правила / Алгоритмы
- Proxy adapter матчится по `PropertyName`, а не по `LocatorValue`, как и существующие composite adapters.
- Proxy adapter не должен менять публичный `UiControlType` property definition; он только перенаправляет runtime resolution на inner locator.
- Recorder proxy helper должен настраивать mapping так, чтобы recorder генерировал один и тот же page property независимо от того, с root или inner part пришло событие.
- Inner-part alias всегда обязан содержать конечный `UiControlType`; helper не может рассчитывать на повторное применение `ControlHint` после alias.
- Proxy support не автоподхватывается глобально: consumer обязан явно обернуть resolver в месте создания page.
- Для split-button пакет не вводит новый action vocabulary:
  - primary button записывается как `ClickButton`;
  - dropdown/open button тоже записывается как `ClickButton`, но в отдельный logical property.
- Для `ListBox`-proxy операции выбора элемента входят в scope только когда resolved object реализует `ISelectableListBoxControl`; иначе в scope остаются только read-only/list assertion сценарии.

## 8. Точки интеграции и триггеры
- `src/AppAutomation.Abstractions/UiControlAdapters.cs`
- при необходимости новый соседний файл в `src/AppAutomation.Abstractions/` для proxy-record/helper types;
- `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorderOptions.cs`
- при необходимости новый helper-файл в `src/AppAutomation.Recorder.Avalonia/`
- `tests/AppAutomation.Abstractions.Tests/UiControlAdapterTests.cs`
- `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`
- `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs` или соседний headless integration test для real resolver smoke

## 9. Изменения модели данных / состояния
Persisted state не меняется.

Изменяется только конфигурационная модель resolver/recorder:
- новые proxy helpers в runtime;
- новые recorder helper methods поверх уже существующих `ControlHints` / `LocatorAliases`.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - добавить proxy API;
  - добавить recorder helper API;
  - добавить explicit wiring examples/tests для `Headless` и composition-level path для `FlaUI`;
  - покрыть тестами;
  - существующий код потребителей не ломается, новый API opt-in.
- Rollback:
  - revert изменённых файлов в `AppAutomation.Abstractions`, `AppAutomation.Recorder.Avalonia` и тестах.
- Совместимость:
  - существующие composite adapters и page definitions должны продолжить работать без изменений;
  - consumers без proxy wiring продолжают работать по старому поведению;
  - consumers, которые хотят wrapper-proxy, должны явно обновить resolver composition.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - можно зарегистрировать logical `TextBox` property, который runtime-resolve'ится во внутренний locator через proxy adapter;
  - то же самое работает для `Button`, `ComboBox`, `ListBox`, `Spinner`, `DateTimePicker`, `Label`;
  - для `ListBox` proxy selection-path считается покрытым только если inner runtime object реализует `ISelectableListBoxControl`;
  - recorder helper может смэппить inner part locator обратно в logical locator и выставить корректный `UiControlType` за счёт typed alias, без изменения alias-precedence в resolver;
  - generated step / page attribute остаются в существующей primitive-модели и не требуют нового `UiControlType`.
- Тесты:
  - adapter contract tests в `tests/AppAutomation.Abstractions.Tests`;
  - recorder tests в `tests/AppAutomation.Recorder.Avalonia.Tests`.
  - минимум один real `HeadlessControlResolver` smoke test в `tests/AppAutomation.TestHost.Avalonia.Tests`, где proxy adapter оборачивает реальный resolver и работает через generated primitive contract.
  - для `FlaUI` в рамках этого пакета обязательны composition-level tests / examples и отсутствие изменений в primitive resolver path; отдельный desktop end-to-end smoke остаётся follow-up.
- Команды проверки:
  - `dotnet test tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj`
  - `dotnet test tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj`
  - `dotnet test tests/AppAutomation.TestHost.Avalonia.Tests/AppAutomation.TestHost.Avalonia.Tests.csproj`
  - `git diff --check`

## 12. Риски и edge cases
- Если consumer не даёт стабильный locator ни root, ни inner part, proxy API проблему не решит.
- Если inner primitive locator нестабилен и меняется от шаблона к шаблону, recorder alias будет хрупким.
- `MxSplitButton` в этом пакете покрывается только как две separate buttons; unified semantic API для split-button остаётся follow-up.
- `ListViewControl` может не всегда сводиться к реальному `ListBox`; в таких случаях interactive selection выходит за scope и пакет даёт только read-only coverage либо не применяется вовсе.
- Возможен конфликт между proxy adapters и проектными custom adapters, если они матчятся по одному и тому же `PropertyName`; нужно сохранить порядок `WithAdapters(...)`.
- В репозитории нет готового детерминированного desktop FlaUI smoke harness для такого пакета; поэтому реальное end-to-end desktop подтверждение остаётся отдельным follow-up после architecture-safe integration шага.

## 13. План выполнения
1. Добавить generic proxy config type и `WithProxy(...)` в `AppAutomation.Abstractions`.
2. При необходимости добавить thin typed wrappers как sugar поверх `WithProxy(...)`.
3. Реализовать proxy adapter для primitive controls поверх existing `innerResolver`.
4. Добавить recorder helper API `ConfigureProxy(...)`, который конфигурирует `ControlHints` + typed `LocatorAliases`.
5. Добавить contract tests на runtime proxy resolution и typed list-selection behavior.
6. Добавить recorder tests на proxy mapping root/inner part.
7. Добавить real headless smoke test на wrapped `HeadlessControlResolver`.
8. Прогнать targeted `dotnet test` и `git diff --check`.

## 14. Открытые вопросы
Нет блокирующих вопросов.

Есть одно осознанное решение пакета:
- split-button остаётся в primitive-модели через две кнопки, а не через новый composite control type.

Есть второе осознанное решение пакета:
- proxy support остаётся explicit opt-in через resolver wiring, а не пытается внедриться автоматически через source generator или глобальный manifest bootstrap.

## 15. Соответствие профилю
- Профиль: `ui-automation-testing`
- Выполненные требования профиля:
  - решение строится вокруг стабильных selectors / automation anchors;
  - rich controls не объявляются “поддержанными” без реальной typed contract модели;
  - изменения будут покрыты тестами на resolver/recorder контракт.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-04-26-wrapper-control-proxy-support.md` | Новая рабочая спека | QUEST gate перед кодовыми изменениями |
| `src/AppAutomation.Abstractions/UiControlAdapters.cs` | Будет расширен proxy API | Runtime support для wrapper controls |
| `src/AppAutomation.Recorder.Avalonia/RecorderProxyConfigurationExtensions.cs` | Будет добавлен helper/config API | Recorder mapping root <-> inner part |
| `tests/AppAutomation.Abstractions.Tests/UiControlAdapterTests.cs` | Будут добавлены contract tests | Проверить proxy resolution |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` | Будут добавлены recorder tests | Проверить alias/hint proxy mapping |
| `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs` | Будет добавлен headless smoke или соседний integration test | Проверить wrapped real resolver path |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Wrapper roots в runtime | Нужен project-specific adapter или прямой inner locator в page object | Появляется общий proxy API поверх primitive resolvers, но подключается explicit wiring-ом |
| Recorder для wrapper parts | Нужно руками собирать `ControlHints` + `LocatorAliases` | Появляется helper-конфигурация для proxy mapping с typed alias |
| `MxSplitButton` first-level support | Нет общего паттерна | Можно описать как две logical button properties через proxy |
| `ListViewControl` first-level support | Нет общего паттерна | Можно описать как logical list proxy, если inner surface реально резолвится как `ListBox`; interactive selection зависит от `ISelectableListBoxControl` |

## 18. Альтернативы и компромиссы
- Вариант: сразу вводить новые `UiControlType` для editor family и split-button.
  - Плюсы: богаче доменная модель.
  - Минусы: затронет source generator, page extensions, runtimes, recorder action vocabulary.
  - Почему не выбран: слишком большой пакет для первого шага и высокий риск псевдо-поддержки.
- Вариант: ничего не делать в runtime, а просто документировать inner locators.
  - Плюсы: нулевой объём кода.
  - Минусы: плохой DX, засорение page objects internal part names, нет единого контракта.
  - Почему не выбран: не закрывает пользовательскую проблему.
- Вариант: делать только recorder helpers без runtime proxy adapters.
  - Плюсы: меньше кодовых изменений.
  - Минусы: шаги будут записываться, но потребитель останется без clean runtime resolution.
  - Почему не выбран: пакет должен закрывать recorder + headless/FlaUI story целиком для wrapper cases.
- Вариант: auto-wire proxies через source generator, manifest или глобальный bootstrap.
  - Плюсы: меньше ручной конфигурации у consumer.
  - Минусы: затрагивает authoring generator, runtime bootstrap и весь adoption path гораздо шире первого пакета.
  - Почему не выбран: слишком большой blast radius для первого шага; explicit wiring проще внедрить и проверить.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Архитектура proxy support и границы пакета описаны. |
| C. Безопасность изменений | 11-13 | PASS | Rollback простой, rich controls явно исключены. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria и команды тестирования указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | План выполним без дополнительных решений пользователя. |
| F. Соответствие профилю | 20 | PASS | Изменения опираются на стабильные UI contracts и тесты. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Зафиксирован именно первый пакет и его пределы. |
| 2. Понимание текущего состояния | 5 | Использованы выводы из gap-analysis и текущая архитектура adapters/recorder. |
| 3. Конкретность целевого дизайна | 5 | Перечислены ожидаемые API и поведение proxy mapping. |
| 4. Безопасность (миграция, откат) | 5 | Изменение additive, rollback прост. |
| 5. Тестируемость | 5 | Есть точные test targets и критерии приёмки. |
| 6. Готовность к автономной реализации | 5 | Достаточно конкретно для прямой реализации. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено на этапе проектирования:
  - объём пакета ограничен wrapper/proxy scenarios;
  - тяжёлые Eremex surfaces явно вынесены из этого шага;
  - split-button сведён к двум logical buttons вместо нового доменного типа.
  - typed alias сделан обязательной частью recorder-helper механики;
  - explicit consumer wiring зафиксирован как обязательный adoption path;
  - listbox scope сужен до реальных list surfaces;
  - добавлен обязательный real headless smoke в test plan.
- Что остаётся на подтверждение пользователя:
  - запуск EXEC по этому пакету после подтверждения спеки.

### Post-EXEC Review
- Статус: PASS
- Что реализовано:
  - добавлен generic proxy API в `AppAutomation.Abstractions`;
  - добавлены recorder proxy configuration helpers с typed alias;
  - добавлены tests для abstractions, recorder и real headless resolver path.
- Что проверено:
  - `dotnet test --project tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj`
  - `dotnet test --project tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj`
  - `dotnet test --project tests/AppAutomation.TestHost.Avalonia.Tests/AppAutomation.TestHost.Avalonia.Tests.csproj`
  - `git diff --check`
- Остаточные границы пакета:
  - `FlaUI` получил composition-safe поддержку через общий proxy layer, но отдельный desktop e2e smoke в этот пакет не входил;
  - rich Eremex surfaces (`DataGridControl`, `TreeListControl`, `PropertyGridControl`, `DockManager`, `Ribbon`) по-прежнему требуют отдельных пакетов.

## Approval
Подтверждено пользователем фразой: "спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Формирование первого кодового пакета по gap-analysis | 0.95 | Подтверждение пользователя для EXEC | Ожидать фразу `Спеку подтверждаю` | Да | Да, запрашивается подтверждение | Центральные инструкции требуют SPEC-first перед кодовыми изменениями | `specs/2026-04-26-wrapper-control-proxy-support.md` |
| EXEC | Реализация runtime proxy layer | 0.95 | Нет | Добавить recorder helpers и tests | Нет | Да, пользователь подтвердил спеку | Generic proxy adapter дал минимальный blast radius и использовал существующие primitive resolvers | `src/AppAutomation.Abstractions/UiControlAdapters.cs`, `src/AppAutomation.Abstractions/UiControlProxyAdapters.cs` |
| EXEC | Реализация recorder proxy mapping | 0.96 | Нет | Проверить typed alias на tests | Нет | Нет | Typed alias обязателен из-за текущего alias-precedence в `RecorderSelectorResolver` | `src/AppAutomation.Recorder.Avalonia/RecorderProxyConfigurationExtensions.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| EXEC | Проверка runtime и integration coverage | 0.94 | Отдельный desktop e2e smoke для FlaUI | Завершить задачу итоговым отчётом | Нет | Нет | Контрактные tests + real headless smoke закрывают текущий пакет без расширения desktop harness | `tests/AppAutomation.Abstractions.Tests/UiControlAdapterTests.cs`, `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs` |
