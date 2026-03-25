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

`AppAutomation` — это переиспользуемый фреймворк автоматизации пользовательского интерфейса настольных приложений, выделенный из этого репозитория.

Пакеты:

- `AppAutomation.Abstractions`: контракты автоматизации, примитивы модели страниц, ожидания и диагностика.
- `AppAutomation.Authoring`: анализатор и генератор исходного кода для объектов страниц на основе `[UiControl]`.
- `AppAutomation.TUnit`: `UiTestBase` и общие вспомогательные средства тестирования для `TUnit`.
- `AppAutomation.Avalonia.Headless`: встроенная в процесс среда выполнения Avalonia Headless.
- `AppAutomation.FlaUI`: настольная среда выполнения Windows поверх FlaUI.

Рекомендуемая структура тестового решения:

- `<MyApp>.UiTests.Authoring`: объекты страниц и общие сценарии.
- `<MyApp>.UiTests.Headless`: необязательные тесты в режиме `Headless`.
- `<MyApp>.UiTests.FlaUI`: необязательные тесты настольного приложения под Windows.

Полное руководство по настройке:

- Краткое руководство: https://github.com/Kibnet/AppAutomation/blob/main/docs/appautomation/quickstart.md
- Структура проектов: https://github.com/Kibnet/AppAutomation/blob/main/docs/appautomation/project-topology.md
- Публикация: https://github.com/Kibnet/AppAutomation/blob/main/docs/appautomation/publishing.md
