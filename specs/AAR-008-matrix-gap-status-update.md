# AAR-008 Matrix Gap Status Update

## Status
- Task: AAR-008
- Control: Support matrix documentation
- Scenario: Reflect implemented gap resolutions and remaining runtime-only risks
- Spec status: approved automatically by user instruction

## Objective
Update `ControlSupportMatrix.md` after AAR-001..AAR-007 so the matrix no longer
describes resolved gaps as open implementation gaps.

## Scope
- Update only support-matrix documentation.
- Keep the original Arm.Srv static-audit facts.
- Reclassify implemented support into current status for recorder, headless and
  FlaUI layers.
- Keep residual risks explicit where runtime UIA behavior was not verified.

## Non-Goals
- No runtime or API changes.
- No Arm.Srv XAML changes.
- No attempt to claim native Eremex UIA support beyond the bridge/composite
  adapters implemented in earlier tasks.
- No generated code or test changes.

## Required Matrix Changes
1. Mark search picker, grid user actions, in-grid editing, popup range filters,
   dialogs/notifications/export and shell navigation as implemented through the
   new typed contracts and composite adapters.
2. Preserve constraints that still require application/runtime cooperation:
   stable automation ids, configured child parts, bridge rows/cells and runtime
   validation of Eremex UIA exposure.
3. Update the "additional support by layer" section so it lists remaining
   follow-up risks instead of AAR-001..AAR-007 work that now exists.
4. Keep unresolved items such as `CopyTextBox`, generic Eremex editor discovery,
   status/loading/approval wrappers and native Eremex row/cell UIA clearly
   separated from completed tasks.

## Acceptance Criteria
- `ControlSupportMatrix.md` matches the implemented contracts from AAR-001
  through AAR-007.
- No AAR-001..AAR-007 gap remains phrased as "not covered" or "needs new typed
  API" unless the note is explicitly about runtime-only validation or app
  automation anchors.
- The matrix still distinguishes recorder, headless and FlaUI responsibilities.
- Documentation-only change; code and tests are not modified.

## Verification
- `git diff --check -- ControlSupportMatrix.md tasks.md specs/AAR-008-matrix-gap-status-update.md`
- Manual review of Arm.Srv rows and support-by-layer rows for stale open-gap
  wording.

## Spec Review
- Status: PASS
- Ambiguity check: PASS. Scope is documentation-only and references concrete
  implemented task ranges.
- Edge cases: PASS. Native Eremex UIA and missing automation anchors stay scoped
  as residual runtime risks.
- User approval: Auto-approved per instruction in the current task.

## Implementation Result
- Updated Arm.Srv audit rows in `ControlSupportMatrix.md` so AAR-001..AAR-007
  workflows are listed as supported through typed contracts, composite adapters,
  bridge mappings or authored helpers.
- Replaced stale "required support" rows with residual support by layer:
  Arm.Srv-specific hints/part maps, deterministic bridge ids, runtime Eremex UIA
  validation and proprietary/native gesture limits.
- Kept unresolved wrapper/status/loading/copy-text items separate from completed
  typed API work.

## Post-EXEC Review
- Status: PASS
- Scope check: PASS. Only documentation/task state files were changed.
- Stale gap wording check: PASS. Resolved AAR-001..AAR-007 items are no longer
  described as missing core APIs.
- Verification:
  - `rg -n "SearchPickerControlAdapter|IGridUserActionControl|IEditableGridControl|IDateRangeFilterControl|INumericRangeFilterControl|IDialogControl|INotificationControl|IFolderExportControl|IShellNavigationControl" src tests`
  - `rg -n "not covered|needs new|future concept|не покрыто|не моделирует|не является|будущ|High-priority consumer gap|Current API" ControlSupportMatrix.md`
  - `git diff --check -- ControlSupportMatrix.md tasks.md specs/AAR-008-matrix-gap-status-update.md`
- Verification result: PASS. `git diff --check` reported only existing CRLF
  normalization warnings for markdown files.
