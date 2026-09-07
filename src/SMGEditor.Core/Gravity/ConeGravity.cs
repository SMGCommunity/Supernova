using System.Numerics;

namespace SMGEditor.Core.Gravity;

public sealed class ConeGravity(Vector3 baseCenter, Vector3 centralAxis, float radius, bool enableBottom, float topCutRate) : GravityGenerator
{
    protected override bool TryCalcOwnGravity(Vector3 position, out Vector3 direction, out float distance)
    {
        direction = Vector3.Zero;
        distance = 0f;

        GravityMath.SeparateScalarAndDirection(centralAxis, out float centralAxisLength, out Vector3 unitAxis);

        Vector3 relativePosition = position - baseCenter;
        Vector3 positionOnBasePlane = GravityMath.KillElement(relativePosition, unitAxis);

        if (GravityMath.IsNearZero(positionOnBasePlane))
        {
            float positionOnCentralAxis = Vector3.Dot(relativePosition, unitAxis);
            float axisDistance = MathF.Abs(positionOnCentralAxis);

            if (positionOnCentralAxis > 0f)
            {
                float height = centralAxisLength * (1f - topCutRate);
                axisDistance = MathF.Max(axisDistance - height, 0f);
            }

            if (!IsInRangeDistance(axisDistance))
            {
                return false;
            }

            direction = positionOnCentralAxis > 0f ? -unitAxis : unitAxis;
            distance = axisDistance;
            return true;
        }

        float distanceToCentralAxis = positionOnBasePlane.Length();
        float centralAxisY = Vector3.Dot(unitAxis, relativePosition);

        bool isInsideCone = false;
        if (!GravityMath.IsNearZero(centralAxisLength) && !GravityMath.IsNearZero(radius) &&
            distanceToCentralAxis < radius - (centralAxisY * (radius / centralAxisLength)))
        {
            isInsideCone = true;
        }

        Vector3 dirOnDirectrix = baseCenter + (positionOnBasePlane * (radius / distanceToCentralAxis));
        Vector3 apex = baseCenter + centralAxis;

        if (Vector3.Dot(relativePosition, centralAxis) < 0f)
        {
            if (!enableBottom)
            {
                return false;
            }

            Vector3 footOnBase = GravityMath.PerpendicularFootClampedToSegment(position, baseCenter, dirOnDirectrix);
            if (GravityMath.IsNearZero(footOnBase - position))
            {
                direction = -unitAxis;
                distance = 0f;
                return true;
            }

            return CalcGravityFromMassPosition(position, footOnBase, out direction, out distance);
        }

        Vector3 pointOfAttraction;
        if (topCutRate < 0.01f)
        {
            pointOfAttraction = GravityMath.PerpendicularFootClampedToSegment(position, dirOnDirectrix, apex);
        }
        else
        {
            Vector3 generatrixTermination = (apex * (1f - topCutRate)) + (dirOnDirectrix * topCutRate);
            Vector3 frustumBaseCenter = baseCenter + (centralAxis * (1f - topCutRate));

            if (Vector3.Dot(position - generatrixTermination, generatrixTermination - frustumBaseCenter) <= 0f)
            {
                float frustumDistance = MathF.Max(Vector3.Dot(unitAxis, position - frustumBaseCenter), 0f);
                if (!IsInRangeDistance(frustumDistance))
                {
                    return false;
                }

                direction = -unitAxis;
                distance = frustumDistance;
                return true;
            }

            pointOfAttraction = GravityMath.PerpendicularFootClampedToSegment(position, dirOnDirectrix, generatrixTermination);
        }

        if (GravityMath.IsNearZero(pointOfAttraction - position))
        {
            Vector3 generatrixDirection = GravityMath.NormalizeOrZero(apex - dirOnDirectrix);
            Vector3 gravity = GravityMath.KillElement(-positionOnBasePlane, generatrixDirection);
            direction = GravityMath.IsNearZero(gravity) ? -unitAxis : GravityMath.NormalizeOrZero(gravity);
            distance = 0f;
            return true;
        }

        if (isInsideCone)
        {
            direction = GravityMath.NormalizeOrZero(position - pointOfAttraction);
            distance = 0f;
            return true;
        }

        return CalcGravityFromMassPosition(position, pointOfAttraction, out direction, out distance);
    }
}
