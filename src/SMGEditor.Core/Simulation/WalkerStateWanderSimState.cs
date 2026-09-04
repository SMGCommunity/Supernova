using System.Numerics;

namespace SMGEditor.Core.Simulation;

public sealed class WalkerStateWanderSimState
{
    private enum Phase
    {
        Wait,
        Walk,
    }

    private const float RadiusUnits = 500f;
    private const float GroundFriction = 0.93f;
    private const float TargetReachedDistance = 20f;
    private const float FaceThresholdDegrees = 8f;

    private readonly Vector3 _center;
    private readonly float _speed;
    private readonly int _waitTimeFrames;
    private readonly int _walkTimeFrames;
    private readonly float _turnMaxRateDegree;
    private readonly Random _random;

    private Phase _phase = Phase.Wait;
    private int _stepCounter;
    private Vector3 _target;
    private Vector3 _velocity = Vector3.Zero;

    public Vector3 Position { get; private set; }

    public Vector3 Direction { get; private set; }

    public WalkerStateWanderSimState(Vector3 initialPosition, Vector3 initialDirection, float speed, int waitTimeFrames, int walkTimeFrames, float turnMaxRateDegree, int seed)
    {
        _center = initialPosition;
        Position = initialPosition;
        Direction = FlattenNormalizeOrDefault(initialDirection, Vector3.UnitX);
        _speed = speed;
        _waitTimeFrames = Math.Max(1, waitTimeFrames);
        _walkTimeFrames = Math.Max(1, walkTimeFrames);
        _turnMaxRateDegree = turnMaxRateDegree;
        _random = new Random(seed);
    }

    public void Advance(int frameCount)
    {
        for (int i = 0; i < frameCount; i++)
        {
            StepOneFrame();
        }
    }

    private void StepOneFrame()
    {
        if (_phase == Phase.Wait)
        {
            _stepCounter++;
            if (_stepCounter > _waitTimeFrames)
            {
                _target = PickNextTarget();
                _phase = Phase.Walk;
                _stepCounter = 0;
            }
        }
        else
        {
            _stepCounter++;

            Vector3 toTarget = Flatten(_target - Position);
            Direction = TurnTowardsHorizontal(Direction, toTarget, _turnMaxRateDegree);

            if (AngleBetweenDegrees(Direction, toTarget) <= FaceThresholdDegrees)
            {
                _velocity += Direction * _speed;
            }

            bool reached = toTarget.LengthSquared() < TargetReachedDistance * TargetReachedDistance;
            if (_stepCounter > _walkTimeFrames || reached)
            {
                _phase = Phase.Wait;
                _stepCounter = 0;
            }
        }

        _velocity *= GroundFriction;
        Position += _velocity;
    }

    private Vector3 PickNextTarget()
    {
        var v = new Vector3(NextRange(-1f, 1f), NextRange(-1f, 1f), NextRange(-1f, 1f));
        v = NormalizeOrZero(v);
        v.Y = 0f;
        return _center + (v * RadiusUnits);
    }

    private float NextRange(float min, float max) => min + ((float)_random.NextDouble() * (max - min));

    private static Vector3 Flatten(Vector3 v) => new(v.X, 0f, v.Z);

    private static Vector3 NormalizeOrZero(Vector3 v)
    {
        float lenSq = v.LengthSquared();
        return lenSq > 1e-8f ? v / MathF.Sqrt(lenSq) : Vector3.Zero;
    }

    private static Vector3 FlattenNormalizeOrDefault(Vector3 v, Vector3 fallback)
    {
        Vector3 flat = NormalizeOrZero(Flatten(v));
        return flat != Vector3.Zero ? flat : fallback;
    }

    private static Vector3 TurnTowardsHorizontal(Vector3 from, Vector3 to, float maxDegrees)
    {
        Vector3 target = NormalizeOrZero(Flatten(to));
        if (target == Vector3.Zero)
        {
            return from;
        }

        Vector3 current = NormalizeOrZero(Flatten(from));
        if (current == Vector3.Zero)
        {
            return target;
        }

        float fromAngle = MathF.Atan2(current.X, current.Z);
        float toAngle = MathF.Atan2(target.X, target.Z);
        float delta = WrapAngle(toAngle - fromAngle);
        float maxStepRad = maxDegrees * MathF.PI / 180f;
        float step = Math.Clamp(delta, -maxStepRad, maxStepRad);
        float newAngle = fromAngle + step;
        return new Vector3(MathF.Sin(newAngle), 0f, MathF.Cos(newAngle));
    }

    private static float AngleBetweenDegrees(Vector3 a, Vector3 b)
    {
        Vector3 na = NormalizeOrZero(Flatten(a));
        Vector3 nb = NormalizeOrZero(Flatten(b));
        if (na == Vector3.Zero || nb == Vector3.Zero)
        {
            return 180f;
        }

        float dot = Math.Clamp(Vector3.Dot(na, nb), -1f, 1f);
        return MathF.Acos(dot) * 180f / MathF.PI;
    }

    private static float WrapAngle(float radians)
    {
        while (radians > MathF.PI)
        {
            radians -= 2f * MathF.PI;
        }

        while (radians < -MathF.PI)
        {
            radians += 2f * MathF.PI;
        }

        return radians;
    }
}
