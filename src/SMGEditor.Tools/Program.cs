using System.Text;
using SMGEditor.Core.Formats;

Console.OutputEncoding = Encoding.UTF8;

// Tools is my scratchpad for testing various things with a set filesystem

string root = args.Length > 0 ? args[0] : FindSmgFilesRoot();

var placementLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "ObjInfo", "MapPartsInfo", "ChildObjInfo", "AreaObjInfo",
    "PlanetObjInfo", "DemoObjInfo", "CameraCubeInfo", "StageObjInfo",
};

var perPosName = new SortedDictionary<string, PosNameStats>(StringComparer.Ordinal);
int galaxiesWithGeneralPos = 0;
int totalMarkers = 0;

foreach ((int game, string arcPath) in EnumerateStageArcs(root))
{
    RARCArchive archive;
    try
    {
        archive = RARCArchive.Load(arcPath);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"skip {arcPath}: {ex.Message}");
        continue;
    }

    RARCDirectory? generalPosDir = archive.Root.FindDirectory("jmp/GeneralPos");
    if (generalPosDir is null)
    {
        continue;
    }

    string galaxy = Path.GetFileNameWithoutExtension(arcPath);
    if (galaxy.EndsWith("Map", StringComparison.OrdinalIgnoreCase))
    {
        galaxy = galaxy[..^3];
    }

    var byLinkId = new Dictionary<int, List<string>>();
    var objectStringValues = new List<(string Owner, string Field, string Value)>();

    RARCDirectory? jmp = archive.Root.FindDirectory("jmp");
    if (jmp is not null)
    {
        foreach ((RARCFile file, string layer) in WalkFiles(jmp))
        {
            if (!placementLists.Contains(file.Name))
            {
                continue;
            }

            BCSVTable table;
            try
            {
                table = BCSVTable.Load(file.Data);
            }
            catch
            {
                continue;
            }

            foreach (IReadOnlyDictionary<string, object?> objRow in table.Rows)
            {
                string objName = objRow.TryGetValue("name", out object? n) ? n as string ?? "?" : "?";
                string label = $"{objName} [{file.Name}/{layer}]";

                if (objRow.TryGetValue("l_id", out object? lidObj) && lidObj is int lid && IsSetId(lid))
                {
                    Add(byLinkId, lid, $"{label} l_id={lid}");
                }

                foreach ((string key, object? value) in objRow)
                {
                    if (value is string s && s.Length > 0 && key != "name")
                    {
                        objectStringValues.Add((label, key, s));
                    }
                }
            }
        }
    }

    var markers = new List<(string Layer, string PosName, int ObjId, float X, float Y, float Z, float DirY)>();
    foreach (RARCDirectory layerDir in generalPosDir.Directories)
    {
        RARCFile? file = layerDir.Files.Find(f => string.Equals(f.Name, "GeneralPosInfo", StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            continue;
        }

        BCSVTable table = BCSVTable.Load(file.Data);
        foreach (IReadOnlyDictionary<string, object?> row in table.Rows)
        {
            string posName = row.TryGetValue("PosName", out object? pn) ? pn as string ?? "" : "";
            int objId = row.TryGetValue("Obj_ID", out object? oi) && oi is int oiv ? oiv : -1;
            float x = Flt(row, "pos_x");
            float y = Flt(row, "pos_y");
            float z = Flt(row, "pos_z");
            float dirY = Flt(row, "dir_y");
            markers.Add((layerDir.Name, posName, objId, x, y, z, dirY));
        }
    }

    if (markers.Count == 0)
    {
        continue;
    }

    galaxiesWithGeneralPos++;
    totalMarkers += markers.Count;

    Console.WriteLine($"=== SMG{game}  {galaxy}   ({markers.Count} marker(s)) ===");
    foreach ((string layer, string posName, int objId, float x, float y, float z, float dirY) in markers)
    {
        Console.WriteLine($"  PosName \"{posName}\"   layer {layer}   pos ({x:0.#}, {y:0.#}, {z:0.#})   dirY {dirY:0.#}");

        if (!IsSetId(objId))
        {
            Console.WriteLine("    Obj_ID unset");
        }
        else if (byLinkId.TryGetValue(objId, out List<string>? lidHits))
        {
            foreach (string hit in lidHits)
            {
                Console.WriteLine($"    Obj_ID {objId} -> linked object: {hit}");
            }
        }
        else
        {
            Console.WriteLine($"    Obj_ID {objId} -> no object with that l_id in this zone");
        }

        foreach ((string owner, string field, string value) in objectStringValues)
        {
            if (string.Equals(value, posName, StringComparison.Ordinal))
            {
                Console.WriteLine($"    referenced by name: {owner} field {field}");
            }
        }

        if (!perPosName.TryGetValue(posName, out PosNameStats? stats))
        {
            stats = new PosNameStats();
            perPosName[posName] = stats;
        }

        stats.Count++;
        stats.Games.Add(game);
        stats.Galaxies.Add($"SMG{game}:{galaxy}");
        if (IsSetId(objId) && byLinkId.TryGetValue(objId, out List<string>? linked))
        {
            foreach (string hit in linked)
            {
                stats.LinkedObjects.Add(hit.Split(' ')[0]);
            }
        }
    }

    Console.WriteLine();
}

Console.WriteLine();
Console.WriteLine($"### Summary: {totalMarkers} markers across {galaxiesWithGeneralPos} galaxies, {perPosName.Count} distinct PosName ###");
foreach ((string posName, PosNameStats stats) in perPosName)
{
    string games = string.Join("+", stats.Games.OrderBy(g => g).Select(g => $"SMG{g}"));
    Console.WriteLine($"  \"{posName}\"   [{games}]   x{stats.Count}   in {stats.Galaxies.Count} galaxy/ies");
    if (stats.LinkedObjects.Count > 0)
    {
        Console.WriteLine($"     linked objects: {string.Join(", ", stats.LinkedObjects.OrderBy(s => s))}");
    }
}

return;

static bool IsSetId(int id) => id >= 0 && id != 0xFFFF;

static void Add(Dictionary<int, List<string>> map, int key, string value)
{
    if (!map.TryGetValue(key, out List<string>? list))
    {
        list = [];
        map[key] = list;
    }

    list.Add(value);
}

static float Flt(IReadOnlyDictionary<string, object?> row, string key) =>
    row.TryGetValue(key, out object? v) && v is float f ? f : 0f;

static IEnumerable<(RARCFile File, string ParentDir)> WalkFiles(RARCDirectory dir)
{
    foreach (RARCFile file in dir.Files)
    {
        yield return (file, dir.Name);
    }

    foreach (RARCDirectory sub in dir.Directories)
    {
        foreach ((RARCFile File, string ParentDir) entry in WalkFiles(sub))
        {
            yield return entry;
        }
    }
}

static IEnumerable<(int Game, string ArcPath)> EnumerateStageArcs(string smgFilesRoot)
{
    foreach (int game in new[] { 1, 2 })
    {
        string stageData = Path.Combine(smgFilesRoot, game.ToString(), "DATA", "files", "StageData");
        if (!Directory.Exists(stageData))
        {
            continue;
        }

        foreach (string arc in Directory.EnumerateFiles(stageData, "*.arc"))
        {
            yield return (game, arc);
        }

        foreach (string sub in Directory.EnumerateDirectories(stageData))
        {
            foreach (string arc in Directory.EnumerateFiles(sub, "*Map.arc"))
            {
                yield return (game, arc);
            }
        }
    }
}

static string FindSmgFilesRoot()
{
    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "smg_files");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new DirectoryNotFoundException("Could not locate smg_files. Pass its path as the first argument.");
}

sealed class PosNameStats
{
    public int Count;
    public readonly HashSet<int> Games = [];
    public readonly HashSet<string> Galaxies = [];
    public readonly HashSet<string> LinkedObjects = [];
}
