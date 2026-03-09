# AppAutomation Project Topology

Ниже схема, которая считается канонической для consumer solution.

## Базовый вариант

Если вам нужен только один runtime:

```text
src/
  MyApp/
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
```

или:

```text
src/
  MyApp/
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.FlaUI/
```

## Рекомендуемый полный вариант

```text
src/
  MyApp/
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

## Nested solution layout

Если solution лежит не в repo root, а например в `src/`, это поддерживаемый сценарий:

```text
repo/
  src/
    MyApp.sln
    MyApp/
  tests/
    MyApp.UiTests.Authoring/
    MyApp.UiTests.Headless/
    MyApp.UiTests.FlaUI/
    MyApp.AppAutomation.TestHost/
```

В этом случае:

- не пытайтесь в reusable packages зашивать knowledge про `src/` или конкретный путь до `.sln`;
- держите path discovery, build-on-launch и output resolution внутри `MyApp.AppAutomation.TestHost`;
- выбирайте один канонический anchor, например `solution directory` или `repo root`, и стройте все пути от него.

Подробности по nested layout и bootstrap смотрите в [advanced-integration.md](advanced-integration.md).

## Ответственность проектов

| Проект | Что внутри | Чего там быть не должно |
| --- | --- | --- |
| `MyApp.UiTests.Authoring` | `UiPage` classes, `[UiControl(...)]`, shared scenarios, generated manifest | runtime bootstrap, `.sln` discovery, build-on-launch |
| `MyApp.UiTests.Headless` | `Headless` session wiring, runtime-specific wrappers | duplicate page objects, shared scenarios через `Compile Include` |
| `MyApp.UiTests.FlaUI` | `FlaUI` session wiring, desktop runtime wrappers | duplicate page objects, shared scenarios через `Compile Include` |
| `MyApp.AppAutomation.TestHost` | repo-specific build/launch/infrastructure helpers | reusable framework API |

## Что считать обязательным

Обязательно:

- один authoring project;
- хотя бы один runtime-specific test project.

Опционально:

- второй runtime-specific project для parity between headless and real desktop;
- repo-only `TestHost`, если нужен build/launch bootstrap;
- прямой reference на `AppAutomation.Session.Contracts`, если launch contracts используются отдельно от runtime package.

## Почему нужен отдельный authoring project

Потому что именно он:

- владеет page objects;
- является точкой подключения `AppAutomation.Authoring`;
- получает generated members и manifest;
- даёт один source of truth для shared test scenarios.

Не копируйте этот код в runtime projects через `Compile Include`. Правильный path это `ProjectReference`.

## Repo-specific infrastructure vs reusable packages

В reusable `AppAutomation.*` packages не должно быть:

- поиска solution root;
- вызова `dotnet build`;
- knowledge about your repo layout;
- жёстких путей до `bin/Debug` или `bin/Release`.

Всё это responsibility consumer solution и отдельного repo-only infrastructure project.

## Что обычно входит в TestHost

Типичная ответственность `MyApp.AppAutomation.TestHost`:

- найти `repo root` или `solution directory`;
- вычислить путь до AUT и его output folder;
- подготовить isolated settings, temp dirs, seed data;
- выдать `DesktopAppLaunchOptions` с `Arguments` и `EnvironmentVariables`;
- выдать `HeadlessAppLaunchOptions` с `BeforeLaunchAsync`, `CreateMainWindow` или `CreateMainWindowAsync`.

Это infrastructure layer для вашего repo. Он не должен мигрировать в reusable framework package.
