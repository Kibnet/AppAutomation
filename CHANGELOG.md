# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Add provider-neutral Recorder destination discovery and pre-record selection for existing source `partial` scenario classes, with generic support, compact scan feedback, collision-safe per-scenario files, and legacy preset compatibility.
- Add provider-neutral multi-select popup authoring through `IMultiSelectControl`, `WithMultiSelect(...)`, `SelectMultiItems(...)`, and `CancelMultiSelection(...)`, with semantic Recorder capture for both Apply and Cancel, bounded position-aware traversal of virtualized items, and shared Headless/FlaUI replay.
- Add cardinality-neutral `ComboBoxEditor` filter authoring through `IComboBoxFilterControl`, `WithComboBoxFilter(...)`, `ApplyFilterSelection(...)`, and `CancelFilterSelection(...)`; Recorder stores the actual `0..N` value set and Apply/Cancel outcome without branching on selection mode.

### Fixed

- Record and replay the ARM-style `ServerSearchComboBox` contract in cards and grid cells through its real `PopupEditor` input and popup `ListBox`.
- Preserve the typed search query when `ServerSearchComboBox` replaces the editor text with the selected value before Recorder receives the selection event.

### Removed

- Remove recorder overlay minimize/restore controls and hotkey handling.

## [1.5.9] - 2026-05-20

### Fixed

- Record and replay detached popup search-picker selections, including Arm order customer selection.
- Make search-picker expansion idempotent so direct `Search()` then `SelectItem()` remains safe.

## [1.5.8] - 2026-05-19

### Fixed

- Include `AppAutomation.Recorder.Avalonia` in release packaging so it is published to NuGet, GitHub Packages, and GitHub release assets.

## [2.1.0] - 2026-03-17

### Added

- Source generation for page objects using `[UiControl(...)]` attributes
- Headless adapter (`AppAutomation.Avalonia.Headless`) for fast in-process UI testing
- FlaUI adapter (`AppAutomation.FlaUI`) for Windows desktop UI automation
- TUnit integration (`AppAutomation.TUnit`) with `UiTestBase` and `UiAssert`
- CLI tooling with `appautomation doctor` command for project validation
- `dotnet new` templates (`AppAutomation.Templates`) for canonical Avalonia test topology
- Adapter pattern for composite controls with `WithAdapters(...)` registration API
- Built-in composite abstraction `ISearchPickerControl` with `WithSearchPicker(...)`
- `AppAutomation.TestHost.Avalonia` with reusable desktop and headless launch helpers
- Desktop launch helpers with repo-root, project-path, and build-before-launch support
- Headless launch helpers supporting `BeforeLaunchAsync`, `CreateMainWindow`, `CreateMainWindowAsync`
- Package-based smoke testing via `eng/smoke-consumer.ps1`
- GitHub Actions workflow for automated package publishing

### Changed

- Improved adapter registration API

## [1.1.0] - 2026-02-01

### Added

- Initial public release of the AppAutomation framework
- `AppAutomation.Abstractions` core contracts and interfaces
- `AppAutomation.Session.Contracts` with launch options
- `AppAutomation.Authoring` source generator package
- Basic headless and FlaUI runtime adapters
- Initial template package for consumer scaffolding
- CLI tool foundation with doctor command
