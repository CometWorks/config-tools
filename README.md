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
| PulsarConfig | `PulsarConfig-linux-x64.bin` | Install, update, uninstall, or migrate native Pulsar; SE1 and SE2 selection |
| MagnetarConfig | `MagnetarConfig-linux-x64.bin` | Configure and operate one Magnetar server on Linux |
| MagnetarConfig | `MagnetarConfig-win-x64.exe` | Configure and operate one Magnetar server on Windows |

On Linux, make the downloaded file executable and run it:

```sh
chmod +x PulsarConfig-linux-x64.bin
./PulsarConfig-linux-x64.bin
```

SHA-256 checksums and license notices accompany the executables. These bundles
contain the **configuration tools**; Pulsar/Magnetar and the games keep their
own runtime and platform prerequisites. Pulsar setup downloads game-launcher
packages from **SpaceGT/Pulsar**, not from this repository.

## Appearance

Use **F2 Theme** in PulsarConfig or **Tools → Theme** in MagnetarConfig to switch
between **Muted** and **Turbo C** immediately. The selection is shared by both
tools for your user account on this machine and survives tool updates:

- Linux: `$XDG_CONFIG_HOME/CometWorks/config-tools/theme`, or
  `~/.config/CometWorks/config-tools/theme` when XDG_CONFIG_HOME is unset.
- Windows: `%LOCALAPPDATA%\CometWorks\config-tools\theme`.

It is separate from game/server profiles and portable installation folders.
With no saved preference, Pulsar defaults to muted and Magnetar to its original
Turbo Vision appearance. Other running tool windows pick up the choice on their
next launch. Removing this file restores the defaults.

## Tool guides

- [PulsarConfig setup guide](Docs/PulsarConfig.md): backups, Steam launch options,
  SE1/SE2 selection, migration from LinuxCompat 1.0.x, and offline archives.
- [MagnetarConfig manual](Docs/MagnetarConfig.md): server/world settings, mods,
  plugins/profiles, lifecycle control, and logs.
- [MagnetarConfig internals](Docs/MagnetarConfigInternals.md).

Pulsar's Steam launch paths remain `Interim.bin %command%` for SE1 and
`Modern.bin %command%` for SE2. Setup preserves extra arguments and existing
profiles; it does not alter overlay or display-backend settings.

## Build and test

Building from source requires the .NET 10 SDK. Each tool has one direct runtime
NuGet dependency, Terminal.Gui 1.19.0; restore downloads build dependencies, and
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
The release workflow tests on Linux/Windows, publishes the three executables,
and creates a GitHub release when a `v*` tag is pushed. Main/PR builds upload
artifacts without publishing a release.

## Magnetar integration

Magnetar consumes this repository as its pinned `ConfigTools` submodule.
Its solution references the same MagnetarConfig source and tests. Existing
Magnetar bundles retain `MagnetarConfig.bin` / `MagnetarConfig.exe` at the install
root with managed files and `Libraries/MagnetarConfig/` dependencies.

Standalone builds/tests do not deploy into a live install. To stage the existing
split layout explicitly:

```sh
dotnet build MagnetarConfig/MagnetarConfig.csproj -c Release -p:Magnetar=/path/to/stage
```

config-tools deliberately has no root `Directory.Build.props`: embedded
projects inherit Magnetar's version/deployment settings, while standalone builds
need no game assemblies or Magnetar checkout. Set `MAGNETAR_SHARED` to a staged
`Pulsar.Shared.dll` to exercise optional serializer interoperability tests.

## Origins

Pulsar setup was introduced in [SpaceGT/Pulsar PR #54](https://github.com/SpaceGT/Pulsar/pull/54)
and ported from the standard-library Python installer here. Its old
[`pulsar-linux.py`](Scripts/pulsar-linux.py) URL remains a compatibility
downloader; the release executable is the recommended entry point.

MagnetarConfig, its tests, manuals, and Windows icon were extracted from
[CometWorks/magnetar at d2e0bee](https://github.com/CometWorks/magnetar/tree/d2e0beed3f7aaac3a340e1f00e8e507e42ae265a/MagnetarConfig).
See [LICENSE](LICENSE) and [third-party notices](Licenses/README.md).
