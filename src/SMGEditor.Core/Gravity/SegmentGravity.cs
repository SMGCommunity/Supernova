using System.Numerics;

namespace SMGEditor.Core.Gravity;

public sealed class SegmentGravity : GravityGenerator
{
    private readonly Vector3 _point0;
    private readonly Vector3 _point1;
    private readonly Vector3 _axis;
    private readonly float _axisLength;
    private readonly Vector3 _oppositeSideVecOrtho;
    private readonly float _validSideCos;
    private readonly bool _edge0Valid;
    private readonly bool _edge1Valid;

    public SegmentGravity(Vector3 point0, Vector3 point1, Vector3 sideVector, float validSideDegree, bool edge0Valid, bool edge1Valid)
    {
        _point0 = point0;
        _point1 = point1;
        _validSideCos = MathF.Cos(0.5f * validSideDegree * MathF.PI / 180f);
        _edge0Valid = edge0Valid;
        _edge1Valid = edge1Valid;

        GravityMath.SeparateScalarAndDirection(point1 - point0, out _axisLength, out _axis);

        Vector3 orthoSide = GravityMath.NormalizeOrZero(GravityMath.KillElement(sideVector, _axis));
        if (!GravityMath.IsNearZero(orthoSide))
        {
            orthoSide = GravityMath.RotateAroundAxis(orthoSide, _axis, 0.5f * validSideDegree * MathF.PI / 180f);
        }

        _oppositeSideVecOrtho = orthoSide;
    }

    protected override bool TryCalcOwnGravity(Vector3 position, out Vector3 direction, out float distance)
    {
        direction = Vector3.Zero;
        distance = 0f;

        Vector3 relPosFromBase = position - _point0;
        float axisY = Vector3.Dot(relPosFromBase, _axis);

        if (_validSideCos > -1f && !GravityMath.IsNearZero(_oppositeSideVecOrtho))
        {
            Vector3 dirOnBasePlane = GravityMath.NormalizeOrZero(relPosFromBase - (_axis * axisY));
            if (Vector3.Dot(dirOnBasePlane, _oppositeSideVecOrtho) < _validSideCos)
            {
                return false;
            }
        }

        Vector3 attraction;
        if (axisY < 0f)
        {
            if (!_edge0Valid)
            {
                return false;
            }

            attraction = _point0;
        }
        else if (axisY > _axisLength)
        {
            if (!_edge1Valid)
            {
                return false;
            }

            attraction = _point1;
        }
        else
        {
            attraction = _point0 + (_axis * axisY);
        }

        GravityMath.SeparateScalarAndDirection(attraction - position, out distance, out direction);
        return IsInRangeDistance(distance);
    }
}
