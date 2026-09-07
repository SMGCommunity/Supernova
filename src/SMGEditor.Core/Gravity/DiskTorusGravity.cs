using System.Numerics;

namespace SMGEditor.Core.Gravity;

public sealed class DiskTorusGravity(Vector3 translation, Vector3 rotation, float worldRadius, float diskRadius, int edgeType, bool enableBothSide)
    : GravityGenerator
{
    private const int DisableBothEdgeGravity = 0;
    private const int DisableOuterEdgeGravity = 1;
    private const int DisableInnerEdgeGravity = 2;

    protected override bool TryCalcOwnGravity(Vector3 position, out Vector3 direction, out float distance)
    {
        direction = Vector3.Zero;
        distance = 0f;

        Vector3 relativePosition = position - translation;
        float centralAxisY = Vector3.Dot(relativePosition, rotation);

        if (!enableBothSide && centralAxisY < 0f)
        {
            return false;
        }

        Vector3 dirOnTorusPlaneRaw = relativePosition - (rotation * centralAxisY);
        GravityMath.SeparateScalarAndDirection(dirOnTorusPlaneRaw, out float distanceToCentralAxis, out Vector3 dirOnTorusPlane);
        if (GravityMath.IsNearZero(distanceToCentralAxis))
        {
            dirOnTorusPlane = MakeAxisVerticalZX(rotation);
        }

        float innerRadius = worldRadius - diskRadius;
        Vector3 gravity;

        if (distanceToCentralAxis < innerRadius)
        {
            if (edgeType is DisableBothEdgeGravity or DisableInnerEdgeGravity)
            {
                return false;
            }

            Vector3 nearestInnerEdgePoint = translation + (dirOnTorusPlane * innerRadius);
            gravity = nearestInnerEdgePoint - position;
            GravityMath.SeparateScalarAndDirection(gravity, out distance, out direction);
        }
        else if (distanceToCentralAxis > worldRadius)
        {
            if (edgeType is DisableBothEdgeGravity or DisableOuterEdgeGravity)
            {
                return false;
            }

            Vector3 nearestOuterEdgePoint = translation + (dirOnTorusPlane * worldRadius);
            gravity = nearestOuterEdgePoint - position;
            GravityMath.SeparateScalarAndDirection(gravity, out distance, out direction);
        }
        else
        {
            direction = centralAxisY >= 0f ? -rotation : rotation;
            distance = MathF.Abs(centralAxisY);
        }

        return IsInRangeDistance(distance);
    }

    private static Vector3 MakeAxisVerticalZX(Vector3 axis)
    {
        Vector3 vec = GravityMath.KillElement(Vector3.UnitZ, axis);
        if (GravityMath.IsNearZero(vec))
        {
            vec = GravityMath.KillElement(Vector3.UnitX, axis);
        }

        return GravityMath.NormalizeOrZero(vec);
    }
}
