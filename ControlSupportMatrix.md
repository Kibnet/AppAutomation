# Control Support Matrix (AppAutomation + Avalonia demo)

Цель: быстро понять, какие контролы из существующего showcase уже имеют
фабрично типизированную поддержку, какие идут через fallback и что нужно делать
дальше, чтобы закрыть максимально широкий набор `ControlType`.

## Источник статуса

- Типы контролов в DSL определяются в `src/AppAutomation.Abstractions/UiControlType.cs`
- Автоматическое связывание `UiControlAttribute -> свойство` генерируется
  `src/AppAutomation.Authoring/UiControlSourceGenerator.cs`
- Реальные контролы страницы тестового приложения в
  `sample/DotnetDebug.AppAutomation.Authoring/Pages/MainWindowPage.cs`
- Реальные действия/ожидания в `src/AppAutomation.FlaUI/Extensions/*`

## Что сейчас покрыто в demo (готово для сценариев в тестах)

| Контрол | Avalonia в MainWindow | UiControlType | Уровень поддержки в AppAutomation | Комментарий |
|---|---|---|---|---|
| TextInput | `NumbersInput`, `MixInput`, `HistoryFilterInput`, `MixCountSpinner` | `TextBox` | полная | Через `EnterText`, чтение/запись значения |
| Button | `CalculateButton`, `ApplyFilterButton`, ... | `Button` | полная | Через `ClickButton` |
| Label | `ResultText`, `ModeLabel`, ... | `Label` | полная | Чтение текста и `WaitUntilNameEquals` |
| ListBox | `StepsList`, `HistoryList`, `SeriesList`, `DateDiffList`, `HierarchySelectionList` | `ListBox` | полная | `WaitUntilHasItemsAtLeast`, `WaitUntilListBoxContains`, `ReadListBox` fallback |
| CheckBox | `UseAbsoluteValuesCheck`, `ShowStepsCheck`, `MixShowDetailsCheck` | `CheckBox` | полная | `SetChecked`, `IsChecked` |
| ComboBox | `OperationCombo`, `MixModeCombo` | `ComboBox` | полная | `SelectComboItem` + fallback кандидатов |
| RadioButton | `MixDirectionAscendingRadio`, `MixDirectionDescendingRadio` | `RadioButton` | полная | `SetChecked`, `WaitUntilIsSelected` |
| ToggleButton | `MixAdvancedToggle` | `ToggleButton` | полная | `SetToggled`, `WaitUntilIsToggled` |
| Slider | `MixSpeedSlider` | `Slider` | полная | `SetSliderValue`, `WaitUntilValueEquals` |
| ProgressBar | `SeriesProgressBar` | `ProgressBar` | полная | `WaitUntilProgressAtLeast` |
| Tab | `MainTabs` | `Tab` | полная | `SelectTabItem` (агностичен к provider idiosyncrasies) |
| TabItem | `MathTabItem`, `...` | `TabItem` | полная | Через поиск в дереве Tab/поиск Selected |
| Tree | `DemoTree` | `Tree` | полная | `SelectTreeItem`, `WaitUntilHasItem` |
| DateTimePicker | `StartDatePicker`, `EndDatePicker` | `DateTimePicker` | полная (через основной API + fallback по текстовому полю) | `SetDate` |

## Что поддерживается через fallback/особые пути

| Контрол | Avalonia в MainWindow | Причина |
|---|---|---|
| Spinner (`MixCountSpinner` визуально TextBox) | пока как `UiControlType.TextBox` | В текущем шаблоне это текстовое поле, а не полноценный Spinner control; DSL поддерживает оба пути (`Spinner` и `TextBox`). |

## Что объявлено в enum, но не полноценно охвачено showcase

| Контрол | Статус | Что не хватает |
|---|---|---|
| Calendar | `Calendar` (enum 11) | Нет контролла на UI, можно добавить отдельный вклад/сценарий `Date selection` |
| DataGrid / DataGridView / Grid / GridRow / GridCell | перечислены | Нет DataGrid в UI, нет `WaitUntil` для row/cell/адресации ячеек |

## Что из списка docs на текущий момент не включено в demo

По текущему roadmap этого репозитория в showcase есть: math, mix-логика, date, hierarchy.
Не добавлены: отдельные сценарии для `Calendar`, `DataGrid`, `CalendarDatePicker`-подварианты,
`ListBox`/`Tree` уже закрыты, но без углубления в массовую навигацию по всем типам `IText` и т.п.

## Что дальше (рекомендуемый следующий шаг)

1. Оставить текущий минимальный set как stable baseline.
2. Добавить отдельный вклад `DataGrid` в `MainWindow.axaml` с простым `DataGrid` и тестами чтения числа строк/значений.
3. Добавить вклад `Calendar` и базовую проверку выбора даты/валидации.
4. Для каждого нового контрола:
   - добавить `UiControl` в `MainWindowPage`,
   - методы/ожидания в `UiPageExtensions` + `AutomationElementWaitExtensions` только при необходимости,
   - хотя бы 1-2 бизнес-сценария и связанные тесты в runtime-specific test projects.
5. Для контролов без надёжного UIA паттерна в `FlaUI.Core` фиксировать статус как `Fallback/Нестабильный` и покрывать только устойчивые проверки (`Name`/`Text`, `Availability`, `Enable/Selection`).
