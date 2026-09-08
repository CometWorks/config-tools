using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Magnetar.Config.Model;

namespace Pulsar.Config;

internal sealed record SourceEntry(string Kind, XElement Data)
{
    public string Key =>
        Data.Element(
            Kind is "RemoteHub" or "RemotePlugin" ? "Repo"
            : Kind == "Mod" ? "ID"
            : "Folder"
        )?.Value
        ?? "";
    public string Name => Data.Element("Name")?.Value ?? Key;
    public bool Enabled => Data.Element("Enabled")?.Value is "true" or "1";
    public string Id => Path.GetFileName(Key.TrimEnd('/'));
}

internal sealed record PluginEntry(
    string Id,
    string Name,
    string Kind,
    bool Enabled,
    string Details,
    string[] Dependencies
);

/// <summary>Edits Pulsar's own XML without loading the game or rewriting unknown fields.</summary>
internal sealed class PluginEditor
{
    private readonly Options options;
    private readonly HashSet<string> backedUp = new(StringComparer.Ordinal);
    public string Target { get; }
    public string Game { get; }
    public string ConfigDir { get; }
    public string SourcesPath => Path.Combine(ConfigDir, "Sources", "sources.xml");
    public string ProfilesDir => Path.Combine(ConfigDir, "Profiles");
    public string CurrentPath => Path.Combine(ProfilesDir, "Current.xml");
    public string LaunchOptions =>
        Files.Quote(Path.Combine(Target, Installer.Launcher(Game)))
        + " %command%";

    public PluginEditor(Options options)
    {
        this.options = options;
        Target = Files.InstallPath(options.Target);
        Game = options.Game;
        if (Game == "auto")
        {
            string receipt = new Installer(options, _ => { }).Receipt;
            Game = InstallationDiscovery.ResolveGame(Target, receipt);
        }
        if (Game is not ("se1" or "se2"))
            throw new SetupError("Select SE1 or SE2; the saved game selection is invalid.");
        ConfigDir = Files.RealPath(
            options.Config ?? Path.Combine(Target, Game == "se2" ? "Modern" : "Legacy")
        );
    }

    public ProcessStartInfo LaunchCommand()
    {
        if (!File.Exists(Path.Combine(Target, Installer.Launcher(Game))))
            throw new SetupError("Install Pulsar or choose its installation folder first.");
        // Let Steam apply its existing launch options, environment, overlay and controller setup.
        var start = new ProcessStartInfo(CometWorks.ConfigTools.Prerequisites.SteamExecutable) { UseShellExecute = false };
        start.ArgumentList.Add("-applaunch");
        start.ArgumentList.Add(Game == "se2" ? "1133870" : "244850");
        return start;
    }

    private XElement Profile(string? key = null) =>
        Read(
            ProfilePath(key ?? "Current"),
            "Profile",
            () =>
                new XElement(
                    "Profile",
                    new XElement("Name", key ?? "Current"),
                    new XElement("GitHub"),
                    new XElement("DevFolder"),
                    new XElement("Local"),
                    new XElement("Mods")
                )
        );

    private XElement Sources() =>
        Read(
            SourcesPath,
            "SourcesConfig",
            () =>
                new XElement(
                    "SourcesConfig",
                    new XElement("ShowWarning", true),
                    new XElement("MaxSourceAge", 2),
                    new XElement(
                        "RemoteHubSources",
                        new XElement(
                            "RemoteHub",
                            new XElement("Name", "PluginHub"),
                            new XElement(
                                "Repo",
                                Game == "se2" ? "StarCpt/PluginHub-SE2" : "StarCpt/PluginHub"
                            ),
                            new XElement("Branch", "main"),
                            new XElement("Enabled", true),
                            new XElement("Trusted", true)
                        )
                    ),
                    new XElement("RemotePluginSources"),
                    new XElement("LocalHubSources"),
                    new XElement("LocalPluginSources"),
                    new XElement("ModSources")
                )
        );

    private static XElement Read(string path, string root, Func<XElement> empty)
    {
        if (!File.Exists(path))
            return empty();
        XElement value = XElement.Load(path);
        if (value.Name != root)
            throw new SetupError($"Expected {root} in {path}; the file was not changed.");
        return value;
    }

    private static XElement List(XElement root, string name)
    {
        var list = root.Element(name);
        if (list is null)
        {
            list = new XElement(name);
            root.Add(list);
        }
        return list;
    }

    private static string SourceList(string kind) => kind + "Sources";

    private static string Id(XElement e) => e.Element("Id")?.Value ?? e.Value;

    public IReadOnlyList<SourceEntry> SourcesList() =>
        Sources()
            .Elements()
            .Where(e => e.Name.LocalName.EndsWith("Sources", StringComparison.Ordinal))
            .SelectMany(e => e.Elements())
            .Select(e => new SourceEntry(e.Name.LocalName, new XElement(e)))
            .ToList();

    public IReadOnlyList<(string Key, string Name)> Profiles() =>
        Directory.Exists(ProfilesDir)
            ? Directory
                .EnumerateFiles(ProfilesDir, "*.xml")
                .Where(p => Path.GetFileName(p) != "Current.xml")
                .Select(p =>
                    (
                        Path.GetFileNameWithoutExtension(p),
                        XElement.Load(p).Element("Name")?.Value
                            ?? Path.GetFileNameWithoutExtension(p)
                    )
                )
                .OrderBy(p => p.Item2, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    public void SaveProfile(string name, string? previous = null)
    {
        string key = ProfileKey(name);
        string path = ProfilePath(key);

        Edit(() =>
        {
            if (File.Exists(path) && key != previous)
                throw new SetupError("A profile with this name already exists.");
            XElement profile = previous is null ? Profile() : ExistingProfile(previous);
            profile.SetElementValue("Name", name.Trim());
            var writes = new Dictionary<string, XElement?> { [path] = profile };
            if (previous is not null && previous != key)
                writes[ProfilePath(previous)] = null;
            return writes;
        });
    }

    public void UpdateProfile(string key) =>
        Edit(() =>
        {
            XElement named = ExistingProfile(key),
                current = Profile();
            CopyCollections(current, named);
            return new() { [ProfilePath(key)] = named };
        });

    public void LoadProfile(string key) =>
        Edit(() =>
        {
            XElement current = Profile();
            CopyCollections(ExistingProfile(key), current);
            return new() { [CurrentPath] = current };
        });

    private XElement ExistingProfile(string key)
    {
        ProfileKey(key);
        if (!File.Exists(ProfilePath(key)))
            throw new SetupError("The selected profile no longer exists. Refresh the list.");
        return Profile(key);
    }

    private static void CopyCollections(XElement from, XElement to)
    {
        foreach (string kind in new[] { "GitHub", "DevFolder", "Local", "Mods" })
            List(to, kind)
                .ReplaceNodes(from.Element(kind)?.Elements().Select(e => new XElement(e)) ?? []);
    }

    public void DeleteProfile(string key)
    {
        ProfileKey(key);
        Edit(() => new() { [ProfilePath(key)] = null });
    }

    private static string ProfileKey(string name)
    {
        string key = name.Trim();
        if (
            key.Length == 0
            || key is "." or ".."
            || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || key.Contains('\\')
            || key.Equals("Current", StringComparison.OrdinalIgnoreCase)
        )
            throw new SetupError(
                "Use a non-empty profile name without path separators; Current is reserved."
            );
        return key;
    }

    private string ProfilePath(string key)
    {
        if (key != "Current")
            ProfileKey(key);
        return Path.Combine(ProfilesDir, key + ".xml");
    }

    public void PutSource(
        SourceEntry? original,
        XElement replacement,
        bool? active = null,
        bool debug = true
    )
    {
        string kind = replacement.Name.LocalName;
        if (kind is not ("RemoteHub" or "RemotePlugin" or "LocalHub" or "LocalPlugin" or "Mod"))
            throw new SetupError("Unsupported source type.");
        var updated = new SourceEntry(kind, replacement);
        if (string.IsNullOrWhiteSpace(updated.Name) || string.IsNullOrWhiteSpace(updated.Key))
            throw new SetupError("Name and source location are required.");
        if (kind is "RemoteHub" or "RemotePlugin")
        {
            string[] repo = updated.Key.Split('/');
            if (
                repo.Length != 2
                || repo.Any(p =>
                    p.Length == 0
                    || p.Any(c =>
                        !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'
                    )
                )
            )
                throw new SetupError("Enter a GitHub repository as owner/name.");
            if (string.IsNullOrWhiteSpace(replacement.Element("Branch")?.Value))
                throw new SetupError("A branch is required.");
            if (
                kind == "RemotePlugin"
                && string.IsNullOrWhiteSpace(replacement.Element("File")?.Value)
            )
                throw new SetupError("A manifest filename is required.");
        }
        if (kind is "LocalHub" or "LocalPlugin")
        {
            replacement.SetElementValue("Folder", Files.FullPath(updated.Key));
            if (!Directory.Exists(updated.Key))
                throw new SetupError("The source folder does not exist.");
            if (kind == "LocalPlugin")
            {
                string file = replacement.Element("File")?.Value ?? "";
                if (
                    file.Length == 0
                    || Path.GetFileName(file) != file
                    || !File.Exists(Path.Combine(updated.Key, file))
                )
                    throw new SetupError("Select an existing plugin manifest in this folder.");
                var manifest = XElement.Load(Path.Combine(updated.Key, file));
                if (manifest.Name.LocalName != "PluginData")
                    throw new SetupError("The selected XML is not a PluginData manifest.");
            }
        }
        if (
            kind == "Mod"
            && (!ulong.TryParse(updated.Key, out var id) || id == 0 || id > long.MaxValue)
        )
            throw new SetupError("Use a positive Workshop ID.");
        Edit(() =>
        {
            XElement sources = Sources(),
                list = List(sources, SourceList(kind));
            XElement? old = FindSource(list, original);
            if (original is not null && old is null)
                throw new SetupError("This source changed externally. Refresh and try again.");
            if (
                list.Elements()
                    .Any(e =>
                        e != old
                        && (
                            new SourceEntry(kind, e).Key == updated.Key
                            || kind == "LocalPlugin" && new SourceEntry(kind, e).Id == updated.Id
                        )
                    )
            )
                throw new SetupError(
                    "This source is already registered, or another dev folder has the same folder-name ID."
                );
            if (old is null)
                list.Add(new XElement(replacement));
            else
                old.ReplaceWith(new XElement(replacement));
            if (kind is "RemoteHub" or "RemotePlugin")
            {
                var entry = list.Elements().First(e => new SourceEntry(kind, e).Key == updated.Key);
                foreach (
                    var field in entry
                        .Elements()
                        .Where(e => e.Name == "Hash" || e.Name == "LastCheck")
                        .ToList()
                )
                    field.Remove();
            }
            var writes = new Dictionary<string, XElement?> { [SourcesPath] = sources };
            if (kind == "LocalPlugin")
            {
                // A folder rename must keep saved profiles pointing at the same development source.
                var keys = Profiles().Select(p => p.Key).Prepend("Current");
                foreach (string key in keys)
                {
                    XElement profile = Profile(key);
                    XElement? selected = profile
                        .Element("DevFolder")
                        ?.Elements()
                        .FirstOrDefault(e => Id(e) == (original?.Id ?? updated.Id));
                    bool changed = selected is not null && original?.Id != updated.Id;
                    if (changed)
                        selected!.SetElementValue("Id", updated.Id);
                    if (key == "Current" && active is not null)
                    {
                        if (active.Value)
                        {
                            selected ??= new XElement(
                                "LocalFolderConfig",
                                new XElement("Id", updated.Id)
                            );
                            selected.SetElementValue("DebugBuild", debug);
                            if (selected.Parent is null)
                                List(profile, "DevFolder").Add(selected);
                        }
                        else
                            selected?.Remove();
                        changed = true;
                    }
                    if (changed)
                        writes[ProfilePath(key)] = profile;
                }
            }
            return writes;
        });
    }

    private static XElement? FindSource(XElement list, SourceEntry? original) =>
        original is null
            ? null
            : list.Elements()
                .FirstOrDefault(e =>
                    new SourceEntry(original.Kind, e).Key == original.Key
                    && XNode.DeepEquals(e, original.Data)
                );

    public void RemoveSource(SourceEntry source) =>
        Edit(() =>
        {
            XElement sources = Sources();
            XElement? entry = FindSource(List(sources, SourceList(source.Kind)), source);
            if (entry is null)
                throw new SetupError("This source changed externally. Refresh and try again.");
            entry.Remove();
            return new() { [SourcesPath] = sources };
        });

    public (bool Active, bool Debug) DevState(string id)
    {
        XElement? dev = Profile().Element("DevFolder")?.Elements().FirstOrDefault(e => Id(e) == id);
        return (dev is not null, dev?.Element("DebugBuild")?.Value is not ("false" or "0"));
    }

    public IReadOnlyList<PluginEntry> Plugins(bool includeHidden = false)
    {
        XElement profile = Profile();
        var result = new Dictionary<(string, string), PluginEntry>();
        bool Enabled(string kind, string id) =>
            profile.Element(kind)?.Elements().Any(e => Id(e) == id) == true;
        foreach (string dir in new[] { "Hubs", "Plugins" })
        {
            string path = Path.Combine(ConfigDir, "Sources", dir);
            if (!Directory.Exists(path))
                continue;
            foreach (string file in Directory.EnumerateFiles(path, "*.bin"))
            foreach (
                var plugin in HubCatalog.ReadFile(file, Path.GetFileNameWithoutExtension(file))
            )
            {
                if (
                    plugin.Hidden && !includeHidden
                    || plugin.Kind is HubPluginKind.Obsolete or HubPluginKind.Unknown
                )
                    continue;
                string kind = plugin.Kind == HubPluginKind.Mod ? "Mods" : "GitHub";
                result[(kind, plugin.Id)] = new(
                    plugin.Id,
                    plugin.FriendlyName ?? plugin.Id,
                    kind,
                    Enabled(kind, plugin.Id),
                    $"{plugin.Author}\n{plugin.Tooltip}\n\n{plugin.Description}\n\nSource: {plugin.SourceLabel}",
                    plugin.DependencyIds
                );
            }
        }
        foreach (var source in SourcesList())
        {
            if (source.Kind == "Mod")
                result[("Mods", source.Key)] = new(
                    source.Key,
                    source.Name,
                    "Mods",
                    Enabled("Mods", source.Key),
                    $"Workshop source · enabled: {source.Enabled}",
                    []
                );
            if (source.Kind != "LocalHub" || !Directory.Exists(source.Key))
                continue;
            foreach (
                string file in Directory.EnumerateFiles(
                    source.Key,
                    "*.xml",
                    SearchOption.AllDirectories
                )
            )
            {
                try
                {
                    XElement xml = XElement.Load(file);
                    if (xml.Name.LocalName != "PluginData")
                        continue;
                    string? id = xml.Element("Id")?.Value;
                    string? type = xml.Attribute(
                        XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance")
                    )?.Value;
                    if (
                        string.IsNullOrWhiteSpace(id)
                        || type is not ("GitHubPlugin" or "ModPlugin")
                        || !includeHidden && xml.Element("Hidden")?.Value == "true"
                    )
                        continue;
                    string kind = type == "ModPlugin" ? "Mods" : "GitHub";
                    var meta = PluginManifest.Read(file);
                    result[(kind, id)] = new(
                        id,
                        meta.FriendlyName ?? id,
                        kind,
                        Enabled(kind, id),
                        $"{meta.Author}\n{meta.Tooltip}\n\n{meta.Description}\n\nLocal hub: {source.Name}",
                        xml.Element("DependencyIds")?.Elements("Id").Select(e => e.Value).ToArray()
                            ?? []
                    );
                }
                catch (System.Xml.XmlException)
                { /* Match Pulsar: skip non-manifest XML in local hubs. */
                }
            }
        }
        foreach (var dev in SourcesList().Where(s => s.Kind == "LocalPlugin"))
            result[("DevFolder", dev.Id)] = new(
                dev.Id,
                dev.Name,
                "DevFolder",
                Enabled("DevFolder", dev.Id),
                $"Folder: {dev.Key}\nManifest: {dev.Data.Element("File")?.Value}\nSource enabled: {dev.Enabled}",
                []
            );
        string local = Path.Combine(ConfigDir, "Local");
        if (Directory.Exists(local))
            foreach (
                string file in Directory.EnumerateFiles(local, "*.dll", SearchOption.AllDirectories)
            )
            {
                string id = Path.GetFileName(file);
                if (id == "plugin.dll" && Path.GetDirectoryName(file) != local)
                    id = Path.GetFileName(Path.GetDirectoryName(file)) + ".dll";
                result[("Local", id)] = new(id, id, "Local", Enabled("Local", id), file, []);
            }
        foreach (string kind in new[] { "GitHub", "DevFolder", "Local", "Mods" })
        foreach (XElement entry in profile.Element(kind)?.Elements() ?? [])
        {
            string id = Id(entry);
            result.TryAdd(
                (kind, id),
                new(
                    id,
                    id,
                    kind,
                    true,
                    "Enabled in the profile; missing from the local catalog. Start Pulsar to refresh its catalogs.",
                    []
                )
            );
        }
        return result.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void TogglePlugin(PluginEntry plugin) =>
        Edit(() =>
        {
            XElement profile = Profile();
            bool enabled =
                profile.Element(plugin.Kind)?.Elements().Any(e => Id(e) == plugin.Id) == true;
            if (enabled)
                profile.Element(plugin.Kind)!.Elements().Where(e => Id(e) == plugin.Id).Remove();
            else
            {
                var catalog = Plugins(includeHidden: true);
                var todo = new Queue<PluginEntry>();
                todo.Enqueue(plugin);
                var visited = new HashSet<(string, string)>();
                while (todo.TryDequeue(out var next))
                {
                    if (!visited.Add((next.Kind, next.Id)))
                        continue;
                    XElement list = List(profile, next.Kind);
                    if (!list.Elements().Any(e => Id(e) == next.Id))
                        list.Add(
                            next.Kind switch
                            {
                                "GitHub" => new XElement(
                                    "GitHubPluginConfig",
                                    new XElement("Id", next.Id)
                                ),
                                "DevFolder" => new XElement(
                                    "LocalFolderConfig",
                                    new XElement("Id", next.Id),
                                    new XElement("DebugBuild", true)
                                ),
                                "Local" => new XElement("string", next.Id),
                                "Mods" when ulong.TryParse(next.Id, out _) => new XElement(
                                    "unsignedLong",
                                    next.Id
                                ),
                                _ => throw new SetupError("Invalid plugin entry."),
                            }
                        );
                    foreach (string id in next.Dependencies)
                    {
                        PluginEntry? dependency = catalog.FirstOrDefault(p => p.Id == id);
                        if (dependency is null)
                            throw new SetupError(
                                $"Dependency {id} is not in the cached catalog. Launch Pulsar to refresh it first."
                            );
                        todo.Enqueue(dependency);
                    }
                }
            }
            return new() { [CurrentPath] = profile };
        });

    private void Edit(Func<Dictionary<string, XElement?>> change)
    {
        using var operationLock = Files.Lock(new Installer(options, _ => { }).StateDir);
        Files.RequireStopped(Target, ConfigDir);
        if (!Installer.Modern(Target))
            throw new SetupError(
                "Choose an installed Pulsar folder before editing its configuration."
            );
        var writes = change();
        var before = writes.Keys.ToDictionary(
            p => p,
            p => File.Exists(p) ? File.ReadAllBytes(p) : null
        );
        var modes = writes.Keys.ToDictionary(
            p => p,
            p =>
                OperatingSystem.IsLinux() && File.Exists(p)
                    ? File.GetUnixFileMode(p)
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite
        );
        foreach (string path in writes.Keys)
            if (before[path] is { } bytes && !backedUp.Contains(path))
            {
                Files.Write(path + ".bak", bytes);
                backedUp.Add(path);
            }
        var applied = new List<string>();
        try
        {
            foreach (var (path, xml) in writes)
            {
                applied.Add(path);
                if (xml is null)
                    File.Delete(path);
                else
                {
                    Files.Write(path, Encoding.UTF8.GetBytes(xml.ToString()));
                    if (OperatingSystem.IsLinux())
                        File.SetUnixFileMode(path, modes[path]);
                }
            }
        }
        catch
        {
            foreach (string path in applied.AsEnumerable().Reverse())
                if (before[path] is { } bytes)
                {
                    Files.Write(path, bytes);
                    if (OperatingSystem.IsLinux())
                        File.SetUnixFileMode(path, modes[path]);
                }
                else
                    File.Delete(path);
            throw;
        }
    }
}
