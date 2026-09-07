using System.Numerics;

namespace SMGEditor.Core.Gravity;

public sealed class WireGravity(IReadOnlyList<Vector3> points) : GravityGenerator
{
    protected override bool TryCalcOwnGravity(Vector3 position, out Vector3 direction, out float distance)
    {
        direction = Vector3.Zero;
        distance = 0f;

        float bestSquareDistance = -1f;
        Vector3 pointOfAttraction = Vector3.Zero;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 foot = GravityMath.PerpendicularFootClampedToSegment(position, points[i], points[i + 1]);
            float squareDistance = Vector3.DistanceSquared(position, foot);
            if (squareDistance < bestSquareDistance || bestSquareDistance < 0f)
            {
                pointOfAttraction = foot;
                bestSquareDistance = squareDistance;
            }
        }

        if (!IsInRangeSquare(bestSquareDistance))
        {
            return false;
        }

        if (bestSquareDistance < 0f)
        {
            return false;
        }

        GravityMath.SeparateScalarAndDirection(pointOfAttraction - position, out distance, out direction);
        return true;
    }
}
