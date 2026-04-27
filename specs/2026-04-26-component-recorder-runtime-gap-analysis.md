# Ревизия компонентных исходников: gaps recorder / headless / FlaUI

## 0. Метаданные
- Тип (профиль): `ui-automation-testing`
- Владелец: Codex
- Масштаб: medium
- Целевой релиз / ветка: `feat-arm-paritet`
- Ограничения:
  - Изменяется только этот отдельный файл.
  - Анализ основан на статическом чтении исходников в `.tmp_eremex_controls` и `.tmp_notification_src`.
  - Runtime UIA-поведение не подтверждалось запуском живого приложения.
- Связанные ссылки:
  - `.tmp_eremex_controls`
  - `.tmp_notification_src`
  - `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`
  - `src/AppAutomation.Avalonia.Headless/Automation/HeadlessControlResolver.cs`
  - `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs`

## 1. Overview / Цель
Понять по исходникам компонентных библиотек, какие контролы:

1. сейчас не будут корректно захватываться `AppAutomation.Recorder`;
2. не имеют корректной runtime-поддержки в `Headless` и `FlaUI`;
3. требуют отдельных доработок для полного покрытия recorder-сценариями и UI-тестами.

Результат оформлен как backlog-артефакт в отдельном файле, без изменения runtime-кода.

## 2. Текущее состояние (AS-IS)
- Recorder классифицирует в первую очередь стандартные Avalonia-типы: `Button`, `TextBox`, `ComboBox`, `ListBox`, `TabItem`, `TreeView`, `Calendar`, `DatePicker`; всё остальное попадает в `UiControlType.AutomationElement` (`src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs:728`).
- Headless и FlaUI resolvers имеют явные typed-path только для стандартных контролов и `Grid`; иначе возвращают generic wrapper (`src/AppAutomation.Avalonia.Headless/Automation/HeadlessControlResolver.cs:36-57`, `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs:40-61`).
- В абстракциях уже есть composite adapters для `SearchPicker`, date/numeric range filter, dialog, notification, folder export, shell navigation и typed workflow для editable/user-action grid (`src/AppAutomation.Abstractions/UiControlAdapters.cs`, `src/AppAutomation.Abstractions/UiPageExtensions.cs`).
- Временные исходники компонентов показывают, что существенная часть Eremex/Notification UI построена не на прямых Avalonia primitives, а на `TemplatedControl`, `ItemsControl` и composite wrappers:
  - `BaseEditor`, `TextEditor`, `ButtonEditor`, `PopupEditor`, `ComboBoxEditor`, `DateEditor`, `SpinEditor`;
  - `DataGridControl`, `TreeListControl`, `PropertyGridControl`, `ListViewControl`;
  - `DockManager`, `Toolbar`, `PopupMenu`, `RibbonControl`;
  - `MxSplitButton`, `NotificationMessage`, `NotificationMessageContainer`.
- В компонентных исходниках не найдено собственной явной разметки `AutomationProperties.AutomationId` / `AutomationProperties.Name`; внутренний контракт в основном опирается на template-part names (`PART_RealEditor`, `PART_PopupOpenButton`, `PART_ItemsSelector`, `PART_SearchEditor`, `PART_CellEditor`, `PART_RowPanel`, `PART_GroupPanel`, `PART_SearchControl`, `PART_VirtualizingControl`, `PART_HeadersControl`, `PART_AutoFilterRow`).

## 3. Проблема
Исходники компонентов показывают большой слой custom/composite controls, для которых текущая поддержка AppAutomation либо:
- сводится к generic `AutomationElement`,
- либо работает только через app-specific hints/bridges,
- либо вообще не имеет корректной runtime-модели в `Headless` и `FlaUI`.

Без отдельного списка доработок нельзя планомерно закрыть recorder и UI-test coverage для этих компонентных семейств.

## 4. Цели дизайна
- Явно отделить контролы, которые уже можно автоматизировать как стандартные Avalonia subclasses, от контролов, требующих новых adapters/bridges.
- Разделить проблемы на два слоя:
  - `recorder capture`;
  - `headless/flaui runtime`.
- Зафиксировать только проверяемые выводы из исходников и существующего AppAutomation-кода.
- Сформировать backlog доработок, пригодный для последующей реализации и тестирования.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем runtime-код AppAutomation.
- Не меняем исходники Eremex/Notification компонентов.
- Не утверждаем, что template `Name` гарантированно превращается в стабильный UIA `Name` в живом FlaUI runtime.
- Не проектируем финальный публичный API новых контролов в деталях; здесь только список необходимых направлений.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `docs/appautomation/component-coverage-gaps.md` -> чистый пользовательский backlog-отчёт без spec-обвязки.
- Этот файл -> рабочая спецификация, audit trail и журнал выполнения.
- Будущие runtime-задачи -> отдельные implementation specs и тесты.

### 6.2 Детальный дизайн
Ниже используются статусы:
- `OK with explicit ids` -> уже может работать, если приложению дать стабильные ids.
- `Recorder gap` -> recorder захватывает не ту семантику или только generic element.
- `Runtime gap` -> в `Headless` и/или `FlaUI` нет корректной модели контрола без новых adapters/bridges.

## 7. Компонентная матрица gaps

| Семейство компонентов | Примеры / факт из исходников | Recorder сегодня | Headless сегодня | FlaUI сегодня | Вывод |
| --- | --- | --- | --- | --- | --- |
| Прямые наследники стандартных Avalonia controls | `MxTabControl : TabControl`, `MxTabItem : TabItem`, `CalendarControl : Calendar`, `NotificationMessageButton : Button`, `ToolbarRadioButton : RadioButton`, `ToolbarCheckBox : CheckBox` | В целом корректно, если событие приходит с самого наследника и у него есть стабильный locator | Обычно корректно: inheritance попадает в typed resolver | Вероятно корректно для primitive path, если UIA отдаёт ожидаемый control type | Это не главная проблема; нужны только characterization-tests и стабильные ids |
| `BaseEditor` -> `TextEditor` -> `ButtonEditor` -> `PopupEditor` -> `ComboBoxEditor` / `DateEditor` / `SpinEditor` | `BaseEditor : TemplatedControl`, внутри `PART_RealEditor`; `PopupEditor` держит `PART_PopupOpenButton`; popup list живёт через `PART_ItemsSelector` | `Recorder gap`: root-контрол будет generic/custom, а корректное действие живёт на внутренних parts; автоматическое распознавание отсутствует | Частично: можно дотянуться до внутренних Avalonia parts только если они имеют стабильные ids или надёжный `Name`; root-семантика отсутствует | Некорректно как общий случай: template-part names сами по себе не являются надёжным UIA-контрактом; без ids и adapters нет правильного replay | Нужен editor-component contract и adapters над внутренними parts |
| `MxSplitButton` | `MxSplitButton : ContentControl`, `PART_PrimaryButton`, `PART_PopupOpenButton` | `Recorder gap`: root-клик и primary/dropdown action имеют разную семантику, но типизированного split-button path нет | Runtime gap на уровне root-семантики; primitive children можно трогать только через app-specific mapping | Runtime gap на уровне root-семантики; без отдельных ids на primary/dropdown корректный replay нестабилен | Нужен composite split-button adapter и recorder recognizer |
| Notification root/container | `NotificationMessage : TemplatedControl`, `NotificationMessageContainer : ItemsControl`, `NotificationMessageButton : Button` | `Recorder gap`: автоматическое распознавание notification-root не появится без hints/parts | Частично: existing `NotificationControlAdapter` можно использовать, но только если app даст стабильные text/dismiss anchors | Частично: тот же adapter path, но без стабильных UIA anchors корректной поддержки нет | Нужен стандартный notification automation contract для root/text/close/button items |
| `DataGridControl` | `DataGridControl : DataControlBase`, template parts `PART_VirtualizingControl`, `PART_HeadersControl`, `PART_AutoFilterRow`, `PART_GroupPanel` | `Recorder gap`: без `GridHint`/bridge захватится root/generic element, а не rows/cells/actions | Runtime gap: typed support в resolver есть только для Avalonia `DataGrid`; native Eremex grid rows/cells/actions не моделируются корректно | Runtime gap: resolver умеет `GridPattern` и visual-grid bridge, но не native Eremex row/cell/action model | Без bridge или новых native adapters корректного покрытия нет |
| `TreeListControl` | `TreeListControl : TreeListControlBase`; сочетает tree, columns, filters, editors | `Recorder gap`: нет корректной tree-grid capture semantics | Runtime gap: нет tree-grid model, node/cell/edit/filter path отсутствует | Runtime gap: нет tree-grid model, node/cell/edit/filter path отсутствует | Требуется отдельная `TreeGrid`/`TreeList` abstraction или явный automation bridge |
| `PropertyGridControl` | `PropertyGridControl : TemplatedControl`, parts `PART_VirtualizingControl`, `PART_SearchControl`; строки имеют `PART_CellEditor`, `PART_RowPanel` | `Recorder gap`: root/row/editor не распознаются как property-grid semantics | Runtime gap: нет typed model для property rows/categories/editors | Runtime gap: нет typed model для property rows/categories/editors | Требуется отдельный property-grid adapter или reusable bridge |
| `ListViewControl` | `ListViewControl : TemplatedControl`; используется и внутри ribbon popup gallery | `Recorder gap`: root не попадает в `ListBox`; item semantics app-specific | Runtime gap: headless typed list support завязан на Avalonia `ListBox`, не на custom list view | Runtime gap: без native UIA list/item pattern или explicit bridge корректной модели нет | Нужен adapter `ListViewControl -> IListBoxControl/selection` или отдельный typed contract |
| Command surfaces: `Toolbar`, `ToolbarItem`, `PopupMenu`, `RibbonControl` | Корни на `TemplatedControl`; меню/toolbar item hierarchy живёт во внутреннем item model | `Recorder gap`: root-захват generic, пункт меню/toolbar item не мапится автоматически в user intent | Runtime gap: нет typed menu/toolbar/ribbon navigation model; только доступ к внутренним children при хорошем contract | Runtime gap: нет typed menu/toolbar/ribbon navigation model; overflow/popup/customization paths не описаны | Нужна отдельная command-surface abstraction или набор adapters поверх menu item / popup item / button item |
| Shell / docking | `DockManager : TemplatedControl`; float/overlay/document windows вынесены в custom controls/windows | `Recorder gap`: docking gesture и pane semantics не распознаются как `ShellNavigation` автоматически | Runtime gap: существующий `ShellNavigation` adapter требует стабильный tree/list/tab contract, а сам `DockManager` такого typed contract не даёт | Runtime gap: drag/floating/pinning/layout persistence без специального provider не покрываются корректно | Нужен app-level shell contract поверх стабильных pane tabs/tree/list, отдельно от drag-docking |
| `MxWindow`, message/popup windows | `MxWindow : Window`, `MessageWindow : MxWindow`, `PopupContainer` держит `PART_CloseButton` | Частично: recorder может захватить внутренние buttons, но не root-dialog semantics без hints | Частично: existing dialog adapter применим при стабильных parts | Частично: existing dialog adapter применим при стабильных UIA parts | Тут не нужен новый core control type; нужен единый dialog contract |

## 8. Что именно не будет захватываться рекордером правильно

1. Весь `BaseEditor` family как root-control: recorder классифицирует только стандартные Avalonia types, а `TemplatedControl`-корень редактора остаётся generic.
2. `MxSplitButton`: recorder не различает primary click и dropdown-open как две разные user actions.
3. `NotificationMessage` и `NotificationMessageContainer`: без hints нет automatic notification-root/dismiss capture.
4. `DataGridControl`, `TreeListControl`, `PropertyGridControl`, `ListViewControl`: recorder не извлекает из root корректные row/item/editor semantics без bridge или app-specific hint layer.
5. `Toolbar`, `PopupMenu`, `RibbonControl`, `DockManager`: generic root capture не соответствует реальному пользовательскому intent (`open menu`, `choose command`, `activate pane`, `toggle popup`, `switch document`).

## 9. Для каких контролов сейчас вообще нет корректной поддержки в Headless и FlaUI

### 9.1 Обе runtime-модели сейчас некорректны без специальных доработок
- `DataGridControl`
- `TreeListControl`
- `PropertyGridControl`
- `ListViewControl`
- `Toolbar` / `ToolbarItem` command hierarchy
- `PopupMenu`
- `RibbonControl`
- `DockManager`

### 9.2 Корректная поддержка возможна только как composite adapter поверх стабильных parts
- `BaseEditor` / `TextEditor` / `ButtonEditor`
- `PopupEditor` / `ComboBoxEditor` / `DateEditor` / `SpinEditor`
- `MxSplitButton`
- `NotificationMessage` / `NotificationMessageContainer`
- `MxWindow` / `MessageWindow` / `PopupContainer`

## 10. Список доработок для полного покрытия recorder и UI tests

### 10.1 Контракт автоматизации компонентов
1. Ввести единый component-automation contract для custom Eremex/Notification controls: обязательные `AutomationProperties.AutomationId` на root и на все scenario-facing template parts.
2. Для composite editors закрепить стабильные ids на:
   - root editor;
   - `PART_RealEditor`;
   - popup-open button;
   - popup list / popup content;
   - apply/cancel/footer buttons.
3. Для `MxSplitButton` закрепить отдельные ids на primary-action и dropdown-action.
4. Для notification/dialog surfaces закрепить ids на root, text/content, close/dismiss, action buttons.

### 10.2 Recorder
1. Добавить semantic recognizers для `BaseEditor` family, чтобы root Eremex editor автоматически мапился не в `AutomationElement`, а в правильный typed/composite workflow.
2. Добавить composite recognizer для `MxSplitButton` с двумя action kinds: `ClickPrimary` и `OpenDropdown`.
3. Расширить recorder hints/aliases для:
   - `NotificationMessage`;
   - `MxWindow` / `MessageWindow`;
   - `Toolbar` / `PopupMenu` / `Ribbon` items;
   - `SearchPanel` / column chooser / popup editors.
4. На unsupported Eremex roots выдавать детерминированный diagnostic, а не сохранять misleading generic step.

### 10.3 Headless runtime
1. Добавить adapter layer для `BaseEditor` family:
   - read/write через `PART_RealEditor`;
   - popup open/close;
   - list selection;
   - date/spin commit/cancel.
2. Добавить typed support или bridges для `DataGridControl`, `TreeListControl`, `PropertyGridControl`, `ListViewControl`.
3. Добавить command-surface adapters для `Toolbar`, `PopupMenu`, `RibbonControl`.
4. Добавить shell-navigation adapter поверх `DockManager`, но только для stable pane activation; drag/floating/layout persistence оставить отдельным уровнем.

### 10.4 FlaUI runtime
1. Повторить тот же adapter contract, но поверх реального UIA tree и стабильных `AutomationId`.
2. Для data/tree/property/list surfaces использовать bridge-first стратегию, пока native UIA patterns не доказаны тестами.
3. Для toolbar/ribbon/popup/docking добавить runtime diagnostics:
   - если provider не отдаёт нужный pattern,
   - шаг должен помечаться как unsupported/warning, а не считаться рабочим.

### 10.5 Тестовое покрытие
1. Добавить characterization tests на прямые subclasses стандартных контролов:
   - `MxTabControl`, `MxTabItem`, `CalendarControl`, `NotificationMessageButton`, `ToolbarRadioButton`, `ToolbarCheckBox`.
2. Добавить integration tests для editor family:
   - text input;
   - popup open;
   - combo/list select;
   - date set;
   - spin set;
   - commit/cancel.
3. Добавить отдельные smoke scenarios для:
   - `MxSplitButton`;
   - notification dismiss/assert;
   - dialog confirm/cancel;
   - toolbar/menu/ribbon command selection.
4. Для `DataGridControl`, `TreeListControl`, `PropertyGridControl`, `ListViewControl` сначала добавить bridge/adapter contract tests, потом runtime smoke tests в headless и FlaUI.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Этот файл содержит отдельную классификацию gaps по компонентным семействам.
  - Отдельно перечислены recorder-capture issues.
  - Отдельно перечислены контролы без корректной поддержки в `Headless` и `FlaUI`.
  - Список доработок пригоден как backlog для следующих implementation-задач.
- Тесты кода не запускались: задача документальная, runtime-код не менялся.
- Команды для проверки:
  - `rg -n "ClassifyControlType|ResolveGrid|new HeadlessUiControl|new FlaUiControl" src`
  - `rg -n "public class (BaseEditor|PopupEditor|DataGridControl|TreeListControl|PropertyGridControl|ListViewControl|DockManager|Toolbar|PopupMenu|RibbonControl|NotificationMessage)" .tmp_eremex_controls .tmp_notification_src -g "*.cs"`
  - `rg -n "PART_RealEditor|PART_PopupOpenButton|PART_ItemsSelector|PART_SearchEditor|PART_CellEditor|PART_GroupPanel" .tmp_eremex_controls .tmp_notification_src -g "*.cs"`

## 12. Риски и edge cases
- Некоторые derived controls могут в реальном UIA tree выглядеть лучше или хуже, чем следует из статического чтения исходников.
- Наличие template part name не гарантирует стабильный UIA locator в FlaUI.
- Для Eremex data/tree/property/list surfaces без живого runtime нельзя утверждать точный native UIA pattern; поэтому backlog ориентирован на bridge-first подход.

## 13. План выполнения
1. Собрать факты по текущей классификации recorder/headless/FlaUI.
2. Сгруппировать компонентные исходники по automation-risk.
3. Отделить `recorder gap` от `runtime gap`.
4. Сформировать список доработок в отдельном файле.

## 14. Открытые вопросы
Нет блокирующих вопросов для этого документального артефакта.

## 15. Соответствие профилю
- Профиль: `ui-automation-testing`
- Выполненные требования профиля:
  - результат сфокусирован на существующем UI automation contract;
  - явно выделены нужные future UI tests;
  - сформирован backlog для headless/FlaUI coverage.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-04-26-component-recorder-runtime-gap-analysis.md` | Рабочая спецификация и журнал выполнения | QUEST gate и audit trail |
| `docs/appautomation/component-coverage-gaps.md` | Отдельный пользовательский backlog-отчёт | Зафиксировать список компонентных gaps и доработок вне `specs/` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Компонентные исходники в worktree | Есть, но без пользовательского backlog-отчёта | Есть отдельный отчёт в `docs/appautomation` с классификацией gaps и списком доработок |
| Граница проблем | Разрозненные наблюдения | Явно отделены `recorder gap` и `runtime gap` |

## 18. Альтернативы и компромиссы
- Вариант: перечислить каждый класс отдельно.
  - Плюсы: максимальная полнота.
  - Минусы: шумный документ, плохо приоритизируется.
  - Почему не выбран: для automation roadmap полезнее группировка по семействам и типу поддержки.
- Вариант: ограничиться только `Arm.Srv` usage.
  - Плюсы: ближе к одному consumer app.
  - Минусы: не закрывает вопрос пользователя про исходники самих компонентов.
  - Почему не выбран: задача явно просила пройтись по исходникам компонентов из worktree.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Есть компонентная матрица, backlog доработок и границы вывода. |
| C. Безопасность изменений | 11-13 | PASS | Артефакт документальный, риски и ограничения анализа описаны. |
| D. Проверяемость | 14-16 | PASS | Есть критерии приёмки и команды проверки. |
| E. Готовность к автономной реализации | 17-19 | PASS | План и отсутствие блокирующих вопросов зафиксированы. |
| F. Соответствие профилю | 20 | PASS | Результат ориентирован на UI automation/testing backlog. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Границы анализа и ожидаемый артефакт определены. |
| 2. Понимание текущего состояния | 5 | Текущая модель recorder/headless/FlaUI и компонентные исходники сопоставлены. |
| 3. Конкретность целевого дизайна | 5 | Есть матрица gaps и приоритизированный список доработок. |
| 4. Безопасность (миграция, откат) | 5 | Runtime-код не меняется. |
| 5. Тестируемость | 5 | Есть команды проверки и будущий тестовый backlog. |
| 6. Готовность к автономной реализации | 5 | Следующие implementation-задачи можно декомпозировать напрямую из документа. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: разделены primitive subclasses и реальные composite/runtime gaps; явно добавлен bridge-first вывод для data/tree/property/list surfaces.
- Что осталось на решение пользователя: нет.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: из рабочей спеки вынесен чистый backlog-отчёт в `docs/appautomation/component-coverage-gaps.md`; рабочая спека обновлена до фактического EXEC-результата.
- Что проверено дополнительно для refactor / comments: runtime-код не менялся; проверена только согласованность markdown-артефактов.
- Остаточные риски / follow-ups: выводы по native UIA всё ещё основаны на статическом анализе исходников компонентов и должны подтверждаться runtime smoke-тестами.
- Команды проверки:
  - `rg -n "What recorder will not capture correctly today|What has no correct Headless and FlaUI support today|Required work items|Что recorder сейчас не захватывает корректно|Для чего сейчас нет корректной поддержки" docs/appautomation/component-coverage-gaps.md`
  - `git diff --check -- docs/appautomation/component-coverage-gaps.md specs/2026-04-26-component-recorder-runtime-gap-analysis.md`

## Approval
Подтверждено пользователем фразой: `Спеку утверждаю`

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Маршрутизация инструкций и сбор контекста | 0.97 | Нет runtime UIA-подтверждения | Сопоставить компонентные исходники с AppAutomation runtime | Нет | Нет | Документальная задача допускает статический анализ, но требует явных ограничений | `C:\Projects\My\Agents\*`, `specs/2026-04-26-component-recorder-runtime-gap-analysis.md` |
| SPEC | Анализ recorder/headless/FlaUI границ | 0.95 | Поведение живого UIA дерева | Сгруппировать контролы по типу gap | Нет | Нет | Ключевая граница проходит по `TemplatedControl`/custom roots против standard Avalonia subclasses | `src/AppAutomation.Recorder.Avalonia/*`, `src/AppAutomation.Avalonia.Headless/*`, `src/AppAutomation.FlaUI/*` |
| SPEC | Анализ исходников компонентов | 0.94 | Нет live runtime | Оформить backlog доработок | Нет | Нет | Исходники Eremex/Notification достаточно явно показывают template-part contract и отсутствие явной automation-разметки | `.tmp_eremex_controls`, `.tmp_notification_src` |
| SPEC | Подготовка рабочей спеки | 0.98 | Подтверждение пользователя для выхода в EXEC | Ожидать подтверждение спеки | Да | Да, пользователь подтвердил спеку | Центральный QUEST-процесс требует явного перехода из SPEC в EXEC | `specs/2026-04-26-component-recorder-runtime-gap-analysis.md` |
| EXEC | Подготовка пользовательского deliverable | 0.98 | Нет live runtime | Вынести backlog-результат в `docs/appautomation` | Нет | Да, пользователь подтвердил спеку | Отдельный файл в `docs` лучше соответствует запросу пользователя, чем рабочая спека | `docs/appautomation/component-coverage-gaps.md`, `specs/2026-04-26-component-recorder-runtime-gap-analysis.md` |
| EXEC | Sanity-проверка markdown-артефактов | 0.99 | Нет | Проверить поиском и `git diff --check`, затем завершить задачу | Нет | Нет | Документальная задача не требует тестового прогона, но требует убедиться в согласованности файлов | `docs/appautomation/component-coverage-gaps.md`, `specs/2026-04-26-component-recorder-runtime-gap-analysis.md` |
