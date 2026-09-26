# Внедрение и авторинг UI-тестов AppAutomation

Читай при первом подключении, существенном расширении тестов или сомнении, где должна жить логика. Текущие детали версий и API сверяй с [quickstart](https://github.com/Kibnet/AppAutomation/blob/master/docs/appautomation/quickstart.md), [compatibility](https://github.com/Kibnet/AppAutomation/blob/master/docs/appautomation/compatibility.md), [project topology](https://github.com/Kibnet/AppAutomation/blob/master/docs/appautomation/project-topology.md) и примерами в репозитории. Команды ниже выполняй из корня **проекта-потребителя**, подставляя его имя/пути.

## 1. Подготовь приложение к воспроизводимому тесту

- Проверь .NET SDK/TFM: библиотеки AppAutomation требуют `net8.0+`; Headless поддерживает `net8.0`/`net10.0`, FlaUI — соответствующие Windows TFM. Сверь **версию Avalonia самого приложения** с зависимостями выбранного пакета AppAutomation до генерации: пакеты `1.8.0` и шаблон рассчитаны на Avalonia `12.0.4`. Если приложение использует Avalonia 11, запланируй совместимую версию фреймворка или миграцию приложения и проверь её отдельно; смешение major-версий в одном тестовом процессе не считай поддерживаемой конфигурацией. Для более старого TFM сначала определи миграцию как предпосылку, не подменяй пакеты исходным кодом.
- Выбери один критичный flow и предусмотри тестовый аккаунт, данные и права, изолированные настройки/файлы, фиксированный стартовый экран. Отключи автоматическое обновление и побочные фоновые задачи в тестовом запуске. Это ответственность consumer AUT, а не фреймворка.
- Проверь `global.json`, feed/`NuGet.Config`, уже имеющиеся тесты и структуру solution. При nested solution сохрани канонические роли проектов; поиск repo root и app path сосредоточь в `TestHost`.

## 2. Создай или дострой каноническую структуру

Если структуры нет, выбери одну совместимую опубликованную версию AppAutomation для шаблона, CLI и пакетов. Не вставляй `dotnet new` поверх уже изменённых `tests/` без анализа конфликтов.

```powershell
dotnet new install AppAutomation.Templates
dotnet new tool-manifest
dotnet tool install AppAutomation.Tooling
dotnet new appauto-avalonia --name MyApp
dotnet tool run appautomation doctor --repo-root .
```

`dotnet new tool-manifest` выполняй только если `.config/dotnet-tools.json` ещё нет. Для закреплённой версии используй соответствующие параметры `dotnet new install`, `dotnet tool install --version` и `dotnet new appauto-avalonia --AppAutomationVersion <version>`; не оставляй разные версии шаблона и пакетов непроверенными. Стандартная структура:

```text
tests/
  MyApp.UiTests.Authoring/       # Page objects и общие сценарии
  MyApp.UiTests.Headless/        # Headless hooks и тонкий wrapper
  MyApp.UiTests.FlaUI/           # Windows desktop wrapper
  MyApp.AppAutomation.TestHost/  # Запуск конкретного AUT
```

Проекты выполнения ссылаются на `Authoring` через `ProjectReference`, а не копируют тесты. `FlaUI` можно не подключать только когда desktop runtime не нужен/недоступен; не называй такой прогон проверкой FlaUI. `doctor --strict` используй после замены placeholders: исходный шаблон закономерно не проходит его.

Добавь созданные проекты в существующий `.sln`/`.slnx` и проверь `dotnet sln list` (при вложенном приложении указывай путь к нужному solution; не создавай второй без причины). Если `dotnet sln add` предлагает лишние проектные зависимости, используй `--include-references false` и проверь явные `ProjectReference` в тестовых проектах.

## 3. Подключи настоящее приложение

В `*.AppAutomation.TestHost` замени `AvaloniaAppType`, путь desktop-проекта/exe и фабрику `Window`. Используй `AvaloniaDesktopLaunchHost` и `AvaloniaHeadlessLaunchHost` вместо ручного поиска процесса и окна. Для вложенного solution, auth-состояния, повторных запусков и изолированных настроек см. [advanced integration](https://github.com/Kibnet/AppAutomation/blob/master/docs/appautomation/advanced-integration.md): `AvaloniaDesktopAppDescriptor`, `AutomationLaunchScenario<TPayload>`, `BeforeLaunchAsync`, `TemporaryDirectory` и `AutomationPreflight` применяй по реальной потребности. Секреты не помещай в код или test output.

Headless hooks из шаблона должны запускать `RenderedHeadlessAppBuilder`, вызывать `HeadlessRuntime.SetSession(...)` до тестов и очищать сессию после них. `AppBuilder` должен подключать нужные ресурсы приложения. `Window` создавай через launch callback/`HeadlessRuntime.Dispatch`, показывай до тестовых действий и закрывай при dispose. При ошибке `Headless session is not initialized` сначала проверь hooks, настоящий `AvaloniaAppType` и правильный тестовый проект.

## 4. Опиши пользовательский сценарий один раз

Поставь `AutomationProperties.AutomationId` на корень окна, навигацию, критичные поля/кнопки, результат и дочерние якоря составных контролов. Не строй основные селекторы по тексту, индексу или координатам. Для `WaitUntilName*` задай `AutomationProperties.Name` явно; динамическим элементам дай стабильный domain key. См. [selector contract](https://github.com/Kibnet/AppAutomation/blob/master/docs/appautomation/selector-contract.md).

В `Authoring` создай `UiPage` с `[UiControl(...)]` и общий `UiTestBase<TSession,TPage>` сценарий. В нём действия выражают пользовательский flow и проверяют наблюдаемый UI-result. Runtime wrappers создают сессию и resolver, наследуют сценарий через `[InheritsTests]`; бизнес-шаги туда не дублируй. Сначала попробуй простые контролы. Для composite выбирай built-in `WithAdapters(...)`, `WithSearchPicker(...)` и другие соответствующие контракты по документации; не делай универсальный fork resolver из-за одного виджета.

Avalonia Recorder можно подключить к AUT как необязательный ускоритель: он пишет partial Page/scenario в `Authoring`, а не готовые runtime-specific тесты. Проверь уникальность сценария, валидность selectors и assertions, затем запусти оба применимых runtime. Для специальных grid/popup/menu/date сценариев читай [README](https://github.com/Kibnet/AppAutomation#appautomation), [advanced integration](https://github.com/Kibnet/AppAutomation/blob/master/docs/appautomation/advanced-integration.md) и [coverage gaps](https://github.com/Kibnet/AppAutomation/blob/master/docs/appautomation/component-coverage-gaps.md); отсутствующую поддержку не подменяй предположением.

## 5. Запусти и разберись с результатом

```powershell
dotnet tool run appautomation doctor --repo-root .
dotnet test --project tests/MyApp.UiTests.Headless/MyApp.UiTests.Headless.csproj -c Debug
dotnet test --project tests/MyApp.UiTests.FlaUI/MyApp.UiTests.FlaUI.csproj -c Debug
```

Вторая команда должна пройти до включения третьей. FlaUI требует Windows с интерактивным desktop и может переводить фокус; на headless CI не выдавай пропуск за успех. Для TUnit/MTP при targeted прогоне используй `--treenode-filter`, а не VSTest `--filter`; синтаксис уточняй через `--list-tests`. При падении изучи `UiOperationException.FailureContext` и артефакты, исправляй первопричину и повторяй затронутую проверку. Финальный статус отдельно показывает `doctor`, build, Headless, FlaUI и визуальные evidence.
