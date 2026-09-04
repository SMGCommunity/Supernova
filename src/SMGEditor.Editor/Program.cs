using System.Numerics;
using ImGuiNET;
using SMGEditor.Core;
using SMGEditor.Core.Database;
using SMGEditor.Core.Formats;
using SMGEditor.Core.Simulation;
using SMGEditor.Core.Stage;
using SMGEditor.Editor;
using SMGEditor.Editor.CameraSims;
using SMGEditor.Viewer;
using Silk.NET.Core;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

string crashLogPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled exception (terminating={e.IsTerminating}):{Environment.NewLine}{e.ExceptionObject}{Environment.NewLine}{Environment.NewLine}";
    try
    {
        File.AppendAllText(crashLogPath, text);
    }
    catch
    {
    }

    Console.Error.WriteLine(text);

    if (OperatingSystem.IsWindows())
    {
        NativeCrashDialog.Show(text);
    }
};

/* found the number of consecutive render failures...can happen on Intel GPUs apparently? */
int consecutiveGlfwRenderFailures = 0;

void RenderWindowFrame(IWindow w)
{
    GlfwException? lastError = null;

    for (int attempt = 0; attempt < 4; attempt++)
    {
        try
        {
            w.DoRender();
            consecutiveGlfwRenderFailures = 0;
            return;
        }
        catch (GlfwException ex)
        {
            lastError = ex;
            try
            {
                w.GLContext?.MakeCurrent();
            }
            catch
            {
            }

            Thread.Yield();
        }
    }

    consecutiveGlfwRenderFailures++;
    string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GLFW render error, dropped frame ({consecutiveGlfwRenderFailures} in a row): {lastError?.Message}{Environment.NewLine}";
    try
    {
        File.AppendAllText(crashLogPath, entry);
    }
    catch
    {
    }

    Console.Error.WriteLine(entry);

    if (consecutiveGlfwRenderFailures >= 300)
    {
        // this can happen on laptop GPUs. this fixes the issue.
        throw new InvalidOperationException(
            $"OpenGL rendering failed for {consecutiveGlfwRenderFailures} consecutive frames. On a laptop with switchable graphics, force SMGEditor onto one GPU in Windows Settings > System > Display > Graphics.",
            lastError);
    }
}

string dbCachePath = Path.Combine(AppContext.BaseDirectory, "cache", "objectdb.json");

Console.WriteLine("Loading object database (this may download ~2MB on first run)...");
ObjectDatabase db = ObjectDatabase.LoadOrDownloadAsync(dbCachePath).GetAwaiter().GetResult();
Console.WriteLine($"Object database: {db.ObjectsByInternalName.Count} objects, {db.ClassesByInternalName.Count} classes, {db.Categories.Count} categories.");

string? gameRootDir = null;
int game = 0;
string? outputDir = null;
List<string> availableGalaxies = [];
List<string> availableStages = [];

Dictionary<string, int?> galaxyWorlds = new(StringComparer.OrdinalIgnoreCase);

void PopulateAvailableGalaxies(string rootDir)
{
    availableGalaxies = GalaxyLoader.ListGalaxies(rootDir);
    availableStages = GalaxyLoader.ListAllStages(rootDir);
    galaxyWorlds.Clear();
    foreach (string galaxyName in availableGalaxies)
    {
        galaxyWorlds[galaxyName] = GalaxyLoader.TryGetGalaxyWorld(rootDir, outputDir, galaxyName);
    }
}

string? activeProjectId = null;
string? activeProjectName = null;
string? activeProjectIconKey = null;

HubScreen hubScreen = HubScreen.GameDirsSetup;
ProjectPickerMode pickerMode = ProjectPickerMode.List;
string? editingProjectId = null;
string formName = "";
int formGame = 1;
string formOutputDir = "";
string? formIconKey = null;
string? formError = null;
string formSMG1Dir = "";
string formSMG2Dir = "";
string formSMG2Language = SMG2Languages.Default;
string? gameDirsError = null;

var fileBrowser = new FileBrowser();
BrowseTarget pendingBrowse = BrowseTarget.None;

string? editingGalaxyName = null;
string editGalaxyNameField = "";
int editGalaxyWorldField = 1;
string? editGalaxyError = null;

string starNameField = "";
string? starNameFieldGalaxy = null;
int? starNameFieldScenarioNo = null;

EditableObject? messageEditTarget = null;
string messageEditZoneName = "";
List<string> messageEditLabels = [];
List<string> messageEditTexts = [];

bool pendingOpenMessageEditWindow = false;

var flowGraphEditor = new FlowGraphEditor();
var smg1FlowGraphEditor = new SMG1FlowGraphEditor();
var iconCache = new IconTextureCache();

EditorSettings settings = EditorSettings.Load(EditorSettings.DefaultPath);
Loc.Init(Path.Combine(AppContext.BaseDirectory, "lang"), settings.UiLanguage);
formSMG1Dir = settings.SMG1BaseDir ?? "";
formSMG2Dir = settings.SMG2BaseDir ?? "";
formSMG2Language = settings.SMG2Language ?? SMG2Languages.Default;

if (settings.SMG1BaseDir is not null || settings.SMG2BaseDir is not null)
{
    hubScreen = HubScreen.ProjectPicker;

    ProjectEntry? lastOpenedProject = settings.Projects.FirstOrDefault(p => p.Id == settings.LastOpenedProjectId);
    if (lastOpenedProject is { } startupProject && settings.BaseDirFor(startupProject.Game) is { } startupBaseDir && GalaxyLoader.DetectGame(startupBaseDir) == startupProject.Game)
    {
        gameRootDir = startupBaseDir;
        game = startupProject.Game;
        outputDir = startupProject.OutputDir;
        activeProjectId = startupProject.Id;
        activeProjectName = startupProject.Name;
        activeProjectIconKey = startupProject.IconKey;
        PopulateAvailableGalaxies(gameRootDir);
        hubScreen = HubScreen.StagePicker;
    }
}

if (Array.IndexOf(args, "--show-picker") >= 0)
{
    hubScreen = HubScreen.ProjectPicker;
    pickerMode = ProjectPickerMode.List;
}
else if (Array.IndexOf(args, "--show-picker-form") >= 0)
{
    hubScreen = HubScreen.ProjectPicker;
    pickerMode = ProjectPickerMode.Form;
}
else if (Array.IndexOf(args, "--show-gamedirs") >= 0)
{
    hubScreen = HubScreen.GameDirsSetup;
}

var galaxyWindowOptions = WindowOptions.Default with
{
    Size = new Vector2D<int>(1000, 760),
    Position = new Vector2D<int>(80, 80),
    Title = AppTitle(),
};
IWindow galaxyWindow = Window.Create(galaxyWindowOptions);

GL? galaxyGl = null;
ImGuiController? galaxyImgui = null;

IWindow? cameraWindow = null;
GL? cameraGl = null;
ImGuiController? cameraImgui = null;

bool pendingOpenCameraWindow = false;

string? pendingGalaxyLoadName = null;

CANMAnimation? introCamera = null;
int introCameraScenarioNo = 0;
float cameraPreviewFrame = 0f;
bool cameraPreviewPlaying = false;
bool cameraPreviewActive = false;
int selectedCanmTrack = -1;
int selectedCanmKeyframe = -1;

var cameraKeyWasDown = new Dictionary<ImGuiKey, bool>();
bool CameraKeyPressedEdge(ImGuiKey key)
{
    bool down = ImGui.IsKeyDown(key);
    bool wasDown = cameraKeyWasDown.TryGetValue(key, out bool w) && w;
    cameraKeyWasDown[key] = down;
    return down && !wasDown;
}

int draggingCanmTrack = -1;
int draggingCanmKeyframe = -1;

bool canmPressStartedOnKeyframe = false;
string[] CanmTrackNames() => [L("Position X"), L("Position Y"), L("Position Z"), L("Target X"), L("Target Y"), L("Target Z"), L("Twist"), L("FOV")];

IWindow? demoWindow = null;
GL? demoGl = null;
ImGuiController? demoImgui = null;
bool pendingOpenDemoWindow = false;
DemoTimeline? demoTimeline = null;
string demoTimelineTitle = "";
string? demoTimelineError = null;
object? selectedDemoEntry = null;
string[] demoTrackNames = ["Time", "SubPart", "Action", "Camera", "Player", "Sound", "Wipe"];

string? demoName = null;
Dictionary<string, Dictionary<string, object?>> demoCameraParams = new();

object? draggingDemoEntry = null;
float draggingDemoStartMouseX = 0f;
int draggingDemoStartValue = 0;
Action<int>? draggingDemoApply = null;

bool placingCameraTypePreviewPlayer = false;

bool rotatingCameraTypePreviewPlayer = false;
float cameraTypePreviewPlayerYawDeg = 0f;

bool cameraTypePreviewActive = false;
Vector3 cameraTypePreviewPlayerPos = Vector3.Zero;
EditableObject? cameraTypePreviewSource = null;

LoadedObject? cameraTypePreviewPlayerModel = null;
ObjectInstance? cameraTypePreviewPlayerInstance = null;

string? cameraTypePreviewDebugText = null;

string addObjectSearchText = "";
ObjectDbEntry? addObjectSelectedEntry = null;
string addObjectSelectedLayer = "Common";
string addObjectSelectedZone = "";
bool pendingOpenAddObjectsPopup = false;
AddKind addObjectKind = AddKind.Object;

bool pendingOpenAddZonePopup = false;
string addZoneSearchText = "";
string? addZoneSelected = null;
int addZoneKindFilter = 0;

bool pendingOpenAddGeneralPosPopup = false;
string addGeneralPosSearchText = "";
string? addGeneralPosSelected = null;

bool pendingOpenLightEditor = false;
List<Dictionary<string, object?>> lightPresets = [];
List<LightGalaxyMapEntry> lightGalaxyMap = [];
int lightSelectedIndex = -1;
string lightSearchText = "";
bool lightGalaxyOnly = true;
bool lightDirty = false;
string? lightEditorError = null;

bool pendingOpenMapObjEditor = false;
List<MapObjTableRow> mapObjRows = [];
string mapObjSearch = "";
string mapObjClassFilter = "";
bool mapObjDirty = false;
string? mapObjError = null;

EditableObject? pendingPlacement = null;

EditablePath? pendingPath = null;

(EditablePath Path, int InsertIndex)? pendingPathPointInsert = null;

Vector3? pendingPathSurfaceSnap = null;

bool deleteClickMode = false;

bool copyClickMode = false;

Dictionary<string, LoadedObject?> addedObjectModelCache = new(StringComparer.OrdinalIgnoreCase);

float cameraTypePreviewPanAngleDeg = 0f;
float cameraTypePreviewPanTargetDeg = 0f;
bool cameraTypePreviewPanning = false;

bool playWaitAnimations = false;
bool wasPlayWaitAnimations = false;
float waitAnimationClockSeconds = 0f;
int lastSimulatedDiscreteFrame = 0;

const double AutosaveIntervalSeconds = 300;
double autosaveTimerSeconds = 0;

string AutosaveDir(GalaxySession s) => Path.Combine(AppContext.BaseDirectory, "cache", "autosave", s.Game.ToString(), s.GalaxyName);

EditableObject? selectedMapPartsSimObj = null;
float selectedMapPartsClockSeconds = 0f;
int lastSelectedMapPartsFrame = 0;

bool showPaths = true;

bool showCameraAreas = true;
bool showRegularAreas = true;
bool showGravityAreas = true;

bool previewLighting = args.Contains("--preview-lighting");
int previewLightGroupChoice = 0;
Dictionary<string, object?>? previewLightPreset = null;
string[] previewLightGroupChoices = ["Auto (per object)", "Player", "Strong", "Weak", "Planet"];

if (Array.IndexOf(args, "--hide-areas") >= 0)
{
    showCameraAreas = false;
    showRegularAreas = false;
    showGravityAreas = false;
}

var hiddenZoneStagePaths = new HashSet<string>();

EditableObject? lastRevealedSelection = null;
var revealZoneStagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
(string StagePath, string TreeGroup)? revealCategory = null;
bool revealScrollPending = false;

var keyWasDown = new Dictionary<ImGuiKey, bool>();
bool KeyPressedEdge(ImGuiKey key)
{
    bool down = ImGui.IsKeyDown(key);
    bool wasDown = keyWasDown.TryGetValue(key, out bool w) && w;
    keyWasDown[key] = down;
    return down && !wasDown;
}

void HookImGuiKeyEvents(IInputContext inputContext, ImGuiController controller)
{
    foreach (IKeyboard kb in inputContext.Keyboards)
    {
        kb.KeyDown += (_, key, _) => FeedImGuiKey(controller, key, true);
        kb.KeyUp += (_, key, _) => FeedImGuiKey(controller, key, false);
    }
}

static void FeedImGuiKey(ImGuiController controller, Key key, bool down)
{
    ImGuiKey mapped = key switch
    {
        Key.Backspace => ImGuiKey.Backspace,
        Key.Delete => ImGuiKey.Delete,
        Key.Enter => ImGuiKey.Enter,
        Key.KeypadEnter => ImGuiKey.KeypadEnter,
        Key.Tab => ImGuiKey.Tab,
        Key.Escape => ImGuiKey.Escape,
        Key.Space => ImGuiKey.Space,
        Key.Left => ImGuiKey.LeftArrow,
        Key.Right => ImGuiKey.RightArrow,
        Key.Up => ImGuiKey.UpArrow,
        Key.Down => ImGuiKey.DownArrow,
        Key.Home => ImGuiKey.Home,
        Key.End => ImGuiKey.End,
        Key.PageUp => ImGuiKey.PageUp,
        Key.PageDown => ImGuiKey.PageDown,
        Key.Insert => ImGuiKey.Insert,
        Key.ControlLeft => ImGuiKey.LeftCtrl,
        Key.ControlRight => ImGuiKey.RightCtrl,
        Key.ShiftLeft => ImGuiKey.LeftShift,
        Key.ShiftRight => ImGuiKey.RightShift,
        Key.AltLeft => ImGuiKey.LeftAlt,
        Key.AltRight => ImGuiKey.RightAlt,
        Key.A => ImGuiKey.A,
        Key.C => ImGuiKey.C,
        Key.V => ImGuiKey.V,
        Key.X => ImGuiKey.X,
        Key.Y => ImGuiKey.Y,
        Key.Z => ImGuiKey.Z,
        _ => ImGuiKey.None,
    };

    if (mapped == ImGuiKey.None)
    {
        return;
    }

    nint previous = ImGui.GetCurrentContext();
    ImGui.SetCurrentContext(controller.Context);
    ImGui.GetIO().AddKeyEvent(mapped, down);
    if (previous != nint.Zero && previous != controller.Context)
    {
        ImGui.SetCurrentContext(previous);
    }
}

bool IsCtrlDown() => ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl);
bool IsShiftDown() => ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);

IWindow? window = null;
GL? gl = null;
ImGuiController? imgui = null;
SceneRenderer? renderer = null;
ViewportFramebuffer? viewportFbo = null;
GalaxySession? session = null;
string? statusMessage = null;

var pluginManager = new PluginManager(AppContext.BaseDirectory);
pluginManager.Rescan(settings.ApprovedPlugins);
var pluginContext = new PluginHostContext { StatusSink = m => statusMessage = m };
bool pluginsNotifiedOpen = false;
DiscoveredPlugin? pendingConsentPlugin = null;

bool pendingOpenLevelEditorWindow = false;

bool pendingCloseLevelEditorWindow = false;

bool showUnsavedChangesPopup = false;
Action? pendingDiscardAction = null;

bool bypassCloseConfirmOnce = false;

if (Array.IndexOf(args, "--galaxy") >= 0)
{
    pendingOpenLevelEditorWindow = true;
}

bool cliGalaxyHandled = false;

float yaw = 0.6f, pitch = 0.35f, distance = 1000f;
bool orthographicCamera = false;
bool showObjectLinks = false;

float sceneRadius = 1000f;
bool draggingViewport = false;
bool draggingPan = false;
int frameCount = 0;
int demoFrameCount = 0;
int galaxyFrameCount = 0;

Vector2 lastViewportImagePos = Vector2.Zero;
bool lastViewportHovered = false;
var viewportGizmo = new ViewportGizmo();

bool gizmoWasDraggingThisFrame = false;

bool gizmoDragTrackingActive = false;
Vector3 gizmoDragBeforePosition = default;
Vector3 gizmoDragBeforeRotation = default;
Vector3 gizmoDragBeforeControlIn = default;
Vector3 gizmoDragBeforeControlOut = default;

Vector3? pendingVector3EditBefore = null;

EditableScenario? scenarioModalTarget = null;
string scenarioModalName = "";
int scenarioModalNo = 1;
bool[] scenarioModalPowerStars = new bool[8];
int scenarioModalPowerStarTypeIdx = 0;
int scenarioModalCometIdx = 0;
int scenarioModalCometTimer = 0;
bool scenarioModalIsHidden = false;
List<ScenarioListEntry> scenarioModalCometEntries = [];
List<ScenarioListEntry> scenarioModalAppearEntries = [];
int scenarioModalAppearIdx = 0;
List<string> scenarioModalZoneNames = [];
Dictionary<string, bool[]> scenarioModalZoneLayers = new();
string[] powerStarTypes = ["Normal", "Green", "Hidden"];

string scenarioModalBgmIdName = "";
int scenarioModalBgmStartTypeIdx = 0;
int scenarioModalBgmStartFrame = 0;
bool scenarioModalBgmIsPrepare = false;
string scenarioModalBgmGalaxyDefault = "";

string[] stageBgmSlotNames = ["", "", "", "", ""];
int[] stageBgmSlotStates = [-1, -1, -1, -1, -1];
string stageBgmLoadedSnapshot = "";

const float UiScale = 1.5f;
float sidebarWidth = 420 * UiScale;
float statusBarHeight = 24 * UiScale;

int screenshotArgIndex = Array.IndexOf(args, "--screenshot");
string? screenshotPath = screenshotArgIndex >= 0 && screenshotArgIndex + 1 < args.Length ? args[screenshotArgIndex + 1] : null;
int screenshotFrameIndex = Array.IndexOf(args, "--screenshot-frame");
int screenshotFrame = screenshotFrameIndex >= 0 && screenshotFrameIndex + 1 < args.Length ? int.Parse(args[screenshotFrameIndex + 1]) : 30;

playWaitAnimations = args.Contains("--play-animations");

orthographicCamera = args.Contains("--ortho");

pendingOpenAddObjectsPopup = args.Contains("--open-add-objects");
bool cliOpenLightEditor = args.Contains("--open-light-editor");
bool cliOpenMapObjTable = args.Contains("--open-mapobj-table");

int reproSwitchArgIndex = Array.IndexOf(args, "--repro-switch");
string? reproSwitchTarget = reproSwitchArgIndex >= 0 && reproSwitchArgIndex + 1 < args.Length ? args[reproSwitchArgIndex + 1] : null;
bool reproSwitchTriggered = false;
int reproSwitchOutputDirArgIndex = Array.IndexOf(args, "--repro-switch-output-dir");
string? reproSwitchOutputDir = reproSwitchOutputDirArgIndex >= 0 && reproSwitchOutputDirArgIndex + 1 < args.Length ? args[reproSwitchOutputDirArgIndex + 1] : null;
int reproSwitchGameArgIndex = Array.IndexOf(args, "--repro-switch-game");
int? reproSwitchGame = reproSwitchGameArgIndex >= 0 && reproSwitchGameArgIndex + 1 < args.Length ? int.Parse(args[reproSwitchGameArgIndex + 1]) : null;

RawImage? appIcon = LoadAppIcon();

galaxyWindow.Load += () =>
{
    galaxyGl = GL.GetApi(galaxyWindow);
    IInputContext galaxyInput = galaxyWindow.CreateInput();
    galaxyImgui = new ImGuiController(galaxyGl, galaxyWindow, galaxyInput, () => ConfigureFonts(13 * UiScale));
    HookImGuiKeyEvents(galaxyInput, galaxyImgui);
    galaxyGl.Viewport(0, 0, (uint)galaxyWindow.FramebufferSize.X, (uint)galaxyWindow.FramebufferSize.Y);
    ImGui.SetCurrentContext(galaxyImgui.Context);
    ImGui.GetStyle().ScaleAllSizes(UiScale);
    UpdateGalaxyWindowTitle();
    ApplyProjectIcon(galaxyWindow);
};

static float StepDelta(double dt) => Math.Clamp((float)dt, 0f, 0.1f);

galaxyWindow.Update += dt =>
{
    if (galaxyImgui is null)
    {
        return;
    }

    ImGui.SetCurrentContext(galaxyImgui.Context);
    galaxyImgui.Update(StepDelta(dt));
};

galaxyWindow.Render += _ =>
{
    if (galaxyGl is null || galaxyImgui is null)
    {
        return;
    }

    galaxyWindow.GLContext?.MakeCurrent();
    ImGui.SetCurrentContext(galaxyImgui.Context);
    galaxyGl.ClearColor(0.12f, 0.12f, 0.14f, 1f);
    galaxyGl.Clear((uint)ClearBufferMask.ColorBufferBit);

    DrawGalaxyHost();

    galaxyImgui.Render();

    galaxyFrameCount++;
    if (screenshotPath is not null && galaxyFrameCount == screenshotFrame)
    {
        SaveScreenshot(galaxyGl, galaxyWindow.FramebufferSize.X, galaxyWindow.FramebufferSize.Y, screenshotPath + ".galaxy.png");
    }
};

galaxyWindow.FramebufferResize += size => galaxyGl?.Viewport(size);

void CreateLevelEditorWindow()
{
    var options = WindowOptions.Default with { Size = new Vector2D<int>(1600, 900), Title = AppTitle(), WindowState = Silk.NET.Windowing.WindowState.Maximized };
    window = Window.Create(options);

    window.Load += () =>
    {
        gl = GL.GetApi(window);
    IInputContext input = window.CreateInput();

    imgui = new ImGuiController(gl, window, input, () => ConfigureFonts(13 * UiScale));
    HookImGuiKeyEvents(input, imgui);
    renderer = new SceneRenderer(gl);
    viewportFbo = new ViewportFramebuffer(gl);

    gl.Viewport(0, 0, (uint)window.FramebufferSize.X, (uint)window.FramebufferSize.Y);

    ImGui.SetCurrentContext(imgui.Context);
    ImGui.GetStyle().ScaleAllSizes(UiScale);
    UpdateWindowTitle();
    ApplyProjectIcon(window);

    int galaxyArgIndex = Array.IndexOf(args, "--galaxy");
    if (galaxyArgIndex >= 0 && galaxyArgIndex + 1 < args.Length && !cliGalaxyHandled)
    {
        cliGalaxyHandled = true;
        string galaxyName = args[galaxyArgIndex + 1];
        int gameArgIndex = Array.IndexOf(args, "--game");
        int cliGame = gameArgIndex >= 0 && gameArgIndex + 1 < args.Length ? int.Parse(args[gameArgIndex + 1]) : 2;
        int scenarioArgIndex = Array.IndexOf(args, "--scenario");
        int cliScenarioIndex = scenarioArgIndex >= 0 && scenarioArgIndex + 1 < args.Length ? int.Parse(args[scenarioArgIndex + 1]) : 0;
        try
        {
            gameRootDir = Path.Combine(GalaxyLoader.FindSmgFilesRoot(), cliGame.ToString());
            game = cliGame;
            hubScreen = HubScreen.StagePicker;
            UpdateGalaxyWindowTitle();

            int outputDirArgIndex = Array.IndexOf(args, "--output-dir");
            outputDir = outputDirArgIndex >= 0 && outputDirArgIndex + 1 < args.Length ? args[outputDirArgIndex + 1] : null;

            session = GalaxySession.Load(gameRootDir, outputDir, galaxyName, game, cliScenarioIndex, db, renderer);
            ApplyInitialCameraFraming(session);
            previewLightPreset = null;
            statusMessage = LF("Loaded {0}: {1} object(s), {2} rendered.", galaxyName, session.Objects.Count, session.Instances.Count);

            if (cliOpenLightEditor)
            {
                OpenLightEditor();
            }

            if (cliOpenMapObjTable)
            {
                OpenProductMapObjEditor();
            }

            int focusObjectArgIndex = Array.IndexOf(args, "--focus-object");
            if (focusObjectArgIndex >= 0 && focusObjectArgIndex + 1 < args.Length)
            {
                string wantedInternalName = args[focusObjectArgIndex + 1];
                EditableObject? focusObj = session.Objects.FirstOrDefault(o => o.InternalName == wantedInternalName);
                Console.WriteLine(focusObj is not null
                    ? $"[FocusObject] Found {wantedInternalName} at {focusObj.Position}, stage {focusObj.StagePath}"
                    : $"[FocusObject] No placement named '{wantedInternalName}' found among {session.Objects.Count} object(s).");
                if (focusObj is not null)
                {
                    if (!args.Contains("--no-focus-select"))
                    {
                        session.Selected = focusObj;
                    }

                    float autoDistance = 800f;
                    if (focusObj.Instance is { } inst)
                    {
                        Vector3 boundsCenter = Vector3.Transform((inst.Object.LocalBoundsMin + inst.Object.LocalBoundsMax) / 2f, inst.WorldMatrix);
                        session.ViewCenter = boundsCenter;
                        autoDistance = Math.Max((inst.Object.LocalBoundsMax - inst.Object.LocalBoundsMin).Length(), 10f);
                    }
                    else
                    {
                        session.ViewCenter = focusObj.Position;
                    }

                    int focusDistanceArgIndex = Array.IndexOf(args, "--focus-distance");
                    distance = focusDistanceArgIndex >= 0 && focusDistanceArgIndex + 1 < args.Length
                        ? float.Parse(args[focusDistanceArgIndex + 1])
                        : autoDistance;

                    int focusPointArgIndex = Array.IndexOf(args, "--focus-point");
                    if (focusPointArgIndex >= 0 && focusPointArgIndex + 1 < args.Length)
                    {
                        string[] parts = args[focusPointArgIndex + 1].Split(',');
                        session.ViewCenter = new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
                    }
                }
            }

            int focusPathPointArgIndex = Array.IndexOf(args, "--focus-path-point");
            if (focusPathPointArgIndex >= 0 && focusPathPointArgIndex + 1 < args.Length)
            {
                string[] fppParts = args[focusPathPointArgIndex + 1].Split(',');
                int fppPathIndex = int.Parse(fppParts[0]);
                int fppPointIndex = int.Parse(fppParts[1]);
                PathPointPart fppPart = fppParts.Length > 2
                    ? fppParts[2] switch { "in" => PathPointPart.ControlIn, "out" => PathPointPart.ControlOut, _ => PathPointPart.Anchor }
                    : PathPointPart.Anchor;

                EditablePath fppPath = session.Paths[fppPathIndex];
                session.SelectedPath = fppPath;
                session.SelectedPathPointIndex = fppPointIndex;
                session.SelectedPathPointPart = fppPart;
                session.Selected = null;

                PathPoint fppPoint = fppPath.WorldPoints[fppPointIndex];
                session.ViewCenter = fppPart switch
                {
                    PathPointPart.ControlIn => fppPoint.ControlPointIn,
                    PathPointPart.ControlOut => fppPoint.ControlPointOut,
                    _ => fppPoint.Position,
                };
                distance = 500f;
                Console.WriteLine($"[FocusPathPoint] path {fppPathIndex} ({fppPath.Name}), point {fppPointIndex}, part {fppPart}, at {session.ViewCenter}");
            }

            int moveFocusedArgIndex = Array.IndexOf(args, "--move-focused");
            if (moveFocusedArgIndex >= 0 && moveFocusedArgIndex + 1 < args.Length && session.Selected is { } moveTarget)
            {
                string[] moveParts = args[moveFocusedArgIndex + 1].Split(',');
                moveTarget.Position += new Vector3(float.Parse(moveParts[0]), float.Parse(moveParts[1]), float.Parse(moveParts[2]));
                moveTarget.SyncTransformToInstance();
            }

            if (args.Contains("--delete-focused") && session.Selected is { } deleteTarget)
            {
                RemoveObject(deleteTarget);
            }

            if (args.Contains("--save"))
            {
                statusMessage = SaveGalaxy.Save(session);
                Console.WriteLine("[Save] " + statusMessage);
            }

            if (args.Contains("--open-message-edit") && session.Selected is { } messageEditFocusObj
                && messageEditFocusObj.Fields.TryGetValue("MessageId", out object? focusMessageIdVal) && focusMessageIdVal is int focusMessageId)
            {
                if (game == 2 && gameRootDir is not null)
                {
                    string zoneName = messageEditFocusObj.StagePath.Split('/')[^1];
                    string language = settings.SMG2Language ?? SMG2Languages.Default;
                    flowGraphEditor.Open(gameRootDir, outputDir, language, zoneName, $"{MessageBaseName(messageEditFocusObj)}{focusMessageId:D3}");
                }
                else if (game == 1 && gameRootDir is not null)
                {
                    string zoneName = messageEditFocusObj.StagePath.Split('/')[^1];
                    smg1FlowGraphEditor.Open(gameRootDir, outputDir, $"{zoneName}_{MessageBaseName(messageEditFocusObj)}{focusMessageId:D3}");
                }
                else
                {
                    OpenMessageEditWindow(messageEditFocusObj, focusMessageId);
                }
            }

            int openDemoArgIndex = Array.IndexOf(args, "--open-demo");
            if (openDemoArgIndex >= 0)
            {
                string? wantedTimeSheetName = openDemoArgIndex + 1 < args.Length && !args[openDemoArgIndex + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[openDemoArgIndex + 1]
                    : null;
                EditableObject? demoObj = session.Objects.FirstOrDefault(o => o.SourceList == "DemoObjInfo" &&
                    (wantedTimeSheetName is null || (o.Fields.TryGetValue("TimeSheetName", out object? tsn) && tsn as string == wantedTimeSheetName)));
                if (demoObj is not null)
                {
                    OpenDemoTimeline(demoObj);
                }
            }
        }
        catch (Exception ex)
        {
            statusMessage = LF("Failed to load '{0}': {1}", galaxyName, ex.Message);
        }
    }
};

window.Update += dt =>
{
    if (imgui is null)
    {
        return;
    }

    ImGui.SetCurrentContext(imgui.Context);
    imgui.Update(StepDelta(dt));

    if (playWaitAnimations)
    {
        waitAnimationClockSeconds += StepDelta(dt);
    }
    else if (wasPlayWaitAnimations && session is not null)
    {
        foreach (EditableObject obj in session.Objects)
        {
            if (obj.RailMoveSim is not null || obj.RotateMoveSim is not null || obj.WalkerStateWanderSim is not null || obj.AstroDomeOrbitSim is not null)
            {
                obj.RailMoveSim = null;
                obj.RotateMoveSim = null;
                obj.WalkerStateWanderSim = null;
                obj.AstroDomeOrbitSim = null;
                obj.SyncTransformToInstance();
            }
        }
    }

    wasPlayWaitAnimations = playWaitAnimations;

    if (!playWaitAnimations)
    {
        string? selectedClassName = session?.Selected?.DbEntry?.ClassName(session.Game);
        if (session?.Selected is { } selected && (selectedClassName == "RailMoveObj" || selectedClassName == "RotateMoveObj"))
        {
            if (!ReferenceEquals(selected, selectedMapPartsSimObj))
            {
                selectedMapPartsSimObj = selected;
                selectedMapPartsClockSeconds = 0f;
                lastSelectedMapPartsFrame = 0;
            }

            selectedMapPartsClockSeconds += StepDelta(dt);
        }
        else if (selectedMapPartsSimObj is not null)
        {
            selectedMapPartsSimObj.RotateMoveSim = null;
            selectedMapPartsSimObj.RailMoveSim = null;
            selectedMapPartsSimObj.SyncTransformToInstance();
            selectedMapPartsSimObj = null;
            selectedMapPartsClockSeconds = 0f;
            lastSelectedMapPartsFrame = 0;
        }
    }

    autosaveTimerSeconds += dt;
    if (autosaveTimerSeconds >= AutosaveIntervalSeconds)
    {
        autosaveTimerSeconds = 0;
        if (session is { History.IsDirty: true })
        {
            string autosaveDir = AutosaveDir(session);
            SaveGalaxy.Save(session, autosaveDir);
            statusMessage = LF("Autosaved to {0}.", autosaveDir);
        }
    }
};

window.Render += _ =>
{
    if (gl is null || imgui is null || renderer is null || viewportFbo is null)
    {
        return;
    }

    window.GLContext?.MakeCurrent();
    ImGui.SetCurrentContext(imgui.Context);
    gl.ClearColor(0.12f, 0.12f, 0.14f, 1f);
    gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

    DrawHost();

    imgui.Render();

    frameCount++;
    if (screenshotPath is not null && frameCount == screenshotFrame)
    {
        SaveScreenshot(gl, window.FramebufferSize.X, window.FramebufferSize.Y, screenshotPath);
    }
};

    window.FramebufferResize += size => gl?.Viewport(size);

    window.Initialize();
    if (galaxyImgui is not null)
    {
        ImGui.SetCurrentContext(galaxyImgui.Context);
    }
}

galaxyWindow.Initialize();
if (pendingOpenLevelEditorWindow)
{
    pendingOpenLevelEditorWindow = false;
    CreateLevelEditorWindow();
}

while (!galaxyWindow.IsClosing)
{
    pluginContext.Game = game;
    pluginContext.GameRootDir = gameRootDir ?? "";
    pluginContext.OutputDir = outputDir ?? "";
    pluginContext.GalaxyName = session?.GalaxyName;
    pluginContext.UiScale = UiScale;

    bool projectActive = gameRootDir is not null && outputDir is not null;
    if (projectActive && !pluginsNotifiedOpen)
    {
        pluginManager.NotifyProjectOpened(pluginContext);
        pluginsNotifiedOpen = true;
    }
    else if (!projectActive && pluginsNotifiedOpen)
    {
        pluginManager.NotifyProjectClosed();
        pluginsNotifiedOpen = false;
    }

    if (!bypassCloseConfirmOnce)
    {
        if (galaxyWindow.IsClosing)
        {
            galaxyWindow.IsClosing = false;
            RequestDiscardChanges(() =>
            {
                bypassCloseConfirmOnce = true;
                galaxyWindow.Close();
            });
        }
        else if (window is { IsClosing: true })
        {
            window.IsClosing = false;
            RequestDiscardChanges(() =>
            {
                bypassCloseConfirmOnce = true;
                window?.Close();
            });
        }
    }
    else
    {
        bypassCloseConfirmOnce = false;
    }

    if (reproSwitchTarget is not null && !reproSwitchTriggered && session is not null && frameCount > 10)
    {
        reproSwitchTriggered = true;
        int reproGame = reproSwitchGame ?? game;
        string reproGameRootDir = reproSwitchGame is { } g ? Path.Combine(GalaxyLoader.FindSmgFilesRoot(), g.ToString()) : gameRootDir!;
        Console.WriteLine("[ReproSwitch] Closing galaxy, switching project, loading '" + reproSwitchTarget + "'...");
        SwitchProject();
        gameRootDir = reproGameRootDir;
        game = reproGame;
        outputDir = reproSwitchOutputDir;
        pendingGalaxyLoadName = reproSwitchTarget;
    }

    if (pendingOpenCameraWindow)
    {
        pendingOpenCameraWindow = false;
        CreateCameraWindow();
    }

    if (pendingCloseLevelEditorWindow)
    {
        pendingCloseLevelEditorWindow = false;
        window?.Close();
    }

    if (pendingGalaxyLoadName is not null)
    {
        string galaxyToLoad = pendingGalaxyLoadName;
        pendingGalaxyLoadName = null;

        if (window is null || window.IsClosing)
        {
            CreateLevelEditorWindow();
        }

        window!.GLContext?.MakeCurrent();
        if (imgui is not null)
        {
            ImGui.SetCurrentContext(imgui.Context);
        }

        LoadGalaxyByName(galaxyToLoad);
    }

    if (pendingOpenDemoWindow)
    {
        pendingOpenDemoWindow = false;
        CreateDemoWindow();
    }

    if (!galaxyWindow.IsClosing)
    {
        galaxyWindow.DoEvents();
        galaxyWindow.DoUpdate();
        RenderWindowFrame(galaxyWindow);
    }

    if (cameraWindow is { IsClosing: false })
    {
        cameraWindow.DoEvents();
        cameraWindow.DoUpdate();
        RenderWindowFrame(cameraWindow);
    }
    else if (cameraWindow is not null)
    {
        cameraWindow.DoEvents();
        cameraWindow.GLContext?.MakeCurrent();
        cameraImgui?.Dispose();
        cameraWindow.Dispose();
        cameraImgui = null;
        cameraGl = null;
        cameraPreviewActive = false;
        cameraWindow = null;
    }

    if (demoWindow is { IsClosing: false })
    {
        demoWindow.DoEvents();
        demoWindow.DoUpdate();
        RenderWindowFrame(demoWindow);
    }
    else if (demoWindow is not null)
    {
        demoWindow.DoEvents();
        demoWindow.GLContext?.MakeCurrent();
        demoImgui?.Dispose();
        demoWindow.Dispose();
        demoImgui = null;
        demoGl = null;
        demoWindow = null;
    }

    if (window is { IsClosing: false })
    {
        window.DoEvents();
        window.DoUpdate();
        RenderWindowFrame(window);
    }
    else if (window is not null)
    {
        window.DoEvents();
        window.GLContext?.MakeCurrent();
        imgui?.Dispose();
        window.Dispose();
        imgui = null;
        gl = null;
        renderer = null;
        viewportFbo = null;
        session = null;
        window = null;
    }
}

galaxyWindow.DoEvents();

if (window is { IsClosing: false })
{
    window.Close();
    window.DoEvents();
}

if (cameraWindow is { IsClosing: false })
{
    cameraWindow.Close();
    cameraWindow.DoEvents();
}

if (demoWindow is { IsClosing: false })
{
    demoWindow.Close();
    demoWindow.DoEvents();
}

static unsafe void SaveScreenshot(GL gl, int width, int height, string path)
{
    byte[] pixels = new byte[width * height * 4];
    fixed (byte* p = pixels)
    {
        gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
    }

    byte[] flipped = new byte[width * height * 4];
    for (int y = 0; y < height; y++)
    {
        Array.Copy(pixels, (height - 1 - y) * width * 4, flipped, y * width * 4, width * 4);
    }

    ImageIo.WritePng(path, width, height, flipped);
    Console.WriteLine($"[Screenshot] Saved {width}x{height} to {path}");
}

static RawImage? LoadAppIcon()
{
    string? iconPath = FindRepoRootFile("supernova.png");
    if (iconPath is null)
    {
        return null;
    }

    try
    {
        (int width, int height, byte[] rgba) = ImageIo.DecodeRgba(iconPath);
        return new RawImage(width, height, rgba);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Icon] Failed to load {iconPath}: {ex.Message}");
        return null;
    }
}

static string? FindRepoRootFile(string fileName)
{
    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, fileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

void ApplyAppIcon(IWindow win)
{
    if (appIcon is { } icon)
    {
        win.SetWindowIcon(ref icon);
    }
}

void ApplyProjectIcon(IWindow win)
{
    if (activeProjectIconKey is { } key && ProjectIcons.TryDecodeRgba(key, out byte[] rgba, out int w, out int h))
    {
        var icon = new RawImage(w, h, rgba);
        win.SetWindowIcon(ref icon);
    }
    else
    {
        ApplyAppIcon(win);
    }
}

static string AppTitle() => $"Supernova ({BuildInfo.DisplayVersion})";

void UpdateWindowTitle() => window!.Title = activeProjectName is { Length: > 0 } name ? $"{AppTitle()} - {name}" : AppTitle();

void UpdateGalaxyWindowTitle() => galaxyWindow.Title = hubScreen switch
{
    HubScreen.GameDirsSetup => $"{AppTitle()} - {L("Set Up")}",
    HubScreen.ProjectPicker => $"{AppTitle()} - {L("Projects")}",
    _ => activeProjectName is { Length: > 0 } name ? $"{AppTitle()} - {name}" : AppTitle(),
};

static unsafe void ConfigureFonts(float fontSizePixels)
{
    ImGuiIOPtr io = ImGui.GetIO();
    io.ConfigWindowsMoveFromTitleBarOnly = true;

    string bundled = Path.Combine(AppContext.BaseDirectory, "fonts");
    string winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 0 } windir
        ? Path.Combine(windir, "Fonts")
        : "";

    string[] uiCandidates =
    [
        Path.Combine(bundled, "ui.ttf"), Path.Combine(bundled, "ui.otf"), Path.Combine(bundled, "ui.ttc"),
        Path.Combine(winFonts, "segoeui.ttf"), Path.Combine(winFonts, "arial.ttf"),
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
        "/usr/share/fonts/noto/NotoSans-Regular.ttf",
        "/System/Library/Fonts/SFNS.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/Library/Fonts/Arial.ttf",
        "/System/Library/Fonts/Geneva.ttf",
    ];

    string[] cjkCandidates =
    [
        Path.Combine(bundled, "cjk.otf"), Path.Combine(bundled, "cjk.ttf"), Path.Combine(bundled, "cjk.ttc"),
        Path.Combine(winFonts, "YuGothR.ttc"), Path.Combine(winFonts, "meiryo.ttc"), Path.Combine(winFonts, "msgothic.ttc"),
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/opentype/noto/NotoSansCJKjp-Regular.otf",
        "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/opentype/noto/NotoSerifCJK-Regular.ttc",
        "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc",
        "/System/Library/Fonts/ヒラギノ角ゴシック W3.ttc",
        "/System/Library/Fonts/ヒラギノ角ゴ ProN W3.ttc",
        "/Library/Fonts/Arial Unicode.ttf",
        "/System/Library/Fonts/Hiragino Sans GB.ttc",
        "/System/Library/Fonts/Supplemental/Hiragino Sans GB.ttc",
        "/System/Library/Fonts/Supplemental/Osaka.ttf",
    ];

    string? uiFont = FindExisting(uiCandidates);
    if (uiFont is not null)
    {
        io.Fonts.AddFontFromFileTTF(uiFont, fontSizePixels);
    }
    else
    {
        Console.WriteLine("[Fonts] No system UI font found - using the built-in ImGui font.");
        io.Fonts.AddFontDefault();
    }

    string? cjkFont = FindExisting(cjkCandidates);
    if (cjkFont is not null)
    {
        ImFontConfigPtr mergeConfig = new(ImGuiNative.ImFontConfig_ImFontConfig());
        mergeConfig.MergeMode = true;
        io.Fonts.AddFontFromFileTTF(cjkFont, fontSizePixels, mergeConfig, io.Fonts.GetGlyphRangesJapanese());
        mergeConfig.Destroy();
    }
    else
    {
        Console.WriteLine("[Fonts] No CJK font found - Japanese text (BCSV strings) will render as boxes. "
            + "Drop a font at fonts/cjk.otf next to the executable, or install Noto Sans CJK.");
    }

    static string? FindExisting(string[] candidates)
    {
        foreach (string path in candidates)
        {
            try
            {
                if (path.Length > 0 && File.Exists(path))
                {
                    return path;
                }
            }
            catch
            {
            }
        }

        return null;
    }
}

void DrawHost()
{
    ImGuiViewportPtr viewport = ImGui.GetMainViewport();
    ImGui.SetNextWindowPos(viewport.Pos);
    ImGui.SetNextWindowSize(viewport.Size);
    ImGui.SetNextWindowViewport(viewport.ID);

    ImGuiWindowFlags hostFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBringToFrontOnFocus
        | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.MenuBar;

    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
    ImGui.Begin("##Host", hostFlags);
    ImGui.PopStyleVar(2);

    DrawMenuBar();
    DrawUnsavedChangesPopup();
    DrawMessageEditWindow();
    DrawLightEditorPopup();
    DrawProductMapObjEditorPopup();
    flowGraphEditor.Draw(UiScale);
    smg1FlowGraphEditor.Draw(UiScale);
    DrawPluginWindows();

    Vector2 avail = ImGui.GetContentRegionAvail();
    float statusH = statusMessage is null ? 0 : statusBarHeight;

    ImGui.BeginChild("##Sidebar", new Vector2(sidebarWidth, avail.Y - statusH), ImGuiChildFlags.Borders);

    float scenarioBoxHeight = 130 * UiScale;
    ImGui.BeginChild("##ScenariosBox", new Vector2(0, scenarioBoxHeight), ImGuiChildFlags.Borders);
    ImGui.TextUnformatted(L("Scenarios"));
    ImGui.Separator();
    DrawScenarioList();
    ImGui.EndChild();

    float sidebarAvailY = ImGui.GetContentRegionAvail().Y;
    float treeHeight = MathF.Floor(sidebarAvailY * 0.45f);

    ImGui.BeginChild("##ObjectsTree", new Vector2(0, treeHeight), ImGuiChildFlags.Borders);
    ImGui.TextUnformatted(L("Objects"));
    ImGui.Separator();
    DrawAddDeleteCopyButtons();
    ImGui.Separator();
    DrawObjectTree();
    ImGui.EndChild();

    ImGui.BeginChild("##ParametersPanel", Vector2.Zero, ImGuiChildFlags.Borders);
    ImGui.TextUnformatted(L("Parameters"));
    ImGui.Separator();
    DrawParameterPanel();
    ImGui.EndChild();
    ImGui.EndChild();

    ImGui.SameLine();

    float viewportWidth = avail.X - sidebarWidth - ImGui.GetStyle().ItemSpacing.X;
    ImGui.BeginChild("##ViewportPanel", new Vector2(viewportWidth, avail.Y - statusH));
    DrawViewportPanel();
    ImGui.EndChild();

    if (statusMessage is not null)
    {
        ImGui.Separator();
        ImGui.TextWrapped(statusMessage);
    }

    ImGui.End();

    ImGuiIOPtr io = ImGui.GetIO();
    bool ctrl = IsCtrlDown();

    if (ctrl && KeyPressedEdge(ImGuiKey.O))
    {
        RequestDiscardChanges(SwitchProject);
    }

    if (ctrl && KeyPressedEdge(ImGuiKey.W) && session is not null)
    {
        RequestDiscardChanges(() => session = null);
    }

    if (ctrl && KeyPressedEdge(ImGuiKey.S))
    {
        SaveCurrentGalaxy();
    }

    if (ctrl && session is not null)
    {
        if (KeyPressedEdge(ImGuiKey.Z))
        {
            if (IsShiftDown())
            {
                session.History.Redo();
            }
            else
            {
                session.History.Undo();
            }
        }
        else if (KeyPressedEdge(ImGuiKey.Y))
        {
            session.History.Redo();
        }
    }

    bool escapePressed = KeyPressedEdge(ImGuiKey.Escape);

    if (escapePressed && !io.WantTextInput && pendingPlacement is not null)
    {
        RemoveObject(pendingPlacement);
        statusMessage = L("Placement cancelled.");
    }
    else if (!io.WantTextInput && pendingPath is not null && (escapePressed || KeyPressedEdge(ImGuiKey.Enter) || KeyPressedEdge(ImGuiKey.KeypadEnter)))
    {
        FinishPendingPath();
    }
    else if (escapePressed && !io.WantTextInput && pendingPathPointInsert is not null)
    {
        pendingPathPointInsert = null;
        statusMessage = L("Point insert cancelled.");
    }
    else if (escapePressed && !io.WantTextInput && (deleteClickMode || copyClickMode))
    {
        deleteClickMode = false;
        copyClickMode = false;
        statusMessage = null;
    }
    else if (!viewportGizmo.ConsumedEscapeThisFrame && escapePressed && session is not null && (session.Selected is not null || session.SelectedPath is not null) && !io.WantTextInput)
    {
        session.Selected = null;
        session.SelectedPath = null;
        session.SelectedPathPointIndex = null;
    }

    if (escapePressed && (cameraTypePreviewActive || placingCameraTypePreviewPlayer || rotatingCameraTypePreviewPlayer) && !io.WantTextInput)
    {
        cameraTypePreviewActive = false;
        placingCameraTypePreviewPlayer = false;
        rotatingCameraTypePreviewPlayer = false;
    }

    if (KeyPressedEdge(ImGuiKey.Delete) && !io.WantTextInput && pendingPlacement is null && pendingPath is null && pendingPathPointInsert is null)
    {
        DeleteSelectedOrEnterClickMode();
    }

    if (rotatingCameraTypePreviewPlayer && lastViewportHovered)
    {
        cameraTypePreviewPlayerYawDeg += io.MouseDelta.X * 0.5f;
    }

    if (cameraTypePreviewActive && !io.WantTextInput)
    {
        const float roundIntervalDeg = 45f;
        if (KeyPressedEdge(ImGuiKey.LeftArrow))
        {
            cameraTypePreviewPanTargetDeg = (MathF.Round(cameraTypePreviewPanAngleDeg / roundIntervalDeg) - 1) * roundIntervalDeg;
            cameraTypePreviewPanning = true;
        }
        else if (KeyPressedEdge(ImGuiKey.RightArrow))
        {
            cameraTypePreviewPanTargetDeg = (MathF.Round(cameraTypePreviewPanAngleDeg / roundIntervalDeg) + 1) * roundIntervalDeg;
            cameraTypePreviewPanning = true;
        }
        else if (KeyPressedEdge(ImGuiKey.DownArrow))
        {
            cameraTypePreviewPanTargetDeg = 0f;
            cameraTypePreviewPanning = true;
        }

        if (cameraTypePreviewPanning)
        {
            const float stepDeg = 0.08f * 180f / MathF.PI;
            cameraTypePreviewPanAngleDeg = cameraTypePreviewPanTargetDeg > cameraTypePreviewPanAngleDeg
                ? Math.Min(cameraTypePreviewPanAngleDeg + stepDeg, cameraTypePreviewPanTargetDeg)
                : Math.Max(cameraTypePreviewPanAngleDeg - stepDeg, cameraTypePreviewPanTargetDeg);

            if (MathF.Abs(cameraTypePreviewPanAngleDeg - cameraTypePreviewPanTargetDeg) < 0.001f)
            {
                cameraTypePreviewPanning = false;
            }
        }
    }
}

void SaveCurrentGalaxy()
{
    if (session is null)
    {
        return;
    }

    statusMessage = SaveGalaxy.Save(session);

    if (session.OutputDir is not null)
    {
        session.History.MarkSaved();
    }
}

void RequestDiscardChanges(Action proceed)
{
    if (session is { History.IsDirty: true })
    {
        pendingDiscardAction = proceed;
        showUnsavedChangesPopup = true;
    }
    else
    {
        proceed();
    }
}

void DrawUnsavedChangesPopup()
{
    if (showUnsavedChangesPopup)
    {
        ImGui.OpenPopup($"{L("Unsaved Changes")}###UnsavedChanges");
        showUnsavedChangesPopup = false;
    }

    if (!ImGui.BeginPopupModal($"{L("Unsaved Changes")}###UnsavedChanges", ImGuiWindowFlags.AlwaysAutoResize))
    {
        return;
    }

    ImGui.TextUnformatted(LF("{0} has unsaved changes.", session?.GalaxyName));
    ImGui.TextDisabled(L("Save them before continuing, or discard them?"));
    ImGui.Spacing();

    if (ImGui.Button(L("Save"), new Vector2(100 * UiScale, 0)))
    {
        SaveCurrentGalaxy();
        Action? action = pendingDiscardAction;
        pendingDiscardAction = null;
        bypassCloseConfirmOnce = true;
        ImGui.CloseCurrentPopup();
        action?.Invoke();
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Discard"), new Vector2(100 * UiScale, 0)))
    {
        Action? action = pendingDiscardAction;
        pendingDiscardAction = null;
        bypassCloseConfirmOnce = true;
        ImGui.CloseCurrentPopup();
        action?.Invoke();
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Cancel"), new Vector2(100 * UiScale, 0)))
    {
        pendingDiscardAction = null;
        ImGui.CloseCurrentPopup();
    }

    ImGui.EndPopup();
}

void DrawPluginWindows()
{
    foreach (LoadedPlugin plugin in pluginManager.Plugins)
    {
        if (!plugin.WindowOpen)
        {
            continue;
        }

        bool open = true;
        ImGui.SetNextWindowSize(new Vector2(760, 520) * UiScale, ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"{plugin.Info.Name}###plugin_{plugin.Info.Id}", ref open))
        {
            try
            {
                plugin.Instance.DrawWindow(pluginContext);
            }
            catch (Exception ex)
            {
                ImGui.TextColored(new Vector4(0.95f, 0.5f, 0.4f, 1f), $"Plugin error: {ex.Message}");
            }
        }

        ImGui.End();
        plugin.WindowOpen = open;
    }
}

void DrawMenuBar()
{
    if (ImGui.BeginMenuBar())
    {
        if (ImGui.BeginMenu(L("File")))
        {
            if (ImGui.MenuItem(L("Switch Project..."), "Ctrl+O"))
            {
                RequestDiscardChanges(SwitchProject);
            }

            if (ImGui.MenuItem(L("Save"), "Ctrl+S", false, session is not null))
            {
                SaveCurrentGalaxy();
            }

            if (ImGui.MenuItem(L("Close Galaxy"), "Ctrl+W", false, session is not null))
            {
                RequestDiscardChanges(() => session = null);
            }

            ImGui.Separator();
            if (ImGui.MenuItem(L("Exit"), "Alt+F4"))
            {
                galaxyWindow.Close();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(L("Edit")))
        {
            if (ImGui.MenuItem(L("Undo"), "Ctrl+Z", false, session?.History.CanUndo == true))
            {
                session!.History.Undo();
            }

            if (ImGui.MenuItem(L("Redo"), "Ctrl+Y", false, session?.History.CanRedo == true))
            {
                session!.History.Redo();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(L("Tools")))
        {
            if (ImGui.MenuItem(L("Light Editor..."), "", false, session is not null))
            {
                OpenLightEditor();
            }

            if (ImGui.MenuItem(L("Map Object Class Table..."), "", false, session?.Game == 2))
            {
                OpenProductMapObjEditor();
            }

            if (pluginManager.AnyActive)
            {
                ImGui.Separator();
                if (ImGui.BeginMenu(L("Plugins")))
                {
                    foreach (LoadedPlugin plugin in pluginManager.Plugins)
                    {
                        if (ImGui.MenuItem(plugin.Info.Name, "", plugin.WindowOpen))
                        {
                            plugin.WindowOpen = !plugin.WindowOpen;
                        }

                        if (plugin.Info.Description is { Length: > 0 } description && ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(description);
                        }
                    }

                    ImGui.EndMenu();
                }
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(L("View")))
        {
            ImGui.MenuItem(L("Play Object Animations"), "", ref playWaitAnimations);

            ImGui.MenuItem(L("Show Paths"), "", ref showPaths);

            ImGui.Separator();
            ImGui.MenuItem(L("Show Camera Areas"), "", ref showCameraAreas);
            ImGui.MenuItem(L("Show Regular Areas"), "", ref showRegularAreas);
            ImGui.MenuItem(L("Show Gravity Areas"), "", ref showGravityAreas);

            ImGui.Separator();
            ImGui.MenuItem(L("Preview Lighting (LightData)"), "", ref previewLighting);
            ImGui.BeginDisabled(!previewLighting);
            ImGui.SetNextItemWidth(180 * UiScale);
            ImGui.Combo(L("Light group"), ref previewLightGroupChoice, previewLightGroupChoices, previewLightGroupChoices.Length);
            ImGui.EndDisabled();

            ImGui.Separator();
            ImGui.MenuItem(L("Orthographic Camera"), "", ref orthographicCamera);
            ImGui.EndMenu();
        }

        if (session is not null)
        {
            ImGui.Text(LF("  {0} (scenario {1}, SMG{2})", session.GalaxyName, session.ScenarioIndex, session.Game));

            if (outputDir is not null && session.ScenarioIndex < session.Scenarios.Count)
            {
                int starNumber = session.Scenarios[session.ScenarioIndex].ScenarioNo;
                string language = settings.SMG2Language ?? SMG2Languages.Default;

                if (starNameFieldGalaxy != session.GalaxyName || starNameFieldScenarioNo != starNumber)
                {
                    starNameField = (session.Game == 1
                        ? SMG1Text.ResolveScenarioName(session.GameRootDir, outputDir, session.GalaxyName, starNumber)
                        : GalaxyText.ResolveScenarioName(session.GameRootDir, outputDir, language, session.GalaxyName, starNumber)) ?? "";
                    starNameFieldGalaxy = session.GalaxyName;
                    starNameFieldScenarioNo = starNumber;
                }

                ImGui.TextUnformatted(L("Mission Name:"));
                ImGui.SameLine();
                ImGui.SetNextItemWidth(260 * UiScale);
                ImGui.InputText("##StarName", ref starNameField, 256);

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(L("Star name (ScenarioName label) - click Save to write it."));
                }

                ImGui.SameLine();

                if (ImGui.Button($"{L("Save")}##StarName"))
                {
                    if (session.Game == 1)
                    {
                        SMG1Text.SetScenarioName(session.GameRootDir, outputDir, session.GalaxyName, starNumber, starNameField);
                    }
                    else
                    {
                        GalaxyText.SetScenarioName(session.GameRootDir, outputDir, language, session.GalaxyName, starNumber, starNameField);
                    }
                }
            }
        }

        ImGui.EndMenuBar();
    }
}

void ApplyInitialCameraFraming(GalaxySession session)
{
    (Vector3 farMin, Vector3 farMax) = GalaxyLoader.ComputeSceneBounds(session.Instances, includeSky: true);
    sceneRadius = Math.Max((farMax - farMin).Length() / 2f, 1f);

    EditableObject? marioStart = session.Objects
        .Where(o => o.SourceList == "StartInfo" && o.StagePath == session.GalaxyName && o.MarioNo is not null)
        .OrderBy(o => o.MarioNo)
        .FirstOrDefault();
    if (marioStart is not null)
    {
        session.ViewCenter = marioStart.Position;
        Vector3 forward = Vector3.Transform(new Vector3(0f, 0f, 1f), Matrix4x4.CreateRotationY(marioStart.Rotation.Y * MathF.PI / 180f));
        yaw = MathF.Atan2(-forward.X, -forward.Z);
        pitch = MathF.PI / 4f;
        distance = 1800f;
    }
    else
    {
        (Vector3 boundsMin, Vector3 boundsMax) = GalaxyLoader.ComputeSceneBounds(session.Instances);
        session.ViewCenter = (boundsMin + boundsMax) / 2f;
        distance = Math.Max((boundsMax - boundsMin).Length() / 2f, 1f) * 2.5f;
    }
}

void LoadGalaxyByName(string galaxyName)
{
    try
    {
        session = GalaxySession.Load(gameRootDir!, outputDir, galaxyName, game, 0, db, renderer!);
        ApplyInitialCameraFraming(session);
        statusMessage = LF("Loaded {0}: {1} object(s), {2} rendered.", galaxyName, session.Objects.Count, session.Instances.Count);
    }
    catch (Exception ex)
    {
        statusMessage = LF("Failed to load '{0}': {1}", galaxyName, ex.Message);
        session = null;
    }
}

void OpenCameraEditor()
{
    if (session is null || session.ScenarioIndex >= session.Scenarios.Count)
    {
        return;
    }

    EditableScenario scenario = session.Scenarios[session.ScenarioIndex];
    CANMAnimation? loaded = GalaxyLoader.TryLoadIntroCamera(session.GameRootDir, session.OutputDir, session.GalaxyName, scenario.ScenarioNo);
    if (loaded is null)
    {
        statusMessage = LF("No intro camera found for scenario {0} (camera/StartScenario{0}.canm).", scenario.ScenarioNo);
        return;
    }

    introCamera = loaded;
    introCameraScenarioNo = scenario.ScenarioNo;
    cameraPreviewFrame = 0f;
    cameraPreviewPlaying = false;
    selectedCanmTrack = -1;
    selectedCanmKeyframe = -1;

    if (cameraWindow is null || cameraWindow.IsClosing)
    {
        pendingOpenCameraWindow = true;
    }
}

void CreateCameraWindow()
{
    var cameraWindowOptions = WindowOptions.Default with
    {
        Size = new Vector2D<int>(680, 500),
        Position = new Vector2D<int>(540, 100),
        Title = $"Supernova - {L("Intro Camera Editor")}",
        WindowState = Silk.NET.Windowing.WindowState.Maximized,
    };
    cameraWindow = Window.Create(cameraWindowOptions);

    cameraWindow.Load += () =>
    {
        cameraGl = GL.GetApi(cameraWindow);
        IInputContext cameraInput = cameraWindow.CreateInput();
        cameraImgui = new ImGuiController(cameraGl, cameraWindow, cameraInput);
        cameraGl.Viewport(0, 0, (uint)cameraWindow.FramebufferSize.X, (uint)cameraWindow.FramebufferSize.Y);
        ImGui.SetCurrentContext(cameraImgui.Context);
        ImGui.GetStyle().ScaleAllSizes(UiScale);
        ApplyProjectIcon(cameraWindow);
    };

    cameraWindow.Update += dt =>
    {
        if (cameraImgui is null)
        {
            return;
        }

        ImGui.SetCurrentContext(cameraImgui.Context);
        cameraImgui.Update(StepDelta(dt));

        if (cameraPreviewPlaying && introCamera is not null)
        {
            cameraPreviewFrame += StepDelta(dt) * 60f;
            if (cameraPreviewFrame > introCamera.EndFrame)
            {
                cameraPreviewFrame %= Math.Max(introCamera.EndFrame, 1);
            }
        }
    };

    cameraWindow.Render += _ =>
    {
        if (cameraGl is null || cameraImgui is null)
        {
            return;
        }

        cameraWindow.GLContext?.MakeCurrent();
        ImGui.SetCurrentContext(cameraImgui.Context);
        cameraGl.ClearColor(0.12f, 0.12f, 0.14f, 1f);
        cameraGl.Clear((uint)ClearBufferMask.ColorBufferBit);

        DrawCameraEditorWindow();

        cameraImgui.Render();
    };

    cameraWindow.FramebufferResize += size => cameraGl?.Viewport(size);

    cameraWindow.Initialize();
    if (imgui is not null)
    {
        ImGui.SetCurrentContext(imgui.Context);
    }
}

void OpenDemoTimeline(EditableObject obj)
{
    if (session is null)
    {
        return;
    }

    string stageName = obj.StagePath.Contains('/') ? obj.StagePath[(obj.StagePath.LastIndexOf('/') + 1)..] : obj.StagePath;
    string? timeSheetName = obj.Fields.TryGetValue("TimeSheetName", out object? tsn) ? tsn as string : null;
    demoName = obj.Fields.TryGetValue("DemoName", out object? dn) ? dn as string : null;

    demoTimeline = null;
    demoTimelineError = null;
    selectedDemoEntry = null;
    demoTimelineTitle = $"{stageName} - {timeSheetName}";

    demoCameraParams = GalaxyLoader.LoadCameraParams(session.GameRootDir, session.OutputDir, stageName)
        .ToDictionary(kv => kv.Key, kv => new Dictionary<string, object?>(kv.Value));

    if (string.IsNullOrEmpty(timeSheetName))
    {
        demoTimelineError = "This placement has no TimeSheetName.";
    }
    else
    {
        string demoArcPath = Path.Combine(session.GameRootDir, "DATA", "files", "StageData", stageName, stageName + "Demo.arc");
        if (!File.Exists(demoArcPath))
        {
            demoTimelineError = $"No {stageName}Demo.arc found.";
        }
        else
        {
            try
            {
                RARCArchive demoArchive = RARCArchive.Load(demoArcPath);
                demoTimeline = StageDemoReader.ReadTimeline(demoArchive, timeSheetName);
                if (demoTimeline is null)
                {
                    demoTimelineError = $"No sheets found for TimeSheetName \"{timeSheetName}\" in {stageName}Demo.arc.";
                }
            }
            catch (Exception ex)
            {
                demoTimelineError = $"Failed to load {stageName}Demo.arc: {ex.Message}";
            }
        }
    }

    if (demoWindow is null || demoWindow.IsClosing)
    {
        pendingOpenDemoWindow = true;
    }
}

void CreateDemoWindow()
{
    var demoWindowOptions = WindowOptions.Default with
    {
        Size = new Vector2D<int>(900, 560),
        Position = new Vector2D<int>(480, 120),
        Title = $"Supernova - {L("Demo Timeline")}",
        WindowState = Silk.NET.Windowing.WindowState.Normal,
    };
    demoWindow = Window.Create(demoWindowOptions);

    demoWindow.Load += () =>
    {
        demoGl = GL.GetApi(demoWindow);
        IInputContext demoInput = demoWindow.CreateInput();

        demoImgui = new ImGuiController(demoGl, demoWindow, demoInput, () => ConfigureFonts(13 * UiScale));
        demoGl.Viewport(0, 0, (uint)demoWindow.FramebufferSize.X, (uint)demoWindow.FramebufferSize.Y);
        ImGui.SetCurrentContext(demoImgui.Context);
        ImGui.GetStyle().ScaleAllSizes(UiScale);
        ApplyProjectIcon(demoWindow);
    };

    demoWindow.Update += dt =>
    {
        if (demoImgui is null)
        {
            return;
        }

        ImGui.SetCurrentContext(demoImgui.Context);
        demoImgui.Update(StepDelta(dt));
    };

    demoWindow.Render += _ =>
    {
        if (demoGl is null || demoImgui is null)
        {
            return;
        }

        demoWindow.GLContext?.MakeCurrent();
        ImGui.SetCurrentContext(demoImgui.Context);
        demoGl.ClearColor(0.12f, 0.12f, 0.14f, 1f);
        demoGl.Clear((uint)ClearBufferMask.ColorBufferBit);

        DrawDemoTimelineWindow();

        demoImgui.Render();

        demoFrameCount++;
        if (screenshotPath is not null && demoFrameCount == screenshotFrame)
        {
            SaveScreenshot(demoGl, demoWindow.FramebufferSize.X, demoWindow.FramebufferSize.Y, screenshotPath + ".demo.png");
        }
    };

    demoWindow.FramebufferResize += size => demoGl?.Viewport(size);

    demoWindow.Initialize();
    if (imgui is not null)
    {
        ImGui.SetCurrentContext(imgui.Context);
    }
}


static Dictionary<string, (int Start, int Length)> ResolveDemoPartSpans(DemoTimeline timeline)
{
    var spans = new Dictionary<string, (int Start, int Length)>();
    int cursor = 0;
    foreach (DemoTimeEntry entry in timeline.TimeEntries)
    {
        spans[entry.PartName] = (cursor, entry.TotalStep);
        cursor += entry.TotalStep;
    }

    var remaining = new List<DemoSubPartEntry>(timeline.SubPartEntries);
    for (int pass = 0; pass < remaining.Count + 1 && remaining.Count > 0; pass++)
    {
        for (int i = remaining.Count - 1; i >= 0; i--)
        {
            DemoSubPartEntry sub = remaining[i];
            if (spans.TryGetValue(sub.MainPartName, out (int Start, int Length) main))
            {
                spans[sub.SubPartName] = (main.Start + sub.MainPartStep, sub.SubPartTotalStep);
                remaining.RemoveAt(i);
            }
        }
    }

    return spans;
}

void DrawDemoTimelineWindow()
{
    var displaySize = new Vector2(demoWindow!.FramebufferSize.X, demoWindow.FramebufferSize.Y);
    ImGui.SetNextWindowPos(Vector2.Zero);
    ImGui.SetNextWindowSize(displaySize);

    ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
    ImGui.Begin("##DemoTimeline", flags);

    ImGui.Text(demoTimelineTitle);

    float closeButtonWidth = ImGui.CalcTextSize("Close").X + ImGui.GetStyle().FramePadding.X * 2f;
    ImGui.SameLine(ImGui.GetWindowWidth() - closeButtonWidth - ImGui.GetStyle().WindowPadding.X);
    if (ImGui.Button(L("Close")))
    {
        demoWindow?.Close();
    }

    ImGui.Separator();

    if (demoTimelineError is { Length: > 0 })
    {
        ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), demoTimelineError);
        ImGui.End();
        return;
    }

    if (demoTimeline is not { } timeline)
    {
        ImGui.TextWrapped(L("No demo loaded."));
        ImGui.End();
        return;
    }

    Dictionary<string, (int Start, int Length)> spans = ResolveDemoPartSpans(timeline);

    var blocks = new List<(int Row, int Start, int Length, string Label, object Entry)>();
    void AddBlocks<T>(int row, IEnumerable<T> entries, Func<T, string> partName, Func<T, int>? overrideLength = null)
    {
        foreach (T entry in entries)
        {
            string name = partName(entry);
            if (!spans.TryGetValue(name, out (int Start, int Length) span))
            {
                continue;
            }

            blocks.Add((row, span.Start, overrideLength?.Invoke(entry) ?? span.Length, name, entry!));
        }
    }

    AddBlocks(0, timeline.TimeEntries, e => e.PartName);
    AddBlocks(1, timeline.SubPartEntries, e => e.SubPartName, e => e.SubPartTotalStep);
    AddBlocks(2, timeline.ActionEntries, e => e.PartName);
    AddBlocks(3, timeline.CameraEntries, e => e.PartName);
    AddBlocks(4, timeline.PlayerEntries, e => e.PartName);
    AddBlocks(5, timeline.SoundEntries, e => e.PartName);
    AddBlocks(6, timeline.WipeEntries, e => e.PartName);

    int totalFrames = Math.Max(1, timeline.TimeEntries.Sum(e => e.TotalStep));

    float pixelsPerFrame = 3f * UiScale;
    float rowHeight = 26f * UiScale;
    float rulerHeight = 22f * UiScale;
    float labelColumnWidth = 90f * UiScale;
    float contentWidth = totalFrames * pixelsPerFrame;
    float contentHeight = rulerHeight + (demoTrackNames.Length * rowHeight);

    float pad = 4f * UiScale;

    ImGui.BeginChild("##DemoLabels", new Vector2(labelColumnWidth, contentHeight + (16f * UiScale)), ImGuiChildFlags.Borders);
    Vector2 labelOrigin = ImGui.GetCursorScreenPos();
    ImDrawListPtr labelDrawList = ImGui.GetWindowDrawList();
    for (int row = 0; row < demoTrackNames.Length; row++)
    {
        float textY = labelOrigin.Y + rulerHeight + (row * rowHeight) + ((rowHeight - ImGui.GetTextLineHeight()) / 2f);
        labelDrawList.AddText(new Vector2(labelOrigin.X + pad, textY), 0xFFE0E0E0, demoTrackNames[row]);
    }

    ImGui.Dummy(new Vector2(labelColumnWidth, contentHeight));
    ImGui.EndChild();
    ImGui.SameLine();

    ImGui.BeginChild("##DemoTimelineScroll", new Vector2(0, contentHeight + (16f * UiScale)), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar);

    Vector2 origin = ImGui.GetCursorScreenPos();
    ImDrawListPtr drawList = ImGui.GetWindowDrawList();

    for (int f = 0; f <= totalFrames; f += 30)
    {
        float x = origin.X + (f * pixelsPerFrame);
        bool labeled = f % 60 == 0;
        drawList.AddLine(new Vector2(x, origin.Y + rulerHeight - (labeled ? 10f * UiScale : 5f * UiScale)), new Vector2(x, origin.Y + rulerHeight), 0xFF808080);
        if (labeled)
        {
            drawList.AddText(new Vector2(x + (2f * UiScale), origin.Y), 0xFFB0B0B0, f.ToString());
        }
    }

    Vector4[] trackColors =
    [
        new(0.55f, 0.55f, 0.60f, 1f),
        new(0.45f, 0.35f, 0.65f, 1f),
        new(0.20f, 0.55f, 0.85f, 1f),
        new(0.85f, 0.55f, 0.20f, 1f),
        new(0.85f, 0.25f, 0.35f, 1f),
        new(0.25f, 0.70f, 0.40f, 1f),
        new(0.70f, 0.70f, 0.25f, 1f),
    ];

    float minBlockWidth = 6f * UiScale;
    float edgeHandleWidth = 6f * UiScale;

    bool TryGetEdgeDrag(object entry, bool leftEdge, out Action<int>? apply, out int startValue)
    {
        switch (entry)
        {
            case DemoTimeEntry timeEntry when leftEdge:
            {
                int idx = -1;
                for (int i = 0; i < timeline.TimeEntries.Count; i++)
                {
                    if (ReferenceEquals(timeline.TimeEntries[i], timeEntry))
                    {
                        idx = i;
                        break;
                    }
                }

                if (idx <= 0)
                {
                    apply = null;
                    startValue = 0;
                    return false;
                }

                DemoTimeEntry previous = timeline.TimeEntries[idx - 1];
                startValue = previous.TotalStep;
                apply = v => previous.TotalStep = Math.Max(1, v);
                return true;
            }
            case DemoTimeEntry timeEntry:
                startValue = timeEntry.TotalStep;
                apply = v => timeEntry.TotalStep = Math.Max(1, v);
                return true;
            case DemoSubPartEntry subEntry when leftEdge:
                startValue = subEntry.MainPartStep;
                apply = v => subEntry.MainPartStep = Math.Max(0, v);
                return true;
            case DemoSubPartEntry subEntry:
                startValue = subEntry.SubPartTotalStep;
                apply = v => subEntry.SubPartTotalStep = Math.Max(1, v);
                return true;
            default:
                apply = null;
                startValue = 0;
                return false;
        }
    }

    foreach ((int Row, int Start, int Length, string Label, object Entry) block in blocks)
    {
        float x0 = origin.X + (block.Start * pixelsPerFrame);
        float x1 = Math.Max(origin.X + ((block.Start + Math.Max(block.Length, 1)) * pixelsPerFrame), x0 + minBlockWidth);
        float y0 = origin.Y + rulerHeight + (block.Row * rowHeight) + (2f * UiScale);
        float y1 = y0 + rowHeight - (4f * UiScale);

        Vector4 color = trackColors[block.Row];
        bool selected = ReferenceEquals(selectedDemoEntry, block.Entry);
        uint fillColor = ImGui.ColorConvertFloat4ToU32(selected ? color with { W = 1f } : color with { W = 0.75f });
        uint borderColor = selected ? 0xFFFFFFFF : 0xFF202020;

        drawList.AddRectFilled(new Vector2(x0, y0), new Vector2(x1, y1), fillColor, 2f);
        drawList.AddRect(new Vector2(x0, y0), new Vector2(x1, y1), borderColor, 2f, ImDrawFlags.None, selected ? 2f * UiScale : 1f);

        const uint handleColor = 0xFFF0F0F0;
        float handleInset = 2f * UiScale;
        if (TryGetEdgeDrag(block.Entry, true, out _, out _))
        {
            drawList.AddRectFilled(new Vector2(x0, y0 + handleInset), new Vector2(x0 + (2f * UiScale), y1 - handleInset), handleColor);
        }

        if (TryGetEdgeDrag(block.Entry, false, out _, out _))
        {
            drawList.AddRectFilled(new Vector2(x1 - (2f * UiScale), y0 + handleInset), new Vector2(x1, y1 - handleInset), handleColor);
        }

        if (x1 - x0 > 20f * UiScale)
        {
            drawList.PushClipRect(new Vector2(x0 + pad, y0), new Vector2(x1 - (2f * UiScale), y1), true);
            drawList.AddText(new Vector2(x0 + pad, y0 + (3f * UiScale)), 0xFFFFFFFF, block.Label);
            drawList.PopClipRect();
        }
    }

    ImGui.SetCursorScreenPos(origin);
    ImGui.InvisibleButton("##DemoTimelineHitArea", new Vector2(contentWidth, contentHeight));
    Vector2 mouse = ImGui.GetMousePos();

    if (ImGui.IsItemHovered() && draggingDemoApply is null)
    {
        foreach ((int Row, int Start, int Length, string Label, object Entry) block in blocks)
        {
            float x0 = origin.X + (block.Start * pixelsPerFrame);
            float x1 = Math.Max(origin.X + ((block.Start + Math.Max(block.Length, 1)) * pixelsPerFrame), x0 + minBlockWidth);
            float y0 = origin.Y + rulerHeight + (block.Row * rowHeight);
            float y1 = y0 + rowHeight;
            if (mouse.Y < y0 || mouse.Y > y1)
            {
                continue;
            }

            bool nearLeft = Math.Abs(mouse.X - x0) <= edgeHandleWidth && TryGetEdgeDrag(block.Entry, true, out _, out _);
            bool nearRight = Math.Abs(mouse.X - x1) <= edgeHandleWidth && TryGetEdgeDrag(block.Entry, false, out _, out _);
            if (nearLeft || nearRight)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
                break;
            }
        }
    }

    if (ImGui.IsItemActivated())
    {
        selectedDemoEntry = null;
        draggingDemoEntry = null;
        draggingDemoApply = null;

        foreach ((int Row, int Start, int Length, string Label, object Entry) block in blocks)
        {
            float x0 = origin.X + (block.Start * pixelsPerFrame);
            float x1 = Math.Max(origin.X + ((block.Start + Math.Max(block.Length, 1)) * pixelsPerFrame), x0 + minBlockWidth);
            float y0 = origin.Y + rulerHeight + (block.Row * rowHeight);
            float y1 = y0 + rowHeight;
            if (mouse.Y < y0 || mouse.Y > y1 || mouse.X < x0 - edgeHandleWidth || mouse.X > x1 + edgeHandleWidth)
            {
                continue;
            }

            bool nearLeft = Math.Abs(mouse.X - x0) <= edgeHandleWidth;
            bool nearRight = Math.Abs(mouse.X - x1) <= edgeHandleWidth;

            if (nearLeft && TryGetEdgeDrag(block.Entry, true, out Action<int>? leftApply, out int leftStart))
            {
                draggingDemoEntry = block.Entry;
                draggingDemoStartMouseX = mouse.X;
                draggingDemoStartValue = leftStart;
                draggingDemoApply = leftApply;
                selectedDemoEntry = block.Entry;
                break;
            }

            if (nearRight && TryGetEdgeDrag(block.Entry, false, out Action<int>? rightApply, out int rightStart))
            {
                draggingDemoEntry = block.Entry;
                draggingDemoStartMouseX = mouse.X;
                draggingDemoStartValue = rightStart;
                draggingDemoApply = rightApply;
                selectedDemoEntry = block.Entry;
                break;
            }

            if (mouse.X >= x0 && mouse.X <= x1)
            {
                selectedDemoEntry = block.Entry;
                break;
            }
        }
    }
    else if (ImGui.IsItemActive() && draggingDemoApply is not null)
    {
        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        int frameDelta = (int)MathF.Round((mouse.X - draggingDemoStartMouseX) / pixelsPerFrame);
        draggingDemoApply(draggingDemoStartValue + frameDelta);
    }

    if (ImGui.IsItemDeactivated())
    {
        draggingDemoEntry = null;
        draggingDemoApply = null;
    }

    ImGui.EndChild();

    ImGui.Separator();
    ImGui.Text(L("Selected entry"));
    DrawSelectedDemoEntry(selectedDemoEntry);

    ImGui.End();
}

void DrawNullableBoolField(string label, Func<bool?> get, Action<bool?> set)
{
    bool has = get().HasValue;
    if (ImGui.Checkbox($"{label} set", ref has))
    {
        set(has ? false : null);
    }

    if (get() is bool current)
    {
        ImGui.SameLine();
        bool v = current;
        if (ImGui.Checkbox(label, ref v))
        {
            set(v);
        }
    }
}

void DrawNullableIntField(string label, Func<int?> get, Action<int?> set)
{
    bool has = get().HasValue;
    if (ImGui.Checkbox($"{label} set", ref has))
    {
        set(has ? 0 : null);
    }

    if (get() is int current)
    {
        ImGui.SameLine();
        int v = current;
        ImGui.SetNextItemWidth(120 * UiScale);
        if (ImGui.InputInt(label, ref v))
        {
            set(v);
        }
    }
}

void DrawOptionalTextField(string label, Func<string?> get, Action<string?> set, uint maxLength = 64)
{
    string v = get() ?? "";
    if (ImGui.InputText(label, ref v, maxLength))
    {
        set(v.Length > 0 ? v : null);
    }
}

void DrawSelectedDemoEntry(object? entry)
{
    switch (entry)
    {
        case null:
            ImGui.TextDisabled(L("Click a block above to see its fields."));
            break;
        case DemoTimeEntry e:
        {
            ImGui.Text(LF("Time: {0}", e.PartName));
            DrawOptionalTextField("PartName##Time", () => e.PartName, v => e.PartName = v ?? "");
            int totalStep = e.TotalStep;
            if (ImGui.InputInt("TotalStep", ref totalStep))
            {
                e.TotalStep = totalStep;
            }

            bool suspend = e.SuspendFlag;
            if (ImGui.Checkbox("SuspendFlag", ref suspend))
            {
                e.SuspendFlag = suspend;
            }

            DrawNullableBoolField("WaitUserInputFlag", () => e.WaitUserInputFlag, v => e.WaitUserInputFlag = v);
            break;
        }
        case DemoActionEntry e:
        {
            ImGui.Text(LF("Action: {0}", e.PartName));
            DrawOptionalTextField("PartName##Action", () => e.PartName, v => e.PartName = v ?? "");
            DrawOptionalTextField("CastName", () => e.CastName, v => e.CastName = v);
            int castId = e.CastId;
            if (ImGui.InputInt("CastID", ref castId))
            {
                e.CastId = castId;
            }

            if (ImGui.BeginCombo("ActionType", e.ActionType.ToString()))
            {
                foreach (DemoActionType type in Enum.GetValues<DemoActionType>())
                {
                    bool isSelected = type == e.ActionType;
                    if (ImGui.Selectable(type.ToString(), isSelected))
                    {
                        e.ActionType = type;
                    }
                }

                ImGui.EndCombo();
            }

            DrawOptionalTextField("PosName##Action", () => e.PosName, v => e.PosName = v);
            DrawOptionalTextField("AnimName", () => e.AnimName, v => e.AnimName = v);
            break;
        }
        case DemoCameraEntry e:
        {
            ImGui.Text(LF("Camera: {0}", e.PartName));
            DrawOptionalTextField("PartName##Camera", () => e.PartName, v => e.PartName = v ?? "");
            DrawOptionalTextField("CameraTargetName", () => e.CameraTargetName, v => e.CameraTargetName = v);
            int camTargetCastId = e.CameraTargetCastId;
            if (ImGui.InputInt("CameraTargetCastID", ref camTargetCastId))
            {
                e.CameraTargetCastId = camTargetCastId;
            }

            DrawOptionalTextField("AnimCameraName", () => e.AnimCameraName, v => e.AnimCameraName = v);
            int startFrame = e.AnimCameraStartFrame;
            if (ImGui.InputInt("AnimCameraStartFrame", ref startFrame))
            {
                e.AnimCameraStartFrame = startFrame;
            }

            int endFrame = e.AnimCameraEndFrame;
            if (ImGui.InputInt("AnimCameraEndFrame", ref endFrame))
            {
                e.AnimCameraEndFrame = endFrame;
            }

            bool isContinuous = e.IsContinuous;
            if (ImGui.Checkbox("IsContinuous", ref isContinuous))
            {
                e.IsContinuous = isContinuous;
            }

            ImGui.Separator();

            if (string.IsNullOrEmpty(demoName))
            {
                ImGui.TextDisabled(L("This demo's DemoObjInfo placement has no DemoName - can't resolve a CameraParam.bcam row."));
            }
            else
            {
                string camParamId = $"e:{demoName}[{e.PartName}]";
                if (demoCameraParams.TryGetValue(camParamId, out Dictionary<string, object?>? camRow))
                {
                    ImGui.Text(LF("CameraParam.bcam row: {0}", camParamId));
                    foreach ((string key, object? value) in camRow.OrderBy(f => f.Key).ToList())
                    {
                        DrawCameraParamField(camRow, key, value);
                    }
                }
                else
                {
                    ImGui.TextDisabled(LF("No CameraParam.bcam row found with id \"{0}\".", camParamId));
                }
            }

            break;
        }
        case DemoPlayerEntry e:
        {
            ImGui.Text(LF("Player: {0}", e.PartName));
            DrawOptionalTextField("PartName##Player", () => e.PartName, v => e.PartName = v ?? "");
            DrawOptionalTextField("PosName##Player", () => e.PosName, v => e.PosName = v);
            DrawOptionalTextField("BCKName", () => e.BCKName, v => e.BCKName = v);
            break;
        }
        case DemoSoundEntry e:
        {
            ImGui.Text(LF("Sound: {0}", e.PartName));
            DrawOptionalTextField("PartName##Sound", () => e.PartName, v => e.PartName = v ?? "");
            DrawOptionalTextField("Bgm", () => e.Bgm, v => e.Bgm = v);
            DrawOptionalTextField("SystemSe", () => e.SystemSe, v => e.SystemSe = v);
            DrawOptionalTextField("ActionSe", () => e.ActionSe, v => e.ActionSe = v);
            bool returnBgm = e.ReturnBgm;
            if (ImGui.Checkbox("ReturnBgm", ref returnBgm))
            {
                e.ReturnBgm = returnBgm;
            }

            int bgmWipeoutFrame = e.BgmWipeoutFrame;
            if (ImGui.InputInt("BgmWipeoutFrame", ref bgmWipeoutFrame))
            {
                e.BgmWipeoutFrame = bgmWipeoutFrame;
            }

            DrawNullableIntField("AllSoundStopFrame", () => e.AllSoundStopFrame, v => e.AllSoundStopFrame = v);
            break;
        }
        case DemoSubPartEntry e:
        {
            ImGui.Text(LF("SubPart: {0}", e.SubPartName));
            DrawOptionalTextField("SubPartName", () => e.SubPartName, v => e.SubPartName = v ?? "");
            int subTotalStep = e.SubPartTotalStep;
            if (ImGui.InputInt("SubPartTotalStep", ref subTotalStep))
            {
                e.SubPartTotalStep = subTotalStep;
            }

            DrawOptionalTextField("MainPartName", () => e.MainPartName, v => e.MainPartName = v ?? "");
            int mainPartStep = e.MainPartStep;
            if (ImGui.InputInt("MainPartStep", ref mainPartStep))
            {
                e.MainPartStep = mainPartStep;
            }

            break;
        }
        case DemoWipeEntry e:
        {
            ImGui.Text(LF("Wipe: {0}", e.PartName));
            DrawOptionalTextField("PartName##Wipe", () => e.PartName, v => e.PartName = v ?? "");
            DrawOptionalTextField("WipeName", () => e.WipeName, v => e.WipeName = v);
            int wipeType = e.WipeType;
            if (ImGui.InputInt("WipeType", ref wipeType))
            {
                e.WipeType = wipeType;
            }

            int wipeFrame = e.WipeFrame;
            if (ImGui.InputInt("WipeFrame", ref wipeFrame))
            {
                e.WipeFrame = wipeFrame;
            }

            break;
        }
    }
}

CANMTrack[] GetCanmTracks(CANMAnimation anim) =>
    [anim.PositionX, anim.PositionY, anim.PositionZ, anim.TargetX, anim.TargetY, anim.TargetZ, anim.Twist, anim.FovY];

void DrawCameraEditorWindow()
{
    var displaySize = new Vector2(cameraWindow!.FramebufferSize.X, cameraWindow.FramebufferSize.Y);
    ImGui.SetNextWindowPos(Vector2.Zero);
    ImGui.SetNextWindowSize(displaySize);

    ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
    ImGui.Begin("##CameraEditor", flags);

    if (introCamera is null)
    {
        ImGui.TextWrapped(L("No intro camera loaded."));
        ImGui.End();
        return;
    }

    ImGui.Text(LF("Scenario {0} intro camera - {1} frames", introCameraScenarioNo, introCamera.EndFrame));

    float closeButtonWidth = ImGui.CalcTextSize("Close").X + ImGui.GetStyle().FramePadding.X * 2f;
    ImGui.SameLine(ImGui.GetWindowWidth() - closeButtonWidth - ImGui.GetStyle().WindowPadding.X);
    if (ImGui.Button(L("Close")))
    {
        cameraWindow?.Close();
    }

    ImGui.Checkbox(L("Preview in main viewport"), ref cameraPreviewActive);
    ImGui.SameLine();
    if (ImGui.Button(cameraPreviewPlaying ? "Pause" : "Play"))
    {
        cameraPreviewPlaying = !cameraPreviewPlaying;
    }

    ImGui.SameLine();
    ImGui.SetNextItemWidth(220 * UiScale);
    float frameSlider = cameraPreviewFrame;
    if (ImGui.SliderFloat(L("Frame"), ref frameSlider, 0f, introCamera.EndFrame))
    {
        cameraPreviewFrame = frameSlider;
        cameraPreviewPlaying = false;
    }

    ImGui.Separator();
    DrawCanmTimeline();
    ImGui.Separator();
    DrawCanmKeyframeInspector();

    CANMTrack[] canmTracksForNav = GetCanmTracks(introCamera!);
    if (selectedCanmTrack >= 0 && selectedCanmTrack < canmTracksForNav.Length && !ImGui.GetIO().WantTextInput)
    {
        List<CANMKeyframe> selectedTrackKeyframes = canmTracksForNav[selectedCanmTrack].Keyframes;
        if (CameraKeyPressedEdge(ImGuiKey.LeftArrow) && selectedCanmKeyframe > 0)
        {
            selectedCanmKeyframe--;
        }
        else if (CameraKeyPressedEdge(ImGuiKey.RightArrow) && selectedCanmKeyframe >= 0 && selectedCanmKeyframe < selectedTrackKeyframes.Count - 1)
        {
            selectedCanmKeyframe++;
        }
    }

    ImGui.End();
}

void DrawCanmTimeline()
{
    CANMAnimation anim = introCamera!;
    CANMTrack[] tracks = GetCanmTracks(anim);
    float endFrame = Math.Max(anim.EndFrame, 1);

    float labelWidth = 110 * UiScale;
    float rowHeight = 24 * UiScale;
    float markerRadius = 5 * UiScale;

    float hitRadius = Math.Min(markerRadius + (8f * UiScale), (rowHeight / 2f) - 1f);

    Vector2 avail = ImGui.GetContentRegionAvail();
    float timelineWidth = Math.Max(avail.X - labelWidth, 50f);
    float totalHeight = rowHeight * tracks.Length;
    float pixelsPerFrame = timelineWidth / endFrame;

    Vector2 origin = ImGui.GetCursorScreenPos();
    ImDrawListPtr drawList = ImGui.GetWindowDrawList();

    for (int t = 0; t < tracks.Length; t++)
    {
        float rowY = origin.Y + (t * rowHeight);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, rowY));
        ImGui.TextUnformatted(CanmTrackNames()[t]);

        drawList.AddLine(
            new Vector2(origin.X + labelWidth, rowY + (rowHeight / 2f)),
            new Vector2(origin.X + labelWidth + timelineWidth, rowY + (rowHeight / 2f)),
            ImGui.GetColorU32(ImGuiCol.Border));
    }

    ImGui.SetCursorScreenPos(new Vector2(origin.X + labelWidth, origin.Y));
    ImGui.InvisibleButton("##canm_timeline", new Vector2(timelineWidth, totalHeight));
    Vector2 mouse = ImGui.GetIO().MousePos;

    if (ImGui.IsItemActivated())
    {
        (int Track, int Keyframe)? hit = null;
        float bestDistSq = MathF.Pow(hitRadius, 2);
        for (int t = 0; t < tracks.Length; t++)
        {
            float rowY = origin.Y + (t * rowHeight) + (rowHeight / 2f);
            for (int k = 0; k < tracks[t].Keyframes.Count; k++)
            {
                float x = origin.X + labelWidth + (tracks[t].Keyframes[k].Frame * pixelsPerFrame);
                float distSq = Vector2.DistanceSquared(mouse, new Vector2(x, rowY));
                if (distSq <= bestDistSq)
                {
                    bestDistSq = distSq;
                    hit = (t, k);
                }
            }
        }

        if (hit is { } h)
        {
            bool alreadySelected = selectedCanmTrack == h.Track && selectedCanmKeyframe == h.Keyframe;
            selectedCanmTrack = h.Track;
            selectedCanmKeyframe = h.Keyframe;
            canmPressStartedOnKeyframe = true;

            if (alreadySelected)
            {
                draggingCanmTrack = h.Track;
                draggingCanmKeyframe = h.Keyframe;
            }
            else
            {
                draggingCanmTrack = -1;
                draggingCanmKeyframe = -1;
            }
        }
        else
        {
            draggingCanmTrack = -1;
            draggingCanmKeyframe = -1;
            canmPressStartedOnKeyframe = false;
        }
    }

    if (ImGui.IsItemActive())
    {
        float mouseX = mouse.X - (origin.X + labelWidth);
        if (draggingCanmTrack >= 0 && draggingCanmTrack < tracks.Length &&
            draggingCanmKeyframe >= 0 && draggingCanmKeyframe < tracks[draggingCanmTrack].Keyframes.Count)
        {
            CANMTrack draggedTrack = tracks[draggingCanmTrack];
            float newFrame = MathF.Round(Math.Clamp(mouseX / pixelsPerFrame, 0f, endFrame));
            draggedTrack.Keyframes[draggingCanmKeyframe] = draggedTrack.Keyframes[draggingCanmKeyframe] with { Frame = newFrame };
        }
        else if (!canmPressStartedOnKeyframe)
        {
            cameraPreviewFrame = Math.Clamp(mouseX / pixelsPerFrame, 0f, endFrame);
            cameraPreviewPlaying = false;
        }
    }

    if (ImGui.IsItemDeactivated() && draggingCanmTrack >= 0 && draggingCanmTrack < tracks.Length &&
        draggingCanmKeyframe >= 0 && draggingCanmKeyframe < tracks[draggingCanmTrack].Keyframes.Count)
    {
        CANMTrack draggedTrack = tracks[draggingCanmTrack];
        CANMKeyframe moved = draggedTrack.Keyframes[draggingCanmKeyframe];
        draggedTrack.Keyframes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        selectedCanmKeyframe = draggedTrack.Keyframes.IndexOf(moved);
        draggingCanmTrack = -1;
        draggingCanmKeyframe = -1;
    }

    for (int t = 0; t < tracks.Length; t++)
    {
        CANMTrack track = tracks[t];
        float rowY = origin.Y + (t * rowHeight) + (rowHeight / 2f);

        for (int k = 0; k < track.Keyframes.Count; k++)
        {
            float x = origin.X + labelWidth + (track.Keyframes[k].Frame * pixelsPerFrame);
            bool isSelected = selectedCanmTrack == t && selectedCanmKeyframe == k;
            uint color = isSelected ? ImGui.GetColorU32(new Vector4(1f, 0.7f, 0.2f, 1f)) : ImGui.GetColorU32(new Vector4(0.3f, 0.6f, 1f, 1f));
            drawList.AddCircleFilled(new Vector2(x, rowY), markerRadius, color);
        }
    }

    float playheadX = origin.X + labelWidth + (cameraPreviewFrame * pixelsPerFrame);
    drawList.AddLine(new Vector2(playheadX, origin.Y), new Vector2(playheadX, origin.Y + totalHeight), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.8f)), 2f);

    ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + totalHeight + (4 * UiScale)));
}

void DrawCanmKeyframeInspector()
{
    CANMAnimation anim = introCamera!;
    CANMTrack[] tracks = GetCanmTracks(anim);

    if (selectedCanmTrack < 0 || selectedCanmTrack >= tracks.Length || selectedCanmKeyframe < 0 || selectedCanmKeyframe >= tracks[selectedCanmTrack].Keyframes.Count)
    {
        ImGui.TextWrapped(L("Click a keyframe marker above to edit it."));
    }
    else
    {
        CANMTrack track = tracks[selectedCanmTrack];
        CANMKeyframe kf = track.Keyframes[selectedCanmKeyframe];
        ImGui.Text(LF("{0} - keyframe {1}/{2}", CanmTrackNames()[selectedCanmTrack], selectedCanmKeyframe + 1, track.Keyframes.Count));

        float frameVal = kf.Frame;
        ImGui.Text(L("Frame"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120 * UiScale);
        if (ImGui.DragFloat("##kfFrame", ref frameVal, 1f, 0f, anim.EndFrame, "%.0f"))
        {
            track.Keyframes[selectedCanmKeyframe] = track.Keyframes[selectedCanmKeyframe] with { Frame = MathF.Round(frameVal) };
        }

        ImGui.SameLine();
        float value = kf.Value;
        ImGui.Text(L("Value"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160 * UiScale);
        if (ImGui.DragFloat("##kfValue", ref value, 1f))
        {
            track.Keyframes[selectedCanmKeyframe] = track.Keyframes[selectedCanmKeyframe] with { Value = value };
        }

        if (track.Type == CANMTrackType.Ckan)
        {
            ImGui.SameLine();
            float slope = kf.InSlope;
            ImGui.Text(L("Slope"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(160 * UiScale);
            if (ImGui.DragFloat("##kfSlope", ref slope, 0.5f))
            {
                track.Keyframes[selectedCanmKeyframe] = track.Keyframes[selectedCanmKeyframe] with { InSlope = slope, OutSlope = slope };
            }
        }

        CANMKeyframe edited = track.Keyframes[selectedCanmKeyframe];
        track.Keyframes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        selectedCanmKeyframe = track.Keyframes.IndexOf(edited);

        ImGui.BeginDisabled(track.Keyframes.Count <= 1);
        if (ImGui.Button(L("Delete Keyframe")))
        {
            track.Keyframes.RemoveAt(selectedCanmKeyframe);
            selectedCanmKeyframe = -1;
        }

        ImGui.EndDisabled();
    }

    ImGui.Spacing();
    ImGui.SetNextItemWidth(160 * UiScale);
    if (ImGui.BeginCombo("##addTrackCombo", selectedCanmTrack >= 0 ? CanmTrackNames()[selectedCanmTrack] : L("Select track")))
    {
        for (int t = 0; t < tracks.Length; t++)
        {
            if (ImGui.Selectable(CanmTrackNames()[t], selectedCanmTrack == t))
            {
                selectedCanmTrack = t;
            }
        }

        ImGui.EndCombo();
    }

    ImGui.SameLine();
    ImGui.BeginDisabled(selectedCanmTrack < 0);
    if (ImGui.Button(L("Add Keyframe At Playhead")))
    {
        float roundedFrame = MathF.Round(cameraPreviewFrame);
        CANMTrack track = tracks[selectedCanmTrack];
        float value = track.Sample(roundedFrame);
        track.Keyframes.Add(new CANMKeyframe(roundedFrame, value));
        track.Keyframes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        selectedCanmKeyframe = track.Keyframes.FindIndex(k => k.Frame == roundedFrame);
    }

    ImGui.EndDisabled();
}

void DrawGalaxyHost()
{
    var displaySize = new Vector2(galaxyWindow.FramebufferSize.X, galaxyWindow.FramebufferSize.Y);
    ImGui.SetNextWindowPos(Vector2.Zero);
    ImGui.SetNextWindowSize(displaySize);

    ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
    ImGui.Begin("##GalaxyBrowser", flags);
    ImGui.Indent(20 * UiScale);
    ImGui.Dummy(new Vector2(0, 12 * UiScale));

    switch (hubScreen)
    {
        case HubScreen.GameDirsSetup:
            DrawGameDirsSetup();
            break;
        case HubScreen.ProjectPicker:
            DrawProjectPicker();
            break;
        default:
            DrawStagePicker();
            break;
    }

    ImGui.Unindent(20 * UiScale);

    DrawFileBrowser();

    ImGui.End();
}

void DrawHubSettingsPopup()
{
    ImGui.SetNextWindowSize(new Vector2(560, 460) * UiScale, ImGuiCond.Appearing);
    if (!ImGui.BeginPopupModal($"{L("Settings")}###HubSettings", ImGuiWindowFlags.NoResize))
    {
        return;
    }

    if (KeyPressedEdge(ImGuiKey.Escape) && !ImGui.IsPopupOpen("Approve plugin?##PluginConsent"))
    {
        pendingConsentPlugin = null;
        ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return;
    }

    ImGui.TextUnformatted(L("Language"));
    ImGui.SetNextItemWidth(220 * UiScale);
    string currentLangName = Loc.Available.FirstOrDefault(l => l.Code == Loc.CurrentLanguage).Name ?? Loc.CurrentLanguage;
    if (ImGui.BeginCombo("##UiLanguage", currentLangName))
    {
        foreach ((string code, string name) in Loc.Available)
        {
            if (ImGui.Selectable(name, code == Loc.CurrentLanguage) && code != Loc.CurrentLanguage)
            {
                Loc.SetLanguage(code);
                settings.UiLanguage = code;
                settings.Save(EditorSettings.DefaultPath);
            }
        }

        ImGui.EndCombo();
    }

    ImGui.TextDisabled(L("Translations live in the lang folder next to the executable."));

    ImGui.Separator();
    ImGui.TextUnformatted(L("Plugins"));
    ImGui.TextDisabled(pluginManager.PluginsDir);
    ImGui.TextWrapped(L("Plugins are ordinary programs. An approved plugin runs with your account's full access every time Supernova starts. Only approve plugins from people you trust."));
    ImGui.Separator();

    var orange = new Vector4(0.95f, 0.65f, 0.35f, 1f);
    var red = new Vector4(0.95f, 0.5f, 0.4f, 1f);

    if (pluginManager.Discovered.Count > 0)
    {
        foreach (DiscoveredPlugin discovered in pluginManager.Discovered)
        {
            ImGui.PushID(discovered.FileName);

            bool approved = discovered.Approved;
            bool scannable = discovered.Scan.Error is null && discovered.Scan.HasPluginTypes;
            string label = discovered.Loaded.Count > 0 ? discovered.Loaded[0].Info.Name : discovered.FileName;

            ImGui.BeginDisabled(!scannable && !approved);
            if (ImGui.Checkbox($"{label}###approve", ref approved))
            {
                if (approved)
                {
                    pendingConsentPlugin = discovered;
                    ImGui.OpenPopup("Approve plugin?##PluginConsent");
                }
                else
                {
                    settings.ApprovedPlugins.RemoveAll(a => string.Equals(a.FileName, discovered.FileName, StringComparison.OrdinalIgnoreCase));
                    settings.Save(EditorSettings.DefaultPath);
                    pluginManager.Rescan(settings.ApprovedPlugins);
                }
            }

            ImGui.EndDisabled();

            ImGui.Indent();
            ImGui.TextDisabled($"{discovered.FileName}  -  sha256 {(discovered.Scan.Sha256.Length >= 12 ? discovered.Scan.Sha256[..12] : "?")}");

            if (discovered.Scan.Error is { } scanError)
            {
                ImGui.TextColored(red, scanError);
            }
            else if (!discovered.Scan.HasPluginTypes)
            {
                ImGui.TextDisabled(L("No plugin found in this file."));
            }
            else if (discovered.HashChanged)
            {
                ImGui.TextColored(orange, "This file changed since it was approved. Re-approve it to use it.");
            }

            if (discovered.Scan.Capabilities.Count > 0)
            {
                ImGui.TextColored(orange, $"Uses: {string.Join(", ", discovered.Scan.Capabilities)}");
            }

            if (discovered.LoadError is { } loadError)
            {
                ImGui.TextColored(red, $"Failed to load: {loadError}");
            }

            ImGui.Unindent();
            ImGui.Spacing();
            ImGui.PopID();
        }
    }
    else
    {
        ImGui.TextWrapped(L("No plugin files in the folder. Drop a plugin's .dll into it and reopen this window."));
    }

    if (pluginManager.FolderError is { } folderError)
    {
        ImGui.Spacing();
        ImGui.TextColored(red, folderError);
    }

    ImGui.Spacing();
    if (ImGui.Button(L("Rescan folder")))
    {
        pluginManager.Rescan(settings.ApprovedPlugins);
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Close")))
    {
        ImGui.CloseCurrentPopup();
    }

    DrawPluginConsentPopup();

    ImGui.EndPopup();
}

void DrawPluginConsentPopup()
{
    ImGui.SetNextWindowSize(new Vector2(520, 0) * UiScale, ImGuiCond.Appearing);
    if (!ImGui.BeginPopupModal("Approve plugin?##PluginConsent", ImGuiWindowFlags.NoResize))
    {
        return;
    }

    if (pendingConsentPlugin is not { } plugin)
    {
        ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return;
    }

    ImGui.TextWrapped(LF("Approve \"{0}\"?", plugin.FileName));
    ImGui.Spacing();
    ImGui.TextWrapped("Once approved, this plugin's code runs inside Supernova with your account's "
        + "full access on every launch, until you remove approval here.");
    ImGui.Spacing();

    ImGui.TextDisabled(L("SHA-256"));
    ImGui.SetNextItemWidth(-1);
    string hash = plugin.Scan.Sha256;
    ImGui.InputText("##consenthash", ref hash, 128, ImGuiInputTextFlags.ReadOnly);

    ImGui.Spacing();
    if (plugin.Scan.Capabilities.Count > 0)
    {
        ImGui.TextColored(new Vector4(0.95f, 0.65f, 0.35f, 1f), "Its code references:");
        foreach (string capability in plugin.Scan.Capabilities)
        {
            ImGui.BulletText(capability);
        }
    }
    else
    {
        ImGui.TextDisabled(L("Nothing risky was flagged in a quick scan."));
    }

    ImGui.TextDisabled(L("This scan is only a hint - a plugin can do more than it lists here."));
    ImGui.Separator();

    if (ImGui.Button(L("Approve")))
    {
        settings.ApprovedPlugins.RemoveAll(a => string.Equals(a.FileName, plugin.FileName, StringComparison.OrdinalIgnoreCase));
        settings.ApprovedPlugins.Add(new ApprovedPlugin(plugin.FileName, plugin.Scan.Sha256));
        settings.Save(EditorSettings.DefaultPath);
        pluginManager.Rescan(settings.ApprovedPlugins);
        pendingConsentPlugin = null;
        ImGui.CloseCurrentPopup();
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Cancel")) || KeyPressedEdge(ImGuiKey.Escape))
    {
        pendingConsentPlugin = null;
        ImGui.CloseCurrentPopup();
    }

    ImGui.EndPopup();
}

void DrawStagePicker()
{
    DrawEditGalaxyPopup();

    if (ImGui.Button(L("< Switch Project")))
    {
        SwitchProject();
        return;
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Settings")))
    {
        ImGui.OpenPopup($"{L("Settings")}###HubSettings");
    }

    DrawHubSettingsPopup();

    ImGui.Spacing();
    ImGui.TextUnformatted(LF("Galaxies ({0})", availableGalaxies.Count));
    if (gameRootDir is not null)
    {
        ImGui.TextDisabled(gameRootDir);
    }

    ImGui.Separator();

    if (gameRootDir is null)
    {
        ImGui.TextWrapped(L("No project open."));
        return;
    }

    if (availableGalaxies.Count == 0)
    {
        ImGui.TextWrapped(L("No galaxies found under this base directory's StageData folder."));
        return;
    }

    bool anyWorldInfo = availableGalaxies.Any(g => galaxyWorlds.GetValueOrDefault(g) is not null);
    if (!anyWorldInfo)
    {
        foreach (string galaxyName in availableGalaxies)
        {
            DrawGalaxyEntry(galaxyName);
        }

        return;
    }

    var byWorld = new SortedDictionary<int, List<string>>();
    var noWorld = new List<string>();
    foreach (string galaxyName in availableGalaxies)
    {
        if (galaxyWorlds.GetValueOrDefault(galaxyName) is int world)
        {
            (byWorld.TryGetValue(world, out List<string>? list) ? list : byWorld[world] = []).Add(galaxyName);
        }
        else
        {
            noWorld.Add(galaxyName);
        }
    }

    foreach ((int world, List<string> galaxyNames) in byWorld)
    {
        if (ImGui.TreeNodeEx($"{LF("World {0} ({1})", world, galaxyNames.Count)}###world_{world}", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (string galaxyName in galaxyNames)
            {
                DrawGalaxyEntry(galaxyName);
            }

            ImGui.TreePop();
        }
    }

    if (noWorld.Count > 0 && ImGui.TreeNodeEx($"{LF("Other ({0})", noWorld.Count)}###world_other", ImGuiTreeNodeFlags.DefaultOpen))
    {
        foreach (string galaxyName in noWorld)
        {
            DrawGalaxyEntry(galaxyName);
        }

        ImGui.TreePop();
    }
}

void DrawGalaxyEntry(string galaxyName)
{
    bool isActive = session is not null && session.GalaxyName == galaxyName;
    string displayName = game == 1
        ? SMG1Text.ResolveGalaxyName(gameRootDir!, outputDir, galaxyName)
        : GalaxyText.ResolveGalaxyName(gameRootDir!, outputDir, settings.SMG2Language ?? SMG2Languages.Default, galaxyName);

    if (ImGui.Selectable(displayName, isActive))
    {
        pendingGalaxyLoadName = galaxyName;
    }

    if (displayName != galaxyName && ImGui.IsItemHovered())
    {
        ImGui.SetTooltip(galaxyName);
    }

    if (game == 2 && ImGui.BeginPopupContextItem($"##galaxyctx_{galaxyName}"))
    {
        if (ImGui.MenuItem(L("Edit...")))
        {
            OpenEditGalaxyPopup(galaxyName, displayName);
        }

        ImGui.EndPopup();
    }
}

void OpenEditGalaxyPopup(string galaxyName, string currentDisplayName)
{
    editingGalaxyName = galaxyName;
    editGalaxyNameField = currentDisplayName;
    editGalaxyWorldField = galaxyWorlds.GetValueOrDefault(galaxyName) ?? 1;
    editGalaxyError = null;
    ImGui.OpenPopup($"{L("Edit Galaxy")}###EditGalaxy");
}

void DrawEditGalaxyPopup()
{
    if (!ImGui.BeginPopupModal($"{L("Edit Galaxy")}###EditGalaxy", ImGuiWindowFlags.AlwaysAutoResize))
    {
        return;
    }

    ImGui.TextDisabled(editingGalaxyName ?? "");
    ImGui.Spacing();

    ImGui.TextUnformatted(L("Display name"));
    ImGui.SetNextItemWidth(300 * UiScale);
    ImGui.InputText("##EditGalaxyName", ref editGalaxyNameField, 256);

    ImGui.Spacing();
    ImGui.TextUnformatted(L("World"));
    ImGui.SetNextItemWidth(100 * UiScale);
    ImGui.InputInt("##EditGalaxyWorld", ref editGalaxyWorldField);

    ImGui.Spacing();
    if (editGalaxyError is not null)
    {
        ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), editGalaxyError);
        ImGui.Spacing();
    }

    if (ImGui.Button(L("Save")) && editingGalaxyName is not null)
    {
        if (gameRootDir is null || outputDir is null)
        {
            editGalaxyError = "No project open.";
        }
        else
        {
            if (game == 1)
            {
                SMG1Text.SetGalaxyName(gameRootDir, outputDir, editingGalaxyName, editGalaxyNameField);
            }
            else
            {
                string language = settings.SMG2Language ?? SMG2Languages.Default;
                GalaxyText.SetGalaxyName(gameRootDir, outputDir, language, editingGalaxyName, editGalaxyNameField);
            }

            GalaxyLoader.SetGalaxyWorld(gameRootDir, outputDir, editingGalaxyName, editGalaxyWorldField);
            galaxyWorlds[editingGalaxyName] = editGalaxyWorldField;
            editingGalaxyName = null;
            ImGui.CloseCurrentPopup();
        }
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Cancel")))
    {
        editingGalaxyName = null;
        ImGui.CloseCurrentPopup();
    }

    ImGui.EndPopup();
}

void SwitchProject()
{
    pendingCloseLevelEditorWindow = true;
    gameRootDir = null;
    game = 0;
    outputDir = null;
    availableGalaxies = [];
    availableStages = [];
    galaxyWorlds.Clear();
    activeProjectId = null;
    activeProjectName = null;
    activeProjectIconKey = null;
    session = null;

    hubScreen = HubScreen.ProjectPicker;
    pickerMode = ProjectPickerMode.List;
    UpdateGalaxyWindowTitle();
}

void OpenGameDirsSetup()
{
    formSMG1Dir = settings.SMG1BaseDir ?? "";
    formSMG2Dir = settings.SMG2BaseDir ?? "";
    formSMG2Language = settings.SMG2Language ?? SMG2Languages.Default;
    gameDirsError = null;
    hubScreen = HubScreen.GameDirsSetup;
    UpdateGalaxyWindowTitle();
}

void OpenNewProjectForm()
{
    editingProjectId = null;
    formName = "";
    formGame = settings.SMG1BaseDir is not null ? 1 : 2;
    formOutputDir = "";
    formIconKey = null;
    formError = null;
    pickerMode = ProjectPickerMode.Form;
}

void OpenEditProjectForm(ProjectEntry entry)
{
    editingProjectId = entry.Id;
    formName = entry.Name;
    formGame = entry.Game;
    formOutputDir = entry.OutputDir;
    formIconKey = entry.IconKey;
    formError = null;
    pickerMode = ProjectPickerMode.Form;
}

void OpenProjectEntry(ProjectEntry entry)
{
    gameRootDir = settings.BaseDirFor(entry.Game);
    game = entry.Game;
    outputDir = entry.OutputDir;
    activeProjectId = entry.Id;
    activeProjectName = entry.Name;
    activeProjectIconKey = entry.IconKey;
    if (gameRootDir is not null)
    {
        PopulateAvailableGalaxies(gameRootDir);
    }
    else
    {
        availableGalaxies = [];
        availableStages = [];
        galaxyWorlds.Clear();
    }

    settings.LastOpenedProjectId = entry.Id;
    settings.Save(EditorSettings.DefaultPath);

    hubScreen = HubScreen.StagePicker;
    UpdateGalaxyWindowTitle();
    ApplyProjectIcon(galaxyWindow);
}

void DrawProjectPicker()
{
    if (pickerMode == ProjectPickerMode.List)
    {
        DrawProjectPickerList();
    }
    else
    {
        DrawProjectPickerForm();
    }
}

void DrawProjectPickerList()
{
    ImGui.TextUnformatted(L("Projects"));
    ImGui.Spacing();
    ImGui.TextWrapped(L("Each project is a separate SMG1 or SMG2 mod with its own output directory - the game dump itself is shared, set once in Game Directories."));
    ImGui.Spacing();

    if (ImGui.Button(L("+ New Project"), new Vector2(180 * UiScale, 32 * UiScale)))
    {
        OpenNewProjectForm();
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Game Directories..."), new Vector2(180 * UiScale, 32 * UiScale)))
    {
        OpenGameDirsSetup();
    }

    ImGui.Spacing();
    ImGui.Spacing();

    const float thumbSize = 48f;
    foreach (ProjectEntry entry in settings.Projects.ToList())
    {
        ImGui.PushID(entry.Id);

        uint? tex = iconCache.GetOrCreate(galaxyGl!, entry.IconKey);
        if (tex is { } texHandle)
        {
            ImGui.Image((IntPtr)texHandle, new Vector2(thumbSize, thumbSize) * UiScale);
        }
        else
        {
            ImGui.Dummy(new Vector2(thumbSize, thumbSize) * UiScale);
        }

        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted(entry.Name);
        ImGui.TextDisabled($"SMG{entry.Game}");
        ImGui.EndGroup();

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - (300 * UiScale) + ImGui.GetCursorPosX());
        if (ImGui.Button(L("Open"), new Vector2(80 * UiScale, 0)))
        {
            OpenProjectEntry(entry);
        }

        ImGui.SameLine();
        if (ImGui.Button(L("Edit"), new Vector2(80 * UiScale, 0)))
        {
            OpenEditProjectForm(entry);
        }

        ImGui.SameLine();
        if (ImGui.Button(L("Remove"), new Vector2(80 * UiScale, 0)))
        {
            ImGui.OpenPopup("RemoveProjectConfirm");
        }

        if (ImGui.BeginPopup("RemoveProjectConfirm"))
        {
            ImGui.TextUnformatted(LF("Remove \"{0}\" from the list?", entry.Name));
            ImGui.TextDisabled(L("This only forgets the project - no files are deleted."));
            if (ImGui.Button(L("Remove"), new Vector2(100 * UiScale, 0)))
            {
                settings.Projects.Remove(entry);
                if (activeProjectId == entry.Id)
                {
                    activeProjectId = null;
                    activeProjectName = null;
                    activeProjectIconKey = null;
                }

                settings.Save(EditorSettings.DefaultPath);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button(L("Cancel"), new Vector2(100 * UiScale, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.PopID();
    }

    if (settings.Projects.Count == 0)
    {
        ImGui.TextDisabled(L("No projects yet - click \"New Project\" to set one up."));
    }
}

void DrawProjectPickerForm()
{
    float fieldWidth = 480 * UiScale;

    ImGui.TextUnformatted(editingProjectId is null ? "New Project" : "Edit Project");
    ImGui.Spacing();

    ImGui.TextUnformatted(L("Name"));
    ImGui.SetNextItemWidth(fieldWidth);
    ImGui.InputText("##ProjectName", ref formName, 128);

    ImGui.Spacing();
    ImGui.TextUnformatted(L("Game"));
    for (int g = 1; g <= 2; g++)
    {
        if (g == 2)
        {
            ImGui.SameLine();
        }

        bool selected = formGame == g;
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? new Vector4(0.3f, 0.5f, 0.9f, 1f) : new Vector4(0.2f, 0.2f, 0.2f, 1f));
        if (ImGui.Button($"Super Mario Galaxy {g}", new Vector2(220 * UiScale, 32 * UiScale)))
        {
            formGame = g;
        }

        ImGui.PopStyleColor();
    }

    if (settings.BaseDirFor(formGame) is null)
    {
        ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), $"SMG{formGame}'s directory isn't set yet.");
        ImGui.SameLine();
        if (ImGui.Button($"{L("Set it now...")}##FromForm"))
        {
            OpenGameDirsSetup();
            return;
        }
    }
    else
    {
        ImGui.TextDisabled(settings.BaseDirFor(formGame));
    }

    ImGui.Spacing();
    ImGui.TextUnformatted(L("Output directory (edits are saved here, never into the base directory)"));
    ImGui.SetNextItemWidth(fieldWidth);
    ImGui.InputText("##OutputDirText", ref formOutputDir, 512, ImGuiInputTextFlags.ReadOnly);
    ImGui.SameLine();
    if (ImGui.Button($"{L("Browse...")}##Output"))
    {
        pendingBrowse = BrowseTarget.OutputDir;
        fileBrowser.OpenFolder(formOutputDir);
    }

    ImGui.Spacing();
    ImGui.TextUnformatted(L("Icon"));
    DrawIconPicker();

    ImGui.Spacing();
    ImGui.Spacing();
    if (formError is not null)
    {
        ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), formError);
        ImGui.Spacing();
    }

    if (ImGui.Button(L("Save and Open"), new Vector2(160 * UiScale, 32 * UiScale)))
    {
        TrySaveAndOpenProject();
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Cancel"), new Vector2(160 * UiScale, 32 * UiScale)))
    {
        pickerMode = ProjectPickerMode.List;
    }
}

void DrawIconPicker()
{
    const float cellSize = 40f;
    float wrapWidth = ImGui.GetContentRegionAvail().X;
    float cursorStartX = ImGui.GetCursorPosX();
    float x = 0f;

    void NextCell()
    {
        x += (cellSize + 8f) * UiScale;
        if (x + cellSize * UiScale > wrapWidth)
        {
            x = 0f;
        }
        else
        {
            ImGui.SameLine(cursorStartX + x);
        }
    }

    bool noneSelected = formIconKey is null;
    ImGui.PushStyleColor(ImGuiCol.Button, noneSelected ? new Vector4(0.3f, 0.5f, 0.9f, 1f) : new Vector4(0.2f, 0.2f, 0.2f, 1f));
    if (ImGui.Button(L("None"), new Vector2(cellSize, cellSize) * UiScale))
    {
        formIconKey = null;
    }

    ImGui.PopStyleColor();

    foreach (string name in ProjectIcons.BuiltInIconNames)
    {
        NextCell();
        string key = ProjectIcons.BuiltInIconKey(name);
        uint? tex = iconCache.GetOrCreate(galaxyGl!, key);
        bool selected = formIconKey == key;

        ImGui.PushStyleColor(ImGuiCol.Button, selected ? new Vector4(0.3f, 0.5f, 0.9f, 1f) : new Vector4(0.15f, 0.15f, 0.15f, 1f));
        if (tex is { } texHandle)
        {
            if (ImGui.ImageButton($"##icon_{name}", (IntPtr)texHandle, new Vector2(cellSize - 8f, cellSize - 8f) * UiScale))
            {
                formIconKey = key;
            }
        }
        else if (ImGui.Button($"?##icon_{name}", new Vector2(cellSize, cellSize) * UiScale))
        {
            formIconKey = key;
        }

        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(name);
        }
    }

    NextCell();
    if (ImGui.Button($"{L("Browse...")}##CustomIcon", new Vector2(cellSize * 2.2f, cellSize) * UiScale))
    {
        pendingBrowse = BrowseTarget.ProjectIcon;
        fileBrowser.OpenFile("", ".png", ".jpg", ".jpeg", ".bmp");
    }

    if (formIconKey is { } previewKey)
    {
        ImGui.Spacing();
        uint? previewTex = iconCache.GetOrCreate(galaxyGl!, previewKey);
        if (previewTex is { } previewHandle)
        {
            ImGui.Image((IntPtr)previewHandle, new Vector2(64, 64) * UiScale);
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Couldn't load that icon.");
        }
    }
}

void TrySaveAndOpenProject()
{
    if (formName.Length == 0)
    {
        formError = "Give this project a name.";
        return;
    }

    string? baseDir = settings.BaseDirFor(formGame);
    if (baseDir is null || GalaxyLoader.DetectGame(baseDir) != formGame)
    {
        formError = $"SMG{formGame}'s directory isn't set (or no longer looks valid) - set it in Game Directories first.";
        return;
    }

    if (formOutputDir.Length == 0)
    {
        formError = "Pick an output directory for your edits.";
        return;
    }

    string normalizedOutput = Path.GetFullPath(formOutputDir).TrimEnd('\\');
    string normalizedBase = Path.GetFullPath(baseDir).TrimEnd('\\');
    if (string.Equals(normalizedBase, normalizedOutput, StringComparison.OrdinalIgnoreCase))
    {
        formError = "The output directory must be different from the base directory - edits are never written back into your retail dump.";
        return;
    }

    Directory.CreateDirectory(normalizedOutput);

    ProjectEntry? existing = editingProjectId is { } id ? settings.Projects.FirstOrDefault(p => p.Id == id) : null;
    ProjectEntry entry = existing ?? new ProjectEntry { Id = Guid.NewGuid().ToString("N"), Name = formName, Game = formGame, OutputDir = normalizedOutput };
    entry.Name = formName;
    entry.Game = formGame;
    entry.OutputDir = normalizedOutput;
    entry.IconKey = formIconKey;

    if (existing is null)
    {
        settings.Projects.Add(entry);
    }

    formError = null;
    OpenProjectEntry(entry);
}

void DrawGameDirsSetup()
{
    ImGui.TextUnformatted(L("Set up your SMG1/SMG2 directories"));
    ImGui.Spacing();
    ImGui.TextWrapped(
        "Point at your SMG1 and/or SMG2 game dump (the folder containing DATA and UPDATE) - these are "
        + "read-only and never written to, and shared by every project for that game, so you only set "
        + "them once. You only need the one(s) you actually have - leave the other blank.");
    ImGui.Spacing();
    ImGui.Spacing();

    float fieldWidth = 480 * UiScale;
    DrawGameDirField("SMG1 directory", ref formSMG1Dir, 1, fieldWidth);
    ImGui.Spacing();
    DrawGameDirField("SMG2 directory", ref formSMG2Dir, 2, fieldWidth);

    if (formSMG2Dir.Length > 0)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(L("SMG2 text language"));
        ImGui.SetNextItemWidth(240 * UiScale);
        if (ImGui.BeginCombo("##SMG2Language", formSMG2Language))
        {
            foreach (string code in SMG2Languages.Codes)
            {
                bool selected = code == formSMG2Language;
                if (ImGui.Selectable(code, selected))
                {
                    formSMG2Language = code;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextWrapped(L("Which LocalizeData folder galaxy/star names and level text are read from - pick whichever your SMG2 dump actually ships."));
    }

    ImGui.Spacing();
    ImGui.Spacing();
    if (gameDirsError is not null)
    {
        ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), gameDirsError);
        ImGui.Spacing();
    }

    if (ImGui.Button(L("Continue"), new Vector2(160 * UiScale, 32 * UiScale)))
    {
        TrySaveGameDirs();
    }

    if (settings.SMG1BaseDir is not null || settings.SMG2BaseDir is not null)
    {
        ImGui.SameLine();
        if (ImGui.Button(L("Cancel"), new Vector2(160 * UiScale, 32 * UiScale)))
        {
            formSMG1Dir = settings.SMG1BaseDir ?? "";
            formSMG2Dir = settings.SMG2BaseDir ?? "";
            formSMG2Language = settings.SMG2Language ?? SMG2Languages.Default;
            gameDirsError = null;
            hubScreen = HubScreen.ProjectPicker;
            UpdateGalaxyWindowTitle();
        }
    }
}

void DrawGameDirField(string label, ref string field, int forGame, float fieldWidth)
{
    ImGui.TextUnformatted(label);
    ImGui.SetNextItemWidth(fieldWidth);
    ImGui.InputText($"##Dir{forGame}", ref field, 512, ImGuiInputTextFlags.ReadOnly);
    ImGui.SameLine();
    if (ImGui.Button($"Browse...##Dir{forGame}"))
    {
        pendingBrowse = forGame == 1 ? BrowseTarget.GameDir1 : BrowseTarget.GameDir2;
        fileBrowser.OpenFolder(field);
    }

    if (field.Length > 0)
    {
        int? detected = GalaxyLoader.DetectGame(field);
        ImGui.TextUnformatted(detected == forGame
            ? $"Detected: Super Mario Galaxy {forGame}"
            : detected is int wrongGame
                ? $"This looks like an SMG{wrongGame} dump, not SMG{forGame} - double check the folder."
                : "This doesn't look like a valid SMG1/SMG2 dump (missing DATA\\files\\StageData or ObjectData).");
    }
}

void TrySaveGameDirs()
{
    if (formSMG1Dir.Length == 0 && formSMG2Dir.Length == 0)
    {
        gameDirsError = "Set at least one of SMG1 or SMG2's directory.";
        return;
    }

    if (formSMG1Dir.Length > 0 && GalaxyLoader.DetectGame(formSMG1Dir) != 1)
    {
        gameDirsError = "The SMG1 directory doesn't look like a valid SMG1 dump.";
        return;
    }

    if (formSMG2Dir.Length > 0 && GalaxyLoader.DetectGame(formSMG2Dir) != 2)
    {
        gameDirsError = "The SMG2 directory doesn't look like a valid SMG2 dump.";
        return;
    }

    settings.SMG1BaseDir = formSMG1Dir.Length > 0 ? Path.GetFullPath(formSMG1Dir).TrimEnd('\\') : null;
    settings.SMG2BaseDir = formSMG2Dir.Length > 0 ? Path.GetFullPath(formSMG2Dir).TrimEnd('\\') : null;
    settings.SMG2Language = formSMG2Dir.Length > 0 ? formSMG2Language : null;
    settings.Save(EditorSettings.DefaultPath);

    gameDirsError = null;
    hubScreen = HubScreen.ProjectPicker;
    UpdateGalaxyWindowTitle();
}

void DrawFileBrowser()
{
    if (pendingBrowse == BrowseTarget.None)
    {
        return;
    }

    switch (fileBrowser.Draw(UiScale))
    {
        case FileBrowser.DrawResult.Confirmed:
            string picked = fileBrowser.SelectedPath;
            switch (pendingBrowse)
            {
                case BrowseTarget.OutputDir:
                    formOutputDir = picked;
                    formError = null;
                    break;
                case BrowseTarget.GameDir1:
                    formSMG1Dir = picked;
                    gameDirsError = null;
                    break;
                case BrowseTarget.GameDir2:
                    formSMG2Dir = picked;
                    gameDirsError = null;
                    break;
                case BrowseTarget.ProjectIcon:
                    formIconKey = ProjectIcons.CustomIconKey(picked);
                    break;
            }

            pendingBrowse = BrowseTarget.None;
            break;

        case FileBrowser.DrawResult.Cancelled:
            pendingBrowse = BrowseTarget.None;
            break;
    }
}

void DrawScenarioList()
{
    if (session is null)
    {
        ImGui.TextWrapped(L("No galaxy loaded."));
        return;
    }

    if (ImGui.Button(L("Add Scenario")))
    {
        session.Scenarios.Add(EditableScenario.CreateNew(session.Scenarios));
    }

    ImGui.SameLine();
    ImGui.BeginDisabled(session.ScenarioIndex >= session.Scenarios.Count);
    if (ImGui.Button(L("Edit Scenario")))
    {
        OpenEditScenarioModal(session.Scenarios[session.ScenarioIndex]);
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Intro Camera")))
    {
        OpenCameraEditor();
    }

    ImGui.EndDisabled();

    for (int i = 0; i < session.Scenarios.Count; i++)
    {
        EditableScenario scenario = session.Scenarios[i];
        bool isSelected = i == session.ScenarioIndex;
        if (ImGui.Selectable($"{scenario.ListLabel}###scenario_{i}", isSelected) && !isSelected)
        {
            ReloadScenario(i);
        }
    }

    DrawEditScenarioModal();
}

void ReloadScenario(int scenarioIndex)
{
    if (session is null || renderer is null)
    {
        return;
    }

    try
    {
        session.LoadScenario(scenarioIndex, db, renderer);
        (Vector3 farMin, Vector3 farMax) = GalaxyLoader.ComputeSceneBounds(session.Instances, includeSky: true);
        sceneRadius = Math.Max((farMax - farMin).Length() / 2f, 1f);
        statusMessage = LF("Loaded {0} (scenario {1}): {2} object(s), {3} rendered.", session.GalaxyName, scenarioIndex, session.Objects.Count, session.Instances.Count);
    }
    catch (Exception ex)
    {
        statusMessage = LF("Failed to switch scenario: {0}", ex.Message);
    }
}

void OpenEditScenarioModal(EditableScenario target)
{
    scenarioModalTarget = target;
    scenarioModalNo = target.ScenarioNo;
    scenarioModalName = target.ScenarioName;
    for (int bit = 0; bit < scenarioModalPowerStars.Length; bit++)
    {
        scenarioModalPowerStars[bit] = (target.PowerStarId & (1 << bit)) != 0;
    }

    scenarioModalPowerStarTypeIdx = Math.Max(Array.IndexOf(powerStarTypes, target.PowerStarType), 0);
    scenarioModalCometTimer = target.CometLimitTimer;
    scenarioModalIsHidden = target.IsHidden;

    scenarioModalCometEntries = ScenarioLookupTables.CometTypes.ForGame(session!.Game).ToList();
    scenarioModalCometIdx = Math.Max(scenarioModalCometEntries.FindIndex(e => e.Key == target.Comet), 0);

    scenarioModalAppearEntries = ScenarioLookupTables.AppearPowerStarObjTypes.ForGame(session.Game).ToList();
    scenarioModalAppearIdx = Math.Max(scenarioModalAppearEntries.FindIndex(e => e.Key == target.AppearPowerStarObj), 0);

    string[] fixedScenarioFields =
        ["ScenarioNo", "ScenarioName", "PowerStarId", "AppearPowerStarObj", "PowerStarType", "Comet", "CometLimitTimer", "LuigiModeTimer", "ErrorCheck", "IsHidden"];
    scenarioModalZoneNames = target.Fields.Keys
        .Where(k => !fixedScenarioFields.Contains(k))
        .OrderBy(k => k == session!.GalaxyName ? 0 : 1)
        .ThenBy(k => k, StringComparer.Ordinal)
        .ToList();

    scenarioModalZoneLayers = new Dictionary<string, bool[]>();
    foreach (string zoneName in scenarioModalZoneNames)
    {
        int mask = target.GetLayerMask(zoneName);
        var layers = new bool[16];
        for (int bit = 0; bit < layers.Length; bit++)
        {
            layers[bit] = (mask & (1 << bit)) != 0;
        }

        scenarioModalZoneLayers[zoneName] = layers;
    }

    ScenarioBgmEntry? bgm = session.Game == 2 && gameRootDir is not null
        ? StageBgm.FindScenarioBgm(gameRootDir, outputDir, session.GalaxyName, target.ScenarioNo)
        : null;
    scenarioModalBgmIdName = bgm?.BgmIdName ?? "";
    scenarioModalBgmStartTypeIdx = bgm?.StartType ?? 0;
    scenarioModalBgmStartFrame = bgm?.StartFrame ?? 0;
    scenarioModalBgmIsPrepare = bgm?.IsPrepare ?? false;

    scenarioModalBgmGalaxyDefault = session.Game == 2 && gameRootDir is not null
        ? StageBgm.FindScenarioBgm(gameRootDir, outputDir, session.GalaxyName, 0)?.BgmIdName ?? ""
        : "";

    StageBgmChangeEntry? stageBgm = session.Game == 2 && gameRootDir is not null
        ? StageBgm.FindStageBgmChanges(gameRootDir, outputDir, session.GalaxyName)
        : null;
    for (int i = 0; i < stageBgmSlotNames.Length; i++)
    {
        stageBgmSlotNames[i] = stageBgm is not null && i < stageBgm.ChangeBgmIdNames.Count ? stageBgm.ChangeBgmIdNames[i] : "";
        stageBgmSlotStates[i] = stageBgm is not null && i < stageBgm.ChangeBgmStates.Count ? stageBgm.ChangeBgmStates[i] : -1;
    }

    stageBgmLoadedSnapshot = StageBgmSlotsSnapshot();

    ImGui.OpenPopup($"{L("Edit Scenario")}###EditScenario");
}

void DrawEditScenarioModal()
{
    ImGui.SetNextWindowSize(new Vector2(380, 0) * UiScale);
    if (!ImGui.BeginPopupModal($"{L("Edit Scenario")}###EditScenario", ImGuiWindowFlags.NoResize))
    {
        return;
    }

    if (ImGui.BeginTabBar("##scenarioModalTabs"))
    {
        if (ImGui.BeginTabItem(L("Scenario")))
        {
            DrawEditScenarioDataTab();
            ImGui.EndTabItem();
        }

        if (session!.Game == 2 && ImGui.BeginTabItem(L("Music")))
        {
            DrawEditScenarioMusicTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    ImGui.Separator();
    if (ImGui.Button(L("Save")))
    {
        CommitEditScenarioModal();
        ImGui.CloseCurrentPopup();
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Cancel")))
    {
        scenarioModalTarget = null;
        ImGui.CloseCurrentPopup();
    }

    ImGui.EndPopup();
}

void DrawEditScenarioDataTab()
{
    ImGui.InputInt(L("Scenario No"), ref scenarioModalNo);
    ImGui.InputText(L("Name"), ref scenarioModalName, 64);

    ImGui.TextUnformatted(L("Power Stars"));
    for (int bit = 0; bit < scenarioModalPowerStars.Length; bit++)
    {
        ImGui.Checkbox($"Star {bit + 1}###modalstar{bit}", ref scenarioModalPowerStars[bit]);
        if (bit % 4 != 3)
        {
            ImGui.SameLine();
        }
    }

    if (session!.Game == 2)
    {
        ImGui.Combo(L("Power Star Type"), ref scenarioModalPowerStarTypeIdx, powerStarTypes, powerStarTypes.Length);
    }

    string[] cometLabels = scenarioModalCometEntries.Select(e => e.Display).ToArray();
    ImGui.Combo(L("Comet"), ref scenarioModalCometIdx, cometLabels, cometLabels.Length);
    ImGui.InputInt(L("Comet Timer (sec)"), ref scenarioModalCometTimer);

    string[] appearLabels = scenarioModalAppearEntries.Select(e => e.Display).ToArray();
    ImGui.Combo(L("Power Star Appearance"), ref scenarioModalAppearIdx, appearLabels, appearLabels.Length);

    ImGui.Checkbox(L("Hidden (SMG1 only)"), ref scenarioModalIsHidden);

    ImGui.Separator();
    ImGui.TextUnformatted(L("Layers (per zone)"));

    foreach (string zoneName in scenarioModalZoneNames)
    {
        bool[] layers = scenarioModalZoneLayers[zoneName];
        bool anySet = Array.IndexOf(layers, true) >= 0;
        ImGuiTreeNodeFlags zoneFlags = zoneName == session.GalaxyName || anySet ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (ImGui.TreeNodeEx($"{zoneName}###modalzone_{zoneName}", zoneFlags))
        {
            for (int bit = 0; bit < layers.Length; bit++)
            {
                ImGui.Checkbox($"Layer{(char)('A' + bit)}###modallayer_{zoneName}_{bit}", ref layers[bit]);
                if (bit % 4 != 3)
                {
                    ImGui.SameLine();
                }
            }

            ImGui.TreePop();
        }
    }
}

void DrawEditScenarioMusicTab()
{
    ImGui.TextDisabled(L("Per-scenario BGM override (ScenarioBgmInfo)."));

    BgmTrackCombo("Track", ref scenarioModalBgmIdName);
    ImGui.InputText(L("BGM ID"), ref scenarioModalBgmIdName, 64);

    if (scenarioModalBgmIdName.Length == 0)
    {
        ImGui.TextDisabled(scenarioModalBgmGalaxyDefault.Length == 0
            ? "No entry for this scenario."
            : $"No entry for this scenario; galaxy default is {BgmTrackNames.Describe(scenarioModalBgmGalaxyDefault)}.");
    }
    else
    {
        string[] startTypeLabels = [L("Music Already Plays"), L("Plays Once Mario Lands")];
        ImGui.Combo(L("Start Type"), ref scenarioModalBgmStartTypeIdx, startTypeLabels, startTypeLabels.Length);
        ImGui.InputInt(L("Start Frame"), ref scenarioModalBgmStartFrame);
        ImGui.Checkbox("Is Prepare", ref scenarioModalBgmIsPrepare);

        if (ImGui.Button(L("Clear Music Override")))
        {
            scenarioModalBgmIdName = "";
        }
    }

    ImGui.Separator();
    if (ImGui.CollapsingHeader(L("Mid-stage music changes (StageBgmInfo)")))
    {
        ImGui.TextDisabled(L("Shared by every scenario in this galaxy."));
        ImGui.TextWrapped(L("State is the CBgmSettingInfo row of the currently playing track, or -1 for any."));

        for (int i = 0; i < stageBgmSlotNames.Length; i++)
        {
            ImGui.PushID($"stagebgm{i}");
            ImGui.TextUnformatted(LF("Slot {0}", i));
            BgmTrackCombo("Track", ref stageBgmSlotNames[i]);
            ImGui.InputText(L("BGM ID"), ref stageBgmSlotNames[i], 64);
            ImGui.InputInt("State", ref stageBgmSlotStates[i]);
            ImGui.PopID();
        }
    }
}

string StageBgmSlotsSnapshot()
{
    var parts = new string[stageBgmSlotNames.Length];
    for (int i = 0; i < parts.Length; i++)
    {
        parts[i] = $"{stageBgmSlotNames[i]}:{stageBgmSlotStates[i]}";
    }

    return string.Join("|", parts);
}

void BgmTrackCombo(string label, ref string bgmId)
{
    string preview = bgmId.Length == 0 ? "(none)" : BgmTrackNames.Describe(bgmId);
    if (!ImGui.BeginCombo(label, preview))
    {
        return;
    }

    if (ImGui.Selectable(L("(none)"), bgmId.Length == 0))
    {
        bgmId = "";
    }

    foreach (string name in StageBgm.KnownBgmNames)
    {
        if (ImGui.Selectable($"{BgmTrackNames.Describe(name)}###{label}_{name}", name == bgmId))
        {
            bgmId = name;
        }
    }

    ImGui.EndCombo();
}

void CommitEditScenarioModal()
{
    if (scenarioModalTarget is null || session is null)
    {
        return;
    }

    EditableScenario target = scenarioModalTarget;
    target.ScenarioNo = scenarioModalNo;
    target.ScenarioName = scenarioModalName;
    int powerStarMask = 0;
    for (int bit = 0; bit < scenarioModalPowerStars.Length; bit++)
    {
        if (scenarioModalPowerStars[bit])
        {
            powerStarMask |= 1 << bit;
        }
    }

    target.PowerStarId = powerStarMask;
    target.PowerStarType = powerStarTypes[scenarioModalPowerStarTypeIdx];
    target.Comet = scenarioModalCometEntries[scenarioModalCometIdx].Key;
    target.CometLimitTimer = scenarioModalCometTimer;
    target.AppearPowerStarObj = scenarioModalAppearEntries[scenarioModalAppearIdx].Key;
    target.IsHidden = scenarioModalIsHidden;

    foreach ((string zoneName, bool[] layers) in scenarioModalZoneLayers)
    {
        int mask = 0;
        for (int bit = 0; bit < layers.Length; bit++)
        {
            if (layers[bit])
            {
                mask |= 1 << bit;
            }
        }

        target.SetLayerMask(zoneName, mask);
    }

    if (session.Game == 2 && gameRootDir is not null && outputDir is not null)
    {
        if (scenarioModalBgmIdName.Length == 0)
        {
            StageBgm.RemoveScenarioBgm(gameRootDir, outputDir, session.GalaxyName, target.ScenarioNo);
        }
        else
        {
            StageBgm.SetScenarioBgm(gameRootDir, outputDir, new ScenarioBgmEntry(
                session.GalaxyName, target.ScenarioNo, scenarioModalBgmIdName,
                scenarioModalBgmStartTypeIdx, scenarioModalBgmStartFrame, scenarioModalBgmIsPrepare));
        }

        if (StageBgmSlotsSnapshot() != stageBgmLoadedSnapshot)
        {
            StageBgm.SetStageBgmChanges(gameRootDir, outputDir, session.GalaxyName, stageBgmSlotNames, stageBgmSlotStates);
        }
    }

    int index = session.Scenarios.IndexOf(target);
    if (index == session.ScenarioIndex && renderer is not null)
    {
        ReloadScenario(index);
    }

    scenarioModalTarget = null;
}

void TrackVector3FieldEdit(Vector3 before, Func<Vector3> getCurrent, Action<Vector3> apply)
{
    if (ImGui.IsItemActivated())
    {
        pendingVector3EditBefore = before;
    }

    if (ImGui.IsItemDeactivatedAfterEdit() && pendingVector3EditBefore is { } capturedBefore)
    {
        Vector3 after = getCurrent();
        if (capturedBefore != after)
        {
            session!.History.Push(() => apply(capturedBefore), () => apply(after));
        }

        pendingVector3EditBefore = null;
    }
}

void TrackObjectGizmoDrag(EditableObject obj)
{
    bool draggingNow = viewportGizmo.IsDragging;
    if (draggingNow && !gizmoDragTrackingActive)
    {
        gizmoDragBeforePosition = obj.Position;
        gizmoDragBeforeRotation = obj.Rotation;
    }
    else if (!draggingNow && gizmoDragTrackingActive)
    {
        Vector3 beforePos = gizmoDragBeforePosition;
        Vector3 beforeRot = gizmoDragBeforeRotation;
        Vector3 afterPos = obj.Position;
        Vector3 afterRot = obj.Rotation;
        if (beforePos != afterPos || beforeRot != afterRot)
        {
            session!.History.Push(
                () => { obj.Position = beforePos; obj.Rotation = beforeRot; obj.SyncTransformToInstance(); },
                () => { obj.Position = afterPos; obj.Rotation = afterRot; obj.SyncTransformToInstance(); });
        }
    }

    gizmoDragTrackingActive = draggingNow;
}

void TrackPathPointGizmoDrag(EditablePath path, PathPoint point)
{
    bool draggingNow = viewportGizmo.IsDragging;
    if (draggingNow && !gizmoDragTrackingActive)
    {
        gizmoDragBeforePosition = point.Position;
        gizmoDragBeforeControlIn = point.ControlPointIn;
        gizmoDragBeforeControlOut = point.ControlPointOut;
    }
    else if (!draggingNow && gizmoDragTrackingActive)
    {
        Vector3 beforePos = gizmoDragBeforePosition;
        Vector3 beforeIn = gizmoDragBeforeControlIn;
        Vector3 beforeOut = gizmoDragBeforeControlOut;
        Vector3 afterPos = point.Position;
        Vector3 afterIn = point.ControlPointIn;
        Vector3 afterOut = point.ControlPointOut;
        if (beforePos != afterPos || beforeIn != afterIn || beforeOut != afterOut)
        {
            session!.History.Push(
                () => { point.Position = beforePos; point.ControlPointIn = beforeIn; point.ControlPointOut = beforeOut; path.RecomputePolyline(); },
                () => { point.Position = afterPos; point.ControlPointIn = afterIn; point.ControlPointOut = afterOut; path.RecomputePolyline(); });
        }
    }

    gizmoDragTrackingActive = draggingNow;
}

void PushRemoveObjectUndo(EditableObject obj)
{
    int index = session!.Objects.IndexOf(obj);
    List<ObjectInstance> instances = obj.AllInstances.ToList();
    session.History.Push(
        () =>
        {
            session.Objects.Insert(index, obj);
            foreach (ObjectInstance instance in instances)
            {
                session.Instances.Add(instance);
            }

            session.Selected = obj;
        },
        () => RemoveObject(obj));
}

void RemoveObject(EditableObject obj)
{
    if (session is null)
    {
        return;
    }

    if (ReferenceEquals(session.Selected, obj))
    {
        session.Selected = null;
    }

    if (ReferenceEquals(pendingPlacement, obj))
    {
        pendingPlacement = null;
    }

    foreach (ObjectInstance instance in obj.AllInstances)
    {
        session.Instances.Remove(instance);
    }

    session.Objects.Remove(obj);
}

void DuplicateObject(EditableObject source)
{
    if (session is null)
    {
        return;
    }

    var cloneFields = new Dictionary<string, object?>(source.Fields);
    if (cloneFields.ContainsKey("l_id"))
    {
        cloneFields["l_id"] = NextPlacementId(source.StagePath, source.SourceList, "l_id");
    }
    else if (cloneFields.ContainsKey("MarioNo"))
    {
        cloneFields["MarioNo"] = NextPlacementId(source.StagePath, source.SourceList, "MarioNo");
    }

    var clone = new EditableObject
    {
        InternalName = source.InternalName,
        Layer = source.Layer,
        Position = source.Position,
        Rotation = source.Rotation,
        Scale = source.Scale,
        Fields = cloneFields,
        SourceList = source.SourceList,
        StagePath = source.StagePath,
        DbEntry = source.DbEntry,
        DbClass = source.DbClass,
    };

    if (source.Instance is { } sourceInstance)
    {
        var instance = new ObjectInstance { Object = sourceInstance.Object, WorldMatrix = sourceInstance.WorldMatrix };
        clone.Instance = instance;
        session.Instances.Add(instance);
    }

    session.Objects.Add(clone);
    session.Selected = null;
    deleteClickMode = false;
    copyClickMode = false;
    pendingPlacement = clone;
    statusMessage = LF("Placing a copy of {0} - click a surface to place it, Esc to cancel.", clone.DisplayName);
}

void DeleteSelectedOrEnterClickMode()
{
    if (session is null)
    {
        return;
    }

    if (session.SelectedPath is { } selectedPath && session.SelectedPathPointIndex is int selectedPointIndex
        && selectedPointIndex >= 0 && selectedPointIndex < selectedPath.WorldPoints.Count)
    {
        DeleteSelectedPathPoint(selectedPath, selectedPointIndex);
        return;
    }

    if (session.Selected is { } toDelete)
    {
        string deletedName = toDelete.DisplayName;
        PushRemoveObjectUndo(toDelete);
        RemoveObject(toDelete);
        statusMessage = LF("Deleted {0}.", deletedName);
    }
    else
    {
        deleteClickMode = true;
        copyClickMode = false;
        statusMessage = L("Click an object or path point in the viewport to delete it (hold Shift to delete more than one). Press Esc to stop.");
    }
}

void DeleteSelectedPathPoint(EditablePath path, int index)
{
    if (session is null)
    {
        return;
    }

    if (path.WorldPoints.Count <= 2)
    {
        statusMessage = L("A path needs at least 2 points.");
        return;
    }

    PathPoint point = path.WorldPoints[index];
    path.WorldPoints.RemoveAt(index);
    path.RecomputePolyline();
    session.SelectedPath = path;
    session.SelectedPathPointIndex = null;
    session.SelectedPathPointPart = PathPointPart.Anchor;
    statusMessage = LF("Deleted point #{0}.", index);

    session.History.Push(
        () =>
        {
            int at = Math.Clamp(index, 0, path.WorldPoints.Count);
            path.WorldPoints.Insert(at, point);
            path.RecomputePolyline();
            session.SelectedPath = path;
            session.SelectedPathPointIndex = at;
        },
        () =>
        {
            int at = path.WorldPoints.IndexOf(point);
            if (at >= 0)
            {
                path.WorldPoints.RemoveAt(at);
                path.RecomputePolyline();
            }

            if (ReferenceEquals(session.SelectedPath, path))
            {
                session.SelectedPathPointIndex = null;
            }
        });
}

void OpenAddObjectsPopup(AddKind kind)
{
    if (session is null)
    {
        return;
    }

    addObjectKind = kind;
    addObjectSearchText = "";
    addObjectSelectedEntry = null;
    addObjectSelectedLayer = "Common";
    addObjectSelectedZone = session.GalaxyName;
    deleteClickMode = false;
    copyClickMode = false;
    ImGui.OpenPopup($"{L("Add Objects")}###AddObjects");
}

void OpenAddZonePopup()
{
    if (session is null)
    {
        return;
    }

    addZoneSearchText = "";
    addZoneSelected = null;
    addObjectSelectedZone = session.GalaxyName;
    deleteClickMode = false;
    copyClickMode = false;
    ImGui.OpenPopup($"{L("Add Zone")}###AddZone");
}

void DrawAddDeleteCopyButtons()
{
    if (session is null)
    {
        return;
    }

    if (pendingOpenAddObjectsPopup)
    {
        pendingOpenAddObjectsPopup = false;
        OpenAddObjectsPopup(addObjectKind);
    }

    if (pendingOpenAddZonePopup)
    {
        pendingOpenAddZonePopup = false;
        OpenAddZonePopup();
    }

    if (pendingOpenAddGeneralPosPopup)
    {
        pendingOpenAddGeneralPosPopup = false;
        addGeneralPosSearchText = "";
        addGeneralPosSelected = null;
        addObjectSelectedLayer = "Common";
        addObjectSelectedZone = session.GalaxyName;
        deleteClickMode = false;
        copyClickMode = false;
        ImGui.OpenPopup($"{L("Add General Position")}###AddGeneralPosition");
    }

    ImGui.BeginDisabled(pendingPlacement is not null || pendingPath is not null || pendingPathPointInsert is not null);

    if (ImGui.Button(L("Add")))
    {
        ImGui.OpenPopup("##AddKindMenu");
    }

    ImGui.SameLine();

    if (ImGui.Button(L("Delete")))
    {
        DeleteSelectedOrEnterClickMode();
    }

    ImGui.SameLine();

    if (ImGui.Button(L("Copy")))
    {
        if (session.Selected is { } toCopy)
        {
            DuplicateObject(toCopy);
        }
        else
        {
            deleteClickMode = false;
            copyClickMode = true;
            statusMessage = L("Click an object in the viewport to duplicate it. Press Esc to stop.");
        }
    }

    ImGui.EndDisabled();

    if (ImGui.BeginPopup("##AddKindMenu"))
    {
        if (ImGui.MenuItem(L("Object")))
        {
            pendingOpenAddObjectsPopup = true;
            addObjectKind = AddKind.Object;
        }

        if (ImGui.MenuItem(L("Area")))
        {
            pendingOpenAddObjectsPopup = true;
            addObjectKind = AddKind.Area;
        }

        if (ImGui.MenuItem(L("Camera Area")))
        {
            pendingOpenAddObjectsPopup = true;
            addObjectKind = AddKind.CameraArea;
        }

        if (ImGui.MenuItem(L("Gravity")))
        {
            pendingOpenAddObjectsPopup = true;
            addObjectKind = AddKind.Gravity;
        }

        ImGui.Separator();
        if (ImGui.MenuItem(L("Path")))
        {
            BeginAddPath();
        }

        if (ImGui.MenuItem(L("Path Point")))
        {
            BeginAddPathPoint();
        }

        ImGui.Separator();
        if (ImGui.MenuItem(L("Starting Point")))
        {
            BeginAddStartingPointPlacement();
        }

        if (ImGui.MenuItem(L("General Position...")))
        {
            pendingOpenAddGeneralPosPopup = true;
        }

        if (ImGui.MenuItem(L("Zone...")))
        {
            pendingOpenAddZonePopup = true;
        }

        ImGui.EndPopup();
    }

    DrawAddObjectPopup();
    DrawAddZonePopup();
    DrawAddGeneralPosPopup();
}

string AddKindLabel(AddKind kind) => kind switch
{
    AddKind.Object => L("Object"),
    AddKind.Area => L("Area"),
    AddKind.CameraArea => L("Camera Area"),
    AddKind.Gravity => L("Gravity"),
    _ => L("Object"),
};

bool AddKindMatches(ObjectDbEntry entry)
{
    string? list = entry.ListName(session!.Game);
    return addObjectKind switch
    {
        AddKind.Object => list is "ObjInfo" or "MapPartsInfo" or "DemoObjInfo",
        AddKind.Area => list == "AreaObjInfo",
        AddKind.CameraArea => list == "CameraCubeInfo",
        AddKind.Gravity => list == "PlanetObjInfo",
        _ => true,
    };
}

void DrawAddObjectPopup()
{
    ImGui.SetNextWindowSize(new Vector2(760, 560) * UiScale, ImGuiCond.Appearing);
    if (!ImGui.BeginPopupModal($"{L("Add Objects")}###AddObjects", ImGuiWindowFlags.NoResize))
    {
        return;
    }

    if (KeyPressedEdge(ImGuiKey.Escape))
    {
        ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return;
    }

    if (session is null || renderer is null)
    {
        ImGui.TextWrapped(L("No galaxy loaded."));
        if (ImGui.Button(L("Close")))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        return;
    }

    ImGui.TextUnformatted(LF("Add {0}", AddKindLabel(addObjectKind)));
    ImGui.Separator();

    ImGui.TextUnformatted(L("Search:"));
    ImGui.SameLine();
    ImGui.SetNextItemWidth(280 * UiScale);
    ImGui.InputText("##AddObjectSearch", ref addObjectSearchText, 128);

    List<ObjectDbEntry> matches = db.ObjectsByInternalName.Values
        .Where(AddKindMatches)
        .Where(e => addObjectSearchText.Length == 0 ||
            e.InternalName.Contains(addObjectSearchText, StringComparison.OrdinalIgnoreCase) ||
            e.Name.Contains(addObjectSearchText, StringComparison.OrdinalIgnoreCase))
        .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    ObjectDbEntry? confirmedEntry = null;

    ImGui.BeginChild("##AddObjectList", new Vector2(300 * UiScale, 380 * UiScale), ImGuiChildFlags.Borders);
    foreach (ObjectDbEntry entry in matches)
    {
        bool isSelected = ReferenceEquals(addObjectSelectedEntry, entry);
        if (ImGui.Selectable($"{entry.Name} ({entry.InternalName})###addobj_{entry.InternalName}", isSelected))
        {
            addObjectSelectedEntry = entry;
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            addObjectSelectedEntry = entry;
            confirmedEntry = entry;
        }
    }

    ImGui.EndChild();

    ImGui.SameLine();

    ImGui.BeginChild("##AddObjectDetails", new Vector2(0, 380 * UiScale), ImGuiChildFlags.Borders);
    if (addObjectSelectedEntry is { } selectedEntry)
    {
        ImGui.TextWrapped(selectedEntry.Name);
        ImGui.TextDisabled(selectedEntry.InternalName);
        ImGui.Separator();

        if (!string.IsNullOrEmpty(selectedEntry.Notes))
        {
            ImGui.TextWrapped(selectedEntry.Notes);
            ImGui.Separator();
        }

        ObjectDbClass? dbClass = db.FindClass(selectedEntry.ClassName(session.Game));
        if (dbClass is not null && dbClass.Parameters.Count > 0)
        {
            ImGui.TextUnformatted(L("Parameters"));
            foreach ((string key, ObjectDbParameter param) in dbClass.Parameters)
            {
                string label = string.IsNullOrEmpty(param.Name) ? key : $"{key} - {param.Name}";
                ImGui.TextWrapped(label);
                if (!string.IsNullOrEmpty(param.Description))
                {
                    ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), param.Description);
                    ImGui.PopTextWrapPos();
                }
            }
        }
        else
        {
            ImGui.TextDisabled(L("No documented parameters."));
        }
    }
    else
    {
        ImGui.TextDisabled(L("Select an object to see its details."));
    }

    ImGui.EndChild();

    ImGui.Separator();
    ImGui.TextUnformatted(L("Layer:"));
    ImGui.SameLine();
    ImGui.SetNextItemWidth(160 * UiScale);
    if (ImGui.BeginCombo("##AddObjectLayer", addObjectSelectedLayer))
    {
        foreach (string layerName in new[] { "Common" }.Concat(ScenarioLayers.LayerDirNames))
        {
            if (ImGui.Selectable(layerName, layerName == addObjectSelectedLayer))
            {
                addObjectSelectedLayer = layerName;
            }
        }

        ImGui.EndCombo();
    }

    List<string> zoneStagePaths = [session.GalaxyName];
    zoneStagePaths.AddRange(session.Objects.Where(o => o.SourceList == "StageObjInfo").Select(o => $"{o.StagePath}/{o.InternalName}"));
    if (!zoneStagePaths.Contains(addObjectSelectedZone))
    {
        addObjectSelectedZone = session.GalaxyName;
    }

    string ZoneDisplayName(string stagePath) => stagePath.Contains('/') ? stagePath[(stagePath.LastIndexOf('/') + 1)..] : stagePath;

    ImGui.SameLine();
    ImGui.TextUnformatted(L("Zone:"));
    ImGui.SameLine();
    ImGui.SetNextItemWidth(220 * UiScale);
    if (ImGui.BeginCombo("##AddObjectZone", ZoneDisplayName(addObjectSelectedZone)))
    {
        foreach (string stagePath in zoneStagePaths)
        {
            if (ImGui.Selectable(ZoneDisplayName(stagePath), stagePath == addObjectSelectedZone))
            {
                addObjectSelectedZone = stagePath;
            }
        }

        ImGui.EndCombo();
    }

    string addObjectSelectedZoneName = addObjectSelectedZone.Contains('/')
        ? addObjectSelectedZone[(addObjectSelectedZone.LastIndexOf('/') + 1)..]
        : addObjectSelectedZone;

    EditableScenario currentScenario = session.Scenarios[session.ScenarioIndex];
    IReadOnlyList<string> activeLayers = ScenarioLayers.Resolve(currentScenario.Fields, addObjectSelectedZoneName);
    if (!activeLayers.Contains(addObjectSelectedLayer))
    {
        ImGui.TextColored(new Vector4(1f, 0.7f, 0.2f, 1f),
            $"{addObjectSelectedLayer} isn't active in the current scenario yet - use Edit Scenario to enable it, or this object won't appear in-game.");
    }

    ImGui.Separator();

    ImGui.BeginDisabled(addObjectSelectedEntry is null);
    if (ImGui.Button(L("OK")))
    {
        confirmedEntry = addObjectSelectedEntry;
    }

    ImGui.EndDisabled();

    ImGui.SameLine();
    if (ImGui.Button(L("Cancel")))
    {
        ImGui.CloseCurrentPopup();
    }

    if (confirmedEntry is not null)
    {
        BeginAddObjectPlacement(confirmedEntry);
        ImGui.CloseCurrentPopup();
    }

    ImGui.EndPopup();
}

void DrawAddZonePopup()
{
    ImGui.SetNextWindowSize(new Vector2(420, 480) * UiScale, ImGuiCond.Appearing);
    if (!ImGui.BeginPopupModal($"{L("Add Zone")}###AddZone", ImGuiWindowFlags.NoResize))
    {
        return;
    }

    if (session is null || KeyPressedEdge(ImGuiKey.Escape))
    {
        ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return;
    }

    ImGui.TextWrapped(L("Add another stage - a galaxy or a zone - as a sub-zone of this galaxy. Save and reload the scenario to load its contents."));
    ImGui.Separator();

    ImGui.TextUnformatted(L("Search:"));
    ImGui.SameLine();
    ImGui.SetNextItemWidth(240 * UiScale);
    ImGui.InputText("##AddZoneSearch", ref addZoneSearchText, 128);

    ImGui.RadioButton(L("All"), ref addZoneKindFilter, 0);
    ImGui.SameLine();
    ImGui.RadioButton(L("Galaxies"), ref addZoneKindFilter, 1);
    ImGui.SameLine();
    ImGui.RadioButton(L("Zones"), ref addZoneKindFilter, 2);

    var galaxySet = new HashSet<string>(availableGalaxies, StringComparer.OrdinalIgnoreCase);
    var alreadyAdded = new HashSet<string>(
        session.Objects.Where(o => o.SourceList == "StageObjInfo").Select(o => o.InternalName),
        StringComparer.OrdinalIgnoreCase);

    IEnumerable<string> source = availableStages.Count > 0 ? availableStages : availableGalaxies;

    string? confirmedZone = null;

    ImGui.BeginChild("##AddZoneList", new Vector2(0, 320 * UiScale), ImGuiChildFlags.Borders);
    foreach (string zoneName in source
        .Where(s => !string.Equals(s, session.GalaxyName, StringComparison.OrdinalIgnoreCase) && !alreadyAdded.Contains(s))
        .Where(s => addZoneSearchText.Length == 0 || s.Contains(addZoneSearchText, StringComparison.OrdinalIgnoreCase))
        .Where(s => addZoneKindFilter switch
        {
            1 => galaxySet.Contains(s),
            2 => !galaxySet.Contains(s),
            _ => true,
        })
        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
    {
        bool isSelected = zoneName == addZoneSelected;
        string tag = galaxySet.Contains(zoneName) ? L("galaxy") : L("zone");
        if (ImGui.Selectable($"{zoneName}###addzone_{zoneName}", isSelected))
        {
            addZoneSelected = zoneName;
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            confirmedZone = zoneName;
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"[{tag}]");
    }

    ImGui.EndChild();

    ImGui.Separator();

    ImGui.BeginDisabled(addZoneSelected is null);
    if (ImGui.Button(L("OK")))
    {
        confirmedZone = addZoneSelected;
    }

    ImGui.EndDisabled();

    ImGui.SameLine();
    if (ImGui.Button(L("Cancel")))
    {
        ImGui.CloseCurrentPopup();
    }

    if (confirmedZone is not null)
    {
        BeginPlacement(confirmedZone, "StageObjInfo", null, null);
        statusMessage = LF("Placing zone {0} - click to position it, then Save and reload to load its contents.", confirmedZone);
        ImGui.CloseCurrentPopup();
    }

    ImGui.EndPopup();
}

void DrawAddGeneralPosPopup()
{
    ImGui.SetNextWindowSize(new Vector2(460, 520) * UiScale, ImGuiCond.Appearing);
    if (!ImGui.BeginPopupModal($"{L("Add General Position")}###AddGeneralPosition", ImGuiWindowFlags.NoResize))
    {
        return;
    }

    if (session is null || KeyPressedEdge(ImGuiKey.Escape))
    {
        ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return;
    }

    ImGui.TextWrapped(L("A named marker (GeneralPosInfo) that cutscene and object code looks up by name. Pick a known type or type a custom name."));
    ImGui.Separator();

    ImGui.TextUnformatted(L("Search:"));
    ImGui.SameLine();
    ImGui.SetNextItemWidth(260 * UiScale);
    ImGui.InputText("##AddGeneralPosSearch", ref addGeneralPosSearchText, 128);

    string? confirmed = null;

    ImGui.BeginChild("##AddGeneralPosList", new Vector2(0, 360 * UiScale), ImGuiChildFlags.Borders);
    foreach (GeneralPosNameEntry entry in GeneralPosCatalog.Entries
        .Where(e => addGeneralPosSearchText.Length == 0
            || e.FriendlyName.Contains(addGeneralPosSearchText, StringComparison.OrdinalIgnoreCase)
            || e.RawName.Contains(addGeneralPosSearchText, StringComparison.OrdinalIgnoreCase))
        .OrderBy(e => e.FriendlyName, StringComparer.OrdinalIgnoreCase))
    {
        bool isSelected = entry.RawName == addGeneralPosSelected;
        if (ImGui.Selectable($"{entry.FriendlyName}###addgp_{entry.RawName}", isSelected))
        {
            addGeneralPosSelected = entry.RawName;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{entry.RawName}   [{entry.Games}]");
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            confirmed = entry.RawName;
        }
    }

    ImGui.EndChild();

    ImGui.Separator();

    ImGui.BeginDisabled(addGeneralPosSelected is null);
    if (ImGui.Button(L("Add")))
    {
        confirmed = addGeneralPosSelected;
    }

    ImGui.EndDisabled();

    ImGui.SameLine();
    if (ImGui.Button(L("Custom / blank")))
    {
        confirmed = addGeneralPosSearchText;
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Cancel")))
    {
        ImGui.CloseCurrentPopup();
    }

    if (confirmed is not null)
    {
        BeginAddGeneralPos(confirmed);
        ImGui.CloseCurrentPopup();
    }

    ImGui.EndPopup();
}

void BeginAddGeneralPos(string posName)
{
    if (session is null)
    {
        return;
    }

    string stagePath = string.IsNullOrEmpty(addObjectSelectedZone) ? session.GalaxyName : addObjectSelectedZone;

    var fields = new Dictionary<string, object?>
    {
        ["name"] = "GeneralPos",
        ["PosName"] = posName,
        ["Obj_ID"] = -1,
    };
    if (session.Game == 1)
    {
        fields["ChildObjId"] = -1;
    }

    var placement = new EditableObject
    {
        InternalName = "GeneralPos",
        Layer = addObjectSelectedLayer,
        Position = session.ViewCenter,
        Rotation = Vector3.Zero,
        Scale = Vector3.One,
        Fields = fields,
        SourceList = "GeneralPosInfo",
        StagePath = stagePath,
    };

    session.Objects.Add(placement);
    session.Selected = null;
    deleteClickMode = false;
    copyClickMode = false;
    pendingPlacement = placement;
    statusMessage = LF("Placing {0} - click a surface to place it, Esc to cancel.", placement.DisplayName);
}

int NextPlacementId(string stagePath, string sourceList, string idField)
{
    var used = new HashSet<int>();
    foreach (EditableObject o in session!.Objects)
    {
        bool inScope = o.StagePath == stagePath && (idField == "l_id" || o.SourceList == sourceList);
        if (inScope && o.Fields.TryGetValue(idField, out object? v) && v is int i && i >= 0)
        {
            used.Add(i);
        }
    }

    int id = 0;
    while (used.Contains(id))
    {
        id++;
    }

    return id;
}

static int? AreaShapeNoFor(string? areaShape) => areaShape switch
{
    "BaseOriginCube" => 0,
    "CenterOriginCube" => 1,
    "Sphere" => 2,
    "Cylinder" => 3,
    "Bowl" => 4,
    _ => null,
};

void BeginAddObjectPlacement(ObjectDbEntry entry)
{
    string sourceList = entry.ListName(session!.Game) ?? "ObjInfo";
    int? areaShapeNo = sourceList is "AreaObjInfo" or "CameraCubeInfo" ? AreaShapeNoFor(entry.AreaShape) ?? 0 : null;
    BeginPlacement(entry.InternalName, sourceList, entry, areaShapeNo);

    if (sourceList == "AreaObjInfo")
    {
        showRegularAreas = true;
    }
    else if (sourceList == "CameraCubeInfo")
    {
        showCameraAreas = true;
    }
    else if (sourceList == "PlanetObjInfo")
    {
        showGravityAreas = true;
    }
}

void BeginAddStartingPointPlacement() => BeginPlacement("Mario", "StartInfo", null, null);

void BeginAddPath()
{
    if (session is null)
    {
        return;
    }

    string stagePath = !string.IsNullOrEmpty(addObjectSelectedZone) ? addObjectSelectedZone
        : session.SelectedPath?.StagePath
        ?? session.Selected?.StagePath
        ?? session.GalaxyName;

    int nextNo = 0;
    while (session.Paths.Any(p => p.StagePath == stagePath && p.No == nextNo))
    {
        nextNo++;
    }

    int nextLid = 0;
    while (session.Paths.Any(p => p.StagePath == stagePath && p.LinkId == nextLid))
    {
        nextLid++;
    }

    Matrix4x4 zoneMatrix = session.ZoneWorldMatrices.TryGetValue(stagePath, out Matrix4x4 zm) ? zm : Matrix4x4.Identity;

    var fields = new Dictionary<string, object?> { ["type"] = "Bezier", ["Path_ID"] = -1 };
    for (int i = 0; i < 8; i++)
    {
        fields[$"path_arg{i}"] = -1;
    }

    var path = new EditablePath
    {
        Name = "",
        Closed = false,
        Usage = "General",
        LinkId = nextLid,
        No = nextNo,
        Fields = fields,
        StagePath = stagePath,
        WorldPoints = [],
        ZoneToWorld = zoneMatrix,
        Color = PathColorPalette.ForIndex(session.Paths.Count),
    };

    session.Paths.Add(path);
    session.Selected = null;
    session.SelectedPath = path;
    session.SelectedPathPointIndex = null;
    deleteClickMode = false;
    copyClickMode = false;
    pendingPath = path;
    statusMessage = L("Placing path: click a surface to add the first point. Enter or Esc to finish.");
}

void FinishPendingPath()
{
    if (pendingPath is not { } path)
    {
        return;
    }

    pendingPath = null;

    if (path.WorldPoints.Count >= 2)
    {
        int index = session!.Paths.IndexOf(path);
        session.SelectedPath = path;
        session.SelectedPathPointIndex = null;
        statusMessage = LF("Path created with {0} points.", path.WorldPoints.Count);
        session.History.Push(
            () =>
            {
                session.Paths.Remove(path);
                if (ReferenceEquals(session.SelectedPath, path))
                {
                    session.SelectedPath = null;
                    session.SelectedPathPointIndex = null;
                }
            },
            () =>
            {
                if (!session.Paths.Contains(path))
                {
                    session.Paths.Insert(Math.Min(index, session.Paths.Count), path);
                }

                session.SelectedPath = path;
            });
    }
    else
    {
        session!.Paths.Remove(path);
        if (ReferenceEquals(session.SelectedPath, path))
        {
            session.SelectedPath = null;
        }

        statusMessage = L("Path needs at least 2 points - discarded.");
    }
}

void BeginAddPathPoint()
{
    if (session is null)
    {
        return;
    }

    if (session.SelectedPath is not { } path)
    {
        statusMessage = L("Select a path or a path point first, then choose Add > Path Point.");
        return;
    }

    int insertIndex = session.SelectedPathPointIndex is int i ? i + 1 : path.WorldPoints.Count;
    pendingPathPointInsert = (path, insertIndex);
    session.Selected = null;
    deleteClickMode = false;
    copyClickMode = false;
    string label = path.Name.Length > 0 ? path.Name : $"Path {path.No}";
    statusMessage = LF("Inserting point #{0} on {1} - click a surface, Esc to cancel.", insertIndex, label);
}

Vector3 PendingPathClickPoint(Vector2 viewportMousePos, Matrix4x4 view, Matrix4x4 projection, float vw, float vh)
{
    if (pendingPathSurfaceSnap is { } snapped)
    {
        return snapped;
    }

    Picking.Ray ray = Picking.ScreenPointToRay(viewportMousePos, new Vector2(vw, vh), view, projection);
    return Picking.RaycastScenePoint(ray, session!.Objects) ?? ray.Origin + ray.Direction * 1000f;
}

PathPoint MakeNewPathPoint(Vector3 worldPos)
{
    var fields = new Dictionary<string, object?>();
    for (int i = 0; i < 8; i++)
    {
        fields[$"point_arg{i}"] = -1;
    }

    return new PathPoint
    {
        Position = worldPos,
        ControlPointIn = worldPos,
        ControlPointOut = worldPos,
        Fields = fields,
    };
}

void CommitPathPointInsert(Vector3 worldPos)
{
    if (pendingPathPointInsert is not { } ins || session is null)
    {
        return;
    }

    pendingPathPointInsert = null;

    EditablePath path = ins.Path;
    int index = Math.Clamp(ins.InsertIndex, 0, path.WorldPoints.Count);
    PathPoint point = MakeNewPathPoint(worldPos);

    path.WorldPoints.Insert(index, point);
    path.RecomputePolyline();
    session.SelectedPath = path;
    session.SelectedPathPointIndex = index;
    session.SelectedPathPointPart = PathPointPart.Anchor;
    statusMessage = LF("Inserted point #{0}.", index);

    session.History.Push(
        () =>
        {
            int at = path.WorldPoints.IndexOf(point);
            if (at >= 0)
            {
                path.WorldPoints.RemoveAt(at);
                path.RecomputePolyline();
            }

            if (ReferenceEquals(session.SelectedPath, path))
            {
                session.SelectedPathPointIndex = null;
            }
        },
        () =>
        {
            int at = Math.Clamp(index, 0, path.WorldPoints.Count);
            path.WorldPoints.Insert(at, point);
            path.RecomputePolyline();
            session.SelectedPath = path;
            session.SelectedPathPointIndex = at;
        });
}

void BeginPlacement(string internalName, string sourceList, ObjectDbEntry? entry, int? areaShapeNo)
{
    if (session is null || renderer is null)
    {
        return;
    }

    if (!addedObjectModelCache.TryGetValue(internalName, out LoadedObject? model))
    {
        string objectDataDir = Path.Combine(session.GameRootDir, "DATA", "files", "ObjectData");
        model = GalaxyLoader.TryLoadObject(internalName, objectDataDir) ?? GalaxyLoader.TryLoadBtiBillboard(internalName, objectDataDir);
        if (model is not null)
        {
            renderer.UploadObject(model);
        }

        addedObjectModelCache[internalName] = model;
    }

    ObjectDbClass? dbClass = entry is null ? null : db.FindClass(entry.ClassName(session.Game));
    string stagePath = string.IsNullOrEmpty(addObjectSelectedZone) ? session.GalaxyName : addObjectSelectedZone;
    string idField = sourceList == "StartInfo" ? "MarioNo" : "l_id";

    var fields = new Dictionary<string, object?>();
    EditableObject? template = session.Objects.FirstOrDefault(o => o.SourceList == sourceList && o.StagePath == stagePath)
        ?? session.Objects.FirstOrDefault(o => o.SourceList == sourceList)
        ?? session.Objects.FirstOrDefault(o => o.SourceList == "ObjInfo");
    if (template is not null)
    {
        foreach ((string key, object? value) in template.Fields)
        {
            fields[key] = value switch
            {
                float => 0f,
                string => "",
                _ => -1,
            };
        }
    }

    fields[idField] = NextPlacementId(stagePath, sourceList, idField);
    if (areaShapeNo is int shapeNo)
    {
        fields["AreaShapeNo"] = shapeNo;
    }

    var placement = new EditableObject
    {
        InternalName = internalName,
        Layer = sourceList == "StageObjInfo" ? "Common" : addObjectSelectedLayer,
        Position = session.ViewCenter,
        Rotation = Vector3.Zero,
        Scale = Vector3.One,
        Fields = fields,
        SourceList = sourceList,
        StagePath = stagePath,
        DbEntry = entry,
        DbClass = dbClass,
    };

    if (model is not null)
    {
        var instance = new ObjectInstance { Object = model, WorldMatrix = GalaxyLoader.ComposePlacementMatrix(placement.Position, placement.Rotation, placement.Scale) };
        placement.Instance = instance;
        session.Instances.Add(instance);
    }

    session.Objects.Add(placement);
    session.Selected = null;
    deleteClickMode = false;
    copyClickMode = false;
    pendingPlacement = placement;
    statusMessage = LF("Placing {0} - click a surface to place it, Esc to cancel.", placement.DisplayName);
}

void DrawObjectTree()
{
    if (session is null)
    {
        ImGui.TextWrapped(L("No galaxy loaded."));
        return;
    }

    if (!ReferenceEquals(session.Selected, lastRevealedSelection))
    {
        lastRevealedSelection = session.Selected;
        revealZoneStagePaths.Clear();
        revealCategory = null;

        if (session.Selected is { } revealed)
        {
            revealScrollPending = true;

            string[] segments = revealed.StagePath.Split('/');
            for (int depth = 2; depth <= segments.Length; depth++)
            {
                revealZoneStagePaths.Add(string.Join('/', segments[..depth]));
            }

            if (revealed.SourceList != "StageObjInfo")
            {
                revealCategory = (revealed.StagePath, revealed.TreeGroup);
            }
        }
    }

    List<EditableObject> rootObjects = session.Objects.Where(o => o.StagePath == session.GalaxyName).ToList();

    if (ImGui.TreeNodeEx($"{session.GalaxyName}###stage_{session.GalaxyName}", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth))
    {
        DrawStageCategories(session.GalaxyName, rootObjects);
        ImGui.TreePop();
    }

    foreach (EditableObject zonePlacement in rootObjects.Where(o => o.SourceList == "StageObjInfo").OrderBy(o => o.DisplayName))
    {
        DrawZoneNode(zonePlacement);
    }

    revealScrollPending = false;
}

void DrawStageCategories(string stagePath, List<EditableObject> stageObjects)
{
    foreach (IGrouping<string, EditableObject> group in stageObjects
        .Where(o => o.SourceList != "StageObjInfo")
        .GroupBy(o => o.TreeGroup)
        .OrderBy(g => g.Key))
    {
        if (revealCategory == (stagePath, group.Key))
        {
            ImGui.SetNextItemOpen(true);
        }

        if (ImGui.TreeNode($"{group.Key} ({group.Count()})###cat_{stagePath}_{group.Key}"))
        {
            foreach (EditableObject obj in group.OrderBy(o => o.DisplayName))
            {
                bool isSelected = ReferenceEquals(session!.Selected, obj);
                string idPrefix = obj.TreeId is int id ? $"[{id}] " : "";
                if (ImGui.Selectable($"{idPrefix}{obj.DisplayName}###obj_{obj.GetHashCode()}", isSelected))
                {
                    session!.Selected = obj;
                    session.SelectedPath = null;
                    session.SelectedPathPointIndex = null;
                }

                if (isSelected && revealScrollPending)
                {
                    ImGui.SetScrollHereY();
                }
            }

            ImGui.TreePop();
        }
    }

    DrawPathsCategory(stagePath);
}

void DrawPathsCategory(string stagePath)
{
    List<EditablePath> paths = session!.Paths.Where(p => p.StagePath == stagePath).ToList();
    if (paths.Count == 0)
    {
        return;
    }

    if (ImGui.TreeNode($"{LF("Paths ({0})", paths.Count)}###paths_{stagePath}"))
    {
        foreach (EditablePath path in paths.OrderBy(p => p.No))
        {
            DrawPathNode(path);
        }

        ImGui.TreePop();
    }
}

void DrawPathNode(EditablePath path)
{
    bool isSelected = ReferenceEquals(session!.SelectedPath, path) && session.SelectedPathPointIndex is null;
    string label = path.Name.Length > 0 ? path.Name : $"Path {path.No}";
    ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
    if (isSelected)
    {
        flags |= ImGuiTreeNodeFlags.Selected;
    }

    bool open = ImGui.TreeNodeEx($"[{path.No}] {label}###path_{path.StagePath}_{path.No}", flags);
    if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
    {
        session.SelectedPath = path;
        session.SelectedPathPointIndex = null;
        session.Selected = null;
    }

    if (open)
    {
        for (int i = 0; i < path.WorldPoints.Count; i++)
        {
            bool pointSelected = ReferenceEquals(session.SelectedPath, path) && session.SelectedPathPointIndex == i;
            if (ImGui.Selectable($"{LF("Point {0}", i)}###pathpt_{path.StagePath}_{path.No}_{i}", pointSelected))
            {
                session.SelectedPath = path;
                session.SelectedPathPointIndex = i;
                session.SelectedPathPointPart = PathPointPart.Anchor;
                session.Selected = null;
            }
        }

        ImGui.TreePop();
    }
}

void DrawZoneNode(EditableObject zonePlacement)
{
    string stagePath = $"{zonePlacement.StagePath}/{zonePlacement.InternalName}";
    List<EditableObject> stageObjects = session!.Objects.Where(o => o.StagePath == stagePath).ToList();

    bool isSelected = ReferenceEquals(session.Selected, zonePlacement);
    string idPrefix = zonePlacement.TreeId is int zid ? $"[{zid}] " : "";
    ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth;
    if (isSelected)
    {
        flags |= ImGuiTreeNodeFlags.Selected;
    }

    bool visible = !hiddenZoneStagePaths.Contains(stagePath);
    if (ImGui.Checkbox($"###zonevis_{stagePath}", ref visible))
    {
        if (visible)
        {
            hiddenZoneStagePaths.Remove(stagePath);
        }
        else
        {
            hiddenZoneStagePaths.Add(stagePath);
        }
    }

    ImGui.SameLine();

    if (revealZoneStagePaths.Contains(stagePath))
    {
        ImGui.SetNextItemOpen(true);
    }

    bool open = ImGui.TreeNodeEx($"{idPrefix}{zonePlacement.DisplayName}###zone_{stagePath}", flags);
    if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
    {
        session.Selected = zonePlacement;
        session.SelectedPath = null;
        session.SelectedPathPointIndex = null;
    }

    if (isSelected && revealScrollPending)
    {
        ImGui.SetScrollHereY();
    }

    if (open)
    {
        DrawStageCategories(stagePath, stageObjects);
        foreach (EditableObject childZone in stageObjects.Where(o => o.SourceList == "StageObjInfo").OrderBy(o => o.DisplayName))
        {
            DrawZoneNode(childZone);
        }

        ImGui.TreePop();
    }
}

void SetGeneralPosName(EditableObject obj, string newName)
{
    string before = obj.Fields.TryGetValue("PosName", out object? v) && v is string s ? s : "";
    if (before == newName)
    {
        return;
    }

    obj.Fields["PosName"] = newName;
    session!.History.Push(() => obj.Fields["PosName"] = before, () => obj.Fields["PosName"] = newName);
}

void DrawParameterPanel()
{
    if (session?.SelectedPath is { } path)
    {
        if (session.SelectedPathPointIndex is int pointIndex && pointIndex >= 0 && pointIndex < path.WorldPoints.Count)
        {
            DrawPathPointParameterPanel(path, pointIndex);
        }
        else
        {
            DrawPathParameterPanel(path);
        }

        return;
    }

    EditableObject? obj = session?.Selected;
    if (obj is null)
    {
        ImGui.TextWrapped(L("No object selected."));
        return;
    }

    ImGui.Text(obj.DisplayName);
    ImGui.TextDisabled(obj.InternalName);
    if (obj.DbEntry?.Notes is { Length: > 0 } notes)
    {
        ImGui.TextWrapped(notes);
    }

    ImGui.Separator();
    ImGui.Text(L("Transform"));

    Vector3 posBefore = obj.Position;
    var pos = posBefore;
    if (ImGui.DragFloat3(L("Position"), ref pos, 10f))
    {
        obj.Position = pos;
        obj.SyncTransformToInstance();
    }

    TrackVector3FieldEdit(posBefore, () => obj.Position, v => { obj.Position = v; obj.SyncTransformToInstance(); });

    Vector3 rotBefore = obj.Rotation;
    var rot = rotBefore;
    if (ImGui.DragFloat3(L("Rotation"), ref rot, 1f))
    {
        obj.Rotation = rot;
        obj.SyncTransformToInstance();
    }

    TrackVector3FieldEdit(rotBefore, () => obj.Rotation, v => { obj.Rotation = v; obj.SyncTransformToInstance(); });

    Vector3 scaleBefore = obj.Scale;
    var scale = scaleBefore;
    if (ImGui.DragFloat3(L("Scale"), ref scale, 0.05f))
    {
        obj.Scale = scale;
        obj.SyncTransformToInstance();
    }

    TrackVector3FieldEdit(scaleBefore, () => obj.Scale, v => { obj.Scale = v; obj.SyncTransformToInstance(); });

    ImGui.Separator();
    ImGui.Text(L("Parameters"));

    if (obj.DbClass is null)
    {
        DrawRawFields(obj);
    }
    else
    {
        foreach ((string key, ObjectDbParameter param) in obj.DbClass.Parameters.OrderBy(p => p.Key))
        {
            if (param.Exclusives.Contains(obj.InternalName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!obj.Fields.ContainsKey(key))
            {
                continue;
            }

            DrawParameterField(obj, key, param);
        }
    }

    if (obj.DbClass?.Parameters.ContainsKey("Message") == true && obj.Fields.TryGetValue("MessageId", out object? messageIdVal))
    {
        int messageId = messageIdVal is int mi ? mi : -1;
        ImGui.SetNextItemWidth(120 * UiScale);
        if (ImGui.InputInt("Message ID", ref messageId))
        {
            obj.Fields["MessageId"] = messageId;
        }

        if (messageId != -1 && messageId != -2)
        {
            ImGui.SameLine();
            if (ImGui.Button(L("Edit...")))
            {
                if (game == 2 && gameRootDir is not null)
                {
                    string zoneName = obj.StagePath.Split('/')[^1];
                    string language = settings.SMG2Language ?? SMG2Languages.Default;
                    flowGraphEditor.Open(gameRootDir, outputDir, language, zoneName, $"{MessageBaseName(obj)}{messageId:D3}");
                }
                else if (game == 1 && gameRootDir is not null)
                {
                    string zoneName = obj.StagePath.Split('/')[^1];
                    smg1FlowGraphEditor.Open(gameRootDir, outputDir, $"{zoneName}_{MessageBaseName(obj)}{messageId:D3}");
                }
                else
                {
                    OpenMessageEditWindow(obj, messageId);
                }
            }
        }
    }

    if (obj.SourceList == "DemoObjInfo")
    {
        ImGui.Separator();
        if (ImGui.Button(L("Open Demo Timeline")))
        {
            OpenDemoTimeline(obj);
        }
    }

    if (obj.SourceList == "GeneralPosInfo")
    {
        string posName = obj.Fields.TryGetValue("PosName", out object? pn) && pn is string pns ? pns : "";

        GeneralPosNameEntry? matched = GeneralPosCatalog.Find(posName);
        string preview = matched is not null ? matched.FriendlyName
            : posName.Length > 0 ? $"{posName} (custom)"
            : "(none)";

        ImGui.SetNextItemWidth(260 * UiScale);
        if (ImGui.BeginCombo(L("Type"), preview))
        {
            foreach (GeneralPosNameEntry entry in GeneralPosCatalog.Entries.OrderBy(e => e.FriendlyName, StringComparer.OrdinalIgnoreCase))
            {
                if (ImGui.Selectable($"{entry.FriendlyName}###gptype_{entry.RawName}", entry.RawName == posName) && entry.RawName != posName)
                {
                    SetGeneralPosName(obj, entry.RawName);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"{entry.RawName}   [{entry.Games}]");
                }
            }

            ImGui.EndCombo();
        }

        string rawEdit = posName;
        ImGui.SetNextItemWidth(260 * UiScale);
        if (ImGui.InputText("PosName", ref rawEdit, 128, ImGuiInputTextFlags.EnterReturnsTrue) && rawEdit != posName)
        {
            SetGeneralPosName(obj, rawEdit);
        }

        if (ImGui.IsItemDeactivatedAfterEdit() && rawEdit != posName)
        {
            SetGeneralPosName(obj, rawEdit);
        }

        int objId = obj.Fields.TryGetValue("Obj_ID", out object? oid) && oid is int oidVal ? oidVal : -1;
        ImGui.SetNextItemWidth(120 * UiScale);
        if (ImGui.InputInt("Obj_ID", ref objId))
        {
            int before = obj.Fields.TryGetValue("Obj_ID", out object? o2) && o2 is int b ? b : -1;
            obj.Fields["Obj_ID"] = objId;
            session!.History.Push(() => obj.Fields["Obj_ID"] = before, () => obj.Fields["Obj_ID"] = objId);
        }
    }

    if (obj.CameraParamFields is { } camFields)
    {
        ImGui.Separator();
        ImGui.Text(L("Camera Parameters"));

        foreach ((string key, object? value) in camFields.OrderBy(f => f.Key))
        {
            DrawCameraParamField(camFields, key, value);
        }

        if (camFields.TryGetValue("camtype", out object? camtypeVal) && camtypeVal as string == "CAM_TYPE_XZ_PARA")
        {
            ImGui.Separator();
            if (placingCameraTypePreviewPlayer && ReferenceEquals(cameraTypePreviewSource, obj))
            {
                ImGui.TextWrapped(L("Click a surface in the viewport to place the player..."));
                if (ImGui.Button(L("Cancel")))
                {
                    placingCameraTypePreviewPlayer = false;
                }
            }
            else if (rotatingCameraTypePreviewPlayer && ReferenceEquals(cameraTypePreviewSource, obj))
            {
                ImGui.TextWrapped(L("Move the mouse to set which way the player faces, then click to confirm."));
                if (ImGui.Button(L("Cancel")))
                {
                    rotatingCameraTypePreviewPlayer = false;
                }
            }
            else if (cameraTypePreviewActive && ReferenceEquals(cameraTypePreviewSource, obj))
            {
                ImGui.TextWrapped(L("Previewing - edit the fields above to see changes live."));
                if (ImGui.Button(L("Stop Preview")))
                {
                    cameraTypePreviewActive = false;
                }
            }
            else if (ImGui.Button(L("Preview Camera")))
            {
                placingCameraTypePreviewPlayer = true;
                cameraTypePreviewActive = false;
                cameraTypePreviewSource = obj;
            }
        }
    }

    if (session is not null)
    {
        List<ObjectLink> links = ObjectLinks.FindLinks(obj, session.Objects);
        if (links.Count > 0)
        {
            ImGui.Separator();
            ImGui.Text(L("Links"));
            ImGui.Checkbox(L("Show Arrows in Viewport"), ref showObjectLinks);

            int linkIndex = 0;
            foreach (IGrouping<EditableObject, ObjectLink> group in links.GroupBy(l => l.Target))
            {
                EditableObject target = group.Key;
                string matchDesc = string.Join(", ", group.Select(l => $"{l.SourceField}->{l.TargetField}={l.Value}"));
                if (ImGui.Selectable($"{target.DisplayName} ({matchDesc})##link{linkIndex}"))
                {
                    session.Selected = target;
                }

                linkIndex++;
            }
        }
    }
}

string MessageBaseName(EditableObject obj) => obj.DbEntry?.ClassName(game) ?? obj.InternalName;

void OpenMessageEditWindow(EditableObject obj, int messageId)
{
    string zoneName = obj.StagePath.Split('/')[^1];
    string language = settings.SMG2Language ?? SMG2Languages.Default;

    string baseName = MessageBaseName(obj);
    IReadOnlyList<(string Label, string Text)> entries = game == 1
        ? SMG1Text.ResolveObjectMessages(gameRootDir!, outputDir, zoneName, baseName, messageId)
        : ZoneText.ResolveObjectMessages(gameRootDir!, outputDir, language, zoneName, baseName, messageId);

    if (entries.Count == 0)
    {
        string fallbackLabel = game == 1 ? $"{zoneName}_{baseName}{messageId:D3}" : $"{baseName}{messageId:D3}";
        entries = [(fallbackLabel, "")];
    }

    messageEditTarget = obj;
    messageEditZoneName = zoneName;
    messageEditLabels = entries.Select(e => e.Label).ToList();
    messageEditTexts = entries.Select(e => e.Text).ToList();
    pendingOpenMessageEditWindow = true;
}

void DrawMessageEditWindow()
{
    if (pendingOpenMessageEditWindow)
    {
        pendingOpenMessageEditWindow = false;
        ImGui.OpenPopup($"{L("Edit Messages")}###EditMessages");
    }

    if (!ImGui.BeginPopupModal($"{L("Edit Messages")}###EditMessages", ImGuiWindowFlags.AlwaysAutoResize))
    {
        return;
    }

    ImGui.TextDisabled(messageEditTarget?.InternalName ?? "");
    ImGui.Spacing();

    for (int i = 0; i < messageEditLabels.Count; i++)
    {
        ImGui.TextUnformatted(messageEditLabels[i]);
        string text = messageEditTexts[i];
        if (ImGui.InputTextMultiline($"##msg{i}", ref text, 2048, new Vector2(500 * UiScale, 80 * UiScale)))
        {
            messageEditTexts[i] = text;
            SaveMessageEditEntry(i);
        }

        ImGui.Spacing();
    }

    ImGui.Spacing();
    if (ImGui.Button(L("Close")))
    {
        messageEditTarget = null;
        ImGui.CloseCurrentPopup();
    }

    ImGui.EndPopup();
}

void OpenLightEditor()
{
    if (session is null)
    {
        return;
    }

    lightEditorError = null;
    lightDirty = false;
    lightSelectedIndex = -1;
    lightSearchText = "";

    try
    {
        lightPresets = LightData.LoadPresets(session.GameRootDir, session.OutputDir, session.Game);
        lightGalaxyMap = LightData.LoadGalaxyMap(session.GameRootDir, session.OutputDir, session.Game, session.GalaxyName);
    }
    catch (Exception ex)
    {
        lightPresets = [];
        lightGalaxyMap = [];
        lightEditorError = ex.Message;
    }

    if (lightPresets.Count == 0 && lightEditorError is null)
    {
        lightEditorError = "No LightData.bcsv found for this game.";
    }

    lightGalaxyOnly = lightGalaxyMap.Count > 0;

    if (lightGalaxyMap.Count > 0)
    {
        string firstName = lightGalaxyMap[0].AreaLightName;
        lightSelectedIndex = lightPresets.FindIndex(p => p.GetValueOrDefault("AreaLightName") as string == firstName);
    }

    pendingOpenLightEditor = true;
}

void DrawLightEditorPopup()
{
    if (pendingOpenLightEditor)
    {
        pendingOpenLightEditor = false;
        ImGui.OpenPopup($"{L("Light Editor")}###LightEditor");
    }

    ImGui.SetNextWindowSize(new Vector2(920, 660) * UiScale, ImGuiCond.Appearing);
    if (!ImGui.BeginPopupModal($"{L("Light Editor")}###LightEditor", ImGuiWindowFlags.NoResize))
    {
        return;
    }

    if (session is null)
    {
        ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return;
    }

    if (lightEditorError is not null)
    {
        ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), lightEditorError);
    }

    var referenced = new HashSet<string>(lightGalaxyMap.Select(e => e.AreaLightName), StringComparer.Ordinal);

    ImGui.TextUnformatted(LF("{0} - LightID map", session.GalaxyName));
    ImGui.BeginChild("##lightmap", new Vector2(0, 92 * UiScale), ImGuiChildFlags.Borders);
    if (lightGalaxyMap.Count == 0)
    {
        ImGui.TextDisabled(L("No LightID map found for this galaxy."));
    }

    foreach (LightGalaxyMapEntry entry in lightGalaxyMap)
    {
        string idLabel = entry.LightId < 0 ? "default" : entry.LightId.ToString();
        if (ImGui.Selectable($"LightID {idLabel}  ->  {entry.AreaLightName}###lm_{entry.LightId}_{entry.AreaLightName}"))
        {
            int idx = lightPresets.FindIndex(p => p.GetValueOrDefault("AreaLightName") as string == entry.AreaLightName);
            if (idx >= 0)
            {
                lightSelectedIndex = idx;
                lightGalaxyOnly = true;
            }
        }
    }

    ImGui.EndChild();

    ImGui.Separator();

    ImGui.BeginChild("##lightlist", new Vector2(280 * UiScale, -36 * UiScale), ImGuiChildFlags.Borders);
    ImGui.Checkbox(L("This galaxy only"), ref lightGalaxyOnly);
    ImGui.TextUnformatted(L("Search"));
    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
    ImGui.InputText("##lightsearch", ref lightSearchText, 128);
    ImGui.Separator();

    for (int i = 0; i < lightPresets.Count; i++)
    {
        string name = lightPresets[i].GetValueOrDefault("AreaLightName") as string ?? "";
        if (lightGalaxyOnly && !referenced.Contains(name))
        {
            continue;
        }

        if (lightSearchText.Length > 0 && !name.Contains(lightSearchText, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (ImGui.Selectable($"{name}###lp_{i}", i == lightSelectedIndex))
        {
            lightSelectedIndex = i;
        }
    }

    ImGui.EndChild();

    ImGui.SameLine();

    ImGui.BeginChild("##lighteditor", new Vector2(0, -36 * UiScale), ImGuiChildFlags.Borders);
    if (lightSelectedIndex >= 0 && lightSelectedIndex < lightPresets.Count)
    {
        DrawLightPresetEditor(lightPresets[lightSelectedIndex]);
    }
    else
    {
        ImGui.TextDisabled(L("Select a light preset from the list."));
    }

    ImGui.EndChild();

    ImGui.Separator();
    ImGui.BeginDisabled(!lightDirty || session.OutputDir is null);
    if (ImGui.Button(L("Save")))
    {
        try
        {
            LightData.SavePresets(session.GameRootDir, session.OutputDir!, session.Game, lightPresets);
            lightDirty = false;
            statusMessage = L("Saved light data.");
            ImGui.CloseCurrentPopup();
        }
        catch (Exception ex)
        {
            lightEditorError = ex.Message;
        }
    }

    ImGui.EndDisabled();

    ImGui.SameLine();
    if (ImGui.Button(L("Close")))
    {
        ImGui.CloseCurrentPopup();
    }

    ImGui.EndPopup();
}

void DrawLightPresetEditor(Dictionary<string, object?> row)
{
    ImGui.TextUnformatted(row.GetValueOrDefault("AreaLightName") as string ?? "");
    ImGui.Separator();

    int interpolate = LightInt(row, "Interpolate");
    ImGui.SetNextItemWidth(120 * UiScale);
    if (ImGui.InputInt("Interpolate", ref interpolate))
    {
        row["Interpolate"] = interpolate;
        lightDirty = true;
    }

    foreach (string group in LightData.Groups)
    {
        ImGuiTreeNodeFlags flags = group == "Player" ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (!ImGui.CollapsingHeader($"{group}###lightgrp_{group}", flags))
        {
            continue;
        }

        ImGui.PushID(group);

        for (int n = 0; n < 2; n++)
        {
            ImGui.PushID(n);
            ImGui.TextDisabled(LF("Light {0}", n));

            var direction = new Vector3(
                LightFloat(row, $"{group}Light{n}PosX"),
                LightFloat(row, $"{group}Light{n}PosY"),
                LightFloat(row, $"{group}Light{n}PosZ"));
            if (ImGui.DragFloat3(L("Position"), ref direction, 100f))
            {
                row[$"{group}Light{n}PosX"] = direction.X;
                row[$"{group}Light{n}PosY"] = direction.Y;
                row[$"{group}Light{n}PosZ"] = direction.Z;
                lightDirty = true;
            }

            DrawLightRgba(row, $"{group}Light{n}Color", "Color");

            bool followCamera = LightInt(row, $"{group}Light{n}FollowCamera") != 0;
            if (ImGui.Checkbox(L("Follow camera"), ref followCamera))
            {
                row[$"{group}Light{n}FollowCamera"] = followCamera ? 1 : 0;
                lightDirty = true;
            }

            ImGui.PopID();
            ImGui.Spacing();
        }

        int alpha2 = LightInt(row, $"{group}Alpha2");
        ImGui.SetNextItemWidth(160 * UiScale);
        if (ImGui.SliderInt("Alpha2", ref alpha2, 0, 255))
        {
            row[$"{group}Alpha2"] = alpha2;
            lightDirty = true;
        }

        DrawLightRgba(row, $"{group}Ambient", "Ambient");

        ImGui.PopID();
    }
}

void DrawLightRgba(Dictionary<string, object?> row, string prefix, string label)
{
    int r = LightInt(row, prefix + "R");
    int g = LightInt(row, prefix + "G");
    int b = LightInt(row, prefix + "B");

    float h = ImGui.GetFrameHeight();
    ImGui.ColorButton($"##sw_{prefix}", new Vector4(r / 255f, g / 255f, b / 255f, 1f),
        ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop,
        new Vector2(h, h));
    if (ImGui.IsItemHovered())
    {
        ImGui.SetTooltip(L("Rough RGB preview only - these are added into the model's lighting, not an emitted colour"));
    }

    ImGui.SameLine();
    LightByteField("R", row, prefix + "R");
    ImGui.SameLine();
    LightByteField("G", row, prefix + "G");
    ImGui.SameLine();
    LightByteField("B", row, prefix + "B");
    ImGui.SameLine();
    LightByteField("A", row, prefix + "A");
    ImGui.SameLine();
    ImGui.TextUnformatted(label);
}

void LightByteField(string channel, Dictionary<string, object?> row, string key)
{
    int v = LightInt(row, key);
    ImGui.SetNextItemWidth(46 * UiScale);
    if (ImGui.DragInt($"{channel}##{key}", ref v, 1f, 0, 255))
    {
        row[key] = Math.Clamp(v, 0, 255);
        lightDirty = true;
    }
}

static int LightInt(Dictionary<string, object?> row, string key) =>
    row.TryGetValue(key, out object? v) && v is int i ? i : 0;

static float LightFloat(Dictionary<string, object?> row, string key) =>
    row.TryGetValue(key, out object? v) && v is float f ? f : 0f;

void OpenProductMapObjEditor()
{
    if (session is null)
    {
        return;
    }

    mapObjError = null;
    mapObjDirty = false;
    mapObjSearch = "";
    mapObjClassFilter = "";

    try
    {
        mapObjRows = ProductMapObjTable.Load(session.GameRootDir, session.OutputDir)
            .Select(e => new MapObjTableRow { ModelName = e.ModelName, ClassName = e.ClassName })
            .ToList();
    }
    catch (Exception ex)
    {
        mapObjRows = [];
        mapObjError = ex.Message;
    }

    if (mapObjRows.Count == 0 && mapObjError is null)
    {
        mapObjError = "ProductMapObjDataTable.arc not found for this game.";
    }

    pendingOpenMapObjEditor = true;
}

void DrawProductMapObjEditorPopup()
{
    if (pendingOpenMapObjEditor)
    {
        pendingOpenMapObjEditor = false;
        ImGui.OpenPopup($"{L("Map Object Class Table")}###MapObjectClassTable");
    }

    ImGui.SetNextWindowSize(new Vector2(820, 640) * UiScale, ImGuiCond.Appearing);
    if (!ImGui.BeginPopupModal($"{L("Map Object Class Table")}###MapObjectClassTable", ImGuiWindowFlags.NoResize))
    {
        return;
    }

    if (session is null)
    {
        ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return;
    }

    if (mapObjError is not null)
    {
        ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), mapObjError);
    }

    ImGui.TextWrapped(L("Assigns an object (its name and model in ObjectData) to a built-in class. Add a row to register a custom object with a class."));

    ImGui.TextUnformatted(L("Search"));
    ImGui.SameLine();
    ImGui.SetNextItemWidth(220 * UiScale);
    ImGui.InputText("##mapobjsearch", ref mapObjSearch, 128);
    ImGui.SameLine();
    ImGui.SetNextItemWidth(220 * UiScale);
    if (ImGui.BeginCombo("##mapobjclassfilter", mapObjClassFilter.Length == 0 ? "all classes" : mapObjClassFilter))
    {
        if (ImGui.Selectable(L("all classes"), mapObjClassFilter.Length == 0))
        {
            mapObjClassFilter = "";
        }

        foreach (string cls in ProductMapObjTable.KnownClasses)
        {
            if (ImGui.Selectable(cls, cls == mapObjClassFilter))
            {
                mapObjClassFilter = cls;
            }
        }

        ImGui.EndCombo();
    }

    ImGui.SameLine();
    if (ImGui.Button(L("Add Row")))
    {
        mapObjRows.Insert(0, new MapObjTableRow { ClassName = mapObjClassFilter.Length > 0 ? mapObjClassFilter : "SimpleMapObj" });
        mapObjDirty = true;
    }

    var visible = new List<int>();
    for (int i = 0; i < mapObjRows.Count; i++)
    {
        MapObjTableRow row = mapObjRows[i];
        if (mapObjSearch.Length > 0 && !row.ModelName.Contains(mapObjSearch, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (mapObjClassFilter.Length > 0 && row.ClassName != mapObjClassFilter)
        {
            continue;
        }

        visible.Add(i);
    }

    ImGui.TextDisabled(LF("{0} of {1} objects", visible.Count, mapObjRows.Count));

    int? removeAt = null;
    const ImGuiTableFlags tableFlags = ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV
        | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
    if (ImGui.BeginTable("##mapobjtable", 3, tableFlags, new Vector2(0, -36 * UiScale)))
    {
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Object", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableSetupColumn("Class", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, 28 * UiScale);
        ImGui.TableHeadersRow();

        foreach (int i in visible)
        {
            MapObjTableRow row = mapObjRows[i];
            ImGui.PushID(i);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            string model = row.ModelName;
            if (ImGui.InputText("##model", ref model, 128))
            {
                row.ModelName = model;
                mapObjDirty = true;
            }

            ImGui.TableSetColumnIndex(1);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.BeginCombo("##class", row.ClassName.Length == 0 ? "(pick class)" : row.ClassName))
            {
                foreach (string cls in ProductMapObjTable.KnownClasses)
                {
                    if (ImGui.Selectable(cls, cls == row.ClassName))
                    {
                        row.ClassName = cls;
                        mapObjDirty = true;
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.TableSetColumnIndex(2);
            if (ImGui.Button("X"))
            {
                removeAt = i;
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    if (removeAt is int idx)
    {
        mapObjRows.RemoveAt(idx);
        mapObjDirty = true;
    }

    ImGui.Separator();
    ImGui.BeginDisabled(!mapObjDirty || session.OutputDir is null);
    if (ImGui.Button(L("Save")))
    {
        try
        {
            ProductMapObjTable.Save(session.GameRootDir, session.OutputDir!,
                mapObjRows.Select(r => (r.ModelName, r.ClassName)).ToList());
            mapObjDirty = false;
            statusMessage = L("Saved ProductMapObjDataTable.");
            ImGui.CloseCurrentPopup();
        }
        catch (Exception ex)
        {
            mapObjError = ex.Message;
        }
    }

    ImGui.EndDisabled();

    ImGui.SameLine();
    if (ImGui.Button(L("Close")))
    {
        ImGui.CloseCurrentPopup();
    }

    ImGui.EndPopup();
}

void SaveMessageEditEntry(int index)
{
    if (messageEditTarget is null || gameRootDir is null || outputDir is null)
    {
        return;
    }

    string label = messageEditLabels[index];
    string text = messageEditTexts[index];

    if (game == 1)
    {
        SMG1Text.SetObjectMessage(gameRootDir, outputDir, label, text);
    }
    else
    {
        string language = settings.SMG2Language ?? SMG2Languages.Default;
        ZoneText.SetObjectMessage(gameRootDir, outputDir, language, messageEditZoneName, label, text);
    }
}

void DrawCameraParamField(Dictionary<string, object?> fields, string key, object? value)
{
    if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
    {
        ImGui.TextDisabled($"ID: {value}");
        return;
    }

    string label = CameraParamCatalog.Label(key);

    if (key.Equals("camtype", StringComparison.OrdinalIgnoreCase) && value is string currentType)
    {
        if (ImGui.BeginCombo(label, currentType))
        {
            foreach (CameraParamCatalog.CameraTypeInfo type in CameraParamCatalog.CameraTypes)
            {
                bool isSelected = type.Value == currentType;
                if (ImGui.Selectable(type.Value, isSelected))
                {
                    fields[key] = type.Value;
                }

                if (type.Description is { Length: > 0 } desc && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(desc);
                }
            }

            ImGui.EndCombo();
        }

        return;
    }

    if ((key.Equals("angleA", StringComparison.OrdinalIgnoreCase) ||
         key.Equals("angleB", StringComparison.OrdinalIgnoreCase) ||
         key.Equals("roll", StringComparison.OrdinalIgnoreCase)) && value is float radValue)
    {
        float degValue = radValue * 180f / MathF.PI;
        if (ImGui.InputFloat($"{label} (deg)", ref degValue))
        {
            fields[key] = degValue * MathF.PI / 180f;
        }

        return;
    }

    switch (value)
    {
        case float f:
        {
            float v = f;
            if (ImGui.InputFloat(label, ref v))
            {
                fields[key] = v;
            }

            break;
        }
        case int i:
        {
            int v = i;
            if (ImGui.InputInt(label, ref v))
            {
                fields[key] = v;
            }

            break;
        }
        case string s:
        {
            string v = s;
            if (ImGui.InputText(label, ref v, 128))
            {
                fields[key] = v;
            }

            break;
        }
        default:
            ImGui.Text($"{label}: {value}");
            break;
    }
}

void DrawParameterField(EditableObject obj, string key, ObjectDbParameter param)
{
    string label = param.Name ?? key;
    object? raw = obj.Fields[key];
    int intValue = raw is int i ? i : 0;

    bool changed = false;
    switch (param.Type)
    {
        case "Boolean":
        {
            bool b = intValue != 0;
            if (ImGui.Checkbox(label, ref b))
            {
                obj.Fields[key] = b ? 1 : 0;
                changed = true;
            }

            break;
        }
        case "Integer" or "Bitfield" when param.Values.Count > 0:
        {
            int current = intValue;
            string preview = param.Values.Find(v => v.Value == current.ToString())?.Notes ?? current.ToString();
            if (ImGui.BeginCombo(label, preview))
            {
                foreach (ObjectDbValueOption option in param.Values)
                {
                    bool isSelected = option.Value == current.ToString();
                    if (ImGui.Selectable($"{option.Notes} ({option.Value})", isSelected))
                    {
                        if (int.TryParse(option.Value, out int v))
                        {
                            obj.Fields[key] = v;
                            changed = true;
                        }
                    }
                }

                ImGui.EndCombo();
            }

            break;
        }
        default:
        {
            int v = intValue;
            if (ImGui.InputInt(label, ref v))
            {
                obj.Fields[key] = v;
                changed = true;
            }

            break;
        }
    }

    if (ImGui.IsItemHovered() && param.Description is { Length: > 0 })
    {
        ImGui.SetTooltip(param.Description);
    }

    if (changed)
    {
        object? before = raw;
        object? after = obj.Fields[key];
        session!.History.Push(
            () => { obj.Fields[key] = before; obj.RotateMoveSim = null; obj.RailMoveSim = null; obj.SyncTransformToInstance(); },
            () => { obj.Fields[key] = after; obj.RotateMoveSim = null; obj.RailMoveSim = null; obj.SyncTransformToInstance(); });

        obj.RotateMoveSim = null;
        obj.RailMoveSim = null;
        obj.SyncTransformToInstance();
    }
}

static void DrawRawFields(EditableObject obj)
{
    foreach ((string key, object? value) in obj.Fields.OrderBy(f => f.Key))
    {
        if (key is "name" or "pos_x" or "pos_y" or "pos_z" or "dir_x" or "dir_y" or "dir_z" or "scale_x" or "scale_y" or "scale_z")
        {
            continue;
        }

        if (obj.SourceList == "GeneralPosInfo" && key is "PosName" or "Obj_ID")
        {
            continue;
        }

        ImGui.Text($"{key}: {value}");
    }
}

void DrawPathParameterPanel(EditablePath path)
{
    ImGui.Text(path.Name.Length > 0 ? path.Name : $"Path {path.No}");
    ImGui.TextDisabled(LF("CommonPathInfo row {0} (l_id {1})", path.No, path.LinkId));

    ImGui.Separator();
    ImGui.Text(L("Path"));

    string name = path.Name;
    if (ImGui.InputText(L("Name"), ref name, 128))
    {
        path.Name = name;
    }

    bool closed = path.Closed;
    if (ImGui.Checkbox(L("Loop?"), ref closed))
    {
        bool closedBefore = path.Closed;
        bool closedAfter = closed;
        path.Closed = closedAfter;
        path.RecomputePolyline();
        session!.History.Push(
            () => { path.Closed = closedBefore; path.RecomputePolyline(); },
            () => { path.Closed = closedAfter; path.RecomputePolyline(); });
    }

    string usage = path.Usage;
    if (ImGui.InputText("Usage", ref usage, 64))
    {
        path.Usage = usage;
    }

    ImGui.TextDisabled(LF("{0} point(s)", path.WorldPoints.Count));

    ImGui.Separator();
    ImGui.Text(L("Parameters"));

    foreach ((string key, object? value) in path.Fields.OrderBy(f => f.Key))
    {
        if (key.StartsWith("path_arg", StringComparison.OrdinalIgnoreCase) && value is int argValue)
        {
            int v = argValue;
            if (ImGui.InputInt(key, ref v))
            {
                int before = argValue;
                int after = v;
                path.Fields[key] = after;
                session!.History.Push(() => path.Fields[key] = before, () => path.Fields[key] = after);
            }
        }
        else if (key is not ("name" or "l_id" or "no" or "closed" or "usage"))
        {
            ImGui.Text($"{key}: {value}");
        }
    }
}

void DrawPathPointParameterPanel(EditablePath path, int index)
{
    PathPoint point = path.WorldPoints[index];
    string pathLabel = path.Name.Length > 0 ? path.Name : $"Path {path.No}";

    ImGui.Text(LF("Point {0}", index));
    ImGui.TextDisabled(LF("On {0} - select the path itself in the Objects tree to edit its settings", pathLabel));

    if (ImGui.Button(L("Delete Point")))
    {
        DeleteSelectedPathPoint(path, index);
        return;
    }

    ImGui.Separator();
    ImGui.Text(L("Position & Handles"));

    Vector3 posBefore = point.Position;
    var pos = posBefore;
    if (ImGui.DragFloat3(L("Position"), ref pos, 10f))
    {
        point.Position = pos;
        path.RecomputePolyline();
    }

    TrackVector3FieldEdit(posBefore, () => point.Position, v => { point.Position = v; path.RecomputePolyline(); });

    Vector3 ctrlInBefore = point.ControlPointIn;
    var ctrlIn = ctrlInBefore;
    if (ImGui.DragFloat3(L("Control In"), ref ctrlIn, 10f))
    {
        point.ControlPointIn = ctrlIn;
        path.RecomputePolyline();
    }

    TrackVector3FieldEdit(ctrlInBefore, () => point.ControlPointIn, v => { point.ControlPointIn = v; path.RecomputePolyline(); });

    Vector3 ctrlOutBefore = point.ControlPointOut;
    var ctrlOut = ctrlOutBefore;
    if (ImGui.DragFloat3(L("Control Out"), ref ctrlOut, 10f))
    {
        point.ControlPointOut = ctrlOut;
        path.RecomputePolyline();
    }

    TrackVector3FieldEdit(ctrlOutBefore, () => point.ControlPointOut, v => { point.ControlPointOut = v; path.RecomputePolyline(); });

    ImGui.Separator();
    ImGui.Text(L("Parameters"));

    foreach ((string key, object? value) in point.Fields.OrderBy(f => f.Key))
    {
        if (key.StartsWith("point_arg", StringComparison.OrdinalIgnoreCase) && value is int argValue)
        {
            int v = argValue;
            if (ImGui.InputInt(key, ref v))
            {
                int before = argValue;
                int after = v;
                point.Fields[key] = after;
                session!.History.Push(() => point.Fields[key] = before, () => point.Fields[key] = after);
            }
        }
        else if (!key.StartsWith("pnt0", StringComparison.OrdinalIgnoreCase) &&
                 !key.StartsWith("pnt1", StringComparison.OrdinalIgnoreCase) &&
                 !key.StartsWith("pnt2", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.Text($"{key}: {value}");
        }
    }
}

bool IsStagePathHidden(string stagePath) =>
    hiddenZoneStagePaths.Any(hidden => stagePath == hidden || stagePath.StartsWith(hidden + "/", StringComparison.Ordinal));

bool IsObjectHidden(EditableObject obj) =>
    IsStagePathHidden(obj.StagePath) || (obj.SourceList == "StageObjInfo" && IsStagePathHidden($"{obj.StagePath}/{obj.InternalName}"));

List<EditableObject> GetVisibleObjects()
{
    if (session is null)
    {
        return [];
    }

    return hiddenZoneStagePaths.Count == 0 ? session.Objects : session.Objects.Where(o => !IsObjectHidden(o)).ToList();
}

List<EditablePath> GetVisiblePaths()
{
    if (session is null)
    {
        return [];
    }

    IEnumerable<EditablePath> unhidden = hiddenZoneStagePaths.Count == 0 ? session.Paths : session.Paths.Where(p => !IsStagePathHidden(p.StagePath));

    if (showPaths)
    {
        return unhidden.ToList();
    }

    int? selectedObjectPathLinkId = session.Selected?.Fields.TryGetValue("CommonPath_ID", out object? cpid) == true && cpid is int cpidValue && cpidValue != 65535
        ? cpidValue
        : null;

    return unhidden.Where(p => p.LinkId == selectedObjectPathLinkId).ToList();
}

List<(EditableObject Obj, SceneRenderer.AreaShapeKind Shape, Matrix4x4 World, Vector3 Color)> GetVisibleAreaShapes()
{
    var result = new List<(EditableObject, SceneRenderer.AreaShapeKind, Matrix4x4, Vector3)>();
    if (session is null)
    {
        return result;
    }

    List<EditableObject> visibleObjectsForAreas = GetVisibleObjects();

    if (showCameraAreas || showRegularAreas)
    {
        foreach (EditableObject obj in visibleObjectsForAreas)
        {
            bool isCamera = obj.SourceList == "CameraCubeInfo";
            bool isRegular = obj.SourceList == "AreaObjInfo";
            if ((!isCamera && !isRegular) || (isCamera && !showCameraAreas) || (isRegular && !showRegularAreas))
            {
                continue;
            }

            if (!obj.Fields.TryGetValue("AreaShapeNo", out object? shapeVal) || shapeVal is not int shapeNo)
            {
                continue;
            }

            var shapeKind = (SceneRenderer.AreaShapeKind)Math.Clamp(shapeNo, 0, 4);
            Vector3 shapeScale = shapeKind switch
            {
                SceneRenderer.AreaShapeKind.Sphere or SceneRenderer.AreaShapeKind.Bowl => new Vector3(obj.Scale.X),
                SceneRenderer.AreaShapeKind.Cylinder => new Vector3(obj.Scale.X, obj.Scale.Y, obj.Scale.X),
                _ => obj.Scale,
            };

            Vector3 areaColor = isCamera ? new Vector3(0.9f, 0.15f, 0.15f) : new Vector3(0.5f, 0.75f, 1f);
            Matrix4x4 shapeWorld = GalaxyLoader.ComposePlacementMatrix(obj.Position, obj.Rotation, shapeScale);
            result.Add((obj, shapeKind, shapeWorld, areaColor));
        }
    }

    if (showGravityAreas)
    {
        foreach (EditableObject obj in visibleObjectsForAreas)
        {
            if (obj.SourceList != "PlanetObjInfo")
            {
                continue;
            }

            SceneRenderer.AreaShapeKind? gravityShape = obj.InternalName switch
            {
                "GlobalPlaneGravityInBox" => SceneRenderer.AreaShapeKind.CenterOriginBox,
                "GlobalCubeGravity" => SceneRenderer.AreaShapeKind.CenterOriginBox,
                "GlobalPlaneGravity" or "GlobalPointGravity" => SceneRenderer.AreaShapeKind.Sphere,
                "GlobalPlaneGravityInCylinder" => SceneRenderer.AreaShapeKind.Cylinder,
                _ => null,
            };

            if (gravityShape is not { } kind)
            {
                continue;
            }

            Vector3 gravityScale = kind switch
            {
                SceneRenderer.AreaShapeKind.Sphere => obj.Fields.TryGetValue("Range", out object? rangeVal) && rangeVal is float range
                    ? new Vector3(range / 500f)
                    : obj.Scale,
                SceneRenderer.AreaShapeKind.Cylinder => new Vector3(obj.Scale.X, obj.Scale.Y, obj.Scale.X),
                _ => obj.Scale,
            };

            Matrix4x4 gravityWorld = GalaxyLoader.ComposePlacementMatrix(obj.Position, obj.Rotation, gravityScale);
            result.Add((obj, kind, gravityWorld, new Vector3(0.25f, 0.9f, 0.3f)));
        }
    }

    return result;
}

void DrawViewportPanel()
{
    ImGui.TextUnformatted(L("Viewport"));
    ImGui.Separator();

    ImGuiIOPtr diagIo = ImGui.GetIO();
    string diagCenter = session is null ? "-" : $"{session.ViewCenter.X:F1}, {session.ViewCenter.Y:F1}, {session.ViewCenter.Z:F1}";
    ImGui.Text($"FPS {diagIo.Framerate:F1} | dist {distance:F2} | yaw {yaw:F2} pitch {pitch:F2} | center {diagCenter} | GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} | mem {GC.GetTotalMemory(false) / 1024f / 1024f:F1}MB");

    if (rotatingCameraTypePreviewPlayer)
    {
        ImGui.TextColored(new Vector4(0.4f, 1f, 0.5f, 1f), "Move the mouse to set the player's facing, click to confirm, Esc to cancel");
    }
    else if (cameraTypePreviewActive)
    {
        ImGui.TextColored(new Vector4(0.4f, 1f, 0.5f, 1f), "Camera Preview - Left/Right arrows to pan, Down to reset, Esc to exit");
        if (cameraTypePreviewDebugText is not null)
        {
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.5f, 1f), cameraTypePreviewDebugText);
        }
    }

    Vector2 avail = ImGui.GetContentRegionAvail();
    uint vw = (uint)Math.Max(avail.X, 1);
    uint vh = (uint)Math.Max(avail.Y, 1);
    viewportFbo!.EnsureSize(vw, vh);

    Vector3 eye = default;
    Matrix4x4 view = default, projection = default;

    gizmoWasDraggingThisFrame = viewportGizmo.IsDragging;

    if (session is not null)
    {
        viewportFbo.Bind();
        gl!.ClearColor(0.15f, 0.17f, 0.2f, 1f);
        gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        if (cameraPreviewActive && introCamera is not null)
        {
            (Vector3 camEye, Vector3 camTarget, float twistDeg, float fovyDeg) = introCamera.Sample(cameraPreviewFrame);
            eye = camEye;

            Vector3 forward = camTarget - camEye;
            Vector3 up = Vector3.UnitY;
            if (forward.LengthSquared() > 1e-6f)
            {
                forward = Vector3.Normalize(forward);
                if (MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) < 0.999f)
                {
                    up = Vector3.TransformNormal(Vector3.UnitY, Matrix4x4.CreateFromAxisAngle(forward, twistDeg * MathF.PI / 180f));
                }
            }

            view = Matrix4x4.CreateLookAt(eye, camTarget, up);
            float fovyRad = Math.Clamp(fovyDeg, 1f, 179f) * MathF.PI / 180f;
            projection = Matrix4x4.CreatePerspectiveFieldOfView(fovyRad, vw / (float)vh, Math.Max(sceneRadius * 0.001f, 0.1f), sceneRadius * 3f);
        }
        else if (cameraTypePreviewActive && cameraTypePreviewSource?.CameraParamFields is { } previewCamFields)
        {
            XzParaCameraSim.Result simResult = XzParaCameraSim.Compute(
                previewCamFields, cameraTypePreviewSource.Rotation, cameraTypePreviewPlayerPos,
                cameraTypePreviewPlayerYawDeg, cameraTypePreviewPanAngleDeg,
                vw / (float)vh, Math.Max(sceneRadius * 0.001f, 0.1f), sceneRadius * 3f);

            eye = simResult.Eye;
            view = simResult.View;
            projection = simResult.Projection;
            cameraTypePreviewDebugText = simResult.DebugText;
        }
        else
        {
            eye = session.ViewCenter + new Vector3(
                distance * MathF.Cos(pitch) * MathF.Sin(yaw),
                distance * MathF.Sin(pitch),
                distance * MathF.Cos(pitch) * MathF.Cos(yaw));
            view = Matrix4x4.CreateLookAt(eye, session.ViewCenter, Vector3.UnitY);

            const float fovyRad = MathF.PI / 4f;
            float nearPlane = Math.Max(distance * 0.005f, 0.1f);
            float farPlane = sceneRadius * 3f;
            if (orthographicCamera)
            {
                float halfHeight = distance * MathF.Tan(fovyRad / 2f);
                float halfWidth = halfHeight * vw / (float)vh;
                projection = Matrix4x4.CreateOrthographicOffCenter(-halfWidth, halfWidth, -halfHeight, halfHeight, nearPlane, farPlane);
            }
            else
            {
                projection = Matrix4x4.CreatePerspectiveFieldOfView(fovyRad, vw / (float)vh, nearPlane, farPlane);
            }
        }

        HashSet<ObjectInstance> hiddenInstances = cameraPreviewActive
            ? new HashSet<ObjectInstance>(session.Objects.Where(o => o.SourceList == "StartInfo" && o.Instance is not null).Select(o => o.Instance!))
            : [];
        List<ObjectInstance> visibleInstances = hiddenZoneStagePaths.Count == 0 && hiddenInstances.Count == 0
            ? session.Instances
            : GetVisibleObjects().Where(o => o.Instance is null || !hiddenInstances.Contains(o.Instance)).SelectMany(o => o.AllInstances).ToList();

        if (playWaitAnimations)
        {
            foreach (LoadedObject obj in session.Instances.Select(i => i.Object).Distinct())
            {
                if (obj.WaitAnimation is not { } anim)
                {
                    continue;
                }

                float animFrame = anim.EndFrame > 0 ? (waitAnimationClockSeconds * 60f) % anim.EndFrame : 0f;
                Matrix4x4[] animatedJointMatrices = BDLMeshBuilder.ComputeAnimatedJointWorldMatrices(obj.Model, anim, animFrame);
                Matrix4x4[] inverseBindMatrices = obj.CachedInverseBindMatrices ??= BDLMeshBuilder.ComputeInverseBindMatrices(obj.Model);

                for (int m = 0; m < obj.Meshes.Count && m < obj.RenderMeshes.Count; m++)
                {
                    GpuMesh gpuMesh = obj.Meshes[m];
                    float[] rebaked = gpuMesh.RebakeScratch ??= new float[gpuMesh.Vertices.Length];
                    BDLMeshBuilder.RebakeVertices(gpuMesh, animatedJointMatrices, inverseBindMatrices, rebaked);
                    renderer!.UpdateMeshVertices(obj.RenderMeshes[m], rebaked);
                }
            }

            foreach (EditableObject obj in session.Objects)
            {
                if (obj.Instance is null)
                {
                    continue;
                }

                if (WaitAnimationSpinSim.ComputeSpinDegrees(obj.InternalName, waitAnimationClockSeconds) is not { } spinDeg)
                {
                    continue;
                }

                Matrix4x4 spin = Matrix4x4.CreateRotationY(spinDeg * MathF.PI / 180f);
                obj.Instance.WorldMatrix = spin * GalaxyLoader.ComposePlacementMatrix(obj.Position, obj.Rotation, obj.Scale);
            }

            foreach (EditableObject obj in session.Objects)
            {
                (float Scale, float Offset)? uv1Scroll = null;
                if (obj.ElectricRailSim is { } sim)
                {
                    if (obj.ElectricRailPoints is { } pointInstances)
                    {
                        List<Vector3> positions = sim.ComputePointPositions(waitAnimationClockSeconds);
                        for (int i = 0; i < pointInstances.Count && i < positions.Count; i++)
                        {
                            pointInstances[i].WorldMatrix = Matrix4x4.CreateTranslation(positions[i]);
                        }
                    }

                    uv1Scroll = sim.ComputeRibbonUvScroll(waitAnimationClockSeconds);
                }

                if (obj.Instance is not { } ribbonInstance || ribbonInstance.Object.Meshes.Count == 0 || ribbonInstance.Object.RenderMeshes.Count == 0)
                {
                    continue;
                }

                (float A, float B, float C, float D, float Tx, float Ty)? uv0Transform = null;
                if (obj.ElectricRailUvAnim is { } uvAnim)
                {
                    float uvAnimFrame = uvAnim.EndFrame > 0 ? (waitAnimationClockSeconds * 60f) % uvAnim.EndFrame : 0f;
                    uv0Transform = uvAnim.SampleMatrix(uvAnimFrame);
                }

                if (uv1Scroll is null && uv0Transform is null)
                {
                    continue;
                }

                GpuMesh ribbonMesh = ribbonInstance.Object.Meshes[0];
                float[] scrolled = GalaxyLoader.ScrollRibbonUv(ribbonMesh, uv1Scroll, uv0Transform);
                renderer!.UpdateMeshVertices(ribbonInstance.Object.RenderMeshes[0], scrolled);
            }
        }

        void SimulateMapPart(EditableObject obj, string className, int deltaFrames)
        {
            if (obj.Instance is null)
            {
                return;
            }

            if (className == "RailMoveObj")
            {
                if (obj.RailMoveSim is null)
                {
                    int? pathLinkId = obj.Fields.TryGetValue("CommonPath_ID", out object? cpid) && cpid is int cpidValue && cpidValue != 65535
                        ? cpidValue
                        : null;
                    EditablePath? rail = pathLinkId is null
                        ? null
                        : session!.Paths.FirstOrDefault(p => p.StagePath == obj.StagePath && p.LinkId == pathLinkId);

                    if (rail is null || rail.WorldPoints.Count == 0)
                    {
                        return;
                    }

                    obj.RailMoveSim = new RailMoveSimState(rail.WorldPoints, rail.Closed, rail.Fields, obj.Position);
                }

                if (!obj.RailMoveSim.IsFinished)
                {
                    Vector3 simPosition = obj.RailMoveSim.Advance(deltaFrames);
                    obj.Instance.WorldMatrix = GalaxyLoader.ComposePlacementMatrix(simPosition, obj.Rotation, obj.Scale);
                }
            }
            else if (className == "RotateMoveObj")
            {
                obj.RotateMoveSim ??= new RotateMoveSimState(obj.Fields);

                if (!obj.RotateMoveSim.IsFinished)
                {
                    obj.RotateMoveSim.Advance(deltaFrames);
                    Matrix4x4 spin = obj.RotateMoveSim.Axis switch
                    {
                        RotateAxis.X => Matrix4x4.CreateRotationX(obj.RotateMoveSim.AngleDegrees * MathF.PI / 180f),
                        RotateAxis.Y => Matrix4x4.CreateRotationY(obj.RotateMoveSim.AngleDegrees * MathF.PI / 180f),
                        _ => Matrix4x4.CreateRotationZ(obj.RotateMoveSim.AngleDegrees * MathF.PI / 180f),
                    };
                    obj.Instance.WorldMatrix = spin * GalaxyLoader.ComposePlacementMatrix(obj.Position, obj.Rotation, obj.Scale);
                }
            }
        }

        void SimulateEnemyWander(EditableObject obj, string className, int deltaFrames)
        {
            if (obj.Instance is null)
            {
                return;
            }

            if (obj.WalkerStateWanderSim is null)
            {
                (float speed, int waitTime, int walkTime, float turnMaxRateDegree) = className switch
                {
                    "Kuribo" => (0.2f, 120, 120, 3.0f),
                    "KuriboMini" => (0.1f, 120, 120, 3.0f),
                    "KuriboChief" => (0.1f, 120, 300, 1.0f),
                    _ => (0f, 0, 0, 0f),
                };

                if (speed <= 0f)
                {
                    return;
                }

                Vector3 initialDirection = GalaxyLoader.CalcFrontVecFromRotation(obj.Rotation);
                int seed = HashCode.Combine(obj.Position.X, obj.Position.Y, obj.Position.Z);
                obj.WalkerStateWanderSim = new WalkerStateWanderSimState(obj.Position, initialDirection, speed, waitTime, walkTime, turnMaxRateDegree, seed);
            }

            obj.WalkerStateWanderSim.Advance(deltaFrames);

            float yaw = MathF.Atan2(obj.WalkerStateWanderSim.Direction.X, obj.WalkerStateWanderSim.Direction.Z);
            obj.Instance.WorldMatrix = Matrix4x4.CreateScale(obj.Scale) *
                Matrix4x4.CreateRotationY(yaw) *
                Matrix4x4.CreateTranslation(obj.WalkerStateWanderSim.Position);
        }

        void SimulateAstroDomeOrbit(EditableObject obj, int deltaFrames)
        {
            if (obj.Instance is null || session is null)
            {
                return;
            }

            if (obj.AstroDomeOrbitSim is null)
            {
                List<EditableObject> siblings = session.Objects
                    .Where(o => o.StagePath == obj.StagePath && o.Layer == obj.Layer && o.DbEntry?.ClassName(session.Game) == "MiniatureGalaxy")
                    .ToList();

                bool IsKoopaType(EditableObject o) => o.Fields.TryGetValue("Obj_arg0", out object? v) && v is int i && i == 2;
                List<EditableObject> ordered = [.. siblings.Where(o => !IsKoopaType(o)), .. siblings.Where(IsKoopaType)];

                int ringIndex = Math.Max(0, ordered.IndexOf(obj));
                obj.AstroDomeOrbitSim = new AstroDomeOrbitSimState(ringIndex, siblings.Count);
            }

            obj.AstroDomeOrbitSim.Advance(deltaFrames);

            Vector3 domeCenter = session.Objects.FirstOrDefault(o => o.StagePath == obj.StagePath && o.InternalName == "SphereSelectorHandle")?.Position ?? Vector3.Zero;
            Vector3 orbitPos = obj.AstroDomeOrbitSim.ComputePosition(domeCenter);

            obj.Instance.WorldMatrix = Matrix4x4.CreateScale(obj.Scale) *
                Matrix4x4.CreateRotationY(obj.AstroDomeOrbitSim.SelfSpinDegrees * MathF.PI / 180f) *
                Matrix4x4.CreateTranslation(orbitPos);
        }

        if (playWaitAnimations && session is not null)
        {
            int targetFrame = (int)(waitAnimationClockSeconds * 60f);
            int deltaFrames = targetFrame - lastSimulatedDiscreteFrame;
            lastSimulatedDiscreteFrame = targetFrame;

            if (deltaFrames > 0)
            {
                foreach (EditableObject obj in session.Objects)
                {
                    string? className = obj.DbEntry?.ClassName(session.Game);
                    if (className == "RailMoveObj" || className == "RotateMoveObj")
                    {
                        SimulateMapPart(obj, className, deltaFrames);
                    }
                    else if (className is "Kuribo" or "KuriboMini" or "KuriboChief")
                    {
                        SimulateEnemyWander(obj, className, deltaFrames);
                    }
                    else if (className == "MiniatureGalaxy")
                    {
                        SimulateAstroDomeOrbit(obj, deltaFrames);
                    }

                    obj.OceanRingSim?.Advance(deltaFrames);
                }
            }
        }

        if (!playWaitAnimations && selectedMapPartsSimObj is not null && session is not null)
        {
            int targetFrame = (int)(selectedMapPartsClockSeconds * 60f);
            int deltaFrames = targetFrame - lastSelectedMapPartsFrame;
            lastSelectedMapPartsFrame = targetFrame;

            if (deltaFrames > 0)
            {
                string? className = selectedMapPartsSimObj.DbEntry?.ClassName(session.Game);
                if (className == "RailMoveObj" || className == "RotateMoveObj")
                {
                    SimulateMapPart(selectedMapPartsSimObj, className, deltaFrames);
                }
            }
        }

        GalaxySession activeSession = session!;

        if (previewLighting)
        {
            if (previewLightPreset is null)
            {
                try
                {
                    previewLightPreset = LightData.ResolveDefaultPreset(activeSession.GameRootDir, activeSession.OutputDir, activeSession.Game, activeSession.GalaxyName);
                }
                catch
                {
                    previewLightPreset = null;
                }
            }

            if (previewLightPreset is not null)
            {
                PreviewLightGroup[] groups = new PreviewLightGroup[4];
                for (int g = 0; g < 4; g++)
                {
                    groups[g] = LightData.ExtractGroup(previewLightPreset, LightData.Groups[g]);
                }

                renderer!.SetPreviewLighting(true, groups, previewLightGroupChoice - 1);
            }
            else
            {
                renderer!.SetPreviewLighting(false, [], -1);
            }
        }
        else
        {
            renderer!.SetPreviewLighting(false, [], -1);
        }

        renderer.Render(visibleInstances, view, projection);

        List<EditableObject> oceanRings = GetVisibleObjects().Where(o => o.OceanRingSim is not null && o.OceanRingMesh is not null).ToList();
        if (oceanRings.Count > 0)
        {
            renderer.CaptureOpaqueSceneTexture((int)vw, (int)vh);
            var oceanViewportSize = new Vector2(vw, vh);
            foreach (EditableObject ring in oceanRings)
            {
                renderer.RenderOceanRing(ring.OceanRingMesh!, ring.OceanRingSim!, view, projection, oceanViewportSize);
            }
        }

        foreach (EditableObject obj in GetVisibleObjects())
        {
            if (obj.Instance is null && !(cameraPreviewActive && obj.SourceList == "StartInfo") &&
                obj.SourceList is not ("CameraCubeInfo" or "AreaObjInfo" or "PlanetObjInfo"))
            {
                Matrix4x4 world = GalaxyLoader.ComposePlacementMatrix(obj.Position, obj.Rotation, Vector3.One);
                renderer.RenderPlaceholder(world, view, projection, depthTest: true);
            }
        }

        var visibleAreaShapeSize = new Vector2(viewportFbo.Width, viewportFbo.Height);
        List<(EditableObject Obj, SceneRenderer.AreaShapeKind Shape, Matrix4x4 World, Vector3 Color)> visibleAreaShapes = GetVisibleAreaShapes();
        foreach ((EditableObject _, SceneRenderer.AreaShapeKind shape, Matrix4x4 world, Vector3 color) in visibleAreaShapes)
        {
            renderer.RenderAreaShape(shape, world, color, view, projection, visibleAreaShapeSize);
        }

        if (activeSession.Selected is { } selectedForArea)
        {
            var selectedArea = visibleAreaShapes.FirstOrDefault(a => ReferenceEquals(a.Obj, selectedForArea));
            if (selectedArea.Obj is not null)
            {
                renderer.RenderAreaShape(selectedArea.Shape, selectedArea.World, new Vector3(1f, 0.6f, 0.1f), view, projection, visibleAreaShapeSize, lineWidthPixels: 10f);
            }
        }

        if (playWaitAnimations)
        {
            var domeGroups = activeSession.Objects
                .Where(o => o.SourceList == "ObjInfo" && o.DbEntry?.ClassName(activeSession.Game) == "MiniatureGalaxy")
                .GroupBy(o => (o.StagePath, o.Layer));

            foreach (var domeGroup in domeGroups)
            {
                EditableObject? handle = activeSession.Objects.FirstOrDefault(o => o.StagePath == domeGroup.Key.StagePath && o.InternalName == "SphereSelectorHandle");
                if (handle is null)
                {
                    continue;
                }

                int ringCount = domeGroup.Count();
                for (int ring = 0; ring < ringCount; ring++)
                {
                    List<Vector3> outline = AstroDomeOrbitSimState.ComputeRingOutline(ring, ringCount, handle.Position);
                    renderer.RenderPath(outline, new Vector3(19 / 255f, 177 / 255f, 1f), view, projection, lineWidth: 1.5f);
                }
            }
        }

        List<EditablePath> visiblePaths = GetVisiblePaths();

        foreach (EditablePath path in visiblePaths)
        {
            renderer.RenderPath(path.WorldPolyline, path.Color, view, projection);

            foreach (PathPoint point in path.WorldPoints)
            {
                renderer.RenderPlaceholder(Matrix4x4.CreateTranslation(point.Position), view, projection, path.Color, depthTest: true);
                renderer.RenderPlaceholder(Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(point.ControlPointIn), view, projection, path.Color, depthTest: true);
                renderer.RenderPlaceholder(Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(point.ControlPointOut), view, projection, path.Color, depthTest: true);
                renderer.RenderPath([point.Position, point.ControlPointIn], path.Color, view, projection, lineWidth: 1f);
                renderer.RenderPath([point.Position, point.ControlPointOut], path.Color, view, projection, lineWidth: 1f);
            }
        }

        if (activeSession.SelectedPath is { } selectedPath && visiblePaths.Contains(selectedPath))
        {
            var pathSelectionColor = new Vector3(1f, 0.6f, 0.1f);
            renderer.RenderPath(selectedPath.WorldPolyline, pathSelectionColor, view, projection, lineWidth: 4f);

            if (activeSession.SelectedPathPointIndex is int spi && spi >= 0 && spi < selectedPath.WorldPoints.Count)
            {
                PathPoint selectedPoint = selectedPath.WorldPoints[spi];
                Vector3 pointPos = activeSession.SelectedPathPointPart switch
                {
                    PathPointPart.ControlIn => selectedPoint.ControlPointIn,
                    PathPointPart.ControlOut => selectedPoint.ControlPointOut,
                    _ => selectedPoint.Position,
                };
                Matrix4x4 pointOutlineWorld = Matrix4x4.CreateScale(SceneRenderer.PlaceholderBoxHalfExtent) * Matrix4x4.CreateTranslation(pointPos);
                renderer.RenderBoundsOutline(pointOutlineWorld, pathSelectionColor, view, projection, depthTest: true);
            }
        }

        if (pendingPlacement is not null)
        {
            if (lastViewportHovered)
            {
                Vector2 hoverMousePos = ImGui.GetIO().MousePos - lastViewportImagePos;
                Picking.Ray hoverRay = Picking.ScreenPointToRay(hoverMousePos, new Vector2(vw, vh), view, projection);
                Vector3 fallbackPoint = hoverRay.Origin + hoverRay.Direction * 1000f;
                IEnumerable<EditableObject> raycastTargets = activeSession.Objects.Where(o => !ReferenceEquals(o, pendingPlacement));
                pendingPlacement.Position = Picking.RaycastScenePoint(hoverRay, raycastTargets) ?? fallbackPoint;
                pendingPlacement.SyncTransformToInstance();
            }

            if (pendingPlacement.Instance is { } pendingInstance)
            {
                LoadedObject pendingModel = pendingInstance.Object;
                Vector3 pendingCenter = (pendingModel.LocalBoundsMin + pendingModel.LocalBoundsMax) / 2f;
                Vector3 pendingHalf = (pendingModel.LocalBoundsMax - pendingModel.LocalBoundsMin) / 2f;
                Matrix4x4 pendingOutlineWorld = Matrix4x4.CreateScale(Vector3.Max(pendingHalf, new Vector3(1f))) *
                    Matrix4x4.CreateTranslation(pendingCenter) * pendingInstance.WorldMatrix;
                renderer.RenderBoundsOutline(pendingOutlineWorld, new Vector3(0.2f, 0.9f, 0.9f), view, projection);
            }
        }

        if (pendingPath is not null || pendingPathPointInsert is not null)
        {
            if (lastViewportHovered)
            {
                Vector2 hoverMousePos = ImGui.GetIO().MousePos - lastViewportImagePos;
                Picking.Ray hoverRay = Picking.ScreenPointToRay(hoverMousePos, new Vector2(vw, vh), view, projection);
                pendingPathSurfaceSnap = Picking.RaycastScenePoint(hoverRay, activeSession.Objects)
                    ?? hoverRay.Origin + hoverRay.Direction * 1000f;
            }

            if (pendingPathSurfaceSnap is { } snap)
            {
                var markerColor = new Vector3(0.2f, 0.9f, 0.9f);
                Matrix4x4 markerWorld = Matrix4x4.CreateScale(SceneRenderer.PlaceholderBoxHalfExtent) * Matrix4x4.CreateTranslation(snap);
                renderer.RenderBoundsOutline(markerWorld, markerColor, view, projection, depthTest: true);
                renderer.RenderBoundsOutline(markerWorld, markerColor, view, projection);

                if (pendingPath is { WorldPoints.Count: > 0 } drawingPath)
                {
                    renderer.RenderPath([drawingPath.WorldPoints[^1].Position, snap], markerColor, view, projection);
                }
            }
        }
        else
        {
            pendingPathSurfaceSnap = null;
        }

        if (placingCameraTypePreviewPlayer)
        {
            Vector2 hoverMousePos = ImGui.GetIO().MousePos - lastViewportImagePos;
            Picking.Ray hoverRay = Picking.ScreenPointToRay(hoverMousePos, new Vector2(vw, vh), view, projection);
            if (Picking.RaycastScenePoint(hoverRay, activeSession.Objects) is { } hoverPoint)
            {
                renderer.RenderPlaceholder(Matrix4x4.CreateTranslation(hoverPoint), view, projection, new Vector3(0.2f, 0.9f, 0.3f));
            }
        }
        else if (rotatingCameraTypePreviewPlayer || cameraTypePreviewActive)
        {
            if (cameraTypePreviewPlayerModel is null)
            {
                string objectDataDir = Path.Combine(activeSession.GameRootDir, "DATA", "files", "ObjectData");
                cameraTypePreviewPlayerModel = GalaxyLoader.TryLoadObject("Mario", objectDataDir);
                if (cameraTypePreviewPlayerModel is not null)
                {
                    renderer.UploadObject(cameraTypePreviewPlayerModel);
                }
            }

            Matrix4x4 playerWorld = GalaxyLoader.ComposePlacementMatrix(
                cameraTypePreviewPlayerPos, new Vector3(0f, cameraTypePreviewPlayerYawDeg, 0f), Vector3.One);

            if (cameraTypePreviewPlayerModel is not null)
            {
                cameraTypePreviewPlayerInstance ??= new ObjectInstance { Object = cameraTypePreviewPlayerModel, WorldMatrix = playerWorld };
                cameraTypePreviewPlayerInstance.WorldMatrix = playerWorld;
                renderer.Render([cameraTypePreviewPlayerInstance], view, projection);
            }
            else
            {
                renderer.RenderPlaceholder(Matrix4x4.CreateTranslation(cameraTypePreviewPlayerPos), view, projection, new Vector3(0.2f, 0.9f, 0.3f));
            }
        }

        if (activeSession.Selected is { } selected)
        {
            (Matrix4x4 outlineWorld, Vector3 focusCenter) = ObjectLinks.ComputeOutline(selected);
            float focusRadius = selected.Instance is { } inst
                ? Math.Max((inst.Object.LocalBoundsMax - inst.Object.LocalBoundsMin).Length() / 2f, 10f)
                : SceneRenderer.PlaceholderBoxHalfExtent;

            renderer.RenderBoundsOutline(outlineWorld, new Vector3(1f, 0.6f, 0.1f), view, projection);

            if (showObjectLinks)
            {
                foreach (IGrouping<EditableObject, ObjectLink> group in ObjectLinks.FindLinks(selected, activeSession.Objects).GroupBy(l => l.Target))
                {
                    (Matrix4x4 targetOutline, Vector3 targetCenter) = ObjectLinks.ComputeOutline(group.Key);
                    Vector3 linkColor = group.Any(l => l.Kind == ObjectLinkKind.Switch) ? ObjectLinks.SwitchColor : ObjectLinks.ObjIdColor;
                    renderer.RenderBoundsOutline(targetOutline, linkColor, view, projection);
                    ObjectLinks.DrawArrow(renderer, focusCenter, targetCenter, linkColor, view, projection);
                }
            }

            bool allowKeyboardGrab = !ImGui.GetIO().WantTextInput;

            if (allowKeyboardGrab && lastViewportHovered && !viewportGizmo.IsDragging && KeyPressedEdge(ImGuiKey.F))
            {
                activeSession.ViewCenter = focusCenter;
                distance = focusRadius * 3f;
            }

            viewportGizmo.Update(selected, eye, ImGui.GetIO().MousePos - lastViewportImagePos, new Vector2(vw, vh), view, projection, lastViewportHovered, allowKeyboardGrab, renderer);
            TrackObjectGizmoDrag(selected);
        }
        else if (activeSession.SelectedPath is { } gizmoPath && activeSession.SelectedPathPointIndex is int gizmoPointIndex &&
                 gizmoPointIndex >= 0 && gizmoPointIndex < gizmoPath.WorldPoints.Count)
        {
            PathPoint gizmoPoint = gizmoPath.WorldPoints[gizmoPointIndex];
            IGizmoTarget target = activeSession.SelectedPathPointPart switch
            {
                PathPointPart.ControlIn => new PathPointGizmoTarget(gizmoPath, () => gizmoPoint.ControlPointIn, v => gizmoPoint.ControlPointIn = v),
                PathPointPart.ControlOut => new PathPointGizmoTarget(gizmoPath, () => gizmoPoint.ControlPointOut, v => gizmoPoint.ControlPointOut = v),
                _ => new PathPointGizmoTarget(gizmoPath, () => gizmoPoint.Position, v =>
                {
                    Vector3 delta = v - gizmoPoint.Position;
                    gizmoPoint.Position = v;
                    if (!IsShiftDown())
                    {
                        gizmoPoint.ControlPointIn += delta;
                        gizmoPoint.ControlPointOut += delta;
                    }
                }),
            };

            bool allowKeyboardGrabPoint = !ImGui.GetIO().WantTextInput;
            viewportGizmo.Update(target, eye, ImGui.GetIO().MousePos - lastViewportImagePos, new Vector2(vw, vh), view, projection, lastViewportHovered, allowKeyboardGrabPoint, renderer);
            TrackPathPointGizmoDrag(gizmoPath, gizmoPoint);
        }

        ViewportFramebuffer.Unbind(gl, (uint)window.FramebufferSize.X, (uint)window.FramebufferSize.Y);
    }

    ImGui.Image((IntPtr)viewportFbo.ColorTexture, new Vector2(vw, vh), new Vector2(0, 1), new Vector2(1, 0));
    lastViewportImagePos = ImGui.GetItemRectMin();
    lastViewportHovered = ImGui.IsItemHovered();

    bool gizmoActive = (session?.Selected is not null || session?.SelectedPathPointIndex is not null) && viewportGizmo.IsDragging;

    if (session is not null && lastViewportHovered && !cameraPreviewActive && !cameraTypePreviewActive)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        if (io.MouseWheel != 0)
        {
            float minDistance = 1f;
            float maxDistance = sceneRadius * 5f;
            distance = Math.Clamp(distance - io.MouseWheel * distance * 0.1f, minDistance, maxDistance);
        }

        if (ImGui.IsMouseDown(ImGuiMouseButton.Middle) || ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            if (!draggingPan)
            {
                draggingPan = true;
            }
            else
            {
                var forward = new Vector3(
                    -MathF.Cos(pitch) * MathF.Sin(yaw),
                    -MathF.Sin(pitch),
                    -MathF.Cos(pitch) * MathF.Cos(yaw));
                Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                Vector3 camUp = Vector3.Cross(right, forward);
                float worldPerPixel = Math.Max(2f * distance * MathF.Tan(MathF.PI / 8f) / vh, 0.05f);
                session.ViewCenter -= right * io.MouseDelta.X * worldPerPixel;
                session.ViewCenter += camUp * io.MouseDelta.Y * worldPerPixel;
            }
        }
        else
        {
            draggingPan = false;
        }
    }
    else
    {
        draggingPan = false;
    }

    if (session is not null && lastViewportHovered && !gizmoActive && !cameraPreviewActive && (placingCameraTypePreviewPlayer || !cameraTypePreviewActive))
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (!draggingViewport)
            {
                draggingViewport = true;
            }
            else
            {
                yaw += io.MouseDelta.X * 0.01f;
                pitch = Math.Clamp(pitch - io.MouseDelta.Y * 0.01f, -1.5f, 1.5f);
            }
        }
        else
        {
            draggingViewport = false;
        }

        if (!gizmoWasDraggingThisFrame && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4f))
        {
            Vector2 mousePos = io.MousePos - lastViewportImagePos;

            if (pendingPlacement is not null)
            {
                EditableObject placed = pendingPlacement;
                pendingPlacement = null;
                session.Selected = placed;
                statusMessage = LF("Placed {0}.", placed.DisplayName);

                int placedIndex = session.Objects.IndexOf(placed);
                List<ObjectInstance> placedInstances = placed.AllInstances.ToList();
                session.History.Push(
                    () => RemoveObject(placed),
                    () =>
                    {
                        session.Objects.Insert(placedIndex, placed);
                        foreach (ObjectInstance instance in placedInstances)
                        {
                            session.Instances.Add(instance);
                        }

                        session.Selected = placed;
                    });
            }
            else if (pendingPath is not null)
            {
                Vector3 pathHit = PendingPathClickPoint(mousePos, view, projection, vw, vh);
                pendingPath.WorldPoints.Add(MakeNewPathPoint(pathHit));
                pendingPath.RecomputePolyline();
                statusMessage = LF("Path: {0} point(s). Click to add another, Enter or Esc to finish.", pendingPath.WorldPoints.Count);
            }
            else if (pendingPathPointInsert is not null)
            {
                CommitPathPointInsert(PendingPathClickPoint(mousePos, view, projection, vw, vh));
            }
            else if (deleteClickMode)
            {
                (EditablePath Path, int PointIndex, PathPointPart Part)? deletePoint =
                    Picking.PickPathPoint(mousePos, new Vector2(vw, vh), view, projection, GetVisiblePaths());
                if (deletePoint is { } dp)
                {
                    DeleteSelectedPathPoint(dp.Path, dp.PointIndex);
                    if (!IsShiftDown())
                    {
                        deleteClickMode = false;
                    }
                }
                else
                {
                    Picking.Ray deleteRay = Picking.ScreenPointToRay(mousePos, new Vector2(vw, vh), view, projection);
                    if (Picking.Pick(deleteRay, GetVisibleObjects()) is { } toDelete)
                    {
                        string deletedName = toDelete.DisplayName;
                        PushRemoveObjectUndo(toDelete);
                        RemoveObject(toDelete);
                        bool shiftHeld = IsShiftDown();
                        statusMessage = shiftHeld
                            ? $"Deleted {deletedName}. Click another object to delete it, or press Esc to stop."
                            : LF("Deleted {0}.", deletedName);

                        if (!shiftHeld)
                        {
                            deleteClickMode = false;
                        }
                    }
                }
            }
            else if (copyClickMode)
            {
                Picking.Ray copyRay = Picking.ScreenPointToRay(mousePos, new Vector2(vw, vh), view, projection);
                if (Picking.Pick(copyRay, GetVisibleObjects()) is { } toCopy)
                {
                    DuplicateObject(toCopy);
                }
            }
            else if (placingCameraTypePreviewPlayer)
            {
                Picking.Ray placeRay = Picking.ScreenPointToRay(mousePos, new Vector2(vw, vh), view, projection);
                if (Picking.RaycastScenePoint(placeRay, session.Objects) is { } hitPoint)
                {
                    cameraTypePreviewPlayerPos = hitPoint;
                    placingCameraTypePreviewPlayer = false;
                    rotatingCameraTypePreviewPlayer = true;
                    cameraTypePreviewPlayerYawDeg = 0f;
                }
            }
            else if (rotatingCameraTypePreviewPlayer)
            {
                rotatingCameraTypePreviewPlayer = false;
                cameraTypePreviewActive = true;
                cameraTypePreviewPanAngleDeg = 0f;
                cameraTypePreviewPanTargetDeg = 0f;
                cameraTypePreviewPanning = false;
            }
            else
            {
                List<EditablePath> clickablePaths = GetVisiblePaths();
                (EditablePath Path, int PointIndex, PathPointPart Part)? pickedPoint = Picking.PickPathPoint(mousePos, new Vector2(vw, vh), view, projection, clickablePaths);
                if (pickedPoint is { } pp)
                {
                    session.SelectedPath = pp.Path;
                    session.SelectedPathPointIndex = pp.PointIndex;
                    session.SelectedPathPointPart = pp.Part;
                    session.Selected = null;
                }
                else
                {
                    EditablePath? pickedPath = Picking.PickPath(mousePos, new Vector2(vw, vh), view, projection, clickablePaths);
                    if (pickedPath is not null)
                    {
                        session.SelectedPath = pickedPath;
                        session.SelectedPathPointIndex = null;
                        session.Selected = null;
                    }
                    else
                    {
                        EditableObject? pickedAreaShape = Picking.PickAreaShapeBorder(mousePos, new Vector2(vw, vh), view, projection,
                            GetVisibleAreaShapes().Select(a => (a.Obj, a.Shape, a.World)));
                        if (pickedAreaShape is not null)
                        {
                            session.Selected = pickedAreaShape;
                            session.SelectedPath = null;
                            session.SelectedPathPointIndex = null;
                        }
                        else
                        {
                            Picking.Ray ray = Picking.ScreenPointToRay(mousePos, new Vector2(vw, vh), view, projection);
                            session.Selected = Picking.Pick(ray, GetVisibleObjects());
                            session.SelectedPath = null;
                            session.SelectedPathPointIndex = null;
                        }
                    }
                }
            }
        }
    }
    else if (!gizmoActive)
    {
        draggingViewport = false;
    }
}
