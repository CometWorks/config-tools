# CometWorks config tools

Standalone tools for configuring and managing CometWorks installations.

## Pulsar Linux setup

Install, update, or uninstall the unified Pulsar package for **Space Engineers 1
and 2**, or migrate an older native SE1 LinuxCompat installation, with a terminal
UI or explicit command-line actions. Requires Linux x64 and
Python 3.10+; the terminal UI uses the standard-library `curses` module.

```sh
curl -fLO https://raw.githubusercontent.com/CometWorks/config-tools/main/Scripts/pulsar-linux.py
python3 pulsar-linux.py
```

Use **Game** in the TUI to choose the Steam shortcut and launch command:
**SE1 → `Interim.bin`**, **SE2 → `Modern.bin`**. Both launchers are installed from
the shared package; the selection does not remove the other game's support.
For example, `python3 pulsar-linux.py --game se2` starts setup with SE2 selected.
Updates remember the selection; new installs default to SE1.

Run as your normal Steam user, without sudo. The tool downloads Pulsar packages
from [SpaceGT/Pulsar](https://github.com/SpaceGT/Pulsar/releases), not this repository.
This script manages native Linux installs. For Windows setup, use the
[Windows installer](https://github.com/StarCpt/Pulsar-Installer).

See the [Linux setup guide](Scripts/README.md) for prerequisites, backups,
Steam launch options, migration, and offline use.

## Development

```sh
python3 -m unittest discover -s Tests/LinuxInstaller -v
```

The real-release migration integration test is optional; its fixture setup is
documented in the Linux setup guide. Tests use temporary installation directories
and do not modify your live Pulsar installation.

The Linux setup tool was moved from
[SpaceGT/Pulsar PR #54](https://github.com/SpaceGT/Pulsar/pull/54).
Licensed under [MIT](LICENSE).
