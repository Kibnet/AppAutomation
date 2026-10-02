# Проверка управления указателем

Проверка выполнена 2 октября 2026 года в Windows. Framework проверен поверх `e7161c869004e65d9c15063c385f48c47a0a16a3`; Unlimotion — поверх `46711e60d6ef453e794104ee7d9f81ad6f37c68c`. [SPEC и журнал](../../../specs/2026-10-02-desktop-pointer-ownership.md), [описание API](../../appautomation/pointer-input.md).

## Видео и возврат мыши

- [До изменений](before.mp4): `StatusContract_RussianDarkBlocked`, AppAutomation 1.6.0. Исходная OS trace зафиксировала движение через `(0,0)` и отсутствие возврата самим тестом. Записанный baseline завершился ошибкой обнаружения дополнительного tooltip-текста, хотя подсказка видна в видео.
- [После изменений](after.mp4): тот же сценарий, локальные пакеты `1.8.1-pointer.20261002.2`, PASS. [PNG подсказки](tooltip.png) сделан внутри hover callback до возврата мыши.
- [Trace click/hover](pointer-trace.jsonl), [OS read-back](cursor-result.json): сохранённая и конечная точка `(4474,1190)`, отдельный возврат после каждого действия; `(0,0)` отсутствует.
- [Аудит полного consumer-прогона](restoration-audit.json): 9 физических операций, 9 точных совпадений saved/restored, 0 failed pointer operations. Это не означает отсутствие ошибок остальных assertions в suite.

Видео ограничены клиентской областью синтетического тестового окна, без звука: 1252×720, 30 FPS. Разница длительности 39.267s/8.833s не является замером ускорения: захват начинался в разные моменты. Возврат за пределы окна подтверждает OS trace. Сырые записи с краями фонового рабочего стола не включены.

SHA-256:

```text
before.mp4  7D1647E25E2C1B80FE3233C54E54D8BC1182EC4F697E2BC71CDE182DED5AF13F
after.mp4   2C75802640010288567C853010EEA8B3FBF27E817059307BDCDB51ED04E7566C
```

## Результаты

| Проверка | Результат |
| --- | --- |
| Framework Release build, net8.0/net10.0 | PASS, 0 errors |
| Восемь non-native test projects | 656 PASS |
| Framework FlaUI suite | 123 PASS / 0 FAIL / 2 expected skips |
| Framework итого | 779 PASS / 0 FAIL / 2 skips |
| Unlimotion FlaUI | 26 PASS / 1 baseline FAIL / 1 expected skip |
| Unlimotion Headless | 50 PASS / 1 FAIL |
| Последние TaskLoading/TaskSpaces helper changes | По 1 PASS в отдельных финальных запусках |

Framework FlaUI включает 40 controller/fault tests, 12 native pointer tests и cross-process mutex test. Native drag реально меняет порядок на `B,A` и даёт один Drop. Пропуски framework: clipboard E2E требует отдельной изолированной desktop-сессии; служебный child entry запускается родительским тестом. Consumer skip — opt-in startup comparison без baseline executable.

Физические мониторы: `(0,0)-(3840,2160)` и `(3840,561)-(5760,1641)`. Возврат между ними проверен. Отрицательная раскладка, отдельные комбинации DPI и отключение монитора физически не проверялись; соответствующие ошибки покрываются fake backend tests. Конфигурация дисплеев не менялась.

Оставшиеся consumer failures: `CurrentTaskRepeaterSection` отсутствует также на baseline; показанная Headless-форма выявила сброс выбора remote при HTTP→SSH. Product fix Unlimotion не включён. Его полный repository unit suite и CI не заявляются пройденными.

Полные локальные логи сохранены в `artifacts/pointer/validation/` framework и `artifacts/pointer/` consumer. В Git включена небольшая выборка визуального evidence. Эти результаты отделены от PR CI checks и не подтверждают публикацию пакетов в NuGet.
