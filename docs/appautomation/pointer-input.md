# Физическая мышь в FlaUI-тестах

Функциональные действия (`ClickButton`, выбор вкладки или элемента списка) сначала используют подходящий UIA pattern. Явный физический ввод нужен для проверки собственно мыши, tooltip или drag-and-drop.

Каждая физическая операция сохраняет текущую позицию **после получения владения desktop**, выполняет жест и возвращает курсор перед завершением метода. Click и hover перемещают курсор сразу, без анимации и парковки в углу. Drag выполняет ограниченный путь с доставкой событий движения, затем отпускает кнопку и возвращает курсор.

## Page API

```csharp
using AppAutomation.Abstractions;

var source = new UiControlDefinition(
    "Card", UiControlType.AutomationElement, "Card", FallbackToName: false);
var destination = new UiControlDefinition(
    "DropZone", UiControlType.AutomationElement, "DropZone", FallbackToName: false);

await page.PointerClickAsync(source);
await page.PointerClickAsync(source, new PointerClickOptions
{
    Button = PointerButton.Right
});
await page.PointerClickAsync(source, new PointerClickOptions { ClickCount = 2 });

await page.HoverAsync(source, async ct =>
{
    // Только чтение и проверки. Screenshot tooltip тоже делается здесь.
    await WaitForOwnedVisibleTooltipAsync(ct);
    await AssertTooltipContentsAsync(ct);
}, new PointerHoverOptions { Timeout = TimeSpan.FromSeconds(5) });

await page.DragAndDropAsync(source, destination, new PointerDragOptions
{
    Duration = TimeSpan.FromMilliseconds(250)
});
```

`WaitForOwnedVisibleTooltipAsync` и `AssertTooltipContentsAsync` в примере — проверки самого теста. Проверяйте видимый tooltip нужного приложения, а не рост числа одинаковых текстов: tooltip может быть открыт до начала операции. Disabled-контрол разрешён для hover.

`SupportsPointerInput` сообщает о поддержке. API реализован через optional `IUiPointerRuntime`; обязательный контракт `IUiControlResolver` не изменён. Adapter-aware resolver передаёт операции своему runtime. Headless сообщает `false` и отклоняет физический API через `NotSupportedException`.

## Raw FlaUI и существующие тесты

```csharp
using AppAutomation.FlaUI.Input;

await DesktopPointer.ClickAsync(() => FindCurrentElement());
await DesktopPointer.HoverAsync(() => FindCurrentElement(), async ct =>
{
    await VerifyTooltipAsync(ct);
});
await DesktopPointer.DragAndDropAsync(
    () => FindCurrentSource(), () => FindCurrentDestination());

// Для существующего синхронного кода:
DesktopPointer.Click(element);
```

Фабрики позволяют разрешать актуальные элементы под владением мышью. Legacy `ClickElement` по-прежнему означает физический клик, теперь с возвратом. `ClickButton` сохраняет семантический смысл Invoke. Прямые вызовы `FlaUI.Core.Input.Mouse` и `AutomationElement.Click()` в consumer-тестах обходят эту защиту: их нужно мигрировать отдельно. `Mouse.MoveTo(0, 0)` для подготовки hover следует удалить.

## Ошибки, отмена и совместное использование desktop

- Timeout по умолчанию — 5 секунд на очередь, поиск и действие. Освобождение мыши имеет отдельный бюджет 1 секунду и не отменяется пользовательским token.
- Перед вводом библиотека ждёт освобождения кнопок, клавиатурных modifiers и Escape. Чужие кнопки и modifiers она не отпускает.
- Вмешательство пользователя (перемещение или неожиданное нажатие) останавливает жест. Автоматического повторного наведения и перезапуска нет.
- Курсор возвращается только после подтверждения, что **все** кнопки мыши отпущены. Если это невозможно в cleanup budget, возвращается ошибка; курсор с зажатой кнопкой не переносится через desktop. Повреждённая сессия прекращает последующий физический ввод.
- Исходная ошибка проверки или отмена сохраняется. Ошибки cleanup прилагаются к ней; ошибка возврата никогда не считается успешным тестом.
- Hover callback вызывается один раз. Он должен быстро вернуть Task, использовать переданный token и выполнять только чтение/проверки. Вложенные pointer actions, UI mutations и fire-and-forget запрещены. Незавершающийся Task не удерживает мышь после timeout; синхронно зависший callback или COM-вызов нельзя принудительно прервать этой гарантией.
- Координаты — physical screen pixels; отрицательные координаты дополнительных мониторов допустимы. Нулевой размер, точка между мониторами, чужое окно поверх цели и невозможный read-back вызывают ошибку без запасного клика в `(0,0)`.
- Участвующие процессы AppAutomation координируются mutex для одной Windows session/window station/desktop. Это не блокирует аппаратную мышь или сторонний input API. `NotInParallel` остаётся необходимым для сценариев с общим keyboard/focus.
- Dispose сессии отменяет её активную операцию и выполняет ограниченный cleanup. Позиция начала всей сессии не используется для позднего возврата.

Гарантия возврата ограничена доступностью input desktop и исходного монитора. Библиотека не меняет настройки дисплеев, не использует глобальную блокировку ввода и не восстанавливает keyboard focus.

## Диагностика и проверка миграции

`DesktopPointer.GetTraceSnapshot()` возвращает JSONL недавних операций: идентификатор, этапы, цель, исходные/запрошенные/фактические координаты, нажатия и ошибки. Trace включается в FlaUI failure artifacts. `APPAUTOMATION_POINTER_TRACE_FILE` задаёт путь для дополнительного JSONL-файла; его родительский каталог нужно создать до запуска. Для доказательства восстановления сравнивайте physical cursor read-back сразу после каждой операции, включая ошибку и отмену.

При ошибке исходный exception содержит `Data["AppAutomation.Pointer.Artifacts"]` — список `UiFailureArtifact` с JSONL в `InlineTextPreview`. Это диагностика в памяти: `RelativePath` сам по себе не создаёт файл, и существующий TUnit collector не выгружает этот ключ автоматически. Для файлов используйте явный trace path или сохранение списка в тестовом harness. Внутренние physical fallbacks отмечаются этапом `physical-fallback` с технической причиной; содержимое пользовательских контролов и текст исключений в trace не записываются.

Координатор хранит время последнего нажатия в `%TEMP%\AppAutomation.Pointer\<desktop-hash>.uptime-ms`. Файл содержит только монотонную отметку uptime в миллисекундах и переживает тестовый процесс, чтобы два соседних одиночных клика разных процессов не превратились в double-click. Чтение и перезапись выполняются под desktop mutex; файл может быть удалён при очистке временных файлов, когда тесты не запущены. Ожидание интервала выполняется перед следующим захватом позиции и входит в timeout следующей операции.

Для click проверяйте реальные события и их количество, для hover — содержимое tooltip **до** возврата, для drag — реальное изменение порядка/состояния drop target. Видео окна дополняет trace: оно не показывает курсор, сохранённый за границами окна. Headless screenshot не является доказательством native pointer-поведения.

Рабочая спецификация и матрица проверок: [desktop pointer ownership](../../specs/2026-10-02-desktop-pointer-ownership.md).
