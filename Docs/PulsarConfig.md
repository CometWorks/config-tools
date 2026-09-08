# PulsarConfig

PulsarConfig is a .NET 10 / Terminal.Gui tool for configuring, launching,
installing, and managing Pulsar on Windows and Linux for Space Engineers 1 and 2. Release executables bundle
.NET and all managed dependencies: no Python or separate .NET install is needed
to run setup. Steam, the chosen game, GPU drivers, and the .NET 10 runtime remain
prerequisites for running Pulsar and the game; the tool's private runtime does
not install a system runtime.

Download `PulsarConfig-win-x64.exe` on Windows or `PulsarConfig-linux-x64.bin` on Linux from the
[config-tools releases](https://github.com/CometWorks/config-tools/releases):

```sh
# Download PulsarConfig-linux-x64.bin from the latest pulsarconfig-v* release.
chmod +x PulsarConfig-linux-x64.bin
./PulsarConfig-linux-x64.bin
```

The main screen shows the selected installation, game, configuration directory,
and Steam launch options. Use **Choose installation** on the home screen to choose another
installation or switch SE1/SE2. An optional config override supports `-home` and
custom setups; point it at the directory Pulsar actually uses. The defaults are
`<install>/Legacy` for SE1 and `<install>/Modern` for SE2. CLI equivalents:

```sh
./PulsarConfig-linux-x64.bin --target ~/Games/Pulsar --game se1
./PulsarConfig-linux-x64.bin --target ~/Games/Pulsar --game se2 --config /path/to/Modern
```

**F5 Start game** asks Steam to launch the selected game with its existing launch
options (`steam -applaunch 244850` for SE1, `1133870` for SE2). It preserves Steam's
arguments, overlay, input configuration, and environment. The tool does not
rewrite Steam settings; ensure the displayed installation matches the launch
options already configured in Steam.

**Manage Pulsar** on the home screen opens the installation form described below.
Tab moves between fields and buttons; Enter activates the selected action.
Installation sets the target folder; Release accepts `latest` or a tag such as
`v2.4.1`; Game accepts `auto`, `se1`, or `se2`. Each operation asks for
confirmation and displays a progress log.
Cancel task stops downloads/preparation before the installation is switched.
Run as your normal Steam user, without sudo.

The unified package supplies both launchers. Game selection chooses the menu
shortcut and displayed Steam command, not a different package. Updates preserve
the saved choice; a new installation defaults to SE1. Use `--game se2` to select
SE2 directly. SE2 setup rejects packages missing the Modern launcher before
replacing any existing installation. Windows packages also include the Legacy
.NET Framework launcher. The displayed Steam command uses Interim for SE1 and
Modern for SE2; existing Steam launch options are never rewritten.

## Navigation and terminal size

The home screen groups game/plugin actions on the left and installation/tool
actions on the right. Every button uses the same hover outline, with shortcut
labels shown only in the bottom bar. Installation paths sit below both columns. **F1** returns
home; **F2** changes the theme. Mouse movement highlights buttons, lists and bottom
shortcuts without moving keyboard focus. Keyboard input restores keyboard
highlighting. The bottom bar is outside Tab/arrow focus navigation, but its
shortcuts work from setup and dialogs too. Navigation cancels unsaved forms;
running setup tasks cancel and finish cleanup before navigation proceeds.

On startup, the TUI requests at least **128 columns × 40 rows**, preserving larger
windows. It uses the terminal’s resize request and the native console API on
Windows. A terminal or window manager can decline the request; manual resizing
continues to work. CLI commands do not resize the terminal.

## Windows

Run `PulsarConfig-win-x64.exe` normally. Installation defaults to
`%LOCALAPPDATA%\Pulsar`; **Choose installation** selects an existing folder.
Keep the configuration tool outside that folder for install/update/uninstall,
so Windows does not lock the directory being replaced. Configuration editing
and self-update also work when the tool is beside Pulsar.

Setup downloads and verifies the Windows ZIP, installs all three launchers,
preserves settings, and creates a Start Menu shortcut through Steam. Steam is
located through its registry entry or standard installation folder. Dependency
checks cover .NET 10, .NET Framework 4.8, Steam and the selected game. Running
launchers and hosted game processes must be closed before setup or config edits.

Steam launch options use quoted Windows paths, for example:

```text
"C:\Games\Pulsar\Interim.exe" %command%
"C:\Games\Pulsar\Modern.exe" %command%
```

Keep additional game arguments after `%command%`. The tool starts Steam with
`-applaunch`, retaining Steam’s configured launch options, overlay and controls.
Use the same CLI actions below with the Windows executable and Windows paths;
offline installation accepts `--archive C:\Downloads\Pulsar-v2.4.1-win-x64.zip`.

## Plugins, dev folders, sources, and profiles

- **F3 Plugins** browses cached hub/plugin catalogs, local hub manifests, local
  DLLs, and registered dev folders. Filter by name/ID/type; Space, Enter, or
  Toggle changes the active profile. Enabling a plugin also enables its cached
  dependencies. Missing dependencies are reported before writing. Remote
  catalogs are refreshed by Pulsar on game launch.
- **F6 Dev folders** adds a source by selecting its plugin manifest XML. Edit its
  display name, folder, manifest filename, source Enabled flag, active-profile
  selection, and Debug build flag. Unchecking Debug build selects Release.
  The profile ID is the folder name, matching Pulsar; the manifest filename is
  stored as `File` in `Sources/sources.xml`. Renaming the folder updates matching
  dev IDs in saved profiles as well as Current.
- **F7 Sources** adds/edits/removes remote hubs, individual plugin
  repositories, local hubs, and Workshop sources. Remote entries expose branch,
  manifest file (plugin repos), Enabled, and Trusted. Editing remote source
  locations clears their cached hash/check time for Pulsar's next refresh.
- **F4 Profiles** saves the active set under a new name, loads a preset into
  Current, updates a preset, renames it, or deletes it. Version pins, debug
  flags, mod selections, and unknown XML fields are preserved. `Current` is
  reserved for the active profile. Loading/deleting/overwriting asks first.

Edits use `Profiles/Current.xml` and `Sources/sources.xml` beneath the selected
configuration directory. Existing files get a `.bak` copy before their first
change in a session. Writes are atomic and retain file permissions; operations
that touch multiple files roll back if a write fails. Malformed XML is reported
without replacing it. Edits share the installer's operation lock and require
Pulsar/the game to be closed, preventing a running loader from overwriting them.
Removing sources unregisters them; source files and profile selections remain.

## Tool updates and prerequisite checks

**Check for tool updates** on the home screen checks for newer `pulsarconfig-vX.Y.Z` releases and can
update this executable after closing the tool. `--check-update`, `--self-update`, and
`--tool-version` provide the headless equivalents. Tool updates are separate
from Pulsar package updates; see the [update and recovery details](../README.md#updating-the-tools).

Setup's **Check prerequisites** button and `PulsarConfig check --game se1` report
.NET 10 and Steam/game discovery, plus .NET Framework on Windows or
Vulkan/Opus, display libraries and optional audio on Linux.
Install and update also run these checks before downloading. They are
advisory: Steam containers can supply libraries absent on the host, and installation
can precede runtime setup. File/path validation, writable staging, checksums and
running-process checks remain mandatory. Uninstall does not require game runtimes.

On Linux, the check accepts either Wayland or X11 and respects an existing
`SDL_VIDEODRIVER`; it does not choose a display backend or modify input settings.
The bundled tool runtime does not satisfy Pulsar's separate .NET 10 requirement.

## Install, update, and uninstall

The default location is `%LOCALAPPDATA%\Pulsar` on Windows or
`$XDG_DATA_HOME/Pulsar` (normally `~/.local/share/Pulsar`) on Linux. A tool placed inside an installed Pulsar directory
defaults to that installation instead. `PULSAR_DATA_DIR` or `--target` overrides it. Updates
keep the chosen path, profiles, custom plugins, and other user files. Existing
portable unified Pulsar installations can also be updated. Use **Check for tool updates** on the home screen or `--self-update`
to update the tool itself. Older unified
packages published by linux-compat (for example 2.3.3) use Update.

The tool downloads the matching Windows x64 ZIP or Linux x64 tar.gz from **SpaceGT/Pulsar**, verifies
GitHub's SHA-256 digest, rejects unsafe archive entries, and prepares a complete
staging directory before switching installations. A failure during the switch
restores the previous program, receipt, and desktop entry. Setup refuses to
replace a running installation or an unrelated non-empty directory.

The previous directory is retained beside the target as
`.Pulsar-backup-TIMESTAMP-SUFFIX` (using your chosen folder name). This includes
program files and user data. Receipts and shortcut backups are stored under
`$XDG_STATE_HOME/pulsar-installer`, normally `~/.local/state/pulsar-installer`.
Keep enough disk space for the current installation, staging copy, and backups.
Remove backups yourself after verifying the new installation to reclaim space.

Uninstall removes the package-owned launcher files and `Libraries`, plus this
installation's menu shortcut. This removes **both game launchers**, not only the
selected game. It **keeps** profiles, local plugins, other user
files, and the backup. Space Engineers files and saves are never removed.
After uninstalling, remove the Pulsar executable and `%command%` from both games'
Steam launch options if configured, keeping any game arguments.

## Steam launch options

Setup displays the exact launch option for the selected game. In Steam, open
that game's **Properties → General → Launch Options**. For SE1:

```text
/path/to/your/Interim.bin %command%
```

For SE2:

```text
/path/to/your/Modern.bin %command%
```

Replace `/path/to/your/` with your Pulsar installation folder and keep
`%command%` unchanged. You can configure both games against the same installation.

Keep extra arguments, such as `-nosplash -noprompt`, after `%command%`. Paths
containing spaces are quoted. The tool does not rewrite Steam's configuration
while Steam is running. The installed menu shortcut launches the selected game
through Steam (SE1: 244850; SE2: 1133870), so configure its launch option first.

Setup does not add a launch wrapper, set overlay variables, or force X11/Wayland.
The normal unified Pulsar and Steam launch paths remain responsible for those.

## Existing installations

On startup without an explicit `--target` or `--config`, choose an installation
from the picker. It checks the tool directory, working directory, data-home
location, installer receipts, and remembered selections. Manually unpacked
current releases are recognized by their program files; no receipt is required.
`PULSAR_DATA_DIR` and explicit command-line paths retain precedence.

Use **Choose installation** from the dashboard or setup to switch folders.
**Browse** selects an arbitrary installation; **Search folder** scans a selected
parent (up to four levels and 2,000 directories, skipping hidden child folders
and directory links). Back cancels a search. **Refresh** checks the known paths
again. To install somewhere new, enter its full folder path and choose
**Install new**. Successful selections are remembered in per-user tool settings.

Setup shows the detected status and enables applicable actions. All action
buttons occupy three rows, with matching mouse and keyboard highlight areas.
Tab/Shift-Tab follow the form; fields scroll into view on smaller terminals.

Older LinuxCompat wrapper installations are detected but no longer managed.
Install the current release in a new folder and retain your old files/settings.
Migration, `--source`, and `--settings` have been removed. The active configuration
override remains `--config`; supported Windows Legacy launchers are unaffected.

## Scripted and offline use

An explicit action with `--yes` uses the same implementation without the terminal UI:

```sh
./PulsarConfig-linux-x64.bin install --target "$HOME/Games/Pulsar" --yes
./PulsarConfig-linux-x64.bin install --game se2 --target "$HOME/Games/Pulsar" --yes
./PulsarConfig-linux-x64.bin update --target "$HOME/Games/Pulsar" --version v2.4.0 --yes
./PulsarConfig-linux-x64.bin uninstall --target "$HOME/Games/Pulsar" --yes
```

Choose one install command for a new installation. To switch an existing
installation's shortcut to SE2, use `update --game se2`; this also updates the
shared package. Omitting `--game` preserves the saved choice.

`--archive /path/to/pulsar-v2.4.0-linux-x64.tar.gz` uses a local release archive.
Supply `--sha256 HEX_DIGEST` to verify it; otherwise a local archive is treated
as a file you already trust.

## Validation

```sh
dotnet test PulsarConfigTests/PulsarConfigTests.csproj -c Release
```

## Appearance

Press **F2 Theme** to choose **Muted** or **Turbo C**. Switching applies immediately
and preserves the current form and operation. The choice is saved for both tools
in [machine-local user settings](../README.md#appearance), independently of the
Pulsar install and game profiles. Pulsar defaults to muted until a preference is
saved.
