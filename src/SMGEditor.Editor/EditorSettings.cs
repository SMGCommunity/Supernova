using System.Text.Json;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal enum HubScreen
{
    GameDirsSetup,
    ProjectPicker,
    StagePicker,
}

internal enum ProjectPickerMode
{
    List,
    Form,
}

internal enum AddKind
{
    Object,
    Area,
    CameraArea,
    Gravity,
}

internal enum BrowseTarget
{
    None,
    OutputDir,
    GameDir1,
    GameDir2,
    ProjectIcon,
}

internal sealed class MapObjTableRow
{
    public string ModelName { get; set; } = "";
    public string ClassName { get; set; } = "";
}

internal sealed class ProjectEntry
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required int Game { get; set; }
    public required string OutputDir { get; set; }
    public string? IconKey { get; set; }
}

internal sealed class EditorSettings
{
    public string? SMG1BaseDir { get; set; }
    public string? SMG2BaseDir { get; set; }

    public string? SMG2Language { get; set; }

    public string UiLanguage { get; set; } = "en";

    public List<ProjectEntry> Projects { get; set; } = [];
    public string? LastOpenedProjectId { get; set; }

    public List<ApprovedPlugin> ApprovedPlugins { get; set; } = [];

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "cache", "editorsettings.json");

    public string? BaseDirFor(int game) => game == 1 ? SMG1BaseDir : SMG2BaseDir;

    private static readonly JsonSerializerOptions LoadOptions = new() { PropertyNameCaseInsensitive = true };

    public static EditorSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                RawSettings? raw = JsonSerializer.Deserialize<RawSettings>(json, LoadOptions);
                if (raw is not null)
                {
                    (EditorSettings result, bool needsResave) = Migrate(raw);
                    if (needsResave)
                    {
                        result.Save(path);
                    }

                    return result;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new EditorSettings();
    }

    private static (EditorSettings Result, bool NeedsResave) Migrate(RawSettings raw)
    {
        var result = new EditorSettings
        {
            SMG1BaseDir = raw.SMG1BaseDir,
            SMG2BaseDir = raw.SMG2BaseDir,
            SMG2Language = raw.SMG2Language,
            UiLanguage = string.IsNullOrWhiteSpace(raw.UiLanguage) ? "en" : raw.UiLanguage,
            LastOpenedProjectId = raw.LastOpenedProjectId,
        };
        bool needsResave = false;

        foreach (RawApprovedPlugin p in raw.ApprovedPlugins ?? [])
        {
            if (p.FileName is not null && p.Sha256 is not null)
            {
                result.ApprovedPlugins.Add(new ApprovedPlugin(p.FileName, p.Sha256));
            }
        }

        if (raw.Projects is { } projects)
        {
            foreach (RawProject p in projects)
            {
                if (p.Id is null || p.Name is null || p.OutputDir is null)
                {
                    continue;
                }

                int? game = p.Game;
                if (game is null && p.BaseDir is { } legacyBaseDir)
                {
                    game = GalaxyLoader.DetectGame(legacyBaseDir);
                    if (game == 1)
                    {
                        result.SMG1BaseDir ??= legacyBaseDir;
                    }
                    else if (game == 2)
                    {
                        result.SMG2BaseDir ??= legacyBaseDir;
                    }

                    needsResave = true;
                }

                if (game is not (1 or 2))
                {
                    continue;
                }

                result.Projects.Add(new ProjectEntry { Id = p.Id, Name = p.Name, Game = game.Value, OutputDir = p.OutputDir, IconKey = p.IconKey });
            }
        }
        else if (raw is { BaseDir: { } flatBaseDir, OutputDir: { } flatOutputDir })
        {
            if (GalaxyLoader.DetectGame(flatBaseDir) is int game)
            {
                if (game == 1)
                {
                    result.SMG1BaseDir ??= flatBaseDir;
                }
                else
                {
                    result.SMG2BaseDir ??= flatBaseDir;
                }

                var migrated = new ProjectEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = Path.GetFileName(flatBaseDir.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : "My Project",
                    Game = game,
                    OutputDir = flatOutputDir,
                };
                result.Projects.Add(migrated);
                result.LastOpenedProjectId = migrated.Id;
                needsResave = true;
            }
        }

        return (result, needsResave);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this));
    }

    private sealed class RawSettings
    {
        public string? BaseDir { get; set; }
        public string? OutputDir { get; set; }
        public string? SMG1BaseDir { get; set; }
        public string? SMG2BaseDir { get; set; }
        public string? SMG2Language { get; set; }
        public string? UiLanguage { get; set; }
        public List<RawProject>? Projects { get; set; }
        public string? LastOpenedProjectId { get; set; }
        public List<RawApprovedPlugin>? ApprovedPlugins { get; set; }
    }

    private sealed class RawApprovedPlugin
    {
        public string? FileName { get; set; }
        public string? Sha256 { get; set; }
    }

    private sealed class RawProject
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? BaseDir { get; set; }
        public int? Game { get; set; }
        public string? OutputDir { get; set; }
        public string? IconKey { get; set; }
    }
}
