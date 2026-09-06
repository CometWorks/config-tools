# Pulsar Linux setup

The installer has moved to the C# project [`PulsarConfig`](../PulsarConfig).
Use its self-contained executable from [config-tools releases](https://github.com/CometWorks/config-tools/releases).
See the [setup guide](../Docs/PulsarConfig.md).

`pulsar-linux.py` is retained only as a small compatibility downloader for old
links. It verifies and starts the released executable, forwarding the same CLI
arguments. New installations can download the executable directly and need no
Python. It does not contain a second installer implementation.

The downloader selects stable `pulsarconfig-vX.Y.Z` releases even when the latest
repository release belongs to MagnetarConfig. Both executables provide their own
`--self-update`; Python is only needed for this legacy bootstrap or build-time
smoke tests, not for using or updating the tools.

`publish.ps1 -Tool PulsarConfig|MagnetarConfig -Rid linux-x64|win-x64` produces a
self-contained single executable and runs `test-self-update.py` on a matching
host OS. See the root README for independent release tags.
