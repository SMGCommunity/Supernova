using SMGEditor.Core.Formats;

namespace SMGEditor.PluginApi;

// default context for a plugin
// the basic idea is for users to just be able to access the root dir and do what they want when they read files and write them
// most likely only going to be BCSV editors, but shrug

public interface IPluginContext
{
    bool HasProject { get; }

    int Game { get; }

    string GameRootDir { get; }

    string OutputDir { get; }

    string? GalaxyName { get; }

    float UiScale { get; }

    byte[]? ReadFile(string relativePath);

    void WriteOutputFile(string relativePath, byte[] data);

    RARCArchive? LoadArchive(string relativePath, out bool wasCompressed);

    void SaveArchive(string relativePath, RARCArchive archive, bool compress);

    void Status(string message);
}
