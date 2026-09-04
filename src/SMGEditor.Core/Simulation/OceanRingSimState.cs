using System.Numerics;
using SMGEditor.Core.Stage;

namespace SMGEditor.Core.Simulation;

public readonly struct OceanRingWaterPoint(Vector3 originalPos, Vector3 upVec, float coordAcrossRail, float coordOnRail, float taper)
{
    public Vector3 OriginalPos { get; } = originalPos;
    public Vector3 UpVec { get; } = upVec;
    public float CoordAcrossRail { get; } = coordAcrossRail;
    public float CoordOnRail { get; } = coordOnRail;
    public float Taper { get; } = taper;
}

public sealed class OceanRingSimState
{
    public const int Stride = 15;
    private const float PointIntervalLine = 200f;
    private const float DefaultHalfWidth = 1200f;
    private const float EdgePointNum = 2f;
    private const float WaveSpeed1 = -0.04f;
    private const float WaveSpeed2 = -0.06f;

    public IReadOnlyList<OceanRingWaterPoint> Points { get; }
    public int SegmentCount { get; }
    public bool Closed { get; }
    public float WaveHeight1 { get; }
    public float WaveHeight2 { get; }

    public float Theta1 { get; private set; }
    public float Theta2 { get; private set; }

    public Vector2 Tex0Scroll { get; private set; }
    public Vector2 Tex1Scroll { get; private set; }
    public Vector2 Tex2Scroll { get; private set; }

    public OceanRingSimState(RailCoordSampleTable rail, bool closed, float waveHeight1, float waveHeight2)
    {
        Closed = closed;
        WaveHeight1 = waveHeight1;
        WaveHeight2 = waveHeight2;

        SegmentCount = (int)(rail.TotalLength / PointIntervalLine) + 1;
        float segmentSize = rail.TotalLength / SegmentCount;

        var points = new List<OceanRingWaterPoint>(SegmentCount * Stride);
        var up = Vector3.UnitY;

        for (int i = 0; i < SegmentCount; i++)
        {
            float coord = i * segmentSize;
            Vector3 railPos = rail.PositionAtCoord(coord);
            Vector3 railDir = ComputeRailDirection(rail, coord);
            Vector3 right = Vector3.Normalize(Vector3.Cross(railDir, up));

            for (int j = -7; j <= 7; j++)
            {
                float widthOffset = 171.42857f * j;
                Vector3 pos = railPos + right * widthOffset;

                int edgePointIdx = 7 - Math.Abs(j);
                float taper = edgePointIdx < EdgePointNum
                    ? MathF.Sin(edgePointIdx / EdgePointNum * (MathF.PI / 2f))
                    : 1f;

                float coordAcrossRail = (j + 7) * 171.42857f;
                points.Add(new OceanRingWaterPoint(pos, up, coordAcrossRail, coord, taper));
            }
        }

        Points = points;
    }

    public static (float Height1, float Height2) WaveHeightsForArg0(int arg0) => arg0 switch
    {
        0 => (80f, 100f),
        2 => (20f, 30f),
        _ => (50f, 80f),
    };

    public void Advance(int frameCount)
    {
        Theta1 += WaveSpeed1 * frameCount;
        Theta2 += WaveSpeed2 * frameCount;

        Tex0Scroll = Repeat(Tex0Scroll + new Vector2(-0.003f, -0.001f) * frameCount);
        Tex1Scroll = Repeat(Tex1Scroll + new Vector2(-0.002f, 0.001f) * frameCount);
        Tex2Scroll = Repeat(Tex2Scroll + new Vector2(0f, 0.003f) * frameCount);
    }

    private static Vector2 Repeat(Vector2 v) => new(Repeat(v.X), Repeat(v.Y));

    private static float Repeat(float v)
    {
        v %= 1f;
        return v < 0f ? v + 1f : v;
    }

    private static Vector3 ComputeRailDirection(RailCoordSampleTable rail, float coord)
    {
        const float epsilon = 1f;
        Vector3 a = rail.PositionAtCoord(Math.Max(coord - epsilon, 0f));
        Vector3 b = rail.PositionAtCoord(Math.Min(coord + epsilon, rail.TotalLength));
        Vector3 delta = b - a;
        return delta.LengthSquared() > 1e-8f ? Vector3.Normalize(delta) : Vector3.UnitZ;
    }
}
