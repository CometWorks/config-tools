#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CometWorks.ConfigTools;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace Magnetar.Config.Install;

internal sealed class Installer
{
    private const string Repository = "CometWorks/magnetar";
    private static readonly HttpClient Http = CreateHttp();
    private readonly InstallOptions options;
    private readonly Action<string> report;
    internal readonly string Target, StateDirectory, Receipt;
    internal Action<string, byte[]> WriteReceipt = InstallFiles.Write;
    private static readonly string[] Managed = ["MagnetarInterim.bin", "MagnetarInterim.exe", "MagnetarInterim.dll",
        "MagnetarInterim.deps.json", "MagnetarInterim.runtimeconfig.json", "MagnetarLegacy.exe", "MagnetarLegacy.exe.config",
        "Libraries/MagnetarInterim", "Libraries/MagnetarLegacy", "Libraries/Compiler", "LICENSE", "README.md"];
    private static readonly HashSet<string> BundledTool = new(StringComparer.OrdinalIgnoreCase)
        { "MagnetarConfig.bin", "MagnetarConfig.exe", "MagnetarConfig.dll", "MagnetarConfig.deps.json", "MagnetarConfig.runtimeconfig.json" };

    public Installer(InstallOptions options, Action<string> report, string? stateDirectory = null)
    {
        this.options = options;
        this.report = report;
        Target = InstallFiles.TargetPath(options.Target);
        string state = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } xdg) state = xdg;
        StateDirectory = InstallFiles.RealPath(stateDirectory ?? Path.Combine(state, "config-tools", "magnetar-installer"));
        if (InstallFiles.Contains(Target, StateDirectory)) throw new InstallError("Installation folder must not contain the setup state directory.");
        string key = OperatingSystem.IsWindows() ? Target.ToUpperInvariant() : Target;
        Receipt = Path.Combine(StateDirectory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20] + ".json");
    }

    internal static bool IsPortable(string target) => File.Exists(Path.Combine(target, "MagnetarInterim.dll"))
        && File.Exists(Path.Combine(target, "MagnetarInterim.runtimeconfig.json"))
        && (File.Exists(Path.Combine(target, "MagnetarInterim.bin")) || File.Exists(Path.Combine(target, "MagnetarInterim.exe")));

    private static bool IsLegacyLinux(string target) => File.Exists(Path.Combine(target, "MagnetarInterim"))
        && File.Exists(Path.Combine(target, "Bin/MagnetarInterim"));

    private bool HasReceipt()
    {
        if (!File.Exists(Receipt)) return false;
        using var document = JsonDocument.Parse(File.ReadAllBytes(Receipt));
        return document.RootElement.TryGetProperty("target", out var target)
            && string.Equals(target.GetString(), Target, InstallFiles.Comparison);
    }

    public async Task Run(string action, CancellationToken cancellation = default)
    {
        if (action == "check") { report(Prerequisites.Magnetar(options.Ds64)); return; }
        if (action is not ("install" or "update" or "uninstall")) throw new InstallError("Choose install, update, or uninstall.");
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsWindows()) || RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new InstallError("Magnetar packages support Linux x64 and Windows x64.");
        InstallFiles.PrivateFolder(StateDirectory);
        using var operationLock = AcquireLock();
        cancellation.ThrowIfCancellationRequested();
        if (IsLegacyLinux(Target)) throw new InstallError("This is an older Linux Bin/wrapper installation. Automatic migration is not supported; install the current release in a new folder and retain your existing -config and -path directories.");
        bool portable = IsPortable(Target), known = portable || HasReceipt();
        if (Directory.Exists(Target) && Directory.EnumerateFileSystemEntries(Target).Any() && !known)
            throw new InstallError("This non-empty folder is not a recognized portable Magnetar installation.");
        if (action == "install" && portable) throw new InstallError("Magnetar is already installed. Choose Update.");
        if (action == "update" && !portable) throw new InstallError("No portable Magnetar installation was found. Choose Install for a new folder.");
        if (action == "uninstall" && !known) throw new InstallError("No recognized Magnetar installation was found.");
        ServerGuard.RequireStopped(Target);
        if (action != "uninstall" && options.CheckDependencies) report(Prerequisites.Magnetar(options.Ds64));

        string parent = Path.GetDirectoryName(Target)!;
        Directory.CreateDirectory(parent);
        string work = Path.Combine(parent, "." + Path.GetFileName(Target) + "-setup-" + Guid.NewGuid().ToString("N"));
        InstallFiles.PrivateFolder(work);
        try
        {
            string package = Path.Combine(work, "package"), stage = Path.Combine(work, "install");
            string version = options.Version, checksum = "";
            if (action != "uninstall")
            {
                string archive;
                string? expected = options.Sha256;
                if (!string.IsNullOrWhiteSpace(options.Archive))
                {
                    archive = InstallFiles.FullPath(options.Archive);
                    if (version == "latest") version = Path.GetFileName(archive);
                }
                else
                {
                    archive = Path.Combine(work, "release.7z");
                    (version, expected) = await Download(version, archive, cancellation);
                }
                report("Verifying and unpacking the Magnetar release…");
                checksum = Unpack(archive, package, expected, cancellation);
            }
            report("Preparing installation; current files remain in place…");
            if (Directory.Exists(Target)) InstallFiles.CopyTree(Target, stage, cancellation);
            else InstallFiles.PrivateFolder(stage);
            // Libraries is shared with independently installed tools. Replace only server-owned subdirectories.
            if (new DirectoryInfo(Path.Combine(stage, "Libraries")).LinkTarget is not null)
                throw new InstallError("The installation's Libraries folder is a symbolic link. Use a regular directory before setup.");
            foreach (string path in Managed) InstallFiles.Remove(Path.Combine(stage, path));
            if (action != "uninstall")
            {
                foreach (string path in Managed)
                {
                    string source = Path.Combine(package, path), dest = Path.Combine(stage, path);
                    if (!InstallFiles.Exists(source)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (Directory.Exists(source)) Directory.Move(source, dest);
                    else File.Move(source, dest);
                }
            }
            byte[] receipt = JsonSerializer.SerializeToUtf8Bytes(new { target = Target, installed = action != "uninstall", version, sha256 = checksum });
            cancellation.ThrowIfCancellationRequested();
            ServerGuard.RequireStopped(Target);
            Commit(stage, receipt);
            report(action == "uninstall" ? $"Magnetar program files removed. Configurations, worlds, tools, and unrelated files remain in {Target}."
                : $"Magnetar {version} installed in {Target}.");
            if (action != "uninstall")
            {
                report("Keep your existing -config and -path arguments when launching an updated server.");
                if (OperatingSystem.IsWindows()) report("Both Windows launcher variants are installed.");
            }
        }
        finally { InstallFiles.Remove(work); }
    }

    private FileStream AcquireLock()
    {
        try { return new FileStream(Path.Combine(StateDirectory, "setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new InstallError($"Another Magnetar setup operation may be running: {error.Message}"); }
    }

    internal void Commit(string stage, byte[] receipt)
    {
        byte[]? oldReceipt = File.Exists(Receipt) ? File.ReadAllBytes(Receipt) : null;
        string? backup = null;
        bool switched = false;
        try
        {
            if (Directory.Exists(Target))
            {
                backup = Path.Combine(Path.GetDirectoryName(Target)!, "." + Path.GetFileName(Target) + "-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                Directory.Move(Target, backup);
            }
            Directory.Move(stage, Target);
            switched = true;
            WriteReceipt(Receipt, receipt);
            if (backup is not null && oldReceipt is not null)
                InstallFiles.Write(Path.Combine(StateDirectory, "backups", Path.GetFileName(backup), "receipt.json"), oldReceipt);
        }
        catch
        {
            if (switched) InstallFiles.Remove(Target);
            if (backup is not null && Directory.Exists(backup)) Directory.Move(backup, Target);
            if (oldReceipt is null) File.Delete(Receipt); else InstallFiles.Write(Receipt, oldReceipt);
            throw;
        }
        if (backup is not null) report($"Previous installation retained at {backup}");
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CometWorks-MagnetarConfig/1.0");
        return http;
    }

    private async Task<(string version, string checksum)> Download(string version, string destination, CancellationToken token)
    {
        if (!Regex.IsMatch(version, @"\A(?:latest|v?\d+\.\d+\.\d+(?:[-.][A-Za-z0-9]+)*)\z"))
            throw new InstallError("Use latest or a release tag such as v2.4.0.0.");
        report($"Checking {Repository} releases…");
        string endpoint = version == "latest" ? "latest" : "tags/" + version;
        using var json = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/{endpoint}", token));
        string platform = OperatingSystem.IsWindows() ? "Windows" : "Linux";
        var assets = json.RootElement.GetProperty("assets").EnumerateArray().Where(a => Regex.IsMatch(a.GetProperty("name").GetString()!, @"\AMagnetarFor" + platform + @"-[0-9][A-Za-z0-9.\-]*\.7z\z")).ToArray();
        if (assets.Length != 1) throw new InstallError($"This release does not provide a unique Magnetar {platform} package.");
        string digest = assets[0].TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, @"\Asha256:[a-fA-F0-9]{64}\z")) throw new InstallError("GitHub did not provide the package's SHA-256 checksum.");
        string url = assets[0].GetProperty("browser_download_url").GetString()!;
        if (!url.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.Ordinal)) throw new InstallError("Unexpected release download URL.");
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = File.Create(destination);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        byte[] buffer = new byte[1024 * 1024];
        long total = 0;
        while (true)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            int count = await input.ReadAsync(buffer, timeout.Token);
            if (count == 0) break;
            total += count;
            if (total > 512L * 1024 * 1024) throw new InstallError("Package exceeds the 512 MiB download limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
        return (json.RootElement.GetProperty("tag_name").GetString()!, digest[7..]);
    }

    internal static string Unpack(string path, string destination, string? expected, CancellationToken token = default)
    {
        if (new FileInfo(path).Length > 512L * 1024 * 1024) throw new InstallError("Package exceeds the 512 MiB archive limit.");
        string hash = InstallFiles.Hash(path);
        if (expected is not null && (!Regex.IsMatch(expected, @"\A[a-fA-F0-9]{64}\z") || !hash.Equals(expected, StringComparison.OrdinalIgnoreCase)))
            throw new InstallError("Package checksum mismatch. Installation was not changed.");
        InstallFiles.PrivateFolder(destination);
        using var archive = SevenZipArchive.OpenArchive(path);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            string relative = EntryPath(entry);
            if (!names.Add(relative) || names.Count > 10000) throw new InstallError($"Duplicate entry or excessive archive entries: {entry.Key}");
            total = checked(total + entry.Size);
            if (entry.Size < 0 || total > 2L * 1024 * 1024 * 1024) throw new InstallError("Package exceeds the 2 GiB extraction limit.");
        }
        // Official 7z releases are solid: stream in archive order instead of decompressing per file.
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            token.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            string relative = EntryPath(entry);
            if (relative == "" || BundledTool.Contains(relative) || relative == "Libraries/MagnetarConfig" || relative.StartsWith("Libraries/MagnetarConfig/", StringComparison.Ordinal)) continue;
            string outputPath = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            if (entry.IsDirectory) Directory.CreateDirectory(outputPath);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                using var input = reader.OpenEntryStream();
                using (var output = new FileStream(outputPath, FileMode.CreateNew))
                {
                    byte[] buffer = new byte[65536];
                    long written = 0;
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        token.ThrowIfCancellationRequested();
                        written += count;
                        if (written > entry.Size) throw new InstallError("Archive entry exceeds its declared size.");
                        output.Write(buffer, 0, count);
                    }
                    if (written != entry.Size) throw new InstallError("Archive entry is truncated.");
                }
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(outputPath,
                    relative.EndsWith(".bin", StringComparison.Ordinal) ? (UnixFileMode)493 : (UnixFileMode)420);
            }
        }
        string[] required = ["MagnetarInterim.dll", "MagnetarInterim.deps.json", "MagnetarInterim.runtimeconfig.json", "LICENSE", "Libraries/MagnetarInterim/Pulsar.Shared.dll",
            OperatingSystem.IsWindows() ? "MagnetarInterim.exe" : "MagnetarInterim.bin",
            OperatingSystem.IsWindows() ? "Libraries/Compiler/Compiler.exe" : "Libraries/Compiler/Compiler.bin"];
        if (OperatingSystem.IsWindows()) required = [.. required, "MagnetarLegacy.exe", "MagnetarLegacy.exe.config", "Libraries/MagnetarLegacy/Pulsar.Shared.dll"];
        else required = [.. required, "Libraries/MagnetarInterim/Steamworks.NET.dll", "Libraries/MagnetarInterim/libsteam_api.so"];
        if (required.Any(name => !File.Exists(Path.Combine(destination, name))))
            throw new InstallError("Not a current portable Magnetar package for this platform: required launcher files are missing. Older Bin/wrapper releases require manual migration.");
        return hash;
    }

    private static string EntryPath(IEntry entry)
    {
        string name = entry.Key ?? "";
        int unixType = ((entry.Attrib ?? 0) >> 16) & 0xf000;
        if (entry.IsEncrypted || entry.LinkTarget is not null || ((entry.Attrib ?? 0) & (int)FileAttributes.ReparsePoint) != 0
            || (unixType != 0 && unixType != 0x8000 && unixType != 0x4000)
            || name.Contains('\\') || name.Contains(':') || name.Any(char.IsControl))
            throw new InstallError($"Unsafe archive entry: {name}");
        string[] parts = name.TrimEnd('/').Split('/');
        if (parts.Length == 0 || parts[0] != "Magnetar" || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InstallError($"Unsupported archive path: {name}. Use a current portable Magnetar release.");
        if (parts.Length == 1 && entry.IsDirectory) return "";
        string relative = string.Join('/', parts.Skip(1));
        bool allowed = Managed.Contains(relative) || BundledTool.Contains(relative) || relative == "Libraries" || relative == "Libraries/MagnetarConfig"
            || Managed.Where(p => p.StartsWith("Libraries/", StringComparison.Ordinal)).Any(p => relative.StartsWith(p + "/", StringComparison.Ordinal))
            || relative.StartsWith("Libraries/MagnetarConfig/", StringComparison.Ordinal);
        if (!allowed) throw new InstallError($"Unexpected archive entry: {name}");
        return relative;
    }
}
