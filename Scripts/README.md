# Pulsar Linux setup

The installer has moved to the C# project [`PulsarConfig`](../PulsarConfig).
Use its self-contained executable from [config-tools releases](https://github.com/CometWorks/config-tools/releases).
See the [setup guide](../Docs/PulsarConfig.md).

`pulsar-linux.py` is retained only as a small compatibility downloader for old
links. It verifies and starts the released executable, forwarding the same CLI
arguments. New installations can download the executable directly and need no
Python. It does not contain a second installer implementation.
