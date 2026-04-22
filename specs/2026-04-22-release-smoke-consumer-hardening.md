# Release Smoke Consumer Hardening

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: AppAutomation
- Масштаб: small
- Целевой релиз / ветка: `master`, успешный patch release `1.5.2`; release/tag `1.5.1` уже создан, но workflow упал на publish после частичной публикации NuGet пакетов
- Ограничения:
  - До подтверждения спеки менять только этот файл.
  - Не менять публичный API пакетов.
  - Не переписывать tag/release `1.5.0`.
  - Не отключать `NU1605` и не ослаблять restore warnings-as-errors.
  - Перед push/release выполнить локальные проверки из раздела 11.
- Связанные ссылки:
  - GitHub Actions job: `https://github.com/Kibnet/AppAutomation/actions/runs/24786696285/job/72532579907`
  - Локальный анализ: release workflow упал на `Smoke consumer`.

## 1. Overview / Цель
Исправить воспроизводимое падение релизного workflow, перенести проверку packaged template consumer на PR-этап, запушить исправление в `master` и создать новый patch release. После запуска `1.5.1` найден второй блокер publish step; финальный успешный выпуск должен быть новым patch release `1.5.2`, без переписывания уже созданного `1.5.1`.

## 2. Текущее состояние (AS-IS)
- `.github/workflows/publish-packages.yml` выполняет `Restore`, `Build`, `Test`, `Pack`, затем `Smoke consumer`; публикация пакетов начинается только после успешного smoke.
- `.github/workflows/pr-validation.yml` выполняет только `dotnet restore`, `dotnet build`, `dotnet test`.
- `Directory.Packages.props` задаёт `Avalonia.Headless` `11.3.8`.
- `src/AppAutomation.Avalonia.Headless/AppAutomation.Avalonia.Headless.csproj` публикует пакет с зависимостью `Avalonia.Headless >= 11.3.8`.
- `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/tests/SampleApp.UiTests.Headless/SampleApp.UiTests.Headless.csproj` всё ещё генерирует прямой `PackageReference Include="Avalonia.Headless" Version="11.3.7"`.
- `eng/smoke-consumer.ps1` после `pack` устанавливает `AppAutomation.Templates` из локального package output, генерирует `appauto-avalonia` consumer и делает restore generated headless project.
- Release workflow для `1.5.1` подтвердил, что `Smoke consumer` теперь проходит на GitHub runner-е.
- `eng/publish-nuget.ps1` при заданном `NUGET_SYMBOL_SOURCE` публикует `.nupkg` через `dotnet nuget push` без `--no-symbols`, из-за чего CLI неявно публикует связанный `.snupkg`; затем скрипт повторно публикует те же `.snupkg` отдельным циклом.

## 3. Проблема
Корневая проблема первого падения: template consumer содержит захардкоженную версию `Avalonia.Headless`, которая разошлась с package dependency `AppAutomation.Avalonia.Headless`, а PR validation не запускает packaged template smoke и поэтому пропускает несовместимость до релиза.

Корневая проблема второго падения: publish script дублирует публикацию symbol packages. При выпуске `1.5.1` `.snupkg` были отправлены вместе с `.nupkg`, затем повторный explicit push получил conflicts и `InternalServerError` от NuGet symbol endpoint, после чего workflow упал уже после публикации основных пакетов.

## 4. Цели дизайна
- Разделение ответственности: template content хранит consumer csproj; build tests проверяют статические инварианты template content; CI запускает packaged smoke.
- Повторное использование: использовать существующий `eng/pack.ps1` и `eng/smoke-consumer.ps1`.
- Тестируемость: добавить быстрый regression test, который падает при version drift.
- Консистентность: template `Avalonia.Headless` должен соответствовать централизованной версии из `Directory.Packages.props`.
- Идемпотентность publish: `.nupkg` и `.snupkg` должны публиковаться ровно одним выбранным путём, а повторный workflow не должен ломаться из-за собственного дублирования symbol push.
- Обратная совместимость: не менять публичные типы, CLI параметры, template shortName или структуру generated consumer.

## 5. Non-Goals (чего НЕ делаем)
- Не переписываем и не переиспользуем release/tag `1.5.0`.
- Не переписываем и не удаляем release/tag `1.5.1`, потому что пакеты этой версии уже были частично опубликованы во внешние feed-ы.
- Не меняем стратегию версионирования AppAutomation packages.
- Не заменяем PowerShell smoke скрипт на новую инфраструктуру.
- Не обновляем все сторонние зависимости, кроме точечного `Avalonia.Headless` в шаблоне.
- Не отключаем прямую зависимость template consumer на `Avalonia.Headless`, потому что generated project использует `Avalonia.Headless` API напрямую.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `SampleApp.UiTests.Headless.csproj` -> генерировать совместимую версию `Avalonia.Headless`.
- `TemplateContentTests` -> проверять, что template `Avalonia.Headless` не ниже версии из `Directory.Packages.props`; предпочтительно точное равенство для текущего шаблона.
- `.github/workflows/pr-validation.yml` -> после успешного solution test выполнять pack и smoke consumer, чтобы PR блокировался до merge.

### 6.2 Детальный дизайн
- Обновить в headless template csproj `Avalonia.Headless` с `11.3.7` на `11.3.8`.
- Добавить тест в `tests/AppAutomation.Build.Tests/TemplateContentTests.cs`:
  - прочитать `Directory.Packages.props` как XML;
  - извлечь `PackageVersion Include="Avalonia.Headless"`;
  - прочитать template headless csproj как XML или текст с XML parsing;
  - проверить, что direct `PackageReference Include="Avalonia.Headless"` имеет такую же версию.
- Расширить `pr-validation.yml`:
  - после `Test` добавить `Pack` с `pwsh -File eng/pack.ps1 -Configuration Release`;
  - добавить `Smoke consumer` с `pwsh -File eng/smoke-consumer.ps1 -Configuration Release -SkipPack`.
- Error handling остаётся прежним: `eng/smoke-consumer.ps1` падает при неуспешном `dotnet restore/build/doctor`.
- Performance tradeoff: PR validation станет дольше на pack + smoke, но это дешевле, чем падение release после tag.

### 6.3 Publish hardening
- Вычислить список `.snupkg` до основного цикла `.nupkg`.
- Если symbol packages существуют, добавить `--no-symbols` в `dotnet nuget push` для `.nupkg`, чтобы `.snupkg` не публиковались неявно.
- Оставить явный цикл `.snupkg` как единственную точку публикации symbols при заданном `SymbolSource`.
- Добавить статический regression test для `eng/publish-nuget.ps1`, который фиксирует `--no-symbols` при separate symbol publishing.

## 7. Бизнес-правила / Алгоритмы
- Template package dependency invariant:
  - `TemplateConsumer.UiTests.Headless` direct `Avalonia.Headless` version must equal `Directory.Packages.props` `Avalonia.Headless`.
  - Если централизованная версия поднимается, regression test обязан потребовать синхронного обновления шаблона.
- CI invariant:
  - PR считается release-ready только если packaged template can restore/build through `eng/smoke-consumer.ps1`.
- Publish invariant:
  - Основной `.nupkg` push обязан использовать `--no-symbols`, когда рядом есть `.snupkg`, чтобы CLI не отправлял symbols неявно и не конфликтовал с выбранной explicit/skip стратегией.

## 8. Точки интеграции и триггеры
- Триггер `pull_request` в `.github/workflows/pr-validation.yml`: запускает новый pack/smoke этап.
- Триггер `workflow_dispatch` в `.github/workflows/pr-validation.yml`: тоже запускает новый pack/smoke этап.
- Release workflow остаётся вторым контуром защиты и продолжает запускать smoke перед publish.

## 9. Изменения модели данных / состояния
- Новых persisted данных нет.
- Изменяется только template package content и CI workflow.
- Generated consumer после `dotnet new appauto-avalonia` будет получать `Avalonia.Headless` `11.3.8`.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - merge фикса в `master`;
  - создать новый release/tag `1.5.1`, чтобы `publish-packages` собрал и опубликовал пакеты с исправлением smoke;
  - после обнаружения publish blocker добавить отдельный hardening commit в `master`;
  - создать новый release/tag `1.5.2`, потому что `1.5.1` уже публиковался во внешние feed-ы и не должен переписываться.
- Rollback:
  - откатить template version/test/workflow additions одним revert commit;
  - если release `1.5.1` уже создан, не удалять опубликованные пакеты автоматически; сделать следующий patch release с revert/fix.
  - если нужно временно ускорить PR validation, smoke можно вынести в отдельный required workflow, но не удалять release smoke.
- Совместимость:
  - существующие consumers не меняются автоматически;
  - новые generated consumers будут совместимы с `AppAutomation.Avalonia.Headless >= 1.5.0`.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В шаблоне больше нет `Avalonia.Headless` `11.3.7`.
  - Regression test падает при рассинхроне `Directory.Packages.props` и template `Avalonia.Headless`.
  - PR validation запускает packaged consumer smoke до merge.
  - `eng/smoke-consumer.ps1` локально проходит после `eng/pack.ps1`.
  - Изменения закоммичены и запушены в `master`.
  - Создан новый GitHub release/tag `1.5.1`; release workflow запущен на новом tag и подтвердил прохождение smoke.
  - Publish script не дублирует `.snupkg` push при заданном `NUGET_SYMBOL_SOURCE`.
  - Создан новый GitHub release/tag `1.5.2`; release workflow проходит publish end-to-end.
- Какие тесты добавить/изменить:
  - `TemplateContentTests` добавить проверку версии `Avalonia.Headless`.
- Characterization checks:
  - Existing `SmokeConsumerScriptTests` не менять, если они остаются зелёными.
- Команды для проверки:
  - `dotnet test --project .\tests\AppAutomation.Build.Tests\AppAutomation.Build.Tests.csproj -c Release`
  - `pwsh -File .\eng\pack.ps1 -Configuration Release -Version 1.5.1`
  - `pwsh -File .\eng\smoke-consumer.ps1 -Configuration Release -Version 1.5.1 -SkipPack`
  - fallback для локальной диагностики, если `smoke-consumer` зависает до template restore из-за SDK/workload bootstrap: установить `AppAutomation.Templates@1.5.1` из `artifacts/packages/1.5.1`, сгенерировать `appauto-avalonia` consumer и выполнить `dotnet restore` generated headless project.
  - `dotnet build .\AppAutomation.sln -c Release`
  - `dotnet test --solution .\AppAutomation.sln -c Release --no-build`
  - `git push origin master`
  - `gh release create 1.5.1 --repo Kibnet/AppAutomation --target master --title "1.5.1" --notes "<release notes>"`
  - `gh release create 1.5.2 --repo Kibnet/AppAutomation --target master --title "1.5.2" --notes "<release notes>"`

## 12. Риски и edge cases
- Риск: `eng/pack.ps1` без `-Version` в PR validation использует fallback `eng/Versions.props` (`1.4.3` сейчас). Это допустимо для PR smoke, потому что проверяется взаимная совместимость локально собранных пакетов с одной resolved version; релизный workflow всё равно передаёт release version.
- Риск: PR validation станет заметно дольше. Смягчение: smoke запускается после обычных тестов и использует уже существующие скрипты без новой инфраструктуры.
- Риск: если template начнёт использовать несколько Avalonia direct dependencies, текущая проверка закрывает только known failing dependency. Это осознанно узкий bugfix; расширение можно сделать отдельной задачей.
- Риск: direct push в `master` обходит PR review. Смягчение: выполнить локальный targeted/full verification до push и проверить, что GitHub Actions release workflow стартовал после создания release.
- Риск: release creation может запустить publish на NuGet/GitHub Packages; это ожидаемое поведение текущего `.github/workflows/publish-packages.yml`.
- Риск: `1.5.1` уже частично опубликован, поэтому повторный выпуск той же версии приведёт к duplicate handling и потенциальной рассинхронизации tag/package provenance. Смягчение: не переписывать `1.5.1`, выпускать `1.5.2`.

## 13. План выполнения
1. Обновить template `Avalonia.Headless` до `11.3.8`.
2. Добавить regression test в `TemplateContentTests`.
3. Добавить `Pack` и `Smoke consumer` steps в PR validation workflow.
4. Запустить targeted build tests.
5. Запустить `pack` и `smoke-consumer` локально.
6. Запустить solution build/test.
7. Выполнить post-EXEC review и исправить критичные находки.
8. Закоммитить изменения и запушить `master`.
9. Создать GitHub release/tag `1.5.1` и проверить запуск release workflow.
10. Если release workflow падает после smoke на publish script, исправить publish idempotence отдельным scoped commit.
11. Запушить publish hardening в `master`.
12. Создать GitHub release/tag `1.5.2` и дождаться успешного workflow.

## 14. Открытые вопросы
Нет блокирующих вопросов. Пользователь уточнил требуемый финальный результат: push в `master` и новый release. После частичной публикации `1.5.1` безопасный финальный путь — новый patch release `1.5.2`, без переписывания `1.5.0` и `1.5.1`.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`
- Выполненные требования профиля:
  - Не меняется UI thread / desktop runtime logic.
  - UI automation smoke остаётся обязательным и переносится раньше в CI.
  - Перед завершением планируются `dotnet build`, `dotnet test`, packaged smoke.
  - Стабильные selectors/API не меняются.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/tests/SampleApp.UiTests.Headless/SampleApp.UiTests.Headless.csproj` | `Avalonia.Headless` `11.3.7` -> `11.3.8` | Устранить `NU1605` в generated consumer |
| `tests/AppAutomation.Build.Tests/TemplateContentTests.cs` | Добавить regression test версии `Avalonia.Headless` | Ловить drift до release |
| `.github/workflows/pr-validation.yml` | Добавить `Pack` + `Smoke consumer` | Блокировать несовместимые PR до merge/tag |
| `eng/publish-nuget.ps1` | Добавить `--no-symbols` для `.nupkg` push при separate symbol publishing | Устранить дублирующую публикацию `.snupkg` |
| `tests/AppAutomation.Build.Tests/PublishNugetScriptTests.cs` | Добавить regression tests для publish script | Ловить возврат duplicate symbol push |
| `specs/2026-04-22-release-smoke-consumer-hardening.md` | SPEC + журнал | QUEST audit trail |
| Git history / GitHub release | Commit, push в `master`, release/tag `1.5.1`, затем hardening commit и release/tag `1.5.2` | Доставить исправление и заново запустить release pipeline |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Template `Avalonia.Headless` | `11.3.7` | `11.3.8`, синхронно с central package version |
| PR validation | restore/build/test only | restore/build/test/pack/smoke-consumer |
| Regression coverage | Нет статической проверки template Avalonia version | Есть тест на version drift |
| Publish symbols | `.snupkg` может публиковаться неявно вместе с `.nupkg` и затем повторно explicit циклом | `.nupkg` push использует `--no-symbols`, когда рядом есть `.snupkg`; explicit `.snupkg` push выполняется только при заданном `SymbolSource` |
| Release workflow | Ловит ошибку после tag/release | Остаётся защитой, но ошибка должна ловиться на PR |
| Delivery | Только локальный fix plan | Push в `master`, release `1.5.1` для smoke fix, затем release `1.5.2` для publish hardening |

## 18. Альтернативы и компромиссы
- Вариант: убрать прямой `Avalonia.Headless` из template.
- Плюсы: меньше ручных версий.
- Минусы: generated code использует `Avalonia.Headless` namespace/API напрямую; implicit transitive dependency хрупче для consumer project.
- Почему выбранное решение лучше в контексте этой задачи: точечная синхронизация версии и regression test устраняют конкретную релизную ошибку без изменения consumer contract.

- Вариант: оставить PR validation быстрым и запускать smoke только вручную перед release.
- Плюсы: быстрее PR.
- Минусы: человеческий шаг снова может быть пропущен; ошибка обнаружится после tag.
- Почему выбранное решение лучше в контексте этой задачи: автоматический required CI надёжнее для release gate.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, алгоритмы, rollout и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Есть acceptance criteria, тест-план, риски и пошаговый план. |
| D. Проверяемость | 14-16 | PASS | Открытых блокеров нет, профиль и таблица файлов указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, альтернативы и review зафиксированы. |
| F. Соответствие профилю | 20 | PASS | .NET/Avalonia smoke и UI automation требования учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope ограничен release smoke hardening. |
| 2. Понимание текущего состояния | 5 | Указаны workflow, template, central versions и smoke script. |
| 3. Конкретность целевого дизайна | 5 | Файлы и проверки перечислены. |
| 4. Безопасность (миграция, откат) | 5 | Rollout/rollback и release strategy описаны. |
| 5. Тестируемость | 5 | Есть targeted, smoke и full verification commands. |
| 6. Готовность к автономной реализации | 5 | Открытых вопросов нет, план последовательный. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: добавлен явный риск про fallback `eng/Versions.props` в PR validation; scope обновлён под пользовательское требование push в `master` и release `1.5.1`.
- Что осталось на решение пользователя: подтвердить переход к EXEC фразой `Спеку подтверждаю`.

### EXEC Verification Result
- `dotnet test --project .\tests\AppAutomation.Build.Tests\AppAutomation.Build.Tests.csproj -c Release`: PASS, 14/14.
- `pwsh -File .\eng\pack.ps1 -Configuration Release -Version 1.5.1`: PASS, packages created; existing analyzer/vulnerability warnings only.
- `pwsh -File .\eng\smoke-consumer.ps1 -Configuration Release -Version 1.5.1 -SkipPack`: локально зависает до template restore на `dotnet new sln` / SDK workload bootstrap; это не воспроизводит исходный `NU1605`.
- Manual packaged-template restore fallback: PASS; `AppAutomation.Templates@1.5.1` installed from local package output, `appauto-avalonia` generated, generated headless project restored without `NU1605`.
- `dotnet build .\AppAutomation.sln -c Release`: PASS; existing warnings only.
- `dotnet test --solution .\AppAutomation.sln -c Release --no-build`: PASS, 159/159.
- Publish hardening update:
  - `pwsh -NoLogo -NoProfile -Command '$null = [scriptblock]::Create((Get-Content -Raw .\eng\publish-nuget.ps1))'`: PASS.
  - `dotnet test --project .\tests\AppAutomation.Build.Tests\AppAutomation.Build.Tests.csproj -c Release`: PASS, 16/16.
  - `git diff --check`: PASS; line-ending warnings only.
  - `dotnet build .\AppAutomation.sln -c Release`: PASS; existing warnings only.
  - `dotnet test --solution .\AppAutomation.sln -c Release --no-build`: PASS, 161/161.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: удалён случайно созданный локальный `Smoke.AppAutomation.sln`; команда targeted test в spec исправлена на `dotnet test --project`.
- Что проверено дополнительно для refactor / comments: публичный API не менялся; `11.3.7` больше не найден в source/docs/scripts; `git diff --check` без whitespace errors; unrelated untracked `specs/2026-04-14-flaui-order-position-datagrid-automation-report.md` не включается в scope.
- Остаточные риски / follow-ups: локальный full `smoke-consumer` на этой машине зависает до проверяемого сценария из-за SDK/workload bootstrap; GitHub release workflow остаётся publish gate и должен подтвердить smoke на runner-е перед фактической публикацией.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | CI failure hardening | 0.96 | Нет блокирующих данных | Запросить подтверждение EXEC | Да | Да, требуется фраза `Спеку подтверждаю` | Локальные правила QUEST запрещают менять код/workflow без подтверждённой спеки | `specs/2026-04-22-release-smoke-consumer-hardening.md` |
| SPEC | Scope update: push + release | 0.94 | Нет блокирующих данных | Запросить подтверждение EXEC | Да | Да, пользователь изменил целевой результат, но ещё не дал фразу перехода | SPEC обновлена: теперь включает push в `master` и release `1.5.1`; `1.5.0` не переписывается | `specs/2026-04-22-release-smoke-consumer-hardening.md` |
| EXEC | Template/test/CI implementation | 0.95 | Результаты проверок ещё не получены | Запустить targeted build tests | Нет | Нет | Внесены только утверждённые scoped изменения: версия template, regression test и PR smoke gate | `src/AppAutomation.Templates/.../SampleApp.UiTests.Headless.csproj`, `tests/AppAutomation.Build.Tests/TemplateContentTests.cs`, `.github/workflows/pr-validation.yml`, `specs/2026-04-22-release-smoke-consumer-hardening.md` |
| EXEC | Verification | 0.91 | Release workflow result будет известен после создания GitHub release | Выполнить post-EXEC review и подготовить commit | Нет | Нет | Targeted tests, pack, manual packaged-template restore, full build и full tests прошли; full smoke script локально завис до проверяемого сценария из-за SDK bootstrap | `artifacts/packages/1.5.1`, temp template verification workspace, `specs/2026-04-22-release-smoke-consumer-hardening.md` |
| EXEC | Post-EXEC review | 0.94 | GitHub Actions результат будет после push/release | Закоммитить и запушить scoped файлы | Нет | Нет | Diff соответствует SPEС, случайный локальный артефакт удалён, unrelated untracked файл оставлен вне scope | `.github/workflows/pr-validation.yml`, `src/AppAutomation.Templates/.../SampleApp.UiTests.Headless.csproj`, `tests/AppAutomation.Build.Tests/TemplateContentTests.cs`, `specs/2026-04-22-release-smoke-consumer-hardening.md` |
| EXEC | Release workflow triage | 0.93 | Результат нового `1.5.2` release workflow ещё не получен | Исправить duplicate symbol publish и добавить regression tests | Нет | Нет | `1.5.1` прошёл smoke, но упал на `Publish packages`: `.snupkg` публиковался дважды, второй цикл получил conflicts/500; безопаснее выпустить `1.5.2`, не переписывая уже опубликованный `1.5.1` | `eng/publish-nuget.ps1`, `tests/AppAutomation.Build.Tests/PublishNugetScriptTests.cs`, `specs/2026-04-22-release-smoke-consumer-hardening.md` |
| EXEC | Publish hardening verification | 0.94 | GitHub Actions результат будет после push/release `1.5.2` | Закоммитить, запушить `master`, создать release `1.5.2` | Нет | Нет | Publish script parse, targeted tests, solution build, full tests and whitespace check pass; scoped diff only touches publish script, its tests and spec journal | `eng/publish-nuget.ps1`, `tests/AppAutomation.Build.Tests/PublishNugetScriptTests.cs`, `specs/2026-04-22-release-smoke-consumer-hardening.md` |
