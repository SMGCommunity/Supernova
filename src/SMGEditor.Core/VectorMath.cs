using System.Numerics;

namespace SMGEditor.Core;

internal static class VectorMath
{
    public static Vector3 NormalizeOrZero(Vector3 vec, float toleranceSq = 1e-12f)
    {
        float lenSq = vec.LengthSquared();
        return lenSq > toleranceSq ? vec / MathF.Sqrt(lenSq) : Vector3.Zero;
    }

    public static Vector3 SampleCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }
}
