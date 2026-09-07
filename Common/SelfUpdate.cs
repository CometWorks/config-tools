#nullable enable
// An empty assembly Location intentionally distinguishes published bundles from development builds.
#pragma warning disable IL3000
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CometWorks.ConfigTools;

internal sealed record ToolRelease(string Tag, Version Version, string Url, string Sha256);

/// <summary>Only replaces this tool's executable. Game/server installation is a separate operation.</summary>
internal static class SelfUpdate
{
    internal const string Repo = "CometWorks/config-tools";
    internal static string Tool => typeof(SelfUpdate).Assembly.GetName().Name!;
    internal static string VersionText =>
        typeof(SelfUpdate)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];

    internal static string Prefix(string tool) => tool.ToLowerInvariant() + "-v";

    internal static string Asset(string tool) =>
        tool + (OperatingSystem.IsWindows() ? "-win-x64.exe" : "-linux-x64.bin");

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CometWorks-ConfigTools/1.0");
        return client;
    }

    internal static ToolRelease? Select(JsonElement releases, string tool, string asset)
    {
        ToolRelease? best = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (
                release.GetProperty("draft").GetBoolean()
                || release.GetProperty("prerelease").GetBoolean()
            )
                continue;
            string tag = release.GetProperty("tag_name").GetString()!;
            string prefix = Prefix(tool);
            if (
                !tag.StartsWith(prefix, StringComparison.Ordinal)
                || !Regex.IsMatch(tag[prefix.Length..], @"\A\d+\.\d+\.\d+\z")
                || !Version.TryParse(tag[prefix.Length..], out var version)
            )
                continue;
            var assets = release
                .GetProperty("assets")
                .EnumerateArray()
                .Where(a => a.GetProperty("name").GetString() == asset)
                .ToArray();
            if (assets.Length != 1 || (best != null && version <= best.Version))
                continue;
            string hash = assets[0].TryGetProperty("digest", out var digest)
                ? digest.GetString() ?? ""
                : "";
            string url = assets[0].GetProperty("browser_download_url").GetString()!;
            // A missing checksum is reported when selecting the newest release, never downgraded past.
            best = new ToolRelease(
                tag,
                version,
                url,
                hash.StartsWith("sha256:", StringComparison.Ordinal) ? hash[7..] : ""
            );
        }
        return best;
    }

    public static async Task<ToolRelease?> Check(CancellationToken token = default)
    {
        if (
            RuntimeInformation.OSArchitecture != Architecture.X64
            || !(OperatingSystem.IsLinux() || OperatingSystem.IsWindows())
        )
            throw new InvalidOperationException(
                "Tool releases currently support Linux x64 and Windows x64."
            );
        ToolRelease? best = null;
        // Separate tag streams: the repository's /releases/latest may belong to the other tool.
        for (int page = 1; page <= 10; page++)
        {
            using var json = JsonDocument.Parse(
                await Http.GetStringAsync(
                    $"https://api.github.com/repos/{Repo}/releases?per_page=100&page={page}",
                    token
                )
            );
            var candidate = Select(json.RootElement, Tool, Asset(Tool));
            if (candidate != null && (best == null || candidate.Version > best.Version))
                best = candidate;
            if (json.RootElement.GetArrayLength() < 100)
                break;
            if (page == 10)
                throw new IOException(
                    "Release history exceeds the update lookup limit; check the repository's releases page."
                );
        }
        if (best == null)
            throw new IOException(
                $"No stable {Tool} release is published yet. See https://github.com/{Repo}/releases."
            );
        var current = Version.Parse(VersionText.Split('-')[0]);
        return best.Version > current || (best.Version == current && VersionText.Contains('-'))
            ? best
            : null;
    }

    internal static void Verify(string path, string hash)
    {
        if (!Regex.IsMatch(hash, @"\A[0-9a-fA-F]{64}\z"))
            throw new IOException("Release has no valid SHA-256 digest.");
        using var input = File.OpenRead(path);
        if (
            !Convert
                .ToHexString(SHA256.HashData(input))
                .Equals(hash, StringComparison.OrdinalIgnoreCase)
        )
            throw new IOException(
                "Downloaded executable failed SHA-256 verification. The installed tool was not changed."
            );
    }

    internal static async Task Download(
        ToolRelease release,
        string destination,
        CancellationToken token
    )
    {
        string expected =
            $"https://github.com/{Repo}/releases/download/{release.Tag}/{Asset(Tool)}";
        if (release.Url != expected || !Regex.IsMatch(release.Sha256, @"\A[0-9a-fA-F]{64}\z"))
            throw new IOException("Release URL or SHA-256 digest is missing or invalid.");
        using var response = await Http.GetAsync(
            release.Url,
            HttpCompletionOption.ResponseHeadersRead,
            token
        );
        response.EnsureSuccessStatusCode();
        using (var input = await response.Content.ReadAsStreamAsync(token))
        using (var output = new FileStream(destination, FileMode.CreateNew))
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            byte[] buffer = new byte[1024 * 1024];
            long total = 0;
            while (true)
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                int count = await input.ReadAsync(buffer, timeout.Token);
                if (count == 0)
                    break;
                total += count;
                if (total > 256L * 1024 * 1024)
                    throw new IOException("Tool download exceeds 256 MiB.");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
        }
        Verify(destination, release.Sha256);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                destination,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
            );
    }

    internal sealed record UpdatePlan(int Parent, long Started, string Target, string Hash);

    // Linux Process.StartTime derives a wall clock from uptime separately in each process.
    // /proc start ticks are stable across parent/helper and also prevent PID reuse races.
    private static long StartStamp(Process process) =>
        OperatingSystem.IsLinux()
            ? long.Parse(
                File.ReadAllText($"/proc/{process.Id}/stat").Split(") ")[^1].Split(' ')[19],
                System.Globalization.CultureInfo.InvariantCulture
            )
            : process.StartTime.ToUniversalTime().Ticks;

    public static async Task Prepare(ToolRelease release, CancellationToken token = default)
    {
        // Framework-dependent development builds must be updated through source/build, not replace dotnet itself.
        if (!string.IsNullOrEmpty(typeof(SelfUpdate).Assembly.Location))
            throw new InvalidOperationException(
                "Self-update requires a published single-file executable. Rebuild this development checkout instead."
            );
        string target =
            Environment.ProcessPath
            ?? throw new IOException("Cannot determine this tool's executable path.");
        target = Path.GetFullPath(new FileInfo(target).ResolveLinkTarget(true)?.FullName ?? target);
        string work = Path.Combine(
            Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + "-update-" + Guid.NewGuid().ToString("N")
        );
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(work);
        else
            Directory.CreateDirectory(
                work,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        try
        {
            string package = Path.Combine(work, "download");
            await Download(release, package, token);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(package, File.GetUnixFileMode(target) & (UnixFileMode)0x1ff);
            string helper = Path.Combine(
                work,
                OperatingSystem.IsWindows() ? "helper.exe" : "helper"
            );
            File.Copy(package, helper);
            using var parent = Process.GetCurrentProcess();
            var plan = new UpdatePlan(parent.Id, StartStamp(parent), target, release.Sha256);
            string planPath = Path.Combine(work, "plan.json");
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
            token.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(helper)
            {
                UseShellExecute = false,
                WorkingDirectory = work,
            };
            start.ArgumentList.Add("--apply-self-update");
            start.ArgumentList.Add(planPath);
            using var child =
                Process.Start(start) ?? throw new IOException("Could not start update helper.");
            for (int attempt = 0; !File.Exists(Path.Combine(work, "ready")); attempt++)
            {
                if (child.HasExited || attempt >= 100)
                    throw new IOException(
                        "Update helper did not start. The installed executable was not changed."
                    );
                await Task.Delay(100, CancellationToken.None);
            }
            // Helper is now responsible for cleanup. Caller exits normally, flushing settings first.
        }
        catch
        {
            Directory.Delete(work, true);
            throw;
        }
    }

    internal static void Replace(string staged, string target, string hash)
    {
        Verify(staged, hash);
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Update target changed into a link; refusing replacement.");
        string backup = target + ".previous";
        File.Move(target, backup, true);
        try
        {
            File.Move(staged, target);
        }
        catch
        {
            File.Move(backup, target, true);
            throw;
        }
    }

    private static async Task<int> Apply(string planPath)
    {
        string work = Path.GetDirectoryName(Path.GetFullPath(planPath))!;
        UpdatePlan? plan = null;
        bool validated = false;
        try
        {
            plan =
                JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath))
                ?? throw new IOException("Invalid update plan.");
            // GetFullPath also expands Windows short names (e.g. RUNNER~1). Normalize
            // every operand so the helper accepts the same file through either spelling.
            plan = plan with
            {
                Target = Path.GetFullPath(plan.Target),
            };
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (
                !string.Equals(
                    Path.GetDirectoryName(work),
                    Path.GetDirectoryName(plan.Target),
                    comparison
                )
                || !Path.GetFileName(work)
                    .StartsWith(
                        "." + Path.GetFileName(plan.Target) + "-update-",
                        StringComparison.Ordinal
                    )
            )
                throw new IOException(
                    $"Invalid update staging directory: {work} (target: {plan.Target})."
                );
            if (
                !string.Equals(
                    Path.GetDirectoryName(Path.GetFullPath(Environment.ProcessPath!)),
                    work,
                    comparison
                ) || !File.Exists(Path.Combine(work, "download"))
            )
                throw new IOException(
                    $"Update helper must run from its staging directory: {Environment.ProcessPath} (staging: {work})."
                );
            validated = true;
            File.WriteAllText(Path.Combine(work, "ready"), "ready");
            try
            {
                using var parent = Process.GetProcessById(plan.Parent);
                if (StartStamp(parent) == plan.Started)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    await parent.WaitForExitAsync(timeout.Token);
                }
            }
            catch (Exception error)
                when (error
                        is ArgumentException
                            or InvalidOperationException
                            or FileNotFoundException
                            or DirectoryNotFoundException
                )
            { /* parent already exited */
            }
            // Serialize concurrent helpers even when several tool windows were open.
            using (
                var updateLock = new FileStream(
                    plan.Target + ".update.lock",
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                )
            )
            {
                Replace(Path.Combine(work, "download"), plan.Target, plan.Hash);
                File.WriteAllText(
                    plan.Target + ".update.log",
                    "Tool update installed successfully. Previous executable: "
                        + plan.Target
                        + ".previous\n"
                );
            }
            return 0;
        }
        catch (Exception error)
        {
            string message = "Tool update stopped: " + error.Message;
            Console.Error.WriteLine(message);
            if (validated)
                try
                {
                    File.WriteAllText(plan!.Target + ".update.log", message);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            return 1;
        }
        finally
        {
            // Windows cannot delete its running helper. The next tool launch removes the staging directory.
            if (validated)
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(work, "finished"),
                        Environment.ProcessId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    );
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (!OperatingSystem.IsWindows())
                    try
                    {
                        Directory.Delete(work, true);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void Cleanup()
    {
        string? target = Environment.ProcessPath;
        if (target == null || !string.IsNullOrEmpty(typeof(SelfUpdate).Assembly.Location))
            return;
        foreach (
            string work in Directory.EnumerateDirectories(
                Path.GetDirectoryName(target)!,
                "." + Path.GetFileName(target) + "-update-*"
            )
        )
        {
            // Never remove an active download/helper or follow a directory link.
            if (
                (File.GetAttributes(work) & FileAttributes.ReparsePoint) != 0
                || !File.Exists(Path.Combine(work, "finished"))
            )
                continue;
            try
            {
                if (
                    !int.TryParse(
                        File.ReadAllText(Path.Combine(work, "finished")),
                        out int helperPid
                    )
                )
                    continue;
                try
                {
                    using var helper = Process.GetProcessById(helperPid);
                    if (!helper.HasExited)
                        continue;
                }
                catch (ArgumentException) { }
                Directory.Delete(work, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static async Task<int?> Handle(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--apply-self-update")
                return await Apply(args[1]);
            try
            {
                Cleanup();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (
                args.Length != 1
                || args[0] is not ("--self-update" or "--check-update" or "--tool-version")
            )
                return null;
            Console.WriteLine($"{Tool} {VersionText}");
            if (args[0] == "--tool-version")
                return 0;
            var release = await Check();
            Console.WriteLine(
                release == null ? "Already up to date." : $"Available: {release.Tag}"
            );
            if (release != null && args[0] == "--self-update")
            {
                await Prepare(release);
                Console.WriteLine(
                    "Verified update staged. The helper will replace this executable after exit; see the adjacent .update.log for the result."
                );
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Tool update: " + error.Message);
            return 1;
        }
    }
}
