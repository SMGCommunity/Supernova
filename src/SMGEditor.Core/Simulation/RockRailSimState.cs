using System.Numerics;
using SMGEditor.Core.Gravity;
using SMGEditor.Core.Stage;

namespace SMGEditor.Core.Simulation;

public sealed class RockRailSimState
{
    private const float BinderRadius = 225f;
    private const float RotateSpeedRate = 1.1f;
    private const float GravityAccelPerFrame = 1.5f;

    private readonly RailCoordSampleTable _table;
    private readonly float _moveSpeed;
    private readonly float _rotateSpeedDegPerFrame;
    private readonly GravityZoneSet _gravityZone;
    private readonly Vector3 _fallbackUp;

    private float _coord;
    private float _fallSpeed;

    public Vector3 Position { get; private set; }

    public Vector3 Up { get; private set; }

    public Vector3 Front { get; private set; } = Vector3.UnitZ;

    public float RollAngleDegrees { get; private set; }

    public RockRailSimState(
        IReadOnlyList<PathPoint> worldPoints, bool closed, float moveSpeed, float radiusScale, Vector3 initialPosition,
        GravityZoneSet gravityZone, Vector3 fallbackUp)
    {
        _table = new RailCoordSampleTable(worldPoints, closed);
        _moveSpeed = MathF.Max(moveSpeed, 0f);
        _gravityZone = gravityZone;
        _fallbackUp = fallbackUp;

        float rockRadius = MathF.Max(radiusScale, 0.001f) * BinderRadius;
        _rotateSpeedDegPerFrame = (_moveSpeed * 180f * RotateSpeedRate) / (MathF.PI * rockRadius);

        _coord = NearestCoord(initialPosition);
        Position = _table.PositionAtCoord(_coord);
        Up = ResolveUp(Position);
    }

    public void Advance(int frameCount)
    {
        for (int i = 0; i < frameCount && _table.TotalLength > 0f; i++)
        {
            StepOneFrame();
        }

        Up = ResolveUp(Position);
    }

    private void StepOneFrame()
    {
        Vector3 previousPosition = Position;
        Vector3 down = -ResolveUp(Position);

        _coord = Wrap(_coord + _moveSpeed);
        Vector3 targetPos = _table.PositionAtCoord(_coord);

        Vector3 offset = targetPos - Position;
        Vector3 horizontalOffset = offset - (down * Vector3.Dot(down, offset));
        Vector3 chasedPos = Position + horizontalOffset;

        float verticalGap = Vector3.Dot(targetPos - chasedPos, down);
        if (verticalGap <= 0f)
        {
            Position = targetPos;
            _fallSpeed = 0f;
        }
        else
        {
            _fallSpeed += GravityAccelPerFrame;
            if (_fallSpeed >= verticalGap)
            {
                Position = targetPos;
                _fallSpeed = 0f;
            }
            else
            {
                Position = chasedPos + (down * _fallSpeed);
            }
        }

        RollAngleDegrees = Wrap360(RollAngleDegrees + _rotateSpeedDegPerFrame);

        Vector3 delta = Position - previousPosition;
        if (delta.LengthSquared() > 1e-8f)
        {
            Front = Vector3.Normalize(delta);
        }
    }

    private Vector3 ResolveUp(Vector3 position) =>
        _gravityZone.TryCalcTotalGravity(position, out Vector3 downDir) && downDir.LengthSquared() > 1e-6f
            ? -downDir
            : _fallbackUp;

    private float NearestCoord(Vector3 position)
    {
        if (_table.Positions.Count == 0)
        {
            return 0f;
        }

        int bestIndex = 0;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < _table.Positions.Count; i++)
        {
            float distSq = Vector3.DistanceSquared(_table.Positions[i], position);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestIndex = i;
            }
        }

        return MathF.Min(bestIndex * 100f, _table.TotalLength);
    }

    private float Wrap(float coord)
    {
        float length = _table.TotalLength;
        if (length <= 0f)
        {
            return 0f;
        }

        return ((coord % length) + length) % length;
    }

    private static float Wrap360(float degrees) => ((degrees % 360f) + 360f) % 360f;
}
