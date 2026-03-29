# BRD: Устранение review-gap'ов после onboarding uplift

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `product-system-design`
- Владелец: `AppAutomation maintainers`
- Масштаб: `medium`
- Целевой релиз / ветка: `Unreleased` / `fix/feedback-changes`
- Ограничения:
  - без breaking changes публичного API
  - без расширения scope beyond remediation
  - `doctor` не превращается в consumer-specific credential checker
- Связанные ссылки:
  - `specs/2026-03-25-appautomation-consumer-onboarding-deterministic-launch.md`
  - `specs/2026-03-24-appautomation-arm-client-integration-feedback.md`
  - `specs/AppAutomation.AdoptionJournal.md`
  - `AGENTS.md` + `C:\Projects\My\Agents\AGENTS.md`
- Instruction stack (quest):
  - `quest-governance`
  - `collaboration-baseline`
  - `testing-baseline`
  - `testing-dotnet`
  - `dotnet-desktop-client`
  - `product-system-design`
  - `spec-linter`
  - `spec-rubric`

## 1. Overview / Цель
### 1.1 Бизнес-контекст (BRD)
Первая волна улучшений onboarding закрыла основной функциональный разрыв, но оставила операционные и диагностические зазоры, которые напрямую влияют на доверие потребителя к framework-опыту первого внедрения.

### 1.2 Стейкхолдеры
- Maintainers `AppAutomation`
- Команды-потребители `Avalonia` desktop AUT
- Release owner, отвечающий за publish-gate и reproducible install path

### 1.3 Бизнес-цель
Снизить стоимость второго и последующих внедрений за счёт:
- детерминированного поведения launch/runtime при параллелизме
- предсказуемого desktop build lifecycle
- прозрачной и честной publish/smoke валидации
- полного закрытия ключевых onboarding pain points из двух внедрений

## 2. Текущее состояние (AS-IS)
- `AutomationLaunchContext` использует process-global ambient override, что потенциально даёт cross-test leakage при параллельных headless запусках.
- `DesktopSession` и `DesktopAppSession` могут потерять первичный launch-failure, если `DisposeCallback` выбрасывает исключение в `catch`.
- `UseIsolatedBuildOutput=true` создаёт новый auto temp root на каждый вызов и де-факто подрывает `BuildOncePerProcess=true`.
- `smoke-consumer.ps1` и `verify-published-consumer.ps1` внутри себя дополняют generated scaffold перед strict-проверкой; docs описывают путь без явного акцента на scripted completion.
- В docs нет полного closure по замечаниям внедрений:
  - явный `dotnet test --project` и `dotnet test --solution` path
  - troubleshooting для `Headless session is not initialized`
- Onboarding docs всё ещё жёстко прошивают конкретную package version даже там, где consumer чаще всего ожидает установку последней доступной версии из feed.

## 3. Проблема
Корневая проблема: post-uplift onboarding path стал заметно лучше, но пока не полностью надёжен и не полностью честно отражён в release/documentation contract, из-за чего остаётся риск повторной потери доверия на интеграции.

## 4. Цели дизайна
- Сохранить детерминизм и изоляцию launch context при параллельных сценариях.
- Гарантировать сохранение первичного источника ошибки при любых launch/pre-launch сбоях.
- Вернуть ожидаемую семантику `BuildOncePerProcess` в isolated desktop build mode.
- Убрать расхождение между фактическим verify-path и тем, что обещают docs.
- Полностью закрыть оставшиеся high-signal onboarding пробелы из feedback.
- Развести `latest-by-default` onboarding guidance и explicit version pinning для release/reproducibility сценариев.

## 5. Non-Goals (чего НЕ делаем)
- Не добавляем новые продуктовые подсистемы вне remediation scope.
- Не реализуем автоматический scanner отсутствующих `AutomationId` в AUT.
- Не проектируем универсальный dock/ReactiveUI abstraction layer.
- Не меняем каноническую topology `Authoring -> Headless -> FlaUI -> TestHost`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/AppAutomation.Session.Contracts/*`: корректный scope ambient launch context.
- `src/AppAutomation.Avalonia.Headless/*` и `src/AppAutomation.FlaUI/*`: сохранение primary exception при cleanup.
- `src/AppAutomation.TestHost.Avalonia/*`: стабильная build-once семантика для isolated output.
- `eng/*`: честная consumer verification стратегия.
- `README` + `docs/appautomation/*`: финальная фиксация onboarding guidance из feedback.

### 6.2 Детальный дизайн
- Launch context:
  - заменить process-global single slot на scope-friendly storage (рекомендуется `AsyncLocal` + stack semantics)
  - сохранить порядок чтения `ambient override -> environment`
  - nested scopes корректно восстанавливают предыдущий контекст
- Launch error handling:
  - primary launch exception всегда возвращается наружу как root-cause
  - cleanup failure добавляется как secondary (`AggregateException` или явно прикреплённая вложенная ошибка) без маскировки root-cause
- Isolated build output:
  - для auto-root использовать stable-per-process path, вычисляемый из project/configuration/tfm
  - `BuildOncePerProcess=true` должен работать одинаково в обычном и isolated mode
  - cleanup policy:
    - explicit `IsolatedBuildRoot` не удаляется framework-ом
    - auto-root удаляется по завершению процесса или final shared release, без race-condition
- Honest verification:
  - target state: strict проверка проходит на документированном consumer path
  - если нужен scripted completion scaffolding, это должно быть явно отражено в docs и acceptance
- Docs:
  - добавить и RU, и EN эквиваленты для:
    - `dotnet test --project ...`
    - `dotnet test --solution ...`
    - `Headless session is not initialized` troubleshooting
  - разделить policy по версиям:
    - `README` и `quickstart` показывают unpinned install path как основной consumer flow
    - рядом присутствует короткий pinned example для reproducible install
    - `publishing` и release-oriented scripts сохраняют explicit `Version`

## 7. Бизнес-правила / Алгоритмы (BRD-требования)
- `FR-1`: При параллельных headless сценариях контексты запуска не пересекаются.
- `FR-2`: При launch failure первичная причина ошибки не теряется, даже если cleanup падает.
- `FR-3`: `BuildOncePerProcess=true` не деградирует при включённом isolated build mode.
- `FR-4`: Publish verification не заявляет green path, недостижимый без скрытого scripted patching.
- `FR-5`: Документация содержит рабочие команды для current SDK/MTP и troubleshooting ключевой headless ошибки.
- `FR-6`: Onboarding docs не требуют explicit package version там, где consumer path должен брать последнюю доступную версию по умолчанию.
- `FR-7`: Release/publish docs и scripts сохраняют explicit version pinning там, где важна воспроизводимость конкретного релиза.
- `NFR-1`: Изменения не ломают существующие non-isolated desktop и headless сценарии.
- `NFR-2`: Изменения покрыты regression-тестами и входящими командами из спеки.

## 8. Точки интеграции и триггеры
- `AutomationLaunchContext.TryGetCurrent/GetRequired/PushAmbientOverride`
- `DesktopSession.Launch(...)`
- `DesktopAppSession.Launch(...)`
- `AvaloniaDesktopLaunchHost.CreateLaunchOptions(...)`
- `eng/smoke-consumer.ps1`
- `eng/verify-published-consumer.ps1`
- `README.md`
- `docs/appautomation/quickstart.md`
- `docs/appautomation/publishing.md`
- `docs/appautomation/advanced-integration.md`

## 9. Изменения модели данных / состояния
- Persisted data: не добавляется.
- Runtime state:
  - ambient launch context storage model меняется (scope-aware вместо single global slot).
  - isolated build root lifecycle становится стабильным и переиспользуемым в рамках процесса.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - additive и обратно совместимый
  - включение через текущие API без breaking signature changes
- Rollback:
  - можно откатить точечно по подсистемам (context scoping / launch exception handling / docs / scripts)
  - при rollback release-gate может вернуть текущую non-ideal semantics

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - параллельные headless-тесты не делят launch context
  - при failing cleanup root launch exception остаётся первичной
  - isolated mode + `BuildOncePerProcess=true` не пересобирает AUT на каждый вызов
  - smoke/verify и docs описывают один и тот же достижимый strict-path
  - `README` и `quickstart` содержат `--project`, `--solution` и headless troubleshooting
  - `README` и `quickstart` используют unpinned install commands как основной onboarding path
  - `publishing.md` и release scripts сохраняют explicit `Version`
- Тесты:
  - расширить `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs`
  - добавить regression для parallel context isolation
  - добавить regression для primary-vs-cleanup exception chain
  - добавить regression для stable isolated build root semantics
  - обновить `tests/AppAutomation.Build.Tests/ConsumerDocsTests.cs` под docs acceptance
- Команды проверки:
  - `dotnet test AppAutomation.sln -c Debug`
  - `pwsh -File eng/smoke-consumer.ps1 -Configuration Release -Version <version>`
  - `pwsh -File eng/verify-published-consumer.ps1 -Version <version> -Source <source>`

## 12. Риски и edge cases
- `AsyncLocal` scoping может выявить неявные зависимости старых тестов на process-global контекст.
- Stable isolated roots могут оставлять мусор при аварийном завершении процесса без корректного dispose.
- Fixture или scripted completion strategy для verify может увеличить время release-gate.
- Неполная синхронизация RU/EN docs приведёт к повторению onboarding drift.

## 13. План выполнения
1. Исправить scope-модель launch context и добавить parallel regression coverage.
2. Исправить обработку launch + cleanup exceptions в обеих session-реализациях.
3. Нормализовать isolated build root lifecycle и build-once semantics.
4. Выровнять smoke/verify contract с publish/docs narrative.
5. Обновить onboarding docs и build-tests под закрытие оставшихся feedback gaps.

## 14. Открытые вопросы
- Нет блокирующих открытых вопросов.
- Решение по verify-path принимается в пользу "документированного достижимого strict-path"; реализация может быть через fixture или явно задокументированный scripted completion, но выбранный вариант обязан быть прозрачен в docs.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `product-system-design`
- Выполненные требования профиля:
  - определены цели и жёсткие `Non-Goals`
  - зафиксированы API/контрактные последствия для launch/runtime
  - учтены диагностика, стабильность параллельного исполнения и desktop build lifecycle
  - задана стратегия обратной совместимости и rollback

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Session.Contracts/AutomationLaunchContext.cs` | scope-aware ambient context | исключить cross-test leakage |
| `src/AppAutomation.Avalonia.Headless/Session/DesktopSession.cs` | сохранить primary exception при cleanup failure | улучшить диагностику launch failures |
| `src/AppAutomation.FlaUI/Session/DesktopAppSession.cs` | сохранить primary exception при cleanup failure | улучшить диагностику launch failures |
| `src/AppAutomation.TestHost.Avalonia/AvaloniaDesktopLaunchHost.cs` | stable isolated root + корректный build-once key | убрать performance regression |
| `eng/smoke-consumer.ps1` | синхронизировать verify narrative со strict-path | честный smoke contract |
| `eng/verify-published-consumer.ps1` | синхронизировать verify narrative со strict-path | честный publish gate |
| `README.md` | `dotnet test --project/--solution` + troubleshooting + latest-by-default install examples | закрыть feedback gaps onboarding |
| `docs/appautomation/quickstart.md` | `dotnet test --project/--solution` + troubleshooting + latest-by-default install examples | закрыть feedback gaps onboarding |
| `docs/appautomation/publishing.md` | явный strict-path contract | убрать расхождение docs vs scripts |
| `docs/appautomation/advanced-integration.md` | диагностические рекомендации по launch/cleanup | снизить интеграционные риски |
| `tests/AppAutomation.TestHost.Avalonia.Tests/LaunchContractTests.cs` | новые regression tests | предотвратить повторные регрессии |
| `tests/AppAutomation.Build.Tests/ConsumerDocsTests.cs` | docs acceptance checks | удержать doc contract |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Ambient launch context | process-global single slot | scope-aware isolation |
| Launch failure reporting | cleanup может замаскировать root-cause | root-cause сохраняется |
| Isolated + BuildOncePerProcess | частая пересборка | реальное build-once поведение |
| Publish/strict narrative | implicit scripted step | явно описанный достижимый path |
| Onboarding CLI guidance | частично закрыта | полный рабочий набор команд, troubleshooting и latest-by-default policy |

## 18. Альтернативы и компромиссы
- Вариант: оставить process-global context и ограничиться sequential runs.
  - Плюсы: минимум изменений.
  - Минусы: не решает параллельный leakage и остаётся хрупким.
- Вариант: полностью отключить isolated mode при `BuildOncePerProcess=true`.
  - Плюсы: простая логика.
  - Минусы: теряется часть ценности isolated output.
- Вариант: оставить explicit versions во всех docs.
  - Плюсы: максимум воспроизводимости по тексту документа.
  - Минусы: onboarding docs быстрее устаревают и хуже соответствуют ожиданию "поставь последнее стабильное".
- Выбранный путь:
  - сохраняет existing API
  - закрывает ключевые correctness gaps
  - минимизирует риск повторного onboarding drift
  - разводит потребительский happy path и release-engineering path по реальным сценариям использования

## 19. Результат прогона линтера
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, контракты, интеграции, обработка ошибок и rollout описаны |
| C. Безопасность изменений | 11-13 | PASS | additive подход, обратная совместимость и rollback определены |
| D. Проверяемость | 14-16 | PASS | Acceptance, тесты и команды проверки заданы |
| E. Готовность к автономной реализации | 17-19 | PASS | Пошаговый план есть, блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | Требования выбранных профилей покрыты |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Чётко определены бизнес-цель и границы remediation |
| 2. Понимание текущего состояния | 5 | AS-IS привязан к конкретным runtime/docs/scripts gap'ам |
| 3. Конкретность целевого дизайна | 5 | Определены API-level изменения и правила поведения |
| 4. Безопасность (миграция, откат) | 5 | Rollout/rollback и совместимость описаны |
| 5. Тестируемость | 5 | Есть acceptance и конкретный regression test plan |
| 6. Готовность к автономной реализации | 5 | Задача декомпозирована, блокеров нет |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

## Approval
Ожидается фраза: "Спеку подтверждаю"
