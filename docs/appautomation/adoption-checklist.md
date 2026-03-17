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

Пройдите этот checklist до первого реального сценария.

## Prerequisites

- SDK закреплён через `global.json`.
- `NuGet.Config` настроен для `nuget.org` или корпоративного mirror.
- `appautomation doctor --repo-root .` отрабатывает без ошибок.
- выбрана одна критичная smoke-flow для первой итерации.

## Topology

- созданы `Authoring`, `Headless`, `FlaUI`, `TestHost`.
- runtime projects ссылаются на `Authoring`, а не копируют test code.
- `TestHost` хранит только repo-specific bootstrap.

## AUT Readiness

- есть deterministic login / auth story;
- есть deterministic test data / permissions story;
- есть isolated settings path;
- отключены auto-update и другие background side effects;
- определён стабильный startup screen.

## Selectors

- есть `AutomationId` для root window;
- есть `AutomationId` для main navigation / tabs;
- есть `AutomationId` для критичных input/button/result controls;
- для composite widget размечены child anchors, а не только outer container.

## Execution

- сначала проходит `Headless`;
- только потом добавлен `FlaUI`;
- shared scenarios живут только в `Authoring`.
