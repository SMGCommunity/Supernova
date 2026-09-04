namespace SMGEditor.Viewer;

public sealed class OceanRingGpuMesh
{
    public required uint Vao { get; init; }
    public required uint Vbo { get; init; }
    public required uint Ebo { get; init; }
    public required int IndexCount { get; init; }
}
