using System.Numerics;
using ImGuiNET;
using SMGEditor.Core.Formats;
using SMGEditor.PluginApi;

namespace BcsvPeekPlugin;

/* basic easy plugin that opens a file in an archive */
public sealed class BcsvPeekPlugin : ISupernovaEditorPlugin
{
    public PluginInfo Info { get; } = new(
        "com.supernova.samples.bcsvpeek",
        "BCSV Peek",
        "Loads a BCSV file out of an archive and shows its fields and rows.",
        "Supernova",
        "1.0");

    public int SupportedGame => 0;

    private string _archivePath = "DATA/files/StageData/{galaxy}/{galaxy}Map.arc";
    private string _fileInArchive = "jmp/Placement/Common/ObjInfo";
    private string _status = "";
    private BCSVTable? _table;

    public void DrawWindow(IPluginContext context)
    {
        if (!context.HasProject)
        {
            ImGui.TextWrapped("Open a project first.");
            return;
        }

        ImGui.TextDisabled($"Game {context.Game}  -  {context.GalaxyName ?? "(no galaxy)"}");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("Archive", ref _archivePath, 512);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("File in archive", ref _fileInArchive, 512);

        if (ImGui.Button("Load"))
        {
            Load(context);
        }

        if (_status.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextWrapped(_status);
        }

        if (_table is not { } table)
        {
            return;
        }

        ImGui.Separator();
        ImGui.Text($"{table.Fields.Count} field(s), {table.Rows.Count} row(s)");

        ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;

        if (ImGui.BeginTable("##bcsv", Math.Max(table.Fields.Count, 1), flags, new Vector2(0, 380 * context.UiScale)))
        {
            foreach (BCSVField field in table.Fields)
            {
                ImGui.TableSetupColumn(field.Name);
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (IReadOnlyDictionary<string, object?> row in table.Rows)
            {
                ImGui.TableNextRow();
                foreach (BCSVField field in table.Fields)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(row.TryGetValue(field.Name, out object? value) ? value?.ToString() ?? "" : "");
                }
            }

            ImGui.EndTable();
        }
    }

    private void Load(IPluginContext context)
    {
        _table = null;
        string archivePath = _archivePath.Replace("{galaxy}", context.GalaxyName ?? "");

        RARCArchive? archive = context.LoadArchive(archivePath, out _);
        if (archive is null)
        {
            _status = $"Archive not found: {archivePath}";
            return;
        }

        RARCFile? file = archive.Root.FindFile(_fileInArchive);
        if (file is null)
        {
            _status = $"'{_fileInArchive}' is not in that archive.";
            return;
        }

        try
        {
            _table = BCSVTable.Load(file.Data);
            _status = "";
            context.Status($"BCSV Peek loaded {_fileInArchive}.");
        }
        catch (Exception ex)
        {
            _status = $"Could not parse as BCSV: {ex.Message}";
        }
    }
}
