# AppAutomation Adoption Checklist

**English** | [Русский](#русская-версия)

Complete this checklist before your first real scenario.

## Prerequisites

- SDK is pinned via `global.json`.
- `NuGet.Config` is configured for `nuget.org` or corporate mirror.
- `appautomation doctor --repo-root .` runs without errors.
- one critical smoke flow is selected for the first iteration.

## Topology

- `Authoring`, `Headless`, `FlaUI`, `TestHost` are created.
- runtime projects reference `Authoring`, not copy test code.
- `TestHost` stores only repo-specific bootstrap.

## AUT Readiness

- there is a deterministic login / auth story;
- there is a deterministic test data / permissions story;
- there is an isolated settings path;
- auto-update and other background side effects are disabled;
- a stable startup screen is defined.

## Selectors

- there is an `AutomationId` for root window;
- there is an `AutomationId` for main navigation / tabs;
- there is an `AutomationId` for critical input/button/result controls;
- for composite widgets, child anchors are marked, not just the outer container.

## Execution

- `Headless` passes first;
- only then `FlaUI` is added;
- shared scenarios live only in `Authoring`.

---

<a id="русская-версия"></a>

## Русская версия

[English](#appautomation-adoption-checklist) | **Русский**

Пройдите этот контрольный список до первого реального сценария.

## Предварительные условия

- SDK закреплён через `global.json`.
- `NuGet.Config` настроен для `nuget.org` или корпоративного зеркала.
- `appautomation doctor --repo-root .` отрабатывает без ошибок.
- для первой итерации выбран один критичный сценарий быстрой проверки.

## Структура проектов

- созданы `Authoring`, `Headless`, `FlaUI`, `TestHost`.
- проекты выполнения ссылаются на `Authoring`, а не копируют код тестов.
- `TestHost` хранит только логику запуска, специфичную для репозитория.

## Готовность AUT

- есть детерминированный сценарий входа и аутентификации;
- есть детерминированный сценарий с тестовыми данными и правами доступа;
- есть изолированный путь для настроек;
- отключены автообновление и другие фоновые побочные эффекты;
- определён стабильный стартовый экран.

## Селекторы

- есть `AutomationId` для корневого окна;
- есть `AutomationId` для основной навигации и вкладок;
- есть `AutomationId` для критичных полей ввода, кнопок и элементов с результатами;
- для составных виджетов размечены дочерние опорные элементы, а не только внешний контейнер.

## Выполнение

- сначала проходят тесты `Headless`;
- только потом добавляется `FlaUI`;
- общие сценарии живут только в `Authoring`.
