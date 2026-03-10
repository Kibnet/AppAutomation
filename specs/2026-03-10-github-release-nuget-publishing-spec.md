# GitHub Release NuGet Publishing

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client`
- Владелец: Framework Maintainers
- Масштаб: medium
- Целевая ветка: текущая рабочая ветка
- Ограничения:
  - Не менять состав publishable пакетов `AppAutomation.*`.
  - Не менять публичный API framework-пакетов без отдельного согласования.
  - Сохранить локальные сценарии `eng/pack.ps1`, `eng/publish-nuget.ps1` и `eng/smoke-consumer.ps1` рабочими.
  - GitHub publish flow должен запускаться именно при публикации GitHub Release.
  - Версия publish-пакетов в CI должна определяться из release tag, а не из локального version-файла.
  - Из поддерживаемых файлов репозитория должен быть удалён устаревший legacy-нейминг.
  - Перед завершением должны оставаться рабочими `dotnet build`, `dotnet test`, `eng/pack.ps1`, `eng/smoke-consumer.ps1`.
- Связанные ссылки:
  - `.github/workflows/publish-packages.yml`
  - `Directory.Build.targets`
  - `eng/Versions.props`
  - `eng/pack.ps1`
  - `eng/publish-nuget.ps1`
  - `eng/smoke-consumer.ps1`
  - `docs/appautomation/publishing.md`
  - `README.md`
  - `sample/DotnetDebug.AppAutomation.TestHost/DotnetDebugAppLaunchHost.cs`
  - `sample/Verify-UiScenarioDiscoveryParity.ps1`
  - `src/AppAutomation.Authoring/UiControlSourceGenerator.cs`
  - `ControlSupportMatrix.md`
  - `C:\Projects\My\Agents\instructions\core\quest-governance.md`
  - `C:\Projects\My\Agents\instructions\core\collaboration-baseline.md`
  - `C:\Projects\My\Agents\instructions\core\testing-baseline.md`
  - `C:\Projects\My\Agents\instructions\contexts\testing-dotnet.md`
  - `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-linter.md`
  - `C:\Projects\My\Agents\instructions\governance\spec-rubric.md`

## 1. Overview / Цель
Подготовить репозиторий к надёжной публикации NuGet-пакетов `AppAutomation.*` при публикации GitHub Release. Целевое состояние:

1. GitHub Actions запускает release pipeline на событии `release.published`.
2. Package version в CI извлекается из release tag формата `appautomation-v<version>`.
3. Эта версия единообразно используется для `dotnet pack`, путей артефактов и `nuget push`.
4. Репозиторий больше не содержит устаревший legacy-нейминг в поддерживаемых исходниках, скриптах и документации.
5. Полный pipeline реально проходит локальные проверки: build, tests, pack, smoke consumer.

Дополнительно нужно устранить уже выявленный блокер: текущий full test run падает из-за sample `DotnetDebug` launch host, который ищет старое имя solution-файла.

## 2. Текущее состояние (AS-IS)
- В репозитории уже есть release-oriented tooling:
  - `eng/pack.ps1` пакует 6 publishable проектов;
  - `eng/publish-nuget.ps1` публикует `.nupkg` и `.snupkg`;
  - `eng/smoke-consumer.ps1` успешно валидирует внешний consumer path.
- Сейчас package version читается из `eng/Versions.props` и прокидывается через `Directory.Build.targets` и release scripts.
- Текущий workflow `.github/workflows/publish-packages.yml` срабатывает по:
  - `workflow_dispatch`;
  - `push` тега `appautomation-v*`.
- Текущий workflow вычисляет version из локального файла, а не из GitHub Release event.
- Фактическая локальная проверка показала:
  - `dotnet build AppAutomation.sln -c Release` проходит;
  - `pwsh -File eng/pack.ps1 -Configuration Release` проходит;
  - `pwsh -File eng/smoke-consumer.ps1 -Configuration Release -SkipPack` проходит;
  - `dotnet test --solution AppAutomation.sln -c Release --no-build` падает.
- Причина падения тестов не в Actions YAML, а в sample test host:
  - `sample/DotnetDebug.AppAutomation.TestHost/DotnetDebugAppLaunchHost.cs` ищет `DotnetDebug.sln`;
  - в репозитории solution называется `AppAutomation.sln`;
  - из-за этого падают FlaUI integration tests до стадии publish.
- В поддерживаемых файлах репозитория всё ещё присутствует устаревший legacy-нейминг:
  - `eng/Versions.props`
  - `Directory.Build.targets`
  - `eng/pack.ps1`
  - `eng/publish-nuget.ps1`
  - `eng/smoke-consumer.ps1`
  - `docs/appautomation/publishing.md`
  - `src/AppAutomation.Authoring/UiControlSourceGenerator.cs`
  - `sample/Verify-UiScenarioDiscoveryParity.ps1`
  - `ControlSupportMatrix.md`
- Дополнительный risk в current flow:
  - release version source of truth находится вне самого GitHub Release;
  - это допускает публикацию пакетов одной версии из релиза с другим tag.

## 3. Проблема
Репозиторий ещё не готов к надёжной автоматической публикации NuGet-пакетов при создании GitHub Release, потому что:

1. release pipeline триггерится не на том событии;
2. версия публикации в CI берётся не из release tag;
3. pipeline в текущем виде не проходит полный test gate;
4. в коде и документации остался устаревший legacy-нейминг.

## 4. Цели дизайна
- Перевести публикацию на `GitHub Release -> published`.
- Сделать release tag единственным источником publish-версии в CI.
- Передавать version в packaging scripts явно, а не полагаться на скрытое чтение локального файла в release pipeline.
- Удалить устаревший legacy-нейминг из поддерживаемых файлов репозитория.
- Устранить текущий test blocker без лишнего рефакторинга sample topology.
- Оставить удобный `workflow_dispatch` для ручной валидации или ручной публикации.
- Сделать release checklist и docs однозначными и воспроизводимыми.

## 5. Non-Goals
- Не менять package IDs `AppAutomation.*`.
- Не перестраивать packaging architecture в reusable workflow или matrix build.
- Не добавлять отдельный publish flow для GitHub Packages.
- Не заниматься массовой чисткой analyzer warnings, если они не блокируют релизный pipeline.
- Не добавлять новые runtime adapters или новые publishable packages.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Release trigger model
Workflow `publish-packages.yml` переводится на два сценария запуска:

1. `release` с типом `published` как основной production trigger.
2. `workflow_dispatch` как ручной override для проверки и/или ручной публикации.

Норматив:
- автоматическая публикация в NuGet должна происходить только из `release.published`;
- `workflow_dispatch` остаётся для maintainers, но должен принимать явный `version` input;
- tag push сам по себе больше не считается release trigger.

### 6.2 Release version resolution
В CI version должна извлекаться из самого релиза.

Целевой контракт:
- production source of truth: `github.event.release.tag_name`;
- допустимый tag format: `appautomation-v<version>`;
- `<version>` после удаления префикса используется как package version без чтения локального version-файла;
- для `workflow_dispatch` version передаётся явно через input.

Предпочтительная реализация:
- добавить отдельный PowerShell script в `eng/`, который:
  - принимает release tag или explicit version;
  - валидирует формат;
  - возвращает нормализованную package version;
  - умеет работать и в GitHub Actions, и локально.

### 6.3 Packaging contract
Packaging scripts должны перейти на явную передачу версии.

Целевой контракт:
- `eng/pack.ps1` принимает `-Version`;
- `eng/publish-nuget.ps1` принимает `-Version` или `-PackagesPath`, вычисленный из этой версии;
- `eng/smoke-consumer.ps1` при необходимости принимает `-Version`;
- release workflow передаёт одну и ту же version во все шаги.

Норматив:
- release pipeline не должен вычислять publish version через локальный defaults-файл;
- локальные сценарии могут использовать fallback из `eng/Versions.props`, но только как developer convenience;
- artifact path должен оставаться совместимым с текущей структурой `artifacts/packages/<version>`.

### 6.4 Neutral naming cleanup
Устаревший legacy-нейминг удаляется из поддерживаемых файлов репозитория.

Минимально-достаточный scope cleanup:
- neutral property names в `eng/Versions.props`;
- neutral MSBuild properties в `Directory.Build.targets`;
- neutral variable/function naming в `eng/*.ps1`;
- neutral default assembly fallback в `src/AppAutomation.Authoring/UiControlSourceGenerator.cs`;
- нейтральные имена/пути/текст в docs и sample helper scripts;
- правка `ControlSupportMatrix.md`, чтобы там не оставалось legacy-терминов.

Предпочтительный вариант:
- использовать один нейтральный full-version property, например `AppAutomationVersion`, вместо split-модели `base version + suffix`;
- preview-версии представлять полным semver значением, например `2.1.0-preview.1`.

### 6.5 Pipeline structure
Целевой pipeline сохраняет текущий порядок проверок, но делает его release-safe:

1. checkout;
2. setup .NET по `global.json`;
3. resolve package version from release tag or manual input;
4. restore;
5. build;
6. test;
7. pack with explicit version;
8. smoke consumer against that version;
9. upload artifacts;
10. publish packages.

Норматив:
- publish step выполняется только после прохождения всех validation gates;
- publish step использует существующий `eng/publish-nuget.ps1`;
- pack step использует существующий `eng/pack.ps1`;
- upload step публикует артефакты по пути версии, извлечённой из релиза.

### 6.6 Test blocker fix
Чтобы pipeline реально работал, sample `DotnetDebugAppLaunchHost` должен перестать зависеть от старого имени solution-файла.

Минимально-достаточное исправление:
- заменить жёстко прошитый `DotnetDebug.sln` на актуальный `AppAutomation.sln`, либо
- сделать поиск solution root устойчивым к текущему layout без привязки к конкретному устаревшему имени.

Норматив:
- fix должен устранять падение существующих FlaUI tests;
- fix не должен ломать headless tests и локальный sample workflow;
- scope исправления ограничивается repo-local sample/test host logic.

### 6.7 Documentation update
Документация должна отражать новый release flow и нейтральный нейминг:

- `README.md`:
  - публикация происходит через GitHub Release;
  - tag должен быть `appautomation-v<version>`;
  - publish version берётся из release tag.
- `docs/appautomation/publishing.md`:
  - основной trigger = `release.published`;
  - `workflow_dispatch` остаётся как manual path;
  - release checklist явно включает tag format, tests и publish version flow.

### 6.8 Automated regression coverage
Поскольку меняется релизное поведение, нужно оставить автоматическую проверку новой логики.

Предпочтительный минимальный вариант:
- вынести version resolution в отдельный script/function;
- добавить automated tests или эквивалентную regression-проверку для:
  - корректного извлечения version из `appautomation-v<version>`;
  - отказа на невалидном tag;
  - manual path с explicit version;
  - отсутствия legacy-термина в поддерживаемых файлах.

Если unit-style tests для script окажутся непропорционально дорогими, допустим fallback:
- automated verification через targeted script invocation и `rg`-проверки в test/validation commands.

## 7. Бизнес-правила / Алгоритмы
- В `release.published` source of truth для publish version = release tag.
- Допустимый release tag format: `appautomation-v<semver>`.
- Для `workflow_dispatch` version передаётся explicit input-ом.
- Publish запрещён, если:
  - tag не начинается с `appautomation-v`;
  - после парсинга version пуста или невалидна;
  - full validation pipeline не зелёный;
  - отсутствует `NUGET_API_KEY`.
- Локальные manual scripts могут использовать fallback version из `eng/Versions.props`, если explicit version не передана.
- В поддерживаемых файлах репозитория не должно оставаться legacy-термина.

## 8. Точки интеграции и триггеры
- GitHub trigger:
  - `.github/workflows/publish-packages.yml`
- Local fallback version storage:
  - `eng/Versions.props`
- Packaging:
  - `eng/pack.ps1`
  - `Directory.Build.targets`
- Publishing:
  - `eng/publish-nuget.ps1`
- Smoke verification:
  - `eng/smoke-consumer.ps1`
- Sample runtime launch for FlaUI tests:
  - `sample/DotnetDebug.AppAutomation.TestHost/DotnetDebugAppLaunchHost.cs`
- Repo-wide naming cleanup:
  - `docs/*`
  - `sample/*`
  - `src/AppAutomation.Authoring/UiControlSourceGenerator.cs`
  - `ControlSupportMatrix.md`

## 9. Изменения модели данных / состояния
- Новых persisted данных не появляется.
- Меняется operational state release pipeline:
  - source of truth для publish version становится release tag;
  - появляется явная state machine: `release tag/input -> version resolve -> build/test/pack/smoke -> publish`.
- Локальный version default в `eng/Versions.props` упрощается и переименовывается в нейтральный формат.

## 10. Миграция / Rollout / Rollback
- Rollout:
  1. Исправить sample test blocker.
  2. Перевести scripts на explicit version contract.
  3. Перевести workflow на `release.published` и version-from-release.
  4. Удалить legacy-нейминг из затронутых файлов.
  5. Обновить docs.
  6. Прогнать локальные проверки.
- Backward compatibility:
  - `workflow_dispatch` остаётся доступным;
  - локальные `pack`/`publish` scripts продолжают работать;
  - package IDs и набор пакетов не меняются.
- Rollback:
  - можно вернуть workflow trigger на tag push;
  - можно временно вернуть version fallback из локального файла в CI;
  - naming cleanup rollback ограничен текстовыми/скриптовыми правками;
  - fix sample test host rollback не требует миграции данных.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - GitHub workflow запускается на `release.published`.
  - Manual dispatch остаётся доступным и принимает explicit version.
  - В release pipeline package version извлекается из release tag, а не из локального version-файла.
  - `dotnet test --solution AppAutomation.sln -c Release --no-build` больше не падает из-за поиска старого solution filename.
  - `eng/pack.ps1` и `eng/smoke-consumer.ps1` продолжают успешно работать.
  - В поддерживаемых файлах репозитория больше нет legacy-термина.
  - Docs описывают GitHub Release-based process и version-from-release flow.
- Какие тесты добавить/изменить:
  - regression coverage для version resolution logic;
  - при необходимости test на поиск solution root/актуальный solution filename;
  - текстовая regression-проверка на отсутствие legacy-термина.
- Команды для проверки:

```powershell
dotnet build AppAutomation.sln -c Release
dotnet test --solution AppAutomation.sln -c Release
pwsh -File eng/pack.ps1 -Configuration Release
pwsh -File eng/smoke-consumer.ps1 -Configuration Release -SkipPack
```

Дополнительные targeted checks:

```powershell
rg -n "release:|workflow_dispatch|publish|version" .github/workflows/publish-packages.yml
pwsh -File <new-version-script> -Tag appautomation-v2.1.0
rg -n "<legacy-term>" README.md docs eng src sample tests Directory.Build.targets ControlSupportMatrix.md
```

## 12. Риски и edge cases
- Release может быть создан с неправильным tag, поэтому валидация tag format обязательна.
- Prerelease tag (`appautomation-v2.1.0-preview.1`) должен поддерживаться без ручных исключений.
- `workflow_dispatch` без explicit version должен завершаться предсказуемой ошибкой или использовать строго определённый fallback по контракту.
- Rerun release workflow не должен публиковать несовместимую версию; `--skip-duplicate` остаётся защитой на publish step.
- Full UI test run может зависеть от runner environment, поэтому важно оставить локально воспроизводимый green path до publish.
- Если fix sample test host будет слишком жёстко завязан на текущее имя solution, проблема может повториться при следующем rename; предпочтительно сделать lookup менее хрупким.

## 13. План выполнения
1. Исправить sample `DotnetDebug` launch host так, чтобы full `dotnet test` больше не падал на поиске solution root.
2. Вынести version resolution из release tag/manual input в отдельную reusable script/function.
3. Перевести `eng/pack.ps1`, `eng/publish-nuget.ps1` и при необходимости `eng/smoke-consumer.ps1` на explicit version contract.
4. Обновить `publish-packages.yml` под `release.published` и version-from-release.
5. Удалить legacy-нейминг из затронутых файлов репозитория.
6. Обновить release docs и checklist.
7. Прогнать targeted checks, затем полный build/test/pack/smoke цикл.

## 14. Открытые вопросы
- Стоит ли дополнительно прикладывать `.nupkg/.snupkg` к GitHub Release как assets. Предварительно: не делать это обязательной частью данной задачи, если пользователь отдельно не запросит.
- Оставлять ли `eng/Versions.props` как local fallback store или полностью убрать его из репозитория. Предварительно: оставить как local fallback, но с нейтральным именованием и single-version model.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - изменения ограничены desktop/.NET solution и CI/release tooling вокруг него;
  - перед завершением будут запущены `dotnet build` и `dotnet test`;
  - будет сохранена стабильность sample automation flow вместо архитектурного переписывания runtime logic.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `.github/workflows/publish-packages.yml` | Перевод на `release.published`, manual `version` input, version-from-release | Автопубликация по GitHub Release |
| `Directory.Build.targets` | Переход на нейтральные MSBuild properties | Удалить legacy-нейминг и упростить версионирование |
| `eng/Versions.props` | Нейтральная single-version модель для local fallback | Убрать legacy-нейминг |
| `eng/pack.ps1` | Explicit `-Version`, отказ от release-time зависимости на local file | Версия в CI должна приходить из релиза |
| `eng/publish-nuget.ps1` | Explicit version/path contract | Версия и publish path должны быть синхронны с релизом |
| `eng/smoke-consumer.ps1` | При необходимости explicit version support и нейтральный нейминг | Согласовать smoke flow с новой схемой версий |
| `sample/DotnetDebug.AppAutomation.TestHost/DotnetDebugAppLaunchHost.cs` | Исправление поиска solution root | Убрать текущий блокер полного `dotnet test` |
| `docs/appautomation/publishing.md` | Обновление release flow и терминологии | Синхронизировать docs с реальным pipeline |
| `README.md` | Краткое описание нового GitHub Release flow | Синхронизировать верхнеуровневую документацию |
| `src/AppAutomation.Authoring/UiControlSourceGenerator.cs` | Удаление legacy fallback name | Полный naming cleanup |
| `sample/Verify-UiScenarioDiscoveryParity.ps1` | Удаление legacy path/reference | Полный naming cleanup |
| `ControlSupportMatrix.md` | Удаление legacy упоминаний | Полный naming cleanup |
| `tests/*` | Regression coverage для version resolution и naming cleanup | Зафиксировать новое поведение автоматическими проверками |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Release trigger | `push` tag `appautomation-v*` | `release.published` |
| Source of publish version | Локальный version-файл | Release tag / manual input |
| Version model | Split base/suffix с legacy-именами | Нейтральная explicit full-version model |
| Full test gate | Падает на старом solution filename | Проходит в актуальном repo layout |
| Docs и scripts | Содержат legacy-термин | Используют нейтральный нейминг |

## 18. Альтернативы и компромиссы
- Вариант: оставить version source в локальном файле и только валидировать его против release tag.
- Плюсы:
  - меньше правок в scripts;
  - проще backward compatibility.
- Минусы:
  - release остаётся не единственным источником publish version;
  - не соответствует прямому требованию пользователя;
  - оставляет часть legacy-концепции в release flow.
- Почему выбранное решение лучше в контексте этой задачи:
  - оно напрямую соответствует требованию публиковать по GitHub Release и брать version из самого релиза, одновременно закрывая cleanup устаревшего нейминга.

## 19. Результат прогона линтера
### 19.1 SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, дизайн-цели и non-goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Описаны release source of truth, explicit version contract, naming cleanup и test blocker fix |
| C. Безопасность изменений | 11-13 | PASS | Есть rollout, rollback и границы без изменения package IDs и публичного API |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria и команды проверки измеримы |
| E. Готовность к автономной реализации | 17-19 | PASS | План работ линейный, открытые вопросы неблокирующие |
| F. Соответствие профилю | 20 | PASS | Спека соответствует `dotnet-desktop-client` и .NET testing context |

Итог: `ГОТОВО`

### 19.2 SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | --- | --- |
| 1. Ясность цели и границ | 5 | Scope ограничен релизным pipeline, naming cleanup и выявленным test blocker |
| 2. Понимание текущего состояния | 5 | AS-IS опирается на фактический audit workflow/scripts и локальные прогоны |
| 3. Конкретность целевого дизайна | 5 | Зафиксированы release source of truth, explicit version flow и required file changes |
| 4. Безопасность (миграция, откат) | 5 | Изменения локализованы в release tooling, docs и sample infrastructure |
| 5. Тестируемость | 5 | Есть команды full verification и текстовые/скриптовые regression checks |
| 6. Готовность к автономной реализации | 5 | Блокеры локализованы, план работ последовательный и проверяемый |

Итоговый балл: `30 / 30`
Зона: `готово к автономному выполнению`

Слабые места:
- release-поведение нельзя полностью проверить без реального GitHub event, поэтому нужна тщательная локальная эквивалентная валидация;
- cleanup по всему репозиторию требует аккуратно не задеть package IDs и другие публичные идентификаторы;
- возможны дополнительные environment-specific ограничения у FlaUI tests на CI runner, даже после фикса поиска solution root.

## Approval
Ожидается фраза: **"Спеку подтверждаю"**
