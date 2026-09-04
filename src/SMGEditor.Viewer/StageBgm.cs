using SMGEditor.Core.Formats;

namespace SMGEditor.Viewer;

public sealed record ScenarioBgmEntry(string StageName, int ScenarioNo, string BgmIdName, int StartType, int StartFrame, bool IsPrepare);

public sealed record StageBgmChangeEntry(string StageName, IReadOnlyList<string> ChangeBgmIdNames, IReadOnlyList<int> ChangeBgmStates);

public static class StageBgm
{
    private const string ArcRelativePath = "DATA/files/AudioRes/Info/StageBgmInfo.arc";
    private const int ChangeSlotCount = 5;

    public static readonly IReadOnlyList<string> KnownBgmNames =
    [
        "BGM_DASH_YOSHI", "BGM_EV_BOSSRUSH", "BGM_GAMBLE", "BGM_PINCH_01", "BGM_REPEAT_TIMER", "BGM_SLIDER",
        "BGM_SMG2_BOSS_04A", "BGM_SMG2_EV_PORTER",
        "MBGM_EV_KINOTAN", "MBGM_MINI_GAME", "MBGM_SMG2_EV_MOKKU", "MBGM_SMG2_GALAXY31",
        "MBGM_SMG2_GALAXY_01", "MBGM_SMG2_GALAXY_02", "MBGM_SMG2_GALAXY_03", "MBGM_SMG2_GALAXY_04",
        "MBGM_SMG2_GALAXY_05", "MBGM_SMG2_GALAXY_06", "MBGM_SMG2_GALAXY_07", "MBGM_SMG2_GALAXY_09",
        "MBGM_SMG2_GALAXY_10", "MBGM_SMG2_GALAXY_11", "MBGM_SMG2_GALAXY_12", "MBGM_SMG2_GALAXY_13",
        "MBGM_SMG2_GALAXY_14", "MBGM_SMG2_GALAXY_15", "MBGM_SMG2_GALAXY_16", "MBGM_SMG2_GALAXY_17",
        "MBGM_SMG2_GALAXY_18", "MBGM_SMG2_GALAXY_19", "MBGM_SMG2_GALAXY_20", "MBGM_SMG2_GALAXY_21",
        "MBGM_SMG2_GALAXY_22", "MBGM_SMG2_GALAXY_23", "MBGM_SMG2_GALAXY_24", "MBGM_SMG2_GALAXY_24B",
        "MBGM_SMG2_GALAXY_26", "MBGM_SMG2_GALAXY_27", "MBGM_SMG2_GALAXY_29", "MBGM_SMG2_GALAXY_30",
        "MBGM_SMG2_GALAXY_32", "MBGM_SMG2_GALAXY_DANGER", "MBGM_SMG2_GALAXY_HURRY", "MBGM_SMG2_GALAXY_INTER",
        "MBGM_SMG2_WORLDMAP_00", "MBGM_SMG2_WORLDMAP_03", "MBGM_SMG2_WORLDMAP_04", "MBGM_SMG2_WORLDMAP_06",
        "MBGM_SMG2_WORLDMAP_07", "MBGM_SMG2_WORLDMAP_08",
        "MBGM_SMG_GALAXY_01", "MBGM_SMG_GALAXY_14", "MBGM_SMG_GALAXY_28", "MBGM_STAR_CHANCE_2",
        "STM_SMG2_EV_PROLOGUE_05", "STM_SMG_ASTROOUT03",
    ];

    private static readonly Dictionary<(string Root, string? OutputDir), (List<ScenarioBgmEntry> Scenario, List<StageBgmChangeEntry> Stage)> Cache = new();

    private static (List<ScenarioBgmEntry> Scenario, List<StageBgmChangeEntry> Stage) Load(string gameRootDir, string? outputDir)
    {
        var key = (gameRootDir, outputDir);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var scenario = new List<ScenarioBgmEntry>();
        var stage = new List<StageBgmChangeEntry>();

        string arcPath = Path.Combine(ProjectFiles.ResolveRoot(gameRootDir, outputDir, ArcRelativePath), ArcRelativePath);
        if (File.Exists(arcPath))
        {
            RARCArchive arc = RARCArchive.Load(arcPath);

            RARCFile? scenarioFile = arc.Root.FindFileByName("ScenarioBgmInfo.bcsv");
            if (scenarioFile is not null)
            {
                BCSVTable table = BCSVTable.Load(scenarioFile.Data);
                foreach (IReadOnlyDictionary<string, object?> row in table.Rows)
                {
                    scenario.Add(new ScenarioBgmEntry(
                        StageName: row["StageName"] as string ?? "",
                        ScenarioNo: row["ScenarioNo"] as int? ?? 0,
                        BgmIdName: row["BgmIdName"] as string ?? "",
                        StartType: row["StartType"] as int? ?? 0,
                        StartFrame: row["StartFrame"] as int? ?? 0,
                        IsPrepare: (row["IsPrepare"] as int? ?? 0) != 0));
                }
            }

            RARCFile? stageFile = arc.Root.FindFileByName("StageBgmInfo.bcsv");
            if (stageFile is not null)
            {
                BCSVTable table = BCSVTable.Load(stageFile.Data);
                foreach (IReadOnlyDictionary<string, object?> row in table.Rows)
                {
                    var names = new List<string>(ChangeSlotCount);
                    var states = new List<int>(ChangeSlotCount);
                    for (int i = 0; i < ChangeSlotCount; i++)
                    {
                        names.Add(row[$"ChangeBgmIdName{i}"] as string ?? "");
                        states.Add(row[$"ChangeBgmState{i}"] as int? ?? -1);
                    }

                    stage.Add(new StageBgmChangeEntry(row["StageName"] as string ?? "", names, states));
                }
            }
        }

        var result = (scenario, stage);
        Cache[key] = result;
        return result;
    }

    public static ScenarioBgmEntry? FindScenarioBgm(string gameRootDir, string? outputDir, string stageName, int scenarioNo) =>
        Load(gameRootDir, outputDir).Scenario.FirstOrDefault(e => e.StageName == stageName && e.ScenarioNo == scenarioNo);

    public static StageBgmChangeEntry? FindStageBgmChanges(string gameRootDir, string? outputDir, string stageName) =>
        Load(gameRootDir, outputDir).Stage.FirstOrDefault(e => e.StageName == stageName);

    public static void SetScenarioBgm(string gameRootDir, string outputDir, ScenarioBgmEntry updated)
    {
        UpdateArc(gameRootDir, outputDir, "ScenarioBgmInfo.bcsv", table =>
        {
            var rows = table.Rows.ToList();
            int index = rows.FindIndex(r => r["StageName"] as string == updated.StageName && r["ScenarioNo"] as int? == updated.ScenarioNo);

            var newRow = new Dictionary<string, object?>
            {
                ["StageName"] = updated.StageName,
                ["ScenarioNo"] = updated.ScenarioNo,
                ["BgmIdName"] = updated.BgmIdName,
                ["StartType"] = updated.StartType,
                ["StartFrame"] = updated.StartFrame,
                ["IsPrepare"] = updated.IsPrepare ? 1 : 0,
            };

            if (index >= 0)
            {
                rows[index] = newRow;
            }
            else
            {
                rows.Add(newRow);
            }

            return new BCSVTable { Fields = table.Fields, Rows = rows, EntrySize = table.EntrySize, DataOffset = table.DataOffset };
        });

        InvalidateCache(gameRootDir, outputDir);
    }

    public static void RemoveScenarioBgm(string gameRootDir, string outputDir, string stageName, int scenarioNo)
    {
        UpdateArc(gameRootDir, outputDir, "ScenarioBgmInfo.bcsv", table =>
        {
            var rows = table.Rows.Where(r => !(r["StageName"] as string == stageName && r["ScenarioNo"] as int? == scenarioNo)).ToList();
            return new BCSVTable { Fields = table.Fields, Rows = rows, EntrySize = table.EntrySize, DataOffset = table.DataOffset };
        });

        InvalidateCache(gameRootDir, outputDir);
    }

    public static void SetStageBgmChanges(string gameRootDir, string outputDir, string stageName, IReadOnlyList<string> bgmIdNames, IReadOnlyList<int> states)
    {
        UpdateArc(gameRootDir, outputDir, "StageBgmInfo.bcsv", table =>
        {
            var rows = table.Rows.ToList();
            int index = rows.FindIndex(r => r["StageName"] as string == stageName);

            bool allEmpty = bgmIdNames.All(string.IsNullOrEmpty) && states.All(s => s < 0);
            if (index < 0 && allEmpty)
            {
                return table;
            }

            Dictionary<string, object?> row = index >= 0
                ? new Dictionary<string, object?>(rows[index])
                : new Dictionary<string, object?> { ["StageName"] = stageName };

            for (int i = 0; i < ChangeSlotCount; i++)
            {
                row[$"ChangeBgmIdName{i}"] = i < bgmIdNames.Count ? bgmIdNames[i] : "";
                row[$"ChangeBgmState{i}"] = i < states.Count ? states[i] : -1;
            }

            if (index >= 0)
            {
                rows[index] = row;
            }
            else
            {
                rows.Add(row);
            }

            return new BCSVTable { Fields = table.Fields, Rows = rows, EntrySize = table.EntrySize, DataOffset = table.DataOffset };
        });

        InvalidateCache(gameRootDir, outputDir);
    }

    private static void UpdateArc(string gameRootDir, string outputDir, string bcsvFileName, Func<BCSVTable, BCSVTable> update)
    {
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, ArcRelativePath);

        RARCFile? file = archive.Root.FindFileByName(bcsvFileName);
        if (file is null)
        {
            return;
        }

        BCSVTable updated = update(BCSVTable.Load(file.Data));
        archive.Root.ReplaceFileDataByName(bcsvFileName, updated.Save());
        ProjectFiles.SaveArc(outputDir, ArcRelativePath, archive, wasCompressed);
    }

    private static void InvalidateCache(string gameRootDir, string? outputDir) => Cache.Remove((gameRootDir, outputDir));
}
