# AppAutomation

**English** | [Русский](#русская-версия)

`AppAutomation` is a reusable desktop UI automation framework extracted from this repository.

Packages:

- `AppAutomation.Abstractions`: automation contracts, page model primitives, waits and diagnostics.
- `AppAutomation.Authoring`: source generator/analyzers for `[UiControl]`-based page objects.
- `AppAutomation.TUnit`: `UiTestBase` and shared test helpers for `TUnit`.
- `AppAutomation.Avalonia.Headless`: in-process Avalonia Headless runtime.
- `AppAutomation.FlaUI`: Windows desktop runtime on top of FlaUI.

Recommended test-solution topology:

- `<MyApp>.UiTests.Authoring`: page objects and shared scenarios.
- `<MyApp>.UiTests.Headless`: optional headless runtime tests.
- `<MyApp>.UiTests.FlaUI`: optional Windows desktop runtime tests.

Full setup guide:

- Quickstart: https://github.com/Kibnet/AppAutomation/blob/main/docs/appautomation/quickstart.md
- Project topology: https://github.com/Kibnet/AppAutomation/blob/main/docs/appautomation/project-topology.md
- Publishing: https://github.com/Kibnet/AppAutomation/blob/main/docs/appautomation/publishing.md

---

<a id="русская-версия"></a>

## Русская версия

[English](#appautomation) | **Русский**

`AppAutomation` — это переиспользуемый desktop UI automation framework, извлечённый из этого репозитория.

Пакеты:

- `AppAutomation.Abstractions`: контракты автоматизации, примитивы page model, ожидания и диагностика.
- `AppAutomation.Authoring`: source generator/analyzers для page objects на основе `[UiControl]`.
- `AppAutomation.TUnit`: `UiTestBase` и общие test helpers для `TUnit`.
- `AppAutomation.Avalonia.Headless`: in-process Avalonia Headless runtime.
- `AppAutomation.FlaUI`: Windows desktop runtime поверх FlaUI.

Рекомендуемая топология test-solution:

- `<MyApp>.UiTests.Authoring`: page objects и shared scenarios.
- `<MyApp>.UiTests.Headless`: опциональные headless runtime tests.
- `<MyApp>.UiTests.FlaUI`: опциональные Windows desktop runtime tests.

Полное руководство по настройке:

- Quickstart: https://github.com/Kibnet/AppAutomation/blob/main/docs/appautomation/quickstart.md
- Project topology: https://github.com/Kibnet/AppAutomation/blob/main/docs/appautomation/project-topology.md
- Publishing: https://github.com/Kibnet/AppAutomation/blob/main/docs/appautomation/publishing.md
