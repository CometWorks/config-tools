# Existing-install discovery and setup usability

Status: implemented and verified on Linux. Covers PulsarConfig and MagnetarConfig on Windows and Linux.

Scope update: remove Pulsar migration entirely, including its fields and CLI flags. Older layouts are detected for explanation and protection only.

## Current behavior

- Before this change, both tools recognized installation files once a target was supplied, but did not offer a discovery-and-selection flow. Pulsar defaults to `PULSAR_DATA_DIR`, a recognized install beside the tool, or its data-home folder. Magnetar setup checks beside the tool, then its data-home folder. Magnetar's instance locator separately assumes launchers beside the tool.
- Both installers already save receipts containing the target path. These can seed discovery, but manually unpacked installs must also work without a receipt.
- Pulsar's dashboard actions occupy three terminal rows. Setup mixes three-row actions with one-row prerequisite/cancel buttons, text fields, and radio controls. Magnetar setup uses standard one-row buttons. This is a concrete inconsistency; reproduce the reported focus/highlight behavior before treating it as a rendering bug.
- Pulsar migration fields, command, conversion code, and old migration fixtures have been removed.
- Magnetar explicitly rejects automatic conversion of its old Linux `Bin`/wrapper layout. The Windows launcher called **Legacy** is a supported runtime variant, not that obsolete installation format.

## 1. Add read-only discovery and consistent inspection

Introduce a small installation inspection/discovery helper for each product. Reuse the existing file markers and path utilities; share only path/history plumbing that both tools actually need. Do not add a generic provider framework or new dependency.

Candidate sources, in priority order:

1. Explicit command-line target and the currently selected install/launcher.
2. Previously selected folders and existing installer receipts.
3. The tool directory, current working directory, and existing product/platform default paths, including Pulsar's environment override.
4. **Browse for installation…** for any arbitrary location, plus **Search folder…** for a bounded, cancellable scan of a user-selected parent directory.

Inspect actual product files rather than accepting a folder name or receipt as proof. Return the canonical path, product/layout, available game or launcher variants, and supported actions. Show a version only when reliable metadata is available; label receipt versions as last recorded when they cannot be verified against the files.

Deduplicate normalized paths with the existing platform case rules. Avoid symlink loops, retain mutation-time symlink restrictions, and tolerate unreadable folders, corrupt receipts, and missing paths. Distinguish current installations, older layouts, incomplete installations, and stale history. An `installed: false` receipt alone must not make an uninstalled target appear installed.

Discovery must not create receipts, download packages, execute launchers, or change installation/configuration files. Reuse the same inspection rules for action availability and installer validation so the screen does not promise actions the backend rejects. Revalidate immediately before an operation.

## 2. Connect discovery to opening and management

Provide an **Installations** picker with path, layout/status, and available variants; keep details for the selected item visible. Include **Refresh**, **Browse for installation…**, **Search folder…**, and **Install new…**.

- With no explicit target, show discovered choices before falling back to new-install setup. With none found, provide a useful empty state. With several found, require selection rather than silently choosing the first.
- Explicit CLI paths retain precedence. Headless commands remain deterministic and do not select a target from a discovery scan.
- Choosing an existing folder makes it manageable without reinstalling it or requiring a receipt. Remember successful selections in small per-user tool state outside the installation, following the existing theme preference storage pattern. Forgetting a saved entry only removes history.
- Use the picker from Pulsar's **Choose installation** and **Manage Pulsar**, and Magnetar's startup/open-instance and install-management entry points.
- For Magnetar, derive launcher choices from the selected install, not exclusively `AppContext.BaseDirectory`. Keep installation binaries, Magnetar configuration, DS data/worlds, and DS binaries as distinct paths. Preserve explicit instance overrides and prompt for unresolved paths rather than assuming every server belongs to the installation folder.
- After install/update/uninstall, refresh status and available actions. Update the active editor binding when the selected target changes; use the existing edit-flush behavior before switching an instance.

## 3. Make setup navigation and highlighting consistent

Use Pulsar's existing three-row workspace actions as the visual reference. Apply the same action dimensions and focus/hover behavior to both setup screens, including prerequisite checks, cancellation, and back navigation. Reuse or move the small existing workspace controls into shared code where needed.

Keep editable text single-line, but group fields with deliberate spacing. Order focus visually: installation selection, relevant options, actions, then activity. Tab/Shift-Tab move between controls; arrows retain their normal editing/radio/list behavior; Enter activates the focused action. Hidden and disabled controls must not enter the focus order.

Ensure the visible highlight and mouse hit area cover the same full action rectangle. Hover must not silently move keyboard focus or select an installation; keyboard input restores keyboard highlighting. Verify standalone Magnetar setup as well as setup opened inside either tool.

Use wrapping/stacking or scrolling at small terminal sizes so three-row actions remain reachable without overlapping the activity log. Do not depend on the terminal accepting a resize request. Preserve readable focus in quiet themes and Turbo C.

## 4. Remove migration

Delete Pulsar's migration action, `--source` and `--settings`, conversion code,
and migration-only tests/docs. Keep `--config` as the active configuration override.
Reject older Linux wrapper layouts without changing their files. Explain that the
current release needs a new folder; retain existing files and settings. Windows
Legacy launchers remain supported and must not be confused with the old layout.

## 5. Verification and delivery order

1. Add inspection/discovery and focused filesystem tests: installs without receipts, multiple installs, both products' current/older layouts, supported variants, unrelated folders, incomplete installs, stale/corrupt receipts, path aliases, permission failures, and bounded/cancelled searches.
2. Wire selection and history into both tools; verify explicit CLI precedence, selection surviving restart, and Magnetar's separate instance paths.
3. Rework setup layout and remove migration. Extend existing FakeDriver/input tests to cover focus order, full-row highlight/hit areas, mouse-to-keyboard transitions, hidden fields, and busy/cancel behavior.
4. Exercise both entry paths at 80×24 and the normal larger size, across quiet/Turbo themes. Use existing terminal-input tooling and perform Windows/Linux smoke checks; record any unavailable platform checks.
5. Run both existing test projects and relevant installer preservation/rollback tests; update both manuals and CLI help to match the final flow.

Acceptance: a user who unpacked a supported release outside the tool can discover or browse to it, reopen it later, and manage it without reinstalling; setup actions have consistent spacing and truthful highlighting; ordinary setup contains no unexplained migration fields; older-layout guidance clearly describes what will happen to programs and settings.

Primary implementation locations: `PulsarConfig/Program.cs`, `ConfigUi.cs`, `SetupUi.cs`, `Installer.cs`, `WorkspaceWidgets.cs`, and `PointerHighlight.cs`; `MagnetarConfig/Program.cs`, `Install/InstallOptions.cs`, `Install/SetupUi.cs`, `Install/Installer.cs`, `Io/InstanceLocator.cs`, and the instance-picker/app-shell UI; minimal common code under `Common/`.

## Implementation notes

- Shared catalog, picker, remembered paths, bounded search, setup workspace, and
  workspace highlight controls are implemented under `Common/`.
- Product probes remain small product-specific helpers; the existing installer
  marker checks are reused. No dependency was added.
- Startup/open/setup paths use the picker. Magnetar derives launcher configuration
  from the selected install while retaining separate explicit instance paths.
- Versions are omitted when no reliable installed-version metadata is available.
- Verification: solution tests pass (Pulsar: 65 passed, one Windows-only skip;
  Magnetar: 119 passed). Shared FakeDriver tests cover 80×24 and 132×40,
  quiet/Turbo themes, focus scrolling/order, three-row hit areas, and busy state.
- Linux PTY checks pass for first-run discovery in both tools and Pulsar's
  setup/global navigation, resize handling, and 4,800 mouse events.
- Windows runtime/terminal checks still require a Windows host; no Windows
  execution is claimed. No installed-version display was added without reliable
  metadata. Unsupported/incomplete layouts are shown with actions disabled.
