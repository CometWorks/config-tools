using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pulsar.Config;

internal sealed class Installer
{
    internal const string Repo = "SpaceGT/Pulsar";
    internal static readonly HashSet<string> ProgramFiles = new(
        new[] { "Libraries", "LICENSE", "README.md", "pulsar-linux.py", "Legacy.exe", "Legacy.exe.config" }.Concat(
            new[] { "Interim", "Modern" }.SelectMany(name =>
                new[] { ".bin", ".exe", ".dll", ".deps.json", ".runtimeconfig.json" }.Select(suffix =>
                    name + suffix
                )
            )
        ), Files.Comparer
    );
    internal static string Launcher(string game) =>
        (game == "se2" ? "Modern" : "Interim") + (OperatingSystem.IsWindows() ? ".exe" : ".bin");
    internal static readonly string[] Required =
    [
        Launcher("se1"),
        "Interim.dll",
        "Interim.runtimeconfig.json",
        "Libraries/Interim/Pulsar.Shared.dll",
        .. OperatingSystem.IsWindows() ? new[] { "Legacy.exe", "Legacy.exe.config",
            "Libraries/Legacy/Pulsar.Shared.dll", "Libraries/Compiler/Compiler.exe", "Libraries/Interface/Interface.exe" } : [],
    ];
    internal static readonly string[] Se2Required =
    [
        Launcher("se2"),
        "Modern.dll",
        "Modern.runtimeconfig.json",
        "Libraries/Modern/Pulsar.Shared.dll",
    ];
    private readonly Options options;
    private readonly Action<string> report;
    internal Action<string, byte[]> Write = Files.Write;
    internal readonly string Target,
        StateDir,
        Receipt,
        Desktop;
    internal string Game;

    internal static bool Modern(string path) =>
        Required.All(name => File.Exists(Path.Combine(path, name)))
        || Se2Required.All(name => File.Exists(Path.Combine(path, name)));

    internal static bool Legacy(string path) =>
        File.Exists(Path.Combine(path, "Interim"))
        && File.Exists(Path.Combine(path, "Bin/Interim"));

    public Installer(Options options, Action<string> report, string? stateRoot = null, string? desktopPath = null)
    {
        this.options = options;
        this.report = report;
        Target = Files.InstallPath(options.Target);
        StateDir = stateRoot ?? InstallationDiscovery.StateDirectory;
        Receipt = Path.Combine(StateDir, Files.HashText(OperatingSystem.IsWindows() ? Target.ToUpperInvariant() : Target)[..20] + ".json");
        Desktop = desktopPath ?? (OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Pulsar.url")
            : Path.Combine(Files.DataHome, "applications/pulsar.desktop"));
        Game = options.Game;
    }

    internal string LaunchOptions =>
        Files.Quote(Path.Combine(Target, Launcher(Game)))
        + " %command%";
    private string GameName => Game == "se2" ? "Space Engineers 2" : "Space Engineers 1";
    internal string DesktopContents => OperatingSystem.IsWindows()
        ? $"[InternetShortcut]\r\nURL=steam://rungameid/{(Game == "se2" ? "1133870" : "244850")}\r\nX-Pulsar-Install-Path={Target}\r\n"
        : $"[Desktop Entry]\nType=Application\nName=Pulsar — {GameName}\nComment={GameName} with Pulsar\nExec=steam -applaunch {(Game == "se2" ? "1133870" : "244850")}\nIcon=applications-games\nTerminal=false\nCategories=Game;\nX-Pulsar-Install-Path={Target}\n";

    private void Validate(string action)
    {
        if (OperatingSystem.IsWindows() &&
            (string.Equals(Target, Files.FullPath(AppContext.BaseDirectory), Files.Comparison)
             || Files.Contains(Target, Files.FullPath(AppContext.BaseDirectory))))
            throw new SetupError("Run PulsarConfig from outside the installation folder before changing it on Windows.");
        Files.RequireStopped(Target);
        if (Legacy(Target))
            throw new SetupError("This older Linux layout is no longer supported. Install the current release in a new folder; retain your existing files and settings.");
        var installation = InstallationDiscovery.Create(StateDir).Inspect(Target);
        if (action == "install" && !installation.CanInstall)
            throw new SetupError(installation.CanUpdate ? "Pulsar is already installed. Choose Update." : installation.Status);
        if (action == "update" && !installation.CanUpdate)
            throw new SetupError("No current Pulsar installation was found. " + installation.Status);
        if (action == "uninstall" && !installation.CanUninstall)
            throw new SetupError("No complete Pulsar installation was found. " + installation.Status);
    }

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CometWorks-PulsarConfig/1.0");
        return client;
    }

    internal async Task<(string version, string hash)> Download(
        string version,
        string destination,
        CancellationToken token
    )
    {
        if (!Regex.IsMatch(version, @"\A(?:latest|v?\d+\.\d+\.\d+(?:[-.][A-Za-z0-9]+)*)\z"))
            throw new SetupError("Use 'latest' or a release tag such as v2.4.1.");
        report($"Checking {Repo} releases…");
        string endpoint = version == "latest" ? "latest" : "tags/" + version;
        using var json = JsonDocument.Parse(
            await Http.GetStringAsync(
                $"https://api.github.com/repos/{Repo}/releases/{endpoint}",
                token
            )
        );
        var release = json.RootElement;
        var assets = release
            .GetProperty("assets")
            .EnumerateArray()
            .Where(a =>
                Regex.IsMatch(
                    a.GetProperty("name").GetString()!,
                    OperatingSystem.IsWindows()
                        ? @"\Apulsar-.*-win-x64\.zip\z"
                        : @"\Apulsar-.*-linux-x64\.tar\.gz\z",
                    RegexOptions.IgnoreCase
                )
            )
            .ToArray();
        if (assets.Length != 1)
            throw new SetupError(
                $"This release has no unique {(OperatingSystem.IsWindows() ? "Windows" : "Linux")} x64 package. Try another release tag."
            );
        string digest = assets[0].TryGetProperty("digest", out var hash)
            ? hash.GetString() ?? ""
            : "";
        if (!Regex.IsMatch(digest, @"\Asha256:[0-9a-fA-F]{64}\z"))
            throw new SetupError("GitHub did not provide a SHA-256 digest for this asset.");
        string url = assets[0].GetProperty("browser_download_url").GetString()!;
        if (
            !url.StartsWith(
                $"https://github.com/{Repo}/releases/download/",
                StringComparison.Ordinal
            )
        )
            throw new SetupError("Unexpected release download URL.");
        string tag = release.GetProperty("tag_name").GetString()!;
        using var response = await Http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            token
        );
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = File.Create(destination);
        byte[] buffer = new byte[1024 * 1024];
        long total = 0,
            shown = -1;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        while (true)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            int size = await input.ReadAsync(buffer, timeout.Token);
            if (size == 0)
                break;
            await output.WriteAsync(buffer.AsMemory(0, size), token);
            total += size;
            if (total > 512L * 1024 * 1024)
                throw new SetupError("Release archive exceeds the 512 MiB download limit.");
            if (total / 1048576 != shown)
            {
                shown = total / 1048576;
                report($"Downloading {tag} · {shown} MiB");
            }
        }
        return (tag, digest[7..]);
    }

    internal static string Unpack(
        string archive,
        string destination,
        string? expected,
        CancellationToken token = default
    )
    {
        string digest = Files.Hash(archive);
        if (expected is not null && !digest.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new SetupError("Package checksum mismatch. Installation was not changed.");
        if (OperatingSystem.IsWindows())
        {
            UnpackZip(archive, destination, token);
            if (!Required.All(name => File.Exists(Path.Combine(destination, name))))
                throw new SetupError("Not a Pulsar Windows package: required launcher files are missing.");
            return digest;
        }
        using var input = File.OpenRead(archive);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        long total = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (tar.GetNextEntry() is { } entry)
        {
            token.ThrowIfCancellationRequested();
            string[] parts = entry
                .Name.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p != ".")
                .ToArray();
            if (parts.Length == 0 && entry.EntryType == TarEntryType.Directory)
                continue;
            if (
                entry.Name.StartsWith('/')
                || parts.Contains("..")
                || parts.Length == 0
                || !ProgramFiles.Contains(parts[0])
                || entry.EntryType
                    is not (
                        TarEntryType.Directory
                        or TarEntryType.RegularFile
                        or TarEntryType.V7RegularFile
                    )
            )
                throw new SetupError($"Unsafe or unexpected archive entry: {entry.Name}");
            if (!names.Add(string.Join('/', parts)))
                throw new SetupError($"Duplicate archive entry: {entry.Name}");
            total = checked(total + entry.Length);
            if (total > 2L * 1024 * 1024 * 1024)
                throw new SetupError("Package exceeds the 2 GiB extraction limit.");
            string path = Path.Combine(destination, Path.Combine(parts));
            if (entry.EntryType == TarEntryType.Directory)
                Directory.CreateDirectory(path);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var output = new FileStream(path, FileMode.CreateNew))
                    entry.DataStream?.CopyTo(output);
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(
                        path,
                        (UnixFileMode)(
                            (entry.Mode & (UnixFileMode)73) != 0
                            || path.EndsWith(".bin", StringComparison.Ordinal)
                            || path.EndsWith("pulsar-linux.py", StringComparison.Ordinal)
                                ? 493
                                : 420
                        )
                    );
            }
        }
        if (!Required.All(name => File.Exists(Path.Combine(destination, name))))
            throw new SetupError(
                "Not a unified Pulsar Linux package: required launcher files are missing."
            );
        return digest;
    }

    internal static void UnpackZip(string archive, string destination, CancellationToken token = default)
    {
        using var zip = ZipFile.OpenRead(archive);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            bool directory = name.EndsWith('/');
            string[] parts = name.TrimEnd('/').Split('/');
            if (parts.Any(part => string.IsNullOrEmpty(part) || part is "." or ".."
                    || part.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c))
                    || part.EndsWith('.') || part.EndsWith(' ')
                    || Regex.IsMatch(part, @"\A(?:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase))
                || !ProgramFiles.Contains(parts[0])
                || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || !names.Add(string.Join('/', parts)))
                throw new SetupError($"Unsafe or duplicate ZIP entry: {entry.FullName}");
            string path = Path.Combine(destination, Path.Combine(parts));
            if (directory)
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew);
            byte[] buffer = new byte[81920];
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                token.ThrowIfCancellationRequested();
                total = checked(total + count);
                if (total > 2L * 1024 * 1024 * 1024)
                    throw new SetupError("Package exceeds the 2 GiB extraction limit.");
                output.Write(buffer, 0, count);
            }
        }
    }

    internal void Commit(string stage, byte[] state, string? desktop)
    {
        string? backup = null;
        bool switched = false;
        byte[]? oldState = File.Exists(Receipt) ? File.ReadAllBytes(Receipt) : null;
        byte[]? oldDesktop = File.Exists(Desktop) ? File.ReadAllBytes(Desktop) : null;
        try
        {
            if (Directory.Exists(Target))
            {
                backup = Path.Combine(
                    Path.GetDirectoryName(Target)!,
                    $".{Path.GetFileName(Target)}-backup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"
                );
                Directory.Move(Target, backup);
            }
            Directory.Move(stage, Target);
            switched = true;
            Write(Receipt, state);
            if (desktop is not null)
                Write(Desktop, Encoding.UTF8.GetBytes(desktop));
            else if (
                oldDesktop is not null
                && (
                    Encoding
                        .UTF8.GetString(oldDesktop)
                        .Replace("\r\n", "\n")
                        .Contains($"X-Pulsar-Install-Path={Target}\n", Files.Comparison)
                )
            )
                File.Delete(Desktop);
            if (backup is not null)
            {
                string record = Path.Combine(StateDir, "backups", Path.GetFileName(backup));
                if (oldDesktop is not null)
                    Files.Write(Path.Combine(record, "pulsar.desktop"), oldDesktop);
                if (oldState is not null)
                    Files.Write(Path.Combine(record, "receipt.json"), oldState);
            }
        }
        catch
        {
            if (switched)
                Files.Remove(Target);
            if (backup is not null && Directory.Exists(backup))
                Directory.Move(backup, Target);
            if (oldState is null)
                File.Delete(Receipt);
            else
                Files.Write(Receipt, oldState);
            if (oldDesktop is null)
                File.Delete(Desktop);
            else
                Files.Write(Desktop, oldDesktop);
            throw;
        }
        if (backup is not null)
            report($"Previous installation backed up at {backup}");
    }

    internal string CheckPrerequisites()
    {
        ResolveGame();
        return CometWorks.ConfigTools.Prerequisites.Pulsar(Game);
    }

    private void ResolveGame()
    {
        if (Game == "auto")
        {
            Game = InstallationDiscovery.ResolveGame(Target, Receipt);
        }
        if (Game is not ("se1" or "se2"))
            throw new SetupError("Unknown saved game selection; choose --game se1 or se2.");
    }

    public async Task Run(string action, CancellationToken token = default)
    {
        if (action is not ("install" or "update" or "uninstall"))
            throw new SetupError("Unknown setup action.");
        using var operationLock = Files.Lock(StateDir);
        Validate(action);
        ResolveGame();
        if (action != "uninstall")
            report(CometWorks.ConfigTools.Prerequisites.Pulsar(Game));
        string parent = Path.GetDirectoryName(Target)!;
        Directory.CreateDirectory(parent);
        string work = Path.Combine(parent, $".{Path.GetFileName(Target)}-setup-{Guid.NewGuid():N}");
        if (OperatingSystem.IsLinux())
            Directory.CreateDirectory(
                work,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        else
            Directory.CreateDirectory(work);
        try
        {
            string package = Path.Combine(work, "package"),
                stage = Path.Combine(work, "install");
            Directory.CreateDirectory(package);
            string version = options.Version,
                checksum = "";
            if (action != "uninstall")
            {
                string archive;
                string? expected = options.Sha256;
                if (options.Archive is not null)
                {
                    archive = Files.FullPath(options.Archive);
                    if (version == "latest")
                        version = Path.GetFileName(archive);
                }
                else
                {
                    archive = Path.Combine(work, OperatingSystem.IsWindows() ? "release.zip" : "release.tar.gz");
                    (version, expected) = await Download(version, archive, token);
                }
                report("Verifying and unpacking the release…");
                checksum = Unpack(archive, package, expected, token);
                if (
                    Game == "se2"
                    && !Se2Required.All(name => File.Exists(Path.Combine(package, name)))
                )
                    throw new SetupError(
                        "This package is missing the Space Engineers 2 Modern launcher files."
                    );
            }
            report("Preparing installation; your current files remain in place…");
            if (Directory.Exists(Target))
                Files.CopyTree(Target, stage);
            else
                Directory.CreateDirectory(stage);
            foreach (string name in ProgramFiles)
                Files.Remove(Path.Combine(stage, name));
            if (action != "uninstall")
                foreach (string entry in Directory.EnumerateFileSystemEntries(package))
                {
                    string dest = Path.Combine(stage, Path.GetFileName(entry));
                    if (Directory.Exists(entry))
                        Directory.Move(entry, dest);
                    else
                        File.Move(entry, dest);
                }
            token.ThrowIfCancellationRequested();
            Files.RequireStopped(Target);
            byte[] state = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    target = Target,
                    installed = action != "uninstall",
                    game = Game,
                    version = action == "uninstall" ? null : version,
                    sha256 = checksum,
                }
            );
            // Cancellation stops before this point. Never interrupt a switch halfway through.
            Commit(stage, state, action == "uninstall" ? null : DesktopContents);
            if (action == "uninstall")
            {
                report($"Program files removed. Settings and other files remain in {Target}");
                report(
                    "Remove Pulsar launch options from both games if configured; keep your game arguments."
                );
            }
            else
            {
                report($"Pulsar installed in {Target}");
                report($"Set {GameName} launch options in Steam to:");
                report(LaunchOptions);
                report(
                    "Keep extra game/Pulsar arguments after %command%. The menu shortcut starts Steam."
                );
                report(
                    "Pulsar itself still requires the .NET 10 runtime; the setup tool's bundled runtime is private to this executable."
                );
            }
        }
        finally
        {
            Files.Remove(work);
        }
    }
}
