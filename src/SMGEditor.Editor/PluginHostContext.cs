using SMGEditor.Core.Formats;
using SMGEditor.PluginApi;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal sealed class PluginHostContext : IPluginContext
{
    public int Game { get; set; }
    public string GameRootDir { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public string? GalaxyName { get; set; }
    public float UiScale { get; set; } = 1f;
    public Action<string>? StatusSink { get; set; }

    public bool HasProject => GameRootDir.Length > 0 && OutputDir.Length > 0;

    public byte[]? ReadFile(string relativePath)
    {
        string inOutput = Path.Combine(OutputDir, relativePath);
        if (OutputDir.Length > 0 && File.Exists(inOutput))
        {
            return File.ReadAllBytes(inOutput);
        }

        string inGame = Path.Combine(GameRootDir, relativePath);
        if (GameRootDir.Length > 0 && File.Exists(inGame))
        {
            return File.ReadAllBytes(inGame);
        }

        return null;
    }

    public void WriteOutputFile(string relativePath, byte[] data)
    {
        string full = Path.Combine(OutputDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, data);
    }

    public RARCArchive? LoadArchive(string relativePath, out bool wasCompressed)
    {
        wasCompressed = false;
        if (!HasProject)
        {
            return null;
        }

        try
        {
            (RARCArchive archive, wasCompressed) = ProjectFiles.LoadArc(GameRootDir, OutputDir, relativePath);
            return archive;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public void SaveArchive(string relativePath, RARCArchive archive, bool compress) =>
        ProjectFiles.SaveArc(OutputDir, relativePath, archive, compress);

    public void Status(string message) => StatusSink?.Invoke(message);
}
