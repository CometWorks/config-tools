using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Pulsar.Config;

internal sealed class Installer
{
    internal const string Repo = "SpaceGT/Pulsar";
    internal static readonly HashSet<string> ProgramFiles = new(
        new[] { "Libraries", "LICENSE", "README.md", "pulsar-linux.py" }.Concat(
            new[] { "Interim", "Modern" }.SelectMany(name =>
                new[] { ".bin", ".dll", ".deps.json", ".runtimeconfig.json" }.Select(suffix =>
                    name + suffix
                )
            )
        )
    );
    internal static readonly string[] Required =
    [
        "Interim.bin",
        "Interim.dll",
        "Interim.runtimeconfig.json",
        "Libraries/Interim/Pulsar.Shared.dll",
    ];
    internal static readonly string[] Se2Required =
    [
        "Modern.bin",
        "Modern.dll",
        "Modern.runtimeconfig.json",
        "Libraries/Modern/Pulsar.Shared.dll",
    ];
    private static readonly Dictionary<string, string> CoreIds = new()
    {
        ["se-linux-compat"] = "linux-compat",
        ["se-dotnet-compat"] = "dotnet-compat",
    };
    private readonly Options options;
    private readonly Action<string> report;
    internal Action<string, byte[]> Write = Files.Write;
    internal readonly string Target,
        StateDir,
        Receipt,
        Desktop;
    internal string Game;

    internal static bool Modern(string path) =>
        Required.All(name => File.Exists(Path.Combine(path, name)));

    internal static bool Legacy(string path) =>
        File.Exists(Path.Combine(path, "Interim"))
        && File.Exists(Path.Combine(path, "Bin/Interim"));

    public Installer(Options options, Action<string> report)
    {
        this.options = options;
        this.report = report;
        Target = Files.InstallPath(options.Target);
        StateDir = Path.Combine(Files.Xdg("XDG_STATE_HOME", ".local/state"), "pulsar-installer");
        Receipt = Path.Combine(StateDir, Files.HashText(Target)[..20] + ".json");
        Desktop = Path.Combine(Files.DataHome, "applications/pulsar.desktop");
        Game = options.Game;
    }

    internal string LaunchOptions =>
        Files.Quote(Path.Combine(Target, Game == "se2" ? "Modern.bin" : "Interim.bin"))
        + " %command%";
    private string GameName => Game == "se2" ? "Space Engineers 2" : "Space Engineers 1";
    internal string DesktopContents =>
        $"[Desktop Entry]\nType=Application\nName=Pulsar — {GameName}\nComment={GameName} with Pulsar\nExec=steam -applaunch {(Game == "se2" ? "1133870" : "244850")}\nIcon=applications-games\nTerminal=false\nCategories=Game;\nX-Pulsar-Install-Path={Target}\n";

    private void Validate(string action)
    {
        Files.RequireStopped(Target);
        bool known = Modern(Target) || Legacy(Target) || File.Exists(Receipt);
        if (
            Directory.Exists(Target)
            && Directory.EnumerateFileSystemEntries(Target).Any()
            && !known
        )
            throw new SetupError("This non-empty folder is not a recognized Pulsar installation.");
        if (action == "install" && (Modern(Target) || Legacy(Target)))
            throw new SetupError("Pulsar is already installed. Choose Update or Migrate.");
        if (action == "update" && !Modern(Target))
            throw new SetupError(
                "Choose Migrate for an old LinuxCompat installation, or Install for a new folder."
            );
        if (action == "uninstall" && !known)
            throw new SetupError("No recognized Pulsar installation exists at this path.");
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
                    @"\Apulsar-.*-linux-x64\.tar\.gz\z",
                    RegexOptions.IgnoreCase
                )
            )
            .ToArray();
        if (assets.Length != 1)
            throw new SetupError(
                "This release has no unique Linux x64 package. Try another release tag."
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
        if (!Modern(destination))
            throw new SetupError(
                "Not a unified Pulsar Linux package: required launcher files are missing."
            );
        return digest;
    }

    internal static void CopySettings(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new SetupError($"Legacy settings directory does not exist: {source}");
        if (new DirectoryInfo(destination).LinkTarget is not null)
            throw new SetupError("Destination settings must not be a symbolic link.");
        if (OperatingSystem.IsLinux())
            Directory.CreateDirectory(
                destination,
                File.GetUnixFileMode(source)
                    | UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
            );
        else
            Directory.CreateDirectory(destination);
        foreach (string name in new[] { "config.xml", "Sources", "Profiles", "Local" })
        {
            string src = Path.Combine(source, name),
                dest = Path.Combine(destination, name);
            if (!Files.Exists(src))
                continue;
            if (Files.Exists(dest))
                throw new SetupError(
                    $"Migration would overwrite existing settings: {name}. Use an empty destination."
                );
            if (Directory.Exists(src))
                Files.CopyTree(
                    src,
                    dest,
                    preserveLinks: name == "Local",
                    skipCaches: name == "Sources"
                );
            else
                File.Copy(src, dest);
        }
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
    }

    internal static void MigrateSettings(string root, Action<string> report)
    {
        var profiles = Directory.Exists(Path.Combine(root, "Profiles"))
            ? Directory
                .GetFiles(Path.Combine(root, "Profiles"), "*.xml")
                .Select(p => (path: p, xml: XDocument.Load(p)))
                .ToArray()
            : [];
        var manifests = new Dictionary<string, string>();
        foreach (var (_, xml) in profiles)
        foreach (var item in xml.Descendants("LocalFolderConfig"))
            if (
                (string?)item.Element("Id") is { } id
                && (string?)item.Element("DataFile") is { Length: > 0 } manifest
            )
                manifests[id] = manifest;
        var remap = new Dictionary<string, string>(CoreIds);
        string sourcesFile = Path.Combine(root, "Sources/sources.xml");
        if (File.Exists(sourcesFile))
        {
            var sources = XDocument.Load(sourcesFile);
            foreach (var hub in sources.Descendants("RemoteHub"))
            {
                hub.Element("Hash")?.Remove();
                hub.Element("LastCheck")?.Remove();
            }
            foreach (var item in sources.Descendants("LocalPlugin"))
            {
                string folder = Files.FullPath((string?)item.Element("Folder") ?? ".");
                string oldId = Path.GetFileName(folder);
                string? manifest = (string?)item.Element("File");
                if (string.IsNullOrEmpty(manifest))
                    manifest =
                        manifests.GetValueOrDefault(oldId)
                        ?? manifests.GetValueOrDefault((string?)item.Element("Name") ?? "");
                string? newId = null;
                if (!string.IsNullOrEmpty(manifest))
                {
                    item.SetElementValue("File", manifest);
                    string path = Path.Combine(folder, manifest);
                    if (File.Exists(path))
                        newId = (string?)XDocument.Load(path).Root?.Element("Id");
                    if (!string.IsNullOrEmpty(newId))
                        remap[oldId] = newId;
                }
                if (CoreIds.TryGetValue(oldId, out string? coreId) && newId != coreId)
                {
                    remap[oldId] = coreId;
                    item.SetElementValue("Enabled", "false");
                    report($"Using the current released core plugin: {coreId}");
                }
                else if (string.IsNullOrEmpty(newId))
                    report(
                        $"Check developer source '{oldId}': its plugin XML manifest is missing or has no ID."
                    );
            }
            Files.Write(sourcesFile, Encoding.UTF8.GetBytes(sources.ToString()));
        }
        foreach (var (path, profile) in profiles)
        {
            foreach (var item in profile.Descendants("LocalFolderConfig").ToArray())
            {
                string id = (string?)item.Element("Id") ?? "";
                item.Element("DataFile")?.Remove();
                if (CoreIds.ContainsKey(id))
                    item.Remove();
                else if (remap.TryGetValue(id, out string? newId))
                    item.SetElementValue("Id", newId);
            }
            foreach (var item in profile.Descendants("GitHubPluginConfig"))
                if (remap.TryGetValue((string?)item.Element("Id") ?? "", out string? newId))
                    item.SetElementValue("Id", newId);
            Files.Write(path, Encoding.UTF8.GetBytes(profile.ToString()));
        }
    }

    internal void Commit(string stage, byte[] state, string? desktop, bool oldShortcut)
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
                    oldShortcut
                    || Encoding
                        .UTF8.GetString(oldDesktop)
                        .Contains($"X-Pulsar-Install-Path={Target}\n", StringComparison.Ordinal)
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

    private void CleanIcons()
    {
        foreach (int size in new[] { 16, 24, 32, 48, 64, 96, 128, 256 })
        {
            string icon = Path.Combine(
                Files.DataHome,
                $"icons/hicolor/{size}x{size}/apps/pulsar.png"
            );
            if (!File.Exists(icon))
                continue;
            try
            {
                Files.Write(
                    Path.Combine(StateDir, "legacy-icons", size.ToString(), "pulsar.png"),
                    File.ReadAllBytes(icon)
                );
                File.Delete(icon);
            }
            catch (IOException error)
            {
                report($"Installed; could not clean old icon {icon}: {error.Message}");
            }
            catch (UnauthorizedAccessException error)
            {
                report($"Installed; could not clean old icon {icon}: {error.Message}");
            }
        }
    }

    public async Task Run(string action, CancellationToken token = default)
    {
        if (action is not ("install" or "update" or "migrate" or "uninstall"))
            throw new SetupError("Unknown setup action.");
        using var operationLock = Files.Lock(StateDir);
        Validate(action);
        if (Game == "auto")
        {
            using var receipt = JsonDocument.Parse(
                File.Exists(Receipt) ? File.ReadAllText(Receipt) : "{}"
            );
            Game = receipt.RootElement.TryGetProperty("game", out var game)
                ? game.GetString() ?? "se1"
                : "se1";
        }
        if (Game is not ("se1" or "se2"))
            throw new SetupError("Unknown saved game selection; choose --game se1 or se2.");
        string old =
            action == "migrate" && options.Source is not null
                ? Files.InstallPath(options.Source)
                : Target;
        string settings = Files.RealPath(options.Settings ?? Files.OldConfig);
        string oldCommand = "Exec=" + Path.Combine(old, "Interim");
        bool oldShortcut =
            Legacy(old)
            && File.Exists(Desktop)
            && File.ReadLines(Desktop)
                .Any(line =>
                    line == oldCommand
                    || line.StartsWith(oldCommand + " ", StringComparison.Ordinal)
                );
        if (action == "migrate")
        {
            if (!Legacy(old))
                throw new SetupError(
                    "Legacy source must contain the 1.0.x Interim wrapper and Bin/Interim."
                );
            Files.RequireStopped(old);
            if (old != Target && (Files.Contains(old, Target) || Files.Contains(Target, old)))
                throw new SetupError("Source and destination must not contain one another.");
        }
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
                    archive = Path.Combine(work, "release.tar.gz");
                    (version, expected) = await Download(version, archive, token);
                }
                report("Verifying and unpacking the Linux release…");
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
            if (Legacy(stage))
            {
                if (action is not ("migrate" or "uninstall"))
                    throw new SetupError("This is a legacy installation. Choose Migrate.");
                Files.Remove(Path.Combine(stage, "Bin"));
                Files.Remove(Path.Combine(stage, "Interim"));
            }
            if (action == "migrate")
            {
                CopySettings(settings, Path.Combine(stage, "Legacy"));
                MigrateSettings(Path.Combine(stage, "Legacy"), report);
            }
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
            Files.RequireStopped(Target, old);
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
            Commit(stage, state, action == "uninstall" ? null : DesktopContents, oldShortcut);
            if (oldShortcut)
                CleanIcons();
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
            if (action == "migrate")
            {
                report(
                    $"Original settings retained at {settings}; old caches were not transferred."
                );
                if (old != Target)
                    report(
                        $"Old program files retained at {old}. Remove them after checking the new installation."
                    );
            }
        }
        finally
        {
            Files.Remove(work);
        }
    }
}
