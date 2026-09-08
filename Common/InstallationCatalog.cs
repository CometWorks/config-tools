#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CometWorks.ConfigTools;

internal enum InstallationKind { Missing, Empty, Current, Older, Incomplete, Retained, Unknown }

internal sealed record Installation(string Path, InstallationKind Kind, string Variants = "")
{
    public bool CanOpen => Kind == InstallationKind.Current;
    public bool CanInstall => Kind is InstallationKind.Missing or InstallationKind.Empty or InstallationKind.Retained;
    public bool CanUpdate => Kind == InstallationKind.Current;
    public bool CanUninstall => Kind == InstallationKind.Current;
    public string Status => Kind switch
    {
        InstallationKind.Current => "Installed" + (Variants.Length == 0 ? "" : " · " + Variants),
        InstallationKind.Older => "Older layout — install current release in a new folder",
        InstallationKind.Incomplete => "Incomplete installation — required program files are missing",
        InstallationKind.Retained => "Uninstalled — settings retained",
        InstallationKind.Missing => "Folder not found",
        InstallationKind.Empty => "Empty folder — ready to install",
        _ => "Not a recognized installation",
    };
    public override string ToString() => "  " + Path + " · " + Status;
}

/// <summary>Read-only discovery; remembering a user selection is a separate operation.</summary>
internal sealed class InstallationCatalog
{
    public static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Func<string, Installation> probe;
    private readonly string[] defaults;
    public string Product { get; }
    public string ReceiptDirectory { get; }
    public string HistoryPath { get; }

    public InstallationCatalog(string product, string receipts, Func<string, Installation> probe,
        IEnumerable<string> defaults, string? historyPath = null)
    {
        Product = product;
        ReceiptDirectory = receipts;
        this.probe = probe;
        this.defaults = defaults.ToArray();
        HistoryPath = historyPath ?? System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(ThemePreference.FilePath)!, product.ToLowerInvariant() + "-installs.json");
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            throw new ArgumentException("Choose an installation folder without control characters.");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (value == "~") value = home;
        else if (value.StartsWith("~/", StringComparison.Ordinal)) value = System.IO.Path.Combine(home, value[2..]);
        string full = System.IO.Path.GetFullPath(value);
        string current = System.IO.Path.GetPathRoot(full)!;
        foreach (string part in full[current.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = System.IO.Path.Combine(current, part);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget is not null)
                current = directory.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Broken directory link.");
        }
        return System.IO.Path.TrimEndingDirectorySeparator(current);
    }

    public Installation Inspect(string path)
    {
        string full = Normalize(path);
        if (File.Exists(full)) return new(full, InstallationKind.Unknown);
        if (!Directory.Exists(full)) return new(full, InstallationKind.Missing);
        var result = probe(full);
        if (result.Kind != InstallationKind.Unknown) return result;
        if (!Directory.EnumerateFileSystemEntries(full).Any()) return new(full, InstallationKind.Empty);
        bool? installed = ReceiptInstalled(full);
        return installed is null ? result : new(full, installed.Value ? InstallationKind.Incomplete : InstallationKind.Retained);
    }

    public bool? ReceiptInstalled(string target)
    {
        string normalized = Normalize(target);
        string key = OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
        string file = System.IO.Path.Combine(ReceiptDirectory,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20] + ".json");
        var receipt = ReadReceipt(file);
        return receipt is { } r && PathComparer.Equals(normalized, r.Target) ? r.Installed : null;
    }

    private static (string Target, bool Installed)? ReadReceipt(string file)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("target", out var path)
                || path.ValueKind != JsonValueKind.String || !root.TryGetProperty("installed", out var installed)
                || installed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            return (Normalize(path.GetString()!), installed.GetBoolean());
        }
        catch (Exception error) when (IsPathError(error) || error is JsonException) { return null; }
    }

    private string[] History()
    {
        try { return JsonSerializer.Deserialize<string[]>(File.ReadAllText(HistoryPath)) ?? []; }
        catch (Exception error) when (IsPathError(error) || error is JsonException) { return []; }
    }

    public Installation? SingleSavedInstallation()
    {
        var saved = InspectCandidates(History()).Where(item => item.CanOpen).Take(2).ToArray();
        return saved.Length == 1 ? saved[0] : null;
    }

    public IReadOnlyList<Installation> Discover(string? selected = null)
    {
        var candidates = new List<string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(selected) && Directory.Exists(Normalize(selected))) candidates.Add(selected);
        }
        catch (Exception error) when (IsPathError(error)) { }
        candidates.AddRange(History());
        candidates.AddRange(defaults.Where(Directory.Exists));
        try
        {
            if (Directory.Exists(ReceiptDirectory))
                foreach (string file in Directory.EnumerateFiles(ReceiptDirectory, "*.json"))
                    if (ReadReceipt(file) is { } receipt) candidates.Add(receipt.Target);
        }
        catch (Exception error) when (IsPathError(error)) { }
        return InspectCandidates(candidates);
    }

    private IReadOnlyList<Installation> InspectCandidates(IEnumerable<string> candidates)
    {
        var seen = new HashSet<string>(PathComparer);
        var found = new List<Installation>();
        foreach (string path in candidates)
            try
            {
                var item = Inspect(path);
                if (seen.Add(item.Path) && item.Kind is not (InstallationKind.Unknown or InstallationKind.Empty)) found.Add(item);
            }
            catch (Exception error) when (IsPathError(error)) { }
        return found;
    }

    public IReadOnlyList<Installation> Search(string root, CancellationToken cancellation, out bool limited,
        int maxDepth = 4, int maxDirectories = 2000)
    {
        // Bounded user-requested search. Increase limits only with a progress UI for larger scans.
        var found = new List<Installation>();
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((Normalize(root), 0));
        int visited = 0;
        limited = false;
        while (pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (++visited > maxDirectories) { limited = true; break; }
            var (path, depth) = pending.Dequeue();
            try
            {
                var item = Inspect(path);
                if (item.Kind is InstallationKind.Current or InstallationKind.Older or InstallationKind.Incomplete)
                { found.Add(item); continue; }
                foreach (string child in Directory.EnumerateDirectories(path))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var directory = new DirectoryInfo(child);
                    if (directory.LinkTarget is not null || directory.Name.StartsWith(".", StringComparison.Ordinal)) continue;
                    if (depth >= maxDepth || pending.Count + visited >= maxDirectories) { limited = true; continue; }
                    pending.Enqueue((child, depth + 1));
                }
            }
            catch (Exception error) when (IsPathError(error)) { }
        }
        return found;
    }

    public void Remember(string path)
    {
        var item = Inspect(path);
        if (!item.CanOpen) return;
        var paths = new[] { item.Path }.Concat(History()).Distinct(PathComparer).Take(50).ToArray();
        SaveHistory(paths);
    }

    public void Forget(string path)
    {
        string full = Normalize(path);
        SaveHistory(History().Where(saved => !PathComparer.Equals(saved, full)).ToArray());
    }

    private void SaveHistory(string[] paths)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(HistoryPath)!);
        string temp = HistoryPath + "." + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(paths)); File.Move(temp, HistoryPath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static bool IsPathError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
