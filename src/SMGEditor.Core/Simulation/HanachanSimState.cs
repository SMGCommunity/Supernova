using System.Numerics;
using SMGEditor.Core.Gravity;
using SMGEditor.Core.Stage;

namespace SMGEditor.Core.Simulation;

public sealed class HanachanSimState
{
    public const int PartCount = 5;

    private const float WalkSpeed = 4f;
    private const float CollideRange = 2f;
    private const float SensorRadius = 85f;
    private const float SegmentSeparation = CollideRange + SensorRadius / 2f + SensorRadius / 2f;

    private readonly RailCoordSampleTable _table;
    private readonly bool _closed;
    private readonly GravityZoneSet _gravityZone;
    private readonly Vector3 _fallbackUp;

    private float _headCoord;
    private bool _forward = true;

    public Vector3[] Positions { get; } = new Vector3[PartCount];

    public Vector3[] Fronts { get; } = new Vector3[PartCount];

    public Vector3[] Ups { get; } = new Vector3[PartCount];

    public HanachanSimState(IReadOnlyList<PathPoint> railPoints, bool closed, Vector3 initialPosition, GravityZoneSet gravityZone, Vector3 fallbackUp)
    {
        _table = new RailCoordSampleTable(railPoints, closed);
        _closed = closed;
        _gravityZone = gravityZone;
        _fallbackUp = fallbackUp;

        _headCoord = NearestCoord(initialPosition);

        for (int i = 0; i < PartCount; i++)
        {
            Positions[i] = _table.PositionAtCoord(WrapOrClamp(_headCoord - i * SegmentSeparation));
            Ups[i] = ResolveUp(Positions[i]);
        }

        Fronts[0] = ComputeRailFront(_headCoord);
        for (int i = 1; i < PartCount; i++)
        {
            Vector3 diff = Positions[i - 1] - Positions[i];
            Fronts[i] = diff.LengthSquared() > 1e-8f ? Vector3.Normalize(diff) : Fronts[i - 1];
        }
    }

    public void Advance(int frameCount)
    {
        for (int i = 0; i < frameCount && _table.TotalLength > 0f; i++)
        {
            StepOneFrame();
        }
    }

    private void StepOneFrame()
    {
        _headCoord += _forward ? WalkSpeed : -WalkSpeed;

        if (_closed)
        {
            _headCoord = Wrap(_headCoord);
        }
        else if (_headCoord >= _table.TotalLength)
        {
            _headCoord = _table.TotalLength;
            _forward = false;
        }
        else if (_headCoord <= 0f)
        {
            _headCoord = 0f;
            _forward = true;
        }

        Vector3 headPos = _table.PositionAtCoord(_headCoord);
        Vector3 headFront = ComputeRailFront(_headCoord);

        Positions[0] = headPos;
        Fronts[0] = _forward ? headFront : -headFront;
        Ups[0] = ResolveUp(headPos);

        for (int i = 1; i < PartCount; i++)
        {
            Vector3 toPrev = Positions[i - 1] - Positions[i];
            float dist = toPrev.Length();
            if (dist > 1e-4f)
            {
                Vector3 dir = toPrev / dist;
                Positions[i] = Positions[i - 1] - dir * SegmentSeparation;
                Fronts[i] = dir;
            }

            Ups[i] = ResolveUp(Positions[i]);
        }
    }

    private Vector3 ComputeRailFront(float coord)
    {
        const float epsilon = 4f;
        Vector3 ahead = _table.PositionAtCoord(WrapOrClamp(coord + epsilon));
        Vector3 behind = _table.PositionAtCoord(WrapOrClamp(coord - epsilon));
        Vector3 diff = ahead - behind;
        return diff.LengthSquared() > 1e-8f ? Vector3.Normalize(diff) : Vector3.UnitZ;
    }

    private float WrapOrClamp(float coord) => _closed ? Wrap(coord) : Math.Clamp(coord, 0f, _table.TotalLength);

    private float Wrap(float coord)
    {
        float length = _table.TotalLength;
        if (length <= 0f)
        {
            return 0f;
        }

        return ((coord % length) + length) % length;
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
}
