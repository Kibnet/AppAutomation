# Скриншоты Avalonia Headless

AppAutomation сохраняет кадр окна в PNG без desktop-сессии. Подходит для проверки состояния в середине теста и для диагностики падения. Это изображение области Avalonia Window; системная рамка окна и другие native-окна в него не входят.

## Настроить рендеринг

Headless по умолчанию использует рисование без пикселей. До `HeadlessUnitTestSession.StartNew` подключите Skia в **тестовом** `AppBuilder`. В шаблоне `appauto-avalonia` это уже сделано в `tests/<App>.UiTests.Headless/Infrastructure/RenderedHeadlessAppBuilder.cs` и `HeadlessSessionHooks.cs`; в TestHost нужно задать настоящий `AvaloniaAppType`.

```csharp
public sealed class RenderedHeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MyAvaloniaApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

var testSession = HeadlessUnitTestSession.StartNew(
    typeof(RenderedHeadlessAppBuilder), AvaloniaTestIsolationLevel.PerAssembly);
HeadlessRuntime.SetSession(testSession);
```

Если приложение настраивает шрифты или другие ресурсы в своём `AppBuilder`, перенесите нужные вызовы и в тестовый builder. Создавайте `Window` и другие Avalonia controls только через `HeadlessRuntime.Dispatch` или внутри `CreateMainWindow`: раннее создание на потоке теста может закрепить UI dispatcher за неверным потоком и сорвать инициализацию Skia. Окно нужно показать через dispatcher до захвата и закрыть при очистке сессии. Сгенерированный wrapper уже делает Show/Close.

## Снимок в любой точке теста

```csharp
Page.SelectTabItem(static page => page.ArmDesktopTabItem);
var path = Session.Inner.CaptureScreenshot(Path.Combine(
    AppContext.BaseDirectory, "artifacts", "headless-screenshots",
    Guid.NewGuid().ToString("N"), "arm-desktop.png"));
Console.WriteLine($"Headless screenshot: {path}");
```

`CaptureScreenshot` возвращает абсолютный путь только после записи файла. Укажите новое имя: метод не перезаписывает существующий файл. Каталог создаётся автоматически. Если окно скрыто/закрыто, сессия освобождена, кадр ещё не появился или Skia не настроена, метод выдаёт ошибку с причиной. Изображение фиксирует текущий кадр; нужного бизнес-состояния дождитесь обычным `WaitUntil` до вызова.

## Снимок при ошибке

`HeadlessControlResolver` добавляет PNG к `UiOperationException.FailureContext.Artifacts` при ошибке операции страницы. `Kind == "screenshot"` означает сохранённый файл: восстановите абсолютный путь через `Path.GetFullPath(artifact.RelativePath, AppContext.BaseDirectory)`. `screenshot-unavailable` содержит причину, а не PNG. Дерево элементов и состояние контрола продолжают собираться.

При обычном упавшем assertion базовый `UiTestBase` вызывает `CollectFailureArtifactsAsync` до Dispose. Сгенерированный `MainWindowHeadlessTests` и примерный `MainWindowHeadlessRuntimeTests` уже переопределяют его: они снимают PNG, пишут абсолютный путь в test output и прикрепляют файл к результату TUnit. Для своего headless класса скопируйте этот override. Ошибка захвата не заменяет исходную ошибку теста. Тесты вне `UiTestBase`, аварийный выход процесса и падение после Dispose этим hook не покрываются.

Автоматические снимки пишутся в `<test-bin>/artifacts/ui-failures/avalonia-headless/<id>/`. По умолчанию resolver использует тот же каталог, а `HeadlessScreenshotOptions.ArtifactDirectory` позволяет выбрать другой каталог на том же диске, что `AppContext.BaseDirectory`. Для явного `CaptureScreenshot` путь может быть на любом диске. Снимки каждого падения получают уникальный ID. Каталоги `artifacts/` игнорируются Git; CI должен отдельно опубликовать их, если нужны удалённые вложения.

## Проверить как агент

1. Запустите нужный headless-тест, например `dotnet run --project tests/<App>.UiTests.Headless -- --treenode-filter "/*/*/MainWindowHeadlessTests/CaptureScreenshotExample" --maximum-parallel-tests 1`.
2. Возьмите абсолютный путь из вывода теста или failure artifact, откройте PNG просмотрщиком изображений и проверьте нужное состояние. Один успешный assertion без просмотра кадра не доказывает внешний вид.
3. Приложите ссылку на сохранённый файл и назовите, какое состояние на нём видно. Если результат нужен из CI, отдельно проверьте публикацию файлов из `artifacts/`.

В репозитории фреймворка есть runnable проверки: `HeadlessScreenshotTests` сохраняет два разных состояния, `HeadlessFailureDiagnosticsTests` проверяет screenshot при ошибке операции, а `CaptureScreenshot_AfterNavigation_ShowsCurrentTab` даёт кадр примерного приложения после переключения вкладки. При отсутствии изображения сначала проверьте `UseHeadlessDrawing = false`, `.UseSkia()`, `window.Show()` и текущий путь к артефакту.
