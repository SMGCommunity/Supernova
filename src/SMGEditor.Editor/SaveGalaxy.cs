using System.Numerics;
using SMGEditor.Core.Formats;
using SMGEditor.Core.Stage;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal static class SaveGalaxy
{
    public static string Save(GalaxySession session) => Save(session, session.OutputDir);

    public static string Save(GalaxySession session, string? outputDir)
    {
        if (outputDir is null)
        {
            return "Save requires an open project (no output directory configured). Use File > Switch Project first.";
        }

        var zoneStagePaths = new List<string> { session.GalaxyName };
        zoneStagePaths.AddRange(session.Objects
            .Where(o => o.SourceList == "StageObjInfo")
            .Select(o => $"{o.StagePath}/{o.InternalName}"));

        int stagesSaved = 0;
        foreach (string stagePath in zoneStagePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string bareStageName = stagePath.Contains('/') ? stagePath[(stagePath.LastIndexOf('/') + 1)..] : stagePath;
            if (SaveStage(session, outputDir, stagePath, bareStageName))
            {
                stagesSaved++;
            }
        }

        var scenarioRows = session.Scenarios.Select(s => (IReadOnlyDictionary<string, object?>)s.Fields).ToList();
        GalaxyLoader.SaveScenarios(session.GameRootDir, outputDir, session.GalaxyName, scenarioRows);

        return $"Saved {stagesSaved} zone(s) and {scenarioRows.Count} scenario(s) into {outputDir}.";
    }

    private static bool SaveStage(GalaxySession session, string outputDir, string stagePath, string bareStageName)
    {
        if (stagePath != session.GalaxyName && !session.LoadedStagePaths.Contains(stagePath))
        {
            return false;
        }

        string relativePath = GalaxyLoader.MapArcRelativePath(session.GameRootDir, bareStageName);
        RARCArchive archive;
        bool wasCompressed;
        try
        {
            (archive, wasCompressed) = ProjectFiles.LoadArc(session.GameRootDir, outputDir, relativePath);
        }
        catch (FileNotFoundException)
        {
            return false;
        }

        List<EditableObject> stageObjects = session.Objects.Where(o => o.StagePath == stagePath).ToList();

        RARCDirectory jmpPlacement = FindOrCreateDirectory(archive.Root, "jmp/Placement");
        RARCDirectory jmpStart = FindOrCreateDirectory(archive.Root, "jmp/Start");
        RARCDirectory jmpMapParts = FindOrCreateDirectory(archive.Root, "jmp/MapParts");
        RARCDirectory jmpGeneralPos = FindOrCreateDirectory(archive.Root, "jmp/GeneralPos");

        var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Common" };
        layers.UnionWith(jmpPlacement.Directories.Select(d => d.Name));
        layers.UnionWith(jmpStart.Directories.Select(d => d.Name));
        layers.UnionWith(jmpMapParts.Directories.Select(d => d.Name));
        layers.UnionWith(jmpGeneralPos.Directories.Select(d => d.Name));
        layers.UnionWith(stageObjects.Select(o => o.Layer));

        foreach (string layer in layers)
        {
            foreach (string listName in GalaxyLoader.PlacementListNames)
            {
                List<EditableObject> objects = stageObjects.Where(o => o.Layer == layer && o.SourceList == listName).ToList();
                SaveList(jmpPlacement, layer, listName, objects, session.Game);
            }

            SaveList(jmpStart, layer, "StartInfo", stageObjects.Where(o => o.Layer == layer && o.SourceList == "StartInfo").ToList(), session.Game);
            SaveList(jmpMapParts, layer, "MapPartsInfo", stageObjects.Where(o => o.Layer == layer && o.SourceList == "MapPartsInfo").ToList(), session.Game);
            SaveList(jmpGeneralPos, layer, "GeneralPosInfo", stageObjects.Where(o => o.Layer == layer && o.SourceList == "GeneralPosInfo").ToList(), session.Game);
        }

        SavePaths(archive, session, stagePath);

        ProjectFiles.SaveArc(outputDir, relativePath, archive, wasCompressed);
        return true;
    }

    private static void SavePaths(RARCArchive archive, GalaxySession session, string stagePath)
    {
        List<EditablePath> paths = session.Paths.Where(p => p.StagePath == stagePath).ToList();

        if (paths.Count == 0 && archive.Root.FindDirectory("jmp/Path") is null)
        {
            return;
        }

        RARCDirectory pathDir = FindOrCreateDirectory(archive.Root, "jmp/Path");

        BCSVTable infoSchema = pathDir.Files.Find(f => string.Equals(f.Name, "CommonPathInfo", StringComparison.OrdinalIgnoreCase)) is { } infoFile
            ? BCSVTable.Load(infoFile.Data)
            : PlacementSchemas.CommonPathInfo();

        BCSVTable pointSchema = pathDir.Files.Find(f => f.Name.StartsWith("CommonPathPointInfo.", StringComparison.OrdinalIgnoreCase)) is { } pointFile
            ? BCSVTable.Load(pointFile.Data)
            : PlacementSchemas.CommonPathPointInfo();

        var infoRows = new List<IReadOnlyDictionary<string, object?>>(paths.Count);
        var liveNos = new HashSet<int>();

        foreach (EditablePath path in paths)
        {
            liveNos.Add(path.No);

            if (!Matrix4x4.Invert(path.ZoneToWorld, out Matrix4x4 worldToZone))
            {
                worldToZone = Matrix4x4.Identity;
            }

            var infoRow = new Dictionary<string, object?>(path.Fields)
            {
                ["name"] = path.Name,
                ["type"] = path.Fields.TryGetValue("type", out object? t) && t is string ts && ts.Length > 0 ? ts : "Bezier",
                ["closed"] = path.Closed ? "CLOSE" : "OPEN",
                ["usage"] = string.IsNullOrEmpty(path.Usage) ? "General" : path.Usage,
                ["num_pnt"] = path.WorldPoints.Count,
                ["l_id"] = path.LinkId,
                ["no"] = path.No,
            };
            infoRows.Add(ApplySchemaDefaults(infoRow, infoSchema));

            var pointRows = new List<IReadOnlyDictionary<string, object?>>(path.WorldPoints.Count);
            for (int i = 0; i < path.WorldPoints.Count; i++)
            {
                PathPoint pt = path.WorldPoints[i];
                Vector3 p0 = Vector3.Transform(pt.Position, worldToZone);
                Vector3 p1 = Vector3.Transform(pt.ControlPointIn, worldToZone);
                Vector3 p2 = Vector3.Transform(pt.ControlPointOut, worldToZone);
                var prow = new Dictionary<string, object?>(pt.Fields)
                {
                    ["pnt0_x"] = p0.X, ["pnt0_y"] = p0.Y, ["pnt0_z"] = p0.Z,
                    ["pnt1_x"] = p1.X, ["pnt1_y"] = p1.Y, ["pnt1_z"] = p1.Z,
                    ["pnt2_x"] = p2.X, ["pnt2_y"] = p2.Y, ["pnt2_z"] = p2.Z,
                    ["id"] = i,
                };
                pointRows.Add(ApplySchemaDefaults(prow, pointSchema));
            }

            WriteBcsvFile(pathDir, $"CommonPathPointInfo.{path.No}", new BCSVTable
            {
                Fields = pointSchema.Fields,
                Rows = pointRows,
                EntrySize = pointSchema.EntrySize,
                DataOffset = pointSchema.DataOffset,
            });
        }

        WriteBcsvFile(pathDir, "CommonPathInfo", new BCSVTable
        {
            Fields = infoSchema.Fields,
            Rows = infoRows,
            EntrySize = infoSchema.EntrySize,
            DataOffset = infoSchema.DataOffset,
        });

        pathDir.Files.RemoveAll(f =>
            f.Name.StartsWith("CommonPathPointInfo.", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(f.Name["CommonPathPointInfo.".Length..], out int fileNo)
            && !liveNos.Contains(fileNo));
    }

    private static void WriteBcsvFile(RARCDirectory dir, string name, BCSVTable table)
    {
        byte[] data = table.Save();
        RARCFile? existing = dir.Files.Find(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            dir.ReplaceFileData(existing, data);
        }
        else
        {
            dir.Files.Add(new RARCFile { Name = name, Data = data });
        }
    }

    private static Dictionary<string, object?> ApplySchemaDefaults(Dictionary<string, object?> row, BCSVTable schema)
    {
        foreach (BCSVField field in schema.Fields)
        {
            if (row.ContainsKey(field.Name))
            {
                continue;
            }

            row[field.Name] = field.Type switch
            {
                BCSVValueType.Float => 0f,
                BCSVValueType.String or BCSVValueType.StringOffset => "",
                _ => -1,
            };
        }

        return row;
    }

    private static void SaveList(RARCDirectory placementParent, string layer, string listName, List<EditableObject> objects, int game)
    {
        RARCDirectory? layerDir = placementParent.Directories.Find(d => string.Equals(d.Name, layer, StringComparison.OrdinalIgnoreCase));
        RARCFile? existingFile = layerDir?.Files.Find(f => string.Equals(f.Name, listName, StringComparison.OrdinalIgnoreCase));

        if (existingFile is null && objects.Count == 0)
        {
            return;
        }

        BCSVTable schema;
        if (existingFile is not null)
        {
            schema = BCSVTable.Load(existingFile.Data);
        }
        else
        {
            RARCFile? template = FindTemplateFile(placementParent, listName);
            if (template is not null)
            {
                schema = BCSVTable.Load(template.Data);
            }
            else if (PlacementSchemas.ForGame(game, listName) is { } builtin)
            {
                schema = builtin;
            }
            else
            {
                return;
            }
        }

        List<Dictionary<string, object?>> rows = objects.Select(o => BuildRow(o, schema)).ToList();
        var updatedTable = new BCSVTable { Fields = schema.Fields, Rows = rows, EntrySize = schema.EntrySize, DataOffset = schema.DataOffset };
        byte[] data = updatedTable.Save();

        if (existingFile is not null)
        {
            layerDir!.ReplaceFileData(existingFile, data);
        }
        else
        {
            layerDir ??= FindOrCreateDirectory(placementParent, layer);
            layerDir.Files.Add(new RARCFile { Name = listName, Data = data });
        }
    }

    private static Dictionary<string, object?> BuildRow(EditableObject obj, BCSVTable schema)
    {
        var fields = new Dictionary<string, object?>(obj.Fields)
        {
            ["name"] = obj.InternalName,
            ["pos_x"] = obj.Position.X,
            ["pos_y"] = obj.Position.Y,
            ["pos_z"] = obj.Position.Z,
            ["dir_x"] = obj.Rotation.X,
            ["dir_y"] = obj.Rotation.Y,
            ["dir_z"] = obj.Rotation.Z,
            ["scale_x"] = obj.Scale.X,
            ["scale_y"] = obj.Scale.Y,
            ["scale_z"] = obj.Scale.Z,
        };

        foreach (BCSVField field in schema.Fields)
        {
            if (fields.ContainsKey(field.Name))
            {
                continue;
            }

            fields[field.Name] = field.Type switch
            {
                BCSVValueType.Float => 0f,
                BCSVValueType.String or BCSVValueType.StringOffset => "",
                _ => -1,
            };
        }

        return fields;
    }

    private static RARCFile? FindTemplateFile(RARCDirectory placementParent, string listName)
    {
        foreach (RARCDirectory layerDir in placementParent.Directories)
        {
            RARCFile? file = layerDir.Files.Find(f => string.Equals(f.Name, listName, StringComparison.OrdinalIgnoreCase));
            if (file is not null)
            {
                return file;
            }
        }

        return null;
    }

    private static RARCDirectory FindOrCreateDirectory(RARCDirectory root, string path)
    {
        RARCDirectory current = root;
        foreach (string segment in path.Split('/'))
        {
            RARCDirectory? next = current.Directories.Find(d => string.Equals(d.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                next = new RARCDirectory { Name = segment };
                current.Directories.Add(next);
            }

            current = next;
        }

        return current;
    }
}
