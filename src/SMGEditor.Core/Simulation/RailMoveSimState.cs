using System.Numerics;
using SMGEditor.Core.Stage;

namespace SMGEditor.Core.Simulation;

public sealed class RailMoveSimState
{
    private enum Phase
    {
        Moving,
        StoppedAtPoint,
        StoppedAtEnd,
        Finished,
    }

    private readonly List<(float Distance, Vector3 Position)> _lookup = [];
    private readonly List<float> _pointDistances = [];
    private readonly IReadOnlyList<PathPoint> _points;
    private readonly bool _closed;
    private readonly int _moveStopType;
    private readonly float _totalLength;

    private float _coord;
    private bool _forward = true;
    private int _currentPointIndex;
    private float _speed;
    private float _acceleration;
    private int _accelFramesRemaining;
    private int _stopFramesRemaining;
    private Phase _phase = Phase.Moving;
    private bool _needsSpeedRefresh = true;

    public Vector3 Position { get; private set; }

    public bool IsFinished => _phase == Phase.Finished;

    public RailMoveSimState(IReadOnlyList<PathPoint> worldPoints, bool closed, IReadOnlyDictionary<string, object?> pathFields, Vector3 initialPosition)
    {
        _points = worldPoints;
        _closed = closed;
        _moveStopType = ReadArg(pathFields, "path_arg1", defaultValue: 1);

        BuildArcLengthTable();
        _totalLength = _lookup.Count > 0 ? _lookup[^1].Distance : 0f;

        int railInitPosType = ReadArg(pathFields, "path_arg4", defaultValue: 0);
        _currentPointIndex = railInitPosType == 2 ? 0 : NearestPointIndex(initialPosition);
        _coord = _pointDistances.Count > _currentPointIndex ? _pointDistances[_currentPointIndex] : 0f;

        Position = PositionAtCoord(_coord);
    }

    public Vector3 Advance(int frameCount)
    {
        for (int i = 0; i < frameCount && _phase != Phase.Finished; i++)
        {
            StepOneFrame();
        }

        return Position;
    }

    private void StepOneFrame()
    {
        switch (_phase)
        {
            case Phase.StoppedAtPoint:
                if (--_stopFramesRemaining <= 0)
                {
                    _phase = Phase.Moving;
                    _needsSpeedRefresh = true;
                }

                return;

            case Phase.StoppedAtEnd:
                if (--_stopFramesRemaining <= 0)
                {
                    RestartAtEnd();
                }

                return;

            case Phase.Finished:
                return;
        }

        if (_needsSpeedRefresh)
        {
            RefreshSpeedAndAccel();
            _needsSpeedRefresh = false;
        }

        if (_acceleration != 0f && _accelFramesRemaining > 0)
        {
            _speed += _acceleration;
            _accelFramesRemaining--;
        }

        _coord += _forward ? _speed : -_speed;

        bool reachedRailEnd = !_closed && (_coord >= _totalLength || _coord <= 0f);
        if (reachedRailEnd)
        {
            _coord = Math.Clamp(_coord, 0f, _totalLength);
            Position = PositionAtCoord(_coord);
            _currentPointIndex = _forward ? _points.Count - 1 : 0;
            ReachedEnd();
            return;
        }

        Position = PositionAtCoord(_coord);

        int newPointIndex = CurrentPointIndexAtCoord(_coord);
        if (newPointIndex != _currentPointIndex)
        {
            _currentPointIndex = newPointIndex;
            PassPoint();
        }
    }

    private void PassPoint()
    {
        int stopTime = ReadArg(_points[_currentPointIndex].Fields, "point_arg5", defaultValue: 0);
        if (stopTime > 0)
        {
            _speed = 0f;
            _acceleration = 0f;
            _phase = Phase.StoppedAtPoint;
            _stopFramesRemaining = stopTime;
        }
        else
        {
            _needsSpeedRefresh = true;
        }
    }

    private void ReachedEnd()
    {
        if (_moveStopType == 1)
        {
            _forward = !_forward;
        }

        _speed = 0f;
        _acceleration = 0f;

        int stopTime = ReadArg(_points[_currentPointIndex].Fields, "point_arg5", defaultValue: 0);
        if (stopTime > 0)
        {
            _phase = Phase.StoppedAtEnd;
            _stopFramesRemaining = stopTime;
        }
        else
        {
            RestartAtEnd();
        }
    }

    private void RestartAtEnd()
    {
        switch (_moveStopType)
        {
            case 0:
            case 3:
                _phase = Phase.Finished;
                break;
            case 2:
                _coord = 0f;
                _forward = true;
                _currentPointIndex = 0;
                Position = PositionAtCoord(_coord);
                _phase = Phase.Moving;
                _needsSpeedRefresh = true;
                break;
            default:
                _phase = Phase.Moving;
                _needsSpeedRefresh = true;
                break;
        }
    }

    private void RefreshSpeedAndAccel()
    {
        IReadOnlyDictionary<string, object?> fields = _points[_currentPointIndex].Fields;
        int speedCalcType = ReadArg(fields, "point_arg7", defaultValue: 0);
        int rawArg0 = ReadArg(fields, "point_arg0", defaultValue: -1);

        float targetSpeed = _speed;
        if (speedCalcType == 1)
        {
            if (rawArg0 >= 0)
            {
                float segmentLength = SegmentLength(_currentPointIndex, _forward);
                targetSpeed = rawArg0 > 0 ? segmentLength / rawArg0 : 0f;
            }
        }
        else if (rawArg0 >= 0)
        {
            targetSpeed = rawArg0;
        }

        int accelTime = ReadArg(fields, "point_arg1", defaultValue: 0);
        if (accelTime > 0)
        {
            _acceleration = (targetSpeed - _speed) / accelTime;
            _accelFramesRemaining = accelTime;
        }
        else
        {
            _speed = targetSpeed;
            _acceleration = 0f;
            _accelFramesRemaining = 0;
        }
    }

    private float SegmentLength(int fromPointIndex, bool forward)
    {
        int toIndex = forward ? fromPointIndex + 1 : fromPointIndex - 1;
        if (toIndex < 0 || toIndex >= _pointDistances.Count)
        {
            return 0f;
        }

        return MathF.Abs(_pointDistances[toIndex] - _pointDistances[fromPointIndex]);
    }

    private int CurrentPointIndexAtCoord(float coord)
    {
        int best = 0;
        for (int i = 1; i < _pointDistances.Count; i++)
        {
            if (_pointDistances[i] <= coord)
            {
                best = i;
            }
        }

        return best;
    }

    private int NearestPointIndex(Vector3 position)
    {
        int best = 0;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < _points.Count; i++)
        {
            float distSq = Vector3.DistanceSquared(_points[i].Position, position);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = i;
            }
        }

        return best;
    }

    private void BuildArcLengthTable(int samplesPerSegment = 16)
    {
        if (_points.Count == 0)
        {
            return;
        }

        float cumulative = 0f;
        Vector3 previous = _points[0].Position;
        _lookup.Add((0f, previous));
        _pointDistances.Add(0f);

        int segmentCount = _closed ? _points.Count : _points.Count - 1;
        for (int i = 0; i < segmentCount; i++)
        {
            PathPoint a = _points[i];
            PathPoint b = _points[(i + 1) % _points.Count];

            for (int s = 1; s <= samplesPerSegment; s++)
            {
                float t = s / (float)samplesPerSegment;
                Vector3 sample = SampleCubicBezier(a.Position, a.ControlPointOut, b.ControlPointIn, b.Position, t);
                cumulative += Vector3.Distance(previous, sample);
                previous = sample;
                _lookup.Add((cumulative, sample));
            }

            _pointDistances.Add(cumulative);
        }
    }

    private Vector3 PositionAtCoord(float coord)
    {
        if (_lookup.Count == 0)
        {
            return Vector3.Zero;
        }

        float wrapped;
        if (_closed && _totalLength > 0f)
        {
            wrapped = ((coord % _totalLength) + _totalLength) % _totalLength;
        }
        else
        {
            wrapped = Math.Clamp(coord, 0f, _totalLength);
        }

        for (int i = 1; i < _lookup.Count; i++)
        {
            if (_lookup[i].Distance >= wrapped)
            {
                (float prevDist, Vector3 prevPos) = _lookup[i - 1];
                (float nextDist, Vector3 nextPos) = _lookup[i];
                float span = nextDist - prevDist;
                float t = span > 0f ? (wrapped - prevDist) / span : 0f;
                return Vector3.Lerp(prevPos, nextPos, t);
            }
        }

        return _lookup[^1].Position;
    }

    private static Vector3 SampleCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }

    private static int ReadArg(IReadOnlyDictionary<string, object?> fields, string key, int defaultValue) =>
        fields.TryGetValue(key, out object? v) && v is int i && i >= 0 ? i : defaultValue;
}
