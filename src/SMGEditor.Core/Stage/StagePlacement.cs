using System.Numerics;
using SMGEditor.Core.Formats;

namespace SMGEditor.Core.Stage;

public sealed class PlacedObject
{
    public required string Name { get; init; }
    public required Vector3 Position { get; init; }
    public required Vector3 RotationDegrees { get; init; }
    public required Vector3 Scale { get; init; }
    public required string Layer { get; init; }

    public required string SourceList { get; init; }

    public required IReadOnlyDictionary<string, object?> Fields { get; init; }
}

public static class StagePlacementReader
{
    public static List<PlacedObject> ReadObjInfo(RARCArchive mapArchive, IEnumerable<string> layers) =>
        ReadPlacementFile(mapArchive, layers, "jmp/Placement", "ObjInfo");

    public static List<PlacedObject> ReadPlacementFile(RARCArchive mapArchive, IEnumerable<string> layers, string basePath, string fileName)
    {
        var results = new List<PlacedObject>();

        foreach (string layer in layers)
        {
            RARCFile? file = mapArchive.Root.FindFile($"{basePath}/{layer}/{fileName}");
            if (file is null)
            {
                continue;
            }

            BCSVTable table = BCSVTable.Load(file.Data);
            foreach (IReadOnlyDictionary<string, object?> row in table.Rows)
            {
                results.Add(new PlacedObject
                {
                    Name = row.TryGetValue("name", out object? name) ? (string?)name ?? "" : "",
                    Position = ReadVector3(row, "pos_x", "pos_y", "pos_z"),
                    RotationDegrees = ReadVector3(row, "dir_x", "dir_y", "dir_z"),
                    Scale = ReadVector3(row, "scale_x", "scale_y", "scale_z", Vector3.One),
                    Layer = layer,
                    SourceList = fileName,
                    Fields = row,
                });
            }
        }

        return results;
    }

    private static Vector3 ReadVector3(IReadOnlyDictionary<string, object?> row, string x, string y, string z, Vector3? fallback = null)
    {
        if (row.TryGetValue(x, out object? xv) && xv is float xf &&
            row.TryGetValue(y, out object? yv) && yv is float yf &&
            row.TryGetValue(z, out object? zv) && zv is float zf)
        {
            return new Vector3(xf, yf, zf);
        }

        return fallback ?? Vector3.Zero;
    }
}
