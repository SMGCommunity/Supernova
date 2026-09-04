using SMGEditor.Core.Formats;

namespace SMGEditor.Viewer;

public static class ProductMapObjTable
{
    private const string ArcRelativePath = "DATA/files/ObjectData/ProductMapObjDataTable.arc";
    private const string BcsvName = "ProductMapObjDataTable.bcsv";

    public static readonly string[] KnownClasses =
    [
        "AfterMapObjDrawAir", "Air", "AnmModelSwitchMove", "AnmModelSwitchMoveEndKill",
        "AnmModelSwitchMoveEndKillAnyAnim", "AnmModelSwitchMoveInvalidateCollision",
        "AnmModelSwitchMoveValidateCollision", "AnmModelSwitchSyncBrk", "AutoMakeMapObj",
        "CloudSea", "CloudStep", "EffectObj10x10x10SyncClipping", "EffectObj20x20x10SyncClipping",
        "EffectObj50x50x10SyncClipping", "EffectObjR1000F50", "EffectObjR1500F400", "EffectObjR500F50",
        "GorogoroCylinderRock", "HipDropMoveBlock", "LavaFloater", "PriorDrawAir",
        "ProjectionMapAir", "ProjectionMapSky", "RailAndRotateMoveObj", "RailMoveDemoActionObj",
        "RailMoveLavaProjmapObj", "RailMoveObj", "RailMoveObjClipParts", "RailMoveObjSwitchWarpEnd",
        "RailMoveShadowDropYObj", "RailMoveWithIndirectModelObj", "RailMoveWithReverseAnimObj",
        "RailRotateMoveObj", "RotateMoveObj", "RotateMoveObjClipParts", "SandBirdParts",
        "SimpleBreakableCollisionObj", "SimpleBreakableDeleteShadowObj", "SimpleBreakableObj",
        "SimpleBreakableStrongLightObj", "SimpleClipPartsObj", "SimpleFloaterObj", "SimpleMapObj",
        "SimpleMapObjFarMax", "SimpleMapObjWithEffect", "SimpleMapShadowDropYObj",
        "SimpleSeesawObj", "Sky", "SoundSyncSky", "SwitchingMoveBlock",
    ];

    public static List<(string ModelName, string ClassName)> Load(string gameRootDir, string? outputDir)
    {
        string path = Path.Combine(ProjectFiles.ResolveRoot(gameRootDir, outputDir, ArcRelativePath), ArcRelativePath);
        if (!File.Exists(path))
        {
            return [];
        }

        RARCFile? file = RARCArchive.Load(path).Root.FindFileByName(BcsvName);
        if (file is null)
        {
            return [];
        }

        var result = new List<(string, string)>();
        foreach (IReadOnlyDictionary<string, object?> row in BCSVTable.Load(file.Data).Rows)
        {
            result.Add((
                row.TryGetValue("ModelName", out object? m) && m is string ms ? ms : "",
                row.TryGetValue("ClassName", out object? c) && c is string cs ? cs : ""));
        }

        return result;
    }

    public static void Save(string gameRootDir, string outputDir, IReadOnlyList<(string ModelName, string ClassName)> entries)
    {
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, ArcRelativePath);

        RARCFile? file = archive.Root.FindFileByName(BcsvName);
        if (file is null)
        {
            return;
        }

        BCSVTable schema = BCSVTable.Load(file.Data);
        var rows = entries
            .Where(e => e.ModelName.Length > 0)
            .Select(e => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["ModelName"] = e.ModelName,
                ["ClassName"] = e.ClassName,
            })
            .ToList();

        var updated = new BCSVTable { Fields = schema.Fields, Rows = rows, EntrySize = schema.EntrySize, DataOffset = schema.DataOffset };
        archive.Root.ReplaceFileDataByName(BcsvName, updated.Save());
        ProjectFiles.SaveArc(outputDir, ArcRelativePath, archive, wasCompressed);
    }
}
