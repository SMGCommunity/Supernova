using System.Numerics;

namespace SMGEditor.Core.Gravity;

public sealed class DiskGravity : GravityGenerator
{
    private readonly Vector3 _position;
    private readonly Vector3 _normal;
    private readonly Vector3 _oppositeSideVecOrtho;
    private readonly float _radius;
    private readonly float _validCos;
    private readonly bool _enableBothSide;
    private readonly bool _enableEdgeGravity;

    public DiskGravity(Vector3 position, Vector3 normal, Vector3 sideDirection, float radius, float validDegree, bool enableBothSide, bool enableEdgeGravity)
    {
        _position = position;
        _normal = GravityMath.NormalizeOrZero(normal);
        _radius = radius;
        _validCos = MathF.Cos(0.5f * validDegree * MathF.PI / 180f);
        _enableBothSide = enableBothSide;
        _enableEdgeGravity = enableEdgeGravity;

        Vector3 orthoSide = GravityMath.NormalizeOrZero(GravityMath.KillElement(sideDirection, _normal));
        if (!GravityMath.IsNearZero(orthoSide))
        {
            orthoSide = GravityMath.RotateAroundAxis(orthoSide, _normal, 0.5f * validDegree * MathF.PI / 180f);
        }

        _oppositeSideVecOrtho = orthoSide;
    }

    protected override bool TryCalcOwnGravity(Vector3 position, out Vector3 direction, out float distance)
    {
        direction = Vector3.Zero;
        distance = 0f;

        Vector3 relativePos = position - _position;
        float centralAxisY = Vector3.Dot(relativePos, _normal);

        if (!_enableBothSide && centralAxisY < 0f)
        {
            return false;
        }

        Vector3 dirOnDiskPlaneRaw = relativePos - (_normal * centralAxisY);
        GravityMath.SeparateScalarAndDirection(dirOnDiskPlaneRaw, out float distanceToCentralAxis, out Vector3 dirOnDiskPlane);

        if (_validCos > -1f && Vector3.Dot(dirOnDiskPlane, _oppositeSideVecOrtho) < _validCos)
        {
            return false;
        }

        Vector3 gravity;
        if (distanceToCentralAxis <= _radius)
        {
            gravity = centralAxisY >= 0f ? -_normal : _normal;
            distance = MathF.Abs(centralAxisY);
            direction = gravity;
        }
        else
        {
            if (!_enableEdgeGravity)
            {
                return false;
            }

            Vector3 closestEdgePoint = (dirOnDiskPlane * _radius) + _position;
            gravity = closestEdgePoint - position;
            GravityMath.SeparateScalarAndDirection(gravity, out distance, out direction);
        }

        return IsInRangeDistance(distance);
    }
}
