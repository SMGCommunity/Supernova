using System.Numerics;
using SMGEditor.Core.Formats;

namespace SMGEditor.Core.Stage;

public sealed class PathPoint
{
    public required Vector3 Position { get; set; }
    public required Vector3 ControlPointIn { get; set; }
    public required Vector3 ControlPointOut { get; set; }

    public required Dictionary<string, object?> Fields { get; init; }
}

public sealed class PathData
{
    public required string Name { get; init; }

    public required int LinkId { get; init; }

    public required int No { get; init; }

    public required bool Closed { get; init; }

    public required string Usage { get; init; }
    public required IReadOnlyList<PathPoint> Points { get; init; }
    public required IReadOnlyDictionary<string, object?> Fields { get; init; }
}

public static class StagePathReader
{
    public static List<PathData> ReadPaths(RARCArchive mapArchive)
    {
        RARCFile? pathInfoFile = mapArchive.Root.FindFile("jmp/Path/CommonPathInfo");
        if (pathInfoFile is null)
        {
            return [];
        }

        BCSVTable pathTable = BCSVTable.Load(pathInfoFile.Data);
        var result = new List<PathData>(pathTable.Rows.Count);

        foreach (IReadOnlyDictionary<string, object?> row in pathTable.Rows)
        {
            int no = GetInt(row, "no");
            var points = new List<PathPoint>();

            RARCFile? pointsFile = mapArchive.Root.FindFile($"jmp/Path/CommonPathPointInfo.{no}");
            if (pointsFile is not null)
            {
                BCSVTable pointsTable = BCSVTable.Load(pointsFile.Data);
                foreach (IReadOnlyDictionary<string, object?> prow in pointsTable.Rows)
                {
                    points.Add(new PathPoint
                    {
                        Position = ReadVector3(prow, "pnt0_x", "pnt0_y", "pnt0_z"),
                        ControlPointIn = ReadVector3(prow, "pnt1_x", "pnt1_y", "pnt1_z"),
                        ControlPointOut = ReadVector3(prow, "pnt2_x", "pnt2_y", "pnt2_z"),
                        Fields = new Dictionary<string, object?>(prow),
                    });
                }
            }

            result.Add(new PathData
            {
                Name = row.TryGetValue("name", out object? name) ? (string?)name ?? "" : "",
                LinkId = GetInt(row, "l_id"),
                No = no,
                Closed = row.TryGetValue("closed", out object? closed) && (string?)closed == "CLOSE",
                Usage = row.TryGetValue("usage", out object? usage) ? (string?)usage ?? "" : "",
                Points = points,
                Fields = row,
            });
        }

        return result;
    }

    private static int GetInt(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out object? v) && v is int i ? i : 0;

    private static Vector3 ReadVector3(IReadOnlyDictionary<string, object?> row, string x, string y, string z)
    {
        if (row.TryGetValue(x, out object? xv) && xv is float xf &&
            row.TryGetValue(y, out object? yv) && yv is float yf &&
            row.TryGetValue(z, out object? zv) && zv is float zf)
        {
            return new Vector3(xf, yf, zf);
        }

        return Vector3.Zero;
    }
}

public static class PathTessellator
{
    public static List<Vector3> BuildPolyline(IReadOnlyList<PathPoint> points, bool closed, int samplesPerSegment = 16)
    {
        var result = new List<Vector3>();
        if (points.Count < 2)
        {
            if (points.Count == 1)
            {
                result.Add(points[0].Position);
            }

            return result;
        }

        int segmentCount = closed ? points.Count : points.Count - 1;
        result.Add(points[0].Position);

        for (int i = 0; i < segmentCount; i++)
        {
            PathPoint a = points[i];
            PathPoint b = points[(i + 1) % points.Count];

            for (int s = 1; s <= samplesPerSegment; s++)
            {
                float t = s / (float)samplesPerSegment;
                result.Add(SampleCubicBezier(a.Position, a.ControlPointOut, b.ControlPointIn, b.Position, t));
            }
        }

        return result;
    }

    private static Vector3 SampleCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }
}
