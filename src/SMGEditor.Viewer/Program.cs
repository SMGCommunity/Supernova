using System.Numerics;
using SMGEditor.Core.Formats;
using SMGEditor.Core.Stage;
using SMGEditor.Viewer;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

string smgFilesRoot = GalaxyLoader.FindSmgFilesRoot();

List<(string Name, Matrix4x4 World)> placementRequests;
string windowTitle;
string objectDataDir;

if (args.Length > 0 && args[0] == "--galaxy")
{
    string galaxyName = args[1];
    int game = 2;
    int scenarioIndex = 0;
    for (int i = 2; i < args.Length - 1; i++)
    {
        if (args[i] == "--game")
        {
            game = int.Parse(args[i + 1]);
        }
        else if (args[i] == "--scenario")
        {
            scenarioIndex = int.Parse(args[i + 1]);
        }
    }

    (List<PlacedObject> placedObjects, objectDataDir) = GalaxyLoader.LoadGalaxyPlacements(Path.Combine(smgFilesRoot, game.ToString()), galaxyName, scenarioIndex);
    Console.WriteLine($"Loading galaxy '{galaxyName}', scenario {scenarioIndex}: {placedObjects.Count} placed object(s).");

    placementRequests = placedObjects
        .Select(o => (o.Name, GalaxyLoader.ComposePlacementMatrix(o.Position, o.RotationDegrees, o.Scale)))
        .ToList();
    windowTitle = $"Supernova Viewer - {galaxyName}";

    var unresolved = placementRequests.Select(p => p.Name).Distinct()
        .Where(n => !File.Exists(Path.Combine(objectDataDir, n + ".arc")))
        .OrderBy(n => n)
        .ToList();
    if (unresolved.Count > 0)
    {
        Console.WriteLine($"No direct ObjectData/<name>.arc for {unresolved.Count} name(s), skipping: {string.Join(", ", unresolved)}");
    }
}
else
{
    string arcPath = args.Length > 0 ? args[0] : ProjectFiles.GameFilePath(Path.Combine(smgFilesRoot, "2"), "DATA/files/ObjectData/Abekobe2DMoveLift.arc");
    string objectName = Path.GetFileNameWithoutExtension(arcPath);
    objectDataDir = Path.GetDirectoryName(Path.GetFullPath(arcPath)) ?? "";
    placementRequests = [(objectName, Matrix4x4.Identity)];
    windowTitle = $"Supernova Viewer - {objectName}";
}

var loadedObjects = new Dictionary<string, LoadedObject?>(StringComparer.OrdinalIgnoreCase);
var instances = new List<ObjectInstance>();

foreach ((string name, Matrix4x4 world) in placementRequests)
{
    if (!loadedObjects.TryGetValue(name, out LoadedObject? loaded))
    {
        loaded = GalaxyLoader.TryLoadObject(name, objectDataDir) ?? GalaxyLoader.TryLoadBtiBillboard(name, objectDataDir);
        loadedObjects[name] = loaded;
    }

    if (loaded is not null)
    {
        instances.Add(new ObjectInstance { Object = loaded, WorldMatrix = world });
    }
}

Console.WriteLine($"Loaded {loadedObjects.Count(kv => kv.Value is not null)} unique model(s), placed {instances.Count} instance(s).");

(Vector3 boundsMin, Vector3 boundsMax) = GalaxyLoader.ComputeSceneBounds(instances);
Vector3 boundsCenter = (boundsMin + boundsMax) / 2f;
float boundsRadius = Math.Max((boundsMax - boundsMin).Length() / 2f, 1f);

(Vector3 farMin, Vector3 farMax) = GalaxyLoader.ComputeSceneBounds(instances, includeSky: true);
float farPlaneRadius = Math.Max((farMax - farMin).Length() / 2f, 1f);

var options = WindowOptions.Default with
{
    Size = new Vector2D<int>(1280, 720),
    Title = windowTitle,
};
IWindow window = Window.Create(options);

GL? gl = null;
SceneRenderer? renderer = null;
int frameCount = 0;

int screenshotArgIndex = Array.IndexOf(args, "--screenshot");
string? screenshotPath = screenshotArgIndex >= 0 && screenshotArgIndex + 1 < args.Length ? args[screenshotArgIndex + 1] : null;
int screenshotFrameIndex = Array.IndexOf(args, "--screenshot-frame");
int screenshotFrame = screenshotFrameIndex >= 0 && screenshotFrameIndex + 1 < args.Length ? int.Parse(args[screenshotFrameIndex + 1]) : 10;

float yaw = 0.6f;
float pitch = 0.35f;
float distance = boundsRadius * 2.5f;
Vector2 lastMousePos = default;
bool dragging = false;

window.Load += () =>
{
    gl = GL.GetApi(window);
    IInputContext input = window.CreateInput();
    foreach (IMouse mouse in input.Mice)
    {
        mouse.MouseDown += (_, button) => { if (button == MouseButton.Left) { dragging = true; } };
        mouse.MouseUp += (_, button) => { if (button == MouseButton.Left) { dragging = false; } };
        mouse.MouseMove += (_, pos) =>
        {
            var current = new Vector2(pos.X, pos.Y);
            if (dragging)
            {
                Vector2 delta = current - lastMousePos;
                yaw += delta.X * 0.01f;
                pitch = Math.Clamp(pitch - delta.Y * 0.01f, -1.5f, 1.5f);
            }

            lastMousePos = current;
        };
        mouse.Scroll += (_, wheel) => { distance = Math.Clamp(distance - wheel.Y * boundsRadius * 0.2f, boundsRadius * 0.05f, boundsRadius * 10f); };
    }

    gl.ClearColor(0.15f, 0.17f, 0.2f, 1f);
    gl.Enable(EnableCap.DepthTest);

    renderer = new SceneRenderer(gl);
    foreach (LoadedObject obj in loadedObjects.Values.Where(o => o is not null).Cast<LoadedObject>())
    {
        renderer.UploadObject(obj);
    }

    Console.WriteLine("GL resources uploaded.");
};

window.Resize += size => gl?.Viewport(0, 0, (uint)size.X, (uint)size.Y);

window.Render += _ =>
{
    if (gl is null)
    {
        return;
    }

    gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

    Vector3 eye = boundsCenter + new Vector3(
        distance * MathF.Cos(pitch) * MathF.Sin(yaw),
        distance * MathF.Sin(pitch),
        distance * MathF.Cos(pitch) * MathF.Cos(yaw));
    Matrix4x4 view = Matrix4x4.CreateLookAt(eye, boundsCenter, Vector3.UnitY);
    Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
        MathF.PI / 4f, window.Size.X / (float)Math.Max(window.Size.Y, 1), boundsRadius * 0.01f, farPlaneRadius * 3f);

    renderer?.Render(instances, view, projection);

    frameCount++;
    if (screenshotPath is not null && frameCount == screenshotFrame && gl is not null)
    {
        SaveScreenshot(gl, window.FramebufferSize.X, window.FramebufferSize.Y, screenshotPath);
    }
};

window.Run();

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
