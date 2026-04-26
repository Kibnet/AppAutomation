# Release Trigger And Asset Attach Hardening

## 0. Метаданные
- Тип (профиль): delivery-task; CI/CD workflow hardening
- Владелец: AppAutomation
- Масштаб: medium
- Целевой релиз / ветка: `master`, следующий patch release после `1.5.5`
- Ограничения:
  - До подтверждения спеки менять только этот файл.
  - Не менять package payload, публичный API и versioning contract пакетов.
  - Не переписывать уже опубликованный release/tag `1.5.5`.
  - Не вводить схему, при которой один и тот же релизный тег запускает два publish-run с публикацией в feed.
  - Перед завершением EXEC пройти targeted verification и не делать side-effectful publish в feed вне реального релиза.
- Связанные ссылки:
  - Release `1.5.5`: `https://github.com/Kibnet/AppAutomation/releases/tag/1.5.5`
  - Validation run `1.5.5`: `https://github.com/Kibnet/AppAutomation/actions/runs/24941392826`
  - Manual publish run `1.5.5`: `https://github.com/Kibnet/AppAutomation/actions/runs/24941550230`
  - Срабатывавший auto-release run `1.5.4`: `https://github.com/Kibnet/AppAutomation/actions/runs/24894552071`
  - Документация GitHub Actions events: `https://docs.github.com/actions/using-workflows/events-that-trigger-workflows`

## 1. Overview / Цель
Сделать выпуск AppAutomation устойчивым к пропуску `release`-события и устранить расхождение между путями публикации: сейчас auto-release path и manual `workflow_dispatch publish=true` ведут себя по-разному, из-за чего релиз может опубликовать пакеты без assets на странице GitHub release. Канонический момент авторизации релиза должен остаться прежним: `release.published`. Цель: сохранить этот контракт, а recovery path через `workflow_dispatch publish=true` сделать идемпотентным и способным завершать уже созданный релиз, включая прикрепление `.nupkg/.snupkg`.

## 2. Текущее состояние (AS-IS)
- `.github/workflows/publish-packages.yml` запускается по:
  - `release.types = [published]`
  - `workflow_dispatch`
- `Resolve version` в workflow берёт версию только из:
  - `github.event.release.tag_name`
  - `github.event.inputs.version`
- Publish steps (`NuGet`, `GitHub Packages`) уже умеют работать и на `release`, и на `workflow_dispatch publish=true`.
- Attach step работает только на `github.event_name == 'release'`.
- Для release `1.5.5`:
  - release опубликован пользователем `Kibnet` 2026-04-25 21:51:46 UTC;
  - run с `event=release` не появился вообще;
  - validation `workflow_dispatch publish=false` и manual publish `workflow_dispatch publish=true` прошли успешно;
  - assets пришлось прикреплять вручную post factum.
- Для release `1.5.3` и `1.5.4` auto-release path сработал корректно:
  - есть `event=release` run;
  - assets прикреплены `github-actions[bot]`.
- Локальные git tags в репозитории состоят только из release-версий (`1.0.0` ... `1.5.5`); non-release tags сейчас не используются.
- `docs/appautomation/publishing.md` описывает pack/smoke/publish, но не фиксирует чётко supported release entry path и recovery path на случай пропуска release-event.

## 3. Проблема
Корневая проблема: release delivery зависит от нестабильного с точки зрения репозитория entry point. Даже если `workflow_dispatch publish=true` успешно публикует пакеты, attach/create release-логика жёстко привязана к `github.event_name == 'release'`, поэтому любой пропуск auto-release run приводит к неполному релизу.

Важно: по данным репозитория нельзя строго доказать server-side причину, почему `release`-run не стартовал для `1.5.5`; workflow синтаксически корректен, а `1.5.3` и `1.5.4` показывают, что тот же trigger уже работал. Значит исправлять нужно не предположение о внутренней причине GitHub, а архитектурную хрупкость самого release pipeline.

## 4. Цели дизайна
- Идемпотентность: любой поддерживаемый publish path должен уметь завершить release полностью, включая assets.
- Один автоматический publish на один релиз: не допускать duplicate package push.
- Прозрачность: release entry path должен быть понятен из workflow и документации.
- Восстановимость: manual rerun должен не только публиковать пакеты, но и закрывать gap на release page.
- Обратная совместимость: version contract (`<version>`, `v<version>`, `appautomation-v<version>`) сохраняется.

## 5. Non-Goals (чего НЕ делаем)
- Не расследуем и не “чинить” внутреннее server-side поведение GitHub beyond repo control.
- Не меняем сами NuGet package contents и не трогаем runtime/library код.
- Не строим новый multi-workflow release orchestration layer.
- Не выполняем тестовый publish в реальный feed ради проверки до следующего настоящего patch release.
- Не вводим поддержку произвольных non-release tags, потому что в этом репозитории их сейчас нет.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `.github/workflows/publish-packages.yml`
  - primary auto-trigger по `release.published`;
  - manual repair/replay path через `workflow_dispatch`;
  - asset attach и release completion на любом publish path;
  - preservation contract: recovery path не переписывает вручную оформленный release body/title без явной необходимости.
- `eng/resolve-package-version.ps1`
  - остаётся единственной точкой нормализации tag/version contract.
- `tests/AppAutomation.Build.Tests/*`
  - статические contract-tests для release workflow и publish path invariants.
- `docs/appautomation/publishing.md`
  - пользовательская документация по supported release path и recovery path.

### 6.2 Детальный дизайн
#### 6.2.1 Workflow trigger model
- Оставить `on.release.types = [published]` как единственный канонический auto-trigger publish workflow.
- Оставить `workflow_dispatch`:
  - `publish=false` как validation-only path;
  - `publish=true` как recovery/replay path.
- Не добавлять `push tags` как publish trigger.

Причина выбора:
- пользователь явно зафиксировал, что канонический момент авторизации релиза менять нельзя;
- сохранение `release` и добавление `push` как второго auto-trigger дало бы риск duplicate publish;
- задача в scope не про смену release ritual, а про то, чтобы manual recovery path мог корректно завершить уже авторизованный релиз.

#### 6.2.2 Version resolution
- Расширить `Resolve version` step:
  - продолжить поддерживать explicit `workflow_dispatch` input.
- Step должен отдавать:
  - `full` version;
  - normalized `release_tag`, который будет использоваться для create/update release.
- Для `workflow_dispatch publish=true` release completion должна работать только по уже существующему release/tag этой версии.
  - Recovery path не должен сам “изобретать” provenance версии на произвольном checkout ref.
  - Если release/tag для версии не существует, workflow должен падать с явной ошибкой, а не создавать новый release на неканоническом ref.

#### 6.2.3 Release asset attach / release ensure
- Перестроить attach phase так, чтобы она выполнялась на любом publish path:
  - `release`
  - `workflow_dispatch publish=true`
- Release step должен работать идемпотентно:
  - если release уже существует для tag -> обновить/прикрепить assets;
  - если release отсутствует на canonical `release` path -> это аномалия и job должен падать с явной диагностикой;
  - если release отсутствует на recovery path -> job тоже должен падать с явной диагностикой, а не создавать новый release.
- Для create/update release semantics использовать тот же workflow, а не ручной пост-обход, но recovery path должен быть strictly assets-oriented:
  - не переписывать `title`;
  - не переписывать `body`;
  - не менять `draft/prerelease` state;
  - обновлять только binaries/assets и, при необходимости, target release selection по tag.

#### 6.2.4 Manual recovery semantics
- Если auto-trigger не случился или был отменён, manual `workflow_dispatch publish=true` должен:
  - собрать/проверить/опубликовать пакеты;
  - гарантированно завершить release page assets;
  - работать только для уже существующего release/tag соответствующей версии;
  - падать с понятной ошибкой, если release/tag отсутствует.

#### 6.2.5 Documentation
- Явно зафиксировать в `docs/appautomation/publishing.md`:
  - primary path: publish по version tag;
  - recovery path: `workflow_dispatch publish=true` для уже известной версии;
  - почему manual publish теперь полнофункционален, а не “только push в feed”.

## 7. Бизнес-правила / Алгоритмы
- Release invariant:
  - один опубликованный release -> не более одного автоматического publish run.
- Recovery invariant:
  - `workflow_dispatch publish=true` не может завершиться success, оставив release без assets, если workflow смог упаковать артефакты.
- Provenance invariant:
  - manual recovery path не создаёт новый release и не публикует “в никуда”; он завершает только уже существующий canonical release/tag.
- Version invariant:
  - release/tag naming contract остаётся `<version>` / `v<version>` / `appautomation-v<version>`.
- Metadata preservation invariant:
  - recovery path не переписывает release notes/title/state.

## 8. Точки интеграции и триггеры
- `release.published` -> запускает canonical auto publish workflow.
- `workflow_dispatch` `publish=false` -> validation path без feed/release side effects.
- `workflow_dispatch` `publish=true` -> recovery/replay path с full release completion.
- `softprops/action-gh-release` или эквивалентный release update step -> точка создания/обновления release page.

## 9. Изменения модели данных / состояния
- Новых persisted данных нет.
- Меняется operational contract CI/CD:
  - было: publish зависит от `release` event, recovery path публикует пакеты, но не обязан завершать release assets;
  - станет: publish по-прежнему зависит от `release` event, но manual replay умеет достраивать уже существующий release без переписывания его metadata.

## 10. Миграция / Rollout / Rollback
- Rollout:
  1. внести изменения в workflow, tests и docs;
  2. прогнать targeted verification локально;
  3. влить в `master`;
  4. проверить на следующем реальном patch release.
- Rollback:
  - revert commit, возвращающий `publish-packages.yml` к старому trigger model;
  - если новый release уже выпущен, rollback делается только следующим patch release, без удаления опубликованных пакетов.
- Совместимость:
  - существующие релизные теги и release pages не переписываются;
  - `workflow_dispatch` остаётся доступным для ручного запуска.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `publish-packages.yml` сохраняет `release.published` как canonical auto-trigger.
  - Manual `workflow_dispatch publish=true` больше не ограничен `feed-only` поведением и умеет завершать release assets.
  - В workflow нет условия, при котором attach step исполняется только на `github.event_name == 'release'`.
  - Recovery path не создаёт новый release/tag и падает с явной ошибкой, если canonical release отсутствует.
  - Recovery path не переписывает `title/body/draft/prerelease` существующего release.
  - Build tests фиксируют новые release invariants.
  - `docs/appautomation/publishing.md` описывает primary trigger и recovery path.
- Какие тесты добавить/изменить:
  - новый/расширенный build test на contract `publish-packages.yml`:
    - workflow сохраняет `release.published`;
    - attach step больше не ограничен только `release`;
    - publish condition поддерживает `release` и `workflow_dispatch publish=true`.
  - при необходимости расширить `VersioningScriptsTests` для ref-based version resolution contract.
- Characterization checks:
  - existing `PublishNugetScriptTests` и `VersioningScriptsTests` должны остаться зелёными.
- Команды для проверки:
  - `dotnet test --project .\tests\AppAutomation.Build.Tests\AppAutomation.Build.Tests.csproj -c Release`
  - `dotnet build .\AppAutomation.sln -c Release`
  - `dotnet test --solution .\AppAutomation.sln -c Release --no-build`
  - dry-run contract check через `gh workflow run publish-packages.yml -f version=<existing-version> -f publish=false`
- Ограничение проверки:
  - полное E2E `publish=true` с реальным feed/release side effect проверяется только на следующем настоящем patch release; до него можно подтвердить только локальные и validation-only инварианты.

## 12. Риски и edge cases
- Риск: create/update release step может переписать release body сильнее, чем нужно.
  - Смягчение: recovery path должен быть assets-only и иметь отдельный regression test/contract check на metadata preservation.
- Риск: смена primary trigger меняет привычный release ritual.
  - Смягчение: этот риск снимается, потому что canonical trigger сохраняется неизменным.
- Риск: manual recovery может быть запущен до создания release/tag и выпустить пакеты с неправильного ref.
  - Смягчение: recovery path должен валидировать существование release/tag и падать, если canonical release ещё не создан.

## 13. План выполнения
1. Обновить spec после подтверждения пользователя.
2. Изменить `.github/workflows/publish-packages.yml`:
   - сохранить canonical trigger `release.published`;
   - расширить version resolution и release lookup для recovery path;
   - переделать release attach/create на publish paths.
3. Добавить/обновить build tests на workflow contract.
4. Обновить `docs/appautomation/publishing.md`.
5. Запустить targeted build tests.
6. Запустить solution build/test.
7. Выполнить post-EXEC review.
8. Подготовить commit/push.
9. На следующем реальном patch release проверить end-to-end publish behavior.

## 14. Открытые вопросы
Нет блокирующих вопросов. Единственная осознанная граница: точную server-side причину несрабатывания `release` event для `1.5.5` репозиторий доказать не может, поэтому решение строится как архитектурное hardening, а не как “фиксация” непроверяемой гипотезы.

## 15. Соответствие профилю
- Профиль: delivery-task
- Выполненные требования профиля:
  - scope ограничен release automation и docs;
  - package/runtime code не меняется;
  - есть targeted verification plan;
  - зафиксированы rollback и side-effect boundaries.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `.github/workflows/publish-packages.yml` | Новый trigger model, version resolution и release ensure/attach semantics | Устранить зависимость от `release` event и закрыть asset gap |
| `tests/AppAutomation.Build.Tests/*` | Contract tests для workflow | Ловить регрессии release automation до реального релиза |
| `docs/appautomation/publishing.md` | Обновление release/recovery инструкций | Сделать supported path явным |
| `specs/2026-04-26-release-trigger-and-asset-attach-hardening.md` | SPEC + журнал | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Auto publish trigger | `release.published` | `release.published` |
| Manual publish path | Публикует пакеты, но не обязан завершать release assets | Full recovery path, закрывающий и feed, и release assets для уже существующего release |
| Release attach | Только `github.event_name == 'release'` | Идемпотентно на `release` и `workflow_dispatch publish=true` |
| Release metadata | Может быть случайно затронута при repair implementation | Recovery path обязан быть assets-only |
| Release resilience | Пропуск auto-run даёт неполный релиз | Manual replay умеет достроить релиз без ручного asset upload |
| Документация | Release path не полностью формализован | Primary path и recovery path описаны явно |

## 18. Альтернативы и компромиссы
- Вариант: оставить `release` trigger и просто добавить attach на `workflow_dispatch publish=true`.
- Плюсы:
  - минимальный diff;
  - не меняется привычный auto-trigger.
- Минусы:
  - не устраняет зависимость auto path от `release` event;
  - требует аккуратно определить границы recovery path: только existing release, без metadata rewrite.
- Почему выбранное решение лучше:
  - это и есть предпочтительное решение после review: канонический trigger сохраняется, а recovery path доводится до полноценного завершения релиза.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, триггеры, rollout и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Есть acceptance criteria, тест-план, риски и поэтапный план. |
| D. Проверяемость | 14-16 | PASS | Открытых блокеров нет, список файлов и scope зафиксированы. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, альтернативы и review описаны. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope ограничен release automation hardening. |
| 2. Понимание текущего состояния | 5 | Зафиксированы `1.5.5`, `1.5.4`, `1.5.3`, текущий workflow и реальный gap. |
| 3. Конкретность целевого дизайна | 5 | Описаны trigger model, attach semantics, docs и tests. |
| 4. Безопасность (миграция, откат) | 5 | Rollout/rollback и duplicate-publish risk учтены. |
| 5. Тестируемость | 4 | Статические и validation-only проверки определены; full publish возможен только на реальном релизе. |
| 6. Готовность к автономной реализации | 5 | Блокирующих вопросов нет. |

Итоговый балл: 29 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: после review убрана идея замены canonical trigger на `push tags`; зафиксированы provenance и metadata-preservation ограничения recovery path.
- Что осталось на решение пользователя: подтвердить переход к EXEC фразой `Спеку подтверждаю`.

### EXEC Verification Result
- `dotnet test --project .\tests\AppAutomation.Build.Tests\AppAutomation.Build.Tests.csproj -c Release`: PASS, `19/19`.
- `dotnet build .\AppAutomation.sln -c Release --disable-build-servers`: PASS.
- `dotnet test --solution .\AppAutomation.sln -c Release --no-build -v minimal`: PASS, `239` total / `238` passed / `1` skipped.
- `git diff --check`: whitespace errors нет; только line-ending warnings для `.github/workflows/publish-packages.yml` и `docs/appautomation/publishing.md`.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения:
  - recovery path переведён на `gh release upload --clobber`, чтобы он был strictly assets-only и не переписывал release metadata;
  - manual `publish=true` теперь ищет существующий release по tag contract и падает, если canonical release/tag отсутствует.
- Что проверено дополнительно:
  - canonical auto-trigger `release.published` сохранён;
  - `softprops/action-gh-release` больше не используется в этом workflow;
  - docs синхронизированы с новым recovery contract.
- Environment-specific нюансы проверки:
  - параллельный запуск полного `build` и `test` вызвал file lock на `DotnetDebug.AppAutomation.FlaUI.Tests.exe`; финальная верификация прогнана последовательно;
  - `dotnet test --disable-build-servers` несовместим с текущим `Microsoft.Testing.Platform` / `TUnit` test host и был заменён на финальный `dotnet test --solution .\AppAutomation.sln -c Release --no-build -v minimal`.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Release workflow hardening | 0.92 | Нет server-side доказательства причины пропуска `release` event | Запросить подтверждение EXEC | Да | Да, требуется фраза `Спеку подтверждаю` | Исправлять нужно repo-side архитектурный gap, а не гадать про внутренний сбой GitHub | `specs/2026-04-26-release-trigger-and-asset-attach-hardening.md` |
| EXEC | Workflow/docs/tests implementation | 0.95 | Результаты полной верификации ещё не получены | Запустить targeted и full проверки | Нет | Нет | Canonical trigger сохранён, recovery path доведён до existing-release assets-only semantics | `.github/workflows/publish-packages.yml`, `tests/AppAutomation.Build.Tests/ReleaseWorkflowTests.cs`, `docs/appautomation/publishing.md`, `specs/2026-04-26-release-trigger-and-asset-attach-hardening.md` |
| EXEC | Verification and review | 0.96 | Нет | Завершить задачу | Нет | Нет | Final verification прошла на последовательном `build` и `test`; environment-specific failures были локализованы и не относятся к функциональному изменению | `.github/workflows/publish-packages.yml`, `tests/AppAutomation.Build.Tests/ReleaseWorkflowTests.cs`, `docs/appautomation/publishing.md`, `specs/2026-04-26-release-trigger-and-asset-attach-hardening.md` |
