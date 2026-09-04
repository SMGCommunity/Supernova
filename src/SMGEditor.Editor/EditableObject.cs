using System.Numerics;
using SMGEditor.Core.Database;
using SMGEditor.Core.Formats;
using SMGEditor.Core.Simulation;
using SMGEditor.Core.Stage;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal sealed class EditableObject : IGizmoTarget
{
    public bool SupportsRotation => true;

    void IGizmoTarget.OnChanged() => SyncTransformToInstance();


    public required string InternalName { get; init; }
    public required string Layer { get; init; }
    public required Vector3 Position { get; set; }
    public required Vector3 Rotation { get; set; }
    public required Vector3 Scale { get; set; }

    public required Dictionary<string, object?> Fields { get; init; }

    public required string SourceList { get; init; }

    public required string StagePath { get; init; }

    public ObjectDbEntry? DbEntry { get; init; }
    public ObjectDbClass? DbClass { get; init; }
    public ObjectInstance? Instance { get; set; }

    public IReadOnlyList<(Matrix4x4 LocalOffset, ObjectInstance Instance)>? ExtraParts { get; init; }

    public IEnumerable<ObjectInstance> AllInstances
    {
        get
        {
            if (Instance is { } instance)
            {
                yield return instance;
            }

            if (ExtraParts is not null)
            {
                foreach ((_, ObjectInstance partInstance) in ExtraParts)
                {
                    yield return partInstance;
                }
            }

            if (ElectricRailPoints is not null)
            {
                foreach (ObjectInstance pointInstance in ElectricRailPoints)
                {
                    yield return pointInstance;
                }
            }

            if (NoteFairyNotes is not null)
            {
                foreach (ObjectInstance noteInstance in NoteFairyNotes)
                {
                    yield return noteInstance;
                }
            }
        }
    }

    public Dictionary<string, object?>? CameraParamFields { get; init; }

    public RailMoveSimState? RailMoveSim { get; set; }

    public RotateMoveSimState? RotateMoveSim { get; set; }

    public OceanRingSimState? OceanRingSim { get; set; }

    public OceanRingGpuMesh? OceanRingMesh { get; set; }

    public WalkerStateWanderSimState? WalkerStateWanderSim { get; set; }

    public AstroDomeOrbitSimState? AstroDomeOrbitSim { get; set; }

    public ElectricRailMovingSimState? ElectricRailSim { get; set; }

    public List<ObjectInstance>? ElectricRailPoints { get; init; }

    public BTKUvAnimEntry? ElectricRailUvAnim { get; set; }

    public LoadedObject? RailRibbonTemplate { get; init; }

    public List<ObjectInstance>? NoteFairyNotes { get; init; }

    // GeneralPosInfo is one of the few that don't use "name" for object access
    public string DisplayName =>
        SourceList == "GeneralPosInfo" && Fields.TryGetValue("PosName", out object? posName) && posName is string { Length: > 0 } s
            ? GeneralPosCatalog.Friendly(s)
            : DbEntry?.Name ?? InternalName;

    public int? LinkId => Fields.TryGetValue("l_id", out object? v) && v is int id ? id : null;

    public int? MarioNo => Fields.TryGetValue("MarioNo", out object? v) && v is int id ? id : null;

    public int? TreeId => LinkId ?? MarioNo;

    public string TreeGroup => SourceList switch
    {
        "ObjInfo" => "Objects",
        "AreaObjInfo" => "Areas",
        "CameraCubeInfo" => "Cameras",
        "StartInfo" => "Starting Positions",
        "PlanetObjInfo" => "Planets",
        "DemoObjInfo" => "Demo Objects",
        "MapPartsInfo" => "Map Parts",
        "GeneralPosInfo" => "General Positions",
        _ => SourceList,
    };

    public void SyncTransformToInstance()
    {
        Matrix4x4 world = GalaxyLoader.ComposePlacementMatrix(Position, Rotation, Scale);
        if (Instance is { } instance && !instance.Object.IsPreBakedWorldSpace)
        {
            instance.WorldMatrix = world;
        }

        if (ExtraParts is not null)
        {
            foreach ((Matrix4x4 localOffset, ObjectInstance partInstance) in ExtraParts)
            {
                partInstance.WorldMatrix = localOffset * world;
            }
        }
    }
}
