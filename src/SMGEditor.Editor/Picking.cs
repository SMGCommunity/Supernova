using System.Numerics;
using SMGEditor.Core.Stage;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal static class Picking
{
    public readonly record struct Ray(Vector3 Origin, Vector3 Direction);

    public static Ray ScreenPointToRay(Vector2 pixelPos, Vector2 viewportSize, Matrix4x4 view, Matrix4x4 projection)
    {
        float ndcX = pixelPos.X / viewportSize.X * 2f - 1f;
        float ndcY = 1f - pixelPos.Y / viewportSize.Y * 2f;

        Matrix4x4.Invert(view * projection, out Matrix4x4 invViewProj);

        Vector4 nearH = Vector4.Transform(new Vector4(ndcX, ndcY, 0f, 1f), invViewProj);
        Vector4 farH = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invViewProj);
        var nearPoint = new Vector3(nearH.X, nearH.Y, nearH.Z) / nearH.W;
        var farPoint = new Vector3(farH.X, farH.Y, farH.Z) / farH.W;

        return new Ray(nearPoint, Vector3.Normalize(farPoint - nearPoint));
    }

    public static float? IntersectAabb(Ray ray, Vector3 boundsMin, Vector3 boundsMax)
    {
        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;

        for (int axis = 0; axis < 3; axis++)
        {
            float origin = axis == 0 ? ray.Origin.X : axis == 1 ? ray.Origin.Y : ray.Origin.Z;
            float dir = axis == 0 ? ray.Direction.X : axis == 1 ? ray.Direction.Y : ray.Direction.Z;
            float min = axis == 0 ? boundsMin.X : axis == 1 ? boundsMin.Y : boundsMin.Z;
            float max = axis == 0 ? boundsMax.X : axis == 1 ? boundsMax.Y : boundsMax.Z;

            if (MathF.Abs(dir) < 1e-8f)
            {
                if (origin < min || origin > max)
                {
                    return null;
                }

                continue;
            }

            float t1 = (min - origin) / dir;
            float t2 = (max - origin) / dir;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax)
            {
                return null;
            }
        }

        return tMax < 0 ? null : MathF.Max(tMin, 0f);
    }

    public static EditableObject? Pick(Ray ray, IEnumerable<EditableObject> objects)
    {
        EditableObject? best = null;
        float bestT = float.PositiveInfinity;

        foreach (EditableObject obj in objects)
        {
            if (obj.InternalName.Contains("Sky", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (obj.Instance is null && obj.SourceList is "CameraCubeInfo" or "AreaObjInfo" or "PlanetObjInfo")
            {
                continue;
            }

            bool hasRenderableInstance = false;
            foreach (ObjectInstance instance in obj.AllInstances)
            {
                hasRenderableInstance = true;
                LoadedObject model = instance.Object;
                (Vector3 boundsMin, Vector3 boundsMax) = TransformedBounds(model.LocalBoundsMin, model.LocalBoundsMax, instance.WorldMatrix);

                float? broad = IntersectAabb(ray, boundsMin, boundsMax);
                if (broad is not float broadT || broadT >= bestT)
                {
                    continue;
                }

                if (IntersectMeshTriangles(ray, model, instance.WorldMatrix) is float hit && hit < bestT)
                {
                    bestT = hit;
                    best = obj;
                }
            }

            if (!hasRenderableInstance)
            {
                const float half = SceneRenderer.PlaceholderBoxHalfExtent;
                Matrix4x4 world = GalaxyLoader.ComposePlacementMatrix(obj.Position, obj.Rotation, Vector3.One);
                (Vector3 boundsMin, Vector3 boundsMax) = TransformedBounds(new Vector3(-half), new Vector3(half), world);

                if (IntersectAabb(ray, boundsMin, boundsMax) is float hit && hit < bestT)
                {
                    bestT = hit;
                    best = obj;
                }
            }
        }

        return best;
    }

    public static Vector3? RaycastScenePoint(Ray ray, IEnumerable<EditableObject> objects)
    {
        float bestT = float.PositiveInfinity;
        Vector3? bestPoint = null;

        foreach (EditableObject obj in objects)
        {
            if (obj.Instance is not { } instance || obj.InternalName.Contains("Sky", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            LoadedObject model = instance.Object;
            (Vector3 boundsMin, Vector3 boundsMax) = TransformedBounds(model.LocalBoundsMin, model.LocalBoundsMax, instance.WorldMatrix);

            float? broad = IntersectAabb(ray, boundsMin, boundsMax);
            if (broad is not float broadT || broadT >= bestT)
            {
                continue;
            }

            if (IntersectMeshTriangles(ray, model, instance.WorldMatrix) is float hit && hit < bestT)
            {
                bestT = hit;
                bestPoint = ray.Origin + ray.Direction * hit;
            }
        }

        return bestPoint;
    }

    private static float? IntersectMeshTriangles(Ray ray, LoadedObject model, Matrix4x4 world)
    {
        const int stride = 18;
        float? best = null;

        foreach (GpuMesh mesh in model.Meshes)
        {
            float[] v = mesh.Vertices;
            int triCount = mesh.VertexCount / 3;
            for (int i = 0; i < triCount; i++)
            {
                int b = i * 3 * stride;
                Vector3 p0 = Vector3.Transform(new Vector3(v[b], v[b + 1], v[b + 2]), world);
                Vector3 p1 = Vector3.Transform(new Vector3(v[b + stride], v[b + stride + 1], v[b + stride + 2]), world);
                Vector3 p2 = Vector3.Transform(new Vector3(v[b + stride * 2], v[b + stride * 2 + 1], v[b + stride * 2 + 2]), world);

                if (IntersectTriangle(ray, p0, p1, p2) is float hit && (best is null || hit < best))
                {
                    best = hit;
                }
            }
        }

        return best;
    }

    private static float? IntersectTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        const float epsilon = 1e-6f;
        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        Vector3 h = Vector3.Cross(ray.Direction, edge2);
        float a = Vector3.Dot(edge1, h);
        if (MathF.Abs(a) < epsilon)
        {
            return null;
        }

        float f = 1f / a;
        Vector3 s = ray.Origin - v0;
        float u = f * Vector3.Dot(s, h);
        if (u < 0f || u > 1f)
        {
            return null;
        }

        Vector3 q = Vector3.Cross(s, edge1);
        float vCoord = f * Vector3.Dot(ray.Direction, q);
        if (vCoord < 0f || u + vCoord > 1f)
        {
            return null;
        }

        float t = f * Vector3.Dot(edge2, q);
        return t > epsilon ? t : null;
    }

    public static EditablePath? PickPath(Vector2 pixelPos, Vector2 viewportSize, Matrix4x4 view, Matrix4x4 projection, IEnumerable<EditablePath> paths, float pixelThreshold = 8f)
    {
        Matrix4x4 viewProj = view * projection;
        EditablePath? best = null;
        float bestDist = pixelThreshold;

        foreach (EditablePath path in paths)
        {
            IReadOnlyList<Vector3> points = path.WorldPolyline;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                if (TryProjectToScreen(points[i], viewProj, viewportSize, out Vector2 a) &&
                    TryProjectToScreen(points[i + 1], viewProj, viewportSize, out Vector2 b))
                {
                    float dist = DistancePointToSegment(pixelPos, a, b);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = path;
                    }
                }
            }
        }

        return best;
    }

    public static (EditablePath Path, int PointIndex, PathPointPart Part)? PickPathPoint(Vector2 pixelPos, Vector2 viewportSize, Matrix4x4 view, Matrix4x4 projection, IEnumerable<EditablePath> paths, float pixelThreshold = 10f)
    {
        Matrix4x4 viewProj = view * projection;
        EditablePath? bestPath = null;
        int bestIndex = -1;
        PathPointPart bestPart = PathPointPart.Anchor;
        float bestDist = pixelThreshold;

        foreach (EditablePath path in paths)
        {
            for (int i = 0; i < path.WorldPoints.Count; i++)
            {
                PathPoint point = path.WorldPoints[i];
                CheckMarker(point.Position, PathPointPart.Anchor);
                CheckMarker(point.ControlPointIn, PathPointPart.ControlIn);
                CheckMarker(point.ControlPointOut, PathPointPart.ControlOut);

                void CheckMarker(Vector3 worldPos, PathPointPart part)
                {
                    if (TryProjectToScreen(worldPos, viewProj, viewportSize, out Vector2 screen))
                    {
                        float dist = Vector2.Distance(pixelPos, screen);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestPath = path;
                            bestIndex = i;
                            bestPart = part;
                        }
                    }
                }
            }
        }

        return bestPath is not null ? (bestPath, bestIndex, bestPart) : null;
    }

    public static EditableObject? PickAreaShapeBorder(Vector2 pixelPos, Vector2 viewportSize, Matrix4x4 view, Matrix4x4 projection, IEnumerable<(EditableObject Obj, SceneRenderer.AreaShapeKind Shape, Matrix4x4 World)> shapes, float pixelThreshold = 8f)
    {
        Matrix4x4 viewProj = view * projection;
        EditableObject? best = null;
        float bestDist = pixelThreshold;

        foreach ((EditableObject obj, SceneRenderer.AreaShapeKind shape, Matrix4x4 world) in shapes)
        {
            foreach ((Vector3 a, Vector3 b) in AreaShapeGeometry.ForShape(shape))
            {
                Vector3 worldA = Vector3.Transform(a, world);
                Vector3 worldB = Vector3.Transform(b, world);
                if (TryProjectToScreen(worldA, viewProj, viewportSize, out Vector2 screenA) &&
                    TryProjectToScreen(worldB, viewProj, viewportSize, out Vector2 screenB))
                {
                    float dist = DistancePointToSegment(pixelPos, screenA, screenB);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = obj;
                    }
                }
            }
        }

        return best;
    }

    private static bool TryProjectToScreen(Vector3 worldPos, Matrix4x4 viewProj, Vector2 viewportSize, out Vector2 screen)
    {
        Vector4 clip = Vector4.Transform(new Vector4(worldPos, 1f), viewProj);
        if (clip.W <= 0.0001f)
        {
            screen = default;
            return false;
        }

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        screen = new Vector2((ndc.X * 0.5f + 0.5f) * viewportSize.X, (1f - (ndc.Y * 0.5f + 0.5f)) * viewportSize.Y);
        return true;
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.LengthSquared();
        float t = lenSq > 1e-6f ? Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f) : 0f;
        Vector2 closest = a + ab * t;
        return Vector2.Distance(p, closest);
    }

    private static (Vector3 Min, Vector3 Max) TransformedBounds(Vector3 localMin, Vector3 localMax, Matrix4x4 world)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? localMin.X : localMax.X,
                (i & 2) == 0 ? localMin.Y : localMax.Y,
                (i & 4) == 0 ? localMin.Z : localMax.Z);
            Vector3 world3 = Vector3.Transform(corner, world);
            min = Vector3.Min(min, world3);
            max = Vector3.Max(max, world3);
        }

        return (min, max);
    }
}
