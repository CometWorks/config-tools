# PulsarConfig — Linux setup

PulsarConfig is a .NET 10 / Terminal.Gui tool for configuring, launching,
installing, and managing native Pulsar for Space Engineers 1 and 2. Release executables bundle
.NET and all managed dependencies: no Python or separate .NET install is needed
to run setup. Steam, the chosen game, GPU drivers, and the .NET 10 runtime remain
prerequisites for running Pulsar and the game; the tool's private runtime does
not install a system runtime.

Download `PulsarConfig-linux-x64.bin` from the
[config-tools releases](https://github.com/CometWorks/config-tools/releases):

```sh
curl -fLO https://github.com/CometWorks/config-tools/releases/latest/download/PulsarConfig-linux-x64.bin
chmod +x PulsarConfig-linux-x64.bin
./PulsarConfig-linux-x64.bin
```

The main screen shows the selected installation, game, configuration directory,
and Steam launch options. Use **File → Open installation** to choose another
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

**File → Setup / update / migrate** opens the installation form described below.
Tab moves between fields and buttons; Enter activates the selected action.
Installation sets the target folder; Release accepts `latest` or a tag such as
`v2.4.1`; Game accepts `auto`, `se1`, or `se2`. Old install/settings are used for
migration. Each operation asks for confirmation and displays a progress log.
Cancel task stops downloads/preparation before the installation is switched.
Run as your normal Steam user, without sudo.

The unified package supplies both launchers. Game selection chooses the menu
shortcut and displayed Steam command, not a different package. Updates preserve
the saved choice; a new installation defaults to SE1. Use `--game se2` to select
SE2 directly. SE2 setup rejects packages missing the Modern launcher before
replacing any existing installation. The tool is Linux-only; Pulsar itself
also supports Windows through its Windows packages and installer.

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
- **Plugins → Sources** adds/edits/removes remote hubs, individual plugin
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

## Install, update, and uninstall

The default location is `$XDG_DATA_HOME/Pulsar`, normally
`~/.local/share/Pulsar`. A tool placed inside an installed Pulsar directory
defaults to that installation instead. `PULSAR_DATA_DIR` or `--target` overrides it. Updates
keep the chosen path, profiles, custom plugins, and other user files. Existing
portable unified Pulsar installations can also be updated. Download a new config-tools release
to update the tool itself. Older unified
packages published by linux-compat (for example 2.3.3) use Update.

The tool downloads the Linux x64 archive from **SpaceGT/Pulsar**, verifies
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

## Migrate LinuxCompat 1.0.x

This migrates the old **SE1** layout and profiles; it is not an SE2 profile
converter. The resulting unified installation also supports SE2.

For the native 1.0.16 release, the old defaults are:

- Binaries: `~/.local/share/Pulsar` (`Interim` shell wrapper and `Bin/Interim`).
- Settings: `~/.config/Pulsar`; `~/.local/config/Pulsar` is also detected.
  `XDG_CONFIG_HOME`, `PULSAR_DIR`, or `--settings` can override this.

Migrate replaces the old binary layout and copies `config.xml`, `Profiles`,
`Local`, and source configuration into the new installation's `Legacy` folder.
It does not overwrite an existing destination profile/configuration. Old
PluginHub, preloader, compiler, and native-library caches are not migrated;
Pulsar downloads current metadata and rebuilds plugins on first launch.

Migration converts legacy core-plugin IDs to the unified equivalents. It also
moves developer-folder `DataFile` declarations from profiles to the source's
`File` field and reads plugin IDs from their manifests. Incompatible old core
developer sources are disabled so current core plugins can load. Developer
sources without a recorded manifest are reported for review.

The original settings remain available for rollback. When the destination is
different, old program files remain at the source too; remove them after
checking the migrated installation. The old release's owned menu icons are
backed up and removed when its shortcut is replaced. Flatpak application and
Steam compatibility-tool migration are not supported by this native installer.

After migration, replace the old `Interim` wrapper path in Steam launch options
with the displayed `Interim.bin` path. No old environment-setting wrapper is
retained.

## Scripted and offline use

An explicit action with `--yes` uses the same implementation without the terminal UI:

```sh
./PulsarConfig-linux-x64.bin install --target "$HOME/Games/Pulsar" --yes
./PulsarConfig-linux-x64.bin install --game se2 --target "$HOME/Games/Pulsar" --yes
./PulsarConfig-linux-x64.bin update --target "$HOME/Games/Pulsar" --version v2.4.0 --yes
./PulsarConfig-linux-x64.bin migrate --target "$HOME/.local/share/Pulsar" \
    --settings "$HOME/.config/Pulsar" --yes
./PulsarConfig-linux-x64.bin uninstall --target "$HOME/Games/Pulsar" --yes
```

Choose one install command for a new installation. To switch an existing
installation's shortcut to SE2, use `update --game se2`; this also updates the
shared package. Omitting `--game` preserves the saved choice.

`--archive /path/to/pulsar-v2.4.0-linux-x64.tar.gz` uses a local release archive.
Supply `--sha256 HEX_DIGEST` to verify it; otherwise a local archive is treated
as a file you already trust. The installer never downloads or executes the old
installer during migration.

## Validation

```sh
dotnet test PulsarConfigTests/PulsarConfigTests.csproj -c Release
```

The optional integration test runs the **actual** native 1.0.16 install script,
migrates its output using a real unified release archive, checks the migrated
profiles/developer sources, updates, uninstalls, and runs the old cleanup script.
Every home, XDG, and legacy install location is redirected into a temporary
directory which is removed at the end, including settings, icons, and backups.
It does not launch the old game.

```sh
PULSAR_TEST_LEGACY_BUNDLE=/path/to/extracted/PulsarForLinux-Native \
PULSAR_TEST_RELEASE_ARCHIVE=/path/to/pulsar-v2.4.0-linux-x64.tar.gz \
    dotnet test PulsarConfigTests/PulsarConfigTests.csproj -c Release
```

The reference bundle is the `PulsarForLinux-Native.*.7z` asset of
[linux-compat release 1.0.16](https://github.com/CometWorks/linux-compat/releases/tag/1.0.16).

## Appearance

Press **F2 Theme** to choose **Muted** or **Turbo C**. Switching applies immediately
and preserves the current form and operation. The choice is saved for both tools
in [machine-local user settings](../README.md#appearance), independently of the
Pulsar install and game profiles. Pulsar defaults to muted until a preference is
saved.
