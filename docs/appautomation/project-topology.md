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

## Canonical Layout

```text
tests/
  MyApp.UiTests.Authoring/
  MyApp.UiTests.Headless/
  MyApp.UiTests.FlaUI/
  MyApp.AppAutomation.TestHost/
```

Это layout, который создаёт template `appauto-avalonia`.

## Responsibility Split

| Проект | Владеет | Не должен владеть |
| --- | --- | --- |
| `*.UiTests.Authoring` | page objects, `[UiControl(...)]`, shared scenarios, manual composite control properties | build-on-launch, repo discovery, app bootstrap |
| `*.UiTests.Headless` | headless session hooks, headless resolver, thin runtime wrappers | duplicated scenarios, duplicated page objects |
| `*.UiTests.FlaUI` | FlaUI session wiring, thin runtime wrappers | duplicated scenarios, duplicated page objects |
| `*.AppAutomation.TestHost` | repo-specific launch/bootstrap, temp settings, temp dirs, app paths | reusable framework code |

## Обязательные правила

- Shared scenarios живут только в `Authoring`.
- Runtime projects используют `ProjectReference` на `Authoring`, а не `Compile Include`.
- `TestHost` хранит repo-specific knowledge вне reusable packages.
- `FlaUI` проект опционален только если вам действительно не нужно desktop runtime coverage.

## Composite Controls

Простые controls остаются в generated `[UiControl(...)]` path.

Composite controls:

- могут быть объявлены вручную как page properties;
- должны использовать `WithAdapters(...)` или `WithSearchPicker(...)` до создания consumer-specific resolver forks.

## Nested Solution Layout

Если solution лежит ниже repo root, это не меняет topology. Меняется только `TestHost` implementation.

Типовой layout:

```text
repo/
  src/
    MyApp.sln
    MyApp.Desktop/
  tests/
    ...
```

В этом случае именно `TestHost` отвечает за:

- поиск solution root;
- путь до AUT project/exe;
- build-before-launch;
- isolated files/settings.
