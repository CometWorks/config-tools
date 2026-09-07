# CometWorks config tools

Standalone terminal tools for Pulsar and Magnetar, built with .NET 10 and
Terminal.Gui. Both offer muted slate and classic Turbo C / Turbo Vision themes.

## Downloads

Download from [Releases](https://github.com/CometWorks/config-tools/releases).
Each tool is **one self-contained executable**, including .NET, Terminal.Gui,
and their managed dependencies. No Python, NuGet, or separate .NET installation
is needed to run the tools. Pulsar uses the managed console driver (standard
bash/stty utilities on Linux). Magnetar keeps its existing native terminal
driver; on Linux it uses ncurses/terminfo, with `-netdriver` as the managed fallback.

| Tool | Release executable | Purpose |
| --- | --- | --- |
| PulsarConfig | `PulsarConfig-linux-x64.bin` | Configure plugins, sources, dev folders and profiles; launch, install, update or migrate Pulsar |
| MagnetarConfig | `MagnetarConfig-linux-x64.bin` | Install, update, configure and operate Magnetar on Linux |
| MagnetarConfig | `MagnetarConfig-win-x64.exe` | Install, update, configure and operate Magnetar on Windows |

On Linux, make the downloaded file executable and run it:

```sh
chmod +x PulsarConfig-linux-x64.bin
./PulsarConfig-linux-x64.bin
```

SHA-256 checksums and license notices accompany the executables. These bundles
contain the **configuration tools**; Pulsar/Magnetar and the games keep their
own runtime and platform prerequisites. Pulsar setup downloads game-launcher
packages from **SpaceGT/Pulsar**, not from this repository.

## Updating the tools

Both tools provide **Tools → Tool updates** to check for a newer stable release
and **Update and close** to install it. Checks are explicit; opening the tool
does not download or install updates automatically. For scripts:

```sh
./PulsarConfig-linux-x64.bin --check-update
./PulsarConfig-linux-x64.bin --self-update
./MagnetarConfig-linux-x64.bin --self-update
# Windows: .\MagnetarConfig-win-x64.exe --self-update
```

`--tool-version` prints the installed version. These switches update the **tool**;
`update --target ...` updates Pulsar or Magnetar. Release lookup filters each
tool's tags, so a newer MagnetarConfig release cannot hide a PulsarConfig update.
Choose the matching tool release on the releases page rather than using the
repository-wide `releases/latest/download` URL.

Updates require HTTPS access to GitHub and write access beside the executable.
The helper verifies GitHub's SHA-256 digest and waits for this tool to exit before
replacing it, including on Windows. Close other copies of the same tool first.
Both UI and CLI updates close the tool after staging. Reopen it with your usual
command after the helper finishes; it does not start another TUI in the background.
The previous executable remains as `<executable>.previous`; the result is written
to `<executable>.update.log`. Restore the previous file with the tool closed if
needed. No game/server files, launch arguments, profiles, or theme preferences
are changed. Source/development builds must be rebuilt rather than self-updated.

## Appearance

Use **F2 Theme** in PulsarConfig or **Tools → Theme** in MagnetarConfig to switch
between **Muted** and **Turbo C** immediately. The selection is shared by both
tools for your user account on this machine and survives tool updates:

- Linux: `$XDG_CONFIG_HOME/CometWorks/config-tools/theme`, or
  `~/.config/CometWorks/config-tools/theme` when XDG_CONFIG_HOME is unset.
- Windows: `%LOCALAPPDATA%\CometWorks\config-tools\theme`.

It is separate from game/server profiles and portable installation folders.
With no saved preference, both tools default to muted. Other running tool windows pick up the choice on their
next launch. Removing this file restores the defaults.

## Tool guides

- [PulsarConfig guide](Docs/PulsarConfig.md): plugin/source/profile editing, Steam launch, backups, launch options,
  SE1/SE2 selection, migration from LinuxCompat 1.0.x, and offline archives.
- [MagnetarConfig manual](Docs/MagnetarConfig.md): server/world settings, mods,
  plugins/profiles, lifecycle control, and logs.
- [MagnetarConfig internals](Docs/MagnetarConfigInternals.md).

Pulsar's Steam launch paths remain `Interim.bin %command%` for SE1 and
`Modern.bin %command%` for SE2. Setup preserves extra arguments and existing
profiles; it does not alter overlay or display-backend settings.

## Build and test

Building from source requires the .NET 10 SDK. Both tools use Terminal.Gui 1.19.0;
MagnetarConfig also bundles SharpCompress to read upstream `.7z` packages; restore downloads build dependencies, and
publishing bundles them for end users. Tests use xUnit.

```sh
dotnet test MagnetarConfigTests/MagnetarConfigTests.csproj -c Release
# Linux only:
dotnet test PulsarConfigTests/PulsarConfigTests.csproj -c Release
dotnet run --project PulsarConfig -- --help
dotnet run --project MagnetarConfig -- --help
```

Publish a standalone executable (use `win-x64` for MagnetarConfig on Windows):

```sh
dotnet publish PulsarConfig/PulsarConfig.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist/PulsarConfig
dotnet publish MagnetarConfig/MagnetarConfig.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist/MagnetarConfig
```

Native libraries are included in the bundle; trimming is disabled to preserve
Terminal.Gui and XML serializer reflection. Runtime discovery uses
`AppContext.BaseDirectory`, so renamed single-file executables work correctly.
Each tool's **`Directory.Build.props`** defines its `<Version>`:

- [`PulsarConfig/Directory.Build.props`](PulsarConfig/Directory.Build.props)
- [`MagnetarConfig/Directory.Build.props`](MagnetarConfig/Directory.Build.props)

Bump the relevant version and merge to `main`. CI evaluates the project through
MSBuild, tests/publishes the executables, and creates `pulsarconfig-vX.Y.Z` or
`magnetarconfig-vX.Y.Z` from that configured version. It retains an already
complete release when the version has not changed; bump the version to ship
changed binaries. Tags cannot override the version in the project properties.
A matching tag on the current `main` commit can still trigger an individual tool
release, or use **Run workflow → tool** on `main` to select either tool separately.
PRs and manual runs on other branches produce `-dev` artifacts without publishing.

CI maintains **at most one public release per tool**. It uploads the replacement
as a draft and verifies every asset's SHA-256 digest before changing visibility.
Previous releases for that tool become drafts; their assets and Git tags remain
available to maintainers. The other tool's release is untouched. Publishing is
serialized per tool, and stale builds cannot replace the current `main` release.
If promotion fails, CI checks the actual remote state before restoring the
previous public release. GitHub's visibility changes are separate API calls, so
there is a brief interval with no public release during the switch.

Stable versions use three numeric components (`X.Y.Z`); prereleases are excluded
from self-update. The updater and compatibility bootstrap discover the remaining
public release in each tool's tag stream.

For the same single-file checks and update-helper smoke test locally:

```sh
pwsh Scripts/publish.ps1 -Tool PulsarConfig -Rid linux-x64
pwsh Scripts/publish.ps1 -Tool MagnetarConfig -Rid linux-x64
```

## Magnetar integration

MagnetarConfig is downloaded independently from this repository's releases.
Magnetar's server build and bundles do not include or depend on the TUI.
Place the executable beside a Magnetar launcher for adjacent-file discovery,
or pass `-magnetar`, `-config`, and `-path` when keeping it elsewhere.

Building and publishing config-tools never deploys into a live installation.
No game assemblies or Magnetar checkout are required. Set `MAGNETAR_SHARED`
for Magnetar tests or `PULSAR_SHARED` for Pulsar tests to an existing
`Pulsar.Shared.dll` to exercise optional serializer interoperability checks.

## Origins

Pulsar setup was introduced in [SpaceGT/Pulsar PR #54](https://github.com/SpaceGT/Pulsar/pull/54)
and ported from the standard-library Python installer here. Its old
[`pulsar-linux.py`](Scripts/pulsar-linux.py) URL remains a compatibility
downloader; the release executable is the recommended entry point.

MagnetarConfig, its tests, manuals, and Windows icon were extracted from
[CometWorks/magnetar at d2e0bee](https://github.com/CometWorks/magnetar/tree/d2e0beed3f7aaac3a340e1f00e8e507e42ae265a/MagnetarConfig).
See [LICENSE](LICENSE) and [third-party notices](Licenses/README.md).
