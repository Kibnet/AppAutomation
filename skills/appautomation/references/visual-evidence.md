# Скриншоты Headless, визуальная приёмка и показ результата

Читай, когда нужно увидеть реальное отрисованное состояние Avalonia, проверить визуальную фичу, сравнить с референсом или показать результат пользователю. Сверяй API с [документацией Headless screenshots](https://github.com/Kibnet/AppAutomation/blob/master/docs/appautomation/headless-screenshots.md) установленной версии.

## Получи пиксели из теста

Шаблон `appauto-avalonia` уже создаёт `RenderedHeadlessAppBuilder` и `HeadlessSessionHooks`. При ручной интеграции до `HeadlessUnitTestSession.StartNew` настрой тестовый builder:

```csharp
AppBuilder.Configure<MyAvaloniaApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
```

Перенеси в builder необходимые приложению шрифты/ресурсы. Создавай Avalonia controls только в launch callback/`HeadlessRuntime.Dispatch`; окно должно быть показано, иметь ненулевой размер и не быть закрытым. Дождись именно проверяемого бизнес-состояния обычным `WaitUntil`, затем в Headless test wrapper сохрани новый кадр:

```csharp
var path = Session.Inner.CaptureScreenshot(Path.Combine(
    AppContext.BaseDirectory, "artifacts", "headless-screenshots",
    Guid.NewGuid().ToString("N"), "feature-state.png"));
Console.WriteLine($"Headless screenshot: {path}");
```

`CaptureScreenshot` возвращает абсолютный путь после записи и не перезаписывает файл. Это кадр **области Avalonia Window**; системная рамка и отдельные native-окна в него не входят. Ошибки при скрытом/закрытом окне, освобождённой сессии или отсутствии Skia-кадра разбирай по причине, не выдавай пустой файл за доказательство.

## Используй снимок для приёмки

1. Открой PNG инструментом просмотра изображений и убедись, что это нужный экран, вкладка, модальное состояние и момент после действия. Проверка размера файла или успешный assertion не подтверждают внешний вид.
2. Сверь видимые признаки с задачей: расположение, читаемость, контент, активное/неактивное состояние, ошибку/успех. При референсе сравни сопоставимые масштаб и состояние; не заявляй pixel-perfect равенство, если тест проверяет только смысловые признаки.
3. Сохрани доступный пользователю artifact: ссылку на локальный PNG в текущем workspace, прикреплённый файл или CI artifact URL. В сообщении назови, что видно и какую часть требований кадр подтверждает. Если файл остаётся только в удалённом CI без публикации `artifacts/`, настрой artifact upload или честно укажи ограничение.

Для сравнения «до/после» используй два отдельных кадра соответствующих состояний. Снимки не заменяют поведенческие assertions: зелёный тест и просмотренный PNG отвечают на разные вопросы.

## Снимки при падениях

`HeadlessControlResolver` добавляет PNG в `UiOperationException.FailureContext.Artifacts`: `Kind == "screenshot"` — файл, путь восстанавливается через `Path.GetFullPath(artifact.RelativePath, AppContext.BaseDirectory)`. `screenshot-unavailable` — объяснение, не изображение. Для обычного упавшего TUnit assertion `UiTestBase.CollectFailureArtifactsAsync` вызывается до Dispose; сгенерированный Headless wrapper переопределяет hook, прикладывает PNG к результату и пишет путь. В собственном wrapper сохрани такой override. Ошибка захвата не должна скрыть первоначальное падение теста.

Автоматические файлы лежат в `<test-bin>/artifacts/ui-failures/avalonia-headless/<id>/`, если каталог не переопределён. Проверь реальный путь в выводе теста; `artifacts/` игнорируется Git и не публикуется CI автоматически. При отсутствии кадра сначала проверь `.UseSkia()`, `UseHeadlessDrawing = false`, `window.Show()`, размер и время кадра, UI dispatcher и срок жизни сессии.
