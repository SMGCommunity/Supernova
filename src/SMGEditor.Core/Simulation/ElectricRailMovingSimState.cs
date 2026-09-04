using System.Numerics;
using SMGEditor.Core.Stage;

namespace SMGEditor.Core.Simulation;

public sealed class ElectricRailMovingSimState
{
    private readonly RailCoordSampleTable _table;
    private readonly int _segmentNum;
    private readonly float _movementSpeed;
    private readonly float _dashLength;
    private readonly int _stackHeight;

    public ElectricRailMovingSimState(RailCoordSampleTable table, int segmentNum, float movementSpeed, float dashLength, int stackHeight)
    {
        _table = table;
        _segmentNum = Math.Max(segmentNum, 1);
        _movementSpeed = movementSpeed;
        _dashLength = dashLength;
        _stackHeight = Math.Max(stackHeight, 1);
    }

    public static float DefaultDashLength(float railTotalLength, int segmentNum) =>
        segmentNum > 0 ? railTotalLength / (2f * segmentNum) : railTotalLength;

    public List<Vector3> ComputePointPositions(float clockSeconds)
    {
        var points = new List<Vector3>(_segmentNum * 2 * _stackHeight);
        if (_table.TotalLength <= 0f)
        {
            return points;
        }

        float ec = Repeat(_movementSpeed * clockSeconds * 60f, _table.TotalLength);
        float segmentSpacing = _table.TotalLength / _segmentNum;

        for (int seg = 0; seg < _segmentNum; seg++)
        {
            float coord = Repeat(ec, _table.TotalLength);
            Vector3 position = _table.PositionAtCoord(coord);
            float previousCoord = Repeat(coord - _dashLength, _table.TotalLength);
            Vector3 previousPosition = _table.PositionAtCoord(previousCoord);

            points.Add(position);
            points.Add(previousPosition);

            for (int layer = 1; layer < _stackHeight; layer++)
            {
                points.Add(position + Vector3.UnitY * (100f * layer));
            }

            for (int layer = 1; layer < _stackHeight; layer++)
            {
                points.Add(previousPosition + Vector3.UnitY * (100f * layer));
            }

            ec = Repeat(ec + segmentSpacing, _table.TotalLength);
        }

        return points;
    }

    public int PointCount => _segmentNum * 2 * _stackHeight;

    public (float Scale, float Offset) ComputeRibbonUvScroll(float clockSeconds)
    {
        if (_table.TotalLength <= 0f)
        {
            return (1f, 0f);
        }

        float coordPhase = Repeat(_movementSpeed * clockSeconds * 60f, _table.TotalLength);
        float scale = (100f * _segmentNum) / (0.25f * _table.TotalLength);
        float offset = -(0.25f * scale * coordPhase) / 100f;
        return (scale, offset);
    }

    private static float Repeat(float coord, float length) => length > 0f ? ((coord % length) + length) % length : 0f;
}
