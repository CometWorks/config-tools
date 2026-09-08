# CometWorks config tools

Standalone terminal tools for configuring and managing Pulsar and Magnetar.
Both support **Windows x64 and Linux x64**, with installation management, profiles,
plugin configuration, dependency checks and self-updates.

## Downloads

Choose **PulsarConfig** or **MagnetarConfig** from [Releases](https://github.com/CometWorks/config-tools/releases).

| System | Download | Start |
| --- | --- | --- |
| Windows | The tool’s `-win-x64.exe` | Run the executable. |
| Linux | The tool’s `-linux-x64.bin` | Make it executable with `chmod +x filename`, then run `./filename`. |

Each download is a single executable with its runtime and libraries included.
No separate .NET, Python or NuGet installation is needed. Open the tool to choose
an installation or manage an existing one; `--help` lists command-line options.
On Windows, keep the tool outside the installation folder when installing,
updating or uninstalling it, because Windows locks running executables.

## Updating the tools

Each launch checks for a newer stable release in the background. Choose **Update…**
when prompted, or **Later** to continue. The check times out after eight seconds;
network failures never block startup. You can also open **Tool updates** manually.

**Update and close** downloads and verifies the executable, keeps the previous
version as a backup, and closes the tool so its helper can replace it. Close other
windows of the same tool first, then reopen it normally. The update does not alter
game/server files, launch arguments, profiles or the saved theme.

Use `--check-update` to check from the command line, or `--self-update` to install
an update. Each tool follows its own release tags, so updates to one never hide
updates to the other. Choose the tool’s release rather than the repository-wide
`releases/latest/download` URL. Source builds must be rebuilt.

If the installation directory is not writable, move the executable to a writable
folder or download the replacement manually. Keep the `.previous` backup until
the new version works; with the tool closed, it can be restored over the executable.

## Appearance

Both tools default to **Sandstone**. **Graphite**, **Sage**, **Plum** and **Turbo C**
are also available. Quiet themes use the console’s default background, including
transparency; Turbo C retains the terminal’s classic 16-colour palette.

The theme is shared by both tools for your account on this machine:

- Windows: `%LOCALAPPDATA%\CometWorks\config-tools\theme`.
- Linux: `$XDG_CONFIG_HOME/CometWorks/config-tools/theme`, defaulting to
  `~/.config/CometWorks/config-tools/theme`.

The setting survives updates and is separate from installation folders and
profiles. Other open windows pick up the change on their next launch.

## Tool guides

- [PulsarConfig guide](Docs/PulsarConfig.md): navigation, Steam launch, plugins,
  sources, dev folders, profiles, installation and migration.
- [MagnetarConfig manual](Docs/MagnetarConfig.md): installation, server/world
  settings, mods, plugins, profiles, lifecycle control and logs.
- [MagnetarConfig internals](Docs/MagnetarConfigInternals.md).

The tools are released separately from the applications they manage. Their bundled
runtime runs the configuration tool; applications and games retain their own
prerequisites. Setup reports missing dependencies before installation.

## Build and test

Install the .NET 10 SDK. Both projects use Terminal.Gui 1.19 and xUnit tests.
Select a tool and runtime; the same commands apply to either project:

```powershell
$Tool = 'PulsarConfig' # or 'MagnetarConfig'
$Rid = 'win-x64'      # or 'linux-x64'
dotnet test "$($Tool)Tests/$($Tool)Tests.csproj" -c Release
./Scripts/publish.ps1 -Tool $Tool -Rid $Rid
```

Run the publish script with PowerShell (`pwsh`) on Linux. It creates a self-contained,
single-file executable, bundles native libraries and tests self-replacement on
the matching host OS. Trimming is disabled to preserve reflection. No game
assemblies or other checkout are required, and builds never deploy into live
installations. `PULSAR_SHARED` or `MAGNETAR_SHARED` can point tests at an existing
`Pulsar.Shared.dll` for optional serializer checks.

## Releases

Each project’s `Directory.Build.props` controls its version:
[PulsarConfig](PulsarConfig/Directory.Build.props),
[MagnetarConfig](MagnetarConfig/Directory.Build.props).
Bump that version and merge to `main`. CI tests both operating systems and publishes
both executables under the tool’s `X.Y.Z` tag. An unchanged version keeps its
existing release assets. Matching tags or **Run workflow → tool** can release one
tool separately; PRs and non-main manual runs produce development artifacts.

CI keeps **one release per tool**. It uploads a new candidate as a draft, verifies
asset SHA-256 digests, publishes it, and then **deletes superseded releases and
assets**, including historical drafts. Old releases never return to draft.
Git tags remain for source history. Separate API calls can briefly expose both
versions during the switch.

Publishing is serialized per tool and stale builds cannot replace the current
`main` version. A failed publication preserves the previous release; failed
cleanup leaves the replacement live and fails CI. Rerunning retries cleanup
without replacing published assets. Prereleases are excluded from self-update.

## Origins

Pulsar setup originated in [SpaceGT/Pulsar PR #54](https://github.com/SpaceGT/Pulsar/pull/54).
The old [Python bootstrap](Scripts/pulsar-linux.py) remains a compatibility downloader.
MagnetarConfig was extracted from
[CometWorks/magnetar](https://github.com/CometWorks/magnetar/tree/d2e0beed3f7aaac3a340e1f00e8e507e42ae265a/MagnetarConfig).
See [LICENSE](LICENSE) and [third-party notices](Licenses/README.md).
