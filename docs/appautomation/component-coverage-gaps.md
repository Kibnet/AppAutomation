# AppAutomation Component Coverage Gaps

**English** | [Русский](#русская-версия)

This document summarizes gaps found in the component source snapshots that are present in the current worktree:

- `.tmp_eremex_controls`
- `.tmp_notification_src`

The analysis compares those component families against the current AppAutomation surfaces:

- recorder classification in `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`
- headless runtime in `src/AppAutomation.Avalonia.Headless/Automation/HeadlessControlResolver.cs`
- FlaUI runtime in `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs`

## 1. Main conclusion

The current AppAutomation stack is strong on standard Avalonia controls and on composed workflows that already have explicit adapters (`SearchPicker`, range filters, dialog, notification, folder export, shell navigation, editable grid requests).

The main gaps are custom/composite component roots that are implemented as `TemplatedControl`, `ItemsControl` or complex wrappers. Those controls are not discovered with the right semantics automatically, and many of them do not have a correct native runtime model in either `Headless` or `FlaUI`.

## 2. What recorder will not capture correctly today

| Component family | Source evidence | Why recorder is wrong today |
| --- | --- | --- |
| `BaseEditor` family | `BaseEditor`, `TextEditor`, `ButtonEditor`, `PopupEditor`, `ComboBoxEditor`, `DateEditor`, `SpinEditor` | Recorder classifies standard Avalonia controls first and falls back to generic `AutomationElement` for custom roots. The actual user action lives in inner parts like `PART_RealEditor`, `PART_PopupOpenButton`, `PART_ItemsSelector`. |
| `MxSplitButton` | `MxSplitButton : ContentControl`, `PART_PrimaryButton`, `PART_PopupOpenButton` | Recorder does not distinguish primary action from dropdown-open action. |
| `NotificationMessage` / `NotificationMessageContainer` | `NotificationMessage : TemplatedControl`, `NotificationMessageContainer : ItemsControl` | Without explicit hints or ids, recorder cannot infer notification-root semantics or dismiss flow automatically. |
| `DataGridControl` | `DataGridControl : DataControlBase` | Root capture does not yield row/cell/action semantics unless a bridge or `GridHint` is configured. |
| `TreeListControl` | `TreeListControl : TreeListControlBase` | Recorder has no tree-grid capture model. |
| `PropertyGridControl` | `PropertyGridControl : TemplatedControl` | Recorder does not understand row/category/editor semantics from the root. |
| `ListViewControl` | `ListViewControl : TemplatedControl` | Recorder cannot treat it as a stable `ListBox`-like surface automatically. |
| `Toolbar`, `PopupMenu`, `RibbonControl`, `DockManager` | Command and docking surfaces are rooted in custom controls | Generic root capture does not match real user intent such as selecting a command, opening a popup, or activating a document pane. |

## 3. What has no correct Headless and FlaUI support today

### 3.1 Missing in both runtimes without new adapters or bridges

- `DataGridControl`
- `TreeListControl`
- `PropertyGridControl`
- `ListViewControl`
- `Toolbar` / `ToolbarItem` command hierarchy
- `PopupMenu`
- `RibbonControl`
- `DockManager`

### 3.2 Only feasible today through explicit composite-part mapping

- `BaseEditor` / `TextEditor` / `ButtonEditor`
- `PopupEditor` / `ComboBoxEditor` / `DateEditor` / `SpinEditor`
- `MxSplitButton`
- `NotificationMessage` / `NotificationMessageContainer`
- `MxWindow` / `MessageWindow` / `PopupContainer`

## 4. Control families that are lower risk

These are direct subclasses of standard Avalonia controls and should be much easier to cover once stable ids exist:

- `MxTabControl : TabControl`
- `MxTabItem : TabItem`
- `CalendarControl : Calendar`
- `NotificationMessageButton : Button`
- `ToolbarRadioButton : RadioButton`
- `ToolbarCheckBox : CheckBox`

They still need characterization tests, but they are not the primary architecture gap.

## 5. Required work items

### 5.1 Automation contract

1. Add explicit `AutomationProperties.AutomationId` to all scenario-facing custom component roots.
2. Add explicit ids to critical template parts:
   - editor root
   - `PART_RealEditor`
   - popup-open button
   - popup list/content
   - apply/cancel/footer buttons
   - dialog/notification text and action buttons
3. Give `MxSplitButton` separate ids for primary and dropdown actions.
4. For data-heavy controls, expose deterministic bridge ids instead of relying on implicit UIA behavior.

### 5.2 Recorder

1. Add semantic recognizers for the Eremex editor family so recorder maps the root to a typed/composite workflow instead of generic `AutomationElement`.
2. Add split-button recognition with distinct action kinds.
3. Extend recorder hints and aliases for notification, dialog, popup editor, toolbar/menu and shell surfaces.
4. Prefer explicit unsupported diagnostics over misleading generic recorded steps for custom roots.

### 5.3 Headless

1. Add editor adapters over internal parts for read/write, popup open/close, selection and commit/cancel.
2. Add typed support or bridge support for:
   - `DataGridControl`
   - `TreeListControl`
   - `PropertyGridControl`
   - `ListViewControl`
3. Add command-surface adapters for toolbar/menu/ribbon interactions.
4. Add a shell-navigation layer for pane activation on top of stable anchors, not raw drag-docking gestures.

### 5.4 FlaUI

1. Implement the same adapter contracts over real UIA plus explicit `AutomationId`.
2. Use bridge-first coverage for data/tree/property/list surfaces until native UIA patterns are proven by tests.
3. Add runtime diagnostics when the provider does not expose the required pattern, instead of silently treating the control as supported.

### 5.5 Tests

1. Add characterization tests for direct subclasses of standard Avalonia controls.
2. Add integration tests for the editor family:
   - text input
   - popup open
   - list selection
   - date set
   - spin set
   - commit/cancel
3. Add smoke scenarios for:
   - split button
   - notification dismiss/assert
   - dialog confirm/cancel
   - toolbar/menu/ribbon command selection
4. Add bridge or adapter contract tests first for `DataGridControl`, `TreeListControl`, `PropertyGridControl` and `ListViewControl`, then runtime smoke tests in both `Headless` and `FlaUI`.

## 6. Practical prioritization

Recommended order:

1. Standardize selector contract for custom component roots and template parts.
2. Close recorder recognition gaps for the editor family and split buttons.
3. Add bridge-first support for `DataGridControl`, `TreeListControl`, `PropertyGridControl` and `ListViewControl`.
4. Add command-surface support for toolbar/menu/ribbon.
5. Add docking-shell support only for stable pane activation; keep drag/floating/layout persistence out of the first milestone.

---

<a id="русская-версия"></a>

## Русская версия

[English](#appautomation-component-coverage-gaps) | **Русский**

Этот документ фиксирует gaps, найденные по исходникам компонентных библиотек, которые лежат в текущем worktree:

- `.tmp_eremex_controls`
- `.tmp_notification_src`

Сопоставление выполнено с текущими слоями AppAutomation:

- классификация recorder в `src/AppAutomation.Recorder.Avalonia/RecorderStepFactory.cs`
- headless runtime в `src/AppAutomation.Avalonia.Headless/Automation/HeadlessControlResolver.cs`
- FlaUI runtime в `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs`

## 1. Главный вывод

Текущий стек AppAutomation хорошо закрывает стандартные Avalonia-контролы и составные workflow, для которых уже есть явные adapters (`SearchPicker`, range filters, dialog, notification, folder export, shell navigation, editable grid requests).

Основной разрыв находится в custom/composite controls, построенных на `TemplatedControl`, `ItemsControl` и внутренних wrapper'ах. Для них AppAutomation сегодня либо не захватывает правильную семантику в recorder, либо не имеет корректной runtime-модели в `Headless` и `FlaUI`.

## 2. Что recorder сейчас не захватывает корректно

| Семейство компонентов | Факт из исходников | Почему recorder ошибается сейчас |
| --- | --- | --- |
| Семейство `BaseEditor` | `BaseEditor`, `TextEditor`, `ButtonEditor`, `PopupEditor`, `ComboBoxEditor`, `DateEditor`, `SpinEditor` | Recorder сначала классифицирует стандартные Avalonia controls, а custom root сводит к generic `AutomationElement`. Реальное действие живёт во внутренних parts вроде `PART_RealEditor`, `PART_PopupOpenButton`, `PART_ItemsSelector`. |
| `MxSplitButton` | `MxSplitButton : ContentControl`, `PART_PrimaryButton`, `PART_PopupOpenButton` | Recorder не различает primary action и открытие dropdown. |
| `NotificationMessage` / `NotificationMessageContainer` | `NotificationMessage : TemplatedControl`, `NotificationMessageContainer : ItemsControl` | Без explicit hints или ids recorder не может автоматически понять notification-root и dismiss flow. |
| `DataGridControl` | `DataGridControl : DataControlBase` | Захват root не даёт row/cell/action semantics без bridge или `GridHint`. |
| `TreeListControl` | `TreeListControl : TreeListControlBase` | У recorder нет tree-grid модели захвата. |
| `PropertyGridControl` | `PropertyGridControl : TemplatedControl` | Recorder не понимает row/category/editor semantics от корня. |
| `ListViewControl` | `ListViewControl : TemplatedControl` | Recorder не может автоматически рассматривать его как стабильную `ListBox`-подобную поверхность. |
| `Toolbar`, `PopupMenu`, `RibbonControl`, `DockManager` | Command и docking surfaces rooted в custom controls | Generic root capture не соответствует реальному пользовательскому intent: выбор команды, открытие popup, активация pane и т.д. |

## 3. Для чего сейчас нет корректной поддержки в Headless и FlaUI

### 3.1 Нет корректной модели в обоих runtime без новых adapters или bridges

- `DataGridControl`
- `TreeListControl`
- `PropertyGridControl`
- `ListViewControl`
- `Toolbar` / `ToolbarItem` command hierarchy
- `PopupMenu`
- `RibbonControl`
- `DockManager`

### 3.2 Сейчас это реально закрыть только через explicit composite-part mapping

- `BaseEditor` / `TextEditor` / `ButtonEditor`
- `PopupEditor` / `ComboBoxEditor` / `DateEditor` / `SpinEditor`
- `MxSplitButton`
- `NotificationMessage` / `NotificationMessageContainer`
- `MxWindow` / `MessageWindow` / `PopupContainer`

## 4. Менее рискованные семейства

Это прямые наследники стандартных Avalonia controls. Их обычно проще покрыть, если у них есть стабильные ids:

- `MxTabControl : TabControl`
- `MxTabItem : TabItem`
- `CalendarControl : Calendar`
- `NotificationMessageButton : Button`
- `ToolbarRadioButton : RadioButton`
- `ToolbarCheckBox : CheckBox`

Им всё равно нужны characterization-тесты, но это не главный архитектурный gap.

## 5. Какие доработки нужны

### 5.1 Automation contract

1. Добавить явные `AutomationProperties.AutomationId` на все scenario-facing custom roots.
2. Добавить явные ids на критичные template parts:
   - root editor
   - `PART_RealEditor`
   - popup-open button
   - popup list/content
   - apply/cancel/footer buttons
   - dialog/notification text и action buttons
3. Для `MxSplitButton` завести отдельные ids для primary и dropdown action.
4. Для data-heavy controls использовать детерминированные bridge ids, а не полагаться на неявное UIA-поведение.

### 5.2 Recorder

1. Добавить semantic recognizers для Eremex editor family, чтобы recorder мапил root не в generic `AutomationElement`, а в typed/composite workflow.
2. Добавить распознавание split-button с разными action kinds.
3. Расширить hints и aliases для notification, dialog, popup editor, toolbar/menu и shell surfaces.
4. На custom roots без поддержки отдавать явный unsupported diagnostic, а не misleading generic step.

### 5.3 Headless

1. Добавить editor adapters поверх внутренних parts для read/write, popup open/close, selection и commit/cancel.
2. Добавить typed support или bridge support для:
   - `DataGridControl`
   - `TreeListControl`
   - `PropertyGridControl`
   - `ListViewControl`
3. Добавить adapters для toolbar/menu/ribbon interactions.
4. Добавить shell-navigation слой для activation по stable anchors, а не по raw drag-docking gestures.

### 5.4 FlaUI

1. Реализовать тот же adapter contract поверх реального UIA и explicit `AutomationId`.
2. Для data/tree/property/list surfaces использовать bridge-first подход, пока native UIA patterns не подтверждены тестами.
3. Добавить runtime diagnostics, когда provider не отдаёт требуемый pattern, вместо молчаливого притворного "supported".

### 5.5 Тесты

1. Добавить characterization-тесты для прямых subclasses стандартных Avalonia controls.
2. Добавить integration-тесты для editor family:
   - text input
   - popup open
   - list selection
   - date set
   - spin set
   - commit/cancel
3. Добавить smoke-сценарии для:
   - split button
   - notification dismiss/assert
   - dialog confirm/cancel
   - toolbar/menu/ribbon command selection
4. Для `DataGridControl`, `TreeListControl`, `PropertyGridControl`, `ListViewControl` сначала добавить bridge или adapter contract tests, потом runtime smoke tests в `Headless` и `FlaUI`.

## 6. Практический порядок работ

Рекомендуемый порядок:

1. Нормализовать selector contract для custom roots и template parts.
2. Закрыть recorder recognition gaps для editor family и split buttons.
3. Добавить bridge-first поддержку для `DataGridControl`, `TreeListControl`, `PropertyGridControl`, `ListViewControl`.
4. Добавить command-surface support для toolbar/menu/ribbon.
5. Поддержку docking-shell сначала ограничить стабильной activation логикой; drag/floating/layout persistence оставить вне первого этапа.
