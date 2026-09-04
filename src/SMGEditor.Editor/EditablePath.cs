using System.Numerics;
using SMGEditor.Core.Stage;

namespace SMGEditor.Editor;

internal sealed class EditablePath
{
    public required string Name { get; set; }
    public required bool Closed { get; set; }
    public required string Usage { get; set; }

    public required int LinkId { get; init; }

    public required int No { get; init; }

    public required Dictionary<string, object?> Fields { get; init; }

    public required string StagePath { get; init; }

    public required List<PathPoint> WorldPoints { get; init; }

    public Matrix4x4 ZoneToWorld { get; init; } = Matrix4x4.Identity;

    public IReadOnlyList<Vector3> WorldPolyline { get; private set; } = [];

    public required Vector3 Color { get; init; }

    public void RecomputePolyline()
    {
        WorldPolyline = PathTessellator.BuildPolyline(WorldPoints, Closed);
        Changed?.Invoke();
    }

    public event Action? Changed;
}

internal enum PathPointPart
{
    Anchor,
    ControlIn,
    ControlOut,
}

internal sealed class PathPointGizmoTarget(EditablePath path, Func<Vector3> get, Action<Vector3> set) : IGizmoTarget
{
    public Vector3 Position
    {
        get => get();
        set => set(value);
    }

    public Vector3 Rotation { get => Vector3.Zero; set { } }
    public bool SupportsRotation => false;
    public void OnChanged() => path.RecomputePolyline();
}

internal static class PathColorPalette
{
    private static readonly Vector3[] Colors =
    [
        new(0.90f, 0.10f, 0.10f),
        new(0.10f, 0.55f, 0.95f),
        new(0.15f, 0.80f, 0.20f),
        new(0.95f, 0.65f, 0.05f),
        new(0.75f, 0.15f, 0.85f),
        new(0.10f, 0.85f, 0.80f),
        new(0.95f, 0.90f, 0.10f),
        new(0.55f, 0.30f, 0.10f),
        new(0.95f, 0.35f, 0.65f),
        new(0.45f, 0.45f, 0.95f),
        new(0.35f, 0.65f, 0.15f),
        new(0.90f, 0.10f, 0.45f),
        new(0.10f, 0.45f, 0.35f),
        new(0.65f, 0.65f, 0.65f),
        new(0.55f, 0.10f, 0.10f),
    ];

    public static Vector3 ForIndex(int index) => Colors[((index % Colors.Length) + Colors.Length) % Colors.Length];
}
