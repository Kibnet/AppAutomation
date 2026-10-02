# Управление мышью в UI-тестах: короткие действия, hover и drag-and-drop с возвратом курсора

Найденный источник движения в угол: **Unlimotion `MainWindowFlaUiTests.LaunchSession` явно вызывает `Mouse.MoveTo(0,0)`**. Согласовано удалить этот вызов, добавить общий lifecycle мыши в AppAutomation и перевести raw clicks/hover Unlimotion на него. Обычный click возвращает курсор сразу после доставки, hover — после проверки, drag — после отпускания кнопки. EXEC подтверждён пользователем; реализация и проверки ведутся в изолированных worktrees. Итоговые evidence и ограничения фиксируются в Post-EXEC Review.

## 0. Метаданные

- Дата: 2026-10-02. Владелец результата: Павел; автор исследования и SPEC: Codex.
- Фаза: **EXEC**. 2026-10-02 пользователь подтвердил: «Спеку подтверждаю». Форма: expanded; масштаб large — публичный контракт, несколько модулей и общий ресурс рабочего стола.
- Репозиторий: `C:\Users\Kibnet\.codex\worktrees\87d7\AppAutomation`, исходный HEAD `e7161c869004e65d9c15063c385f48c47a0a16a3`, detached worktree. До исследования рабочее дерево чистое.
- Проверенная конфигурация: AppAutomation 1.8.0, FlaUI.Core/UIA3 5.0.0, Avalonia 12.0.4, TUnit 1.61.29, SDK pin 10.0.202. Это конфигурация данного checkout, не установленного у неизвестного потребителя пакета.
- Stack: central `AGENTS.md` → routing-matrix, creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration-baseline, quest-governance, quest-mode; testing-baseline и testing-dotnet для плана EXEC; dotnet-desktop-client + ui-automation-testing; spec-linter, spec-rubric, review-loops.
- Central root `C:\Users\Kibnet\.codex\agents` — проверенный junction на `C:\Projects\My\Agents`. Локальных `AGENTS.override.md` не найдено. Шаблон: central `templates/specs/_template.md`.
- Поверхность: Codex desktop, Windows/PowerShell. Model eval не применим: меняется библиотека тестирования, а не модель/промпт. Настройки модели не менялись.
- Ветка реализации framework: `fix/desktop-pointer-ownership`. Consumer: отдельный worktree `C:\Users\Kibnet\.codex\worktrees\87d7\Unlimotion-pointer`, ветка `fix/ui-pointer-ownership` от указанного ниже SHA. SPEC включает framework и необходимую миграцию тестов Unlimotion. Commit/push/PR/release не разрешены этим поручением.
- После уточнения пользователя подтверждён consumer: `C:\Projects\Education\Unlimotion Space\Unlimotion`, HEAD `46711e60d6ef453e794104ee7d9f81ad6f37c68c`, `main`, с чужими незакоммиченными изменениями. Его код не менялся; EXEC-миграцию готовить в отдельном worktree, не затрагивая эти изменения. Локальный `AGENTS.override.md` требует соответствующих UI-тестов.

## 1. Overview / Цель

Пользователь наблюдает, как UI-тест рывками перемещает мышь к левому верхнему углу, возвращает её туда после ручного перемещения, затем иногда попадает в нужный элемент и кликает. Курсор остаётся в месте тестового действия. Требуется исследовать причины и подготовить реализацию трёх явных сценариев: короткий клик, наведение с проверкой, перетаскивание; каждый физический сценарий возвращает курсор на место до начала действия.

Outcome contract:

- Обычный функциональный тест не использует физическую мышь, если результат корректно достигается поддерживаемой семантической операцией.
- Когда нужна мышь, AppAutomation временно использует её только на время конкретного действия. Нет парковки в углу, ненужного плавного пути и удержания курсора между действиями.
- Возврат и освобождение кнопок выполняются также при ошибке и отмене; восстановление состояния не маскирует исходную ошибку.
- Результат текущего поручения — эта исследовательская SPEC с границами доказательств, контрактами и планом проверки. В SPEC меняется только этот файл.
- Stop для SPEC: исследование достаточно для проектирования, значимые решения определены, review завершён; точное воспроизведение пользовательского теста не выдумывается. Stop для EXEC: выполнены все обязательные AC либо честно зафиксирован blocker; нет бесконечного перебора fallback и повторов красного прогона без новой гипотезы.

## 2. Текущее состояние (AS-IS)

### 2.1 Подтверждённые факты по коду

Пути ниже относительно корня checkout; номера строк относятся к исходному HEAD.

| ID | Наблюдение | Evidence | Значение |
| --- | --- | --- | --- |
| F1 | Есть курсорные вызовы вне общего владельца ресурса | `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs`: button fallback ~823, list fallback ~1100, combo fallback ~2141, context menu ~2598, tab ~3133, tree ~3976; `Extensions/UiPageExtensions.cs`: ~30, 794, 814, 831, 931, 1410 | Исправить только один helper недостаточно |
| F2 | Tab сначала кликается, затем при необходимости вызывается SelectionItem | `FlaUiControlResolver.cs`, `FlaUiTabItemControl.SelectTab`, ~3129–3143 | Физический клик используется и там, где возможно семантическое действие |
| F3 | В grid есть прямые click/double-click, wheel и drag | `Automation/GridAutomation/FlaUiVisualGridControl.cs`: ~1298, 1629, 1840, 1954, 2008, 3173, 3216, 3357, 3414, 3840 | Нужны общий lifecycle и охват внутренних fallback |
| F4 | У drag thumb нет `finally` между Down и Up; возврата позиции нет | тот же файл, ~3187–3190 | Ошибка посередине может оставить кнопку нажатой |
| F5 | `MoveMouseImmediatelyTo` при отсутствии clickable point использует центр bounds без проверки площади | тот же файл, ~3874–3886 | Пустые bounds могут дать `(0,0)`; это дефект валидации, но ещё не доказанная причина наблюдавшихся рывков |
| F6 | Ожидание clickable проверяет только enabled/offscreen | `Extensions/AutomationElementWaitExtensions.cs`, ~18–28 | Не доказывает правильность координат и фактическое попадание |
| F7 | Возврат мыши не предусмотрен в `DesktopAppSession` | `Session/DesktopAppSession.cs`, Launch/Dispose | Сохранение позиции лишь на запуске сессии всё равно не удовлетворило бы возврату после каждого действия |
| F8 | В исходниках `src`, `tests`, `sample` не найдено `Mouse.MoveTo`, `MoveBy` или явного `moveMouse: true` / `Click(true)` | целевой `rg` по C#; прямые `Mouse.Position` найдены в grid | Нельзя объяснять рывки якобы включённой здесь анимацией по умолчанию |
| F9 | У FlaUI 5.0.0 `Click(bool moveMouse = false)` делает мгновенную перестановку; `MoveTo` интерполирует путь и не учитывает ручное смещение как отмену | первичные исходники FlaUI, ссылки ниже | `Click(false)` всё равно использует физический курсор; это не UIA Invoke |
| F10 | Сериализация sample ограничена TUnit assembly | `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Properties/AssemblyInfo.cs`, `[assembly: NotInParallel]` | Не координирует другой процесс с тестами на том же desktop |
| F11 | Есть две поверхности API: общие Abstractions и legacy FlaUI extensions | `Abstractions/UiPageExtensions.cs`, `FlaUI/Extensions/UiPageExtensions.cs`; factory оборачивает resolver адаптерами | Новая capability должна проходить через adapter-aware resolver; legacy операции тоже надо охватить |
| U1 | В Unlimotion status-contract launch **явно паркует курсор в `(0,0)` плавным методом** | `tests/Unlimotion.UiTests.FlaUI/Tests/MainWindowFlaUiTests.cs:49`, `LaunchSession`, условие `isStatusContract` | Прямой источник описанной траектории; выполняется при каждом таком тесте |
| U2 | Hover disabled status option тоже использует плавный `Mouse.MoveTo` без возврата | тот же файл:228–243; вызов из `StatusContract_RussianDarkBlocked`:203 | Hover нужен по смыслу, плавная дорога и оставшийся курсор — нет |
| U3 | В тех же тестах есть raw `.Click()` и `Mouse.LeftClick(point)` вне AppAutomation | тот же файл:310, 339, 461, 490; также `NewTaskTitleFlaUiTests`, `TaskSpacesFlaUiTests`, `TaskLoadingPerformanceFlaUiTests` | Обновление одного framework-пакета не исправит эти обходы |
| U4 | Unlimotion использует AppAutomation **1.6.0**, resolved FlaUI.Core/UIA3 **5.0.0** | consumer csproj и `tests/Unlimotion.UiTests.FlaUI/obj/project.assets.json` | Нельзя приписывать ему исходники AppAutomation 1.8.0; FlaUI MoveTo сверена с той же версией |

### 2.2 Источники зависимости и Windows

- [FlaUI 5.0.0 AutomationElement](https://github.com/FlaUI/FlaUI/blob/v5.0.0/src/FlaUI.Core/AutomationElements/AutomationElement.cs): физический click, параметр плавного движения, отсутствие возврата.
- [FlaUI 5.0.0 Mouse](https://github.com/FlaUI/FlaUI/blob/v5.0.0/src/FlaUI.Core/Input/Mouse.cs) и [Interpolation](https://github.com/FlaUI/FlaUI/blob/v5.0.0/src/FlaUI.Core/Input/Interpolation.cs): непосредственная установка позиции и интерполяция — разные пути.
- Tag дополнительно прочитан из официального GitHub source archive в памяти, без файловых изменений. Локальный NuGet nuspec 5.0.0 указывает commit `c5e7eb7af081e68534cb6b6890a38aa791ce4168`. Бинарный runtime проблемного теста пока не установлен.
- [Microsoft: SetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursorpos): операция может отказать или ограничить координаты; успешность и фактическую позицию нужно проверять.
- [Microsoft: UI Automation и DPI](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-screenscaling): UIA использует физические экранные координаты. Смешение их с логическими координатами курсора требует отдельной проверки.

### 2.3 Диагноз Unlimotion и оставшиеся гипотезы

После уточнения «в Unlimotion» найден конкретный путь: `LaunchSession → Mouse.MoveTo(0,0)`, затем для blocked scenario `HoverStatusOption → Mouse.MoveTo(center)`. В FlaUI `MoveTo` вычисляет путь от исходной точки и продолжает выдавать промежуточные позиции до конца; ручное смещение не отменяет этот цикл. Это объясняет последовательность «тянет в угол → не отпускает после ручного движения → позже идёт на элемент». Подтверждение — прямой статический call chain и resolved версия зависимости; живое воспроизведение пока не выполнялось. Причины отдельных промахов клика всё ещё требуют native trace.

| Приоритет | Гипотеза | Что уже известно | Как различить в EXEC |
| --- | --- | --- | --- |
| H1 — подтверждена по коду | Consumer напрямую вызывает плавное перемещение | U1/U2; версия FlaUI 5.0.0 подтверждена consumer assets | В EXEC baseline именно `MainWindowFlaUiTests/StatusContract_*`, координаты и before/after |
| H2 | Пустые, устаревшие или неверные координаты выбирают угол/чужую точку | F5 даёт конкретный путь получения `(0,0)` | Перед действием записать clickable point, bounds, availability, target identity и фактическую позицию |
| H3 | Несколько fallback, тестов или процессов повторно перехватывают курсор | Много распределённых физических путей, нет desktop coordinator | PID, operation ID, время владения, попытки, ветка fallback; два участвующих процесса в отдельной проверке |
| H4 | DPI, расположение мониторов или ограничение курсора искажает установку позиции | Риски платформы известны; конфигурация рабочего стола пользователя не исследована | Physical coordinates, DPI context, monitor rectangles, requested/actual position, Win32 error |

Рывки на конкретной машине **не воспроизведены запуском**. UI-тесты во время SPEC не запускались: задача — исследование и проектирование; источник движения найден непосредственно в status-contract launch. U1/U2 подтверждены, H2–H4 остаются возможными причинами дополнительных промахов, а F1–F7 — самостоятельными framework-дефектами. Исправление должно включать удаление U1 и миграцию U2/U3.

## 3. Проблема

В Unlimotion тестовый setup намеренно ведёт курсор в угол, а hover и clicks обращаются напрямую к FlaUI и не восстанавливают позицию. В AppAutomation также нет единого контракта владения курсором, валидации цели и восстановления для адаптеров, legacy helpers и grid fallback. Исправление: убрать конкретную ненужную парковку и дать всем необходимым физическим действиям общий lifecycle.

## 4. Цели дизайна

- Единственный механизм физических операций с общей сериализацией, deadline, диагностикой и cleanup.
- Сначала семантическое действие там, где проверяется результат; отдельные явные жесты там, где проверяется именно pointer behavior.
- Сохранение позиции перед действием, а не перед тестом/сессией. Для hover позиция сохраняется один раз на всё наведение с проверкой.
- Никакого возврата к отсутствующей цели и никакой замены невалидных координат на `(0,0)`.
- Аддитивный API без новых обязательных членов существующих public interfaces; стабильность текущих селекторов, virtualized grid и меню.

## 5. Non-Goals

- Не обещаем одновременное независимое использование одного физического курсора тестом и человеком. Время вмешательства уменьшается, но физический desktop остаётся общим.
- Не блокируем пользовательский ввод через BlockInput/ClipCursor, не ставим глобальные перехватчики и не прячем курсор.
- Не восстанавливаем foreground window, keyboard focus и clipboard целиком: это отдельный контракт; возврат курсора не означает возврат фокуса.
- Не меняем версию FlaUI, не патчим его глобальную скорость и не форкаем зависимость.
- Не перехватываем произвольный `FlaUI.Core.Input.Mouse` из стороннего теста или custom adapter. Для такого кода предоставляется явный поддерживаемый API и инструкция миграции.
- Из consumer-кода меняются только необходимые UI-test helpers/проверки Unlimotion и совместимые package references; продуктовые status rules, emoji/layout work и другие текущие изменения не входят в scope.
- Не добавляем запись hover/drag в Recorder, универсальную DSL жестов, OLE drag произвольных файлов или Headless-эмуляцию физических жестов в этом изменении.
- Не переписываем алгоритм обхода виртуализированных таблиц и не убираем работающий provider fallback без регрессионного evidence.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Ответственности

| Компонент | Ответственность |
| --- | --- |
| `AppAutomation.Abstractions/PointerRuntime.cs` (новый) | Optional `IUiPointerRuntime`, DTO options, page extensions и capability проверки |
| `AppAutomation.FlaUI/Input/DesktopPointerController.cs` (новый) | Lifecycle click/hover/drag/внутреннего wheel, deadline, cleanup, журнал операции |
| `AppAutomation.FlaUI/Input/DesktopInputCoordinator.cs` (новый) | Очередь внутри процесса + межпроцессное владение для одного Windows session/window station/desktop |
| `AppAutomation.FlaUI/Input/WindowsPointerBackend.cs` (новый) | Проверяемые Win32 get/set physical position, кнопки, wheel, screen/desktop state; seam для fake backend |
| `AppAutomation.FlaUI/Input/PointerTargetResolver.cs` (новый) | Свежая цель, physical bounds, hit test, принадлежность нужному окну/разрешённому popup |
| `FlaUiControlResolver` и `FlaUiVisualGridControl` | Выбор семантического пути, общий controller для каждой физической операции |
| Legacy `FlaUI/Extensions` | Такой же controller для собственных физических вызовов; без скрытого изменения смысла `ClickElement` |

Названия новых файлов допускают редакционную корректировку при реализации; ответственность и инварианты остаются.

### 6.2 Детальный дизайн

#### Разделение намерений

| Намерение | Поведение по умолчанию | Когда нужен курсор |
| --- | --- | --- |
| Invoke/выбор/значение/expand/scroll | Прямой подходящий UIA pattern с проверкой требуемого результата | Только подтверждённый provider fallback, если семантический путь не поддержан/не сработал безопасно |
| `PointerClickAsync` | Сохранить → сразу на цель → press/release → подтверждение доставки → вернуть | Всегда; UIA Invoke не заменяет явную проверку клика |
| `HoverAsync` | Сохранить → сразу на цель → выполнить bounded проверку hover → вернуть | На всём протяжении проверки, без повторного принудительного наведения |
| `DragAndDropAsync` | Сохранить → сразу на source → Down → ограниченный путь до target → Up → вернуть | В рамках одной непрерывной операции, включая drag threshold и события движения |
| Внутренний physical wheel | Сохранить → сразу на нужный участок → wheel → подтверждение доставки → вернуть | Только fallback; проверки прогресса прокрутки остаются в вызывающем алгоритме |

`ClickButton` остаётся семантическим Invoke. `ClickElement` legacy остаётся физическим click, теперь с возвратом. Приоритет SelectionItem перед click меняется для tab/list/tree только с доказанным результатом и сохранением special-case provider fallback. После неоднозначной ошибки уже отправленного неидемпотентного Invoke нельзя автоматически кликать повторно: сначала проверить postcondition; если результат неизвестен, завершить с диагностикой.

#### Публичная форма API

Добавить optional `IUiPointerRuntime`, который принимает существующий `UiControlDefinition`, а не абсолютные координаты. Он доступен через page extensions; `IUiControlResolver` не получает обязательных членов. `UiRuntimeCapabilities.SupportsPointerInput` — новое init-свойство, default false. FlaUI объявляет true; Headless — false и явный `NotSupportedException` без ОС-ввода. Adapter-aware resolver делегирует эту capability к inner resolver, как clipboard.

Предлагаемые методы page extensions (названия и смысл — часть контракта):

```csharp
Task<TPage> PointerClickAsync<TPage>(
    this TPage page, UiControlDefinition target,
    PointerClickOptions? options = null, CancellationToken cancellationToken = default);

Task<TPage> HoverAsync<TPage>(
    this TPage page, UiControlDefinition target,
    Func<CancellationToken, Task> verifyWhileHovered,
    PointerHoverOptions? options = null, CancellationToken cancellationToken = default);

Task<TPage> DragAndDropAsync<TPage>(
    this TPage page, UiControlDefinition source, UiControlDefinition target,
    PointerDragOptions? options = null, CancellationToken cancellationToken = default);
```

`TPage : AppAutomation.Abstractions.UiPage`. Runtime возвращает `Task`, page extension возвращает page. LocatorKind, Scope и запрет неоднозначности сохраняются. Использование definitions позволяет переопределять цель после ожиданий без извлечения native handles из custom controls. У raw FlaUI callers — эквивалентная façade, принимающая resolver/factory элементов, с тем же controller, без открытых `Down`/`Up` и без произвольного пользовательского кода между ними.

Options: общий положительный `Timeout = 5s`, включающий очередь, поиск и действие; cleanup имеет независимый ограниченный бюджет 1s. Click: `Button = Left`, `ClickCount = 1` (допустимо 1/2). Hover: только timeout, ожидание задаёт callback; переданный token обязателен для пользовательских ожиданий. Drag: `Button = Left`, `Duration = 250ms`, положительное значение не больше оставшегося deadline. Промежуточные точки вычисляются из start/end в physical pixels; шаг обработки не чаще 16ms, последняя точка точная, требуется движение за системный drag threshold и доставка движения перед Up. Мгновенная перестановка source→target без доставки событий не считается реализацией drag.

Пример нового использования, код пока не существует:

```csharp
var hint = new UiControlDefinition("Help", UiControlType.AutomationElement,
    "HelpIcon", FallbackToName: false);
await Page.HoverAsync(hint, async ct =>
{
    await WaitForTooltipAsync(ct); // ограниченное ожидание реального tooltip
    await AssertTooltipContentsAsync(ct); // выполняется до возврата курсора
});
```

Hover callback выполняется один раз, исключение проверки передаётся тесту. Библиотека ставит deadline, отменяет token и возвращает мышь при timeout, не ожидая бесконечно незавершённый Task callback. Deadline/cleanup исполняются независимо от continuation callback. Callback обязан быстро возвращать Task и выполнять ожидания с cancellation; синхронное зависание до возврата Task, зависший COM/native вызов и произвольный прямой ввод стороннего кода не покрываются гарантией управляемой отмены. Вышедший за deadline Task не прерывается принудительно средствами .NET: он наблюдается для ошибок, его operation context закрыт для новых pointer actions; контракт callback разрешает только проверки/чтение, запрещает fire-and-forget и UI mutations. Это явно документируется и проверяется контролируемым незавершающимся Task. Во время hover запрещена вложенная pointer operation из callback: немедленная диагностическая ошибка, без взаимной блокировки. Обычные чтения/ассерты разрешены.

Если курсор уже над целью, hover не обязан делать лишний выход/вход. Проверка tooltip должна проверять видимый tooltip и его принадлежность нужной строке, а не обязательно увеличение числа одинаковых текстов относительно baseline. В Unlimotion адаптировать `WaitForAdditionalNamedElement`: already-open tooltip тоже валиден. Если сценарий действительно проверяет новый PointerEntered, он должен явно описывать подготовительное leave внутри тестового окна; `(0,0)` не используется как универсальная нейтральная точка.

#### Lifecycle и восстановление

1. Проверить capability/options/cancellation. Получить координацию desktop в пределах deadline. Пока ожидаем очередь, не читаем «исходную» позицию и не двигаем мышь.
2. Проверить доступность input desktop и отсутствие уже нажатых пользователем mouse buttons. Если кнопка зажата, ждать её освобождения в рамках deadline; не отпускать её самостоятельно.
3. При физической подготовке, включая scroll-into-view, первым шагом сохранить **текущую** physical position. Фокус/подготовка, если нужны, выполняются под владением; любое скрытое физическое действие подготовки также идёт через controller.
4. Получить свежие bounds/clickable point; если нужен scroll, выполнить подготовку и разрешить элемент заново. До Down проверить foreground/принадлежность hit-test цели, enabled там, где требуется действие, visibility/площадь. Hover может проверять tooltip disabled-контрола, поэтому enabled для него не обязателен.
5. Переместить курсор непосредственно в требуемую точку, проверить фактическое положение. Допускается один технический повтор установки позиции для известного multi-monitor перехода, только при неизменном input state; нет интерполяции click/hover, homing/парковки и повторного «дожимания» после вмешательства пользователя.
6. Выполнить действие с operation ID и deadline. Перед Down и каждым следующим input/restore сверять позицию, все кнопки и modifiers с ожидаемым состоянием операции. В hover/drag повторять проверку между шагами. Внешнее смещение или неожиданный Down, даже без движения, прерывает операцию с `PointerInterference`; автоматического перезапуска нет. При смешении аппаратного и injected состояния одной и той же кнопки точное происхождение может быть неразличимо — библиотека не заявляет его без evidence.
7. В `finally`: освободить только кнопки, которые нажала операция и для которых ещё требуется Up; проверить их состояние. Ошибка одного cleanup шага не пропускает остальные безопасные шаги. Возврат допустим только при подтверждённом **AllUp всех mouse buttons**, включая чужие. Если пользователь нажал кнопку во время hover/после тестового Up, ждать её естественного отпускания в cleanup budget, не отправляя чужой Up. Затем — ограниченная попытка возврата исходной позиции с read-back. Если остаётся Down/состояние неизвестно, **не перемещать зажатый курсор через desktop**, вернуть cleanup failure и прекратить дальнейший physical input этой session. Освободить межпроцессное и локальное владение; следующая операция снова проходит preflight кнопок. При нормальном доступном desktop возврат происходит перед выходом метода, а не в teardown теста.
8. При уже существующей основной ошибке cleanup errors прилагаются отдельно, её stack/cancellation не теряются. Если действие прошло, а восстановление не удалось, тест не получает success — выдаётся ошибка cleanup. При недоступном desktop или исчезнувшем мониторе гарантия физического возврата ограничена возможностями ОС; записать saved/requested/actual и причину, без клика/парковки в запасном углу.

При обнаруженном вмешательстве пользователя всё равно один раз попытаться вернуть координату до операции, как запрошено пользователем; затем полностью отпустить ресурс. Не отслеживать человека и не переносить saved position вслед за ним. Между read-back и отправкой события остаётся минимальная гонка общего desktop; атомарность с аппаратной мышью не обещается.

Кнопки регистрируются как potentially-down перед отправкой Down, снимаются после подтверждённого Up. Ошибка отправки не позволяет забыть возможное нажатие. Обработчики неизвестных keyboard modifiers не должны создавать иной жест: перед физическим действием ждать их отпускания/завершить диагностикой, если modifier не был явно запрошен самим адаптером; внешние modifiers не отпускать. Не нажимать Escape глобально для «отмены drag» без доказанного адресата.

#### Координаты и координация

- Единица всех pointer targets и saved position — **physical screen pixels**. Отрицательные X/Y допустимы для мониторов слева/сверху. `(0,0)` допустимо только как подтверждённая реальная цель, не sentinel ошибки.
- Backend использует согласованные physical Get/Set API или эквивалент с явным per-thread DPI context и восстановлением этого context. Нельзя менять DPI-awareness всего consumer-процесса после запуска окон. Bounds, monitor geometry и hit-test проверяются в одной системе координат.
- Целевая точка находится в фактическом monitor rectangle и области нужного элемента; прямоугольник объединения мониторов с пустотами недостаточен. Hit-test допускает потомка элемента и принадлежащий приложению popup, не чужое окно поверх него.
- Межпроцессная блокировка — именованный mutex на session/window station/desktop для участвующих AppAutomation runtimes. Mutex thread-affine: acquisition/release на выделенном owner thread; произвольные async continuations не вызывают ReleaseMutex. UIA/COM objects не переносятся вслепую между потоками: lookup остаётся на допустимом потоке, backend получает validated primitive coordinates/handles.
- Вложенные внутренние шаги одной операции разделяют один operation ID и snapshot; вложенная пользовательская операция запрещена. Другой тест ждёт в очереди до своего deadline. Dispose session отменяет принадлежащие ей операции и ожидает bounded cleanup; не возвращает устаревшую позицию старта сессии.
- Этот mutex не останавливает человека, старую версию библиотеки или сторонний Mouse API. Параллельные desktop suites по-прежнему не становятся безопасными для keyboard/focus; существующий `NotInParallel` сохраняется.

#### Диагностика и visual planning artifact

У каждой физической операции: runtime/package version, PID/test correlation, kind, semantic/fallback reason, locator, owner window, saved position, requested/actual target и restore, timestamps, injected-point count, pressed/released buttons, stage, timeout/cancellation/interference/cleanup errors. На failure прикладывается к существующим `UiFailureArtifact`; на успешном test run trace доступен для acceptance без screenshot всего рабочего стола. Не логировать значения полей или содержимое других приложений.

Storyboard состояний — visual planning artifact для поведения курсора (нового пользовательского окна нет):

| Кадр | Click | Hover | Drag |
| --- | --- | --- | --- |
| A | Курсор в P, кнопка отпущена | Курсор в P | Курсор в P, кнопка отпущена |
| B | Один переход P→C | Один переход P→H | Один переход P→S, затем Down |
| C | Down/Up над C | Tooltip открыт, проверка идёт над H | Движение S→D с удержанием, drop над D |
| D | Курсор снова P | После проверки курсор P | Up выполнен, курсор P |
| Ошибка | Cleanup до возврата ошибки | Tooltip может закрыться при возврате; assertion уже выполнен либо упал | Никакого возврата с оставшейся тестовой Down |

### 6.3 User-Observable Scenarios

| Scenario | Trigger | Ожидаемый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| S1 | Обычный выбор вкладки/кнопка с работающим pattern | Нет перемещений/нажатий мыши | semantic trace + результат UI | 1 |
| S2 | Требуется настоящий одиночный/двойной/правый click | Сразу к цели, ровно нужный click, обратно P | OS trace + UI event/counter | 2 |
| S3 | Проверка tooltip/hover state | Наведение сохраняется до конца проверки, затем P | Screenshot открытого tooltip + trace | 3 |
| S4 | Перетаскивание элемента/scroll thumb | Правильный drop/scroll, все кнопки отпущены, P | UI state/event log + trace/video | 4 |
| S5 | Assertion/timeout/cancellation/ошибка отправки | Очистка до завершения, исходная ошибка видна | Fault-injection и native smoke | 5 |
| S6 | Пользователь двигает мышь во время hover/drag | Тест прекращает борьбу, завершает cleanup, диагностирует вмешательство | Controlled interference trace | 6 |
| S7 | Два процесса и разные мониторы/DPI | Нет пересечения участвующих операций, правильные target/restore | Два PID + координаты | 7, 8 |
| S8 | Старые page helpers / grid / adapted page | Автоматический возврат без изменения каждого existing test | Регрессии обеих API-поверхностей | 9 |
| S9 | Нет валидной цели или Headless | Нет системного ввода и ложного клика в углу | backend event count = 0 | 8, 10 |
| S10 | Unlimotion `StatusContract_*` | Setup не ведёт мышь в угол; blocked tooltip проверен внутри scoped hover; все raw clicks мигрированы | Native before/after конкретного suite | 11 |

### 6.4 State / Interaction Matrix

| Состояние | Событие | Результат |
| --- | --- | --- |
| Idle | Семантическое действие | Без pointer ownership; соответствующий postcondition |
| Queued | Lock/deadline/cancellation | Acquire либо ошибка без движения |
| Acquired | Пользователь держит кнопку/modifier | Bounded ожидание, никаких принудительных Up |
| Prepared | Нет свежей валидной цели | Ошибка, без нового click/drag |
| Active click/hover/drag | Успех/ошибка/отмена/вмешательство | Cleaning, всегда |
| Hover active | Вложенный pointer action | Немедленный отказ; cleanup внешней операции |
| Cleaning | Release error | Проверить кнопки; restore только если безопасен; fault session и освободить lock |
| Cleaning | Desktop исчез/restore отказал | Диагностика cleanup; нет success |
| Closed | Поздний hover callback | Ошибка наблюдается, новая операция в закрытом context запрещена |

### 6.5 Decision Ledger

| Решение | Owner | Выбрано | Confidence | Риск | Нужен отдельный ответ перед EXEC |
| --- | --- | --- | ---: | --- | --- |
| Три режима и возврат | user | Выполнять в каждом физическом действии | 1.0 | Минимальное владение всё равно заметно | Нет, уже задано |
| Semantic-first | agent | Только при сохранении семантики; явные pointer gestures всегда физические | 0.95 | Некоторые provider patterns неполны | Нет, проверяется регрессиями |
| Вмешательство человека | agent | Прервать, один restore к P, без restart | 0.9 | Возврат также виден пользователю | Нет, предложенный default SPEC |
| Новый API | agent | Optional capability, definitions, scoped callback | 0.9 | Обёртки должны делегировать | Нет |
| Headless | agent | Честный unsupported для нового physical API | 0.95 | Такие сценарии только во FlaUI suite | Нет |
| Источник движения в угол | evidence | U1 подтверждён по коду, native прогон ещё нужен | 1.0 | Возможны отдельные причины промахов | Нет |
| Миграция Unlimotion | agent | Обязательная часть предлагаемой реализации в изоляции | 0.95 | Raw bypass иначе сохранится | Нет, входит в эту SPEC |
| Публикация | user | Вне scope; локальный package validation допустим в EXEC | 1.0 | Установка опубликованного пакета требует delivery | Да только для отдельного delivery, не для локальной реализации |

### 6.6 Runtime / Config / Data Contract Matrix

| Area | Source of truth | Изменение | Совместимость | Проверка |
| --- | --- | --- | --- | --- |
| Runtime capability | UiRuntimeCapabilities | init false; FlaUI true | Без новых обязательных interface members | Existing custom resolver compile + Headless rejection |
| Adapter wrappers | UiControlAdapters | Делегирование optional runtime | Сохранять Scope/locator resolution | Adapted page native smoke |
| Windows cursor/buttons | ОС | Ephemeral ownership на действие | Нет persisted settings | Native read-back, cleanup |
| Package version | Directory.Packages.props / consumer csproj и assets | Framework dependencies FlaUI/Avalonia не меняются; consumer AppAutomation refs — согласованный local prerelease по §10 | FlaUI 5.0.0, оба framework TFM и согласованный consumer graph | Build + dependency audit |
| Запуск тестов | global.json / CI | Существующий MTP/TUnit | Не использовать VSTest filter | Команды §11 |

## 7. Алгоритмы и инварианты

- NoPointer semantic success ⇒ ноль записей cursor position, Down/Up/wheel.
- Click success ⇒ нужная пара Down/Up и restore; double-click — ровно две пары в допустимом OS интервале; два разных single-click не должны случайно стать double-click.
- Hover verification ⇒ курсор остаётся над целью до завершения проверки, без цикла наведения.
- Drag ⇒ подтверждённое нажатие, хотя бы движение за threshold, доставка move, release до restore.
- Нет успешного возврата метода раньше cleanup. Все отправленные события соотносятся с operation ID и stage.
- Отсутствие target ≠ `(0,0)`. Retry lookup допустим до первого non-idempotent input в пределах общего deadline; blind replay click/drag запрещён.
- Cleanup не использует уже отменённый token основной операции; его бюджет ограничен отдельно.
- Предсуществующие кнопки и modifiers не отпускаются библиотекой.
- На жёсткое завершение процесса/сессии ОС `finally` не гарантируется. Автоматический глобальный watchdog с чужими Up не входит в scope; новый runner диагностирует abandoned ownership, не объявляет состояние доказанно чистым.

## 8. Точки интеграции

- Заменить физические вызовы во всех F1/F3 местах, а не только `MoveMouseImmediatelyTo`.
- Audit dependency convenience calls (`Select`, `Expand`, `SelectTabItem`, calendar/date operations): где внутри бывает physical fallback, использовать прямой pattern или провести его через controller. Каждый найденный bypass классифицировать в итоговом audit.
- Grid keyboard fallback содержит click для фокуса: его click тоже возвращает курсор, при этом keyboard/focus sequence сохраняет нужное окно. Прокрутка/реализация virtualized item и пользовательский drag различаются в diagnostics, используют общий lifecycle.
- Сохранить существующий input delivery barrier перед возвратом; подобрать bounded проверку доставки для нового backend и подтвердить native событийным fixture, а не произвольным sleep после каждого click.
- Failure artifacts связываются с operation trace. Snapshot screenshot при hover assertion должен сниматься, пока состояние открыто, если это безопасно; capture error не мешает cleanup.
- Связь controller с DesktopAppSession нужна для cancellation/dispose и диагностики. Legacy caller без session работает через coordinator по текущему desktop; гарантия операции не зависит от teardown.
- Unlimotion: удалить `Mouse.MoveTo(0,0)` из `LaunchSession`; заменить `HoverStatusOption` на scoped hover с ожиданием, screenshot и assertion внутри callback. Tooltip для disabled row должен продолжить проверяться. Все найденные U3 физические call sites направить через новый controller/API или обоснованный semantic path. Не заменить проверку hover чтением одного HelpText.

## 9. Изменения состояния

Состояние жеста хранится в памяти: operation ID, saved physical position, target snapshots, ownership token, deadline, stage, owned buttons, cleanup errors. Ничего не сохраняется в настройках пользователя. Для предотвращения слияния одиночных кликов разных процессов координатор хранит только монотонную отметку uptime последнего нажатия в `%TEMP%\AppAutomation.Pointer\<desktop-hash>.uptime-ms` под desktop mutex; файл переживает процесс и может очищаться вместе с temp, когда тесты не работают. Новый capability и options являются публичным API, не форматом сохранённых данных. Trace ограничен в памяти; тестовый harness может задать отдельный artifact path через `APPAUTOMATION_POINTER_TRACE_FILE`. `Exception.Data["AppAutomation.Pointer.Artifacts"]` содержит typed in-memory artifacts, не автоматически созданные файлы.

## 10. Миграция / Rollout / Rollback

- Существующие framework actions автоматически получают возврат; пользователь не переписывает каждый тест.
- Тесты, которые полагались на оставшийся после клика hover, переводятся на явный `HoverAsync`. Это намеренное наблюдаемое изменение поведения, его явно описать в changelog/migration notes.
- Прямой сторонний FlaUI input необходимо заменить façade/page API; автоматического обещания для raw handles нет.
- Выполнить staged rollout: framework fake/native fixtures → sample regression → Unlimotion status-contract и затронутые raw-click scenarios в изолированном worktree. У Unlimotion все AppAutomation references сейчас 1.6.0: подготовить локальные пакеты новой согласованной prerelease-версии и восстановить из локального feed с изолированным cache, без публикации. Обновлять согласованно Abstractions/FlaUI/Authoring/Session.Contracts/TestHost/Headless в тестовом graph, не оставлять смесь несовместимых assemblies. Avalonia consumer уже 12.0.4; это не повод автоматически менять остальные зависимости.
- Откат: revert отдельного change set и возврат предыдущей package reference. Новые pointer tests при откате API тоже откатываются/не используются; persisted migration нет. Не добавлять runtime switch, тайно возвращающий небезопасный режим.

## 11. Тестирование и критерии приёмки

Ниже сохранён согласованный план проверок. Фактические результаты EXEC и ограничения оборудования приведены в Post-EXEC Review; наличие строки в этой матрице само по себе не означает успешный прогон.

| AC | Acceptance criterion | Automated check | Visual/log evidence |
| --- | --- | --- | --- |
| 1 | При работающем UIA pattern мышь не используется, результат корректен | Pattern-capable tab/button/list/tree + spy backend; native representative flows | Ноль injected pointer events, проверенное состояние |
| 2 | Click/right/double попадает в цель и возвращается P; нет промежуточного угла/анимации | Pointer runtime fake + native event counter; distinct single vs double cases | Последовательность requested/actual coordinates; mouse event count |
| 3 | Hover assertion выполняется при открытом tooltip; возврат после success/error/timeout | Tooltip fixture, delayed/assertion-throw/noncooperative callback | PNG реально открытого tooltip и pointer trace |
| 4 | Drag вызывает реальный drag/drop или scroll; release перед restore | Sample reorder/drop fixture + grid thumb regression; fault after Down | UI order/scroll delta, events, video/trace |
| 5 | Cleanup выполняется на исключениях, отмене, timeout и dispose; исходная ошибка сохранена | Fault injection на каждом stage, Up failure, restore failure, after-completed Dispose, чужая Down непосредственно до restore, unknown/partial button state | При работоспособном input нет owned Down; restore только при AllUp; lock освобождён, session fault при cleanup failure, primary+cleanup errors |
| 6 | Внешнее смещение/неожиданная Down не вызывает борьбу/автоматический replay | Fake state change + controlled native move during hover/drag; button-down без движения во время hover и после тестового Up | Interference stage, не более одной restore-попытки при AllUp, нет чужого Up/повторного Down |
| 7 | Нет перекрытия операций участвующих процессов; ожидающий сохраняет P после acquisition | Два native test host процесса; queue cancel; nested rejection; abandoned mutex | PID/operation intervals + lock release на owner thread |
| 8 | Empty/stale/offscreen/occluded/outside-monitor targets не кликаются; отрицательные координаты и DPI корректны | Geometry/fault unit tests + native multi-monitor/DPI matrix | Saved/target/actual/restore physical coordinates, valid `(0,0)` отдельно |
| 9 | Возврат действует через adapters, legacy и grid fallback; старые сценарии проходят | Affected sample suite + full solution; audit физических API | Discovery/results, audit каждого bypass |
| 10 | Headless/custom unsupported не двигает ОС-мышь; новая capability аддитивна | Abstractions tests + consumer compile + Headless regression | NotSupported до input; build обоих TFM |
| 11 | Unlimotion status-contract setup не паркует курсор; click/hover восстанавливают P и сохраняют tooltip на disabled row | Native baseline/after `StatusContract_TerminalPickerAndUnarchive`, `StatusContract_RussianDarkFuture`, `StatusContract_RussianDarkBlocked`; regression затронутых raw-click tests | Trace и видео; screenshot/assertion при открытом tooltip, package resolution evidence |

Новые тесты: `DesktopPointerControllerTests`, `PointerPageExtensionsTests`, `DesktopPointerRuntimeTests` в существующих подходящих projects; новый отдельный test project нужен только если существующие references не позволяют проверить backend seam. Sample получает изолированные controls со стабильными AutomationId для click/tooltip/drag и журналом событий.

Native матрица: primary 100%, secondary слева/сверху с отрицательными координатами, разные DPI 100%/150%, цель после scroll, чужое окно поверх цели, input desktop unavailable. Pure/fake tests обязательны всегда; native проверки стандартного single-monitor desktop и реально доступных дополнительных конфигураций обязательны для EXEC PASS. Недоступные multi-monitor/DPI варианты фиксируются отдельно, covered by fake tests и остаются ограничением; нельзя заявлять физическую проверку отсутствующего оборудования. Не менять мониторную конфигурацию пользователя ради прогона.

Команды, основанные на текущем MTP/TUnit workflow (выполнить только в EXEC):

```powershell
dotnet --info
dotnet build AppAutomation.sln -c Release
dotnet run --project sample/DotnetDebug.AppAutomation.FlaUI.Tests -c Release -- --treenode-filter "/*/*/DesktopPointerRuntimeTests/*" --maximum-parallel-tests 1
dotnet run --project sample/DotnetDebug.AppAutomation.FlaUI.Tests -c Release -- --treenode-filter "/*/*/DesktopPointerControllerTests/*" --maximum-parallel-tests 1
dotnet run --project tests/AppAutomation.Abstractions.Tests -c Release -- --treenode-filter "/*/*/PointerPageExtensionsTests/*"
dotnet test --solution AppAutomation.sln -c Release --no-build
```

В отдельном worktree Unlimotion после локальной интеграции:

```powershell
dotnet run --project tests/Unlimotion.UiTests.FlaUI -c Release -- --list-tests
dotnet run --project tests/Unlimotion.UiTests.FlaUI -c Release -- --treenode-filter "/*/*/MainWindowFlaUiTests/StatusContract_*" --maximum-parallel-tests 1
dotnet run --project tests/Unlimotion.UiTests.FlaUI -c Release -- --maximum-parallel-tests 1
dotnet run --project tests/Unlimotion.UiTests.Headless -c Release
```

Перед этим проверить native flags/SDK/launch host именно consumer и discovery; не считать несовместимый CLI invocation тестовым RED. Повторить обычную сборку и применимые обязательные checks Unlimotion для package migration; проверить resolved versions всех проектов. Эти команды не выполнялись на фазе SPEC.

Сначала characterization/failing regression для подтверждённого дефекта, затем targeted проверки, сборка, full suite: изменение публичного API и широкого набора адаптеров требует полного прогона. До длинного прогона проверить SDK/dependencies, объявить команду и log path; фильтры сверить через `--list-tests`, нулевой discovery не считать green. Недоступный интерактивный desktop — environment blocker native evidence, не исправность/неисправность продукта. Failing обязательный тест блокирует завершение. Повторять только затронутые проверки после содержательного изменения/новой гипотезы.

Согласованный формат visual evidence: `artifacts/pointer/<run-id>/before.mp4`, `after.mp4`, `pointer-trace.jsonl`, `tooltip-open.png`, `results.*`; фактические пути записаны в Post-EXEC Review. Видео записывать из automated run по окну тестового fixture, с курсором; screenshot окна без видимого курсора не доказывает возврат вне окна. Для позиции за пределами окна нужен OS trace. Если recorder не умеет захват cursor/window либо безопасная запись технически недоступна, указать точную причину и команду, предоставить native event trace + просмотренные кадры. Для Unlimotion есть meaningful baseline U1/U2: нужен before/after этого flow. У совершенно нового drag fixture до реализации нет meaningful API flow: before для него N/A.

## 12. Риски и edge cases

- UIA pattern может менять состояние иначе, чем физический click. Поэтому semantics-first относится к намерению функционального действия; физические gesture tests остаются явными.
- Tooltip может закрыться при restore — это ожидаемо; проверка должна быть внутри hover callback. Доказать, что screenshot не сделан уже после закрытия.
- Отложенный обработчик UI может увидеть уже возвращённую мышь: нужен подтверждённый delivery barrier и native test, не только spy calls.
- Drag handler может блокировать provider thread; controller не держит COM-ссылки на dedicated mutex owner thread, cleanup остаётся доступным.
- Внешний input может попасть между двумя Win32 вызовами; отказ от борьбы не даёт атомарной изоляции от аппаратной мыши.
- Жёсткий kill, secure desktop/UAC/RDP disconnect, снятый монитор не дают абсолютной гарантии restore. Не маркировать это success и не захватывать другой desktop автоматически.
- При возврате после Up hover нового элемента может сработать там, где пользователь оставил мышь. Это следствие запрошенного восстановления, не повод парковать курсор в углу.

### Expected User Review Objections

| Вероятное замечание | Почему | Как закрывается | Статус |
| --- | --- | --- | --- |
| «Исправили только кнопку, таблица всё ещё забирает мышь» | Несколько входов F1/F3 | Полный audit, common controller, AC9 | mitigated |
| «Вернули курсор до проверки tooltip» | Restore слишком рано | Scoped hover callback, AC3 | mitigated |
| «При падении мышь зажата/осталась в приложении» | Нет finally в текущем drag | Fault injection cleanup, AC5 | mitigated |
| «Обновили AppAutomation, но Unlimotion всё ещё дёргается» | U1/U2/U3 обходят библиотеку | Обязательная consumer migration, AC11 | mitigated |
| «Я всё равно не могу пользоваться мышью одновременно» | Один physical desktop | Короткое владение и abort on interference; полная изоляция требует другого desktop, вне scope | accepted-risk |

Rework Prevention: исходный сценарий сохранён; решения и assumptions приведены; каждому видимому сценарию соответствует AC/evidence; роли и контрпримеры проверяются в §19. EXEC имеет путь проверки без автоматической публикации.

## 13. План выполнения

1. Установить baseline Unlimotion `StatusContract_*`: resolved binary, trace двух MoveTo; characterization framework потери позиции и invalid bounds. H2–H4 не принимать за факт без evidence.
2. Реализовать backend seam, target validation, coordinator и cleanup с fault tests. Затем click/wheel и миграция существующих физически необходимых вызовов.
3. Семантический приоритет в ограниченных известных адаптерах, с проверкой поведения tab/list/tree/grid и без слепых повторов команд.
4. Добавить optional runtime, forwarding wrappers, scoped hover и bounded drag; sample native fixtures и диагностику.
5. Локально собрать согласованные пакеты и мигрировать Unlimotion в отдельном worktree: удалить парковку, заменить raw gestures, сохранить disabled tooltip assertion. Не изменять dirty main checkout.
6. Targeted native evidence обоих репозиториев, full solution validation, consumer package checks, docs/changelog, post-EXEC review; при несоответствии исходному результату не выдавать готовность.

## 14. Открытые вопросы

При первоначальном утверждении SPEC блокирующих продуктовых решений не было. Пользователь уточнил проект Unlimotion; прямые источники движения найдены в status-contract suite. Точное имя запускавшегося теста и причины каждого промаха на фазе SPEC не были подтверждены runtime trace, но это не мешало удалить доказанную парковку и проверить найденный suite. Публикация пакетов остаётся отдельным delivery-действием.

### Выявленное в EXEC расширение scope: HTTP → SSH

Статус: **предложение, не входит в уже подтверждённый EXEC**. §5 ограничивает consumer изменениями UI-test helpers/проверок и package references. Ниже описана отдельная минимальная правка продуктового кода Unlimotion; выполнять её можно только после явного подтверждения расширения пользователем.

- Дефект: при показанном Settings control `ApplyRemoteConnectionTypeSwitch` присваивает `GitRemoteName = origin-ssh` до обновления `RemotesWithAuthType`. ComboBox ещё не содержит такого элемента, сбрасывает SelectedItem и двусторонним binding записывает `GitRemoteName = null`; последующий `ReloadGitMetadata` выбирает `origin`. Команда создаёт SSH remote успешно, но UI теряет его выбор.
- Evidence: consumer `artifacts/pointer/settings-remote-writeback.txt` содержит стек `ApplyRemoteConnectionTypeSwitch:978 → Avalonia SelectedItem binding → GitRemoteNameDisplay:710 → GitRemoteName(null)`, затем `ReloadGitMetadata:963 → EnsureRemoteSelection:1312`. Heap подтвердил завершение команды и наличие обоих remotes. Это не timeout мыши и не зависшая Git-операция.
- Предложенное изменение: только в `src/Unlimotion.ViewModel/SettingsViewModel.cs`, метод `ApplyRemoteConnectionTypeSwitch`, переместить существующий `ReloadGitMetadata()` перед присваиванием `GitRemoteName` и `GitRemoteUrl`. Тогда ItemsSource содержит новый remote до выбора. Не отключать binding, не подставлять результат из теста, не ослаблять assertions.
- Проверка: существующий shown-window Headless regression должен подтвердить `origin-ssh`, точный SSH URL, `IsSshAuthSelected=true`, `NotConfigured` и `Select an SSH key.` после реального нажатия. Дополнить показанную форму roundtrip HTTP→SSH→HTTP с двумя remotes: exact name/url/auth, фактический selected item ComboBox и сохранённая тестовая конфигурация должны совпадать после обоих переходов. Повторить Settings remote tests, весь consumer Headless suite и относящиеся к remote-switch tests ViewModel. Имеющиеся VM tests проверяют прямое переключение, а обратные service tests не покрывают UI binding, поэтому roundtrip обязателен.
- Риски: `ReloadGitMetadata` также обновляет auth/status и выполняет EnsureRemoteSelection; допускаются промежуточные уведомления со старым выбором, но конечные name/url/auth должны соответствовать результату команды. Проверить, что setters выполняют финальное обновление статуса. Формат конфигурации, сеть и реальные пользовательские настройки не изменяются тестами.
- Rollback: вернуть порядок трёх существующих statements; fixture сохраняет проверку настоящей показанной формы, поэтому regression снова воспроизводится.
- Публикация, commit/push/PR, установка и изменения исходного dirty checkout не входят в это расширение.
- Отдельное review предложения: BLOCKER/HIGH нет; MEDIUM про недостаточную проверку обратного перехода закрыт обязательным shown-window roundtrip выше. Reviewer работал только чтением в технически writable sandbox. Утверждение пользователем пока не получено.

## 15. Соответствие профилю

- .NET desktop: platform-specific input выделен; UI-поток не блокируется произвольным ожиданием; build/full suite запланированы для EXEC.
- UI automation: storyboard, стабильные selectors, native click/hover/drag, before/after evidence и объективный fallback определены.
- SPEC: изменён только этот документ. Реализация, сборка и UI-прогоны пока не выполнены.

## 16. Таблица планируемых изменений файлов

| Файл/группа | Изменение | Причина |
| --- | --- | --- |
| `src/AppAutomation.FlaUI/Input/` новые controller/coordinator/backend/resolver | Централизация физических операций | Ownership/cleanup/координаты |
| `src/AppAutomation.FlaUI/Automation/FlaUiControlResolver.cs` | Optional runtime, semantic routing, миграция click | Общие adapters |
| `src/AppAutomation.FlaUI/Automation/GridAutomation/FlaUiVisualGridControl.cs` | Click/wheel/drag через controller | Все grid fallback |
| `src/AppAutomation.FlaUI/Extensions/UiPageExtensions.cs` и затронутые helpers | Миграция native gestures | Legacy API |
| `src/AppAutomation.FlaUI/Automation/FlaUiCalendarSelection.cs`, `GridAutomation/FlaUiGridControls.cs` | Только обнаруженные физические dependency bypass | Audit F1/F3, не общий refactor |
| `src/AppAutomation.FlaUI/Session/DesktopAppSession.cs` | Operation cancellation/cleanup association | Session disposal |
| `src/AppAutomation.Abstractions/PointerRuntime.cs` новый | Public capability/options/extensions | Явные жесты |
| `src/AppAutomation.Abstractions/UiRuntimeCapabilities.cs`, `UiControlAdapters.cs` | init capability, forwarding | Совместимость wrappers |
| `sample/DotnetDebug.AppAutomation.FlaUI.Tests/Tests/` | Pure seam и native regression | AC2–9 |
| `tests/AppAutomation.Abstractions.Tests/`, Headless sample tests | API compatibility/unsupported | AC10 |
| `sample/DotnetDebug.Avalonia/` fixture и `sample/DotnetDebug.AppAutomation.Authoring/` | Изолированный click/tooltip/drag sample | Реальный native результат |
| `README.md`, `docs/appautomation/`, `skills/`, `CHANGELOG.md` | Документация трёх режимов, raw boundary, миграция hover | Применение потребителями; без Recorder DSL изменений |
| Unlimotion: `tests/Unlimotion.UiTests.FlaUI/Tests/MainWindowFlaUiTests.cs` | Удалить парковку, scoped tooltip, clicks | Прямая причина U1/U2/U3 |
| Unlimotion: `NewTaskTitleFlaUiTests.cs`, `TaskSpacesFlaUiTests.cs`, `TaskLoadingPerformanceFlaUiTests.cs` | Raw-click migration | Автоматический возврат в остальных найденных UI сценариях |
| Unlimotion: `tests/*/*.csproj` с AppAutomation references, docs/spec при реализации | Согласованная локальная package integration | Проверка нового API без публикации |

## 17. Было → станет

| Область | Было | Станет |
| --- | --- | --- |
| Физический click | Курсор остаётся на цели | Прямой переход и возврат до завершения |
| Hover | Нет публичного scoped контракта | Наведение держится до конца проверки |
| Drag | Локальный Down/move/Up без гарантии cleanup | Bounded жест, Up/restore в общем lifecycle |
| Невалидная геометрия | Возможен центр пустых bounds | Fail до ввода |
| Конкуренция | Assembly NotInParallel | Общая pointer координация участвующих процессов + прежняя сериализация UI suite |
| Unlimotion setup | `Mouse.MoveTo(0,0)` для status-contract | Нет ненужной парковки; hover отдельно с возвратом |

## 18. Альтернативы и компромиссы

1. **Только увеличить скорость FlaUI или поставить `Click(false)` везде.** Малый diff; default уже false, не даёт возврата, lifecycle и hover. Не решает задачу.
2. **Сохранять курсор на старте теста и возвращать в teardown.** Просто; мышь удерживается весь тест, возвращается устаревшая позиция, hover/drag cleanup не определён. Отклонено.
3. **Общий controller + semantic-first + явные scoped gestures — выбран.** Больше интеграции, зато автоматическое исправление existing framework paths и проверяемый контракт трёх сценариев.
4. **Полностью запретить physical fallback.** Курсор не трогается, но реальные pointer handlers и часть custom grids перестанут проверяться. Не соответствует задаче.
5. **Отдельный desktop/VM.** Даёт настоящую изоляцию от пользователя, но это инфраструктура и другой процесс запуска. Возможное последующее решение, не замена возврату курсора в библиотеке.

## 19. Результат quality gate и review

Self-review и отдельный reviewer pass завершены. **SPEC готова к утверждению.** Ниже оценивается готовность проекта решения, а не прохождение runtime tests.

### SPEC Linter Result

| Пункт | Предмет | Статус | Evidence |
| --- | --- | --- | --- |
| 1 | Цель и outcome | PASS | §1, S1–S10 |
| 2 | AS-IS | PASS | F1–F11, U1–U4, actual assets и source call chain |
| 3 | Корневая проблема | PASS | §3: явная парковка + отсутствующий lifecycle |
| 4 | Цели дизайна | PASS | §4: минимальные движения, возврат, совместимость |
| 5 | Границы | PASS | §5, scoped Unlimotion migration, без публикации |
| 6 | Ответственности | PASS | §6.1: backend/controller/coordinator/target resolver |
| 7 | Интеграция | PASS | §8 и §16, legacy/wrappers/grid/consumer |
| 8 | Инварианты | PASS | §7, lifecycle и state matrix |
| 9 | Recovery | PASS | §6.2: owned buttons, cleanup failure, timeout limits |
| 10 | Performance | PASS | Нет анимации click/hover, bounded drag/queue, не заявлено измеренное ускорение |
| 11 | Данные/состояние | PASS | §9: ephemeral operation state, нет persisted migration |
| 12 | Совместимость | PASS | Optional capability, adapter forwarding, согласованные local packages |
| 13 | Rollback | PASS | §10: revert change set/packages, без данных |
| 14 | Измеримые AC | PASS | §11: события, position read-back, tooltip/drop state |
| 15 | AC→evidence | PASS | AC1–11 + fault/native матрица; unavailable hardware явно отделено |
| 16 | Команды и stop | PASS | MTP/TUnit обоих проектов, discovery, baseline/target/full |
| 17 | План | PASS | §13: framework → локальная consumer integration → evidence |
| 18 | Решения/вопросы | PASS | §6.5 и §14; scope определён, approval ожидается |
| 19 | Форма/масштаб | PASS | §0: expanded, public behavior и multi-module |
| 20 | Профиль | PASS | §15, storyboard и native/video acceptance |

### SPEC Rubric Result

| Критерий | Балл | Основание |
| --- | ---: | --- |
| Цель/границы | 5 | Исходный симптом, три режима, scoped consumer migration и Non-Goals |
| AS-IS | 5 | Конкретные MoveTo и resolved dependency; native observation не подменена статикой |
| Дизайн | 5 | API, очередь/ownership, hit-test, cleanup и ответственность заданы |
| Безопасность/миграция | 5 | Чужой input не отпускается, pressed restore запрещён, isolated consumer + rollback |
| Проверяемость | 5 | AC1–11, pure/native/consumer checks, before/after, ограничения hardware |
| Автономность | 5 | Внутренние решения приняты, точная approval граница и этапы известны |

Итог: 30/30, готово к автономной реализации после approval. Reviewer findings закрыты; оценка не заменяет approval.

### Role-Based Review Result

| Роль | Применимость / вопрос | Статус |
| --- | --- | --- |
| Business analyst / workflow | Возврат после каждого действия соответствует запросу | PASS: framework и обязательный Unlimotion flow, S1–S10 |
| UX / designer | Ненужные движения, вмешательство человека, момент hover assertion | PASS: удаление парковки, scoped hover, явный предел совместного desktop |
| Tester | Наблюдаемые результаты и негативные случаи, source diagnosis | PASS: AC1–11; failure/foreign-down/timeout tests, без фиктивного native RED |
| Developer / architect | Optional API, wrappers, thread-affine mutex, cleanup | PASS: аддитивный контракт, async/mutex boundaries, late callback и release failure |
| Operations / security | Shared desktop, чужой ввод, диагностика без чужих данных | PASS: AllUp before restore, isolated consumer, без BlockInput/публикации |

### Post-SPEC Review

- Статус / Stop decision: **PASS — можно утверждать SPEC**, реализация не начата. Writer этого файла — главный агент.
- Scope reviewed: текущая SPEC; central owners/template/profiles; исходный AppAutomation HEAD, consumer Unlimotion HEAD и dirty state; файлы F1–F11/U1–U4; planned changes §16 и отсутствие блокирующих вопросов §14.
- Independent reviewer sandbox недоступен: `/root/review_pointer_spec` подтвердил `danger-full-access`, unrestricted filesystem, approval `never`. По review owner выполнен отдельный adversarial fallback в writable среде, только чтением. Это не технически независимый read-only review; файлы/мышь/build reviewer не менял.
- Scope/Evidence pass: проверены реальные call sites, consumer csproj/assets (AppAutomation 1.6.0/FlaUI5), статический путь U1/U2 и raw clicks, official FlaUI source/tag archive, Microsoft coordinate/input contracts. Следствие кода отделено от невыполненного живого воспроизведения.
- Contract pass: три режима, автоматический возврат framework operations, миграция raw consumer calls, optional capability и forwarding, no consumer product changes. Approval разрешает только описанную локальную реализацию, без публикации.
- Adversarial pass: рассмотрены late/hung hover Task, synchronous callback/COM hang, чужая Down без смещения, partially-sent Down, Up/restore failure, nested operations, two-process ownership, stale/empty bounds, valid `(0,0)`, отрицательные координаты, DPI и callback после timeout.
- Role-Based pass: результаты в таблице выше; пользовательский workflow и UX не подменены внутренними тестами helper.
- Fix and re-review: исправлены self-review вопросы pressed restore и границ отмены; reviewer HIGH про чужую Down исправлен в lifecycle и AC5/6; LOW package matrix исправлен. Reviewer повторно прочитал изменённые пункты и выдал PASS без незакрытых findings.
- Evidence inspected: перечисленные исходники и dependency metadata; целевой `rg`; проверка структуры SPEC (21 numbered sections + Approval), баланс code fences; whitespace check без ошибок (предупреждение Git LF→CRLF — формат checkout). В AppAutomation `git status` показывает только новый файл этой SPEC; изменения dirty consumer checkout не затрагивались.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | Cleanup / shared desktop | Чужая Down во время hover без смещения могла пройти до restore | Проверять все buttons/ожидаемое состояние; AllUp перед restore, bounded ожидание без чужого Up, AC5/6 | fixed, reviewer re-review PASS |
| HIGH | Cleanup / self-review | Restore после неудачного собственного Up мог протащить зажатую мышь | При неподтверждённом release не двигать; cleanup failure/session fault | fixed |
| MEDIUM | Callback / self-review | Абсолютное обещание timeout не учитывало синхронный hang callback/COM | Ограничить контракт async Task, independent deadline; честно назвать границу | fixed |
| LOW | Compatibility | «Версии не меняются» противоречило consumer migration | Разделить dependencies framework и версии AppAutomation consumer | fixed |

Depth checklist:

- Scope drift/unrelated changes: до approval только SPEC; Unlimotion dirty main не менялся, isolated migration запланирована.
- Acceptance criteria: S1–S10 связаны с AC1–11; primary symptom проверяется в найденном consumer suite.
- User scenarios/decisions/objections: заполнены §6.3/6.5/12, без оставленного user-owned blocker.
- Validation evidence: статический диагноз, реальные packages и review подтверждены; runtime before/after не заявлены выполненными.
- Unsupported claims: ordinary Click не обвинён в плавной анимации; H2–H4 не представлены установленными причинами промахов.
- Regression/edge cases: disabled tooltip, stale target, consumer wrappers, failure/interference/concurrency и platform cases включены.
- Comments/docs/changelog: описана future migration, не изменены на SPEC.
- Hidden contract change: возврат может закрыть hover; это прямо вынесено в migration и explicit hover API.
- Manual-review challenge: самое опасное предположение — «свои кнопки отпущены, значит restore безопасен»; устранено reviewer HIGH, теперь требуется AllUp всех кнопок.

No-findings justification после исправлений: при повторной проверке жизненного цикла и AC5/6 нет разрешённого пути сознательно переместить зажатый курсор; optional API не требует обязательных interface members; consumer raw bypass включены в scope и acceptance. Незакрытых BLOCKER/HIGH/MEDIUM/LOW нет. Остаточные ограничения — аппаратная гонка между Win32 вызовами, зависший внешний код, unavailable desktop/оборудование; они описаны, не объявлены устранёнными.

Needs human: только exact approval для EXEC. Build/native runs, локальная package integration и итоговый post-EXEC review остаются будущей работой.

### Post-EXEC Review

EXEC разрешён точной фразой «Спеку подтверждаю». Реализация управления мышью и обязательные прогоны завершены. Framework и consumer pointer outcome прошли проверки; общий regression gate — **NEEDS-FIX** из-за двух отдельно описанных consumer failures. Дополнение §14 о product fix HTTP→SSH ожидает отдельного подтверждения и не реализовано.

Реализованы optional `IUiPointerRuntime`, page extensions и raw `DesktopPointer`; desktop mutex, save/action/cleanup, per-thread physical DPI, проверка цели перед вводом, bounded hover callback, event-delivered drag, диагностические artifacts. Framework fallback paths и consumer raw calls переведены на controller; functional actions используют доступные semantic patterns. Headless capability остаётся false.

Подтверждённые результаты текущего EXEC:

- Full solution build Release: PASS; framework `net8.0` и `net10.0` собраны.
- Все восемь non-native test projects: **656/656 PASS**. Логи `artifacts/pointer/validation/full-*.log`.
- Controller/fault tests: **40/40 PASS** (`controller-final.log`). Покрыты cleanup, foreign input, late callback/cancellation registration, disposed session, lost monitor, monotonic cooldown и отказ от позднего второго одиночного клика.
- Cross-process coordinator: **1 PASS + 1 intentional child-entry skip** (`coordinator-process-final.log`); child выполнялся отдельно, shared lock и persisted monotonic timeline проверены.
- Full FlaUI regression: **123 PASS / 0 FAIL / 2 expected skips**, 9m37s (`full-FlaUI-final.log`). Пропуски: системный clipboard E2E требует отдельной изолированной desktop-сессии; служебная child entry point запускается из coordinator-теста. Все **12 новых native pointer tests** вошли в этот успешный прогон, включая actual interference и disposed-registration rejection.
- Native drag: реальный `Drops=1`, `Order=B,A`, `Result=Move`, `Overs=12`; screenshot просмотрен, release/restore подтверждены (`native-drag-neutral` и `native-final`). `cursor-observations.jsonl` содержит физические read-back новых native tests. Общий `live-trace.jsonl` full run также включает fake controller tests; искусственные координаты от них не считаются hardware evidence.
- Native run использует реальную конфигурацию двух мониторов: `(0,0)-(3840,2160)` и `(3840,561)-(5760,1641)` (`physical-monitors.json`). Сохранённая точка `(4474,1190)` на втором мониторе восстанавливается после ввода в окно на первом; fixture отрисован при 225% масштабе. Отрицательная раскладка, 100%/150% и отключение монитора физически не воспроизводились: fake geometry/cleanup coverage, конфигурация пользователя не менялась.
- Локальный coherent package graph: шесть пакетов `1.8.1-pointer.20261002.2`, без публикации; consumer final integration выполняется в `Unlimotion-pointer`.

Дополнительные дефекты, выявленные выполнением:

1. Baseline Unlimotion оставлял Escape через одиночный `Keyboard.Press`, что немедленно отменяло OLE drag (STA и platform service существовали; `EscapePressed=true`, zero DragOver). В [FlaUI 5 Keyboard.cs](https://github.com/FlaUI/FlaUI/blob/v5.0.0/src/FlaUI.Core/Input/Keyboard.cs) `Press` отправляет только Down, а `Type` выполняет Down/Up. Парные taps переведены на `Keyboard.Type` в затронутых framework/consumer paths. Backend теперь ждёт нейтральный Escape и прекращает жест при его появлении, не отпуская чужую клавишу. Оставленный нашим baseline Escape один раз освобождён, read-back сохранён в `baseline-escape-cleanup.json`.
2. Активация уже открытого popup HWND могла закрывать Avalonia Flyout перед повторным поиском цели. Если приложение уже foreground, лишняя активация исключена; exact hit-test сохранён. Consumer `StatusContract_RussianDarkBlocked` на `.2` прошёл с просмотренным tooltip PNG и возвратом после click/hover.
3. Upgrade consumer 1.6 → текущая Headless-реализация выявил две тестовые фикстуры, полагавшиеся на `IsEnabled` для неотображённого содержимого. В новой версии проверяется `IsEffectivelyEnabled`; consumer fixture корректируется до настоящего UI flow. Это сравнивалось с baseline, а не объявлено прежним падением.

Review выполнялся отдельным агентом только чтением, но sandbox технически writable (`danger-full-access`): это adversarial review fallback, не технически изолированный read-only reviewer. Все обнаруженные замечания по replay, exact target, primary/fallback locator, diagnostics, callback CTS, cooldown и double-click interval исправлены. Framework: **779 PASS / 0 FAIL / 2 skips** во всех девяти test projects; отдельный framework review PASS.

Consumer evidence, подтверждённое независимо от ещё незавершённого regression gate:

- Изолированный checkout: `C:\Users\Kibnet\.codex\worktrees\87d7\Unlimotion-pointer`, base `46711e60d6ef453e794104ee7d9f81ad6f37c68c`. Все 11 package references в пяти проектах согласованы на `.2`; package hashes сохранены в `artifacts/pointer/package-sha256-dot2.json` consumer.
- Baseline video: `artifacts/pointer/before-crop/StatusContract_RussianDarkBlocked/before.mp4`; final video: `artifacts/pointer/after-final/StatusContract_RussianDarkBlocked/after-final.mp4`. Оба — область тестового окна, 1252×720, 30 FPS, без звука. Просмотренные кадры показывают реальную подсказку. Разница длительности записей не является измерением ускорения, поскольку момент начала захвата различается.
- Final `after-final/StatusContract_RussianDarkBlocked/result.json`: test exit 0, saved/final `(4474,1190)`. Framework trace: click на `(1340,872)` и hover на `(1697,1016)`, отдельный restore `(4474,1190)` после каждого; `(0,0)` отсутствует. В baseline read-only OS trace движение через `(0,0)` присутствует, тест сам оставлял курсор на цели.
- UIA Avalonia представляет эту подсказку как Text под MainWindow, без ToolTip peer. Consumer predicate проверяет точный видимый текст и отдельный native HWND того же PID; исключает inline-причины и строки статуса. Проверка и screenshot происходят внутри hover callback.
- Первый full consumer FlaUI `.2`: **22 PASS / 5 FAIL / 1 expected skip**. После миграции full повтор: **26 PASS / 1 baseline FAIL / 1 expected skip**, 5m31s (`flaui-tests-final-dot2.log`, `after-flaui-final-dot2/*.trx`). Отдельный baseline подтвердил missing `CurrentTaskRepeaterSection`; он не исправлялся. NewTaskTitle прошёл повторный полный запуск без диагностических вставок; первый watcher timeout остаётся зафиксированной нестабильностью, его идентичность baseline не доказана.
- Три consumer input failures исправлены без ослабления exact-hit guard: не повторяется click после уже успешного выбора карточки; TreeItem/ComboBox используют поддержанный SelectionItem. После одного Select тест ждёт готовность selector, один раз закрывает его, проверяет Collapsed и точный результат в приложении. [FlaUI 5 ComboBox.Collapse](https://github.com/FlaUI/FlaUI/blob/v5.0.0/src/FlaUI.Core/AutomationElements/ComboBox.cs) пропускает действие при IsEnabled=false, поэтому ожидание перед закрытием существенно. Ненадёжная дополнительная проверка SelectedItem.Name/IsSelected не входит в финальный код; она не заменяет фактические app-state assertions.
- В Settings remove удалены два ненужных helper для повторного раскрытия/выбора: после Add/Rename уже выбран `Space C renamed`. Перед Remove проверяется точный `TaskSpaceNameTextBox.Text`, связанный TwoWay с `SelectedTaskSpace.DisplayName`; после действия сохранены подтверждение удаления, отсутствие именно этого имени в persisted catalog и fallback Space A. Это проверяет тот же объект, который команда удаляет по SourceId.
- Последние точечные прогоны после full suite: TaskLoading **1/1 PASS, 17.4s**, `after-enabled-collapse-dot2/Startup_and_space_switches_allow_task_actions_after_loading/*.trx`; TaskSpaces **1/1 PASS, 14.6s**, `after-final-cleanup-dot2/Task_spaces_switch_A_B_A_and_emit_visual_evidence/*.trx`. Final consumer build: 0 warnings/errors. Эти повторные тесты не прибавляются второй раз к full-suite count. Гипотеза про UIA lifetime/DPI отвергнута: fresh UIA3 давала тот же root hit; framework guard не менялся.
- Consumer Headless: финальный полный запуск **50 PASS / 1 FAIL / 0 skips**, 2m30s (`headless-pertest-fixture-full.log`, `headless-pertest-fixture-results/*.trx`). Две несовместимые fixture первоначально выявлены сравнением с `1.6.0`. TaskSpaces исправлен и проходит. Единственный FAIL — Settings remote switch: показанное окно выявляет подтверждённый product binding defect, описанный в §14; исправление предложено отдельно, approval ещё нет. Эксперимент PerAssembly не устранял дефект и вызвал удержание прежних resource trees в full suite; этот эксперимент отменён, финальные hooks сохраняют исходный lifetime. Прерванный эксперимент не считается результатом suite (`headless-final-fixture-full.status.txt`). Production-код consumer не меняется, assertions не ослабляются.

Матрица фактической приёмки:

| AC | Результат | Основание / граница |
| --- | --- | --- |
| 1 | PASS | Native semantic Invoke меняет состояние без input events; существующие adapters проходят framework suite |
| 2 | PASS | Native left/right/double event counters, две соседние одиночные операции, physical cursor read-back |
| 3 | PASS | Disabled/already-open tooltip, проверка внутри callback, failure/cancellation cleanup; Unlimotion Blocked screenshot/video |
| 4 | PASS | Настоящий native OLE drop: order `B,A`, один drop; grid regressions в полном suite |
| 5 | PASS | 40 controller/fault tests и native error/cancel/disposed tests; cleanup errors сохраняются |
| 6 | PASS | Controlled native movement + fake foreign Down; stop без chasing/replay |
| 7 | PASS | Два процесса, Windows mutex и shared monotonic cooldown; queue/nested/dispose fault tests |
| 8 | PASS в доступной конфигурации | Два монитора, actual physical restore; missing hardware matrix ограничена fake tests, без изменения дисплеев пользователя |
| 9 | PASS для framework | Полный framework suite 779 PASS / 2 expected skips; consumer baseline-only Repeater failure отдельно |
| 10 | Framework PASS; consumer gate NEEDS-FIX | Optional capability/custom unsupported/Headless framework checks прошли; consumer 50/51, product defect §14 не скрыт |
| 11 | Pointer outcome PASS | Все затронутые consumer pointer scenarios проходят full repeat; последние TaskLoading/TaskSpaces cleanup изменения отдельно PASS |

Общий regression gate остаётся **NEEDS-FIX**, поскольку consumer suite не полностью зелёный. Это не отменяет подтверждённый результат click/hover/drag и framework PASS, но не позволяет объявить всю интеграцию завершённой без оговорок. Утверждение дополнения §14 не заменяет разрешение на публикацию.

Итоговый независимый проход по final diff/evidence завершён: незакрытых **BLOCKER/HIGH/MEDIUM/LOW в pointer diff нет**, **consumer AC11 PASS**. Reviewer проверил последние два targeted TRX, финальную сборку, соответствие textbox выбранному удаляемому объекту и отсутствие physical replay. Первое замечание про cleanup ComboBox закрыто явным ограниченным ожиданием готовности и закрытия; дополнительный повторный выбор Settings space удалён. Reviewer отдельно подтвердил оба full-suite counters и идентичный baseline Repeater locator failure. Общий gate осознанно оставлен NEEDS-FIX по двум описанным consumer issues, без подмены результатов зелёным статусом.

## Approval

Реализация разрешается только после **«Спеку подтверждаю»** по central `quest-mode.md`. Включены framework и описанная локальная интеграция Unlimotion в изолированном worktree. Публикация, push/PR/release и установка за пределами этой проверки не разрешаются автоматически.

После локальной реализации пользователь отдельно поручил **«Оформи PRы»**. Это разрешает commit, push рабочих веток и создание связанных PR для framework и consumer. Это не подтверждение product fix из §14 и не разрешение на merge, релиз или публикацию NuGet-пакетов. Переносимая выборка evidence: [validation report](../docs/validation/2026-10-02-pointer/README.md).

## 20. Журнал действий агента

| Фаза/событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC / исследование | Проверены пути mouse input и первичные контракты FlaUI; подтверждённые дефекты отделены от причин рывков | F1–F11, H1–H4; native repro не выполнялся | Дизайн и acceptance | Поручено исследование и SPEC | Этот файл |
| SPEC / дизайн | Выбран общий lifecycle + semantic-first + три явных жеста | Контракты §6, матрица §11 | Post-SPEC review | Реализация не подтверждена | Этот файл |
| SPEC / уточнение Unlimotion | Найдены явные MoveTo в угол и к disabled status row; миграция consumer обязательна для результата | U1–U4, resolved FlaUI 5.0.0; native repro ещё не выполнен | Review уточнённой SPEC | Пользователь: «в Unlimotion» | Этот файл; consumer прочитан без изменений |
| SPEC / review завершён | Self-review и отдельный writable reviewer fallback закрыли cleanup/async/package findings | Post-SPEC PASS; runtime проверки только в плане EXEC | Ожидать «Спеку подтверждаю» | Approval пока нет | Этот файл |
| EXEC / начало | Пользователь дал exact approval, scope §6/§13 принят | Проверить состояние checkout, toolchain и baseline; реализация и проверки впереди | Реализация framework и isolated consumer | «Спеку подтверждаю» | Framework и отдельный worktree Unlimotion |
| EXEC / framework | Общий controller, optional API, semantic-first и legacy migration реализованы; fault/native проверки и отдельный review пройдены | 779 PASS, 2 expected skips; реальные click/tooltip/drag и возврат между мониторами | Завершить consumer regression | Новое решение не требуется | `artifacts/pointer/validation/`, `native-final/`, `docs/appautomation/pointer-input.md` |
| EXEC / consumer evidence | Удалена парковка; scoped hover сохраняет tooltip до assertion/screenshot; `.2` packages локальные | Final Blocked PASS, before/after video и OS restore; full suite выявил migration/fixture failures | Исправить миграцию и проверить повторно | В рамках принятой SPEC | Consumer `artifacts/pointer/consumer-validation.md` и `after-final/` |
| EXEC / consumer regression | Миграция raw input прошла full repeat; Headless shown-window fixture выявил отдельный product defect | FlaUI 26 PASS / 1 baseline FAIL / 1 skip; Headless 50 PASS / 1 remote-selection FAIL | Закончить точечный ComboBox recheck/review; product fix только после расширения scope | Дополнение §14 предложено, ответ ожидается | Consumer full logs/TRX, `settings-remote-writeback.txt` |
| EXEC / завершение pointer scope | Последние TaskLoading/TaskSpaces rechecks прошли; ненужный повторный выбор Settings удалён; desktop освобождён | Framework PASS и AC11 PASS; общий regression gate NEEDS-FIX по двум consumer issues | Product HTTP→SSH fix только после отдельного подтверждения §14; baseline Repeater issue отдельно | Ответ на предложенное расширение пока не получен | Финальные TRX, отчёт consumer, эта SPEC |
