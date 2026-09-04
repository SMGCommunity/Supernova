using System.Numerics;

namespace SMGEditor.Core.Stage;

public sealed class RailCoordSampleTable
{
    public IReadOnlyList<Vector3> Positions { get; }

    public float TotalLength { get; }

    public RailCoordSampleTable(IReadOnlyList<PathPoint> points, bool closed, int samplesPerSegment = 16)
    {
        if (points.Count == 0)
        {
            Positions = [];
            TotalLength = 0f;
            return;
        }

        var lookup = new List<(float Distance, Vector3 Position)>();
        float cumulative = 0f;
        Vector3 previous = points[0].Position;
        lookup.Add((0f, previous));

        int segmentCount = closed ? points.Count : points.Count - 1;
        for (int i = 0; i < segmentCount; i++)
        {
            PathPoint a = points[i];
            PathPoint b = points[(i + 1) % points.Count];
            for (int s = 1; s <= samplesPerSegment; s++)
            {
                float t = s / (float)samplesPerSegment;
                Vector3 sample = SampleCubicBezier(a.Position, a.ControlPointOut, b.ControlPointIn, b.Position, t);
                cumulative += Vector3.Distance(previous, sample);
                previous = sample;
                lookup.Add((cumulative, sample));
            }
        }

        TotalLength = cumulative;
        if (TotalLength <= 0f)
        {
            Positions = [];
            return;
        }

        Vector3 LerpLookupAtCoord(float coord)
        {
            for (int i = 1; i < lookup.Count; i++)
            {
                if (lookup[i].Distance >= coord)
                {
                    (float prevDist, Vector3 prevPos) = lookup[i - 1];
                    (float nextDist, Vector3 nextPos) = lookup[i];
                    float span = nextDist - prevDist;
                    float t = span > 0f ? (coord - prevDist) / span : 0f;
                    return Vector3.Lerp(prevPos, nextPos, t);
                }
            }

            return lookup[^1].Position;
        }

        int count = (int)(TotalLength / 100f) + 2;
        var positions = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            positions.Add(LerpLookupAtCoord(MathF.Min(100f * i, TotalLength)));
        }

        Positions = positions;
    }

    public Vector3 PositionAtCoord(float coord)
    {
        if (Positions.Count == 0)
        {
            return Vector3.Zero;
        }

        if (Positions.Count == 1)
        {
            return Positions[0];
        }

        int index = Math.Clamp((int)(coord / 100f), 0, Positions.Count - 2);
        float remainder = coord - 100f * index;
        float span = 100f;
        if (Positions.Count - 3 < index)
        {
            span = TotalLength - 100f * index;
        }

        float t = span > 0f ? remainder / span : 0f;
        return Vector3.Lerp(Positions[index], Positions[index + 1], t);
    }

    private static Vector3 SampleCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }
}
