# Linux setup

`pulsar-linux.py` is a single executable Python 3.10+ script. It uses Python's
standard library, including curses for a terminal UI with muted slate colors.
There is no pip installation, UI toolkit download, or sudo step. Linux x64 is
supported for **Space Engineers 1 and 2**; Steam, the chosen game, GPU drivers, and the .NET 10 runtime are
prerequisites for running the game. Setup reports a missing runtime without
installing system packages.

Download the script from this repository. It is maintained separately from
Pulsar release packages. From a terminal:

```sh
curl -fLO https://raw.githubusercontent.com/CometWorks/config-tools/main/Scripts/pulsar-linux.py
python3 pulsar-linux.py
```

Use the arrow keys and Enter to select an action. Location changes the target
folder; Release selects `latest` or a tag such as `v2.4.0`. **Game** cycles between
the saved/default choice, SE1 (`Interim.bin`), and SE2 (`Modern.bin`). Migration also asks
for the old binary and settings folders. The UI confirms the operation before
changing files. Run without sudo, as the user who runs Steam.

The unified package supplies both launchers. Game selection chooses the menu
shortcut and displayed Steam command, not a different package. Updates preserve
the saved choice; a new installation defaults to SE1. Use `--game se2` to select
SE2 directly. SE2 setup rejects packages missing the Modern launcher before
replacing any existing installation. The script is Linux-only; Pulsar itself
also supports Windows through its Windows packages and installer.

## Install, update, and uninstall

The default location is `$XDG_DATA_HOME/Pulsar`, normally
`~/.local/share/Pulsar`. A script placed inside an installed Pulsar directory
defaults to that installation instead. Keep the script outside that directory
to avoid package replacement removing it. `PULSAR_DATA_DIR` or `--target` overrides it. Updates
keep the chosen path, profiles, custom plugins, and other user files. Existing
portable unified Pulsar installations can also be updated. Download the script
again from config-tools to update the tool itself. Older unified
packages published by linux-compat (for example 2.3.3) use Update.

The script downloads the Linux x64 archive from **SpaceGT/Pulsar**, verifies
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
containing spaces are quoted. The script does not rewrite Steam's configuration
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

An explicit action with `--yes` uses the same implementation without curses:

```sh
python3 pulsar-linux.py install --target "$HOME/Games/Pulsar" --yes
python3 pulsar-linux.py install --game se2 --target "$HOME/Games/Pulsar" --yes
python3 pulsar-linux.py update --target "$HOME/Games/Pulsar" --version v2.4.0 --yes
python3 pulsar-linux.py migrate --target "$HOME/.local/share/Pulsar" \
    --settings "$HOME/.config/Pulsar" --yes
python3 pulsar-linux.py uninstall --target "$HOME/Games/Pulsar" --yes
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
python3 -m unittest discover -s Tests/LinuxInstaller -v
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
    python3 -m unittest discover -s Tests/LinuxInstaller -v
```

The reference bundle is the `PulsarForLinux-Native.*.7z` asset of
[linux-compat release 1.0.16](https://github.com/CometWorks/linux-compat/releases/tag/1.0.16).
