using System.Numerics;
using SMGEditor.Core.Formats;
using SMGEditor.Core.Stage;

namespace SMGEditor.Viewer;

public static class GalaxyLoader
{
    public static string MapArcRelativePath(string gameRootDir, string stageName)
    {
        string stageDataDir = Path.Combine("DATA", "files", "StageData");
        string nestedAbsolute = ProjectFiles.GameFilePath(gameRootDir, Path.Combine(stageDataDir, stageName, stageName + "Map.arc"));
        return File.Exists(nestedAbsolute)
            ? Path.Combine(stageDataDir, stageName, stageName + "Map.arc")
            : Path.Combine(stageDataDir, stageName + ".arc");
    }

    public static string FindSmgFilesRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "smg_files");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the smg_files directory.");
    }

    public static (List<PlacedObject> Placements, string ObjectDataDir) LoadGalaxyPlacements(
        string gameRootDir, string galaxyName, int scenarioIndex, string? outputDir = null)
    {
        IReadOnlyList<ScenarioInfo> scenarios = ListScenarios(gameRootDir, outputDir, galaxyName);
        IReadOnlyList<string> layers = ScenarioLayers.Resolve(scenarios[scenarioIndex].Fields, galaxyName);
        return LoadGalaxyMapPlacements(gameRootDir, outputDir, galaxyName, layers);
    }

    public static (List<PlacedObject> Placements, string ObjectDataDir) LoadGalaxyMapPlacements(
        string gameRootDir, string? outputDir, string galaxyName, IReadOnlyList<string> layers)
    {
        string relativePath = MapArcRelativePath(gameRootDir, galaxyName);
        (RARCArchive mapArchive, _) = ProjectFiles.LoadArc(gameRootDir, outputDir, relativePath);

        var placedObjects = new List<PlacedObject>();
        foreach (string fileName in PlacementListNames)
        {
            placedObjects.AddRange(StagePlacementReader.ReadPlacementFile(mapArchive, layers, "jmp/Placement", fileName));
        }

        placedObjects.AddRange(StagePlacementReader.ReadPlacementFile(mapArchive, layers, "jmp/Start", "StartInfo"));

        placedObjects.AddRange(StagePlacementReader.ReadPlacementFile(mapArchive, layers, "jmp/MapParts", "MapPartsInfo"));

        placedObjects.AddRange(StagePlacementReader.ReadPlacementFile(mapArchive, layers, "jmp/GeneralPos", "GeneralPosInfo"));

        string objectDataDir = ProjectFiles.GameFilePath(gameRootDir, "DATA/files/ObjectData");
        return (placedObjects, objectDataDir);
    }

    public static List<PathData> LoadGalaxyPaths(string gameRootDir, string? outputDir, string stageName)
    {
        string relativePath = MapArcRelativePath(gameRootDir, stageName);
        string mapArcPath = ProjectFiles.ResolveFile(gameRootDir, outputDir, relativePath);
        if (!File.Exists(mapArcPath))
        {
            return [];
        }

        RARCArchive mapArchive = RARCArchive.Load(mapArcPath);
        return StagePathReader.ReadPaths(mapArchive);
    }

    public static Dictionary<string, IReadOnlyDictionary<string, object?>> LoadCameraParams(string gameRootDir, string? outputDir, string stageName)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, object?>>();

        string relativePath = MapArcRelativePath(gameRootDir, stageName);
        string mapArcPath = ProjectFiles.ResolveFile(gameRootDir, outputDir, relativePath);
        if (!File.Exists(mapArcPath))
        {
            return result;
        }

        RARCArchive mapArchive = RARCArchive.Load(mapArcPath);
        RARCFile? file = mapArchive.Root.FindFile("camera/CameraParam.bcam");
        if (file is null)
        {
            return result;
        }

        BCSVTable table = BCSVTable.Load(file.Data);
        foreach (IReadOnlyDictionary<string, object?> row in table.Rows)
        {
            if (row.TryGetValue("id", out object? idVal) && idVal is string id)
            {
                result[id] = row;
            }
        }

        return result;
    }

    public static IReadOnlyDictionary<string, object?>? ResolveCameraParam(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> cameraParams, IReadOnlyDictionary<string, object?> cameraCubeFields)
    {
        if (!cameraCubeFields.TryGetValue("Obj_arg0", out object? argVal) || argVal is not int arg || arg < 0)
        {
            return null;
        }

        string id = $"c:{arg:D4}";
        return cameraParams.TryGetValue(id, out IReadOnlyDictionary<string, object?>? row) ? row : null;
    }

    public static int? DetectGame(string baseDir)
    {
        string filesDir = ProjectFiles.FilesRoot(baseDir);
        if (!Directory.Exists(Path.Combine(filesDir, "StageData")) || !Directory.Exists(Path.Combine(filesDir, "ObjectData")))
        {
            return null;
        }

        bool looksLikeSMG2 = Directory.Exists(Path.Combine(filesDir, "LightData")) || Directory.Exists(Path.Combine(filesDir, "LocalizeData"));
        return looksLikeSMG2 ? 2 : 1;
    }

    public static List<string> ListGalaxies(string gameRootDir)
    {
        string stageDataDir = ProjectFiles.GameFilePath(gameRootDir, "DATA/files/StageData");
        if (!Directory.Exists(stageDataDir))
        {
            return [];
        }

        var result = new List<string>();
        foreach (string dir in Directory.EnumerateDirectories(stageDataDir))
        {
            string name = Path.GetFileName(dir);
            if (File.Exists(Path.Combine(dir, name + "Scenario.arc")))
            {
                result.Add(name);
            }
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    public static List<string> ListAllStages(string gameRootDir)
    {
        string stageDataDir = ProjectFiles.GameFilePath(gameRootDir, "DATA/files/StageData");
        if (!Directory.Exists(stageDataDir))
        {
            return [];
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string dir in Directory.EnumerateDirectories(stageDataDir))
        {
            string name = Path.GetFileName(dir);
            if (File.Exists(ProjectFiles.GameFilePath(gameRootDir, MapArcRelativePath(gameRootDir, name))))
            {
                result.Add(name);
            }
        }

        foreach (string file in Directory.EnumerateFiles(stageDataDir, "*.arc"))
        {
            result.Add(Path.GetFileNameWithoutExtension(file));
        }

        var list = result.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static string GalaxyScenarioRelativePath(string galaxyName) =>
        Path.Combine("DATA", "files", "StageData", galaxyName, galaxyName + "Scenario.arc");

    public static int? TryGetGalaxyWorld(string gameRootDir, string? outputDir, string galaxyName)
    {
        string relativePath = GalaxyScenarioRelativePath(galaxyName);
        string arcPath = ProjectFiles.ResolveFile(gameRootDir, outputDir, relativePath);
        if (!File.Exists(arcPath))
        {
            return null;
        }

        RARCArchive scenarioArchive = RARCArchive.Load(arcPath);
        RARCFile? galaxyInfoFile = scenarioArchive.Root.FindFile("GalaxyInfo.bcsv");
        if (galaxyInfoFile is null)
        {
            return null;
        }

        BCSVTable galaxyInfo = BCSVTable.Load(galaxyInfoFile.Data);
        return galaxyInfo.Rows.Count > 0 && galaxyInfo.Rows[0].TryGetValue("WorldNo", out object? worldNo) && worldNo is int world
            ? world
            : null;
    }

    public static void SetGalaxyWorld(string gameRootDir, string outputDir, string galaxyName, int world)
    {
        string relativePath = GalaxyScenarioRelativePath(galaxyName);
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, relativePath);

        RARCFile? galaxyInfoFile = archive.Root.FindFile("GalaxyInfo.bcsv");
        if (galaxyInfoFile is null)
        {
            return;
        }

        BCSVTable galaxyInfo = BCSVTable.Load(galaxyInfoFile.Data);
        if (galaxyInfo.Rows.Count == 0)
        {
            return;
        }

        var updatedRow = new Dictionary<string, object?>(galaxyInfo.Rows[0]) { ["WorldNo"] = world };
        var updatedRows = galaxyInfo.Rows.ToList();
        updatedRows[0] = updatedRow;
        var updatedTable = new BCSVTable { Fields = galaxyInfo.Fields, Rows = updatedRows, EntrySize = galaxyInfo.EntrySize, DataOffset = galaxyInfo.DataOffset };

        archive.Root.ReplaceFileDataByName("GalaxyInfo.bcsv", updatedTable.Save());
        ProjectFiles.SaveArc(outputDir, relativePath, archive, wasCompressed);
    }

    public static void SaveScenarios(string gameRootDir, string outputDir, string galaxyName, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        string relativePath = GalaxyScenarioRelativePath(galaxyName);
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, relativePath);

        RARCFile? scenarioFile = archive.Root.FindFile("ScenarioData.bcsv");
        if (scenarioFile is null)
        {
            return;
        }

        BCSVTable scenarioData = BCSVTable.Load(scenarioFile.Data);
        var updatedTable = new BCSVTable { Fields = scenarioData.Fields, Rows = rows, EntrySize = scenarioData.EntrySize, DataOffset = scenarioData.DataOffset };

        archive.Root.ReplaceFileDataByName("ScenarioData.bcsv", updatedTable.Save());
        ProjectFiles.SaveArc(outputDir, relativePath, archive, wasCompressed);
    }

    public static CANMAnimation? TryLoadIntroCamera(string gameRootDir, string? outputDir, string galaxyName, int scenarioNo)
    {
        string relativePath = MapArcRelativePath(gameRootDir, galaxyName);
        string mapArcPath = ProjectFiles.ResolveFile(gameRootDir, outputDir, relativePath);
        if (!File.Exists(mapArcPath))
        {
            return null;
        }

        RARCArchive mapArchive = RARCArchive.Load(mapArcPath);
        RARCFile? canmFile = mapArchive.Root.FindFile($"camera/StartScenario{scenarioNo}.canm");
        if (canmFile is null)
        {
            return null;
        }

        try
        {
            return CANMReader.Load(canmFile.Data);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }


    public readonly record struct ScenarioInfo(int RowIndex, IReadOnlyDictionary<string, object?> Fields);

    public static IReadOnlyList<ScenarioInfo> ListScenarios(string gameRootDir, string? outputDir, string galaxyName)
    {
        (RARCArchive scenarioArchive, _) = ProjectFiles.LoadArc(gameRootDir, outputDir, GalaxyScenarioRelativePath(galaxyName));
        RARCFile scenarioFile = scenarioArchive.Root.FindFile("ScenarioData.bcsv")
            ?? throw new FileNotFoundException("ScenarioData.bcsv not found in the scenario archive.");
        BCSVTable scenarioData = BCSVTable.Load(scenarioFile.Data);

        var scenarios = new List<ScenarioInfo>(scenarioData.Rows.Count);
        for (int i = 0; i < scenarioData.Rows.Count; i++)
        {
            scenarios.Add(new ScenarioInfo(i, scenarioData.Rows[i]));
        }

        return scenarios;
    }

    public static readonly string[] PlacementListNames =
    [
        "ObjInfo", "AreaObjInfo", "PlanetObjInfo", "DemoObjInfo", "CameraCubeInfo", "StageObjInfo",
    ];

    public static Matrix4x4 ComposePlacementMatrix(Vector3 pos, Vector3 rotDeg, Vector3 scale) =>
        Matrix4x4.CreateScale(scale) *
        Matrix4x4.CreateRotationX(rotDeg.X * MathF.PI / 180f) *
        Matrix4x4.CreateRotationY(rotDeg.Y * MathF.PI / 180f) *
        Matrix4x4.CreateRotationZ(rotDeg.Z * MathF.PI / 180f) *
        Matrix4x4.CreateTranslation(pos);

    public static Vector3 EulerXyzFromMatrix(Matrix4x4 m)
    {
        const float rad2deg = 180f / MathF.PI;
        float pitchY = MathF.Asin(Math.Clamp(-m.M13, -1f, 1f));
        float rollX = MathF.Atan2(m.M23, m.M33);
        float yawZ = MathF.Atan2(m.M12, m.M11);
        return new Vector3(rollX, pitchY, yawZ) * rad2deg;
    }

    public static Vector3 CalcFrontVecFromRotation(Vector3 rotDeg)
    {
        Matrix4x4 rot = Matrix4x4.CreateRotationX(rotDeg.X * MathF.PI / 180f) *
            Matrix4x4.CreateRotationY(rotDeg.Y * MathF.PI / 180f) *
            Matrix4x4.CreateRotationZ(rotDeg.Z * MathF.PI / 180f);
        return Vector3.TransformNormal(Vector3.UnitZ, rot);
    }

    private static readonly BDLColor[] StarPieceColors =
    [
        new BDLColor(0x80, 0x00, 0x99, 0xFF),
        new BDLColor(0xE6, 0xA0, 0x00, 0xFF),
        new BDLColor(0x46, 0xA1, 0x08, 0xFF),
        new BDLColor(0x37, 0x5A, 0xA0, 0xFF),
        new BDLColor(0xBE, 0x33, 0x0B, 0xFF),
        new BDLColor(0x80, 0x80, 0x80, 0xFF),
    ];

    public static BDLColor ResolveStarPieceColor(int arg3, int seed)
    {
        if (arg3 >= 0 && arg3 < StarPieceColors.Length)
        {
            return StarPieceColors[arg3];
        }

        return StarPieceColors[new Random(seed).Next(StarPieceColors.Length)];
    }

    public static LoadedObject? TryLoadObject(string name, string objectDataDir, int colorChangeFrame = -1, int texChangeFrame = -1)
    {
        string arcPath = Path.Combine(objectDataDir, name + ".arc");
        if (!File.Exists(arcPath))
        {
            return null;
        }

        RARCArchive archive = RARCArchive.Load(arcPath);
        RARCFile? bdlFile = archive.Root.FindFileByName(name + ".bdl");
        if (bdlFile is null)
        {
            Console.WriteLine($"  {name}: archive has no {name}.bdl, skipping.");
            return null;
        }

        try
        {
            BDLModel model = BDLModel.Load(bdlFile.Data);

            if (colorChangeFrame >= 0 && archive.Root.FindFileByName("ColorChange.brk") is { } colorChangeFile)
            {
                try
                {
                    BRKAnimation brk = BRKReader.Load(colorChangeFile.Data);
                    model = model.WithMaterials(model.Materials.Select(m => brk.ApplyToMaterial(m, colorChangeFrame)).ToList());
                }
                catch (NotSupportedException ex)
                {
                    Console.WriteLine($"  {name}: unsupported ColorChange.brk feature ({ex.Message}), skipping.");
                }
            }

            RARCFile? texChangeFile = texChangeFrame >= 0 ? archive.Root.FindFileByName("TexChange.btp") : null;
            RARCFile? btpFile = texChangeFile;
            if (btpFile is null)
            {
                List<RARCFile> btpFiles = archive.Root.FindFilesByExtension(".btp").ToList();
                btpFile = btpFiles.Find(f => f.Name.Contains("Blink", StringComparison.OrdinalIgnoreCase)) ?? btpFiles.FirstOrDefault();
            }

            int btpFrame = texChangeFile is not null ? texChangeFrame : 0;
            Dictionary<string, ushort>? textureOverrides = null;
            if (btpFile is not null)
            {
                List<BTPTextureOverride> overrides = BTPReader.Read(btpFile.Data);
                textureOverrides = overrides.ToDictionary(o => o.MaterialName, o => o.TextureIndexAtFrame(btpFrame));
            }

            List<GpuMesh> meshes = BDLMeshBuilder.Build(model, textureOverrides);
            (Vector3 min, Vector3 max) = ComputeLocalBounds(meshes);

            BCKAnimation? waitAnim = null;
            RARCFile? waitFile = archive.Root.FindFileByName("Wait.bck");
            if (waitFile is not null)
            {
                try
                {
                    waitAnim = BCKReader.Load(waitFile.Data, model.Joints.Count);
                }
                catch (NotSupportedException ex)
                {
                    Console.WriteLine($"  {name}: unsupported BCK feature ({ex.Message}), skipping Wait animation.");
                }
            }

            return new LoadedObject { Name = name, Model = model, Meshes = meshes, LocalBoundsMin = min, LocalBoundsMax = max, WaitAnimation = waitAnim };
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine($"  {name}: unsupported BDL feature ({ex.Message}), skipping.");
            return null;
        }
    }

    public static BRKAnimation? TryLoadBrk(string name, string objectDataDir)
    {
        string arcPath = Path.Combine(objectDataDir, name + ".arc");
        if (!File.Exists(arcPath))
        {
            return null;
        }

        RARCArchive archive = RARCArchive.Load(arcPath);
        RARCFile? brkFile = archive.Root.FindFileByName(name + ".brk");
        if (brkFile is null)
        {
            return null;
        }

        try
        {
            return BRKReader.Load(brkFile.Data);
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine($"  {name}: unsupported BRK feature ({ex.Message}), skipping.");
            return null;
        }
    }

    public static BTKAnimation? TryLoadBtk(string name, string objectDataDir, string? btkFileNameOverride = null)
    {
        string arcPath = Path.Combine(objectDataDir, name + ".arc");
        if (!File.Exists(arcPath))
        {
            return null;
        }

        RARCArchive archive = RARCArchive.Load(arcPath);
        RARCFile? btkFile = archive.Root.FindFileByName((btkFileNameOverride ?? name) + ".btk");
        if (btkFile is null)
        {
            return null;
        }

        try
        {
            return BTKReader.Load(btkFile.Data);
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine($"  {name}: unsupported BTK feature ({ex.Message}), skipping.");
            return null;
        }
    }

    private static readonly Dictionary<string, (Vector3 Pt1, Vector3 Pt2, bool Vertical)> BTIBillboardGeometry = new(StringComparer.Ordinal)
    {
        ["Flag"] = (new Vector3(0f, 150f, 0f), new Vector3(0f, -150f, 600f), true),
        ["FlagRaceA"] = (new Vector3(0f, 75f, 0f), new Vector3(0f, -75f, 300f), true),
        ["FlagSurfing"] = (new Vector3(0f, 150f, 0f), new Vector3(0f, -150f, 600f), true),
        ["FlagTamakoro"] = (new Vector3(0f, 150f, 0f), new Vector3(0f, -150f, 600f), true),
        ["FlagPeachCastleA"] = (new Vector3(0f, 150f, 0f), new Vector3(0f, -150f, 600f), true),
        ["FlagPeachCastleB"] = (new Vector3(0f, 150f, 0f), new Vector3(0f, -150f, 600f), true),
        ["FlagPeachCastleC"] = (new Vector3(0f, 150f, 0f), new Vector3(0f, -150f, 600f), true),
        ["FlagKoopaA"] = (new Vector3(0f, 150f, 0f), new Vector3(0f, -150f, 600f), true),
        ["FlagKoopaB"] = (new Vector3(0f, 75f, 0f), new Vector3(0f, -75f, 600f), true),
        ["FlagKoopaCastle"] = (new Vector3(0f, 150f, 0f), new Vector3(0f, -150f, 600f), true),
    };

    public static LoadedObject? TryLoadBtiBillboard(string name, string objectDataDir)
    {
        if (!BTIBillboardGeometry.TryGetValue(name, out (Vector3 Pt1, Vector3 Pt2, bool Vertical) geo))
        {
            return null;
        }

        string arcPath = Path.Combine(objectDataDir, name + ".arc");
        if (!File.Exists(arcPath))
        {
            return null;
        }

        RARCArchive archive = RARCArchive.Load(arcPath);
        RARCFile? btiFile = archive.Root.FindFileByName(name + ".bti");
        if (btiFile is null)
        {
            return null;
        }

        BTITexture tex;
        try
        {
            tex = BTIReader.Load(btiFile.Data);
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine($"  {name}: unsupported BTI feature ({ex.Message}), skipping.");
            return null;
        }

        var bdlTexture = new BDLTexture { Name = name, Format = tex.Format, Width = tex.Width, Height = tex.Height, WrapS = tex.WrapS, WrapT = tex.WrapT, Rgba = tex.Rgba };

        var justTexColorStage = new BDLTevStage(
            ColorInA: 15, ColorInB: 15, ColorInC: 15, ColorInD: 8,
            ColorOp: 0, ColorBias: 0, ColorScale: 0, ColorClamp: true, ColorOutReg: 0,
            AlphaInA: 7, AlphaInB: 7, AlphaInC: 7, AlphaInD: 4,
            AlphaOp: 0, AlphaBias: 0, AlphaScale: 0, AlphaClamp: true, AlphaOutReg: 0,
            KonstColorSel: 0, KonstAlphaSel: 0);

        var white = new BDLColor(255, 255, 255, 255);
        var material = new BDLMaterial
        {
            Name = name,
            CullMode = BDLCullMode.None,
            MaterialColor = white,
            AmbientColor = new BDLColor(0, 0, 0, 0),
            TevStageCount = 1,
            TextureIndices = new ushort?[] { 0, null, null, null, null, null, null, null },
            TexCoordGens = [new BDLTexCoordGen(0, 0, 0)],
            TexMatrices = new BDLTexMatrix?[10],
            TevOrders = [new BDLTevOrder(TexCoordIndex: 0, TexMapIndex: 0, ColorChannel: 0xFF)],
            TevStages = [justTexColorStage],
            IndTexOrders = [],
            IndTexMatrices = [],
            IndTexCoordScales = [],
            IndTevStages = [],
            TevRegisters = [new BDLTevRegisterColor(255, 255, 255, 255), new(255, 255, 255, 255), new(255, 255, 255, 255), new(255, 255, 255, 255)],
            TevKonstColors = [white, white, white, white],
            ColorChannel0 = new BDLColorChan(false, BDLColorSource.Register, 0, BDLDiffuseFn.None, 0, BDLColorSource.Register),
            AlphaCompare = new BDLAlphaCompare(BDLCompare.Always, 0, 0, BDLCompare.Always, 0),
            BlendMode = new BDLBlendMode(BDLBlendType.None, BDLBlendFactor.One, BDLBlendFactor.Zero),
            ZMode = new BDLZMode(true, BDLCompare.LessEqual, true),
        };

        GpuMesh mesh = BuildBillboardQuadMesh(geo.Pt1, geo.Pt2, geo.Vertical);
        var model = new BDLModel
        {
            FormatTag = "J3D2",
            Chunks = [],
            HierarchyNodes = [],
            Joints = [],
            Envelopes = [],
            InverseBindMatrices = [],
            DrawMatrices = [],
            Shapes = [],
            Materials = [material],
            Textures = [bdlTexture],
        };

        (Vector3 min, Vector3 max) = ComputeLocalBounds([mesh]);
        return new LoadedObject { Name = name, Model = model, Meshes = [mesh], LocalBoundsMin = min, LocalBoundsMax = max, WaitAnimation = null };
    }

    public static (BTITexture Water, BTITexture Indirect)? TryLoadWaterWaveTextures(string objectDataDir)
    {
        string arcPath = Path.Combine(objectDataDir, "WaterWave.arc");
        if (!File.Exists(arcPath))
        {
            return null;
        }

        RARCArchive archive = RARCArchive.Load(arcPath);
        RARCFile? waterFile = archive.Root.FindFileByName("Water.bti");
        RARCFile? indirectFile = archive.Root.FindFileByName("WaterIndirect.bti");
        if (waterFile is null || indirectFile is null)
        {
            return null;
        }

        try
        {
            return (BTIReader.Load(waterFile.Data), BTIReader.Load(indirectFile.Data));
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine($"  WaterWave: unsupported BTI feature ({ex.Message}), skipping.");
            return null;
        }
    }

    private static GpuMesh BuildBillboardQuadMesh(Vector3 pt1, Vector3 pt2, bool vertical)
    {
        Vector3 v0, v1, v2, v3;
        if (vertical)
        {
            v0 = new Vector3(pt1.X, pt1.Y, pt1.Z);
            v1 = new Vector3(pt2.X, pt1.Y, pt2.Z);
            v2 = new Vector3(pt1.X, pt2.Y, pt1.Z);
            v3 = new Vector3(pt2.X, pt2.Y, pt2.Z);
        }
        else
        {
            v0 = new Vector3(pt1.X, pt1.Y, pt1.Z);
            v1 = new Vector3(pt1.X, pt1.Y, pt2.Z);
            v2 = new Vector3(pt2.X, pt2.Y, pt1.Z);
            v3 = new Vector3(pt2.X, pt2.Y, pt2.Z);
        }

        Vector3 normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
        if (float.IsNaN(normal.X))
        {
            normal = Vector3.UnitY;
        }

        (Vector3 Pos, Vector2 Uv)[] verts =
        [
            (v0, new Vector2(0f, 0f)), (v1, new Vector2(1f, 0f)), (v2, new Vector2(0f, 1f)),
            (v2, new Vector2(0f, 1f)), (v1, new Vector2(1f, 0f)), (v3, new Vector2(1f, 1f)),
        ];

        float[] vertices = new float[verts.Length * 18];
        var jointIndices = new int[verts.Length][];
        var jointWeights = new float[verts.Length][];
        var isWeighted = new bool[verts.Length];
        var localPositions = new Vector3[verts.Length];
        var localNormals = new Vector3[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            (Vector3 pos, Vector2 uv) = verts[i];
            int b = i * 18;
            vertices[b + 0] = pos.X;
            vertices[b + 1] = pos.Y;
            vertices[b + 2] = pos.Z;
            vertices[b + 3] = normal.X;
            vertices[b + 4] = normal.Y;
            vertices[b + 5] = normal.Z;
            vertices[b + 6] = uv.X;
            vertices[b + 7] = uv.Y;
            vertices[b + 8] = uv.X;
            vertices[b + 9] = uv.Y;
            vertices[b + 10] = 1f;
            vertices[b + 11] = 1f;
            vertices[b + 12] = 1f;
            vertices[b + 13] = 1f;

            jointIndices[i] = [0];
            jointWeights[i] = [1f];
            isWeighted[i] = false;
            localPositions[i] = pos;
            localNormals[i] = normal;
        }

        return new GpuMesh
        {
            MaterialIndex = 0,
            Texture0Index = 0,
            Texture0Slot = 0,
            Vertices = vertices,
            VertexCount = verts.Length,
            VertexJointIndices = jointIndices,
            VertexJointWeights = jointWeights,
            VertexIsWeighted = isWeighted,
            LocalPositions = localPositions,
            LocalNormals = localNormals,
        };
    }

    public sealed record MultiPartInfo(string ModelName, Vector3 Position, Vector3 Rotation, Vector3 Scale)
    {
        public MultiPartInfo(string modelName) : this(modelName, Vector3.Zero, Vector3.Zero, Vector3.One)
        {
        }
    }

    public static readonly Dictionary<string, MultiPartInfo[]> MultiPartModels = new(StringComparer.Ordinal)
    {
        ["BossBegoman"] = [new("BossBegoman"), new("BossBegomanHead")],
        ["BossJugem"] = [new("BossJugem"), new("BossJugemCloud")],
        ["DinoPackun"] = [new("DinoPackun"), new("DinoPackunTailBall", new Vector3(0f, 150f, -750f), new Vector3(0f, 90f, 0f), Vector3.One)],
        ["DinoPackunVs1"] = [new("DinoPackun"), new("DinoPackunTailBall", new Vector3(0f, 150f, -750f), new Vector3(0f, 90f, 0f), Vector3.One)],
        ["DinoPackunVs2"] = [new("DinoPackun2"), new("DinoPackunTailBall", new Vector3(0f, 150f, -750f), new Vector3(0f, 90f, 0f), Vector3.One)],
        ["KoopaJrCastle"] = [new("KoopaJrCastleBody"), new("KoopaJrCastleHead", new Vector3(0f, 2750f, 0f), Vector3.Zero, Vector3.One), new("KoopaJrCastleCapsule", new Vector3(0f, 3475f, 0f), Vector3.Zero, Vector3.One)],
        ["KoopaJrCastleWindUp"] = [new("Fan"), new("FanWind")],
        ["KoopaJrRobot"] = [new("KoopaJrRobot"), new("KoopaJrRobotPod", new Vector3(0f, 1000f, 0f), Vector3.Zero, Vector3.One)],
        ["OtaRockTank"] = [new("OtaRockTank"), new("OtaRockChief", new Vector3(0f, 500f, 0f), Vector3.Zero, Vector3.One)],
        ["SkeletalFishBoss"] = [new("SkeletalFishBoss"), new("SkeletalFishBossHeadA")],
        ["TombSpider"] = [new("TombSpider"), new("TombSpiderPlanet")],

        ["CocoSambo"] = [new("CocoSamboBody"), new("CocoSamboHead", new Vector3(0f, 325f, 0f), Vector3.Zero, Vector3.One)],
        ["BegomanSpike"] = [new("BegomanSpike"), new("BegomanSpikeHead")],
        ["BegomanSpring"] = [new("BegomanSpring"), new("BegomanSpringHead")],
        ["BegomanSpringHide"] = [new("BegomanSpring"), new("BegomanSpringHead")],
        ["ElectricBazooka"] = [new("ElectricBazooka"), new("WaterBazookaCapsule", new Vector3(0f, 495f, 0f), Vector3.Zero, Vector3.One), new("MogucchiShooter", new Vector3(0f, 335f, 0f), Vector3.Zero, Vector3.One)],
        ["GliderBazooka"] = [new("MogucchiSpike"), new("GliderBazooka")],
        ["GliderShooter"] = [new("MogucchiSpike"), new("GliderBazooka")],
        ["KillerShooter"] = [new("MogucchiSpike"), new("GliderBazooka")],
        ["Grapyon"] = [new("GrapyonBody"), new("GrapyonHead", new Vector3(0f, 80f, 0f), Vector3.Zero, Vector3.One)],
        ["HammerHeadPackun"] = [new("PackunFlower"), new("PackunLeaf")],
        ["HammerHeadPackunSpike"] = [new("PackunFlowerSpike"), new("PackunLeafSpike")],
        ["Jugem"] = [new("Jugem"), new("JugemCloud")],
        ["JumpBeamer"] = [new("JumpBeamerBody"), new("JumpBeamerHead")],
        ["JumpGuarder"] = [new("JumpGuarder"), new("JumpGuarderHead", new Vector3(0f, 65f, 0f), Vector3.Zero, new Vector3(0.8f, 0.8f, 0.8f))],
        ["Kiraira"] = [new("Kiraira", new Vector3(0f, 50f, 0f), Vector3.Zero, Vector3.One), new("KirairaChain", new Vector3(0f, -110f, 0f), Vector3.Zero, Vector3.One), new("KirairaFixPointBottom", new Vector3(0f, -125f, 0f), Vector3.Zero, Vector3.One)],
        ["Mogu"] = [new("Mogu"), new("MoguHole")],
        ["Nyoropon"] = [new("NyoroponBody"), new("NyoroponHead", new Vector3(0f, 500f, 0f), new Vector3(90f, 0f, 0f), Vector3.One)],
        ["Patakuri"] = [new("Kuribo"), new("PatakuriWing", new Vector3(0f, 15f, -25f), Vector3.Zero, Vector3.One)],
        ["PatakuriBig"] = [new("KuriboChief"), new("PatakuriWingBig", new Vector3(0f, 750f, 200f), new Vector3(0f, 90f, 0f), Vector3.One)],
        ["Torpedo"] = [new("Torpedo"), new("TorpedoPropeller")],
        ["WaterBazooka"] = [new("WaterBazooka"), new("WaterBazookaCapsule", new Vector3(0f, 495f, 0f), Vector3.Zero, Vector3.One), new("MogucchiShooter", new Vector3(0f, 335f, 0f), Vector3.Zero, Vector3.One)],

        ["GoroRockCoverCage"] = [new("GoroRockCoverCage"), new("GoroRockCoverCageFrame")],
        ["RedBlueTurnBlock"] = [new("RedBlueTurnBlock"), new("RedBlueTurnBlockBase")],
        ["StrayTico"] = [new("StrayTico"), new("ItemBubble")],
        ["YoshiEgg"] = [new("YoshiEgg"), new("YoshiNest")],
        ["YoshiFruit"] = [new("YoshiFruit", new Vector3(0f, 65f, 0f), Vector3.Zero, Vector3.One), new("YoshiFruitStem")],
        ["YoshiFruitBig"] = [new("YoshiFruitBig", new Vector3(0f, 115f, 0f), Vector3.Zero, Vector3.One), new("YoshiFruitStemBig")],
    };

    private static readonly Dictionary<string, string> CircleCoinGroupModel = new(StringComparer.Ordinal)
    {
        ["CircleCoinGroup"] = "Coin",
        ["CirclePurpleCoinGroup"] = "PurpleCoin",
    };

    public static List<MultiPartInfo>? TryBuildCircleCoinGroupParts(string placementName, IReadOnlyDictionary<string, object?> fields)
    {
        if (!CircleCoinGroupModel.TryGetValue(placementName, out string? modelName))
        {
            return null;
        }

        int count = fields.TryGetValue("Obj_arg0", out object? countVal) && countVal is int countInt && countInt != -1 ? countInt : 0;
        float radius = fields.TryGetValue("Obj_arg2", out object? radiusVal) && radiusVal is int radiusInt && radiusInt != -1 ? radiusInt : 200f;

        var parts = new List<MultiPartInfo>(Math.Max(count, 0));
        if (count <= 0)
        {
            return parts;
        }

        float interval = MathF.Tau / count;
        float angle = 0f;
        for (int i = 0; i < count; i++)
        {
            var position = new Vector3(radius * MathF.Sin(angle), 0f, radius * MathF.Cos(angle));
            parts.Add(new MultiPartInfo(modelName, position, Vector3.Zero, Vector3.One));
            angle += interval;
        }

        return parts;
    }

    public static Matrix4x4 ComposeMultiPartOffset(MultiPartInfo part)
    {
        const float deg2rad = MathF.PI / 180f;
        return Matrix4x4.CreateScale(part.Scale) *
            Matrix4x4.CreateRotationX(part.Rotation.Z * deg2rad) *
            Matrix4x4.CreateRotationY(part.Rotation.Y * deg2rad) *
            Matrix4x4.CreateRotationZ(part.Rotation.X * deg2rad) *
            Matrix4x4.CreateTranslation(part.Position);
    }

    private static readonly (float Ax, float Az, float Bx, float Bz)[] ElectricRailPlaneCorners =
    [
        (30f, 30f, -30f, -30f),
        (-30f, 30f, 30f, -30f),
    ];

    public static GpuMesh? BuildElectricRailRibbonMesh(RailCoordSampleTable table, int stackHeight, GpuMesh materialTemplate)
    {
        IReadOnlyList<Vector3> samplePositions = table.Positions;
        if (samplePositions.Count < 2 || table.TotalLength <= 0f)
        {
            return null;
        }

        float UvXAt(int i) => 0.25f * MathF.Min(100f * i, table.TotalLength) / 100f;

        return BuildRibbonStripMesh(samplePositions, UvXAt, stackHeight, materialTemplate);
    }

    public static GpuMesh? BuildElectricRailStaticRibbonMesh(RailCoordSampleTable table, int stackHeight, GpuMesh materialTemplate)
    {
        if (table.TotalLength <= 0f)
        {
            return null;
        }

        int separatorCount = (int)(table.TotalLength / 200f) + 1;
        if (separatorCount < 2)
        {
            return null;
        }

        var positions = new Vector3[separatorCount];
        for (int i = 0; i < separatorCount; i++)
        {
            float coord = i == separatorCount - 1 ? table.TotalLength : 200f * i;
            positions[i] = table.PositionAtCoord(coord);
        }

        return BuildRibbonStripMesh(positions, i => 0.5f * i, stackHeight, materialTemplate);
    }

    private static GpuMesh? BuildRibbonStripMesh(IReadOnlyList<Vector3> samplePositions, Func<int, float> uvXAt, int stackHeight, GpuMesh materialTemplate)
    {
        var directions = new Vector3[samplePositions.Count];
        for (int i = 0; i < samplePositions.Count; i++)
        {
            int next = Math.Min(i + 1, samplePositions.Count - 1);
            Vector3 delta = samplePositions[next] - samplePositions[i];
            directions[i] = delta.LengthSquared() > 1e-6f ? Vector3.Normalize(delta) : (i > 0 ? directions[i - 1] : Vector3.UnitZ);
        }

        var stripPositions = new List<Vector3>();
        var stripUvs = new List<Vector2>();

        int layerCount = Math.Max(stackHeight, 1);
        for (int layer = 0; layer < layerCount; layer++)
        {
            Vector3 layerOffset = Vector3.UnitY * (100f * layer);

            foreach ((float ax, float az, float bx, float bz) in ElectricRailPlaneCorners)
            {
                var strip = new List<(Vector3 Pos, Vector2 Uv)>(samplePositions.Count * 2);
                for (int i = 0; i < samplePositions.Count; i++)
                {
                    (Vector3 axis1, Vector3 axis2) = MakeRailCrossSectionAxes(directions[i]);
                    Vector3 basePos = samplePositions[i] + layerOffset;
                    float u = uvXAt(i);

                    strip.Add((basePos + axis1 * ax + axis2 * az, new Vector2(u, 0f)));
                    strip.Add((basePos + axis1 * bx + axis2 * bz, new Vector2(u, 1f)));
                }

                for (int i = 0; i + 2 < strip.Count; i++)
                {
                    (Vector3 p0, Vector2 uv0) = strip[i];
                    (Vector3 p1, Vector2 uv1) = strip[i + 1];
                    (Vector3 p2, Vector2 uv2) = strip[i + 2];

                    if (i % 2 == 0)
                    {
                        stripPositions.Add(p0); stripUvs.Add(uv0);
                        stripPositions.Add(p1); stripUvs.Add(uv1);
                        stripPositions.Add(p2); stripUvs.Add(uv2);
                    }
                    else
                    {
                        stripPositions.Add(p1); stripUvs.Add(uv1);
                        stripPositions.Add(p0); stripUvs.Add(uv0);
                        stripPositions.Add(p2); stripUvs.Add(uv2);
                    }
                }
            }
        }

        if (stripPositions.Count == 0)
        {
            return null;
        }

        int vertexCount = stripPositions.Count;
        float[] vertices = new float[vertexCount * 18];
        var jointIndices = new int[vertexCount][];
        var jointWeights = new float[vertexCount][];
        var isWeighted = new bool[vertexCount];
        var localPositions = new Vector3[vertexCount];
        var localNormals = new Vector3[vertexCount];

        for (int tri = 0; tri < vertexCount; tri += 3)
        {
            Vector3 normal = Vector3.Normalize(Vector3.Cross(stripPositions[tri + 1] - stripPositions[tri], stripPositions[tri + 2] - stripPositions[tri]));
            if (float.IsNaN(normal.X))
            {
                normal = Vector3.UnitY;
            }

            for (int k = 0; k < 3; k++)
            {
                int v = tri + k;
                Vector3 pos = stripPositions[v];
                Vector2 uv = stripUvs[v];
                int b = v * 18;
                vertices[b + 0] = pos.X;
                vertices[b + 1] = pos.Y;
                vertices[b + 2] = pos.Z;
                vertices[b + 3] = normal.X;
                vertices[b + 4] = normal.Y;
                vertices[b + 5] = normal.Z;
                vertices[b + 6] = uv.X;
                vertices[b + 7] = uv.Y;
                vertices[b + 8] = uv.X;
                vertices[b + 9] = uv.Y;
                vertices[b + 10] = 1f;
                vertices[b + 11] = 1f;
                vertices[b + 12] = 1f;
                vertices[b + 13] = 1f;

                jointIndices[v] = [0];
                jointWeights[v] = [1f];
                isWeighted[v] = false;
                localPositions[v] = pos;
                localNormals[v] = normal;
            }
        }

        return new GpuMesh
        {
            MaterialIndex = materialTemplate.MaterialIndex,
            Texture0Index = materialTemplate.Texture0Index,
            Texture1Index = materialTemplate.Texture1Index,
            Texture2Index = materialTemplate.Texture2Index,
            Texture3Index = materialTemplate.Texture3Index,
            Texture0Slot = materialTemplate.Texture0Slot,
            Texture1Slot = materialTemplate.Texture1Slot,
            Texture2Slot = materialTemplate.Texture2Slot,
            Texture3Slot = materialTemplate.Texture3Slot,
            IndirectTextureIndex = materialTemplate.IndirectTextureIndex,
            Uv0EnvMapMatrix = materialTemplate.Uv0EnvMapMatrix,
            Uv1EnvMapMatrix = materialTemplate.Uv1EnvMapMatrix,
            Uv2EnvMapMatrix = materialTemplate.Uv2EnvMapMatrix,
            Uv3EnvMapMatrix = materialTemplate.Uv3EnvMapMatrix,
            Vertices = vertices,
            VertexCount = vertexCount,
            VertexJointIndices = jointIndices,
            VertexJointWeights = jointWeights,
            VertexIsWeighted = isWeighted,
            LocalPositions = localPositions,
            LocalNormals = localNormals,
        };
    }

    public static float[] ScrollRibbonUv(GpuMesh mesh, (float Scale, float Offset)? uv1Scroll, (float A, float B, float C, float D, float Tx, float Ty)? uv0Transform)
    {
        float[] dst = mesh.RebakeScratch ??= new float[mesh.Vertices.Length];
        Array.Copy(mesh.Vertices, dst, mesh.Vertices.Length);

        for (int v = 0; v < mesh.VertexCount; v++)
        {
            int b = v * 18;
            float bakedU = mesh.Vertices[b + 6];
            float bakedV = mesh.Vertices[b + 7];

            if (uv1Scroll is { } s)
            {
                dst[b + 8] = (bakedU * s.Scale) + s.Offset;
                dst[b + 9] = bakedV;
            }

            if (uv0Transform is { } t)
            {
                dst[b + 6] = (t.A * bakedU) + (t.B * bakedV) + t.Tx;
                dst[b + 7] = (t.C * bakedU) + (t.D * bakedV) + t.Ty;
            }
        }

        return dst;
    }

    private static (Vector3 Axis1, Vector3 Axis2) MakeRailCrossSectionAxes(Vector3 front)
    {
        Vector3 axis1 = Vector3.Cross(Vector3.UnitY, front);
        if (axis1.LengthSquared() < 1e-6f)
        {
            axis1 = Vector3.Cross(Vector3.UnitX, front);
            if (axis1.LengthSquared() < 1e-6f)
            {
                axis1 = Vector3.Cross(Vector3.UnitZ, front);
            }
        }

        axis1 = Vector3.Normalize(axis1);
        Vector3 axis2 = Vector3.Normalize(Vector3.Cross(front, axis1));
        return (axis1, axis2);
    }

    public static List<Vector3> BuildElectricRailPointPositions(IReadOnlyList<PathPoint> worldPoints, int stackHeight)
    {
        var positions = new List<Vector3>();
        int layerCount = Math.Max(stackHeight, 1);

        foreach (PathPoint point in worldPoints)
        {
            if (point.Fields.TryGetValue("point_arg0", out object? v) && v is int i && i != -1)
            {
                continue;
            }

            for (int layer = 0; layer < layerCount; layer++)
            {
                positions.Add(point.Position + (Vector3.UnitY * (100f * layer)));
            }
        }

        return positions;
    }

    public enum NoteFairyPlacementMode
    {
        PerRailPoint,

        ExplicitCount,

        DistanceSpacing,
    }

    public static List<(Vector3 Position, Vector3 Forward)> BuildNoteFairyNotes(
        RailCoordSampleTable table, IReadOnlyList<PathPoint> worldPoints, NoteFairyPlacementMode mode, int explicitCount)
    {
        var result = new List<(Vector3 Position, Vector3 Forward)>();

        if (mode == NoteFairyPlacementMode.PerRailPoint)
        {
            for (int i = 0; i < worldPoints.Count; i++)
            {
                PathPoint p = worldPoints[i];
                Vector3 forward = p.ControlPointOut - p.Position;
                if (forward.LengthSquared() < 1e-6f && worldPoints.Count > 1)
                {
                    forward = worldPoints[(i + 1) % worldPoints.Count].Position - p.Position;
                }

                result.Add((p.Position, forward.LengthSquared() > 1e-6f ? forward : Vector3.UnitZ));
            }

            return result;
        }

        if (table.TotalLength <= 0f)
        {
            return result;
        }

        bool useExplicitCount = mode == NoteFairyPlacementMode.ExplicitCount && explicitCount > 0;
        float spacing = useExplicitCount ? table.TotalLength / explicitCount : 150f;
        int count = useExplicitCount ? explicitCount : Math.Max((int)(table.TotalLength / spacing), 1);

        float coord = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = table.PositionAtCoord(coord);
            Vector3 ahead = table.PositionAtCoord(MathF.Min(coord + 10f, table.TotalLength));
            Vector3 forward = ahead - pos;
            result.Add((pos, forward.LengthSquared() > 1e-6f ? forward : Vector3.UnitZ));
            coord += spacing;
        }

        return result;
    }

    public static (Vector3 Min, Vector3 Max) ComputeLocalBounds(List<GpuMesh> meshes)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        foreach (GpuMesh mesh in meshes)
        {
            for (int i = 0; i < mesh.Vertices.Length; i += 18)
            {
                var p = new Vector3(mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        if (min.X > max.X)
        {
            min = -Vector3.One;
            max = Vector3.One;
        }

        return (min, max);
    }

    public static (Vector3 Min, Vector3 Max) ComputeSceneBounds(IEnumerable<ObjectInstance> instances, bool includeSky = false)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        foreach (ObjectInstance instance in instances)
        {
            if (!includeSky && instance.Object.Name.Contains("Sky", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Vector3 lmin = instance.Object.LocalBoundsMin;
            Vector3 lmax = instance.Object.LocalBoundsMax;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? lmin.X : lmax.X,
                    (i & 2) == 0 ? lmin.Y : lmax.Y,
                    (i & 4) == 0 ? lmin.Z : lmax.Z);
                Vector3 world = Vector3.Transform(corner, instance.WorldMatrix);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }
        }

        if (min.X > max.X)
        {
            min = -Vector3.One;
            max = Vector3.One;
        }

        return (min, max);
    }
}
