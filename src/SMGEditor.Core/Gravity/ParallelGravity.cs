using System.Numerics;

namespace SMGEditor.Core.Gravity;

public enum ParallelGravityDistanceCalcType
{
    Default,
    X,
    Y,
    Z,
}

public sealed class ParallelGravity : GravityGenerator
{
    private enum RangeType
    {
        Sphere,
        Box,
        Cylinder,
    }

    private readonly Vector3 _planePosition;
    private readonly Vector3 _planeUpVec;
    private readonly RangeType _rangeType;
    private readonly float _baseDistance;
    private readonly ParallelGravityDistanceCalcType _distanceCalcType;

    private readonly Vector3 _boxTranslation;
    private readonly Vector3 _boxXDir;
    private readonly Vector3 _boxYDir;
    private readonly Vector3 _boxZDir;
    private readonly float _extentX;
    private readonly float _extentY;
    private readonly float _extentZ;

    private readonly float _cylinderRadius;
    private readonly float _cylinderHeight;

    private ParallelGravity(
        Vector3 planePosition, Vector3 planeUp, RangeType rangeType, float baseDistance, ParallelGravityDistanceCalcType distanceCalcType,
        Vector3 boxTranslation, Vector3 boxXDir, Vector3 boxYDir, Vector3 boxZDir, float cylinderRadius, float cylinderHeight)
    {
        _planePosition = planePosition;
        _planeUpVec = GravityMath.NormalizeOrZero(planeUp);
        _rangeType = rangeType;
        _baseDistance = baseDistance < 0f ? 2000f : baseDistance;
        _distanceCalcType = distanceCalcType;
        _boxTranslation = boxTranslation;
        _boxXDir = boxXDir;
        _boxYDir = boxYDir;
        _boxZDir = boxZDir;
        _extentX = boxXDir.LengthSquared();
        _extentY = boxYDir.LengthSquared();
        _extentZ = boxZDir.LengthSquared();
        _cylinderRadius = cylinderRadius;
        _cylinderHeight = cylinderHeight;
    }

    public static ParallelGravity CreatePlane(Vector3 planePosition, Vector3 planeUp) =>
        new(planePosition, planeUp, RangeType.Sphere, 2000f, ParallelGravityDistanceCalcType.Default,
            Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0f, 0f);

    public static ParallelGravity CreateBox(
        Vector3 planePosition, Vector3 planeUp, float baseDistance, ParallelGravityDistanceCalcType distanceCalcType,
        Vector3 boxTranslation, Vector3 boxXDir, Vector3 boxYDir, Vector3 boxZDir) =>
        new(planePosition, planeUp, RangeType.Box, baseDistance, distanceCalcType, boxTranslation, boxXDir, boxYDir, boxZDir, 0f, 0f);

    public static ParallelGravity CreateCylinder(Vector3 planePosition, Vector3 planeUp, float baseDistance, float cylinderRadius, float cylinderHeight) =>
        new(planePosition, planeUp, RangeType.Cylinder, baseDistance, ParallelGravityDistanceCalcType.Default,
            Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, cylinderRadius, cylinderHeight);

    protected override bool TryCalcOwnGravity(Vector3 position, out Vector3 direction, out float distance)
    {
        bool inRange = _rangeType switch
        {
            RangeType.Box => IsInBoxRange(position, out distance),
            RangeType.Cylinder => IsInCylinderRange(position, out distance),
            _ => IsInSphereRange(position, out distance),
        };

        if (!inRange)
        {
            direction = Vector3.Zero;
            return false;
        }

        direction = -_planeUpVec;
        return true;
    }

    private bool IsInSphereRange(Vector3 position, out float distance)
    {
        distance = _baseDistance;
        if (Range < 0f)
        {
            return true;
        }

        Vector3 dirToCenter = _planePosition - position;
        return dirToCenter.LengthSquared() < Range * Range;
    }

    private bool IsInBoxRange(Vector3 position, out float distance)
    {
        distance = _baseDistance;
        Vector3 dirToCenter = position - _boxTranslation;

        float dotX = Vector3.Dot(dirToCenter, _boxXDir);
        if (dotX < -_extentX || _extentX < dotX)
        {
            return false;
        }

        float dotY = Vector3.Dot(dirToCenter, _boxYDir);
        if (dotY < -_extentY || _extentY < dotY)
        {
            return false;
        }

        float dotZ = Vector3.Dot(dirToCenter, _boxZDir);
        if (dotZ < -_extentZ || _extentZ < dotZ)
        {
            return false;
        }

        distance = _distanceCalcType switch
        {
            ParallelGravityDistanceCalcType.X => _baseDistance + (MathF.Abs(dotX) / MathF.Sqrt(_extentX)),
            ParallelGravityDistanceCalcType.Y => _baseDistance + (MathF.Abs(dotY) / MathF.Sqrt(_extentY)),
            ParallelGravityDistanceCalcType.Z => _baseDistance + (MathF.Abs(dotZ) / MathF.Sqrt(_extentZ)),
            _ => _baseDistance,
        };

        return true;
    }

    private bool IsInCylinderRange(Vector3 position, out float distance)
    {
        distance = 0f;
        float height = Vector3.Dot(_planeUpVec, position - _planePosition);
        if (height < 0f || _cylinderHeight < height)
        {
            return false;
        }

        Vector3 onPlane = GravityMath.KillElement(position - _planePosition, _planeUpVec);
        float radius = onPlane.Length();
        if (radius > _cylinderRadius)
        {
            return false;
        }

        distance = _baseDistance + radius;
        return true;
    }
}
