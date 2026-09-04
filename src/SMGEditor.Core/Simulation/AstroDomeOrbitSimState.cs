using System.Numerics;

namespace SMGEditor.Core.Simulation;

public sealed class AstroDomeOrbitSimState
{
    private static readonly float[] RadiusFiveRings = [4000f, 6200f, 8100f, 10300f, 12000f];
    private static readonly float[] RadiusFourRings = [4000f, 6700f, 9100f, 11800f];
    private const float RotateSpeedDegPerFrame = -0.05f;
    private const float AngleOffsetPerRing = 230f;
    private const float SelfSpinDegPerFrame = 0.4f;

    private readonly float _orbitRadius;
    private float _angle;

    public float SelfSpinDegrees { get; private set; }

    public Vector3 TiltDegrees { get; }

    public AstroDomeOrbitSimState(int ringIndex, int ringCount)
    {
        float[] radii = ringCount >= 5 ? RadiusFiveRings : RadiusFourRings;
        _orbitRadius = radii[Math.Clamp(ringIndex, 0, radii.Length - 1)];
        _angle = AngleOffsetPerRing * ringIndex;
        TiltDegrees = ringIndex >= 4 ? new Vector3(20f, 45f, 0f) : Vector3.Zero;
    }

    public void Advance(int frameCount)
    {
        _angle = RepeatDegree(_angle + (RotateSpeedDegPerFrame * frameCount));
        SelfSpinDegrees = RepeatDegree(SelfSpinDegrees + (SelfSpinDegPerFrame * frameCount));
    }

    public Vector3 ComputePosition(Vector3 domeCenter) => ComputePositionAtAngle(_angle, domeCenter, _orbitRadius, TiltDegrees);

    public static List<Vector3> ComputeRingOutline(int ringIndex, int ringCount, Vector3 domeCenter, int segments = 64)
    {
        float[] radii = ringCount >= 5 ? RadiusFiveRings : RadiusFourRings;
        float radius = radii[Math.Clamp(ringIndex, 0, radii.Length - 1)];
        Vector3 tilt = ringIndex >= 4 ? new Vector3(20f, 45f, 0f) : Vector3.Zero;

        var points = new List<Vector3>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            float angle = 360f * i / segments;
            points.Add(ComputePositionAtAngle(angle, domeCenter, radius, tilt));
        }

        return points;
    }

    private static Vector3 ComputePositionAtAngle(float angleDegrees, Vector3 domeCenter, float orbitRadius, Vector3 tiltDegrees)
    {
        float rad = angleDegrees * MathF.PI / 180f;
        Vector3 pos = new Vector3(MathF.Cos(rad), 0f, MathF.Sin(rad)) * orbitRadius;
        pos += domeCenter;

        if (tiltDegrees != Vector3.Zero)
        {
            Matrix4x4 tilt = Matrix4x4.CreateRotationX(tiltDegrees.X * MathF.PI / 180f) *
                Matrix4x4.CreateRotationY(tiltDegrees.Y * MathF.PI / 180f) *
                Matrix4x4.CreateRotationZ(tiltDegrees.Z * MathF.PI / 180f);
            pos = Vector3.Transform(pos, tilt);
        }

        return pos;
    }

    private static float RepeatDegree(float deg)
    {
        deg %= 360f;
        return deg < 0f ? deg + 360f : deg;
    }
}
