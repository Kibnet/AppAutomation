# AppAutomation Adoption Checklist

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
