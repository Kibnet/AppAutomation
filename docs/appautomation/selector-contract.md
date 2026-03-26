# AppAutomation Selector Contract

**English** | [Русский](#русская-версия)

This document defines the selector contract expected by both `Headless` and `FlaUI`.

## 1. Stable selectors first

Use `AutomationProperties.AutomationId` as the primary selector for:

- root window;
- navigation anchors;
- critical inputs and buttons;
- result labels;
- child anchors inside composite widgets.

Do not build primary waits around visible text, index or layout position.

## 2. `AutomationProperties.Name` is opt-in, not implicit

`WaitUntilNameEquals` and `WaitUntilNameContains` should be used only when the AUT explicitly sets `AutomationProperties.Name`.

Do not assume:

- `Button.Content` automatically becomes a stable automation name;
- `TextBlock.Text` automatically becomes a stable automation name;
- dynamic validation text will project identically in both runtimes.

If a scenario depends on a text assertion, set `AutomationProperties.Name` explicitly on that control.

## 3. Dynamic collections

For repeated entities, prefer deterministic naming based on a domain key:

- `MessageItem_{id}`
- `Row_{key}`
- `AttachmentChip_{fileName}`

Keep the key stable within the scenario and use the same pattern in both runtimes.

## 4. Composite and paired controls

For composite widgets, mark child anchors, not only the outer container.

For paired visible/invisible or active/inactive controls:

- give each stateful element its own `AutomationId`;
- reserve a separate `AutomationId` for panel/root hosts;
- avoid reusing one selector for mutually exclusive controls.

## 5. Success signals

Assert against user-visible UI state, not backend counters or internal test markers.

Good signals:

- shell/root panel became available;
- main window title changed;
- retry action remains available after failure;
- composer or signed-in shell controls became enabled.

---

<a id="русская-версия"></a>

## Русская версия

[English](#appautomation-selector-contract) | **Русский**

Этот документ фиксирует контракт селекторов, который ожидается одновременно и в `Headless`, и в `FlaUI`.

## 1. Сначала стабильные селекторы

Используйте `AutomationProperties.AutomationId` как основной селектор для:

- корневого окна;
- опорных элементов навигации;
- критичных полей ввода и кнопок;
- result label;
- дочерних якорей внутри составных виджетов.

Не стройте основные ожидания на видимом тексте, индексе или позиции в layout.

## 2. `AutomationProperties.Name` задаётся явно

`WaitUntilNameEquals` и `WaitUntilNameContains` следует использовать только там, где AUT явно задаёт `AutomationProperties.Name`.

Не нужно предполагать, что:

- `Button.Content` автоматически становится стабильным automation-name;
- `TextBlock.Text` автоматически становится стабильным automation-name;
- динамический текст валидации одинаково проецируется в оба runtime.

Если сценарий зависит от текстовой проверки, явно задайте `AutomationProperties.Name` на соответствующем контроле.

## 3. Динамические коллекции

Для повторяющихся сущностей используйте детерминированную схему именования на основе доменного ключа:

- `MessageItem_{id}`
- `Row_{key}`
- `AttachmentChip_{fileName}`

Ключ должен быть стабильным внутри сценария, а шаблон именования одинаковым в обоих runtime.

## 4. Составные и парные контролы

Для составных виджетов размечайте дочерние якоря, а не только внешний контейнер.

Для парных visible/invisible или active/inactive контролов:

- давайте каждому состоянию свой `AutomationId`;
- резервируйте отдельный `AutomationId` для panel/root host;
- не переиспользуйте один селектор для взаимоисключающих контролов.

## 5. Сигналы успешного состояния

Assert'ы должны опираться на пользовательское UI-состояние, а не на backend-счётчики или внутренние тестовые маркеры.

Хорошие сигналы:

- появился shell/root panel;
- изменился заголовок главного окна;
- после ошибки осталась доступна повторная попытка;
- включились controls signed-in shell или composer.
