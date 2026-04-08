# BRD: Улучшение UX и полноты покрытия сценариев Avalonia Recorder

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-feature-parity`
- Владелец: `AppAutomation maintainers`
- Масштаб: `large`
- Целевой релиз / ветка: `Unreleased` / `main`
- Ограничения:
  - сохраняем canonical output через `Authoring` partials и не уходим в отдельный runtime DSL
  - не вводим tree-path, координатные или иные нестабильные persisted locator-ы
  - не добавляем low-level gestures как persisted contract, пока под них нет честного `AppAutomation` abstraction surface
  - additive public API предпочтительнее любых silent-breaking изменений существующих page/runtime контрактов
  - coverage recorder-а расширяем только там, где шаг можно либо честно исполнить, либо честно показать как preview-only / manual-review
- Связанные ссылки:
  - `C:\Projects\My\Agents\AGENTS.md`
  - `specs/2026-04-06-appautomation-avalonia-recorder.md`
  - `specs/2026-04-07-appautomation-recorder-parity-followup.md`
  - `src/AppAutomation.Recorder.Avalonia/*`
  - `src/AppAutomation.Abstractions/*`
  - `src/AppAutomation.Avalonia.Headless/*`
  - `src/AppAutomation.FlaUI/*`
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
`AppAutomation.Recorder.Avalonia` уже умеет писать полезные `Authoring` partial-файлы и после parity-итерации получил validation, listbox support, richer overlay и extensibility points. Но практический user experience остаётся ограниченным в трёх областях:

- часть реальных пользовательских действий теряется или записывается неустойчиво;
- overlay всё ещё больше похож на status panel, чем на инструмент review/edit/save;
- покрытие recorder-а заметно уже реального desktop UI surface: динамические контролы, popups, menu/flyout, richer item selectors, keyboard-driven flows, grid-like widgets.

Цель этой follow-up спеки: довести recorder до состояния, в котором его можно использовать как рабочий authoring assistant для живых Avalonia-приложений, а не только как baseline tool для sample и простых smoke-потоков.

## 2. Текущее состояние (AS-IS)
- Уже реализовано:
  - stable selector policy на базе `AutomationId` с контролируемым `Name` fallback;
  - selector/action validation и skip invalid steps при save;
  - configurable hotkeys;
  - overlay с `Save`, `Export...`, `Minimize/Restore`;
  - additive `ListBox` selection capability;
  - tests на validation, hotkeys, overlay state, listbox capability.
- Практические gaps после review:
  - subscriptions на interactive controls делаются один раз при `Window.Loaded`, поэтому dynamic UI может не записываться;
  - text capture завязан на `TextInputEvent`, что пропускает delete/paste/context menu/IME и часть value-change сценариев;
  - `ComboBox` / `ListBox` / `TreeView` selection записываются по display text, что нестабильно для duplicate labels и view-model items;
  - selector validation опирается на snapshot текущего root, а не на live root graph;
  - save/export не сериализованы и не имеют явного busy UX;
  - overlay не даёт step-level review/edit/remove/revalidate flow;
  - recorder не покрывает popup/menu/modal/shortcut/data-grid/search-like сценарии.

## 3. Проблема
Корневая проблема: recorder умеет генерировать правильный код там, где UI статичен и предсказуем, но в реальном desktop AUT слишком часто теряет события, опирается на хрупкие item identity heuristics и не даёт пользователю нормального review flow перед сохранением. В результате инструмент снижает порог входа лишь частично: maintainer всё ещё вынужден вручную переписывать шаги, повторно запускать запись и отлаживать "почему recorder промолчал".

## 4. Цели дизайна
- Повысить полноту и устойчивость записи пользовательских действий без отхода от stable contract philosophy.
- Перевести overlay из статуса "контрольная панель" в статус "легковесный step review tool".
- Сделать item-based selection и dynamic UI capture пригодными для production-like приложений.
- Расширить coverage recorder-а на реально востребованные desktop сценарии через additive abstractions и adapters.
- Сохранить backward compatibility текущего `Authoring`-first workflow и уже записанных partial-файлов.

## 5. Non-Goals (чего НЕ делаем)
- Не добавляем tree-path fallback, screen coordinates и иные нестабильные persisted locator-ы.
- Не превращаем recorder в полноценный visual-tree inspector/IDE.
- Не обещаем покрыть любой custom control без app-side hints/adapters.
- Не добавляем persisted gestures вроде `RightClick`, `DoubleClick`, `Hover`, `Scroll`, `KeyPress`, если для них нет execution contract в `AppAutomation`.
- Не ломаем существующие `IListBoxControl`, `IComboBoxControl`, page object properties или runtime wrappers ради "быстрого" расширения.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Карта улучшений по приоритетам
| Приоритет | Область | Текущее состояние | Целевое изменение |
| --- | --- | --- | --- |
| `P0` | Dynamic UI capture | static subscribe only at load | live root/control observation with re-attach and detach |
| `P0` | Text capture correctness | `TextInputEvent` only | value-change based pipeline with flush semantics |
| `P0` | Save UX and safety | fire-and-forget save/export | single-flight save/export with busy/result state |
| `P0` | Overlay review | only last preview/status | step journal with remove/retry/revalidate/ignore |
| `P1` | Item selector durability | display text only | structured item selector model with stable fallbacks |
| `P1` | Validation root | cached root snapshot | live validation root graph across window/popups |
| `P1` | Extensibility | assertion-only extractors | interaction/control adapters for richer controls |
| `P2` | Coverage breadth | no menu/modal/grid/shortcut coverage | additive coverage for popup/menu/modal/grid/search-like flows |
| `P2` | Advanced editing | no bulk operations | review filters, batch ignore, batch revalidate, diff-friendly diagnostics |

### 6.2 Детальный дизайн
#### 6.2.1 Live control observation вместо одноразовой подписки
- Вводится отдельный orchestration слой наподобие:
  - `RecorderRootRegistry`
  - `RecorderControlObserver`
  - `RecorderRootSnapshot`
- Recorder отслеживает не только `window.Content`, но и live graph roots:
  - owner window root;
  - popup roots;
  - modal/dialog top-levels, привязанные к текущему AUT;
  - динамически подключаемые visual subtree после initial `Loaded`.
- Подписки должны ставиться и сниматься автоматически при:
  - `AttachedToVisualTree`
  - `DetachedFromVisualTree`
  - смене `Window.Content`
  - открытии / закрытии popup-like host-ов
- Это убирает silent-loss для lazy tabs, navigation hosts, modal overlays и template-driven controls.

#### 6.2.2 Text capture через value-change pipeline
- Текущая зависимость от `TextInputEvent` заменяется на pipeline:
  - observe `TextBox.TextProperty`;
  - coalesce изменения debounce-слоем;
  - flush по `LostFocus`, `Enter`, `save`, `stop recording`, `pointer action on another control`;
  - различать user-driven и app-driven updates по recent input/focus heuristics.
- `TextInputEvent` сохраняется только как дополнительный сигнал "пользователь печатает", а не как единственный источник истины.
- Это должно покрыть:
  - `Backspace/Delete`;
  - paste/cut;
  - context-menu edits;
  - IME/composition завершение;
  - editing через native value pipeline конкретного control-а.

#### 6.2.3 Structured item selectors для list/combo/tree-like controls
- Внутренняя модель recorded step расширяется новым объектом:
  - `RecordedItemSelector`
- Базовые selector kinds:
  - `AutomationId`
  - `Text`
  - `TextOrdinal`
  - `Index`
  - `CustomKey`
- Политика выбора:
  - сначала item-level `AutomationId`, если он доступен;
  - затем unique text;
  - затем text + ordinal среди одинаковых caption-ов;
  - затем app-provided custom key extractor/adaptor;
  - `Index` разрешён только как warning-grade fallback либо preview-only, в зависимости от настроек stability policy.
- Persisted step generation должна использовать structured selector, а не raw string item text.
- Runtime adapters (`Headless`, `FlaUI`) должны уметь честно исполнять structured item selection без fallback на `ToString()` там, где есть более стабильный путь.

#### 6.2.4 Live validation root graph
- `RecorderSelectorResolver` не хранит fixed root snapshot.
- Validation каждый раз работает против live `RecorderRootRegistry`, который знает:
  - current owner content root;
  - attached popup/dialog roots;
  - актуальные visual subtree.
- Selector validation получает richer outcomes:
  - `Valid`
  - `Warning`
  - `Invalid`
  - опционально `Unstable`, если locator формально работает, но quality низкая
- Для item-based selection validation отдельно валидируется:
  - locator до control owner;
  - item selector uniqueness/availability внутри current control items.

#### 6.2.5 Overlay v3: review-first UX
- Overlay становится не только status panel, а review surface с тремя зонами:
  - `Session controls`: record/stop, save, export, clear, minimize;
  - `Session diagnostics`: status badge, saved/skipped counts, busy/error state;
  - `Step journal`: последние шаги с preview, validation status и действиями.
- Минимально нужные step actions:
  - `Remove`
  - `Ignore`
  - `Retry validation`
  - `Copy preview`
- Фильтры:
  - `All`
  - `Warnings`
  - `Invalid`
  - `Persistable`
- UX поведение:
  - invalid шаги не только видны, но и объяснимы на уровне шага;
  - save показывает `persisted/skipped` и путь результата;
  - busy-state блокирует повторные save/export/hotkey команды;
  - minimized mode показывает не только status string, но и summary вида `12 steps | 2 warnings | saving...`.

#### 6.2.6 Single-flight save/export
- Session получает coordinator наподобие `RecorderSaveCoordinator`.
- Правила:
  - только одна операция save/export может выполняться одновременно;
  - повторный hotkey во время active save либо игнорируется с явным status, либо ставится в очередь как один pending request;
  - overlay кнопки и соответствующие hotkeys блокируются на время операции;
  - результат save/export всегда возвращает user-visible success/failure summary.
- Это убирает race conditions по записи файлов и flicker статусов.

#### 6.2.7 Extensibility: от assertion extractors к interaction adapters
- Текущий extensibility point на assertions сохраняется, но дополняется новым слоем:
  - `IRecorderControlAdapter`
  - `IRecorderInteractionExtractor`
  - `IRecorderItemSelectorProvider`
- Responsibilities:
  - adapter знает, как извлечь stable selector для сложного control-а;
  - interaction extractor знает, какие actions/assertions можно честно записать;
  - item selector provider знает, как стабильно идентифицировать item внутри composite control-а.
- Built-in adapters со временем должны покрыть:
  - combo/list/tree item selectors;
  - search-like pickers;
  - grid/data-grid rows/cells;
  - menu/flyout items.

#### 6.2.8 Coverage expansion для desktop сценариев
- Следующий реальный coverage scope:
  - popup menus / context menus / flyouts;
  - modal dialogs и secondary windows;
  - keyboard shortcuts/chords на уровне scenario intent;
  - grid/data-grid cell/row selection;
  - composite search pickers;
  - tab-hosted lazy content.
- Каждая новая группа сценариев включается только при наличии:
  - stable selector strategy;
  - execution support в runtime-ах;
  - regression tests на both headless and flaui where applicable.

#### 6.2.9 Review/edit workflow перед save
- Recorder session хранит не только raw step list, но и review metadata:
  - `IsIgnored`
  - `ReviewState`
  - `LastValidationAt`
  - `FailureCode`
- Save pipeline по умолчанию пишет только:
  - `CanPersist = true`
  - `IsIgnored = false`
- Overlay должен позволять пользователю:
  - убрать один плохой шаг, не перезаписывая всю сессию;
  - повторно провалидировать шаг после изменения UI;
  - явно принять warning-grade step.

## 7. Бизнес-правила / Алгоритмы
- `FR-1`: dynamic controls, появившиеся после initial `Loaded`, должны иметь шанс быть записанными без переподключения recorder-а.
- `FR-2`: текстовый шаг фиксируется по конечному value control-а, а не по отдельным printable key events.
- `FR-3`: item-based step не должен persisted только по `ToString()` без проверки uniqueness/selector quality.
- `FR-4`: save/export не должен выполняться параллельно более одного раза.
- `FR-5`: overlay обязан показывать step-level reason, почему шаг invalid/ignored/skipped.
- `FR-6`: popup/menu/modal coverage включается только если recorder умеет привязать событие к стабильному owner locator и честному action contract.
- `FR-7`: additive API expansion не должна ломать существующие page object properties и already generated scenarios.
- `FR-8`: warning-grade unstable item selector допускается только при явной маркировке в preview и diagnostics.

## 8. Точки интеграции и триггеры
- `RecorderSession`
  - orchestration live observation;
  - save/export coordination;
  - review-state transitions.
- `RecorderSelectorResolver`
  - live root graph validation;
  - item selector validation.
- `RecorderStepFactory`
  - structured item selector generation;
  - interaction/control adapters.
- `RecorderOverlay`
  - step journal;
  - busy/error/result state;
  - per-step review actions.
- `AppAutomationRecorder.Attach(...)`
  - registration of root observers and popup/dialog coordination.
- `AppAutomation.Abstractions`
  - additive APIs для richer interactions only where justified.
- `Headless` / `FlaUI`
  - execution support for new structured selectors and interaction surfaces.

## 9. Изменения модели данных / состояния
- Runtime state:
  - `RecorderRootGraph`
  - `PendingValueChanges`
  - `SaveOperationState`
  - `StepReviewState`
- Internal recorded-step model расширяется:
  - `RecordedItemSelector?`
  - `IsIgnored`
  - `ReviewState`
  - `FailureCode`
  - `LastValidationAt`
- Persisted C# output:
  - остаётся page partial + scenario partial;
  - statement generation может использовать richer overloads/DSL helpers для structured item selectors.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - `P0`: dynamic observation, text capture hardening, single-flight save/export, overlay review baseline;
  - `P1`: structured item selectors, live validation root graph, control adapters;
  - `P2`: popup/menu/modal/grid/search-like coverage и step editing maturity.
- Совместимость:
  - текущие recorded partials остаются валидными;
  - additive overloads/helpers предпочтительнее переписывания existing generated syntax;
  - warning-only fallback paths могут сосуществовать с old behavior на transition period.
- Rollback:
  - `P0` mostly локализован в recorder package и overlay;
  - `P1/P2` затрагивают abstractions/runtime contracts и должны внедряться отдельными коммитами/этапами.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - dynamic controls и lazy content начинают записываться без reattach recorder-а;
  - delete/paste/context-menu text edits попадают в recorded text step;
  - item-based selection не опирается только на `ToString()` при наличии более стабильного selector-а;
  - save/export не гоняются параллельно и имеют видимый busy/result state;
  - overlay показывает step journal и даёт убрать/ignore invalid step без full reset;
  - selector validation работает против live root graph, а не stale root snapshot;
  - menu/popup/modal coverage добавляется только вместе с execution support и regression suite.
- Какие тесты добавить/изменить:
  - `tests/AppAutomation.Recorder.Avalonia.Tests`
    - late attach / dynamic control subscribe
    - root swap validation
    - delete-only and paste text capture
    - duplicate item captions and selector-grade outcome
    - single-flight save/export
    - overlay step remove/ignore/revalidate
  - `tests/AppAutomation.Abstractions.Tests`
    - richer item selector helpers and failure modes
  - runtime tests:
    - headless/flaui item selection by `AutomationId` / ordinal / custom key
    - popup/menu/modal interaction support
  - solution regressions:
    - existing recorder and authoring tests stay green
- Команды для проверки:
  - `dotnet build AppAutomation.sln -c Debug`
  - `dotnet test tests/AppAutomation.Recorder.Avalonia.Tests/AppAutomation.Recorder.Avalonia.Tests.csproj -c Debug`
  - `dotnet test tests/AppAutomation.Abstractions.Tests/AppAutomation.Abstractions.Tests.csproj -c Debug`
  - `dotnet test sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/DotnetDebug.AppAutomation.Avalonia.Headless.Tests.csproj -c Debug`
  - `dotnet test sample/DotnetDebug.AppAutomation.FlaUI.Tests/DotnetDebug.AppAutomation.FlaUI.Tests.csproj -c Debug`
  - `dotnet test AppAutomation.sln -c Debug`

## 12. Риски и edge cases
- Dynamic observation легко превратить в event storm и memory leak, если detach/reattach lifecycle будет неполным.
- Text capture hardening может случайно начать писать app-driven updates как user-driven, если heuristics окажутся слишком широкими.
- Structured item selectors потребуют аккуратного contract design, иначе runtime-ы разойдутся по semantics.
- Overlay step journal легко перегрузить и превратить в noisy UI; нужен intentional минимализм.
- Popup/menu/modal support потребует точного определения, какие top-levels считаются частью AUT, а какие нет.

## 13. План выполнения
1. Реализовать live root/control observation и убрать зависимость от one-shot subscribe.
2. Перевести text capture на value-change pipeline с корректным flush semantics.
3. Ввести single-flight save/export и busy/result UX.
4. Добавить overlay step journal с базовыми review actions.
5. Спроектировать structured item selectors и runtime support для list/combo/tree-like controls.
6. Ввести interaction/control adapters для richer composite controls.
7. Расширить coverage на popup/menu/modal/grid/search-like flows.
8. Добить regression suite и documentation updates.

## 14. Открытые вопросы
- Нет блокирующих открытых вопросов для перехода в `EXEC`.
- Осознанно оставлено на последующие итерации:
  - полноценный visual-tree inspector;
  - unstable persisted selectors;
  - gesture parity без execution contract.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-feature-parity`
- Выполненные требования профиля:
  - перечислены реальные UX/coverage gaps текущего recorder-а;
  - выбран phased uplift path с приоритетами `P0/P1/P2`;
  - сохранены desktop-specific ограничения по root graph, popup/dialog lifecycle и UI-thread driven changes;
  - зафиксированы acceptance criteria и regression matrix для обеих runtime-реализаций.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs` | dynamic observation, save coordinator, review-state orchestration | убрать silent-loss и race conditions |
| `src/AppAutomation.Recorder.Avalonia/RecorderSelectorResolver.cs` | live root graph + richer validation | снизить ложные invalid и повысить trust |
| `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs` | structured item selectors + control adapters | убрать fragile `ToString()`-based recording |
| `src/AppAutomation.Recorder.Avalonia/UI/*` | overlay v3 with step journal | превратить overlay в review tool |
| `src/AppAutomation.Abstractions/*` | additive richer selector/interaction helpers | поддержать честное execution API |
| `src/AppAutomation.Avalonia.Headless/*` | runtime support for new selectors/interactions | сохранить headless parity |
| `src/AppAutomation.FlaUI/*` | runtime support for new selectors/interactions | сохранить desktop parity |
| `tests/AppAutomation.Recorder.Avalonia.Tests/*` | new reliability/UX tests | защитить ключевые recorder flows |
| `tests/AppAutomation.Abstractions.Tests/*` | selector and helper tests | проверить public/additive contracts |
| `README.md` | UX/coverage guidance | зафиксировать новый workflow для consumer-ов |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Подписки на controls | один проход по tree на `Loaded` | live root/control observation |
| Text capture | `TextInputEvent`-driven | value-change + flush pipeline |
| Item identity | display text only | structured item selector |
| Save/export | fire-and-forget | single-flight with busy/result UX |
| Overlay | last status + last preview | step journal + review actions |
| Popup/menu/modal | в основном вне recorder-а | phased additive coverage |

## 18. Альтернативы и компромиссы
- Вариант: оставить recorder как есть и закрывать gaps точечно.
  - Плюсы:
    - минимальный объём изменений.
  - Минусы:
    - UX деградирует по мере роста AUT;
    - silent-loss остаётся незаметным;
    - coverage gaps продолжают копиться ad-hoc исключениями.
  - Почему выбранное решение лучше:
    - оно системно усиливает recorder как инструмент authoring-а, а не набор специальных кейсов.
- Вариант: быстро добавить unstable fallbacks вроде item index / tree path "лишь бы записать".
  - Плюсы:
    - больше nominal coverage здесь и сейчас.
  - Минусы:
    - резко падает доверие к generated tests;
    - user experience становится хуже, потому что шаги будут чаще "записываться", но ломаться позже.
  - Почему выбранное решение лучше:
    - quality-first contract важнее формального growth по числу поддержанных controls.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и границы follow-up scope определены |
| B. Качество дизайна | 6-10 | PASS | Выбраны конкретные design paths для dynamic observation, text capture, overlay v3 и structured item selectors |
| C. Безопасность изменений | 11-13 | PASS | Rollout разбит по приоритетам, additive API path и rollback-риск зафиксированы |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria, regression matrix и команды проверки заданы |
| E. Готовность к автономной реализации | 17-19 | PASS | План пошаговый, блокирующих вопросов не осталось |
| F. Соответствие профилю | 20 | PASS | Выполнены требования `dotnet-desktop-client` и `ui-feature-parity` |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Явно отделено улучшение reliability/UX/coverage от unstable fallback-ов и DSL drift |
| 2. Понимание текущего состояния | 5 | Зафиксированы реальные behavioural gaps текущей реализации после parity-итерации |
| 3. Конкретность целевого дизайна | 5 | Для root observation, value capture, review overlay и selector durability выбран конкретный design path |
| 4. Безопасность (миграция, откат) | 5 | Public API changes ограничены additive path, rollout разбит по этапам |
| 5. Тестируемость | 5 | Acceptance criteria и test matrix привязаны к ключевым UX/coverage risks |
| 6. Готовность к автономной реализации | 5 | Спека годится как backlog blueprint для следующего EXEC цикла |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - отделён новый UX/coverage scope от уже завершённой parity-итерации;
  - сохранена philosophy stable contracts и additive API expansion;
  - coverage expansion привязана к execution support, а не к nominal "умеем записать хоть как-то".
- Что осталось на решение пользователя:
  - ничего блокирующего; требуется только явное подтверждение для перехода в `EXEC`.

### Post-EXEC Review
- Статус: PASS для выполненного `P0`-среза
- Что реализовано:
  - `RecorderSession` переведён с one-shot `Loaded` subscribe на live refresh наблюдение за control graph внутри актуального root;
  - text capture теперь опирается на `TextBox.Text` change pipeline с flush по focus / save / stop / переключению на другой control;
  - `Save` / `Export...` переведены в single-flight coordinator с busy summary и блокировкой повторного запуска;
  - overlay получил session summary, step journal и review-действия `Remove / Ignore / Retry / Copy`;
  - host-window recorder-а отвязан от окна AUT и переведён в отдельное непрозрачное standalone окно с обычными window decorations;
  - standalone recorder window теперь поддерживает ручной resize/maximize, overlay показывает текущий scenario file path, а click/selection capture переведён на actionable-owner resolution вместо сырого `e.Source`;
  - immediate add-step path теперь повторно прогоняет revalidation, поэтому invalid action/locator сочетания становятся видны сразу, а не только после `Retry`;
  - README и regression suite обновлены под новый recorder workflow.
- Что осознанно осталось на следующий `EXEC`-срез:
  - structured item selectors для combo/list/tree-like controls;
  - live validation root graph за пределами owner content root;
  - adapters и popup/menu/modal/grid/search-like coverage.
- Почему это приемлемо:
  - `P0` закрывает главные UX/reliability regressions recorder-а и даёт безопасный baseline для следующего расширения surface area;
  - полный regression прогон по solution зелёный, поэтому выполненный срез можно принимать независимо от следующих фаз roadmap-а.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `SPEC` | `review-followup` / UX и coverage hardening recorder-а | `0.92` | Нужно было отделить architectural backlog от уже закрытого parity scope | Сформировать самостоятельную spec с phased uplift path | `Нет` | `Нет` | Findings из review требуют отдельной итерации, иначе они смешаются с уже выполненным parity work | `src/AppAutomation.Recorder.Avalonia/*`, `src/AppAutomation.Abstractions/*`, `src/AppAutomation.Avalonia.Headless/*`, `src/AppAutomation.FlaUI/*` |
| `SPEC` | Маршрутизация `QUEST` и выбор профиля | `0.95` | Блокирующих неизвестных не осталось | Запросить подтверждение спеки | `Да` | `Да, ожидается фраза "Спеку подтверждаю"` | Для этой задачи снова выбран профиль `dotnet-desktop-client + ui-feature-parity`, потому что речь о desktop recorder UX и полноте покрытия сценариев | `C:\Projects\My\Agents\instructions\*`, `specs/2026-04-08-appautomation-recorder-ux-coverage-hardening.md` |
| `EXEC` | `P0` reliability/UX uplift recorder-а | `0.87` | Для полного parity roadmap ещё не реализованы structured item selectors и popup/menu/modal coverage | Закрыть dynamic observation, value-based text capture, single-flight save/export и review-first overlay, затем прогнать regression suite | `Нет` | `Да, пользователь подтвердил переход фразой "спеку подтверждаю"` | Главный риск recorder-а был в silent-loss и слабом review flow, поэтому сначала закрыт `P0`, который даёт безопасную базу под дальнейшие additive расширения | `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderSelectorResolver.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`, `src/AppAutomation.Recorder.Avalonia/UI/*`, `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorder.cs`, `README.md` |
| `EXEC` | Standalone opaque recorder host window | `0.95` | Нужно было только удостовериться, что изменение не ломает recorder overlay workflow и корректно документировано | Отвязать recorder window от owner-state/position, зафиксировать standalone configuration и прогнать targeted recorder tests | `Нет` | `Да, пользователь явно запросил отвязать окно recorder-а от окна приложения` | Старое overlay-host поведение мешало usability: окно ездило вместе с AUT и было прозрачным. Для UX recorder-а лучше отдельное независимое окно | `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorder.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, `README.md`, `specs/2026-04-08-appautomation-recorder-ux-coverage-hardening.md` |
| `EXEC` | Recorder usability and capture hardening follow-up | `0.93` | Для полного parity roadmap всё ещё не закрыты structured item selectors и popup/menu/modal/grid coverage | Добавить resize/maximize для standalone окна, показать scenario path, починить click/selection capture и выровнять immediate validation с `Retry` | `Нет` | `Да, пользователь перечислил пять конкретных UX/recording gaps` | Эти правки уже относятся к quality-of-life и trustworthiness recorder-а: без них окно неудобно использовать, а captured steps выглядят нестабильными и непредсказуемыми | `src/AppAutomation.Recorder.Avalonia/AppAutomationRecorder.cs`, `src/AppAutomation.Recorder.Avalonia/RecorderSession.cs`, `src/AppAutomation.Recorder.Avalonia/UI/*`, `src/AppAutomation.Recorder.Avalonia/CodeGeneration/AuthoringCodeGenerator.cs`, `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` |
| `EXEC` | Regression и финальная валидация | `0.95` | Для последующих фаз roadmap нужны отдельные runtime contract changes, но для текущего среза данных достаточно | Зафиксировать targeted + full solution checks и завершить отчёт | `Нет` | `Нет` | P0 не должен был ломать существующие authoring/headless/flaui/test-host потоки, поэтому прогнаны recorder tests, solution build и полный solution test | `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs`, `README.md`, `AppAutomation.sln` |
