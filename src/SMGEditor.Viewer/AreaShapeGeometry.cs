using System.Numerics;

namespace SMGEditor.Viewer;

public static class AreaShapeGeometry
{
    public static List<(Vector3 A, Vector3 B)> Box(float offsetY)
    {
        const float half = 500f;
        Vector3 c000 = new(-half, offsetY - half, -half), c001 = new(-half, offsetY - half, half);
        Vector3 c010 = new(-half, offsetY + half, -half), c011 = new(-half, offsetY + half, half);
        Vector3 c100 = new(half, offsetY - half, -half), c101 = new(half, offsetY - half, half);
        Vector3 c110 = new(half, offsetY + half, -half), c111 = new(half, offsetY + half, half);

        return
        [
            (c000, c001), (c001, c011), (c011, c010), (c010, c000),
            (c100, c101), (c101, c111), (c111, c110), (c110, c100),
            (c000, c100), (c001, c101), (c011, c111), (c010, c110),
        ];
    }

    public static List<(Vector3 A, Vector3 B)> Sphere(float minYFrac, float maxYFrac)
    {
        const float radius = 500f;
        const int latSegments = 8;
        const int lonSegments = 16;
        var edges = new List<(Vector3, Vector3)>();

        for (int lat = 0; lat <= latSegments; lat++)
        {
            float theta = MathF.PI * (lat / (float)latSegments);
            float y = radius * MathF.Cos(theta);
            if (y / radius < minYFrac || y / radius > maxYFrac)
            {
                continue;
            }

            float ringRadius = radius * MathF.Sin(theta);
            for (int lon = 0; lon < lonSegments; lon++)
            {
                float a0 = lon / (float)lonSegments * MathF.Tau;
                float a1 = (lon + 1) / (float)lonSegments * MathF.Tau;
                edges.Add((
                    new Vector3(ringRadius * MathF.Cos(a0), y, ringRadius * MathF.Sin(a0)),
                    new Vector3(ringRadius * MathF.Cos(a1), y, ringRadius * MathF.Sin(a1))));
            }
        }

        for (int lon = 0; lon < lonSegments; lon += 4)
        {
            float angle = lon / (float)lonSegments * MathF.Tau;
            Vector3? prev = null;
            for (int lat = 0; lat <= latSegments * 2; lat++)
            {
                float theta = MathF.PI * (lat / (float)(latSegments * 2));
                float y = radius * MathF.Cos(theta);
                float ringRadius = radius * MathF.Sin(theta);
                var p = new Vector3(ringRadius * MathF.Cos(angle), y, ringRadius * MathF.Sin(angle));
                if (y / radius >= minYFrac && y / radius <= maxYFrac)
                {
                    if (prev is { } pv)
                    {
                        edges.Add((pv, p));
                    }

                    prev = p;
                }
                else
                {
                    prev = null;
                }
            }
        }

        return edges;
    }

    public static List<(Vector3 A, Vector3 B)> Cylinder()
    {
        const float radius = 500f;
        const float height = 1000f;
        const int segments = 16;
        var edges = new List<(Vector3, Vector3)>();

        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * MathF.Tau;
            float a1 = (i + 1) / (float)segments * MathF.Tau;
            var b0 = new Vector3(radius * MathF.Cos(a0), 0f, radius * MathF.Sin(a0));
            var b1 = new Vector3(radius * MathF.Cos(a1), 0f, radius * MathF.Sin(a1));
            var t0 = new Vector3(radius * MathF.Cos(a0), height, radius * MathF.Sin(a0));
            var t1 = new Vector3(radius * MathF.Cos(a1), height, radius * MathF.Sin(a1));
            edges.Add((b0, b1));
            edges.Add((t0, t1));
            if (i % 4 == 0)
            {
                edges.Add((b0, t0));
            }
        }

        return edges;
    }

    public static List<(Vector3 A, Vector3 B)> ForShape(SceneRenderer.AreaShapeKind shape) => shape switch
    {
        SceneRenderer.AreaShapeKind.BaseOriginBox => Box(500f),
        SceneRenderer.AreaShapeKind.CenterOriginBox => Box(0f),
        SceneRenderer.AreaShapeKind.Sphere => Sphere(-1f, 1f),
        SceneRenderer.AreaShapeKind.Cylinder => Cylinder(),
        SceneRenderer.AreaShapeKind.Bowl => Sphere(-1f, 0f),
        _ => Box(500f),
    };
}
