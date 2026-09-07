using System.Numerics;

namespace SMGEditor.Core.Gravity;

internal static class GravityMath
{
    public static void SeparateScalarAndDirection(Vector3 vec, out float scalar, out Vector3 direction)
    {
        scalar = vec.Length();
        direction = scalar > 1e-6f ? vec / scalar : Vector3.Zero;
    }

    public static Vector3 NormalizeOrZero(Vector3 vec) => VectorMath.NormalizeOrZero(vec);

    public static Vector3 KillElement(Vector3 vec, Vector3 killDir) => vec - (killDir * Vector3.Dot(killDir, vec));

    public static Vector3 PerpendicularFootClampedToSegment(Vector3 pos, Vector3 a, Vector3 b)
    {
        Vector3 offset = b - a;
        float lenSq = offset.LengthSquared();
        float t = lenSq > 1e-8f ? Vector3.Dot(pos - a, offset) / lenSq : 0f;
        t = Math.Clamp(t, 0f, 1f);
        return a + (offset * t);
    }

    public static Vector3 RotateAroundAxis(Vector3 vec, Vector3 axis, float angleRadians) =>
        Vector3.Transform(vec, Quaternion.CreateFromAxisAngle(NormalizeOrZero(axis), angleRadians));

    public static bool IsNearZero(Vector3 vec) => vec.LengthSquared() < 1e-6f;

    public static bool IsNearZero(float value) => MathF.Abs(value) < 1e-6f;
}
