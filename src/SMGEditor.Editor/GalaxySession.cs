using System.Numerics;
using SMGEditor.Core.Database;
using SMGEditor.Core.Formats;
using SMGEditor.Core.Simulation;
using SMGEditor.Core.Stage;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal sealed class GalaxySession
{
    public required string GameRootDir { get; init; }

    public required string? OutputDir { get; init; }

    public required string GalaxyName { get; init; }
    public required int Game { get; init; }
    public required int ScenarioIndex { get; set; }

    public required List<EditableScenario> Scenarios { get; init; }

    public required List<EditableObject> Objects { get; set; }
    public required List<ObjectInstance> Instances { get; set; }
    public required List<EditablePath> Paths { get; set; }

    public HashSet<string> LoadedStagePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Matrix4x4> ZoneWorldMatrices { get; } = new(StringComparer.OrdinalIgnoreCase);

    public EditableObject? Selected { get; set; }
    public EditablePath? SelectedPath { get; set; }

    public int? SelectedPathPointIndex { get; set; }

    public PathPointPart SelectedPathPointPart { get; set; } = PathPointPart.Anchor;

    public Vector3 ViewCenter { get; set; }

    public EditHistory History { get; } = new();

    public static GalaxySession Load(string gameRootDir, string? outputDir, string galaxyName, int game, int scenarioIndex, ObjectDatabase db, SceneRenderer renderer)
    {
        List<EditableScenario> scenarios = GalaxyLoader.ListScenarios(gameRootDir, outputDir, galaxyName)
            .Select(EditableScenario.FromInfo)
            .ToList();

        var session = new GalaxySession
        {
            GameRootDir = gameRootDir,
            OutputDir = outputDir,
            GalaxyName = galaxyName,
            Game = game,
            ScenarioIndex = scenarioIndex,
            Scenarios = scenarios,
            Objects = [],
            Instances = [],
            Paths = [],
        };

        session.LoadScenario(scenarioIndex, db, renderer);
        return session;
    }

    public void LoadScenario(int scenarioIndex, ObjectDatabase db, SceneRenderer renderer)
    {
        EditableScenario scenario = Scenarios[scenarioIndex];

        var loadedModels = new Dictionary<string, LoadedObject?>(StringComparer.OrdinalIgnoreCase);
        var objects = new List<EditableObject>();
        var instances = new List<ObjectInstance>();
        var paths = new List<EditablePath>();

        LoadedStagePaths.Clear();
        ZoneWorldMatrices.Clear();
        var ancestry = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { GalaxyName };
        CollectStage(GalaxyName, GalaxyName, scenario.Fields, Matrix4x4.Identity, ancestry, db, renderer, loadedModels, objects, instances, paths);

        ScenarioIndex = scenarioIndex;
        Objects = objects;
        Instances = instances;
        Paths = paths;
        Selected = null;
        SelectedPath = null;
        SelectedPathPointIndex = null;
        SelectedPathPointPart = PathPointPart.Anchor;

        History.Clear();

        foreach (EditablePath path in paths)
        {
            path.Changed += () => RebuildRailDependents(path, renderer);
        }
    }

    // objects that need specific animating / rendering based on having paths will go here
    private void RebuildRailDependents(EditablePath path, SceneRenderer renderer)
    {
        foreach (EditableObject obj in Objects)
        {
            if (obj.StagePath != path.StagePath ||
                !(obj.Fields.TryGetValue("CommonPath_ID", out object? cpidObj) && cpidObj is int pathLinkId && pathLinkId == path.LinkId))
            {
                continue;
            }

            obj.RailMoveSim = null;

            if (obj.InternalName is "OceanRing" or "OceanRingAndFlag")
            {
                RebuildOceanRing(obj, path, renderer);
            }
            else if (obj.InternalName == "ElectricRailMoving")
            {
                RebuildElectricRailMovingRibbon(obj, path, renderer);
            }
            else if (obj.InternalName == "ElectricRail")
            {
                RebuildElectricRailStaticRibbon(obj, path, renderer);
            }
            else if (obj.InternalName == "NoteFairy")
            {
                RebuildNoteFairyNotes(obj, path);
            }
        }
    }

    private static int ReadObjArg(EditableObject obj, string name, int fallback) =>
        obj.Fields.TryGetValue(name, out object? v) && v is int i && i != -1 ? i : fallback;

    // OceanRing rendering
    private void RebuildOceanRing(EditableObject obj, EditablePath rail, SceneRenderer renderer)
    {
        if (rail.WorldPoints.Count < 2)
        {
            return;
        }

        var table = new RailCoordSampleTable(rail.WorldPoints, rail.Closed);
        if (table.TotalLength <= 0f)
        {
            return;
        }

        int arg0 = ReadObjArg(obj, "Obj_arg0", 0);
        (float height1, float height2) = OceanRingSimState.WaveHeightsForArg0(arg0);
        var sim = new OceanRingSimState(table, rail.Closed, height1, height2);
        OceanRingGpuMesh newMesh = renderer.UploadOceanRingMesh(sim);

        if (obj.OceanRingMesh is { } oldMesh)
        {
            renderer.DeleteOceanRingMesh(oldMesh);
        }

        obj.OceanRingSim = sim;
        obj.OceanRingMesh = newMesh;
    }

    private static void ReplaceLoadedObjectMesh(LoadedObject loaded, GpuMesh newMesh, SceneRenderer renderer)
    {
        foreach (RenderMesh old in loaded.RenderMeshes)
        {
            renderer.DeleteRenderMesh(old);
        }

        loaded.Meshes.Clear();
        loaded.Meshes.Add(newMesh);
        loaded.RenderMeshes.Clear();
        loaded.RenderMeshes.Add(renderer.UploadMeshOnly(newMesh));

        (Vector3 min, Vector3 max) = GalaxyLoader.ComputeLocalBounds([newMesh]);
        loaded.LocalBoundsMin = min;
        loaded.LocalBoundsMax = max;
    }

    // ElectricRailMoving rendering
    private void RebuildElectricRailMovingRibbon(EditableObject obj, EditablePath rail, SceneRenderer renderer)
    {
        if (obj.Instance is not { } instance || obj.RailRibbonTemplate is not { } template || template.Meshes.Count == 0 || rail.WorldPoints.Count < 2)
        {
            return;
        }

        var table = new RailCoordSampleTable(rail.WorldPoints, rail.Closed);
        if (table.TotalLength <= 0f)
        {
            return;
        }

        int stackHeight = ReadObjArg(obj, "Obj_arg3", 1);
        int segmentNum = ReadObjArg(obj, "Obj_arg0", 10);
        float movementSpeed = ReadObjArg(obj, "Obj_arg1", 10);
        float dashLength = obj.Fields.TryGetValue("Obj_arg2", out object? dl) && dl is int dlVal && dlVal != -1
            ? dlVal
            : ElectricRailMovingSimState.DefaultDashLength(table.TotalLength, segmentNum);

        if (GalaxyLoader.BuildElectricRailRibbonMesh(table, stackHeight, template.Meshes[0]) is not { } ribbonMesh)
        {
            return;
        }

        ReplaceLoadedObjectMesh(instance.Object, ribbonMesh, renderer);
        obj.ElectricRailSim = new ElectricRailMovingSimState(table, segmentNum, movementSpeed, dashLength, stackHeight);

        if (obj.ElectricRailPoints is { } pointInstances)
        {
            List<Vector3> positions = obj.ElectricRailSim.ComputePointPositions(0f);
            for (int i = 0; i < pointInstances.Count && i < positions.Count; i++)
            {
                pointInstances[i].WorldMatrix = Matrix4x4.CreateTranslation(positions[i]);
            }
        }
    }

    // ElectricRail rendering
    private void RebuildElectricRailStaticRibbon(EditableObject obj, EditablePath rail, SceneRenderer renderer)
    {
        if (obj.Instance is not { } instance || obj.RailRibbonTemplate is not { } template || template.Meshes.Count == 0 || rail.WorldPoints.Count < 2)
        {
            return;
        }

        var table = new RailCoordSampleTable(rail.WorldPoints, rail.Closed);
        int stackHeight = ReadObjArg(obj, "Obj_arg0", 1);

        if (GalaxyLoader.BuildElectricRailStaticRibbonMesh(table, stackHeight, template.Meshes[0]) is not { } ribbonMesh)
        {
            return;
        }

        ReplaceLoadedObjectMesh(instance.Object, ribbonMesh, renderer);

        if (obj.ElectricRailPoints is { Count: > 0 } pointInstances)
        {
            LoadedObject pointModel = pointInstances[0].Object;
            List<Vector3> positions = GalaxyLoader.BuildElectricRailPointPositions(rail.WorldPoints, stackHeight);

            foreach (ObjectInstance old in pointInstances)
            {
                Instances.Remove(old);
            }

            pointInstances.Clear();
            foreach (Vector3 pos in positions)
            {
                var pointInstance = new ObjectInstance { Object = pointModel, WorldMatrix = Matrix4x4.CreateTranslation(pos) };
                Instances.Add(pointInstance);
                pointInstances.Add(pointInstance);
            }
        }
    }

    // NoteFairy rendering
    private void RebuildNoteFairyNotes(EditableObject obj, EditablePath rail)
    {
        if (obj.NoteFairyNotes is not { Count: > 0 } notes || rail.WorldPoints.Count == 0)
        {
            return;
        }

        LoadedObject noteModel = notes[0].Object;

        int arg0 = obj.Fields.TryGetValue("Obj_arg0", out object? arg0Obj) && arg0Obj is int arg0Val ? arg0Val : -1;
        int arg4 = obj.Fields.TryGetValue("Obj_arg4", out object? arg4Obj) && arg4Obj is int arg4Val ? arg4Val : -1;

        GalaxyLoader.NoteFairyPlacementMode mode;
        int explicitCount = 0;
        if (arg0 == -2)
        {
            mode = GalaxyLoader.NoteFairyPlacementMode.PerRailPoint;
        }
        else if (arg0 > 0 && arg4 < 0)
        {
            mode = GalaxyLoader.NoteFairyPlacementMode.ExplicitCount;
            explicitCount = arg0;
        }
        else
        {
            mode = GalaxyLoader.NoteFairyPlacementMode.DistanceSpacing;
        }

        var table = new RailCoordSampleTable(rail.WorldPoints, rail.Closed);
        List<(Vector3 Position, Vector3 Forward)> notePlacements = GalaxyLoader.BuildNoteFairyNotes(table, rail.WorldPoints, mode, explicitCount);

        foreach (ObjectInstance old in notes)
        {
            Instances.Remove(old);
        }

        notes.Clear();
        foreach ((Vector3 pos, Vector3 forward) in notePlacements)
        {
            Matrix4x4 world = Matrix4x4.CreateWorld(pos, Vector3.Normalize(forward), Vector3.UnitY);
            var noteInstance = new ObjectInstance { Object = noteModel, WorldMatrix = world };
            Instances.Add(noteInstance);
            notes.Add(noteInstance);
        }
    }

    // classes to use so we can refer to SimpleMapObj and its obj_args to ensure accurate rendering
    private static readonly HashSet<string> SimpleMapObjClasses = new(StringComparer.Ordinal)
    {
        "SimpleMapObj", "AutoMakeMapObj", "SimpleMapObjFarMax", "SimpleMirrorReflectionObj", "RotateMoveObj", "UFOKinoko",
    };

    private void CollectStage(
        string stageName, string stagePath, IReadOnlyDictionary<string, object?> scenarioFields, Matrix4x4 zoneWorldMatrix, HashSet<string> ancestry,
        ObjectDatabase db, SceneRenderer renderer, Dictionary<string, LoadedObject?> loadedModels,
        List<EditableObject> objects, List<ObjectInstance> instances, List<EditablePath> paths)
    {
        IReadOnlyList<string> layers = ScenarioLayers.Resolve(scenarioFields, stageName);

        List<PlacedObject> placedObjects;
        string objectDataDir;
        try
        {
            (placedObjects, objectDataDir) = GalaxyLoader.LoadGalaxyMapPlacements(GameRootDir, OutputDir, stageName, layers);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Console.WriteLine($"  Zone '{stageName}': {ex.Message}, skipping.");
            return;
        }

        string? projectObjectDataDir = OutputDir is not null ? ProjectFiles.GameFilePath(OutputDir, "DATA/files/ObjectData") : null;

        LoadedStagePaths.Add(stagePath);
        ZoneWorldMatrices[stagePath] = zoneWorldMatrix;

        foreach (PathData path in GalaxyLoader.LoadGalaxyPaths(GameRootDir, OutputDir, stageName))
        {
            var worldPoints = new List<PathPoint>(path.Points.Count);
            foreach (PathPoint p in path.Points)
            {
                worldPoints.Add(new PathPoint
                {
                    Position = Vector3.Transform(p.Position, zoneWorldMatrix),
                    ControlPointIn = Vector3.Transform(p.ControlPointIn, zoneWorldMatrix),
                    ControlPointOut = Vector3.Transform(p.ControlPointOut, zoneWorldMatrix),
                    Fields = new Dictionary<string, object?>(p.Fields),
                });
            }

            var editablePath = new EditablePath
            {
                Name = path.Name,
                Closed = path.Closed,
                Usage = path.Usage,
                LinkId = path.LinkId,
                No = path.No,
                Fields = new Dictionary<string, object?>(path.Fields),
                StagePath = stagePath,
                WorldPoints = worldPoints,
                ZoneToWorld = zoneWorldMatrix,
                Color = PathColorPalette.ForIndex(paths.Count),
            };
            editablePath.RecomputePolyline();
            paths.Add(editablePath);
        }

        Dictionary<string, IReadOnlyDictionary<string, object?>> cameraParams = GalaxyLoader.LoadCameraParams(GameRootDir, OutputDir, stageName);

        LoadedObject? LoadAndCacheModel(string modelName)
        {
            if (!loadedModels.TryGetValue(modelName, out LoadedObject? cached))
            {
                cached = GalaxyLoader.TryLoadObject(modelName, objectDataDir, projectObjectDataDir: projectObjectDataDir);
                if (cached is not null)
                {
                    renderer.UploadObject(cached);
                }

                loadedModels[modelName] = cached;
            }

            return cached;
        }

        LoadedObject? LoadSimpleMapObjVariant(
            string modelName, int colorChangeFrame, int texChangeFrame, SceneRenderer sceneRenderer,
            string colorBrkFileName = "ColorChange.brk", (string FileName, float Frame)? uvAnimBake = null)
        {
            string key = $"{modelName}#c{colorChangeFrame}#t{texChangeFrame}#{colorBrkFileName}#{uvAnimBake?.FileName}";
            if (!loadedModels.TryGetValue(key, out LoadedObject? cached))
            {
                cached = GalaxyLoader.TryLoadObject(modelName, objectDataDir, colorChangeFrame, texChangeFrame, projectObjectDataDir, colorBrkFileName, uvAnimBake);
                if (cached is not null)
                {
                    sceneRenderer.UploadObject(cached);
                }

                loadedModels[key] = cached;
            }

            return cached;
        }

        int ReadArg(PlacedObject placement, string name, int fallback) =>
            placement.Fields.TryGetValue(name, out object? v) && v is int i && i != -1 ? i : fallback;

        (LoadedObject? RibbonLoaded, BTKUvAnimEntry? UvAnim) BuildRibbonLoadedObject(
            LoadedObject template, PlacedObject placement, int modelType, byte? alphaThresholdOverride, GpuMesh? ribbonMesh)
        {
            if (ribbonMesh is null)
            {
                return (null, null);
            }

            BDLMaterial? ribbonMaterial = template.Model.Materials.Count > 0 ? template.Model.Materials[0] : null;
            bool changed = false;

            if (ribbonMaterial is { } baseMaterial)
            {
                if (GalaxyLoader.TryLoadBrk(placement.Name, objectDataDir, projectObjectDataDir) is { } brk)
                {
                    ribbonMaterial = brk.ApplyToMaterial(baseMaterial, modelType);
                    changed = true;
                }

                if (alphaThresholdOverride is { } alpha && ribbonMaterial.TevRegisters.Count > 1)
                {
                    var regs = ribbonMaterial.TevRegisters.ToList();
                    regs[1] = new BDLTevRegisterColor(regs[1].R, regs[1].G, regs[1].B, alpha);
                    ribbonMaterial = ribbonMaterial.With(tevRegisters: regs);
                    changed = true;
                }
            }

            BDLModel ribbonModel = changed && ribbonMaterial is not null ? template.Model.WithMaterials([ribbonMaterial]) : template.Model;

            BTKUvAnimEntry? uvAnim = null;
            if (ribbonModel.Materials.Count > 0 && GalaxyLoader.TryLoadBtk(placement.Name, objectDataDir, projectObjectDataDir: projectObjectDataDir) is { } btk)
            {
                uvAnim = btk.Entries.FirstOrDefault(e => e.MaterialName == ribbonModel.Materials[0].Name);
                if (btk.IsMaya && uvAnim is not null)
                {
                    Console.WriteLine($"  {placement.Name}: BTK uses the Maya texture-matrix formula, which isn't implemented - texcoord0 won't animate.");
                    uvAnim = null;
                }
            }

            (Vector3 min, Vector3 max) = GalaxyLoader.ComputeLocalBounds([ribbonMesh]);
            var ribbonLoaded = new LoadedObject
            {
                Name = placement.Name,
                Model = ribbonModel,
                Meshes = [ribbonMesh],
                LocalBoundsMin = min,
                LocalBoundsMax = max,
                WaitAnimation = null,
                IsPreBakedWorldSpace = true,
            };

            foreach ((int key, uint handle) in template.TextureHandles)
            {
                ribbonLoaded.TextureHandles[key] = handle;
            }

            ribbonLoaded.RenderMeshes.Add(renderer.UploadMeshOnly(ribbonMesh));

            return (ribbonLoaded, uvAnim);
        }

        (OceanRingSimState? Sim, OceanRingGpuMesh? Mesh) BuildOceanRingInstance(PlacedObject placement)
        {
            if (!placement.Fields.TryGetValue("CommonPath_ID", out object? cpidObj) || cpidObj is not int pathLinkId || pathLinkId == 65535)
            {
                return (null, null);
            }

            EditablePath? rail = paths.FirstOrDefault(p => p.StagePath == stagePath && p.LinkId == pathLinkId);
            if (rail is null || rail.WorldPoints.Count < 2)
            {
                return (null, null);
            }

            var table = new RailCoordSampleTable(rail.WorldPoints, rail.Closed);
            if (table.TotalLength <= 0f)
            {
                return (null, null);
            }

            if (GalaxyLoader.TryLoadWaterWaveTextures(objectDataDir, projectObjectDataDir) is not { } textures)
            {
                return (null, null);
            }

            renderer.EnsureOceanRingTextures(textures.Water, textures.Indirect);

            int arg0 = ReadArg(placement, "Obj_arg0", 0);
            (float height1, float height2) = OceanRingSimState.WaveHeightsForArg0(arg0);
            var sim = new OceanRingSimState(table, rail.Closed, height1, height2);
            OceanRingGpuMesh mesh = renderer.UploadOceanRingMesh(sim);
            return (sim, mesh);
        }

        (LoadedObject? Ribbon, ElectricRailMovingSimState? Sim, List<ObjectInstance>? PointInstances, BTKUvAnimEntry? UvAnim) BuildElectricRailInstance(PlacedObject placement)
        {
            if (!placement.Fields.TryGetValue("CommonPath_ID", out object? cpidObj) || cpidObj is not int pathLinkId || pathLinkId == 65535)
            {
                return (null, null, null, null);
            }

            EditablePath? rail = paths.FirstOrDefault(p => p.StagePath == stagePath && p.LinkId == pathLinkId);
            if (rail is null || rail.WorldPoints.Count < 2)
            {
                return (null, null, null, null);
            }

            LoadedObject? template = LoadAndCacheModel(placement.Name);
            if (template is null || template.Meshes.Count == 0)
            {
                return (null, null, null, null);
            }

            var table = new RailCoordSampleTable(rail.WorldPoints, rail.Closed);
            if (table.TotalLength <= 0f)
            {
                return (null, null, null, null);
            }

            int stackHeight = ReadArg(placement, "Obj_arg3", 1);
            int segmentNum = ReadArg(placement, "Obj_arg0", 10);
            float movementSpeed = ReadArg(placement, "Obj_arg1", 10);
            float dashLength = placement.Fields.TryGetValue("Obj_arg2", out object? dl) && dl is int dlVal && dlVal != -1
                ? dlVal
                : ElectricRailMovingSimState.DefaultDashLength(table.TotalLength, segmentNum);

            float segmentSpacing = table.TotalLength / segmentNum;
            float angle = (-MathF.Tau * dashLength) / segmentSpacing;
            float thresholdValue = (15f * MathF.Sin(angle)) + ((255f * dashLength) / segmentSpacing);
            byte alphaThreshold = (byte)Math.Clamp((int)thresholdValue, 0, 255);

            int modelType = ReadArg(placement, "Obj_arg4", 0);
            GpuMesh? ribbonMesh = GalaxyLoader.BuildElectricRailRibbonMesh(table, stackHeight, template.Meshes[0]);
            (LoadedObject? ribbonLoaded, BTKUvAnimEntry? uvAnim) = BuildRibbonLoadedObject(template, placement, modelType, alphaThreshold, ribbonMesh);

            var sim = new ElectricRailMovingSimState(table, segmentNum, movementSpeed, dashLength, stackHeight);

            List<ObjectInstance>? pointInstances = null;
            if (LoadAndCacheModel("ElectricRailPoint") is { } pointModel)
            {
                pointInstances = new List<ObjectInstance>(sim.PointCount);
                foreach (Vector3 pos in sim.ComputePointPositions(0f))
                {
                    var pointInstance = new ObjectInstance { Object = pointModel, WorldMatrix = Matrix4x4.CreateTranslation(pos) };
                    instances.Add(pointInstance);
                    pointInstances.Add(pointInstance);
                }
            }

            return (ribbonLoaded, sim, pointInstances, uvAnim);
        }

        (LoadedObject? Ribbon, List<ObjectInstance>? PointInstances, BTKUvAnimEntry? UvAnim) BuildElectricRailStaticInstance(PlacedObject placement)
        {
            if (!placement.Fields.TryGetValue("CommonPath_ID", out object? cpidObj) || cpidObj is not int pathLinkId || pathLinkId == 65535)
            {
                return (null, null, null);
            }

            EditablePath? rail = paths.FirstOrDefault(p => p.StagePath == stagePath && p.LinkId == pathLinkId);
            if (rail is null || rail.WorldPoints.Count < 2)
            {
                return (null, null, null);
            }

            LoadedObject? template = LoadAndCacheModel(placement.Name);
            if (template is null || template.Meshes.Count == 0)
            {
                return (null, null, null);
            }

            var table = new RailCoordSampleTable(rail.WorldPoints, rail.Closed);

            int stackHeight = ReadArg(placement, "Obj_arg0", 1);
            int modelType = ReadArg(placement, "Obj_arg3", 0);

            GpuMesh? ribbonMesh = GalaxyLoader.BuildElectricRailStaticRibbonMesh(table, stackHeight, template.Meshes[0]);
            (LoadedObject? ribbonLoaded, BTKUvAnimEntry? uvAnim) = BuildRibbonLoadedObject(template, placement, modelType, alphaThresholdOverride: null, ribbonMesh);

            List<ObjectInstance>? pointInstances = null;
            if (LoadAndCacheModel("ElectricRailPoint") is { } pointModel)
            {
                List<Vector3> pointPositions = GalaxyLoader.BuildElectricRailPointPositions(rail.WorldPoints, stackHeight);
                pointInstances = new List<ObjectInstance>(pointPositions.Count);
                foreach (Vector3 pos in pointPositions)
                {
                    var pointInstance = new ObjectInstance { Object = pointModel, WorldMatrix = Matrix4x4.CreateTranslation(pos) };
                    instances.Add(pointInstance);
                    pointInstances.Add(pointInstance);
                }
            }

            return (ribbonLoaded, pointInstances, uvAnim);
        }

        List<ObjectInstance>? BuildNoteFairyInstance(PlacedObject placement)
        {
            if (!placement.Fields.TryGetValue("CommonPath_ID", out object? cpidObj) || cpidObj is not int pathLinkId || pathLinkId == 65535)
            {
                return null;
            }

            EditablePath? rail = paths.FirstOrDefault(p => p.StagePath == stagePath && p.LinkId == pathLinkId);
            if (rail is null || rail.WorldPoints.Count == 0)
            {
                return null;
            }

            if (LoadAndCacheModel("Note") is not { } noteModel)
            {
                return null;
            }

            int arg0 = placement.Fields.TryGetValue("Obj_arg0", out object? arg0Obj) && arg0Obj is int arg0Val ? arg0Val : -1;
            int arg4 = placement.Fields.TryGetValue("Obj_arg4", out object? arg4Obj) && arg4Obj is int arg4Val ? arg4Val : -1;

            GalaxyLoader.NoteFairyPlacementMode mode;
            int explicitCount = 0;
            if (arg0 == -2)
            {
                mode = GalaxyLoader.NoteFairyPlacementMode.PerRailPoint;
            }
            else if (arg0 > 0 && arg4 < 0)
            {
                mode = GalaxyLoader.NoteFairyPlacementMode.ExplicitCount;
                explicitCount = arg0;
            }
            else
            {
                mode = GalaxyLoader.NoteFairyPlacementMode.DistanceSpacing;
            }

            var table = new RailCoordSampleTable(rail.WorldPoints, rail.Closed);
            List<(Vector3 Position, Vector3 Forward)> notes = GalaxyLoader.BuildNoteFairyNotes(table, rail.WorldPoints, mode, explicitCount);
            if (notes.Count == 0)
            {
                return null;
            }

            var noteInstances = new List<ObjectInstance>(notes.Count);
            foreach ((Vector3 pos, Vector3 forward) in notes)
            {
                Matrix4x4 world = Matrix4x4.CreateWorld(pos, Vector3.Normalize(forward), Vector3.UnitY);
                var noteInstance = new ObjectInstance { Object = noteModel, WorldMatrix = world };
                instances.Add(noteInstance);
                noteInstances.Add(noteInstance);
            }

            return noteInstances;
        }

        LoadedObject? BuildStarPieceInstance(PlacedObject placement)
        {
            bool isFirstStarPieceLoad = !loadedModels.ContainsKey("StarPiece");
            LoadedObject? template = LoadAndCacheModel("StarPiece");
            if (template is null || template.Meshes.Count == 0 || template.Model.Materials.Count == 0)
            {
                return null;
            }

            if (isFirstStarPieceLoad &&
                GalaxyLoader.TryLoadBtk("StarPiece", objectDataDir, "Gift", projectObjectDataDir) is { } giftBtk)
            {
                BTKUvAnimEntry? entry = giftBtk.Entries.FirstOrDefault(e => e.MaterialName == template.Model.Materials[0].Name);
                if (entry is not null)
                {
                    var uv0 = entry.SampleMatrix(5.0f);
                    float[] baked = GalaxyLoader.ScrollRibbonUv(template.Meshes[0], null, uv0);
                    Array.Copy(baked, template.Meshes[0].Vertices, baked.Length);
                    if (template.RenderMeshes.Count > 0)
                    {
                        renderer.UpdateMeshVertices(template.RenderMeshes[0], template.Meshes[0].Vertices);
                    }
                }
            }

            int arg3 = placement.Fields.TryGetValue("Obj_arg3", out object? v) && v is int arg3Val ? arg3Val : -1;

            int seed = 17;
            seed = (seed * 31) + BitConverter.SingleToInt32Bits(placement.Position.X);
            seed = (seed * 31) + BitConverter.SingleToInt32Bits(placement.Position.Y);
            seed = (seed * 31) + BitConverter.SingleToInt32Bits(placement.Position.Z);
            BDLColor color = GalaxyLoader.ResolveStarPieceColor(arg3, seed);

            BDLMaterial tintedMaterial = template.Model.Materials[0].With(materialColor: color);
            BDLModel tintedModel = template.Model.WithMaterials([tintedMaterial]);

            var tintedLoaded = new LoadedObject
            {
                Name = template.Name,
                Model = tintedModel,
                Meshes = template.Meshes,
                LocalBoundsMin = template.LocalBoundsMin,
                LocalBoundsMax = template.LocalBoundsMax,
                WaitAnimation = null,
            };

            foreach ((int key, uint handle) in template.TextureHandles)
            {
                tintedLoaded.TextureHandles[key] = handle;
            }

            tintedLoaded.RenderMeshes.AddRange(template.RenderMeshes);

            return tintedLoaded;
        }

        foreach (PlacedObject po in placedObjects)
        {
            LoadedObject? model;
            List<(GalaxyLoader.MultiPartInfo Part, LoadedObject Model)>? extraPartModels = null;
            ElectricRailMovingSimState? electricRailSim = null;
            List<ObjectInstance>? electricRailPoints = null;
            BTKUvAnimEntry? electricRailUvAnim = null;
            List<ObjectInstance>? noteFairyNotes = null;
            OceanRingSimState? oceanRingSim = null;
            OceanRingGpuMesh? oceanRingMesh = null;
            LoadedObject? railRibbonTemplate = null;

            if (GalaxyLoader.MultiPartModels.TryGetValue(po.Name, out GalaxyLoader.MultiPartInfo[]? parts))
            {
                model = parts.Length > 0 ? LoadAndCacheModel(parts[0].ModelName) : null;
                for (int i = 1; i < parts.Length; i++)
                {
                    if (LoadAndCacheModel(parts[i].ModelName) is { } partModel)
                    {
                        (extraPartModels ??= []).Add((parts[i], partModel));
                    }
                }
            }
            else if (GalaxyLoader.TryBuildCircleCoinGroupParts(po.Name, po.Fields) is { } circleCoinParts)
            {
                model = null;
                foreach (GalaxyLoader.MultiPartInfo part in circleCoinParts)
                {
                    if (LoadAndCacheModel(part.ModelName) is { } partModel)
                    {
                        (extraPartModels ??= []).Add((part, partModel));
                    }
                }
            }
            else if (po.Name == "ElectricRailMoving")
            {
                (model, electricRailSim, electricRailPoints, electricRailUvAnim) = BuildElectricRailInstance(po);
                railRibbonTemplate = loadedModels.GetValueOrDefault(po.Name);
            }
            else if (po.Name == "ElectricRail")
            {
                (model, electricRailPoints, electricRailUvAnim) = BuildElectricRailStaticInstance(po);
                railRibbonTemplate = loadedModels.GetValueOrDefault(po.Name);
            }
            else if (po.Name is "OceanRing" or "OceanRingAndFlag")
            {
                model = null;
                (oceanRingSim, oceanRingMesh) = BuildOceanRingInstance(po);
            }
            else if (po.Name == "NoteFairy")
            {
                model = null;
                noteFairyNotes = BuildNoteFairyInstance(po);
            }
            else if (po.Name == "StarPiece")
            {
                model = BuildStarPieceInstance(po);
            }
            else if (po.Name == "Caretaker")
            {
                model = LoadSimpleMapObjVariant(po.Name, ReadArg(po, "Obj_arg3", 0), -1, renderer, "BodyColor.brk", ("Dirt.btk", 0f));
            }
            else if (SimpleMapObjClasses.Contains(db.FindObject(po.Name)?.ClassName(Game) ?? "")
                && (ReadArg(po, "Obj_arg0", -1) >= 0 || ReadArg(po, "Obj_arg1", -1) >= 0))
            {
                model = LoadSimpleMapObjVariant(po.Name, ReadArg(po, "Obj_arg0", -1), ReadArg(po, "Obj_arg1", -1), renderer);
            }
            else
            {
                model = LoadAndCacheModel(po.Name) ?? GalaxyLoader.TryLoadBtiBillboard(po.Name, objectDataDir, projectObjectDataDir);
            }

            ObjectDbEntry? dbEntry = db.FindObject(po.Name);
            ObjectDbClass? dbClass = db.FindClass(dbEntry?.ClassName(Game) ?? po.Name);
            int lightGroup = LightGroupTable.ForClass(dbEntry?.ClassName(Game) ?? po.Name, dbEntry?.Category ?? "");

            Matrix4x4 localMatrix = GalaxyLoader.ComposePlacementMatrix(po.Position, po.RotationDegrees, po.Scale);
            Matrix4x4 worldMatrix = localMatrix * zoneWorldMatrix;

            Vector3 worldPosition = po.Position;
            Vector3 worldRotation = po.RotationDegrees;
            Vector3 worldScale = po.Scale;
            if (!zoneWorldMatrix.IsIdentity)
            {
                Matrix4x4.Decompose(worldMatrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation);
                worldPosition = translation;
                worldRotation = GalaxyLoader.EulerXyzFromMatrix(Matrix4x4.CreateFromQuaternion(rotation));
                worldScale = scale;
            }

            ObjectInstance? instance = null;
            List<(Matrix4x4 LocalOffset, ObjectInstance Instance)>? extraParts = null;
            if (model is not null)
            {
                instance = new ObjectInstance { Object = model, WorldMatrix = model.IsPreBakedWorldSpace ? Matrix4x4.Identity : worldMatrix, LightGroup = lightGroup };
                instances.Add(instance);
            }

            if (extraPartModels is not null)
            {
                extraParts = new List<(Matrix4x4, ObjectInstance)>(extraPartModels.Count);
                foreach ((GalaxyLoader.MultiPartInfo part, LoadedObject partModel) in extraPartModels)
                {
                    Matrix4x4 localOffset = GalaxyLoader.ComposeMultiPartOffset(part);
                    var partInstance = new ObjectInstance { Object = partModel, WorldMatrix = localOffset * worldMatrix, LightGroup = lightGroup };
                    instances.Add(partInstance);
                    extraParts.Add((localOffset, partInstance));
                }
            }

            Dictionary<string, object?>? cameraParamFields = null;
            if (po.SourceList == "CameraCubeInfo" &&
                GalaxyLoader.ResolveCameraParam(cameraParams, po.Fields) is { } resolvedCameraParam)
            {
                cameraParamFields = new Dictionary<string, object?>(resolvedCameraParam);
            }

            objects.Add(new EditableObject
            {
                InternalName = po.Name,
                Layer = po.Layer,
                SourceList = po.SourceList,
                StagePath = stagePath,
                Position = worldPosition,
                Rotation = worldRotation,
                Scale = worldScale,
                Fields = new Dictionary<string, object?>(po.Fields),
                DbEntry = dbEntry,
                DbClass = dbClass,
                Instance = instance,
                ExtraParts = extraParts,
                CameraParamFields = cameraParamFields,
                ElectricRailSim = electricRailSim,
                ElectricRailPoints = electricRailPoints,
                ElectricRailUvAnim = electricRailUvAnim,
                NoteFairyNotes = noteFairyNotes,
                OceanRingSim = oceanRingSim,
                OceanRingMesh = oceanRingMesh,
                RailRibbonTemplate = railRibbonTemplate,
            });

            if (po.SourceList == "StageObjInfo" && !ancestry.Contains(po.Name))
            {
                var childAncestry = new HashSet<string>(ancestry, StringComparer.OrdinalIgnoreCase) { po.Name };
                CollectStage(po.Name, $"{stagePath}/{po.Name}", scenarioFields, worldMatrix, childAncestry, db, renderer, loadedModels, objects, instances, paths);
            }
        }
    }
}
