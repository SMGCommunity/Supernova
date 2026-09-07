using System.Numerics;

namespace SMGEditor.Core.Gravity;

public abstract class GravityGenerator
{
    public float Range { get; set; } = -1f;

    public float Distant { get; set; }

    public int Priority { get; set; }

    public bool IsInverse { get; set; }

    protected abstract bool TryCalcOwnGravity(Vector3 position, out Vector3 direction, out float distance);

    public bool TryCalcGravity(Vector3 position, out Vector3 gravity)
    {
        if (!TryCalcOwnGravity(position, out Vector3 direction, out float distance))
        {
            gravity = Vector3.Zero;
            return false;
        }

        float radius = MathF.Max(distance - Distant, 1f);
        float scalar = 4_000_000f / (radius * radius);
        gravity = direction * scalar;
        if (IsInverse)
        {
            gravity = -gravity;
        }

        return true;
    }

    protected bool IsInRangeDistance(float distance) => Range < 0f || distance < Range + Distant;

    protected bool IsInRangeSquare(float distanceSquared)
    {
        if (Range < 0f)
        {
            return true;
        }

        float d = Range + Distant;
        return distanceSquared < d * d;
    }

    protected bool CalcGravityFromMassPosition(Vector3 position, Vector3 massPosition, out Vector3 direction, out float distance)
    {
        GravityMath.SeparateScalarAndDirection(massPosition - position, out distance, out direction);
        return IsInRangeDistance(distance);
    }
}
