# AppAutomation Project Topology

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

Если solution lives below repo root, that does not change topology. Меняется только `TestHost` implementation.

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
