using System.Numerics;
using SMGEditor.Core.Gravity;
using SMGEditor.Core.Stage;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal static class GravityZoneBuilder
{
    private static readonly HashSet<string> GravityClassNames =
    [
        "GlobalPointGravity", "GlobalCubeGravity", "GlobalDiskGravity", "GlobalDiskTorusGravity", "GlobalConeGravity",
        "GlobalPlaneGravity", "GlobalPlaneGravityInBox", "GlobalPlaneGravityInCylinder", "GlobalSegmentGravity", "GlobalWireGravity",
    ];

    public static GravityZoneSet Build(GalaxySession session, string stagePath)
    {
        var generators = new List<GravityGenerator>();

        foreach (EditableObject obj in session.Objects)
        {
            if (obj.StagePath != stagePath)
            {
                continue;
            }

            string? className = obj.DbEntry?.ClassName(session.Game);
            if (className is null || !GravityClassNames.Contains(className))
            {
                continue;
            }

            GravityGenerator? generator = BuildOne(session, obj, className);
            if (generator is not null)
            {
                generators.Add(generator);
            }
        }

        return new GravityZoneSet(generators);
    }

    private static GravityGenerator? BuildOne(GalaxySession session, EditableObject obj, string className)
    {
        Vector3 pos = obj.Position;
        Vector3 scale = obj.Scale;
        Matrix4x4 rotMtx = GalaxyLoader.ComposeRotationMatrix(obj.Rotation);
        Vector3 xDir = Vector3.TransformNormal(Vector3.UnitX, rotMtx);
        Vector3 yDir = Vector3.TransformNormal(Vector3.UnitY, rotMtx);
        Vector3 zDir = Vector3.TransformNormal(Vector3.UnitZ, rotMtx);

        GravityGenerator? generator = className switch
        {
            "GlobalPointGravity" => BuildPoint(pos, scale),
            "GlobalCubeGravity" => BuildCube(obj, pos, scale, xDir, yDir, zDir),
            "GlobalDiskGravity" => BuildDisk(obj, pos, scale, xDir, yDir),
            "GlobalDiskTorusGravity" => BuildDiskTorus(obj, pos, scale, yDir),
            "GlobalConeGravity" => BuildCone(obj, pos, scale, xDir, yDir),
            "GlobalPlaneGravity" => ParallelGravity.CreatePlane(pos, yDir),
            "GlobalPlaneGravityInBox" => BuildPlaneInBox(obj, pos, scale, xDir, yDir, zDir),
            "GlobalPlaneGravityInCylinder" => BuildPlaneInCylinder(obj, pos, scale, yDir),
            "GlobalSegmentGravity" => BuildSegment(obj, pos, xDir, yDir),
            "GlobalWireGravity" => BuildWire(session, obj),
            _ => null,
        };

        if (generator is null)
        {
            return null;
        }

        ApplyCommonFields(generator, obj.Fields);
        return generator;
    }

    private static PointGravity BuildPoint(Vector3 pos, Vector3 scale)
    {
        var gravity = new PointGravity(pos) { Distant = 500f * scale.X };
        return gravity;
    }

    private static CubeGravity BuildCube(EditableObject obj, Vector3 pos, Vector3 scale, Vector3 xDir, Vector3 yDir, Vector3 zDir)
    {
        Vector3 translation = pos + (yDir * (scale.Y * 500f));
        Vector3 scaledX = xDir * (scale.X * 500f);
        Vector3 scaledY = yDir * (scale.Y * 500f);
        Vector3 scaledZ = zDir * (scale.Z * 500f);

        int argX = ReadArg(obj.Fields, "Obj_arg0");
        int argY = ReadArg(obj.Fields, "Obj_arg1");
        int argZ = ReadArg(obj.Fields, "Obj_arg2");

        byte activeFaces = 0;
        if ((argX & 1) != 0)
        {
            activeFaces |= 1;
        }

        if ((argX & 2) != 0)
        {
            activeFaces |= 2;
        }

        if ((argY & 1) != 0)
        {
            activeFaces |= 4;
        }

        if ((argY & 2) != 0)
        {
            activeFaces |= 8;
        }

        if ((argZ & 1) != 0)
        {
            activeFaces |= 16;
        }

        if ((argZ & 2) != 0)
        {
            activeFaces |= 32;
        }

        return new CubeGravity(translation, scaledX, scaledY, scaledZ, activeFaces);
    }

    private static DiskGravity BuildDisk(EditableObject obj, Vector3 pos, Vector3 scale, Vector3 xDir, Vector3 yDir)
    {
        int arg0 = ReadArg(obj.Fields, "Obj_arg0");
        int arg1 = ReadArg(obj.Fields, "Obj_arg1");
        int arg2 = ReadArg(obj.Fields, "Obj_arg2");

        float radius = 500f * MaxElement(scale);
        bool bothSide = arg0 != 0;
        bool edgeGravity = arg1 != 0;
        float validDegree = arg2 >= 0 ? arg2 : 360f;

        return new DiskGravity(pos, yDir, xDir, radius, validDegree, bothSide, edgeGravity);
    }

    private static DiskTorusGravity BuildDiskTorus(EditableObject obj, Vector3 pos, Vector3 scale, Vector3 yDir)
    {
        int arg0 = ReadArg(obj.Fields, "Obj_arg0");
        int arg1 = ReadArg(obj.Fields, "Obj_arg1");
        int arg2 = ReadArg(obj.Fields, "Obj_arg2");

        float radius = 500f * MaxElement(scale);
        bool bothSide = arg0 != 0;
        int edgeType = arg1 switch
        {
            0 => 0,
            1 => 1,
            2 => 2,
            _ => 3,
        };
        float diskRadius = MathF.Max(arg2, 0f);

        return new DiskTorusGravity(pos, yDir, radius, diskRadius, edgeType, bothSide);
    }

    private static ConeGravity BuildCone(EditableObject obj, Vector3 pos, Vector3 scale, Vector3 xDir, Vector3 yDir)
    {
        int arg0 = ReadArg(obj.Fields, "Obj_arg0");
        int arg1 = ReadArg(obj.Fields, "Obj_arg1");

        bool enableBottom = arg0 != 0;
        float topCutRate = Math.Clamp(arg1 / 1000f, 0f, 1f);
        Vector3 centralAxis = yDir * (scale.Y * 500f);
        float radius = (xDir * (scale.X * 500f)).Length();

        return new ConeGravity(pos, centralAxis, radius, enableBottom, topCutRate);
    }

    private static ParallelGravity BuildPlaneInBox(EditableObject obj, Vector3 pos, Vector3 scale, Vector3 xDir, Vector3 yDir, Vector3 zDir)
    {
        int arg0 = ReadArg(obj.Fields, "Obj_arg0");
        int arg1 = ReadArg(obj.Fields, "Obj_arg1");

        float baseDistance = arg0 >= 0 ? arg0 : 2000f;
        ParallelGravityDistanceCalcType calcType = arg1 switch
        {
            0 => ParallelGravityDistanceCalcType.X,
            1 => ParallelGravityDistanceCalcType.Y,
            2 => ParallelGravityDistanceCalcType.Z,
            _ => ParallelGravityDistanceCalcType.Default,
        };

        Vector3 boxTranslation = pos + (yDir * (scale.Y * 500f));
        Vector3 boxX = xDir * (scale.X * 500f);
        Vector3 boxY = yDir * (scale.Y * 500f);
        Vector3 boxZ = zDir * (scale.Z * 500f);

        return ParallelGravity.CreateBox(pos, yDir, baseDistance, calcType, boxTranslation, boxX, boxY, boxZ);
    }

    private static ParallelGravity BuildPlaneInCylinder(EditableObject obj, Vector3 pos, Vector3 scale, Vector3 yDir)
    {
        int arg0 = ReadArg(obj.Fields, "Obj_arg0");
        float baseDistance = arg0 >= 0 ? arg0 : 2000f;
        float cylinderRadius = 500f * scale.X;
        float cylinderHeight = 500f * scale.Y;

        return ParallelGravity.CreateCylinder(pos, yDir, baseDistance, cylinderRadius, cylinderHeight);
    }

    private static SegmentGravity BuildSegment(EditableObject obj, Vector3 pos, Vector3 xDir, Vector3 yDir)
    {
        int arg0 = ReadArg(obj.Fields, "Obj_arg0");
        int arg1 = ReadArg(obj.Fields, "Obj_arg1");

        bool edge0Valid = arg0 == 1 || arg0 >= 3 || arg0 < 0;
        bool edge1Valid = arg0 == 2 || arg0 >= 3 || arg0 < 0;
        float validSideDegree = arg1 >= 0 ? arg1 : 360f;

        Vector3 point0 = pos;
        Vector3 point1 = pos + (yDir * 1000f);

        return new SegmentGravity(point0, point1, xDir, validSideDegree, edge0Valid, edge1Valid);
    }

    private static WireGravity? BuildWire(GalaxySession session, EditableObject obj)
    {
        int? pathLinkId = obj.Fields.TryGetValue("CommonPath_ID", out object? cpid) && cpid is int cpidValue && cpidValue != 65535
            ? cpidValue
            : null;
        EditablePath? rail = pathLinkId is null
            ? null
            : session.Paths.FirstOrDefault(p => p.StagePath == obj.StagePath && p.LinkId == pathLinkId);

        if (rail is null || rail.WorldPoints.Count == 0)
        {
            return null;
        }

        var table = new RailCoordSampleTable(rail.WorldPoints, rail.Closed);
        if (table.TotalLength <= 0f)
        {
            return null;
        }

        int numPointsInBetween = ReadArg(obj.Fields, "Obj_arg0");
        if (numPointsInBetween < 0)
        {
            numPointsInBetween = 20;
        }

        int numPoints = numPointsInBetween + 2;
        float interval = table.TotalLength / (numPoints - 1);

        var points = new List<Vector3>(numPoints - 1);
        for (int i = 0; i < numPoints - 1; i++)
        {
            points.Add(table.PositionAtCoord(i * interval));
        }

        return new WireGravity(points);
    }

    private static void ApplyCommonFields(GravityGenerator generator, IReadOnlyDictionary<string, object?> fields)
    {
        float range = ReadFloat(fields, "Range");
        float distant = ReadFloat(fields, "Distant");
        int priority = ReadArg(fields, "Priority");
        int inverse = ReadArg(fields, "Inverse");

        if (range >= 0f)
        {
            generator.Range = range;
        }

        if (distant >= 0f)
        {
            generator.Distant = distant;
        }

        if (priority >= 0)
        {
            generator.Priority = priority;
        }

        if (inverse >= 0)
        {
            generator.IsInverse = inverse != 0;
        }
    }

    private static int ReadArg(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out object? v) && v is int i ? i : -1;

    private static float ReadFloat(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out object? v) && v is float f ? f : -1f;

    private static float MaxElement(Vector3 v) => MathF.Max(v.X, MathF.Max(v.Y, v.Z));
}
