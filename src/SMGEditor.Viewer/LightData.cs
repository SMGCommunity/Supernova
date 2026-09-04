using System.Numerics;
using SMGEditor.Core.Formats;

namespace SMGEditor.Viewer;

public sealed record LightGalaxyMapEntry(int LightId, string AreaLightName);

public readonly record struct PreviewLight(Vector3 Position, Vector3 Color, bool FollowCamera);

public readonly record struct PreviewLightGroup(PreviewLight Light0, PreviewLight Light1, Vector3 Ambient);

public static class LightData
{
    public static readonly string[] Groups = ["Player", "Strong", "Weak", "Planet"];

    public static Dictionary<string, object?>? ResolveDefaultPreset(string gameRootDir, string? outputDir, int game, string galaxyName)
    {
        List<Dictionary<string, object?>> presets = LoadPresets(gameRootDir, outputDir, game);
        if (presets.Count == 0)
        {
            return null;
        }

        List<LightGalaxyMapEntry> map = LoadGalaxyMap(gameRootDir, outputDir, game, galaxyName);
        string? wantedName = map.FirstOrDefault(e => e.LightId < 0)?.AreaLightName;

        if (wantedName is not null)
        {
            Dictionary<string, object?>? match = presets.FirstOrDefault(p => p.GetValueOrDefault("AreaLightName") as string == wantedName);
            if (match is not null)
            {
                return match;
            }
        }

        return presets[0];
    }

    public static PreviewLightGroup ExtractGroup(Dictionary<string, object?> preset, string group) => new(
        ExtractLight(preset, $"{group}Light0"),
        ExtractLight(preset, $"{group}Light1"),
        ByteColor(preset, $"{group}Ambient"));

    private static PreviewLight ExtractLight(Dictionary<string, object?> preset, string prefix) => new(
        new Vector3(F(preset, prefix + "PosX"), F(preset, prefix + "PosY"), F(preset, prefix + "PosZ")),
        ByteColor(preset, prefix + "Color"),
        (preset.GetValueOrDefault(prefix + "FollowCamera") as int? ?? 0) != 0);

    private static Vector3 ByteColor(Dictionary<string, object?> preset, string prefix) => new(
        (preset.GetValueOrDefault(prefix + "R") as int? ?? 0) / 255f,
        (preset.GetValueOrDefault(prefix + "G") as int? ?? 0) / 255f,
        (preset.GetValueOrDefault(prefix + "B") as int? ?? 0) / 255f);

    private static float F(Dictionary<string, object?> preset, string key) =>
        preset.GetValueOrDefault(key) as float? ?? 0f;

    private static string MasterArcRelativePath(int game) =>
        game == 1 ? "DATA/files/ObjectData/LightData.arc" : "DATA/files/LightData/LightData.arc";

    private static string MasterBcsvName(int game) => game == 1 ? "lightdata.bcsv" : "LightData.bcsv";

    public static List<Dictionary<string, object?>> LoadPresets(string gameRootDir, string? outputDir, int game)
    {
        string relativePath = MasterArcRelativePath(game);
        string path = ProjectFiles.ResolveFile(gameRootDir, outputDir, relativePath);
        if (!File.Exists(path))
        {
            return [];
        }

        RARCFile? file = RARCArchive.Load(path).Root.FindFileByName(MasterBcsvName(game));
        if (file is null)
        {
            return [];
        }

        return BCSVTable.Load(file.Data).Rows.Select(r => new Dictionary<string, object?>(r)).ToList();
    }

    public static void SavePresets(string gameRootDir, string outputDir, int game, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        string relativePath = MasterArcRelativePath(game);
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, relativePath);

        string bcsvName = MasterBcsvName(game);
        RARCFile? file = archive.Root.FindFileByName(bcsvName);
        if (file is null)
        {
            return;
        }

        BCSVTable schema = BCSVTable.Load(file.Data);
        var updated = new BCSVTable { Fields = schema.Fields, Rows = rows, EntrySize = schema.EntrySize, DataOffset = schema.DataOffset };
        archive.Root.ReplaceFileDataByName(bcsvName, updated.Save());
        ProjectFiles.SaveArc(outputDir, relativePath, archive, wasCompressed);
    }

    public static List<LightGalaxyMapEntry> LoadGalaxyMap(string gameRootDir, string? outputDir, int game, string galaxyName)
    {
        string relativePath;
        string bcsvName;
        if (game == 1)
        {
            relativePath = MasterArcRelativePath(1);
            bcsvName = $"light{galaxyName.ToLowerInvariant()}.bcsv";
        }
        else
        {
            relativePath = $"DATA/files/StageData/{galaxyName}/{galaxyName}Light.arc";
            bcsvName = $"{galaxyName}Light.bcsv";
        }

        string path = ProjectFiles.ResolveFile(gameRootDir, outputDir, relativePath);
        if (!File.Exists(path))
        {
            return [];
        }

        RARCFile? file = RARCArchive.Load(path).Root.FindFileByName(bcsvName);
        if (file is null)
        {
            return [];
        }

        var result = new List<LightGalaxyMapEntry>();
        foreach (IReadOnlyDictionary<string, object?> row in BCSVTable.Load(file.Data).Rows)
        {
            int id = row.TryGetValue("LightID", out object? idVal) && idVal is int i ? i : 0;
            string name = row.TryGetValue("AreaLightName", out object? nameVal) && nameVal is string s ? s : "";
            result.Add(new LightGalaxyMapEntry(id, name));
        }

        return result;
    }
}
