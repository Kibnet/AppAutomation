# Укрепление consumer onboarding и deterministic launch path в AppAutomation

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `product-system-design`
- Владелец: `AppAutomation maintainers`
- Масштаб: `large`
- Целевой релиз / ветка: `Unreleased` / `fix/feedback-changes`
- Ограничения: без ломающих изменений; package-first остаётся каноническим путём; RU/EN docs синхронны; isolated build mode только opt-in; `doctor` не проверяет consumer-specific secrets
- Связанные ссылки: `README.md`, `docs/appautomation/quickstart.md`, `docs/appautomation/publishing.md`, `eng/Versions.props`, `specs/AppAutomation.AdoptionJournal.md`, `specs/2026-03-24-appautomation-arm-client-integration-feedback.md`

## 1. Overview / Цель
Уменьшить стоимость первого внедрения `AppAutomation`: сделать consumer flow от установки пакетов до первого зелёного smoke воспроизводимым, диагностируемым и одинаковым для `Headless` и `FlaUI`, не меняя базовую архитектуру `Authoring -> runtime -> TestHost`.

## 2. Текущее состояние (AS-IS)
- Версия и install-команды расходятся между `README`, `quickstart`, `publishing`, `template.json` и фактической публикацией; `smoke-consumer.ps1` проверяет локальные пакеты, а не публичный feed.
- Шаблон оставляет consumer с TODO в `HeadlessSessionHooks` и с устаревшими default values.
- `doctor` проверяет topology/package setup, но не сигнализирует о неубранных template placeholders.
- Для signed-in/server-backed сценариев consumer вручную проектирует env/json-протокол передачи состояния.
- Selector contract и рекомендации по `AutomationProperties.Name`, dynamic selectors, dock/shell signals и нестандартным `UiControlType` фрагментированы.
- Desktop launch всегда строит AUT в стандартные `bin/obj`, что усиливает конфликты с design-time host и параллельными раннерами.

## 3. Проблема
Корневая проблема: consumer теряет доверие к фреймворку и тратит время на reverse engineering release/install/bootstrap contract ещё до первой полезной автоматизации.

## 4. Цели дизайна
- Один источник истины по версии и проверяемый consumer happy path.
- First-class deterministic launch scenario для desktop и headless.
- Явная диагностика scaffold readiness и pre-launch failures.
- Единый selector contract для обоих runtime.
- Обратная совместимость: существующие overloads и topology не ломаются.

## 5. Non-Goals (чего НЕ делаем)
- Не добавляем generic auth/backend abstraction и не прячем реальный login flow внутрь фреймворка.
- Не вводим обязательную instrumented-разметку AUT beyond documented selector contract.
- Не делаем isolated build mode обязательным по умолчанию.
- Не превращаем `doctor` в consumer-specific checker credentials/URLs.
- Не меняем каноническую структуру `Authoring/Headless/FlaUI/TestHost`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `eng/*`: version sync, local smoke, public-feed verification, publish gate.
- `src/AppAutomation.Tooling/*`: detection of unfinished generated scaffolding.
- `src/AppAutomation.Session.Contracts/*`: public contract для launch scenario/context.
- `src/AppAutomation.TestHost.Avalonia/*`: transport scenario payload, preflight helpers, opt-in isolated build output.
- `src/AppAutomation.Templates/*`: актуальные defaults, working headless wiring, updated next steps.
- `docs/*` и `ControlSupportMatrix.md`: единый documented consumer contract.

### 6.2 Детальный дизайн
- Версия: `eng/Versions.props` остаётся единственным source of truth; новый `eng/sync-consumer-assets.ps1` синхронизирует versioned snippets в `README`, `quickstart`, `publishing` и `template.json`; build-tests падают при расхождении.
- Consumer install path: docs и `smoke-consumer.ps1` переводятся на `dotnet new install AppAutomation.Templates@<version>`, `dotnet new tool-manifest`, `dotnet tool install AppAutomation.Tooling --version <version>`, `dotnet tool run appautomation doctor`.
- Publish gate: новый `eng/verify-published-consumer.ps1` в temp workspace проверяет remote feed через реальный consumer flow: install template, install tool via manifest, generate topology, restore/build generated `Headless/FlaUI`, run `doctor --strict`; `publish-nuget.ps1` вызывает этот скрипт после push с retry до timeout.
- Template defaults: `AppAutomationVersion` default в `template.json` синхронизируется с `eng/Versions.props`; TFM defaults остаются `net8.0` / `net8.0-windows7.0` как minimum supported baseline, но docs явно показывают override для `net10.0`.
- Headless template wiring: `SampleAppAppLaunchHost` получает `public static Type AvaloniaAppType => throw ...`; `HeadlessSessionHooks` становится рабочим каркасом через `HeadlessUnitTestSession.StartNew(SampleAppAppLaunchHost.AvaloniaAppType)` и `HeadlessRuntime.SetSession(...)`, без TODO.
- `doctor`: добавляется warning на известные placeholders/unfinished scaffold markers в generated `TestHost` и `HeadlessSessionHooks` (`REPLACE_WITH_YOUR_*`, template `NotImplementedException`, невырезанные TODO/comments); в `--strict` такие warning блокируют happy path.
- Launch scenario contract: в `AppAutomation.Session.Contracts` добавляются `AutomationLaunchScenario<TPayload>` и `AutomationLaunchContext` с фиксированными env names `APPAUTOMATION_SCENARIO_NAME` и `APPAUTOMATION_SCENARIO_PAYLOAD_PATH`, read order `ambient override -> environment`, и JSON-deserialization helper methods.
- Session cleanup: `DesktopAppLaunchOptions` и `HeadlessAppLaunchOptions` получают optional dispose callback; обе session implementations вызывают его в `Dispose()` для cleanup temp files/context.
- Desktop transport: `AvaloniaDesktopLaunchHost.CreateLaunchOptions<TPayload>(..., AutomationLaunchScenario<TPayload> scenario, ...)` сериализует payload в temp JSON, прокидывает env vars в child process и регистрирует cleanup callback.
- Headless transport: `AvaloniaHeadlessLaunchHost.Create<TPayload>(..., AutomationLaunchScenario<TPayload> scenario, ...)` сериализует payload в temp JSON, выставляет ambient launch context на lifetime session и очищает его через cleanup callback.
- Preflight diagnostics: в `AppAutomation.TestHost.Avalonia` добавляется fluent helper для required env vars/files/resolved values с masked secret output; он выбрасывает одно агрегированное исключение с actionable message и source labels, но без печати secret values.
- Selector contract: добавляется `docs/appautomation/selector-contract.md`; `README`, `quickstart` и `advanced-integration` ссылаются на него и фиксируют правило: `AutomationId` обязателен; `AutomationProperties.Name` нужен для `WaitUntilName*`; dynamic collections именуются по шаблону `Entity_{key}`; dock/shell success assertions завязываются на пользовательские UI signals, а не backend counters.
- Control matrix: `ControlSupportMatrix.md` дополняется явным `Avalonia control -> рекомендуемый UiControlType` для `ToggleSwitch`, `ItemsControl`, `ScrollViewer`, overlay/composite controls и visible/invisible control pairs.
- Runner robustness: `AvaloniaDesktopLaunchOptions` получает opt-in isolated build settings (`UseIsolatedBuildOutput`, `IsolatedBuildRoot`); `AvaloniaDesktopLaunchHost` при enabled mode запускает `dotnet build` с `BaseOutputPath` и `BaseIntermediateOutputPath`, резолвит exe из isolated output и удаляет auto-created temp root на dispose.

## 7. Бизнес-правила / Алгоритмы
- `publish-nuget.ps1` считает релиз неподтверждённым, если `verify-published-consumer.ps1` не нашёл exact version template/tool/generated projects в целевом source в пределах timeout.
- `doctor` трактует unfinished scaffold как warning, а не error; `--strict` переводит такие warning в blocking status без изменения обычного non-strict flow.
- `AutomationLaunchContext` никогда не логирует payload body; ошибки десериализации показывают `scenario name`, `payload path` и тип payload.
- Preflight helper для secret values выводит только `missing` / `set` и source label; полные значения не печатаются.
- Isolated build mode выключен по умолчанию; потребитель включает его только для AUT с design-time lock / parallel build conflicts.

## 8. Точки интеграции и триггеры
- `README` и `quickstart` становятся каноническим public entrypoint и должны совпадать с `smoke-consumer.ps1`.
- `publish-nuget.ps1` вызывает public verification после push.
- `AvaloniaDesktopLaunchHost` и `AvaloniaHeadlessLaunchHost` становятся единственными framework-level transport points для typed scenario payload.
- `doctor` вызывается после генерации template consumer и должен сигнализировать незавершённый scaffold.
- `advanced-integration.md` получает canonical examples для seeded signed-in shell и dynamic selectors.

## 9. Изменения модели данных / состояния
- Новое временное состояние: scenario payload JSON в temp directory на lifetime session.
- Новый process-local state: ambient launch context для headless session.
- Persisted repo data не добавляется; cleanup идёт через session dispose callbacks.

## 10. Миграция / Rollout / Rollback
- Rollout без breaking changes: существующие overloads launch hosts и текущий `doctor` flow сохраняются.
- Docs и smoke scripts переключаются на manifest path сразу; global tool остаётся fallback path в docs, но не primary.
- Rollback возможен точечно: отключить post-publish verification; не использовать new scenario overloads; не включать isolated build mode.
- Если isolated build mode ломает нестандартный AUT build, consumer остаётся на current in-place mode.

## 11. Тестирование и критерии приёмки
- Acceptance: все versioned snippets и `template.json` совпадают с `eng/Versions.props`; public consumer flow копируется из docs и проходит; generated headless scaffold не содержит TODO; `doctor --strict` падает на untouched template placeholders; один и тот же typed scenario payload читается через единый API в desktop/headless; preflight exceptions маскируют secrets; selector-contract docs покрывают `Name`, dynamic selectors и dock/shell assertions; isolated build mode не использует project `bin/obj`.
- Тесты: расширить `AppAutomation.Build.Tests` на version/docs/template consistency; добавить `AppAutomation.Tooling.Tests` для `doctor` placeholder detection; добавить `AppAutomation.TestHost.Avalonia.Tests` для scenario transport, cleanup callbacks, preflight masking и isolated build path resolution; обновить local smoke scripts под manifest path.
- Команды: `dotnet test AppAutomation.sln -c Release`; `pwsh -File eng/pack.ps1 -Configuration Release`; `pwsh -File eng/smoke-consumer.ps1 -Configuration Release`; `pwsh -File eng/verify-published-consumer.ps1 -Version <version> -Source <source>`.

## 12. Риски и edge cases
- Public feed propagation может быть медленной: mitigation через retry/poll timeout и явное сообщение о неполной публикации.
- Ambient launch context может протечь между headless tests: mitigation через cleanup callbacks и dedicated tests на cleanup.
- Isolated build mode может конфликтовать с кастомным MSBuild/AOT/weaving: mitigation через opt-in design и документацию fallback на sequential runs.
- RU/EN docs могут разъехаться: mitigation через sync script и build tests на выбранные файлы.

## 13. План выполнения
1. Ввести version sync + docs/tests + manifest-based smoke path.
2. Добавить public publish verification и встроить её в `publish-nuget.ps1`.
3. Обновить template defaults, `HeadlessSessionHooks`, `APPAUTOMATION_NEXT_STEPS.md`, `doctor` placeholder detection.
4. Добавить `AutomationLaunchScenario<TPayload>`, `AutomationLaunchContext`, dispose callbacks и scenario overloads для desktop/headless.
5. Добавить preflight helper и sample/docs для signed-in/server-backed bootstrap.
6. Ввести selector-contract docs, расширить `ControlSupportMatrix.md`, добавить dynamic selector examples.
7. Реализовать isolated build mode и покрыть его tests/docs.

## 14. Открытые вопросы
- Нет; по умолчанию принимаются: next release = `Unreleased`, public source default = `nuget.org`, isolated build mode = opt-in, global tool = fallback only.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `product-system-design`
- Выполненные требования профиля: цели и `Non-Goals` фиксированы; публичный API и compatibility contract описаны; UI-thread/build/selector concerns учтены; безопасность конфигурации и secret logging описаны.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `README.md`, `docs/appautomation/quickstart.md`, `docs/appautomation/publishing.md`, `docs/appautomation/advanced-integration.md` | синхронизация versioned snippets, manifest path, `@version`, selector/preflight guidance | восстановить доверие к public happy path |
| `eng/smoke-consumer.ps1`, `eng/publish-nuget.ps1`, `eng/sync-consumer-assets.ps1` (new), `eng/verify-published-consumer.ps1` (new) | local smoke по manifest, sync assets, public publish gate | закрыть разрыв между local pack и public availability |
| `src/AppAutomation.Tooling/*` | placeholder detection в `doctor`, help text | убрать ложное чувство готовности scaffold |
| `src/AppAutomation.Session.Contracts/*`, `src/AppAutomation.TestHost.Avalonia/*` | launch scenario contract, cleanup callbacks, preflight helper, isolated build mode | first-class deterministic launch + diagnostics + runner robustness |
| `src/AppAutomation.Templates/*`, `APPAUTOMATION_NEXT_STEPS.md` | working headless hooks, updated defaults and next steps | сократить manual reverse engineering |
| `tests/AppAutomation.Build.Tests/*`, `tests/AppAutomation.Tooling.Tests/*` (new), `tests/AppAutomation.TestHost.Avalonia.Tests/*` (new), `ControlSupportMatrix.md` | version/docs/tooling/runtime coverage и matrix expansion | удержать изменения под regression gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Versioned consumer docs | ручное обновление, расхождения возможны | `Versions.props` + sync script + build tests |
| Consumer install path | `--tool-path`, `::`, local smoke ≠ docs | manifest-first, `@version`, smoke = docs |
| Headless template | TODO wiring | working scaffold через `AvaloniaAppType` |
| Launch state transfer | env/json hand-rolled per consumer | typed `AutomationLaunchScenario<TPayload>` + unified context |
| Desktop build output | только in-place `bin/obj` | opt-in isolated build output |

## 18. Альтернативы и компромиссы
- Вариант: разбить на 2-3 smaller specs. Минусы: теряется единый contract между docs/release/template/API; выбран umbrella-spec, но с жёстким phased execution.
- Вариант: делать только docs fixes без API. Минусы: повторная ручная реализация launch-state и preflight останется у каждого consumer.
- Вариант: вводить отдельный CLI `preflight`. Минусы: generic tool не знает consumer-specific env logic; выбран library-level preflight helper + stricter `doctor`.

## 19. Результат прогона линтера

### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели дизайна и Non-Goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Распределение ответственности, API, ошибки, rollout и data/state описаны |
| C. Безопасность изменений | 11-13 | PASS | Нет breaking changes; rollback и ограничения явно заданы |
| D. Проверяемость | 14-16 | PASS | Acceptance, тесты и команды проверки определены |
| E. Готовность к автономной реализации | 17-19 | PASS | Решения и фазирование заданы, открытых вопросов нет |
| F. Соответствие профилю | 20 | PASS | Требования `dotnet-desktop-client` и `product-system-design` покрыты |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Корневая проблема и границы явно заданы |
| 2. Понимание текущего состояния | 5 | AS-IS опирается на два внедрения и текущие docs/scripts |
| 3. Конкретность целевого дизайна | 5 | Подсистемы, API, scripts и docs changes определены |
| 4. Безопасность (миграция, откат) | 5 | Все изменения additive или opt-in |
| 5. Тестируемость | 5 | Acceptance и конкретные проверки заданы |
| 6. Готовность к автономной реализации | 2 | Спека large, поэтому реализацию нужно вести по фазам из раздела 13 |

Итоговый балл: 27 / 30
Зона: готово к автономному выполнению

## Approval
Подтверждено пользователем в чате запросом на реализацию.
