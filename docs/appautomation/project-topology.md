# AppAutomation Project Topology

**English** | [Русский](#русская-версия)

## Canonical Layout

```text
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

This is the layout that the `appauto-avalonia` template creates.

## Responsibility Split

| Project | Owns | Must not own |
| --- | --- | --- |
| `*.UiTests.Authoring` | page objects, `[UiControl(...)]`, shared scenarios, manual composite control properties | build-on-launch, repo discovery, app bootstrap |
| `*.UiTests.Headless` | headless session hooks, headless resolver, thin runtime wrappers | duplicated scenarios, duplicated page objects |
| `*.UiTests.FlaUI` | FlaUI session wiring, thin runtime wrappers | duplicated scenarios, duplicated page objects |
| `*.AppAutomation.TestHost` | repo-specific launch/bootstrap, temp settings, temp dirs, app paths | reusable framework code |

## Mandatory Rules

- Shared scenarios live only in `Authoring`.
- Runtime projects use `ProjectReference` to `Authoring`, not `Compile Include`.
- `TestHost` keeps repo-specific knowledge out of reusable packages.
- `FlaUI` project is optional only if you truly do not need desktop runtime coverage.

## Composite Controls

Simple controls stay in generated `[UiControl(...)]` path.

Composite controls:

- can be declared manually as page properties;
- should use `WithAdapters(...)` or `WithSearchPicker(...)` before you create consumer-specific resolver forks.

## Nested Solution Layout

If solution lives below repo root, that does not change topology. Only the `TestHost` implementation changes.

Typical layout:

```text
repo/
  src/
    MyApp.sln
    MyApp.Desktop/
  tests/
    ...
```

In this case, `TestHost` is responsible for:

- finding solution root;
- path to AUT project/exe;
- build-before-launch;
- isolated files/settings.

---

<a id="русская-версия"></a>

## Русская версия

[English](#appautomation-project-topology) | **Русский**

## Стандартная структура

```text
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

Это структура, которую создаёт шаблон `appauto-avalonia`.

## Разделение ответственности

| Проект | Владеет | Не должен владеть |
| --- | --- | --- |
| `*.UiTests.Authoring` | объекты страниц, `[UiControl(...)]`, общие сценарии, вручную описанные свойства составных элементов управления | сборка перед запуском, поиск корня репозитория, инициализация приложения |
| `*.UiTests.Headless` | обработчики сеанса `Headless`, резолвер `Headless`, тонкие обёртки среды выполнения | дублирование сценариев, дублирование объектов страниц |
| `*.UiTests.FlaUI` | подключение сеанса `FlaUI`, тонкие обёртки среды выполнения | дублирование сценариев, дублирование объектов страниц |
| `*.AppAutomation.TestHost` | логика запуска, специфичная для репозитория, временные настройки, временные каталоги, пути к приложению | переиспользуемый код фреймворка |

## Обязательные правила

- Общие сценарии живут только в `Authoring`.
- Проекты выполнения используют `ProjectReference` на `Authoring`, а не `Compile Include`.
- `TestHost` хранит знания, специфичные для репозитория, вне переиспользуемых пакетов.
- Проект `FlaUI` опционален только если вам действительно не нужно покрытие настольной среды выполнения.

## Составные элементы управления

Простые элементы управления остаются в сгенерированном пути `[UiControl(...)]`.

Составные элементы управления:

- могут быть объявлены вручную как свойства страницы;
- должны использовать `WithAdapters(...)` или `WithSearchPicker(...)` до создания собственных форков резолвера.

## Вложенная структура решения

Если решение лежит ниже корня репозитория, это не меняет структуру. Меняется только реализация `TestHost`.

Типовая структура:

```text
repo/
  src/
    MyApp.sln
    MyApp.Desktop/
  tests/
    ...
```

В этом случае именно `TestHost` отвечает за:

- поиск корня решения;
- путь к проекту или исполняемому файлу AUT;
- сборку перед запуском;
- изолированные файлы и настройки.
