# FlaUI Manual Run Report

## Metadata
- Date: 2026-03-05
- Time: 13:53-13:55 (+03:00)
- Tester: Codex (AI assistant)
- Machine: DESKTOP-AUDO1TJ
- OS: Microsoft Windows 11 Enterprise 10.0.22631

## Run Context
- Branch/Commit: master / 0458e75
- Test project: tests/DotnetDebug.UiTests.FlaUI.EasyUse/DotnetDebug.UiTests.FlaUI.EasyUse.csproj
- Command: dotnet test tests/DotnetDebug.UiTests.FlaUI.EasyUse/DotnetDebug.UiTests.FlaUI.EasyUse.csproj
- Scope (smoke/full): full shared scenario set (11 tests)

## Result
- Status (Passed/Failed): Passed
- Total tests: 11
- Passed: 11
- Failed: 0
- Skipped: 0

## Visual Verification Notes
- App window opened: Yes (inferred by successful FlaUI interaction and assertions).
- Scenario observed: Shared MainWindow scenarios executed end-to-end in FlaUI runtime.
- Expected UI changes observed: Yes by automated UI assertions (labels/list/progress/selection states).
- Unexpected behavior: During separate `dotnet test DotnetDebug.sln` full run there was one intermittent COMException in a FlaUI test; isolated re-run of FlaUI project passed.

## Attachments/References
- Logs: console output from `dotnet test` commands in this task.
- Screenshots: none.
- Other notes: Discovery parity for shared scenarios verified via `tests/Verify-UiScenarioDiscoveryParity.ps1` (11/11 parity).
