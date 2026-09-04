namespace SMGEditor.PluginApi;

public sealed record PluginInfo(
    string Id,
    string Name,
    string? Description = null,
    string? Author = null,
    string? Version = null);

public interface ISupernovaEditorPlugin
{
    PluginInfo Info { get; }

    int SupportedGame { get; }

    void OnProjectOpened(IPluginContext context)
    {
    }

    void OnProjectClosed()
    {
    }

    void DrawWindow(IPluginContext context);
}
