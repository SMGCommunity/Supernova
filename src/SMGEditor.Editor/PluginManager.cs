using System.Reflection;
using SMGEditor.PluginApi;

namespace SMGEditor.Editor;

internal sealed record ApprovedPlugin(string FileName, string Sha256);

internal sealed class LoadedPlugin
{
    public required ISupernovaEditorPlugin Instance { get; init; }
    public bool WindowOpen { get; set; }
    public bool ProjectNotified { get; set; }

    public PluginInfo Info => Instance.Info;
}

internal sealed class DiscoveredPlugin
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required PluginScanResult Scan { get; init; }

    public bool Approved { get; set; }
    public bool HashChanged { get; set; }
    public string? LoadError { get; set; }
    public List<LoadedPlugin> Loaded { get; } = [];
}

internal sealed class PluginManager
{
    private static readonly HashSet<string> HostAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SMGEditor.PluginApi", "SMGEditor.Core", "SMGEditor.Viewer", "SMGEditor.Editor", "ImGui.NET",
    };

    public string PluginsDir { get; }
    public List<DiscoveredPlugin> Discovered { get; } = [];
    public List<LoadedPlugin> Plugins { get; } = [];
    public string? FolderError { get; private set; }

    public PluginManager(string baseDir)
    {
        PluginsDir = System.IO.Path.Combine(baseDir, "plugins");
    }

    public bool AnyActive => Plugins.Count > 0;

    public void Rescan(IEnumerable<ApprovedPlugin> approved)
    {
        Discovered.Clear();
        Plugins.Clear();

        try
        {
            Directory.CreateDirectory(PluginsDir);
        }
        catch (Exception ex)
        {
            FolderError = $"Could not open the plugins folder: {ex.Message}";
            return;
        }

        FolderError = null;

        var approvedByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ApprovedPlugin entry in approved)
        {
            approvedByFile[entry.FileName] = entry.Sha256;
        }

        foreach (string dll in EnumeratePluginDlls())
        {
            string fileName = System.IO.Path.GetRelativePath(PluginsDir, dll).Replace('\\', '/');
            PluginScanResult scan = PluginScanner.Scan(dll);

            var discovered = new DiscoveredPlugin { Path = dll, FileName = fileName, Scan = scan };
            if (approvedByFile.TryGetValue(fileName, out string? approvedHash) && scan.Sha256.Length > 0)
            {
                if (string.Equals(approvedHash, scan.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    discovered.Approved = true;
                }
                else
                {
                    discovered.HashChanged = true;
                }
            }

            Discovered.Add(discovered);
        }

        foreach (DiscoveredPlugin discovered in Discovered)
        {
            if (discovered.Approved && discovered.Scan.Error is null && discovered.Scan.HasPluginTypes)
            {
                LoadApproved(discovered);
            }
        }

        Discovered.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
        Plugins.Sort((a, b) => string.Compare(a.Info.Name, b.Info.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadApproved(DiscoveredPlugin discovered)
    {
        try
        {
            Assembly assembly = Assembly.LoadFrom(discovered.Path);

            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            foreach (Type? type in types)
            {
                if (type is null || type.IsAbstract || type.IsInterface
                    || !typeof(ISupernovaEditorPlugin).IsAssignableFrom(type)
                    || type.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                var instance = (ISupernovaEditorPlugin)Activator.CreateInstance(type)!;
                if (string.IsNullOrWhiteSpace(instance.Info.Id) || Plugins.Exists(p => p.Info.Id == instance.Info.Id))
                {
                    continue;
                }

                var loaded = new LoadedPlugin { Instance = instance };
                Plugins.Add(loaded);
                discovered.Loaded.Add(loaded);
            }
        }
        catch (Exception ex)
        {
            discovered.LoadError = ex.Message;
        }
    }

    private IEnumerable<string> EnumeratePluginDlls()
    {
        if (!Directory.Exists(PluginsDir))
        {
            yield break;
        }

        foreach (string dll in Directory.EnumerateFiles(PluginsDir, "*.dll", SearchOption.AllDirectories))
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(dll);
            if (HostAssemblyNames.Contains(name)
                || name.StartsWith("Silk.NET", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("StbImage", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return dll;
        }
    }

    public void NotifyProjectOpened(IPluginContext context)
    {
        foreach (LoadedPlugin plugin in Plugins)
        {
            if (plugin.ProjectNotified)
            {
                continue;
            }

            plugin.ProjectNotified = true;
            try
            {
                plugin.Instance.OnProjectOpened(context);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Plugin] {plugin.Info.Id}: {ex.Message}");
            }
        }
    }

    public void NotifyProjectClosed()
    {
        foreach (LoadedPlugin plugin in Plugins)
        {
            plugin.WindowOpen = false;
            if (!plugin.ProjectNotified)
            {
                continue;
            }

            plugin.ProjectNotified = false;
            try
            {
                plugin.Instance.OnProjectClosed();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Plugin] {plugin.Info.Id}: {ex.Message}");
            }
        }
    }
}
