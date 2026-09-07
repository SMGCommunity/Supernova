using System.Numerics;

namespace SMGEditor.Core.Collision;

internal static class KclMath
{
    public static void SeparateScalarAndDirection(Vector3 vec, out float scalar, out Vector3 direction)
    {
        scalar = vec.Length();
        direction = IsNearZeroComponentwise(vec, 0.001f) ? Vector3.Zero : Vector3.Normalize(vec);
    }

    public static bool IsSameDirection(Vector3 v1, Vector3 v2, float tolerance = 0.01f)
    {
        if (MathF.Abs(v1.Y * v2.Z - v1.Z * v2.Y) > tolerance)
        {
            return false;
        }

        if (MathF.Abs(v1.Z * v2.X - v1.X * v2.Z) > tolerance)
        {
            return false;
        }

        if (MathF.Abs(v1.X * v2.Y - v1.Y * v2.X) > tolerance)
        {
            return false;
        }

        return true;
    }

    public static bool IsNearZero(float x, float tolerance = 0.001f) => MathF.Abs(x) < tolerance;

    public static bool IsNearZeroComponentwise(Vector3 v, float tolerance = 0.001f) =>
        MathF.Abs(v.X) <= tolerance && MathF.Abs(v.Y) <= tolerance && MathF.Abs(v.Z) <= tolerance;
}
