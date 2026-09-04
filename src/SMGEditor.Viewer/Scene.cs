using System.Numerics;
using SMGEditor.Core.Formats;

namespace SMGEditor.Viewer;

public sealed class LoadedObject
{
    public required string Name { get; init; }
    public required BDLModel Model { get; init; }
    public required List<GpuMesh> Meshes { get; init; }

    public required Vector3 LocalBoundsMin { get; set; }
    public required Vector3 LocalBoundsMax { get; set; }
    public Dictionary<int, uint> TextureHandles { get; } = new();
    public List<RenderMesh> RenderMeshes { get; } = [];

    public BCKAnimation? WaitAnimation { get; init; }

    public bool IsPreBakedWorldSpace { get; init; }

    public Matrix4x4[]? CachedInverseBindMatrices { get; set; }
}

public sealed class ObjectInstance
{
    public required LoadedObject Object { get; init; }
    public required Matrix4x4 WorldMatrix { get; set; }
    public int LightGroup { get; set; } = 3;
}

public sealed class RenderMesh
{
    public required uint Vao { get; init; }

    public required uint Vbo { get; init; }

    public required int MaterialIndex { get; init; }
    public ushort? Texture0Index { get; init; }
    public ushort? Texture1Index { get; init; }
    public ushort? Texture2Index { get; init; }
    public ushort? Texture3Index { get; init; }
    public int? Texture0Slot { get; init; }
    public int? Texture1Slot { get; init; }
    public int? Texture2Slot { get; init; }
    public int? Texture3Slot { get; init; }
    public ushort? IndirectTextureIndex { get; init; }
    public BDLTexMatrix? Uv0EnvMapMatrix { get; init; }
    public BDLTexMatrix? Uv1EnvMapMatrix { get; init; }
    public BDLTexMatrix? Uv2EnvMapMatrix { get; init; }
    public BDLTexMatrix? Uv3EnvMapMatrix { get; init; }
    public required int VertexCount { get; init; }
}
