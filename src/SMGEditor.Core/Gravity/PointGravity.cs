using System.Numerics;

namespace SMGEditor.Core.Gravity;

public sealed class PointGravity(Vector3 translation) : GravityGenerator
{
    protected override bool TryCalcOwnGravity(Vector3 position, out Vector3 direction, out float distance)
    {
        GravityMath.SeparateScalarAndDirection(translation - position, out distance, out direction);
        return IsInRangeDistance(distance);
    }
}
