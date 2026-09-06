# CometWorks config tools

Standalone tools for configuring and managing CometWorks installations.

## Pulsar Linux setup

Install, update, uninstall, or migrate an older native LinuxCompat installation
with a terminal UI or explicit command-line actions. Requires Linux x64 and
Python 3.10+; the terminal UI uses the standard-library `curses` module.

```sh
curl -fLO https://raw.githubusercontent.com/CometWorks/config-tools/main/Scripts/pulsar-linux.py
python3 pulsar-linux.py
```

Run as your normal Steam user, without sudo. The tool downloads Pulsar packages
from [SpaceGT/Pulsar](https://github.com/SpaceGT/Pulsar/releases), not this repository.

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
