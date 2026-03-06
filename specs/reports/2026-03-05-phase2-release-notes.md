# Phase 2 Release Notes

Дата: 2026-03-06

## Summary

Выполнен архитектурный рефакторинг базовых UI-test контрактов:
- выделены `EasyUse.Session.Contracts` и `EasyUse.TUnit.Core`;
- удалены legacy TUnit-пакеты/namespace;
- runtime-проекты переведены на общий контракт launch-опций.

## Breaking Changes

1. Удалены проекты:
   - `FlaUI.EasyUse.TUnit`
   - `Avalonia.Headless.EasyUse.TUnit`
2. Удалены legacy API-namespace для этих пакетов.
3. Базовый тестовый класс изменён:
   - `DesktopUiTestBase<TPage>` -> `UiTestBase<TSession, TPage>`.
4. Для headless runtime канонический session namespace:
   - `Avalonia.Headless.EasyUse.Session`.

## Versioning

- `FlaUI.EasyUse` -> `2.0.0`
- `Avalonia.Headless.EasyUse` -> `2.0.0`
- `EasyUse.Session.Contracts` -> `1.0.0` (новый пакет)
- `EasyUse.TUnit.Core` -> `1.0.0` (новый пакет)

## Migration Artifacts

- `specs/reports/2026-03-05-phase2-api-migration-map.md`
- `specs/reports/2026-03-05-phase2-migration-guide.md`

## Validation

Проверка проводится командами из спецификации фазы 2 (build/test/discovery parity + grep checks).
