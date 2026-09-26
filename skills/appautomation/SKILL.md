---
name: appautomation
description: Add, run, and maintain AppAutomation UI tests for Avalonia desktop apps, from a project with no UI tests through Headless/FlaUI scenarios, diagnostics, and visual screenshot review. Use for AppAutomation integration or testing in a consumer project; not for generic browser automation.
---

# AppAutomation для Avalonia

Помогай довести конкретный пользовательский UI-сценарий до проходящего теста и проверяемого результата. Скилл применяется и к новому внедрению, и к проекту, где AppAutomation уже подключён. При работе вне исходного репозитория фреймворка опирайся на установленную версию пакетов и документацию [AppAutomation](https://github.com/Kibnet/AppAutomation/tree/master/docs/appautomation), а не на предполагаемые локальные пути.

## Выбери маршрут по состоянию проекта

1. Осмотри `global.json`, TFM, Avalonia app/desktop entry point, solution и каталоги тестов, `PackageReference`, feed, тестовый runner и ОС. Если AppAutomation уже подключён, сохрани принятую структуру и дополни её. Если тестов нет, прочитай [внедрение и авторинг](references/adoption-and-authoring.md) до изменений.
2. Определи **один конкретный пользовательский flow** и подготовь детерминированные данные, вход/права, настройки и стартовый экран. Для нового подключения установи совместимые `AppAutomation.Templates` **и** `AppAutomation.Tooling`, затем создай topology через `appauto-avalonia` и проверь `appautomation doctor`; точные команды — в reference. Нельзя считать готовыми тесты, которые только сгенерированы шаблоном: `TestHost` должен запускать настоящее приложение, а сценарий должен проходить.
3. Держи Page objects и общие сценарии в `*.UiTests.Authoring`; `*.UiTests.Headless` и `*.UiTests.FlaUI` — тонкие адаптеры выполнения, `*.AppAutomation.TestHost` — специфичный для проекта запуск. Используй стабильные `AutomationId`; если проверяешь `WaitUntilName*`, задавай `AutomationProperties.Name` явно. Утверждай видимый пользователю результат.
4. Сначала добейся зелёного Headless, затем запускай FlaUI при доступном интерактивном Windows desktop. Для TUnit/Microsoft.Testing.Platform используй `--treenode-filter`, когда нужен выборочный запуск. При падении изучи `UiOperationException.FailureContext`, скриншоты и другие артефакты; не выдавай scaffold, `doctor` или одну сборку за прошедший UI-сценарий.

Для составных элементов, таблиц, фильтров, дат, меню и других компонентов сначала проверь существующие контракты/примеры в [README](https://github.com/Kibnet/AppAutomation#appautomation), [advanced integration](https://github.com/Kibnet/AppAutomation/blob/master/docs/appautomation/advanced-integration.md) и [coverage gaps](https://github.com/Kibnet/AppAutomation/blob/master/docs/appautomation/component-coverage-gaps.md). Используй встроенный provider-neutral путь или адаптер до собственного resolver. Recorder может ускорить авторинг, но его generated Page/scenario partials и селекторы требуют review. Если компонент не покрыт, укажи реальный пробел.

## Визуальная проверка и демонстрация

Если пользователь просит оценить внешний вид, подтвердить UI-фичу или показать состояние, прочитай [визуальные доказательства](references/visual-evidence.md). Headless PNG фиксирует **отрисованную область Avalonia Window**, а не системную рамку и native dialogs. Дождись нужного состояния, сохрани новый PNG через тест, **открой изображение** и сравни видимые признаки с ожиданием/референсом. Предоставь пользователю доступный файл или ссылку и кратко назови, что видно и что остаётся непроверенным. При недоступном кадре сообщи причину, не заменяя его заявлением об успешном assertion.

## Результат работы

Сообщи, что именно подключено или проверено: пользовательский сценарий, какие проекты/рантаймы запускались, результат тестов, путь или вложение просмотренного снимка, а также конкретные ограничения (например, FlaUI без Windows desktop или заблокированный feed). Останавливайся, когда требуемый сценарий и доказательства проверены; дополнительные контролы и масштабирование покрытия делай по задаче пользователя.
