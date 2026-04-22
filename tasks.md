## AAR-001 Recorder Custom Control Hints

- Control: Eremex editors and composite wrappers
- Scenario: Recorder maps non-native or wrapped controls to intended `UiControlType`
- Description: Extend recorder hints so consumers can override detected control type and locator kind for custom controls such as Eremex editors, `CopyTextBox`, `SearchControl`, and `ServerSearchComboBox` without hard-coding Eremex dependencies.
- Status: done
- Spec: specs/AAR-001-recorder-custom-control-hints.md
- LastStep: completed
- DoneCriteria: Recorder options support typed custom control hints; generated controls use hinted `UiControlType` and locator metadata; tests cover backwards compatibility and a custom editor mapping.

## AAR-002 Recorder Search Picker Flow

- Control: `miniControls:SearchControl`, `client:ServerSearchComboBox`
- Scenario: Record composite search/select interactions as user-level search picker operations
- Description: Add recorder support for a composite search picker action that can generate `Page.SearchAndSelect(...)` for configured search-picker controls instead of raw text/list/button fragments.
- Status: new
- Spec: null
- LastStep: 0
- DoneCriteria: Recorder can produce and persist a search picker step from configured parts; generated code uses `SearchAndSelect`; tests cover preview and save output.

## AAR-003 Eremex Grid User Actions

- Control: Eremex `DataGridControl`
- Scenario: Record and replay selected-row open, header sort, scroll-to-load-more, copy cell and export triggers
- Description: Add explicit recorder/runtime primitives for grid user actions that are currently only described as gaps in Arm.Srv list-page workflows.
- Status: new
- Spec: null
- LastStep: 0
- DoneCriteria: Grid user actions have typed API/recorded actions where feasible; tests cover generated code or documented unsupported cases with diagnostics.

## AAR-004 In-Grid Editor Activation

- Control: Eremex grid cell editors
- Scenario: Edit a grid cell using text, spin, date, combo or server-search editor and commit/cancel
- Description: Add a provider-neutral edit-cell abstraction and at least one provider implementation path for visual grid cell activation and editor value commit.
- Status: new
- Spec: null
- LastStep: 0
- DoneCriteria: API exposes cell edit workflow; tests cover activation/value/commit behavior or explicit unsupported diagnostics per runtime.

## AAR-005 Popup Date And Range Filters

- Control: `DateRangeFilterControl`, `RangeFromToControl`, Eremex `DateEditor` / `SpinEditor`
- Scenario: Open filter popup, set date/range/spinner values, apply or cancel
- Description: Add composite filter support so date/range popup controls can be automated through stable parts instead of ad hoc button/text interactions.
- Status: new
- Spec: null
- LastStep: 0
- DoneCriteria: Date/range filter adapters or helpers exist; tests cover open, set value, apply and cancel paths.

## AAR-006 Dialog Toast And Export Flow

- Control: `DialogHost`, notifications, folder picker/export
- Scenario: Confirm modal dialogs, assert toast/status messages, and handle export folder selection
- Description: Add automation support or explicit abstractions for modal/toast/export workflows used by Arm.Srv.
- Status: new
- Spec: null
- LastStep: 0
- DoneCriteria: Dialog/toast/export interactions have documented typed API or provider-specific supported/unsupported diagnostics; tests cover supported behavior.

## AAR-007 Shell Docking Navigation

- Control: Eremex docking shell
- Scenario: Open/switch business pages through the real shell
- Description: Add generic shell/docking navigation helpers based on stable automation anchors without taking a hard dependency on Eremex.
- Status: new
- Spec: null
- LastStep: 0
- DoneCriteria: Navigation helpers can select/open named panes or explicitly report unsupported runtime capabilities; tests cover helper behavior.

## AAR-008 Matrix Gap Status Update

- Control: Support matrix documentation
- Scenario: Reflect implemented gap resolutions and remaining runtime-only risks
- Description: Update `ControlSupportMatrix.md` after implementation tasks so resolved gaps are no longer listed as open and residual risks are clearly scoped.
- Status: new
- Spec: null
- LastStep: 0
- DoneCriteria: Matrix status matches implemented code and tests; no resolved gap remains described as requiring implementation.
