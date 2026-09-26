# Скилл полного цикла UI-тестирования AppAutomation, релиз и установка

Expanded SPEC: форма и applicability — `quest-governance`; severity/disposition и review — `review-loops`.

## 0. Метаданные

- Тип (профиль): `delivery-task`; основной профиль `.NET Desktop Client` для Avalonia/.NET контекста, `testing-dotnet` для проверок. `ui-automation-testing` не активируется самой правкой инструкций: поведение UI и тестовые селекторы не меняются.
- Владелец: AppAutomation; исполнитель — Codex.
- Масштаб: medium; expanded из-за публичного скилла, CI workflow, GitHub Release, NuGet publication и системной установки.
- Целевое семейство / behavior baseline: Codex agents, которым поручено с нуля внедрить, развивать или использовать UI-тесты AppAutomation в произвольном совместимом Avalonia consumer-репозитории. Снимки Headless — часть полного цикла, а не единственная цель скилла.
- Поверхность: Codex desktop/CLI на локальной машине. Skill — локальная папка, не Responses API hosted skill и не plugin.
- Effective runtime: текущая Codex-сессия; для behavioral smoke одинаковые model/effort/sandbox в парных запусках `codex exec`, фактические значения записать в EXEC-evidence. Иных model pins скилл не задаёт.
- Eval baseline / evidence: до/после на representative задачах: (1) существующее Avalonia-приложение без UI-тестов с нестандартным расположением solution, (2) пользовательская фича с новым общим UI-сценарием и Headless/FlaUI запуском, (3) визуальная приёмка по реальному PNG. Сохраняются одинаковые промпты, model/effort/sandbox, ответы и scorecard в локальном некоммитимом каталоге артефактов; отдельно выполняется практический pilot в изолированном consumer-проекте без AppAutomation-тестов. Локальный `codex exec` с default `gpt-6-sol` отклонён сервисом; для пары выбрать одну реально доступную модель и одинаковые параметры, а при повторном отказе sandbox не заявлять AC5 выполненным.
- Целевой релиз / ветка: PR в `master`, затем `v1.8.0` от проверенного merge SHA. Тег `v1.8.0` поддерживается `eng/versioning.ps1`; версия NuGet — `1.8.0`.
- Ограничения: до exact approval менять только эту SPEC; не трогать unrelated `sample/DotnetDebug.Avalonia/emxLicense.cs`; секреты и содержимое локального профиля не включать в скилл/релиз.
- Связанные ссылки: [последний релиз 1.7.0](https://github.com/Kibnet/AppAutomation/releases/tag/1.7.0), [PR #32](https://github.com/Kibnet/AppAutomation/pull/32), [официальная структура Skills](https://developers.openai.com/plugins/build/skills).
- Instruction stack: central `AGENTS.md` → `routing-matrix`, `creator-vibe-lens`, `model-behavior-baseline`, `collaboration-baseline`, `tool-execution-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `.NET Desktop Client`, `spec-linter`, `spec-rubric`, `review-loops`, `github-delivery-policy`; локального `AGENTS.override.md` нет. Для формы скилла — `skill-creator`, для установки — `skill-installer`.

## 1. Overview / Цель

Исходное поручение: «Создай скилл для работы с AppAutomation, если такого ещё нет. Добавь его в репозиторий AppAutomation, добавь инструкцию по установке в ридми. Опубликуй релиз с ним и установи скилл в систему». Уточнение пользователя: скилл нужен **по всему AppAutomation**, чтобы агент мог с нуля правильно добавить UI-тесты в любой совместимый Avalonia-проект и использовать их для проверки функций. Headless-скриншоты служат анализу, визуальной приёмке и демонстрации результата пользователю.

Outcome contract:
- Success means: агент с установленным скиллом способен обследовать Avalonia-репозиторий без UI-тестов, выбрать совместимый путь внедрения, создать тестовую топологию, подключить AUT через TestHost, написать и запустить общий пользовательский сценарий в Headless (и FlaUI там, где доступен Windows desktop), использовать диагностические артефакты и проверенные PNG для визуальной приёмки/демонстрации; репозиторий, README, релиз и установленная копия скилла подтверждены evidence.
- Итоговый артефакт: `skills/appautomation/SKILL.md` и узкие references для внедрения/авторинга/визуальной проверки, README EN/RU, release ZIP, changelog и релизная версия.
- Stop rules: не публиковать при красном обязательном CI/проверке скилла, занятом теге, drift `master`, несоответствии архива исходнику или невозможности доказать корректную установку. Не объявлять публикацию NuGet успешной до зелёного release workflow и read-back; при ошибке остановиться и сообщить точную границу.

## 2. Текущее состояние (AS-IS)

- В репозитории и `$CODEX_HOME/skills` не найден `appautomation/SKILL.md`; отдельные скиллы для TUnit/скриншотов не дают полный AppAutomation adoption workflow.
- `docs/appautomation/quickstart.md`, `adoption-checklist.md`, `project-topology.md`, `selector-contract.md`, `advanced-integration.md` уже задают путь от подготовки детерминированного AUT до `Authoring`/`Headless`/`FlaUI`, nested solution, сложных контролов и Recorder. Skill должен уметь выбирать эти части под проект, а не перепечатывать README целиком.
- README двуязычный и описывает NuGet template/CLI, Authoring/Headless/FlaUI, но не установку агентского скилла. Технический документ `docs/appautomation/headless-screenshots.md` уже фиксирует Skia, `CaptureScreenshot`, failure artifacts и необходимость открыть PNG.
- Последний релиз `1.7.0`; после него в `master` только PR #32 со снимками Headless. `publish-packages.yml` при `release.published` запускает restore/build/test/pack/smoke, публикацию в NuGet.org/GitHub Packages и прикрепляет `.nupkg`/`.snupkg` к релизу.
- `eng/Versions.props` всё ещё содержит `1.5.9`, а `CHANGELOG.md` не разделяет `1.7.0` и Unreleased. Это релизный долг, который нужно устранить для достоверного `1.8.0`.
- В дереве есть unrelated untracked `sample/DotnetDebug.Avalonia/emxLicense.cs`; он исключён из diff/commit.

## 3. Проблема

Агенту недоступен обнаруживаемый, переносимый workflow для внедрения и повседневного применения AppAutomation в чужом Avalonia-проекте. Одних API-документов недостаточно: агент должен принять проектные решения, получить проходящий UI-сценарий и обосновать результат тестами и визуальными артефактами.

## 4. Цели дизайна

- Провести агента через весь жизненный цикл: обследование и готовность AUT → установка пакетов/шаблона/CLI → TestHost и селекторы → page objects и общие сценарии → Headless/FlaUI → диагностика, визуальная приёмка и пользовательский отчёт.
- Адаптироваться к пустому/частично подключённому проекту, nested solution, корпоративному NuGet feed, существующим тестам и составным контролам без дублирования структуры или исходников фреймворка.
- Сохранить единый `Authoring` для сценариев и два runtime adapter; Recorder использовать как опциональный способ подготовки кода с review полученных partials.
- Давать проверяемый путь снимка с указанием, что PNG реально открыт и изучен, а затем приложен/показан пользователю в доступной форме.
- Сделать релизный артефакт воспроизводимым через CI, установку — однозначной для Windows/PowerShell.
- Не менять публичный API и поведение фреймворка.

## 5. Non-Goals

- Новый screenshot API, плагин/MCP, отдельный генератор тестов или публикация skill в сторонний marketplace.
- Обещание безусловно автоматизировать любой Avalonia-проект: для `net<8`, недетерминированного AUT, отсутствующего окна/desktop-сеанса или закрытого feed скилл должен выявить и объяснить конкретную предпосылку.
- Рассылка сообщений, изменение consumer-репозиториев, установка NuGet-пакетов на машине пользователя.
- Ослабление существующего release workflow ради отдельного «только skill» релиза.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `skills/appautomation/SKILL.md`: точный триггер, маршрут по состоянию consumer-проекта и типу задачи, неизменные принципы топологии/доказательств, ссылки на узкие references.
- `skills/appautomation/references/adoption-and-authoring.md`: внедрение с нуля/достройка существующей структуры, совместимость, deterministic AUT, template/CLI/doctor, TestHost, селекторы, Page/сценарии, опциональный Recorder/адаптеры, команды Headless/FlaUI и типовые сбои.
- `skills/appautomation/references/visual-evidence.md`: практический Headless PNG, автоматические снимки при ошибках, критерии визуальной приёмки, просмотр/сравнение нужного состояния и способ показать пользователю artifact. Факты сверить с `docs/appautomation/headless-screenshots.md`, не копировать всё руководство.
- `README.md`: EN/RU блок «Codex skill» рядом с Fast Path/«Быстрый старт»; версия, ссылка на релиз, PowerShell установка/проверка, обновление, отличие скилла от .NET tool.
- `eng/pack-skill.ps1`, `.github/workflows/pr-validation.yml` и `.github/workflows/publish-packages.yml`: локально, в PR и в release CI собрать/проверить `appautomation-skill.zip` с корнем `appautomation/`, прикрепить к GitHub Release из того же checkout, что пакеты; для ручного `workflow_dispatch` без publish допустима CI-валидация архива без release upload.
- `eng/Versions.props`, `CHANGELOG.md`, `docs/appautomation/publishing.md`: согласовать локальную версию, release delta и реальный перечень assets/тегов.

### 6.2 Детальный дизайн

- Скилл называется `appautomation`, frontmatter `name`/`description` прямо обещает внедрять и применять AppAutomation UI-тесты в Avalonia-проектах, включая проверку визуальных фич; отличается от общих Playwright/desktop-screenshot задач. Автоматический выбор разрешён; `$appautomation` — явный вызов.
- В начале агент инспектирует TFM/SDK, структуру solution/desktop app, существующие тесты и `PackageReference`, сценарий запуска и данные, ОС/доступность FlaUI. Он не запускает `dotnet new` поверх существующей структуры и не обещает корректность для неподдерживаемого TFM; определяет один критичный пользовательский smoke path и нужные предпосылки.
- В режиме внедрения с нуля агент берёт совместимые версии из feed, устанавливает `AppAutomation.Templates` и локальный `AppAutomation.Tooling`, создаёт canonical topology `Authoring`/`Headless`/`FlaUI`/`TestHost`, запускает `doctor`, заменяет placeholders реальными типом `App`, созданием `Window`, настройками/тестовыми данными и путями AUT (включая nested solution). Репозиторий-потребитель остаётся NuGet-first.
- В режиме авторинга агент ставит стабильные `AutomationId` на элементы критичного пути, `AutomationProperties.Name` — при text-name assertions, описывает Page и общий пользовательский сценарий в `Authoring`; Headless/FlaUI — тонкие wrappers. Для поиска, фильтров, таблиц, popup, menu, date/time и других сложных элементов он сначала проверяет README, образцы и `component-coverage-gaps.md`, затем выбирает подходящий встроенный provider-neutral контракт/adapters либо честно отмечает пробел. Recorder — опциональный помощник, чьи сгенерированные selectors/scenarios надо просмотреть. Тест проверяет видимый результат, не только внутренний счётчик.
- В режиме выполнения агент стабилизирует Headless, затем запускает FlaUI при поддерживаемом Windows desktop; знает TUnit/MTP `--treenode-filter`, собирает при падении failure context и не объявляет успех при красном тесте. Для UI-фичи использует эти тесты как цикл проверки после изменений и соотносит assertions с исходным пользовательским сценарием.
- В режиме визуальной проверки агент проверяет `.UseSkia()` + `UseHeadlessDrawing = false` до запуска сессии, показанное окно и UI dispatcher; после ожидания бизнес-состояния вызывает `Session.Inner.CaptureScreenshot(...)`, сохраняет уникальный PNG, **открывает** его и сравнивает конкретные видимые признаки с требованием/референсом. При падении различает `screenshot` и `screenshot-unavailable`, проверяет TUnit attachment и CI artifact upload. В отчёте даёт доступную пользователю ссылку/вложение на кадр и пояснение, что именно он подтверждает; локальный путь сам по себе не считается демонстрацией, если пользователь не может открыть файл. Headless PNG не включает system chrome/native dialogs.
- Скилл переносим: не ссылается на абсолютные пути локального checkout, memory, секреты или внутренние Codex script paths. Пакетные API и ограничения сверяются с установленной версией и соответствующими docs/образцами; ссылки в references ведут к публичным материалам репозитория, а критичный базовый workflow доступен и офлайн.
- Установка из `appautomation-skill.zip` в `$CODEX_HOME/skills` (fallback `$HOME/.codex/skills`) документируется с проверкой существующей папки и версии. Обновление существующей установки выполняется только после проверки её происхождения/локальных изменений; автоматическое перетирание не требуется. Локальная установка текущего релиза через `skill-installer` с `--repo Kibnet/AppAutomation --path skills/appautomation --ref v1.8.0` и read-back `SKILL.md`/hash.
- Release notes на русском: skill и Headless PNG как фактические additions со ссылкой на PR #32, без обещаний о desktop screenshots. Пакеты версии `1.8.0` — неизбежный эффект `release.published`; до публикации повторная проверка `master`, SHA, tag, asset names, CI и версии. Не использовать auto-generated notes без сверки.
- Visual planning artifact: не применимо — UI приложения не меняется; README copy/skill текст проверяются в Markdown preview.
- UI test video evidence: не применимо к текущему изменению инструкций и release packaging; существующий PR #32 отдельно проверял UI screenshot behavior, новый UI flow не вводится.
- Производительность: не применимо, runtime код не меняется; ZIP мал и создаётся после обычной упаковки.
- Ошибки: неготовый AUT/нет тестовых данных, неподдерживаемый TFM, Headless без пиксельного рендера, FlaUI без Windows desktop, существующий skill/destination, отсутствующий ZIP или несовпавший hash, красная CI, занятый тег/версия, release workflow не стартовал, NuGet публикация/пропагация не подтверждена — остановить соответствующий этап, оставить точные evidence и не выдавать частичный результат за полный.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| В Avalonia-проекте нет UI-тестов | `$appautomation` или просьба «добавь UI-тесты с нуля» | обследование prerequisites, canonical topology, TestHost, первый общий Page/scenario, зелёный Headless и применимый FlaUI | парный behavioral smoke + практический isolated consumer pilot + validator | AC1, AC5, AC6 |
| Тесты уже есть; агент проверяет новую UI-фичу | запрос реализовать/проверить пользовательский flow | сценарий в `Authoring`, устойчивые селекторы, результат Headless/FlaUI и failure diagnostics | behavioral smoke + pilot + test logs | AC1, AC5, AC6 |
| Нужна визуальная приёмка или показ пользователю | запрос снять и показать состояние после действия или на падении | PNG нужного текущего состояния, реально просмотренный кадр, доступная ссылка/вложение с пояснением либо точная причина недоступности | парный smoke, actual PNG, сверка API/docs, evidence link | AC1, AC5, AC6 |
| Пользователь устанавливает скилл | команды из README или локальный `skill-installer` с тегом | `skills/appautomation/SKILL.md` на целевом пути, доступен в следующем ходе Codex | path/hash/read-back, fresh-session discovery | AC2, AC4 |
| Пользователь открывает релиз | GitHub `v1.8.0` | curated notes, ZIP и пакеты `1.8.0`, зелёный release workflow | release URL, asset list/digest, workflow URL/feed read-back | AC3 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Skill absent | install from published tag | folder exists with validated SKILL | missing archive/tag → stop, no false installed claim | installation after release |
| Skill present | README update command | inspect current version/local edits before replacement | modified/unknown origin → stop and preserve | no blind overwrite |
| Release absent | publish `v1.8.0` | release workflow publishes packages and ZIP | occupied tag/version or master drift → stop before publish | PR merged first |
| Release published | workflow still running | wait for final success before reporting completion | failure → investigate/recovery path per publishing.md | GitHub release itself irreversible public side effect |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Создать скилл? | user | да, существующий AppAutomation skill не найден | 0.95 | дублирование при скрытом внешнем skill | Нет |
| Название и scope | user/agent | `appautomation`, полный жизненный цикл Avalonia consumer UI-тестов; Headless PNG — часть визуальной приёмки; не skill для maintenance/release самого фреймворка | 0.99 | слишком широкая автоактивация при неточном description | Нет: уточнено пользователем |
| Публикация | user/agent | полный очередной AppAutomation release `v1.8.0`; он запускает NuGet package publish | 0.9 | внешняя публикация 10 пакетов; это явно указано в SPEC | Нет: пользователь запросил релиз, существующий repo workflow задаёт побочный эффект |
| ZIP asset | agent | CI собирает и прикрепляет стабильное имя `appautomation-skill.zip` | 0.9 | ошибка упаковки | Нет |
| Системная установка | user/agent | текущий пользовательский `$CODEX_HOME/skills/appautomation`, из опубликованного тега | 0.95 | конфликт существующего каталога | Нет |
| PR/merge | agent | PR, зелёный CI, merge в `master`, затем release от merge SHA | 0.9 | публикация непроверенного SHA | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Skill discovery | Codex skill folder + `SKILL.md` frontmatter | новая папка `appautomation` | не влияет на NuGet API | quick validator, fresh-session discovery |
| Consumer adoption | `quickstart.md`, `adoption-checklist.md`, `project-topology.md`, `selector-contract.md`, templates и actual C# | агентский маршрут от нуля до passing UI tests | `1.8.0` включает PR #32; NuGet-first и текущая topology сохраняются | inspect docs/template, isolated pilot, behavioral smoke |
| Headless API | `docs/appautomation/headless-screenshots.md`, actual C# | агентская визуальная проверка/демонстрация | `1.8.0` включает PR #32 | inspect code/docs, actual PNG |
| Release version | `eng/Versions.props`, tag parser | `1.8.0`, tag `v1.8.0` | existing bare tags remain valid | resolve script, CI pack |
| ZIP / release | `publish-packages.yml` | доп. asset; NuGet flow сохраняется | существующие assets неизменны | archive inspect, release read-back |
| Local install | `$CODEX_HOME/skills` or `$HOME/.codex/skills` | добавить `appautomation` | не трогать иные skills | hash + fresh session |

## 7. Бизнес-правила / Алгоритмы

- ZIP содержит ровно папку `appautomation/` с `SKILL.md` и выбранными references, без repo secrets, build outputs и абсолютных путей.
- Версия skill соответствует release tag; release-tag docs и NuGet package version не смешиваются с локально плавающей latest-версией.
- «Тесты подключены» доказано не наличием папок, а проходящим пользовательским сценарием после реального TestHost bootstrap; `doctor` не заменяет запуск теста.
- Screenshot доказан только при сохранённом PNG, просмотренном правильном состоянии и доступной пользователю демонстрации/ссылке, если её запросили; успешный assertion или строка локального пути без просмотра недостаточны.
- GitHub Release нельзя считать завершённым, пока обязательный публикационный workflow не завершён green и обязательные assets не прочитаны обратно.

## 8. Точки интеграции и триггеры

- Codex discovery читает `name`/`description` из установленного `SKILL.md` при новой сессии. Триггеры: начать UI-тестирование Avalonia, дописать Page/scenario, провести regression, использовать Recorder/адаптеры, получить/показать Headless PNG.
- README ведёт к ZIP release asset и варианту установки из GitHub tag; публикационный workflow собирает ZIP из checkout тега.
- `release.published` запускает существующий workflow; новый шаг attach идёт после pack и проверок.

## 9. Изменения модели данных / состояния

- Runtime/model данных AppAutomation не меняется.
- Персистентное состояние: git-файлы skill/docs/workflow/version/changelog, GitHub tag/release/assets, локальная папка установленного skill.

## 10. Миграция / Rollout / Rollback

- До merge: проверить skill, README, CI, archive и release preflight. После merge: повторить SHA/занятость тега, опубликовать release, дождаться workflow, затем установить skill из точно этого тега.
- При ошибке до публикации исправить PR/CI. После публикации не подменять тег/пакеты; исправление — отдельный patch release или repo-approved recovery для того же release без повторного выпуска уже опубликованных версий.
- Локальный rollback: удалить только установленную `appautomation` папку после проверки resolved path и происхождения, либо восстановить сохранённую предыдущую версию. Другие skills не затрагивать.
- Старые consumer-проекты работают без миграции; skill не меняет их зависимости автоматически. В конкретном consumer-проекте агент сначала адаптируется к существующим тестам и структуре, затем меняет только необходимое для порученного тестового flow.

## 11. Тестирование и критерии приёмки

- AC1: skill валиден и даёт агенту полный, корректный маршрут внедрения/применения AppAutomation: prereqs, compatibility, package/template/CLI/doctor, TestHost, selectors, Page/shared scenarios, Headless/FlaUI, Recorder/complex controls при применимости, диагностика, визуальные доказательства и пользовательский отчёт.
- AC2: README EN/RU содержит воспроизводимую установку/проверку/обновление и различает Codex skill, CLI и NuGet; команды реально опробованы в безопасном временном каталоге и на целевом пути.
- AC3: `v1.8.0` от проверенного `master` опубликован с curated notes, ZIP и всеми обычными package assets; release workflow green, package version и опубликованные пакеты подтверждены read-back.
- AC4: установленный skill совпадает с релизным по содержимому/hash и обнаруживается новой Codex-сессией; до этого не говорить «активен».
- AC5: парный before/after behavioral smoke с одинаковым доступным runtime на трёх задачах демонстрирует, что skill ведёт от пустого Avalonia-проекта к тестам, помогает использовать тесты для новой фичи и требует реально просмотренный PNG для визуальной приёмки/демонстрации; static validator сам по себе недостаточен.
- AC6: в изолированном consumer pilot без исходных AppAutomation-тестов агент по skill создаёт или адаптирует topology, завершает TestHost, добавляет один содержательный пользовательский сценарий, запускает Headless до green и сохраняет/просматривает PNG нужного состояния. FlaUI проверяется на Windows desktop при доступности; при невозможности — объективная причина и отдельная граница доказательства. Результат содержит user-visible test/artifact evidence, а не только созданные файлы.
- Обязательные repo/release gates: `quick_validate.py`, ZIP structural/read-back, PowerShell/YAML check для workflow, `dotnet build AppAutomation.sln -c Release`, `dotnet test --solution AppAutomation.sln -c Release` (TUnit/MTP), `eng/pack.ps1` и `eng/smoke-consumer.ps1` для `1.8.0`, PR CI, release workflow и после публикации feed/asset read-back. Targeted check до full suite; сохранять лог/progress для долгих команд. Не добавлять тесты, зеркалирующие README текст.
- Characterization: до изменения skill отсутствует; текущие docs/template и screenshot API уже покрыты существующими пакетными/consumer checks и PR #32. Новых C# тестов в framework не требуется при неизменном API, но practical consumer pilot обязателен.
- Visual acceptance: Markdown preview читаем; UI приложения и video не применимы. Для PNG smoke смотреть реальный файл, а не текст вывода.
- Stop rules: ошибки валидатора/парного smoke/CI блокируют публикацию; отсутствие свежей сессии блокирует claim AC4, но не отменяет проверку установленного пути. Повторять проверки только после исправления или нового конкретного риска.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | `quick_validate.py`, paired prompts | docs/template/API cross-check, output review | validator output + scorecard | — |
| AC2 | PowerShell command dry-run in temp home | README EN/RU review | terminal log + paths | — |
| AC3 | build/test/pack/smoke + CI | tag/SHA/notes/assets/feed read-back | CI/release URLs, digests | — |
| AC4 | file hash comparison | fresh Codex session discovers skill | hash log + session output | — |
| AC5 | paired `codex exec` with same model/effort/sandbox | compare adoption, feature test and visual evidence decisions | prompts/outputs/scorecard | — |
| AC6 | isolated consumer build/headless test and screenshot | open PNG; FlaUI when desktop available; check user-facing evidence link | consumer workspace/test logs/PNG | — |

## 12. Риски и edge cases

- Release публикует NuGet-пакеты: это не «только документация»; failure/duplicate package version требует остановки и честного статуса.
- Skill может привлекать нерелевантные UI requests: frontmatter должен быть узким, но явно включать полное AppAutomation adoption/testing.
- Шаблон может быть создан, но TestHost останется с placeholder, `doctor` может пройти non-strict, а тест не заработает: AC6 требует настоящий bootstrap и зелёный сценарий.
- «Любой Avalonia-проект» включает nested solution/частичные тесты/внутренний feed и нестандартные контролы; skill должен инспектировать состояние и применять соответствующий путь, а unsupported prerequisites сообщать без ложного green.
- Скриншот может быть пустым из-за `UseHeadlessDrawing`, невидимого окна, неверного dispatcher или неподходящего состояния; инструкция должна вести к проверке, а не к ложному success.
- Распакованный archive/установка могут затереть локально изменённый skill; README и installer должны проверять существующий каталог.
- `CHANGELOG.md` содержит stale Unreleased для `1.7.0`; разделить аккуратно, не приписать старые изменения новому релизу.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Скилл учит только скриншотам, а UI-тесты с нуля не подключает» | явное уточнение пользователя | полный adoption/authoring/execution маршрут, практический consumer pilot без тестов и AC1/AC6 | mitigated |
| «Тесты существуют только как scaffold» | шаблон содержит placeholders | завершённый TestHost и passing user flow в AC6 | mitigated |
| «PNG нельзя использовать для приёмки или показать мне» | локальный путь не доказывает видимое состояние и доступность | открыть кадр, проверить признаки, дать доступный artifact link/вложение с пояснением | mitigated |
| «Релиз есть, но скилла среди assets нет» | GitHub tag сам по себе плохо обнаружим | ZIP в CI release asset + read-back | mitigated |
| «Установили не опубликованную версию» | локальный checkout может отличаться | install pinned `v1.8.0`, compare hash | mitigated |
| «Опубликованы пакеты без проверки» | release workflow имеет side effects | предварительные gates, green CI, post-release read-back | mitigated |

### Rework Prevention Checklist

- Сценарии пользователя, evidence, решения и возможные замечания перечислены выше.
- Роли и AC mapping проверяются в post-SPEC ниже.
- EXEC закрывает исходное поручение и уточнение только при наличии repo skill полного цикла, практической проверки от пустого проекта до UI-теста/PNG, README, опубликованного релиза и установленной копии.

## 13. План выполнения

1. После exact approval сделать ветку, написать skill и README EN/RU, обновить workflow/archive/docs/changelog/version; сохранить unrelated file нетронутым.
2. Запустить validator, paired behavioral smoke и practical consumer pilot без исходных UI-тестов; проверить actual Headless PNG и при доступности FlaUI. Выполнить archive/install dry-run; исправить реальные ошибки.
3. Выполнить build/test/pack/smoke, PR self-review и CI; merge после green.
4. Перепроверить `master`/tag/version, опубликовать curated release, дождаться release workflow, проверить assets/feed.
5. Установить из опубликованного тега, проверить hash и fresh-session discovery; записать post-EXEC review/evidence в SPEC, отчитаться ссылками.

## 14. Открытые вопросы

Нет блокирующих. Пользователь уточнил scope до полного AppAutomation adoption/testing, а не отдельного скриншотного skill. Релиз и системная установка уже поручены; точный тип релиза выведен из существующего repo workflow и явно раскрыт в этой SPEC.

## 15. Соответствие профилю

- `.NET Desktop Client`: API/UI не меняются; release build/test обязательны. Инструкция сохраняет `AutomationId` и разделение Headless/FlaUI.
- `testing-dotnet`: TUnit/MTP runner, full release suite, build, pack и smoke указаны; фильтр VSTest не используется.
- `skill-creator`: краткий frontmatter, переносимость, validator и behavioral smoke.
- `github-delivery-policy`: PR, review, tag `v1.8.0`, curated notes от diff и changelog.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `skills/appautomation/SKILL.md` | новый skill | обнаружение и маршрут AppAutomation |
| `skills/appautomation/references/adoption-and-authoring.md` | внедрение с нуля и авторинг/запуск тестов | полный consumer workflow вне entrypoint |
| `skills/appautomation/references/visual-evidence.md` | снимки, визуальная приёмка и показ пользователю | подробности evidence вне entrypoint |
| `README.md` | EN/RU установка и проверка | доступный onboarding |
| `eng/pack-skill.ps1` | валидация/упаковка skill ZIP | одинаковый локальный и CI release asset |
| `.github/workflows/pr-validation.yml` | проверка ZIP в PR | обнаружить ошибку архива до merge/release |
| `.github/workflows/publish-packages.yml` | build/upload ZIP | release asset скилла |
| `docs/appautomation/publishing.md` | asset и актуальный tag contract | документация release flow |
| `eng/Versions.props` | `1.8.0` | локальная версия релиза |
| `CHANGELOG.md` | `1.7.0`/`1.8.0` разделы | корректные release notes |
| эта SPEC | фазы, review и evidence | QUEST trace |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Agent guidance | нет AppAutomation skill | discoverable `$appautomation` для полного UI-testing lifecycle |
| Consumer adoption | отдельные docs, не загружаемые как skill | агент ведёт от обследования до passing UI test |
| Screenshot route | только docs | skill включает просмотр PNG, визуальную приёмку и показ пользователю |
| Installation | отсутствует | README EN/RU + ZIP + локальный installed path |
| Release | `1.7.0`, packages only | `v1.8.0`, skill ZIP + packages |

## 18. Альтернативы и компромиссы

- Только README: дешевле, но агент не обнаружит инструкцию как skill.
- Только git tag без ZIP: технически исходник есть, но пользователь не видит отдельного скачиваемого артефакта; выбран CI ZIP.
- Skill-only release без пакетов: потребовал бы менять канонический release trigger и нарушил ожидания существующего pipeline; выбран обычный релиз `1.8.0` с раскрытой NuGet публикацией.
- Полное копирование docs в skill: риск рассинхронизации; выбран короткий entrypoint с двумя узкими references и ссылками на API-документацию.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | цель, AS-IS, scope, non-goals и наблюдаемые сценарии |
| B. Качество дизайна | 6-10 | PASS | responsibilities, release/install triggers, failure states; runtime perf N/A |
| C. Безопасность изменений | 11-13 | PASS | side effects, совместимость, rollback и conflict handling |
| D. Проверяемость | 14-16 | PASS | AC→evidence, команды и stop rules |
| E. Готовность к автономной реализации | 17-19 | PASS | фазы, решения, профиль, нет блокирующих вопросов |
| F. Соответствие профилю | 20 | PASS | `.NET Desktop Client`, TUnit/release gates |

Итог: ГОТОВО. Детальная оценка критериев 1–20: `1–5 PASS; 6–10 PASS; 11–13 PASS; 14–16 PASS; 17–19 PASS; 20 PASS`; неприменимость runtime performance и UI video обоснована выше.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | точный user outcome, non-goals, side effects |
| 2. Понимание текущего состояния | 5 | проверены skill inventory, README, docs, workflow, versions/tags |
| 3. Конкретность целевого дизайна | 5 | files, skill contract, ZIP/install/release contract |
| 4. Безопасность (миграция, откат) | 5 | release preflight/stop, install conflict, rollback |
| 5. Тестируемость | 5 | AC mapping, behavioral smoke, CI/read-back |
| 6. Готовность к автономной реализации | 5 | решения и последовательность без blocker |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению после exact approval.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Получит ли пользователь usable full-cycle skill, release и install, а не только screenshot-инструкцию? | PASS | нет |
| UX / designer | applicable к README/skill copy и visual evidence | Понятны ли first use, update и как пользователь увидит/оценит кадр? | PASS | проверить preview, EN/RU parity и доступность artifact в EXEC |
| Tester / validation | applicable | Проверяются ли внедрение с нуля, настоящий user flow, негативные случаи и смысловые решения агента? | PASS | парный smoke и практический consumer pilot обязательны |
| Developer / architect | applicable | Не смешаны ли skill и NuGet API, не дублируются ли docs, соблюдена ли topology в чужом проекте? | PASS | держать entrypoint компактным и проверить TestHost в pilot |
| Delivery / operations / security | applicable | Как предотвращены непроверенные NuGet publish и неправильная локальная установка? | PASS | preflight, CI, tag/hash/read-back обязательны |

### Post-SPEC Review

- Статус / stop decision: PASS; можно запрашивать exact approval, но EXEC ещё не разрешён.
- Scope reviewed: эта SPEC после уточнения пользователя; central stack из §0, `skill-creator`, `skill-installer`; `README.md`, `CHANGELOG.md`, `eng/Versions.props`, `eng/versioning.ps1`, `publish-packages.yml`, `docs/appautomation/{quickstart,adoption-checklist,project-topology,selector-contract,advanced-integration,compatibility,headless-screenshots,publishing}.md`, `eng/smoke-consumer.ps1`; `git status`, `git log 1.7.0..HEAD`, GitHub release/workflow metadata.
- Scope/Evidence pass: нет существующего AppAutomation skill; docs подтверждают полный onboarding путь и ограничения совместимости; PR #32 — единственный post-1.7.0 code delta; текущий tag parser принимает `v`.
- Contract pass: исходное поручение и уточнение закрыты AC1–AC6; скилл ведёт от проекта без тестов до passing scenario и visual evidence; non-goals соблюдены.
- Adversarial risk pass: проверены риск узкого screenshot-only scope, ложного успеха scaffold/doctor, неподдерживаемого Avalonia TFM, недоступного FlaUI, недоступного PNG пользователю, скрытая NuGet публикация, stale changelog/version, конфликт установки, tag/master drift.
- Role-Based pass: таблица выше; все применимые роли PASS по плану проверки.
- Fix and re-review: после пользовательского уточнения screenshot-only центр заменён full-cycle adoption/authoring/execution/visual workflow; добавлены два references, три behavioral scenarios и практический pilot AC6. Повторно сверены §1, §3–§12, AC matrix, роли и границы. Ранее замысел «только tag с исходником» усилен CI ZIP asset и read-back.
- Evidence inspected: файловые команды и `gh release view 1.7.0`, `gh run list --workflow publish-packages.yml`, `codex exec --help`; фактическая реализация и CI будут проверены только в EXEC.
- Depth checklist: scope/unrelated file — указан; AC/evidence — матрица и pilot; unsupported claims — release и consumer adoption не названы выполненными; regression — NuGet flow сохранён; docs/changelog — planned; hidden contract — публикация пакетов раскрыта; manual-review challenge — проверить, что skill действительно создаёт проходящий сценарий в незнакомом проекте и показывает доступный кадр, а архив/installed skill совпадают с release SHA.
- No-findings justification: после full-cycle поправки и ZIP/read-back в плане не осталось BLOCKER/HIGH по спецификации; обязательный exact approval — фазовый gate, а не дефект SPEC.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- |
| MEDIUM | delivery | Один тег без downloadable skill asset плохо проверяем пользователем | добавить CI ZIP и read-back | fixed in SPEC |
| MEDIUM | release | В changelog/version остался долг от 1.7.0 | включить точное разделение/обновление в EXEC | fixed in SPEC plan |
| HIGH | scope | Предыдущая редакция могла дать screenshot-first skill без полного внедрения UI-тестов | расширить outcome, режимы, матрицу AC и practical pilot | fixed in SPEC after user clarification |

- Fixed before continuing: три находки внесены в дизайн и AC; affected adoption/release/install passes повторно просмотрены.
- Needs human: только фраза exact approval по `quest-mode`; дополнительных открытых решений нет.
- Residual risks / follow-ups: внешняя публикация пакетов зависит от GitHub/NuGet runtime; её можно оценить только после EXEC.
- Independent reviewer: отдельный `codex exec -m gpt-5.5 -s read-only --ephemeral` подтвердил effective `sandbox: read-only`, но не смог прочитать ни один файл: Windows helper отказал с `helper_sandbox_lock_failed: ... SetNamedSecurityInfoW sandbox dir failed: 5`. Это **не** independent PASS. Выполнен adversarial self-review fallback по реально прочитанным файлам и матрицам; остаточный риск — нет внешней проверки полноты SPEC. В EXEC повторить independent pass, если read-only sandbox заработает, либо снова зафиксировать fallback.
- Adversarial fallback (отдельный проход): попытка опровергнуть план показала критические точки — snapshot skill вместо полного workflow, созданный без реального запуска scaffold, недоступный пользователю PNG, `release.published` публикует NuGet, `Versions.props`/changelog отстают, ZIP может иметь неверный корень, установщик может затереть локальные правки. Для каждой есть явный stop/check в §6–§12; отдельного незакрытого BLOCKER/HIGH в SPEC не найдено.

### Post-EXEC Review

Локальный implementation gate: **PASS**; delivery gate (`PR`/CI/merge/release/install): **PENDING** до read-back. Review охватывает утверждённую SPEC, `git status --short`, полный diff перечисленных в §16 файлов, skill contents, workflow triggers/conditions, README EN/RU, changelog и build/test/package evidence. Unrelated `sample/DotnetDebug.Avalonia/emxLicense.cs` остался untracked и не включается в commit.

- Scope/contract: скилл ведёт через подготовку AUT, шаблон/CLI, четыре роли проектов, настоящий TestHost, общий Page/scenario, Headless/FlaUI, диагностику и просмотренный PNG; screenshot-only отклонение устранено. Публичный API фреймворка и runtime исходники не менялись. `v1.8.0`/ZIP остаются обязанностью delivery gate.
- Automated checks: `quick_validate.py` — `Skill is valid!`; `eng/pack-skill.ps1` — ZIP с тремя ожидаемыми файлами и корнем `appautomation/`; ZIP был распакован в отдельный временный skills root с совпавшим hash `SKILL.md`. `resolve-package-version.ps1 -Tag v1.8.0` вернул `1.8.0`; оба YAML workflow разобраны парсером; `git diff --check` чист. `dotnet build AppAutomation.sln -c Release` — 0 ошибок; `dotnet test --solution AppAutomation.sln -c Release --no-build` — 640 total, 599 passed, 41 skipped, 0 failed (FlaUI desktop недоступен); `eng/pack.ps1 -Configuration Release -Version 1.8.0` — 10 nupkg/9 snupkg; `eng/smoke-consumer.ps1 -Configuration Release -Version 1.8.0 -SkipPack` — build, Headless 1/1, PNG и doctor без ошибок/предупреждений.
- Behavioral comparison: одинаковые `gpt-5.5`, low effort, `danger-full-access` и read-only prompts (без файловых изменений) в трёх парах; outputs в локальном `appautomation-skill-eval-20260927`. Adoption baseline предлагал VSTest `--filter`/один generic test project, candidate — четыре роли, реальный TestHost и TUnit `--treenode-filter`; feature baseline дублировал runtime methods, candidate — shared scenario/adapters/diagnostics; visual baseline предлагал raw `CaptureRenderedFrame`, candidate — `Session.Inner.CaptureScreenshot`, Skia, просмотр PNG и пользовательский artifact. Candidate adoption пропустил установку template в списке команд; entrypoint исправлен и перевалидирован. Read-only sandbox запуск не состоялся из-за `helper_sandbox_lock_failed`, поэтому ни один запуск не назывался изолированным sandbox review.
- Practical consumer: отдельный Avalonia `net8.0` project с вложенным `src/PilotApp` и без UI-тестов. Агент по скиллу создал четыре проекта, подключил реальный `App`/`MainWindow`, shared сценарий `Ready` → click `Activate` → `Activated`, добавил screenshot test и solution references. Первый Headless завис: AUT использовал Avalonia 11, а пакеты 1.8.0 требуют Avalonia 12. В reference добавлены проверка major-версии до генерации и включение проектов в solution. После явной миграции временного AUT на Avalonia 12.0.4, удаления недоступного `Avalonia.Diagnostics 12` и закрепления SDK 10.0.202: solution build green; Headless **2/2 passed**, `doctor --strict` 0 error/0 warning. Реальный PNG `artifacts/skill-pilot-activated.png` открыт: в окне видны `Activated` и кнопка `Activate`; SHA256 `033DB5EAB20EB1381282300F0C6E91B119631E9B27B0822B7395A0FCA088CC86`. Миграцию AUT сделал исполнитель пилота после выявления условия, поэтому это evidence поддерживаемой конфигурации, а не claim совместимости 1.8.0 с Avalonia 11.
- Adversarial/role pass: business/UX — первый пользовательский flow и видимый кадр подтверждены; developer/test — packaged API и TUnit route сверены, mismatch выявлен и описан; delivery/security — workflow публикует обычные NuGet пакеты, asset upload стоит после обязательных checks, token и секреты не копируются в skill. Остатки: FlaUI не проходил без интерактивного desktop; внешний CI/релиз/установка ещё не подтверждены. Ни один из этих pending результатов не объявляется PASS.
- User-observable completion gate: AC1, локальная часть AC2, AC5 и AC6 подтверждены; AC3/AC4 и release часть AC2 ожидают PR/CI/merge/release/install. До их выполнения финальный outcome не закрыт. После delivery read-back повторить проверку tag/SHA/assets/feed/installed hash и обновить этот статус.

## Approval

Получено от пользователя: «Спеку подтверждаю». EXEC разрешён для этой SPEC, включая PR → merge в `master` → релиз `v1.8.0` → системную установку.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток работы | Следующее действие | Фактическое решение человека, если требовалось | Затронутые артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC начата | expanded: публичный skill + release + системная установка | skill inventory, repo/release preflight | review SPEC | исходное поручение, approval новой SPEC не получен | эта SPEC |
| SPEC подготовлена | `v1.8.0`, CI ZIP, install pinned tag | linter/rubric/role review; independent read-only запуск не смог читать файлы из-за sandbox error, выполнен adversarial fallback | запрос exact approval | — | эта SPEC |
| EXEC разрешена | точная фраза «Спеку подтверждаю» после расширения scope | skill/release/install входят в утверждённый контракт | реализовать и валидировать | «Спеку подтверждаю» | эта SPEC |
| Локальная реализация | skill полного цикла, ZIP workflow, README, версия/changelog | validator, paired smoke, build/test/pack/smoke; pilot 2/2 Headless и открытый PNG после Avalonia 12 alignment | PR/CI/merge/release/install; внешние claims pending | дополнительное разрешение не требуется | §16 файлы, локальные artifacts |
| SPEC уточнена | пользователь определил full-cycle AppAutomation skill, screenshots как часть визуального evidence | изучены quickstart/topology/adoption/selector/advanced/compatibility/smoke-consumer; добавлен практический pilot и re-review | запрос exact approval обновлённой SPEC | «Я имел ввиду скилл вообще по всему AppAutomation…» — уточнение, не фраза approval | эта SPEC |
| EXEC начата | exact approval получен; branch `feat/appautomation-skill` | пользователь: «Спеку подтверждаю»; подтверждён исходный `master` SHA `abdc219`, unrelated `emxLicense.cs` исключён | реализация и обязательная валидация | exact approval данной SPEC | skill, README, workflows, release docs, changelog/version |
