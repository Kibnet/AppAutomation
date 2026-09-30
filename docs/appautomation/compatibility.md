# AppAutomation Compatibility

**English** | [Русский](#русская-версия)

## Package Matrix

| Package | Target frameworks | Notes |
| --- | --- | --- |
| `AppAutomation.Abstractions` | `net8.0+` | base UI contracts, page model, extensions |
| `AppAutomation.Authoring` | `netstandard2.0` | analyzer/source generator |
| `AppAutomation.Session.Contracts` | `net8.0+` | launch contracts |
| `AppAutomation.TUnit` | `net8.0+` | shared UI test base |
| `AppAutomation.TestHost.Avalonia` | `net8.0`, `net10.0` | reusable Avalonia test-host helpers |
| `AppAutomation.Avalonia.Headless` | `net8.0`, `net10.0` | Avalonia headless runtime |
| `AppAutomation.Recorder.Avalonia` | `net8.0`, `net10.0` | interactive Avalonia recorder |
| `AppAutomation.FlaUI` | `net8.0-windows7.0`, `net10.0-windows7.0` | Windows desktop runtime |
| `AppAutomation.Tooling` | `.NET tool`, `net8.0` | command `appautomation` |
| `AppAutomation.Templates` | `dotnet new` template package | canonical consumer topology |

## Consumer Runtime Expectations

| Area | Requirement |
| --- | --- |
| Headless | Avalonia app with a deterministic `Window` creation path |
| FlaUI | Windows only, desktop executable available |
| Clipboard text | `CopyTextToClipboardAsync(...)` and `PasteTextFromClipboardAsync(...)` are supported by the built-in Headless and FlaUI runtimes; custom providers expose clipboard writes through `IUiClipboardRuntime`, real target-side paste through `IClipboardPasteTarget`, and advertise the workflow through `SupportsClipboardText` |
| Test runner | `TUnit` + `Microsoft.Testing.Platform` |
| SDK | recommended pinned SDK `8+`; repo examples currently validate under current available SDK and pinned `global.json` |
| Package installation | `NuGet-first` path is primary; source dependency is fallback only |

## Recommended Consumer TFMs

| Project type | Recommended TFM |
| --- | --- |
| `*.UiTests.Authoring` | `net8.0` or `net10.0` |
| `*.UiTests.Headless` | `net8.0` or `net10.0` |
| `*.UiTests.FlaUI` | `net8.0-windows7.0` or `net10.0-windows7.0` |
| `*.AppAutomation.TestHost` | `net8.0` or `net10.0` |

## Notes

- `FlaUI` runtime always requires Windows.
- `AppAutomation.Authoring` stays `netstandard2.0`, because it is consumed as analyzer/source-generator package.
- Recorder-generated checkpoint assertions require the Authoring test project to reference TUnit assertions, as the repository templates already do; Headless and FlaUI providers need no assertion-specific configuration.
- Clipboard replay writes plain text to the runtime clipboard, obtains verified keyboard focus, performs the provider's real paste interaction, and verifies the resulting text-box value. FlaUI owns the clipboard through an internal message-only window of the automation process rather than through the tested application.
- If your repo is not yet on `net8.0+`, treat migration as a prerequisite before framework adoption.

---

<a id="русская-версия"></a>

## Русская версия

[English](#appautomation-compatibility) | **Русский**

## Матрица пакетов

| Пакет | Целевые фреймворки | Примечания |
| --- | --- | --- |
| `AppAutomation.Abstractions` | `net8.0+` | базовые UI-контракты, модель страниц, расширения |
| `AppAutomation.Authoring` | `netstandard2.0` | анализатор и генератор исходного кода |
| `AppAutomation.Session.Contracts` | `net8.0+` | контракты запуска |
| `AppAutomation.TUnit` | `net8.0+` | общий базовый класс для UI тестов |
| `AppAutomation.TestHost.Avalonia` | `net8.0`, `net10.0` | переиспользуемые вспомогательные классы `TestHost` для Avalonia |
| `AppAutomation.Avalonia.Headless` | `net8.0`, `net10.0` | среда выполнения Avalonia Headless |
| `AppAutomation.Recorder.Avalonia` | `net8.0`, `net10.0` | интерактивный recorder для Avalonia |
| `AppAutomation.FlaUI` | `net8.0-windows7.0`, `net10.0-windows7.0` | настольная среда выполнения Windows |
| `AppAutomation.Tooling` | `.NET tool`, `net8.0` | команда `appautomation` |
| `AppAutomation.Templates` | пакет шаблонов `dotnet new` | стандартная структура репозитория-потребителя |

## Требования к среде выполнения у потребителя

| Область | Требование |
| --- | --- |
| Headless | Avalonia-приложение с детерминированным путём создания `Window` |
| FlaUI | только Windows, доступен исполняемый файл настольного приложения |
| Текстовый буфер обмена | `CopyTextToClipboardAsync(...)` и `PasteTextFromClipboardAsync(...)` поддерживаются встроенными Headless- и FlaUI-runtime; пользовательский provider предоставляет запись через `IUiClipboardRuntime`, реальную вставку в целевой контрол через `IClipboardPasteTarget` и объявляет workflow через `SupportsClipboardText` |
| Запуск тестов | `TUnit` + `Microsoft.Testing.Platform` |
| SDK | рекомендуется закреплённый SDK `8+`; примеры в репозитории проверяются на текущем доступном SDK и закреплённом `global.json` |
| Установка пакетов | основным остаётся путь через NuGet; зависимость через исходный код — только запасной вариант |

## Рекомендуемые TFM для проекта-потребителя

| Тип проекта | Рекомендуемый TFM |
| --- | --- |
| `*.UiTests.Authoring` | `net8.0` или `net10.0` |
| `*.UiTests.Headless` | `net8.0` или `net10.0` |
| `*.UiTests.FlaUI` | `net8.0-windows7.0` или `net10.0-windows7.0` |
| `*.AppAutomation.TestHost` | `net8.0` или `net10.0` |

## Примечания

- `FlaUI` всегда требует Windows.
- `AppAutomation.Authoring` остаётся на `netstandard2.0`, так как потребляется как пакет с анализатором и генератором исходного кода.
- Для checkpoint assertions, созданных Recorder, Authoring-проект должен ссылаться на TUnit assertions, как уже делают шаблоны репозитория; отдельная настройка Headless или FlaUI не нужна.
- При воспроизведении clipboard-действия runtime записывает обычный текст в свой буфер обмена, подтверждает клавиатурный фокус, выполняет реальную вставку provider-а и проверяет итоговое значение `TextBox`. Во FlaUI владельцем clipboard служит внутреннее message-only окно процесса автоматизации, а не окно тестируемого приложения.
- Если ваш репозиторий ещё не на `net8.0+`, рассматривайте миграцию как обязательную предпосылку перед подключением фреймворка.
