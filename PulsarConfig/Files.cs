using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Pulsar.Config;

internal static class Files
{
    public static string Home =>
        Environment.GetEnvironmentVariable("HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Xdg(string variable, string fallback) =>
        FullPath(Environment.GetEnvironmentVariable(variable) ?? Path.Combine(Home, fallback));

    internal static readonly StringComparison Comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static readonly StringComparer Comparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static string DataHome => OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        : Xdg("XDG_DATA_HOME", ".local/share");
    public static string ConfigHome => OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        : Xdg("XDG_CONFIG_HOME", ".config");
    public static string StateHome => OperatingSystem.IsWindows()
        ? Path.Combine(DataHome, "CometWorks", "config-tools")
        : Xdg("XDG_STATE_HOME", ".local/state");
    public static string OldConfig =>
        Environment.GetEnvironmentVariable("PULSAR_DIR") is { Length: > 0 } custom
            ? FullPath(custom)
            : new[]
            {
                Path.Combine(ConfigHome, "Pulsar"),
                Path.Combine(Home, ".local/config/Pulsar"),
            }.FirstOrDefault(Directory.Exists)
                ?? Path.Combine(ConfigHome, "Pulsar");

    public static string FullPath(string value)
    {
        if (value == "~")
            value = Home;
        else if (value.StartsWith("~/", StringComparison.Ordinal))
            value = Path.Combine(Home, value[2..]);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    public static string RealPath(string value)
    {
        string full = FullPath(value),
            current = Path.GetPathRoot(full)!;
        foreach (
            string part in full[current.Length..]
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
        )
        {
            current = Path.Combine(current, part);
            var info = new DirectoryInfo(current);
            if (info.LinkTarget is not null)
                current = info.ResolveLinkTarget(true)!.FullName;
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    public static string InstallPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            throw new SetupError(
                "Choose a non-empty installation path without control characters."
            );
        if (new DirectoryInfo(FullPath(value)).LinkTarget is not null)
            throw new SetupError("Choose the real installation folder, not a symbolic link.");
        string path = RealPath(value);
        if (
            new[] { Path.GetPathRoot(path)!, Home, DataHome, ConfigHome, OldConfig, Path.GetTempPath() }
                .Select(RealPath)
                .Contains(path, Comparer)
        )
            throw new SetupError(
                "Choose a dedicated Pulsar folder, not a home/configuration root."
            );
        return path;
    }

    public static bool Exists(string path) =>
        Path.Exists(path) || new FileInfo(path).LinkTarget is not null;

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string HashText(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string Quote(string path) =>
        OperatingSystem.IsWindows() ? "\"" + path + "\"" :
        System.Text.RegularExpressions.Regex.IsMatch(path, @"^[a-zA-Z0-9_@%+=:,./-]+$")
            ? path
            : "'" + path.Replace("'", "'\"'\"'") + "'";

    public static bool Contains(string parent, string child) =>
        child.StartsWith(
            Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar,
            Comparison
        );

    public static void Remove(string path)
    {
        if (new FileInfo(path).LinkTarget is not null || File.Exists(path))
            File.Delete(path);
        else if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    public static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
            };
            if (OperatingSystem.IsLinux())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options))
                stream.Write(bytes);
            File.Move(temp, path, true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public static void CopyTree(
        string source,
        string target,
        bool preserveLinks = true,
        bool skipCaches = false,
        HashSet<string>? parents = null
    )
    {
        parents ??= new(Comparer);
        string real = RealPath(source);
        if (!parents.Add(real))
            throw new SetupError($"Configuration contains a directory link cycle: {source}");
        try
        {
            if (OperatingSystem.IsLinux())
                Directory.CreateDirectory(
                    target,
                    File.GetUnixFileMode(source)
                        | UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute
                );
            else
                Directory.CreateDirectory(target);
            foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
            {
                if (
                    skipCaches
                    && (
                        entry.Name is "Hubs" or "Plugins"
                        || entry.Name.EndsWith(".bin", StringComparison.Ordinal)
                    )
                )
                    continue;
                string dest = Path.Combine(target, entry.Name);
                if (preserveLinks && entry.LinkTarget is { } link)
                {
                    if (entry is DirectoryInfo)
                        Directory.CreateSymbolicLink(dest, link);
                    else
                        File.CreateSymbolicLink(dest, link);
                }
                else if (Directory.Exists(entry.FullName))
                    CopyTree(entry.FullName, dest, preserveLinks, skipCaches, parents);
                else
                    File.Copy(entry.FullName, dest);
            }
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(target, File.GetUnixFileMode(source));
        }
        finally
        {
            parents.Remove(real);
        }
    }

    public static void RequireStopped(params string[] roots)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!roots.Any(Directory.Exists)) return;
            WindowsProcessGuard.RequireStopped(roots);
            return;
        }
        foreach (string proc in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(proc), out int pid) || pid == Environment.ProcessId)
                continue;
            try
            {
                string exe =
                    new FileInfo(Path.Combine(proc, "exe")).ResolveLinkTarget(true)?.FullName ?? "";
                var args = File.ReadAllText(Path.Combine(proc, "cmdline"))
                    .Split('\0')
                    .Take(2)
                    .Append(exe);
                if (args.Any(arg => roots.Any(root => Contains(root, arg))))
                    throw new SetupError(
                        $"Close Pulsar and its game before continuing (PID {pid})."
                    );
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static FileStream Lock(string state)
    {
        Directory.CreateDirectory(state);
        try
        {
            return new FileStream(
                Path.Combine(state, "setup.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            );
        }
        catch (IOException error)
        {
            throw new SetupError(
                $"Cannot lock setup; another operation may be running: {error.Message}"
            );
        }
    }
}
