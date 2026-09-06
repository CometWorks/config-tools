#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Magnetar.Config.Install;

internal static class InstallFiles
{
    internal static readonly StringComparison Comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static readonly UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    internal static string FullPath(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length == 1 ? "" : path[2..]);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    internal static bool Contains(string root, string path) => string.Equals(root, path, Comparison)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, Comparison);

    internal static string RealPath(string path)
    {
        string full = FullPath(path), current = Path.GetPathRoot(full)!;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var info = new DirectoryInfo(current);
            if (info.LinkTarget is not null) current = info.ResolveLinkTarget(true)!.FullName;
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    internal static string TargetPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new InstallError("Choose a dedicated installation folder without control characters.");
        string full = FullPath(value);
        if (new DirectoryInfo(full).LinkTarget is not null) throw new InstallError("Choose the real installation folder, not a symbolic link.");
        full = RealPath(full);
        foreach (string reserved in new[] { Path.GetPathRoot(full)!, Path.GetTempPath(),
                     Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) })
            if (!string.IsNullOrEmpty(reserved) && string.Equals(full, RealPath(reserved), Comparison))
                throw new InstallError("Choose a dedicated Magnetar folder, not a home, data, or filesystem root.");
        if (File.Exists(full)) throw new InstallError("The installation path is a file.");
        return full;
    }

    internal static void PrivateFolder(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, PrivateDirectory);
    }

    internal static bool Exists(string path) => Path.Exists(path) || new FileInfo(path).LinkTarget is not null;
    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    internal static void Write(string path, byte[] bytes)
    {
        PrivateFolder(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }

    internal static void Remove(string path)
    {
        if (new FileInfo(path).LinkTarget is not null || File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    internal static void CopyTree(string source, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PrivateFolder(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            string dest = Path.Combine(destination, entry.Name);
            if (entry.LinkTarget is { } link)
            {
                if (entry is DirectoryInfo) Directory.CreateSymbolicLink(dest, link);
                else File.CreateSymbolicLink(dest, link);
            }
            else if (entry is DirectoryInfo) CopyTree(entry.FullName, dest, token);
            else File.Copy(entry.FullName, dest);
        }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
    }
}
