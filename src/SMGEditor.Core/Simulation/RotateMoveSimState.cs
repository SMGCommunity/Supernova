namespace SMGEditor.Core.Simulation;

public enum RotateAxis
{
    X = 0,
    Y = 1,
    Z = 2,
}

public sealed class RotateMoveSimState
{
    private enum Phase
    {
        Moving,
        StoppedAtTarget,
        Finished,
    }

    private readonly RotateAxis _axis;
    private readonly float _rotateAngle;
    private readonly int _rotateAccelType;
    private readonly int _rotateStopTime;
    private readonly int _rotateType;
    private readonly float _baseDeltaPerFrame;
    private readonly int _easeDurationFrames;

    private float _angle;
    private float _targetAngle;
    private float _deltaSign = 1f;
    private int _framesIntoThisSweep;
    private int _stopFramesRemaining;
    private Phase _phase;

    public float AngleDegrees => _angle;

    public RotateAxis Axis => _axis;

    public bool IsFinished => _phase == Phase.Finished;

    public RotateMoveSimState(IReadOnlyDictionary<string, object?> fields)
    {
        _axis = (RotateAxis)ReadInt(fields, "RotateAxis", 0);
        _rotateAngle = ReadInt(fields, "RotateAngle", 0);
        _rotateAccelType = ReadInt(fields, "RotateAccelType", 0);
        _rotateStopTime = ReadInt(fields, "RotateStopTime", 0);
        _rotateType = ReadInt(fields, "RotateType", 1);

        if (MathF.Abs(_rotateAngle) < 0.001f)
        {
            _phase = Phase.Finished;
            return;
        }

        _baseDeltaPerFrame = ResolveBaseDeltaPerFrame(fields);
        if (_baseDeltaPerFrame == 0f)
        {
            _phase = Phase.Finished;
            return;
        }

        _easeDurationFrames = Math.Max(1, (int)MathF.Round(MathF.Abs(_rotateAngle / _baseDeltaPerFrame)));
        _targetAngle = _rotateAngle;
        _phase = Phase.Moving;
    }

    private float ResolveBaseDeltaPerFrame(IReadOnlyDictionary<string, object?> fields)
    {
        int rotateSpeedField = ReadInt(fields, "RotateSpeed", 0);

        if (_rotateAccelType == 2)
        {
            if (rotateSpeedField > 0)
            {
                return _rotateAngle / rotateSpeedField;
            }

            return 0f;
        }

        return rotateSpeedField * 0.01f;
    }

    public void Advance(int frameCount)
    {
        for (int i = 0; i < frameCount && _phase != Phase.Finished; i++)
        {
            StepOneFrame();
        }
    }

    private void StepOneFrame()
    {
        if (_phase == Phase.StoppedAtTarget)
        {
            if (--_stopFramesRemaining <= 0)
            {
                RestartAtEnd();
            }

            return;
        }

        if (_phase != Phase.Moving)
        {
            return;
        }

        _framesIntoThisSweep++;

        if (_rotateAccelType == 1)
        {
            float startAngle = _targetAngle - (_rotateAngle * MathF.Sign(_deltaSign));
            float t = Math.Clamp(_framesIntoThisSweep / (float)_easeDurationFrames, 0f, 1f);
            float eased = (t * t) * (3f - (2f * t));
            _angle = startAngle + ((_targetAngle - startAngle) * eased);

            if (t >= 1f)
            {
                _angle = _targetAngle;
                ReachedTarget();
            }
        }
        else
        {
            _angle += _baseDeltaPerFrame * MathF.Sign(_deltaSign);

            bool reached = _deltaSign >= 0f ? _angle >= _targetAngle : _angle <= _targetAngle;
            if (reached)
            {
                _angle = _targetAngle;
                ReachedTarget();
            }
        }
    }

    private void ReachedTarget()
    {
        if (_rotateStopTime > 0)
        {
            _phase = Phase.StoppedAtTarget;
            _stopFramesRemaining = _rotateStopTime;
        }
        else
        {
            RestartAtEnd();
        }
    }

    private void RestartAtEnd()
    {
        if (_rotateType == 0)
        {
            _phase = Phase.Finished;
            return;
        }

        if (_rotateType == 1)
        {
            _deltaSign = -_deltaSign;
        }

        _targetAngle = _angle + (_rotateAngle * MathF.Sign(_deltaSign));
        _framesIntoThisSweep = 0;
        _phase = Phase.Moving;
    }

    private static int ReadInt(IReadOnlyDictionary<string, object?> fields, string key, int defaultValue) =>
        fields.TryGetValue(key, out object? v) && v is int i ? i : defaultValue;
}
