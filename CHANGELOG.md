# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Add provider-neutral Recorder destination discovery and pre-record selection for existing source `partial` scenario classes, with generic support, compact scan feedback, canonical per-destination scenario files, and legacy preset compatibility.
- Add provider-neutral multi-select popup authoring through `IMultiSelectControl`, `WithMultiSelect(...)`, `SelectMultiItems(...)`, and `CancelMultiSelection(...)`, with semantic Recorder capture for both Apply and Cancel, bounded position-aware traversal of virtualized items, and shared Headless/FlaUI replay.
- Add cardinality-neutral `ComboBoxEditor` filter authoring through `IComboBoxFilterControl`, `WithComboBoxFilter(...)`, `ApplyFilterSelection(...)`, and `CancelFilterSelection(...)`; Recorder stores the actual `0..N` value set and Apply/Cancel outcome without branching on selection mode.
- Add provider-neutral `SearchControl` authoring through `ISearchControl`, `WithSearchControl(...)`, `EnterSearch(...)`, `ClearSearch(...)`, and `ApplySearchFromHistory(...)`; one registration supports empty or later-populated history, while Recorder keeps manual input, clearing, and history selection as distinct semantic actions in shared Headless/FlaUI replay.
- Add stable grid row authoring through `GridRowSelector`, `WithGridColumns(...)`, and named row/cell overloads; Recorder can opt individual grids into explicit single or composite identities while preserving legacy index-based output for unconfigured grids.
- Add one provider-neutral `GridAutomationCatalog` for Recorder, Headless, and FlaUI, with structural cell metadata, stable row addresses, typed checks/edits, scoped composite editors, and bounded virtualized-row lookup.
- Add provider-neutral confirmed-selection sources so Recorder captures custom popup selectors as one logical `SearchAndSelect(...)` while preserving input-only and standard selector behavior.
- Add logical Spinner recording, numeric assertions, native Avalonia `NumericUpDown` replay, and a provider-neutral text-part adapter for custom spinner wrappers.
- Add lossless `TimePicker` recording, assertions, composite confirm/cancel registration, semantic grid editing, and shared Headless/FlaUI replay.
- Add provider-neutral single-selection parts so editable and non-editable composite editors record and replay through the existing `SelectComboItem(...)` command.
- Add semantic Expander recording, expanded-state assertions, and idempotent Headless/FlaUI replay.
- Add provider-neutral popup color recording, canonical ARGB assertions, and shared standalone/grid replay.
- Add semantic menu-item recording with stable direct locators or exact nested paths and shared Headless/FlaUI invocation.
- Add owner-scoped context-menu recording and exact-path Headless/FlaUI invocation without persisting popup primitives.
- Add Recorder `Check` actions for typed equality/inequality and empty/non-empty TUnit assertions plus replay-time semantic checkpoints, including logical composite targets and stable grid cells.
- Add explicit per-step relative dates in Recorder output through readable `DateTime.Today[.AddDays(...)]` expressions, including ranges, grid edits, and literal assertions.

### Fixed

- Read Headless ListBox captions from real item templates and accessibility names consistently for enumeration, selection and selected text, retaining duplicate-caption errors.
- Read composite selector commits through the existing SingleSelect value part instead of search input or requested-value caches; support composing it with SearchPicker without reopening an active popup.
- Honor the remaining grid-operation timeout while Headless waits for a newly materialized cell after a preceding edit.
- Activate catalog-backed Headless grid editors before requiring a visual cell and use the current `ActiveEditor` when a wide-table cell remains virtualized.
- Apply Number edits to a composite Headless editor's public `Value` before posting and committing the grid transaction.
- Normalize native grid row projections by configured stable identity and compatible scroll/row geometry even when non-identity cells differ, while preserving ambiguity across distinct vertical rows.
- Accept measured FlaUI scrollbar movement or a reached boundary when visible native rows remain unchanged.
- Recognize the start of FlaUI native grids through a usable ScrollPattern or the scrollbar RangeValue fallback, avoiding reset waits when the first rows are already visible.
- Remove the obsolete sample automation-bridge tables and their manual action controls; the native three-row DataGrid is now the single table-editor showcase.
- Resolve catalog-declared stable row identities, including hidden row metadata, from normalized visible FlaUI projections before attempting virtualized scroll traversal, while preserving visible ambiguity.
- Re-resolve virtualized SearchPicker list-item containers after result stabilization and scrolling, preferring `SelectionItemPattern` before a freshly verified click fallback.
- Stop native FlaUI grid scans at known scroll boundaries and distinguish repeated row observations from recycled containers and distinct matching rows.
- Read effective enabled state in Headless, including command availability and disabled ancestors, and reflect changes in both directions.
- Save Recorder controls and scenarios as a rollback-protected file transaction; retain recovery on failed saves.
- Reject ambiguous scoped controls and propagate composite configuration errors instead of treating them as missing popup parts.
- Share grid projection, formatting and explicit culture handling across value reads and stable-row matching.
- Replace generated-value timestamps with a UTC date and compact cryptographic series identifier, shared by the numbered values of each invocation.
- Coordinate polling tests with independent state-change workers rather than read-count-driven mutations.

- Merge `[UiControl]` declarations from all partial files of a Page into one generated Page source, deduplicating exact repeats and reporting conflicting names or locators.
- Keep Recorder output in one Page controls file and one scenario file per destination, merging later saves without changing user-authored partials, marking and cleaning recovery by stable destination, and queuing a final save behind an active autosave.
- Reuse Recorder controls by locator across Page partials and `const string` identifiers, allowing `WaitUntilExists` to use a more specific control while rejecting incompatible typed actions instead of generating suffixed duplicates.
- Revalidate configured Spinner proxy actions and value assertions through their interactive inner text box while keeping the generated logical Spinner locator stable and omitting generic fallback warnings for the verified proxy.
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
