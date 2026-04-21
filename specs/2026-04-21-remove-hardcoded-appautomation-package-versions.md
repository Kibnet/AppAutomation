# Удаление hardcoded AppAutomation package versions из docs/scripts

## 0. Метаданные
- Тип (профиль): `delivery-task`, `refactor-mechanical`
- Владелец: Codex
- Масштаб: medium
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения:
  - До подтверждения спеки не менять файлы вне `./specs/`.
  - Не откатывать уже существующие пользовательские изменения в `tests/AppAutomation.Build.Tests/TemplateContentTests.cs`, `tests/AppAutomation.Build.Tests/VersioningScriptsTests.cs`, `tests/AppAutomation.Tooling.Tests/DoctorPlaceholderTests.cs`.
  - Не менять публичный API пакетов и CLI.
  - Для consumer-facing установки использовать latest/default поведение CLI или NuGet wildcard там, где это поддерживается; для publish/pack оставить явную release version как операционный параметр.
  - Не удалять release history из `CHANGELOG.md`: это историческая документация релизов, а не onboarding/script примеры конкретной версии пакетов.
- Связанные ссылки: `README.md`, `docs/appautomation/quickstart.md`, `docs/appautomation/publishing.md`, `CONTRIBUTING.md`, `eng/sync-consumer-assets.ps1`, `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/.template.config/template.json`, `tests/AppAutomation.Build.Tests/*`, `tests/AppAutomation.Tooling.Tests/DoctorPlaceholderTests.cs`

## 1. Overview / Цель
Убрать из consumer-facing документации и build/support scripts конкретные версии NuGet-пакетов `AppAutomation.*`, а также убрать или переписать тесты, которые требуют совпадения документации/шаблонов с текущим числом версии из `eng/Versions.props`.

## 2. Текущее состояние (AS-IS)
- `README.md` и `docs/appautomation/quickstart.md` содержат pinned examples:
  - `AppAutomation.Templates@2.1.0`;
  - `AppAutomation.Tooling --version 2.1.0`;
  - `--AppAutomationVersion 2.1.0`.
- `docs/appautomation/publishing.md` и `CONTRIBUTING.md` содержат примеры `-Version 2.1.0`.
- `eng/sync-consumer-assets.ps1` читает `eng/Versions.props` и переписывает README, quickstart, publishing и `template.json` на конкретный `$resolvedVersion`.
- `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/.template.config/template.json` содержит `AppAutomationVersion.defaultValue = "2.1.0"`, из-за чего generated consumer PackageReferences получают pinned AppAutomation package version по умолчанию.
- `tests/AppAutomation.Build.Tests/ConsumerDocsTests.cs` проверяет, что docs содержат конкретный `configuredVersion`.
- `tests/AppAutomation.Build.Tests/ConsumerDocsTests.cs` проверяет, что `template.json` default `AppAutomationVersion` равен `eng/Versions.props`.
- `tests/AppAutomation.Build.Tests/TemplateContentTests.cs`, `tests/AppAutomation.Build.Tests/VersioningScriptsTests.cs`, `tests/AppAutomation.Tooling.Tests/DoctorPlaceholderTests.cs` уже изменены в рабочем дереве до этой задачи и содержат конкретные версии `1.4.3`/`1.1.0`.
- `eng/Versions.props` содержит каноническую build/package version. Это не consumer-facing пример и не должно удаляться в этой задаче, иначе `pack.ps1`, workflow и `Directory.Build.targets` потеряют source of truth для сборки пакетов.

## 3. Проблема
Одна корневая проблема: build/tests привязаны к конкретному числу версии AppAutomation-пакетов в документации и шаблонных проверках, поэтому изменение release version или рассинхронизация docs ломает NuGet-сборку без реальной регрессии продукта.

## 4. Цели дизайна
- Разделение ответственности: build version остается в `eng/Versions.props`; consumer onboarding использует latest/default flow или NuGet floating version.
- Повторное использование: команды в docs используют latest/default CLI behavior, `*` для floating consumer package version и `<version>` только там, где нужна release version для pack/publish.
- Тестируемость: тесты проверяют наличие версионно-нейтральных команд и отсутствие hardcoded AppAutomation package versions в docs/scripts.
- Консистентность: английская и русская части README/quickstart остаются симметричными.
- Обратная совместимость: параметры `-Version`, `--version`, `--AppAutomationVersion` остаются поддержаны; меняются только примеры и проверки.

## 5. Non-Goals (чего НЕ делаем)
- Не удаляем `eng/Versions.props` и не меняем механизм package version для actual pack/publish.
- Не меняем NuGet package ids, target frameworks, package metadata или публичные команды CLI.
- Не удаляем release notes из `CHANGELOG.md`.
- Не меняем версии сторонних пакетов (`Avalonia`, `TUnit`, `.NET SDK`, etc.).
- Не используем wildcard/latest для publish target version: публикуемый пакет обязан иметь конкретную версию.
- Не исправляем unrelated тесты/документацию за пределами hardcoded AppAutomation package version cleanup.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `README.md` -> latest/default onboarding; если нужен явный consumer package version override, показывать `--AppAutomationVersion "*"` для floating latest.
- `docs/appautomation/quickstart.md` -> то же для полного quickstart.
- `docs/appautomation/publishing.md` -> release commands используют `<version>` или описывают auto-resolved fallback без конкретного числа.
- `CONTRIBUTING.md` -> generic pack example `-Version "<version>"`.
- `eng/sync-consumer-assets.ps1` -> удалить файл полностью; удалить ссылки на него из docs/tests, потому что его единственная текущая роль - возвращать concrete release versions в consumer-facing assets.
- `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/.template.config/template.json` -> заменить `AppAutomationVersion.defaultValue` с concrete semver на wildcard `*`, чтобы generated PackageReferences по умолчанию брали latest compatible AppAutomation packages из configured feeds.
- `tests/AppAutomation.Build.Tests/ConsumerDocsTests.cs` -> удалить проверки на `configuredVersion` в docs и template default; оставить проверки на структурные onboarding инварианты и добавить/оставить checks на отсутствие concrete AppAutomation package versions в docs.
- `tests/AppAutomation.Build.Tests/TemplateContentTests.cs` -> убрать literal deny-list конкретных версий; если тест сохраняется, он должен проверять только наличие tokenized placeholder `__APPAUTOMATION_VERSION__` без перечисления release numbers.
- `tests/AppAutomation.Build.Tests/VersioningScriptsTests.cs` -> убрать привязку к реальному release number; для parser behavior использовать нейтральные synthetic semver constants, не связанные с текущим AppAutomation release, либо удалить только проверки, которые фактически закрепляют конкретную package version.
- `tests/AppAutomation.Tooling.Tests/DoctorPlaceholderTests.cs` -> заменить hardcoded AppAutomation package versions в fixture на нейтральный placeholder constant, чтобы тест doctor не выглядел как release-version assertion.

### 6.2 Детальный дизайн
- Для docs использовать latest/default CLI commands без версии:
  - `dotnet new install AppAutomation.Templates`;
  - `dotnet tool install AppAutomation.Tooling`;
  - `dotnet new appauto-avalonia --name MyApp`.
- Для floating consumer package override использовать quoted wildcard: `dotnet new appauto-avalonia --name MyApp --AppAutomationVersion "*"`.
- Для publish/pack docs использовать `<version>` как placeholder, потому что публикация новой версии не может использовать latest/wildcard:
  - `pwsh -File eng/publish-nuget.ps1 -Version <version>`;
  - `./eng/pack.ps1 -Version "<version>"`.
- Для regex/tests ограничивать поиск именно AppAutomation package version examples, чтобы не ловить SDK/framework/changelog versions.
- `eng/sync-consumer-assets.ps1` удалить; `docs/appautomation/publishing.md` больше не должен рекомендовать запуск этого скрипта.
- Перед финалом проверить, что `AppAutomationVersion.defaultValue = "*"` проходит restore/build в generated template flow. Если `*` окажется неподдержанным NuGet floating version в текущем toolchain, заменить на ближайший поддержанный latest-compatible wildcard и зафиксировать это в EXEC review.
- Ошибки: существующие scripts продолжают валидировать `-Version` как semver при явном вызове; сообщение об ошибке не меняется.
- Производительность: не применимо.

## 7. Бизнес-правила / Алгоритмы
- Concrete AppAutomation package version в scope: numeric semver рядом с `AppAutomation.Templates@`, `AppAutomation.Tooling --version`, `--AppAutomationVersion`, `PackageReference Include="AppAutomation.*" Version="..."`, либо `-Version` в AppAutomation publish/pack docs examples.
- Допустимые значения в consumer docs/templates: отсутствие версии для latest/default CLI flow или wildcard `*` для AppAutomation package references / `--AppAutomationVersion`.
- Допустимые значения в publish/pack docs: `<version>` или auto-resolved wording; wildcard/latest там запрещены, потому что publish target должен быть deterministic.
- Допустимые версии вне scope: `CHANGELOG.md` release headings, `.NET SDK`, target frameworks, third-party package versions, actual build source of truth `eng/Versions.props`.

## 8. Точки интеграции и триггеры
- CI/build tests: `dotnet test --project tests/AppAutomation.Build.Tests/AppAutomation.Build.Tests.csproj`.
- Tooling tests: `dotnet test --project tests/AppAutomation.Tooling.Tests/AppAutomation.Tooling.Tests.csproj`.
- Full validation: `dotnet build AppAutomation.sln -c Release` и `dotnet test --solution AppAutomation.sln -c Release --no-build`.
- Release workflow still passes explicit versions into `pack.ps1`, `publish-nuget.ps1`, `smoke-consumer.ps1`; этот поток не меняется.

## 9. Изменения модели данных / состояния
- Нет новых persisted данных.
- Нет миграций.
- `eng/Versions.props` остается источником фактической package version для сборки.

## 10. Миграция / Rollout / Rollback
- Rollout: применить текстовые и тестовые изменения одним mechanical pass, затем targeted/full проверки.
- Rollback: revert только файлов из таблицы изменений; `eng/Versions.props` и package outputs не затрагиваются.
- Обратная совместимость: команды с явным `-Version`/`--version` остаются рабочими.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В `README.md`, `docs/appautomation/quickstart.md`, `docs/appautomation/publishing.md`, `CONTRIBUTING.md` нет конкретных numeric versions для AppAutomation NuGet package commands/examples.
  - Consumer onboarding docs используют latest/default CLI commands без версии или `--AppAutomationVersion "*"` там, где нужен floating override.
  - `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/.template.config/template.json` использует wildcard default для `AppAutomationVersion`, если это подтверждено restore/build.
  - `eng/sync-consumer-assets.ps1` удален, а ссылки на него убраны из docs/tests.
  - Тесты больше не требуют, чтобы docs/template examples содержали `eng/Versions.props` value.
  - Existing useful tests for onboarding structure, doctor placeholders и version parser behavior сохранены там, где они не закрепляют конкретную release version.
  - `git diff` не содержит unrelated changes.
- Какие тесты добавить/изменить:
  - Изменить `ConsumerDocsTests` на latest/wildcard assertions и проверку отсутствия ссылок на удаленный sync script.
  - Изменить или удалить version-specific parts в `TemplateContentTests`, `VersioningScriptsTests`, `DoctorPlaceholderTests`.
- Characterization / contract checks:
  - Сохранить проверку latest/default onboarding commands without explicit version.
  - Сохранить проверку `__APPAUTOMATION_VERSION__` в template csproj, если она не требует конкретного числа.
- Команды для проверки:
  - `dotnet test --project tests/AppAutomation.Build.Tests/AppAutomation.Build.Tests.csproj -c Release`
  - `dotnet test --project tests/AppAutomation.Tooling.Tests/AppAutomation.Tooling.Tests.csproj -c Release`
  - `dotnet build AppAutomation.sln -c Release`
  - `dotnet test --solution AppAutomation.sln -c Release --no-build`

## 12. Риски и edge cases
- Риск: удалить слишком много проверок docs и потерять coverage onboarding flow. Смягчение: оставить структурные проверки команд и doctor/test snippets.
- Риск: wildcard `*` для generated PackageReference может быть менее детерминированным для consumers. Это осознанный tradeoff пользовательского требования "latest"; смягчение: docs должны оставлять явный override `--AppAutomationVersion "<version>"` для reproducible onboarding.
- Риск: wildcard `*` может оказаться неподдержанным в конкретном restore path. Смягчение: проверить generated template restore/build; если нужно, использовать ближайший поддержанный NuGet floating pattern.
- Риск: broad regex начнет ловить `CHANGELOG.md` или SDK/framework versions. Смягчение: search patterns привязать к AppAutomation package commands.
- Риск: уже измененные пользователем тесты будут перезаписаны. Смягчение: править их поверх текущего содержимого, без revert.

## 13. План выполнения
1. Обновить docs: README, quickstart заменить concrete AppAutomation package versions на latest commands без версии и `--AppAutomationVersion "*"`.
2. Обновить publishing/CONTRIBUTING: заменить concrete publish/pack versions на `<version>` и убрать инструкции запускать `eng/sync-consumer-assets.ps1`.
3. Удалить `eng/sync-consumer-assets.ps1`.
4. Обновить `template.json`: `AppAutomationVersion.defaultValue` заменить на wildcard latest-compatible value.
5. Обновить build/tooling tests:
   - убрать `configuredVersion` assertions для docs;
   - убрать `TemplateConfig_DefaultVersion_MatchesConfiguredVersion`;
   - добавить/сохранить проверку wildcard default для template `AppAutomationVersion`;
   - добавить/сохранить проверку отсутствия ссылок на `eng/sync-consumer-assets.ps1`;
   - убрать literal deny-list конкретных AppAutomation versions;
   - заменить fixture package versions в doctor tests на single neutral constant.
6. Выполнить targeted search на hardcoded AppAutomation package versions в docs/scripts/tests.
7. Запустить targeted tests.
8. Запустить build/full tests, если окружение позволит.
9. Выполнить post-EXEC review и исправить найденные high-confidence проблемы.

## 14. Открытые вопросы
Нет блокирующих вопросов. Решение после review: `eng/sync-consumer-assets.ps1` удаляется; `template.json` входит в scope и получает wildcard default для `AppAutomationVersion`. Рабочее допущение: `eng/Versions.props` и `CHANGELOG.md` не входят в запрет, потому что это build source of truth и release history, а не brittle docs/scripts package-version examples.

## 15. Соответствие профилю
- Профиль: `refactor-mechanical`
- Выполненные требования профиля:
  - Таблица объёма изменений по файлам есть в разделе 16.
  - Матрица `было -> стало` есть в разделе 17.
  - План по этапам и проверки есть в разделах 11 и 13.
  - Критерии завершённости и rollback есть в разделах 10 и 11.
  - Semantic diff будет проверен через targeted search и тесты.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `README.md` | Заменить `2.1.0` в AppAutomation package commands на latest/default flow и `--AppAutomationVersion "*"` там, где нужен override | Убрать brittle consumer docs |
| `docs/appautomation/quickstart.md` | То же для EN/RU quickstart; использовать latest/default и `--AppAutomationVersion "*"` | Убрать brittle consumer docs |
| `docs/appautomation/publishing.md` | Заменить `-Version 2.1.0` на generic placeholder или auto-resolved wording | Убрать release-number coupling из docs |
| `CONTRIBUTING.md` | Заменить pack example `2.1.0` на `<version>` | Убрать concrete package version из contributor docs |
| `eng/sync-consumer-assets.ps1` | Удалить файл | Скрипт существует только для устаревшей синхронизации concrete versions |
| `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/.template.config/template.json` | Заменить `AppAutomationVersion.defaultValue` на wildcard latest-compatible value | Generated consumers должны брать latest AppAutomation packages по умолчанию |
| `tests/AppAutomation.Build.Tests/ConsumerDocsTests.cs` | Убрать checks against `ReadConfiguredVersion`; сохранить latest/wildcard docs checks | Не валить build из-за docs/version mismatch |
| `tests/AppAutomation.Build.Tests/TemplateContentTests.cs` | Убрать literal checks `1.1.0`/`1.4.3`; оставить placeholder invariant при необходимости | Убрать brittle version deny-list |
| `tests/AppAutomation.Build.Tests/VersioningScriptsTests.cs` | Отвязать parser tests от real release version или удалить version-specific assertions | Сохранить behavior coverage без release coupling |
| `tests/AppAutomation.Tooling.Tests/DoctorPlaceholderTests.cs` | Убрать hardcoded AppAutomation package version literals из fixture | Doctor test не должен закреплять release number |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Docs template install | `AppAutomation.Templates@2.1.0` | `dotnet new install AppAutomation.Templates` |
| Docs tool install | `AppAutomation.Tooling --version 2.1.0` | `dotnet tool install AppAutomation.Tooling` |
| Docs template parameter | `--AppAutomationVersion 2.1.0` | omit for default latest flow or use `--AppAutomationVersion "*"` |
| Publishing docs | `-Version 2.1.0` | `-Version <version>` или explicit fallback wording |
| Template default | `"defaultValue": "2.1.0"` | `"defaultValue": "*"`; fallback допустим только на другой поддержанный wildcard, не concrete semver |
| Sync script | Переписывает docs/template на `$resolvedVersion` | Файл удален, ссылки удалены |
| Consumer docs tests | Требуют `configuredVersion` в docs | Проверяют latest/wildcard commands и отсутствие hardcoded AppAutomation package versions |
| Template content tests | Deny-list конкретных чисел версии | Placeholder invariant без release-number literals |

## 18. Альтернативы и компромиссы
- Вариант: оставить `sync-consumer-assets.ps1`, но менять expected version в тестах.
- Плюсы: минимальный diff.
- Минусы: сохраняет хрупкую систему, которая ломает build при рассинхронизации docs/release version.
- Почему выбранное решение лучше: оно убирает саму причину brittle coupling, сохраняя фактический versioning flow для pack/publish.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность файлов, правила, интеграции, rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Acceptance criteria, риски и план выполнения есть. |
| D. Проверяемость | 14-16 | PASS | Открытых блокеров нет, команды и таблица файлов указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Матрица было/стало, альтернатива и review result есть. |
| F. Соответствие профилю | 20 | PASS | Требования `refactor-mechanical` отражены. |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope ограничен AppAutomation package versions в docs/scripts/tests. |
| 2. Понимание текущего состояния | 5 | Найдены docs, sync script и brittle tests. |
| 3. Конкретность целевого дизайна | 5 | Есть правила latest/wildcard, удаление sync script и file-level plan. |
| 4. Безопасность (миграция, откат) | 5 | Build source of truth не меняется, rollback ограничен. |
| 5. Тестируемость | 5 | Есть targeted/full commands и acceptance criteria. |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов, план по этапам готов. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: после review явно выбраны latest/default и wildcard для consumer onboarding, `template.json` включен в scope, `eng/sync-consumer-assets.ps1` удаляется полностью; `eng/Versions.props`, `CHANGELOG.md`, SDK/framework/third-party versions остаются вне scope.
- Что осталось на решение пользователя: только подтверждение перехода в EXEC.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: проверочные команды в spec приведены к синтаксису текущего SDK (`dotnet test --project ...`); wildcard default подтвержден generated template restore/build через локальный package source.
- Что проверено дополнительно для refactor / comments: повторный поиск не нашел concrete AppAutomation package versions в `README.md`, `docs`, `CONTRIBUTING.md`, `eng`, `tests`, `src`; ссылки на удаленный `eng/sync-consumer-assets.ps1` остались только в этой spec и в тесте отрицательной проверки.
- Остаточные риски / follow-ups: `dotnet build` оставляет существующие warnings NU1903/CA*, не связанные с этой задачей.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Инструкция и discovery | 0.95 | Нет | Создать spec | Да | Нет | QUEST требует spec-first и подтверждение до EXEC | `C:\Projects\My\Agents\AGENTS.md`, central governance docs |
| SPEC | Поиск hardcoded versions | 0.9 | Нет | Зафиксировать scope | Да | Нет | Найдены docs examples, sync script и tests, которые закрепляют configured version | `README.md`, `docs/appautomation/*.md`, `CONTRIBUTING.md`, `eng/*.ps1`, `tests/*.cs` |
| SPEC | Создание спецификации | 0.92 | Нет | Дождаться фразы `Спеку подтверждаю` | Да | Нет | Реализация вне `specs` запрещена до approval | `specs/2026-04-21-remove-hardcoded-appautomation-package-versions.md` |
| SPEC | Review update | 0.94 | Нет | Дождаться фразы `Спеку подтверждаю` | Да | Да: пользователь уточнил latest/wildcard и удаление скрипта | Убрана неоднозначность по `template.json` и `eng/sync-consumer-assets.ps1` | `specs/2026-04-21-remove-hardcoded-appautomation-package-versions.md` |
| EXEC | Docs/scripts cleanup | 0.9 | Нужно проверить тестами wildcard restore path | Обновить brittle tests | Нет | Да: пользователь подтвердил spec | Consumer docs переведены на latest/wildcard, release docs на `<version>`, sync script удален | `README.md`, `docs/appautomation/quickstart.md`, `docs/appautomation/publishing.md`, `CONTRIBUTING.md`, `src/AppAutomation.Templates/content/AppAutomation.Avalonia.Consumer/.template.config/template.json`, `eng/sync-consumer-assets.ps1` |
| EXEC | Test cleanup | 0.88 | Нужно запустить тесты | Выполнить targeted search и тесты | Нет | Нет | Убраны проверки docs/template against `eng/Versions.props`, добавлены latest/wildcard инварианты | `tests/AppAutomation.Build.Tests/ConsumerDocsTests.cs`, `tests/AppAutomation.Build.Tests/TemplateContentTests.cs`, `tests/AppAutomation.Build.Tests/VersioningScriptsTests.cs`, `tests/AppAutomation.Tooling.Tests/DoctorPlaceholderTests.cs` |
| EXEC | Validation | 0.93 | Нет | Выполнить post-EXEC review | Нет | Нет | Targeted tests, full build/test и generated wildcard template restore/build прошли | `dotnet test --project ...Build.Tests`, `dotnet test --project ...Tooling.Tests`, `dotnet build AppAutomation.sln -c Release`, `dotnet test --solution AppAutomation.sln -c Release --no-build`, `eng/pack.ps1`, generated template smoke |
| EXEC | Post-EXEC review | 0.94 | Нет | Финальный отчёт | Нет | Нет | Diff соответствует spec; concrete AppAutomation versions и docs/script links удалены в целевом scope | Финальный diff, targeted `rg`, `git status` |
| EXEC | PR CI follow-up | 0.91 | Нет | Запушить fix в PR | Нет | Да: пользователь попросил исправить CI | No-build ветка sample-теста больше не ищет Debug artifact в Release-only CI workspace | `sample/DotnetDebug.Tests/LaunchOptionsDefaultsTests.cs`, `dotnet test --project sample/DotnetDebug.Tests/DotnetDebug.Tests.csproj -c Release`, `dotnet build AppAutomation.sln -c Release --no-restore`, `dotnet test --solution AppAutomation.sln -c Release --no-build` |
| EXEC | Release smoke follow-up | 0.9 | Нужно подтвердить локальными тестами | Запустить targeted/full проверки, затем запушить fix в PR | Нет | Да: пользователь попросил чинить | Smoke consumer runtime test project теперь явно генерируется как executable test project; local tool manifest создаётся в `.config`, `DOTNET_CLI_HOME` изолируется и tool явно восстанавливается перед `doctor`; добавлены regression-тесты, чтобы это проверялось на этапе build tests | `eng/smoke-consumer.ps1`, `tests/AppAutomation.Build.Tests/SmokeConsumerScriptTests.cs` |
| EXEC | Release smoke validation | 0.94 | Нет | Закоммитить и запушить fix в PR | Нет | Нет | Build tests, full smoke consumer, full build и full test run прошли; остались только существующие warnings NU1903/CA* | `dotnet test --project tests/AppAutomation.Build.Tests/AppAutomation.Build.Tests.csproj -c Release`, `eng/smoke-consumer.ps1 -SkipPack`, `dotnet build AppAutomation.sln -c Release --no-restore`, `dotnet test --solution AppAutomation.sln -c Release --no-build` |
