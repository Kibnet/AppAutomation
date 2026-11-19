# Составные редакторы времени

Обычный Avalonia `TimePicker` и прежняя регистрация `WithTimePicker` сохраняются.
Для настоящего `TimePickerPresenter` используется явный opt-in. Он описывает
реальный input, popup, панели выбора и подтверждённое UI-значение, а не модель приложения.

```csharp
var durationParts = TimePickerParts.ByAutomationIds(
    rootAutomationId: "DurationEditor",
    timePickerAutomationId: "DurationPresenter",
    inputAutomationId: "DurationInput",
    popupRootAutomationId: "DurationPopup",
    confirmButtonAutomationId: "DurationConfirm",
    cancelButtonAutomationId: "DurationCancel",
    commitMode: TimePickerCommitMode.Confirm) with
{
    SurfaceKind = TimePickerSurfaceKind.TimePickerPresenter,
    OpenKind = TimePickerOpenKind.ClickInput,
    CommittedValueProperty = "Value",
    PresenterParts = new TimePickerPresenterParts(
        new TimePickerComponentParts("DurationHours", "HourIncrease", "HourDecrease"),
        new TimePickerComponentParts("DurationMinutes", "MinuteIncrease", "MinuteDecrease"))
};

resolver = resolver.WithTimePicker("PlannedDuration", durationParts);
recorderOptions.TimePickerHints.Add(new RecorderTimePickerHint("DurationEditor", durationParts));
```

Имена в примере условные: host назначает automation IDs своим настоящим template parts.
`Increase`/`Decrease` описывают фактическое изменение числа, не направление стрелки на кнопке.
`Period` обязателен для 12-часового режима, `Seconds` — при включённых секундах.
Вместо `ClickInput` можно оставить `InvokeButton` и задать `OpenButtonLocator` настоящей кнопки.
Нельзя одновременно выдавать TextBox за кнопку или включать Immediate commit для presenter.

## Чтение и запись — разные контракты

`ITimePickerControl.SelectedTime` у настроенного presenter читает **подтверждённое** значение.
Headless и Recorder читают явно названное зарегистрированное UI-свойство `TimeSpan`/`TimeSpan?`.
Пути через `DataContext` и восстановление точности из локализованной подписи не поддерживаются.

FlaUI читает `ValuePattern.Value` настоящего root. Host публикует invariant `TimeSpan` в формате `c`:
пустое поддержанное значение означает `null`, `00:00:00` означает ноль,
`1.01:30:00.0000001` сохраняет дни и ticks. Отсутствующий pattern или неверный формат — ошибка,
не `null`. Runtime не вызывает `ValuePattern.SetValue` на committed root.

Команда `Page.SetTime` по-прежнему принимает время в диапазоне `0 <= value < 1 day`.
Запрошенное значение должно точно представляться текущими шагами/точностью UI.
Чтение длительности больше суток не разрешает вводить её в панель на 24 часа.
Дни, `null` и непредставимые секунды/ticks не округляются и не заменяются нулём.

```csharp
var plannedDuration = Page.PlannedDuration.SelectedTime;
Page.SetTime(static page => page.PlannedDuration, TimeSpan.FromMinutes(90));
```

Recorder записывает только ненулевые компоненты, например
`TimeSpan.FromHours(2) + TimeSpan.FromMinutes(53)`. Неуказанные компоненты равны нулю,
а не сохраняют прежнее значение редактора. Ненулевые дни, секунды и миллисекунды
выводятся отдельными слагаемыми; остаток меньше миллисекунды — через `TimeSpan.FromTicks`,
без округления. Полный ноль — `TimeSpan.Zero`. Возможность прочитать дни не расширяет
ограничение `SetTime`: ввод по-прежнему должен быть меньше суток.

## Требуемая UIA-проекция для FlaUI

В дополнение к root ValuePattern host публикует состояние на **настоящих** root/presenter/panel peers
через `AutomationProperties.ItemStatus`. Codec `TimePickerRuntimeMetadata.ToAutomationValue`
создаёт строку `AppAutomation.TimePicker/1:` с типизированными данными:

| Данные codec | Источник и срок жизни |
| --- | --- |
| `TimePickerOwnerState` | ownerId отдельного UI-instance; retained epoch; isOpen/read-only; реальные clock/precision/steps |
| `TimePickerPresenterState` | тот же ownerId/epoch на presenter текущего popup |
| `TimePickerComponentState` | тот же ownerId/epoch; роль панели; её реальные min/max/step/SelectedValue |

Например, публикация уже прочитанного UI-состояния:

```csharp
var snapshot = new TimePickerOwnerState(
    ownerId, epoch, isOpen, isReadOnly, Clock: 24, UseSeconds: false);
var automationValue = TimePickerRuntimeMetadata.ToAutomationValue(snapshot);
```

Codec не читает VM, не меняет UI, не открывает popup и не регистрирует глобальный bridge.
Host связывает публикацию со своими UI property/template lifecycle событиями.
`ownerId` не является номером записи БД и никогда не попадает в исходник сценария.
Он сохраняется для жизни одного UI control. `epoch` увеличивается при новом открытии,
retemplate или изменении capabilities; normal close сохраняет epoch и меняет `isOpen` на false.
Для FlaUI переход от закрытого редактора к открытому должен иметь ровно следующий epoch:
пропуск поколения означает, что между чтениями мог произойти ещё один lifecycle transition.
Component snapshot обновляется после каждого реального изменения выбранного значения.

Повторное использование ListBoxItem/изменение его подписи не является идентичностью числа.
FlaUI использует canonical panel state и настоящие кнопки/Up/Down; `SelectionItem.Select`
и разбор локализованного AM/PM не заменяют выбор. Каждый шаг проверяется до Confirm.

Если ItemStatus уже занят несовместимыми данными host, интеграция v1 не включается:
нельзя молча перезаписывать его или выбирать другой carrier. Несовпавшие owner/epoch,
неверные роли/диапазоны и неоднозначные parts приводят к отказу.
ID/PID окна сами по себе не доказывают, какой карточке принадлежит detached popup.

Headless доказывает владельца также по настоящим native UI references и Popup.PlacementTarget.
Точность чтения не зависит от UIA display string; существующая metadata, если опубликована,
должна согласовываться с native UI-состоянием.

## Recorder и безопасность

Recorder связывает события с конкретными owner/popup и читает committed значение после штатного
Confirm/Enter handler. Внутренние изменения текста, кнопки и панели не создают дополнительные шаги.
Cancel, Escape и light-dismiss не создают `SetTime`. Старый отложенный callback после reopen,
смены карточки, retemplate или Stop не переносит значение в другую transaction.

Воспроизведение использует один исходный budget на открытие, выбор, Confirm, закрытие и проверку.
Уже открытый owner-bound popup не открывается вторым кликом. Ошибка после возможного commit
не является разрешением повторить Confirm. Read-only/disabled и потерянный owner не обходятся.
Перед Confirm повторно проверяются выбранные числа и native identity панелей.
Клавиатурный fallback также проверяет конкретное foreground-окно, а не только PID приложения.

Этот контракт требует подключения реальных host UI Automation metadata. Наличие библиотеки
или успешная компиляция не доказывают, что конкретный host уже подключён и проверен.
