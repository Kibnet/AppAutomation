# Скриншоты в headless-тестах AppAutomation

## 0. Метаданные

- Тип: delivery-task; stack `dotnet-desktop-client`, context `testing-dotnet`; требования `ui-automation-testing` применены к доказательствам тестового сценария.
- Владелец: пользователь — результат и approval; агент — реализация и проверка.
- Масштаб: medium. Expanded SPEC обязательна из-за нового публичного API, файловых артефактов и интеграции runtime / TUnit / template.
- Baseline: центральный `model-behavior-baseline`; поверхность Codex, Windows/PowerShell. Изменение модели/промпта не выполняется; model eval не применим.
- Репозиторий: `C:\Projects\My\AppAutomation`, ветка `master`, исходный HEAD `8fe6994`; рабочее дерево до SPEC чистое. Публикация пакетов, push и PR не входят в поручение.
- Canonical template: `C:\Users\Kibnet\.codex\agents\templates\specs\_template.md`.
- Stack: `creator-vibe-lens`, `model-behavior-baseline`, `tool-execution-baseline`, `collaboration-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `spec-linter`, `spec-rubric`, `review-loops`. Локальный `AGENTS.override.md` отсутствует.
- Зависимости репозитория: Avalonia / Avalonia.Headless 12.0.4, TUnit 1.61.29, SDK 10.0.202, MTP; библиотека поддерживает net8.0 и net10.0.
- Фаза: SPEC, исполнение пока не разрешено exact approval.

## 1. Overview / Цель

Исходное наблюдение: агенты не получают скриншоты через фреймворк headless-тестирования. Пользователь принял предложение добавить штатную возможность, автоматические снимки ошибок и пример для агентов: «Давай сделаем!».

Success means: автор теста получает открываемый PNG одной командой AppAutomation без видимого desktop-окна; при падении поддерживаемого сценария PNG сохраняется автоматически; новый потребитель находит рабочую настройку и пример в шаблоне и README.

Итог: код, регрессионные тесты, обновлённый consumer template, документация и лично просмотренные PNG из настоящего тестового прогона. Stop rules: сначала exact approval; после EXEC — все обязательные проверки и post-EXEC review. Недоступный рендеринг или красный gate обозначать как незавершённую проверку, а не успешную поставку.

## 2. Текущее состояние (AS-IS)

- `src/AppAutomation.Avalonia.Headless/Session/DesktopSession.cs`: `DesktopAppSession.MainWindow` даёт Avalonia Window; собственного метода сохранения PNG нет.
- `Session/HeadlessRuntime.cs`: общая `HeadlessUnitTestSession` и синхронные Dispatch-обёртки. Новая операция обязана работать с UI-потоком и не вкладывать блокирующий Dispatch в тот же dispatcher.
- `Automation/HeadlessControlResolver.cs`: `SupportsScreenshots: false`; `CollectAsync` возвращает metadata и inline text для logical-tree/control-state, без PNG.
- `UiPageExtensions.AttachArtifacts` вызывает collector при ошибке операции. Это не покрывает обычный упавший TUnit assertion вне операции страницы.
- `src/AppAutomation.TUnit/UiTestBase.cs`: after-test hook только освобождает сессию; захвата перед Dispose нет.
- Sample и template запускают `HeadlessUnitTestSession.StartNew` с типом приложения. Явной настройки Skia / `UseHeadlessDrawing = false` нет.
- Sample factory создаёт `new MainWindow()` без Show; resolver тоже не показывает окно. Для реального render требуются явные Show/Close в тестовом harness, одного включения Skia недостаточно.
- `UiFailureArtifact` содержит относительный путь и metadata; наличие этой записи само по себе не означает запись файла.
- У FlaUI также обнаружен capture без сохранения PNG в `CreateScreenshotArtifact`. Исправление FlaUI — отдельная задача, здесь не обещается.
- `.github/workflows/pr-validation.yml`: restore, Release build, полный solution test, pack, smoke-consumer. `eng/smoke-consumer.ps1` подставляет bootstrap по текстовым шаблонам; изменения scaffold должны сохранить этот путь.

Проверенные внешние источники: [Avalonia 12.0.4 HeadlessUnitTestSession](https://github.com/AvaloniaUI/Avalonia/blob/12.0.4/src/Headless/Avalonia.Headless/HeadlessUnitTestSession.cs), [HeadlessWindowExtensions](https://github.com/AvaloniaUI/Avalonia/blob/12.0.4/src/Headless/Avalonia.Headless/HeadlessWindowExtensions.cs). `StartNew` принимает тип с `BuildAvaloniaApp`; CaptureRenderedFrame запускает обновление кадра и может вернуть null. Рендеринг надо включать до старта сессии.

## 3. Проблема

У фреймворка отсутствует законченный путь «настроить реальный headless-рендеринг → получить и сохранить кадр → найти артефакт». Одной инструкции про Avalonia недостаточно: потребитель снова вынужден писать инфраструктуру.

## 4. Цели дизайна

- Один механизм захвата для явного снимка и диагностики ошибок.
- Настройка рендеринга в test bootstrap, без изменения production App.
- Реально существующие PNG, ясные пути, отсутствие коллизий автоматических артефактов.
- Совместимость существующих конструкторов, сессий без рендеринга и runtime-neutral TUnit.
- Сбой диагностики не скрывает причину падения теста и не мешает cleanup.

## 5. Non-Goals

Pixel-diff / baseline-система; видео; скриншоты рабочего стола; перенос/композиция нескольких окон и popup TopLevel в одну картинку; исправление FlaUI; полный redesign всех text artifacts; production UI; автоматическая публикация артефактов; изменение центральных агентских инструкций; релиз/установка пакетов.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- Headless capture helper: получение кадра на UI dispatcher, сохранение и освобождение bitmap.
- `DesktopAppSession`: удобный публичный вход `CaptureScreenshot(string filePath)`, возвращающий абсолютный путь сохранённого PNG.
- `HeadlessControlResolver`: screenshot в существующей диагностике операций; сохранение logical-tree/control-state поведения.
- TUnit base: дополнительная виртуальная точка сбора failure artifacts перед Dispose; по умолчанию пустая, без зависимости TUnit от Avalonia.
- Sample/template: rendering bootstrap и headless override failure hook, готовые к использованию после обычной настройки AUT.
- Документация: короткий путь от нового шаблона и миграционный рецепт для существующего проекта.

### 6.2 Детальный дизайн

**Явный снимок.** `session.CaptureScreenshot(path)` требует живую сессию и уже показанное тестом окно. Не показывает скрытое окно, не меняет его размеры или состояние. Создаёт отсутствующий родительский каталог, сохраняет PNG и возвращает `Path.GetFullPath(path)`. Не перезаписывает существующий файл молча: для заданного занятого пути — понятная ошибка. Пример в документации показывает уникальное имя либо отдельный output-каталог прогона. Null/empty path, disposed session, скрытое/закрытое окно, отсутствие кадра, ошибочная конфигурация и I/O имеют диагностируемые ошибки, а не фиктивный успех.

Получение кадра и освобождение Avalonia-ресурсов выполняются в корректном UI-контексте. При вызове уже внутри UI dispatch используется прямой путь, исключающий self-deadlock. Не добавлять произвольные задержки: использовать CaptureRenderedFrame и существующие средства синхронизации; ожидание бизнес-состояния остаётся обязанностью сценария. По возможности кодирование/запись не удерживает UI-поток; без небезопасного переноса bitmap между потоками. Не возвращать success до закрытия файла. При ошибке записи не оставлять частичный файл с видом готового PNG; удалять только созданный текущей операцией файл.

**Рендеринг.** В sample и scaffold добавить test-only builder с публичным `BuildAvaloniaApp`, подключающим Skia и `.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })`; `StartNew` получает тип этого builder. Настройку, шрифты и ресурсы AUT сохранить. Нужные PackageReference добавить с текущей версией Avalonia, без upgrade. Для существующих потребителей переход явный: старый bootstrap не переключать глобально. Показать, как сохранить пользовательский AppBuilder и добавить headless-рендеринг последним шагом, не вызывая desktop lifetime.

Headless wrapper в sample и шаблоне явно показывает своё MainWindow через dispatcher после Launch и закрывает перед Inner.Dispose (также через dispatcher и с гарантированным cleanup при ошибке). Тесты, использующие session напрямую, делают Show/Close в своей fixture. Показ headless Window не создаёт native desktop-окно. Общий DesktopAppSession.Launch и capture не получают скрытого auto-show/auto-close поведения. Это исключает накопление показанных окон в общей PerAssembly session и изменение legacy launch semantics.

**Диагностика операций.** Существующий `HeadlessControlResolver(window)` сохранить; дополнительными options разрешить задать каталог артефактов. Default root — `Path.Combine(AppContext.BaseDirectory, "artifacts", "ui-failures", "avalonia-headless")`. Каждый capture получает уникальный подкаталог (случайный ID, без зависимости только от времени). Имена тестов не использовать как непроверенный путь. Успешный `UiFailureArtifact` содержит `Kind = screenshot`, `ContentType = image/png`, RelativePath относительно AppContext.BaseDirectory и краткие размеры/абсолютный путь в диагностическом выводе. Root для failure artifacts должен находиться на том же filesystem root, что AppContext.BaseDirectory: конфигурация на другом диске/UNC root отвергается при настройке с понятным сообщением, иначе Path.GetRelativePath может вернуть абсолютный путь вопреки контракту record. Путь может содержать `..` для явно выбранного внешнего root того же тома; это разрешённая конфигурация caller, не имя теста. Ограничение не касается explicit CaptureScreenshot, возвращающего absolute path.

При невозможности снимка вернуть `screenshot-unavailable` с причиной, не выдавать несуществующий PNG за screenshot. Его RelativePath — диагностическое имя, inline reason; документация различает unavailable record и сохранённый файл. Logical-tree/control-state сохранить даже при проблеме PNG. `SupportsScreenshots` отражает доступность захвата в текущем окне: fake drawing и скрытое окно не объявляются готовыми к захвату, а созданный до `Show` resolver меняет флаг после показа окна. Backend проверяется чтением `GetLastRenderedFrame` без запуска рендера и записи на диск; отсутствие кадра до `Show` не означает отсутствие Skia. Transient I/O error не меняет renderer capability. Backend пробуется один раз при создании resolver, видимость читается при запросе capability; тестовая сессия не пробует рендер на каждом обращении к контролу.

**Падение теста.** В `UiTestBase` добавить защищённый virtual hook `ValueTask<IReadOnlyList<UiFailureArtifact>> CollectFailureArtifactsAsync(CancellationToken)` (пустой default), вызываемый after-test при failure до очистки полей и Dispose. Существующий `public void CleanupUiSession()` сохранить для прямых callers; `[After(Test)]` перенести на отдельный async adapter, который await-ит hook и вызывает cleanup в finally. Не оставлять две регистрации after-test и не блокировать async hook синхронным ожиданием. В sample и template headless override вызывает общий capture helper для session.MainWindow. Артефакты и абсолютные пути выводятся в тестовый output. Успешный тест сам по себе снимок не создаёт. Ошибка hook логируется отдельно, исходный assertion остаётся главным failure, Dispose выполняется в finally. TUnit integration проверить на установленной 1.61.29; не считать unit-вызов hook доказательством его настоящего after-test порядка.

Граница автоматики: ошибки UI-операций — через resolver; обычные assertion/test-body failures — в классе на UiTestBase с установленным headless override (sample/template уже содержат его). Произвольные тесты вне этой base, аварийное завершение процесса, teardown error после Dispose и зависший dispatcher не обещаются как гарантированно снимаемые. Для них остаётся явный capture либо hook, показанный в документации.

**Документация для агентов.** Новый `docs/appautomation/headless-screenshots.md` на русском: команды, точные using/API, bootstrap, explicit/failure пример, output root, различия assertion и UI-operation capture, troubleshooting fake drawing/hidden window/null frame/native Skia. README, PACKAGE_README, quickstart и APPAUTOMATION_NEXT_STEPS ведут к рецепту. Существующие двуязычные разделы обновляются согласованно. Действие агента: выполнить сценарий, открыть PNG средством просмотра изображений, проверить нужное состояние, приложить ссылку; прохождение assertions не выдавать за просмотр изображения.

Visual planning artifact: новый UI/layout не создаётся. Вместо макета — acceptance fixture: одно окно фиксированного размера, два различимых цветных участка, читаемая надпись состояния A; после действия надпись/цвет меняются на B. Эти два PNG являются проверкой захвата текущего состояния, не новым дизайном AUT. Дополнительно получить кадр реального sample MainWindow после пользовательского действия.

Video evidence: изменение инфраструктуры сохранения статического PNG, а не UI flow AUT; новое видео не требуется. До изменения публичного capture нет. Evidence после — PNG A/B, sample PNG, диагностический PNG и логи тестов. Снимок headless не объявляется проверкой native window decorations или desktop composition.

### 6.3 User-Observable Scenarios

| Scenario | Trigger | Visible result | Evidence | AC |
| --- | --- | --- | --- | --- |
| Явный снимок | Вызвать CaptureScreenshot после UI-действия | Открываемый PNG текущего состояния, путь в результате | Decode + просмотр sample PNG | AC1, AC2 |
| Ошибка операции | Ожидать заведомо неверное состояние | Исходный UiOperationException + существующий PNG | Failure test и файл | AC3 |
| Обычный assertion | Assertion failure в sample/template UiTestBase | PNG до Dispose, путь в test output | Изолированный negative runner smoke | AC4 |
| Нет рендеринга | Старый fake drawing bootstrap | Ясная причина; assertion/cleanup сохранены | Negative tests | AC5 |
| Новый потребитель | Пройти README и scaffold | Рабочий bootstrap, capture и failure recipe без reverse engineering | Pack/consumer smoke + скриншот | AC6 |

### 6.4 State / Interaction Matrix

| State | Trigger | Result | Error/concurrent case |
| --- | --- | --- | --- |
| Rendering + shown | Explicit capture | PNG текущего кадра | Занятый explicit path не перезаписывается |
| Fake drawing | Capture | Настройка Skia объяснена в ошибке | Failure collector возвращает unavailable |
| Hidden / closed / disposed | Capture | Понятная ошибка, без изменения видимости | Нулевой/старый кадр не принимается |
| Failed test + live session | After-test | Снимок, затем cleanup | Ошибка снимка не отменяет cleanup |
| Passed test | After-test | Только cleanup | Нет лишнего автоматического PNG |
| Несколько failure captures | Уникальные output paths | Все файлы сохранены | Коллизии защищены exclusive create; UI work сериализован |

### 6.5 Decision Ledger

| Decision | Owner | Chosen option | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | --- | --- | --- |
| Публичный вход | agent | CaptureScreenshot на headless session | 0.95 | Additive API требует тестов | Нет |
| Rendering default | agent | Включён в новом scaffold/sample, opt-in для старого bootstrap | 0.9 | Возможные новые Skia dependencies | Нет |
| Обычные assertions | agent | Optional TUnit failure hook + готовый headless override | 0.85 | Порядок hooks проверить runner smoke | Нет |
| Screenshot root | agent | AppContext.BaseDirectory + настраиваемый root | 0.9 | Read-only root должен давать unavailable | Нет |
| Native desktop и FlaUI | agent | Отдельный scope | 1.0 | Не заявлять универсальные screenshots | Нет |
| Технические уточнения API | agent | Внутри описанных совместимости и AC | 0.9 | Существенное изменение scope требует новой SPEC | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Area | Source of truth | Change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Rendering | Test AppBuilder | Skia + real drawing | Старый fake mode сохранён | Separate-process fake/render tests |
| Artifact paths | Capture argument / resolver options | PNG на диске; root и относительная база документированы | UiFailureArtifact record не ломается | Decode returned path + collision check |
| TUnit cleanup | UiTestBase after-test | Optional pre-dispose hook | Existing derived classes работают без override | Full tests + negative runner smoke |
| Templates | AppAutomation.Templates | Готовая rendering/capture wiring | Обычные AUT placeholders остаются | Pack + generated-consumer smoke |

## 7. Бизнес-правила / Алгоритмы

Success возвращается только после сохранения; каждый автоматический снимок уникален; capture не меняет пользовательское состояние окна; отсутствие renderer не скрывается пустой картинкой. Primary failure приоритетнее ошибки диагностики. Все bitmap/stream освобождаются. Файлы не публикуются автоматически.

## 8. Точки интеграции и триггеры

Explicit session API → общий helper; resolver.CollectAsync → тот же helper; failed-test hook → headless override → тот же helper. Bootstrap конфигурируется до StartNew/первого dispatch. Failure hook работает до Dispose. Никакого переключения renderer в действующей глобальной сессии.

## 9. Изменения модели данных / состояния

Новые PNG и capture options; БД и application settings не меняются. Публичные существующие record/конструкторы и сигнатуры сохраняются. Глобальное состояние ограничено уже существующей headless session; режимы fake/render проверяются в отдельных процессах.

## 10. Миграция / Rollout / Rollback

Старому потребителю: обновить пакет после отдельной публикации, добавить Skia bootstrap, применить пример failure hook. Новый scaffold содержит эти шаги в коде. Этот task завершается локальной реализацией и pack/smoke, не публикацией. Rollback — убрать добавленные API/wiring через обратный diff текущей задачи, не затрагивать пользовательские изменения; вернуться к прежнему bootstrap. PNG — локальные test artifacts; никаких очисток чужих каталогов.

## 11. Тестирование и критерии приёмки

| AC | Automated test/check | Visual/manual evidence | Если не выполнено |
| --- | --- | --- | --- |
| AC1: capture сохраняет настоящий PNG текущего окна | PNG decode, размеры, различимые pixels; второй кадр после смены состояния отличается ожидаемым участком | Открыть оба fixture PNG и sample MainWindow PNG | EXEC не завершён |
| AC2: простой и безопасный API | Вызов из test thread и UI dispatch; каталог с пробелами/Unicode; duplicate path; disposed/hidden/closed; bitmap cleanup; failure root на другом filesystem root явно отклоняется без записи | Путь из результата реально открывается | EXEC не завершён |
| AC3: операция собирает PNG и прежнюю диагностику | Existing HeadlessFailureDiagnosticsTests расширены: UiOperationException.InnerException и logical-tree/control-state сохранены, screenshot file декодируется | PNG соответствует состоянию на failure | EXEC не завершён |
| AC4: assertion failure снимается до cleanup | Изолированный runner scenario с заведомым assertion failure; outer verification проверяет nonzero exit, marker assertion, PNG, cleanup marker; passing control не создаёт automatic PNG; error capture сохраняет primary failure | Output содержит абсолютный путь | EXEC не завершён |
| AC5: negative/runtime compatibility | Fake drawing в отдельном процессе; null frame; I/O failure на детерминированном invalid target; unavailable + исходная ошибка; повторные automatic captures не перезаписываются | Troubleshooting соответствует ошибкам | EXEC не завершён |
| AC6: discoverability + consumer | Generated consumer собирается и выполняет explicit capture; страницы README/quickstart/next steps содержат путь к рецепту | Агент следует рецепту, открывает и прикладывает generated-consumer PNG | EXEC не завершён |
| AC7: regression/build/package gates | Полные repo build/test/pack/smoke, net8/net10 библиотека | Логи команд; diff без unrelated changes | Называть blocker, не PASS |

Обязательный набор: новые targeted tests, полный headless sample suite, полный solution test и Release build, pack и consumer smoke. Основание: публичный API, общий TUnit lifecycle и явные PR gates. Перед выполнением — SDK/restore preflight. Команды репозитория:

```powershell
dotnet --info
dotnet restore AppAutomation.sln
dotnet run --project sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests -- --treenode-filter "/*/*/HeadlessScreenshotTests/*" --maximum-parallel-tests 1
dotnet run --project sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests -- --maximum-parallel-tests 1
dotnet build AppAutomation.sln -c Release --no-restore
dotnet test --solution AppAutomation.sln -c Release --no-build
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release -SkipPack
```

Новые test class names уточняются при реализации; final evidence содержит фактические команды. Negative runner fixture не включать как неизбежно красный тест normal suite: запуск управляется outer test/smoke и проверяет ожидаемый failure. Frame/assertion тесты проверяют наблюдаемое поведение, не только флаг SupportsScreenshots. Перед долгими командами фиксировать progress/log path; после timeout исследовать причину, не повторять без новой гипотезы. После зелёного обязательного набора проверки повторяются только для новых diff/failure/risk.

## 12. Риски и edge cases

Skia native assets/шрифты; отличия headless от native rendering; thread affinity и sync deadlock; сохранение stale кадра закрытого окна; уникальность путей; захват после Dispose; template text replacement в smoke-consumer. Профилактика — реальные renders, отдельные fake/render процессы, test-runner negative smoke, полный consumer gate. Pixel checks используют простые цветные области, а не brittle exact font snapshots. Размеры AUT не меняются ради снимка.

### Expected User Review Objections

| Objection | Why likely | Mitigation | Status |
| --- | --- | --- | --- |
| «Путь есть, а картинки нет» | Текущий metadata-only механизм | Decode реально сохранённого PNG по возвращённому пути | mitigated |
| «Сняли только timeout, обычное падение пропустили» | Collector вызывается из UI operations | TUnit override + настоящий runner smoke | mitigated |
| «Агенты всё равно не найдут как» | Нет рецепта в entry points | README/quickstart/template + runnable consumer | mitigated |
| «Включили тяжёлый renderer всем» | Старые тесты используют fake drawing | Старый bootstrap сохраняется; migration opt-in | mitigated |

Rework Prevention Checklist: исходный сценарий назван; каждый outcome связан с AC; решения и границы автоматики явные; review objections закрыты планом; проверка ролей — раздел 19; все AC описывают результат, не только подготовку.

## 13. План выполнения

1. После approval: проверить dependency API/runner hooks и добавить regression fixtures, которые выражают отсутствие настоящего capture; compilation failure нового API не называть behavioral RED.
2. Реализовать capture/storage, session API и operation diagnostics; покрыть renderer/error/thread/path случаи.
3. Подключить optional TUnit hook, sample, scaffold и настоящий failing-test smoke.
4. Обновить документацию и consumer smoke; выполнить обязательные проверки и лично открыть PNG.
5. Провести post-EXEC review и сверить исходный сценарий по AC; финально дать результат, ссылки на PNG и границы проверенного.

## 14. Открытые вопросы

Существенных пользовательских решений по дизайну нет. Требуется только exact approval текущей SPEC (отдельный фазовый gate, не незакрытое design decision). Внутренние имена options/helper и способ чтения runtime capability агент уточняет по зависимостям в утверждённом scope. Если доказуемая реализация потребует изменения совместимости/результата — обновить SPEC до зависимых изменений.

## 15. Соответствие профилю

UI dispatcher учитывается; sample automation IDs и UX не меняются; TUnit/MTP команды соответствуют pinned runner; real PNG и visually inspected evidence обязательны. Build/test/pack/consumer gates не заменяются mock-тестами. Для нового infrastructure API вместо UI redesign/video описан статический acceptance fixture.

## 16. Таблица изменений файлов

| Files | Change | Reason |
| --- | --- | --- |
| `src/AppAutomation.Avalonia.Headless/Session/DesktopSession.cs`, новый capture helper/options | API, PNG persistence | Один вызов пользователя |
| `src/AppAutomation.Avalonia.Headless/Automation/HeadlessControlResolver.cs` | Failure PNG, capability | Автоматика UI operations |
| `src/AppAutomation.Avalonia.Headless/Session/HeadlessRuntime.cs` при необходимости | Безопасный UI dispatch для capture | Self-deadlock avoidance; не общий refactor |
| `src/AppAutomation.TUnit/UiTestBase.cs` | Optional pre-dispose failure hook | Assertion failures |
| `sample/DotnetDebug.AppAutomation.Avalonia.Headless.Tests/**` | Bootstrap, capture/error tests, override | Реальное sample evidence |
| Isolated negative-runner/fake-render fixtures в test tree, соответствующие csproj/sln при необходимости | Проверка lifecycle и режимов | Процессная изоляция |
| `tests/AppAutomation.Recorder.Avalonia.Tests/RecorderTests.cs` (delivery validation fix) | Считать UI state до async assertions в одном flaky тесте | Разблокировать обязательный полный CI gate после поручения merge; product behavior не меняется |
| `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/**` | Builder, hook, capture example, next steps | Новый потребитель |
| `Directory.Packages.props`, затронутые csproj при необходимости | Avalonia.Skia текущей версии | Явные rendering dependencies |
| `eng/smoke-consumer.ps1`, связанные template checks | Реальный PNG в generated consumer: synthetic Avalonia App/Window вместо нынешнего non-runnable TestHost placeholder; запуск screenshot-сценария | Защита bootstrap/scaffold; одного build недостаточно |
| `README.md`, `docs/appautomation/{PACKAGE_README,quickstart,compatibility,headless-screenshots}.md` | Discoverability и recipe | Пользователь/агент знает путь |
| Текущая SPEC | Журнал и evidence | QUEST |

## 17. Таблица соответствий (было → стало)

| Area | Before | After |
| --- | --- | --- |
| Explicit capture | Собственный код Avalonia | session.CaptureScreenshot(path) |
| Operation failure | Tree/state metadata | Tree/state + настоящий PNG или причина unavailable |
| Assertion failure | Dispose без снимка | В готовом headless harness PNG перед Dispose |
| Bootstrap | Fake drawing | Новый scaffold с rendering; прежний opt-in |
| Onboarding | Рецепта нет | Копируемый runnable recipe со ссылками |

## 18. Альтернативы и компромиссы

- Только документация Avalonia: мало кода, но не выполняет поручение о штатном API и автоматике.
- Всегда переключать все headless-сессии на Skia: проще default, но меняет существующие consumer/runtime требования. Отклонено.
- Универсальный screenshot API для всех adapters: расширяет контракт FlaUI и других платформ. Отложено; headless-first решает текущую задачу.
- Только failure collector: не покрывает successful checkpoints и обычные assertions. Поэтому explicit API и optional TUnit hook входят в один результат.

## 19. Результат quality gate и review

### SPEC Linter Result

| № / блок | Статус | Основание |
| --- | --- | --- |
| 1 / A outcome | PASS | §1 и сценарии §6.3 |
| 2 / A AS-IS | PASS | §2: session/resolver/hooks/template/CI проверены |
| 3 / A проблема | PASS | §3: отсутствует весь PNG workflow |
| 4 / A цели | PASS | §4: capture/storage/discoverability/compatibility |
| 5 / A границы | PASS | §5: native/FlaUI/video/publication исключены |
| 6 / B ответственности | PASS | §6.1 и file map |
| 7 / B интеграция | PASS | §8: bootstrap → runtime, failure → pre-dispose |
| 8 / B инварианты | PASS | §7: real file, current state, primary failure |
| 9 / B errors | PASS | §6.2, §6.4: unavailable, I/O, disposed, cleanup |
| 10 / B performance | PASS | Opt-in для legacy, нет captures на каждом success, UI affinity без self-deadlock |
| 11 / C state | PASS | §9: только test artifacts, существующая session |
| 12 / C compatibility | PASS | §10: additive API, старый bootstrap, старый direct cleanup |
| 13 / C rollback | PASS | §10: обратный scoped diff, без удаления чужих файлов |
| 14 / D AC | PASS | §11 AC1–AC7 наблюдаемы |
| 15 / D evidence | PASS | Decode/pixels/visual/runner/consumer включая negative |
| 16 / D commands | PASS | CI commands и stop rules §11 |
| 17 / E plan | PASS | §13: dependencies, targeted→full→consumer→review |
| 18 / E decisions | PASS | §6.5/§14: design решения приняты, exact approval отдельно |
| 19 / E scale | PASS | §0: expanded из-за public/runtime/template |
| 20 / F profile | PASS | §15: dispatcher, TUnit, PNG evidence, build/test gates |

Итог: ГОТОВО к запросу approval; это оценка плана, не результат runtime-проверок.

### SPEC Rubric Result

| Критерий | Балл | Основание |
| --- | --- | --- |
| Цель/границы | 5 | Исходный symptom, explicit/operation/assertion outcomes, non-goals |
| AS-IS | 5 | Инспекция кода и pinned dependency source, выявлен missing Show |
| Дизайн | 5 | Capture, disk contract, dispatch, backend, hook/lifecycle и bootstrap заданы |
| Безопасность/rollback | 5 | Compatibility, no-overwrite, primary exception, локальные файлы |
| Проверяемость | 5 | AC с реальными PNG, negative runner и consumer smoke |
| Автономность | 5 | Нет незакрытого user-owned design решения, bounded technical choices |

Итог: 30/30, готово к автономному выполнению после exact approval. Оценка не подтверждает реализацию и не снимает обязательные EXEC gates.

### Role-Based Review Result

| Role | Applicability | Review question / result | Verdict |
| --- | --- | --- | --- |
| Business analyst | Не применимо | Бизнес-правила AUT не меняются; developer workflow описан отдельными сценариями | N/A |
| UX / artifact consumer | Применимо | Один вызов, открываемая картинка, видимость в README; empty image не является success | PASS |
| Tester | Применимо | Assertion failure проверяется настоящим runner, PNG декодируется и просматривается; есть fake/I/O/cleanup controls | PASS |
| Developer / architect | Применимо | Renderer до StartNew; Show/Close в harness; async adapter сохраняет direct cleanup API | PASS |
| Operations / filesystem | Применимо | Уникальные paths, explicit no-overwrite, same-root RelativePath; без публикации | PASS |

### Post-SPEC Review

- Scope reviewed: текущая SPEC; центральные phase/linter/rubric/review/testing owners; перечисленные §0 профили; planned file map; открытые вопросы §14.
- Evidence inspected: DesktopSession, HeadlessRuntime, HeadlessControlResolver, UiDiagnostics, UiPage и AttachArtifacts, UiTestBase/IUiTestSession, sample factory и wrappers, template hooks/csproj/TestHost, Directory.Packages.props, global.json, pr-validation.yml, eng/smoke-consumer.ps1, pinned Avalonia source/XML и TUnit TestResult XML. Git preflight: только текущая SPEC добавлена, исходный master чистый.
- Scope/Evidence pass: подтверждено отсутствие PNG API и storage, fake bootstrap, не показанное sample окно, compile-only generated consumer smoke.
- Contract pass: исходный результат разложен на explicit capture, operation failure, assertion failure и onboarding; для каждого есть AC. Legacy constructors/launch/direct cleanup сохраняются. Никакой публикации/central-instruction mutation.
- Отдельный reviewer `/root/review_headless_spec` выполнил инспекцию и вернул PASS после исправления paths. Effective sandbox оказался danger-full-access: технически read-only independent review недоступен, несмотря на выбранную роль. Reviewer выполнял только чтение/поиск и вычисление пути, не менял файлы и не запускал builds. Этот проход не объявляется технически изолированным read-only review.
- Adversarial fallback главного агента при недоступном read-only sandbox: проверены контрпримеры hidden window без render, PNG на другом диске, capture из UI thread, assertion вне UiPageExtensions, ошибка сохранения перед Dispose, несколько окон в PerAssembly, compile-only consumer и old cleanup callers. Для каждого задана проверка/граница; write capability reviewer остаётся процедурным риском, а не доказательством нарушения.
- Role-Based pass: результаты приведены выше; существенных нерешённых решений нет.

| Severity | Area | Finding | Action / disposition | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | paths | RelativePath невозможно сохранить при произвольном другом Windows-диске | Same-root validation только для failure artifact root; explicit absolute API без ограничения | fixed |
| MEDIUM | render/lifecycle | В sample нет Show; включение Skia не гарантирует кадр, а Show без Close накапливает окна | Show/Close в sample/template harness, hidden/closed checks и AC | fixed |
| MEDIUM | consumer evidence | Existing smoke только builds и подменяет bootstrap нерunnable типом | Synthetic AUT + настоящий capture execution, AC6 | fixed |
| LOW | async lifecycle | Sync-compatible cleanup мог привести к sync-over-async либо двойному hook | Старый public cleanup + один отдельный async after-test adapter | fixed |

- Fix and re-review: повторно сверены §6.2, state/path contracts, AC2/AC4/AC6, file map и scope. Reviewer повторно прочитал исправленный same-root контракт и закрыл finding. Главный агент сверил async adapter с текущим direct cleanup API; Show/Close остаются в harness, core Launch не меняет смысл.
- Depth checklist: scope drift/unrelated — только SPEC; AC — все семь имеют evidence plan; user scenarios/decisions/objections — заполнены; validation — commands взяты из CI; unsupported claims — runtime PASS отсутствует; regressions — fake renderer/I/O/cleanup/thread/collision перечислены; docs — recipe и entry points; hidden contract — additive API и opt-in migration; manual challenge — «сохранился ли файл при обычном assertion в реальном runner?» закрыт AC4.
- No-findings justification: не применимо к исходному проходу, findings были и исправлены. После повторной проверки новых блокирующих находок нет.
- Residual risks: фактические timing/state APIs TUnit и Skia native availability проверяются в EXEC; их непрохождение запрещает completion. Не доказана technical read-only изоляция reviewer, выполнен отдельный adversarial fallback.
- Needs human: только exact approval SPEC, дополнительных design-вопросов нет.
- Stop decision: PASS для фазы SPEC; можно запрашивать подтверждение. Implementation/build/tests/PNG пока не выполнены.

### Post-EXEC Review

- Статус: **NEEDS-FIX для общего release gate**. Функция и AC1–AC6 реализованы и проверены; AC7 остаётся красным из-за полного solution test. Это не `PASS` по QUEST, даже при зелёных затронутых модулях.
- Scope/Evidence pass: просмотрены текущие `git status --short`, `git diff --check`, публичные session/resolver/TUnit изменения, renderer hooks, шаблон, скрипты, CI и документация. `dotnet restore AppAutomation.sln` и финальная `dotnet build AppAutomation.sln -c Release --no-restore` прошли; последняя сборка — `artifacts/release-build-final4.log`, ошибок 0. Финальные headless 64/64 (`artifacts/headless-final4.log`), TestHost 25/25 (`artifacts/testhost-final4.log`), runner passing/assertion/capture-error controls (`artifacts/failure-smoke-final4.log`), `pack` (`artifacts/pack-final4.log`), generated consumer net8 real PNG и doctor 0/0 (`artifacts/consumer-smoke-final4.log`) прошли. Другие модули проверялись отдельно: Abstractions 131/131, Authoring 2/2, Build 19/19, Tooling 2/2, DotnetDebug.Tests 23/23; Recorder 308/309.
- Contract pass: AC1 — два PNG с красным/синим центральным пикселем, декодирование и размеры; визуально открыты оба кадра. AC2 — test/UI threads, Unicode, collision, скрытое/закрытое окно, Dispose и другой диск. AC3 — операция сохраняет декодируемый PNG, logical-tree/control-state и исходный timeout; повторные падения дают разные пути, недоступный каталог возвращает `screenshot-unavailable`. AC4 — отдельный runner сохраняет исходный assertion и выполняет cleanup при успешном захвате и при его ошибке; passing control не создаёт failure PNG. AC5 — fake drawing и null frame до Show не объявляются снимком; capability после Show меняется у renderer, у fake остаётся false. AC6 — шаблонный consumer исполняет screenshot test после локальной упаковки, PNG открыт и показывает ожидаемую заливку. Примерный MainWindow PNG открыт: вкладка Arm Desktop и её controls видны.
- Adversarial risk pass: review обнаружил ABI-регрессию от optional параметра публичного конструктора, статически неверную capability скрытого окна и недостаточные checks пикселей/error controls. Старая сигнатура восстановлена отдельным overload и проверена reflection; backend определяется `GetLastRenderedFrame` без renderer tick, видимость читается динамически; проверки дополнены и затронутые build/headless/TestHost/runner/pack/consumer повторены. Проверены недопустимый путь, double capture, source/UI thread, отсутствие silent overwrite, исходная ошибка теста. `git diff --check` завершился с кодом 0; предупреждения о CRLF относятся к настройке рабочего дерева.
- Role-Based pass: business workflow — N/A, AUT не меняется; UX/artifact consumer — реальные PNG визуально проверены, инструкция и ссылки доступны; tester — negative runner и проверки пикселей/ошибок; developer — overload сохраняет ABI, renderer probe не вызывает рендер; delivery — локальный pack/consumer прошли, публикации не было.
- Independent reviewer `/root/review_headless_spec` выполнил отдельную инспекцию в фактически writable `danger-full-access` среде: технический read-only sandbox недоступен, reviewer ничего не менял. После его findings главный агент сделал fix и повторно просмотрел затронутую поверхность; независимый reviewer не исполнял финальные тесты. Для UI-video evidence fallback объективен: headless окно не выводится на desktop, а до изменения fake renderer не давал пикселей; вместо видео сохранены открытые PNG реального sample и generated consumer, а цвет пикселей утверждается тестом.
- User-Observable Completion Gate: исходный сценарий «агент умеет сделать и открыть headless screenshot» проверен sample и generated consumer, failure screenshot тоже открываем. Обязательный общий validation gate AC7 не выполнен: два запуска `dotnet test --solution AppAutomation.sln -c Release --no-build` (обычный и последовательный, `artifacts/solution-test.log`, `artifacts/solution-test-serial.log`) дали `RecorderSmokeMultiSelectCapturesConfirmedAndCanceledSelections` / checkbox `Nu` без Toggle pattern; последовательный suite Recorder отдельно дал `Overlay_RendersStepNumbers_AndResetsAutoscrollStateAfterEmptyJournal` с Avalonia wrong UI thread (`artifacts/AppAutomation.Recorder.Avalonia.Tests-serial.log`). Исполнение solution было остановлено после зависания FlaUI-процесса, поэтому полный green result отсутствует. Файлы Recorder/FlaUI этим diff не менялись; это не доказательство, что падения существовали до diff. Исправление этих сценариев исключено §5, потому gate честно остаётся красным.
- Unrelated changes: untracked `sample/DotnetDebug.Avalonia/emxLicense.cs` не относится к реализации, не просматривался и не включается в поставку. Локальные `artifacts/` и временные consumer workspaces — результаты проверки, не публикация.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | API compatibility | Optional parameter заменял старую бинарную сигнатуру resolver | Восстановить `.ctor(Window)` и проверить reflection/build | fixed |
| MEDIUM | capability | Флаг замораживался false до Show и проба запускала renderer | Backend probe без render tick; динамическая видимость; hidden/show tests | fixed |
| MEDIUM | acceptance evidence | Hash PNG и exists не доказывали цвета/decode; не были покрыты error controls | Pixel/decode, repeated captures, invalid root, 3 runner controls | fixed |
| BLOCKER | AC7 full tests | Solution test не green; FlaUI и Recorder падения/зависание | Отдельно исправить/стабилизировать вне текущего screenshot scope и повторить полный solution gate | open |
| LOW | unrelated workspace file | `emxLicense.cs` появился untracked в sample AUT | Не добавлять к этому изменению | separated |

- Stop decision: **NEEDS-FIX**, не объявлять полный QUEST PASS. Внутри утверждённого screenshot scope дополнительных нарушений после fix/re-review не обнаружено; обязательный AC7 остаётся явно незакрытым. Человеческого design-решения не требуется. Не было commit/push/PR/release.

### Delivery validation после поручения «Влей в мастер»

- Статус: **PASS**, заменяет временное `NEEDS-FIX` первого post-EXEC review выше. Отдельный узкий коммит `c99ed60` стабилизировал только Recorder-тест `Overlay_RendersStepNumbers_AndResetsAutoscrollStateAfterEmptyJournal`: значения Avalonia-контролов считываются до первого асинхронного assertion, поэтому queued UI jobs выполняются на потоке, создавшем overlay. Код Recorder и поведение AUT не менялись. Полный Recorder suite после fix локально прошёл 309/309 (`artifacts/recorder-suite-fix.log`).
- Scope/Evidence и Contract re-review: проверены staged diff Recorder-теста, `git diff --check`, локальный полный Recorder suite и GitHub PR #32 на `c99ed6003946edb3f06ba4a68a73b9930cb7d78e`. [PR Validation run 36274850721](https://github.com/Kibnet/AppAutomation/actions/runs/36274850721) завершился успешно: Release build, полный solution Test **640/640**, failure screenshot smoke, pack и generated consumer smoke. Таким образом AC7 закрыт на чистом Windows runner; локальные красные логи выше сохранены как evidence среды/первого прохода, а не названы зелёными.
- Adversarial и Role-Based re-review: Recorder fix не ослабляет assertions — проверяются прежние counts и тексты в четырёх состояниях журнала; проверки выполняются после всех UI действий. Tester/delivery подтвердили полный CI, developer проверил отсутствие product-code diff в fix. Локальный FlaUI flake не воспроизвёлся в успешном CI; устойчивость FlaUI на этой машине остаётся отдельным низкоприоритетным риском, не блокирующим проверенный PR. Посторонний `emxLicense.cs` остался untracked и не вошёл в PR.
- User-Observable Completion Gate: AC1–AC6 и визуальные PNG подтверждены выше, AC7 подтверждён 640/640 и остальными CI шагами. Video fallback сохраняется: headless окно не имеет desktop surface, а целевой результат — PNG. Новых обязательных незакрытых условий нет. Stop decision: **PASS для слияния PR #32**, если состояние PR и head commit перед merge не изменятся. Коммит SPEC только фиксирует уже полученное evidence; повторного полного локального тестирования документация не требует.

## Approval

Фраза «Спеку подтверждаю» получена от пользователя 2026-09-27; фаза EXEC разрешена только в пределах этой SPEC.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC / обследование | Expanded: публичный API, renderer и TUnit lifecycle | Проверены HEAD/dirty state, session/resolver/UiTestBase, pinned dependencies, templates, CI и Avalonia source | Review текущего документа | «Давай сделаем!» — поручение, ещё не exact approval | Эта SPEC |
| SPEC / review и re-review | Закрыты paths, Show/Close, executable consumer smoke, async cleanup | Отдельная inspector review + основной adversarial fallback; linter PASS, rubric 30/30; runtime validation впереди | Получить exact approval | Ожидается «Спеку подтверждаю» | Эта SPEC |
| EXEC / переход | Exact approval получен, реализация screenshot feature разрешена | Scope AC1–AC7 без публикации/релиза | Код, тесты, template и docs | «Спеку подтверждаю» | Эта SPEC |
| EXEC / validation и review | AC1–AC6 проверены; HIGH ABI и MEDIUM capability/evidence исправлены | Build 0 errors, headless 64/64, TestHost 25/25, failure controls, pack, consumer PNG; полный solution test красный на Recorder/FlaUI | Отчёт с незакрытым AC7, отдельная стабилизация общего gate | Дополнительного выбора нет | Логи `artifacts/*final4.log`, PNG sample/consumer, эта SPEC |
| DELIVERY / повторный gate | По поручению «Влей в мастер» исправлен только Recorder test thread affinity, исходный scope функции не изменён | Локально Recorder 309/309; PR #32 CI `36274850721`: 640/640, failure PNG, pack, consumer smoke PASS | Зафиксировать результат в SPEC и слить проверенный head | «Влей в мастер» | Коммит `c99ed60`, PR #32, CI run 36274850721 |
