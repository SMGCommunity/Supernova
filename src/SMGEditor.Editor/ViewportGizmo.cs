using System.Numerics;
using ImGuiNET;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal interface IGizmoTarget
{
    Vector3 Position { get; set; }
    Vector3 Rotation { get; set; }
    bool SupportsRotation { get; }

    void OnChanged();
}

internal sealed class ViewportGizmo
{
    private enum Handle
    {
        None,
        TranslateX, TranslateY, TranslateZ, TranslateFree,
        RotateX, RotateY, RotateZ,
    }

    private static readonly Vector3[] AxisDirections = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
    private static readonly Vector3[] AxisColors =
    [
        new(0.85f, 0.2f, 0.2f),
        new(0.25f, 0.8f, 0.25f),
        new(0.25f, 0.45f, 0.95f),
    ];

    private static readonly Vector3 HoverColor = new(1f, 0.85f, 0.15f);

    private const float HitThresholdPixels = 10f;

    private const float RingHitThresholdPixels = 16f;

    private const float SizeFactor = 0.12f;

    private Handle _hovered = Handle.None;
    private Handle _dragging = Handle.None;

    private bool _modal;
    private Vector3 _modalStartPosition;
    private Vector3 _modalStartRotation;

    private Vector3 _dragAxisOrigin;
    private Vector3 _dragAxisDir;
    private Vector3 _freeTranslatePlaneNormal;

    private Vector3 _dragOffset;

    private int _dragRotateAxis;
    private float _dragStartMouseAngle;
    private float _dragStartRotationDeg;

    public bool IsDragging => _dragging != Handle.None;
    public bool IsHoveringHandle => _hovered != Handle.None;

    public bool ConsumedEscapeThisFrame { get; private set; }

    private readonly Dictionary<ImGuiKey, bool> _keyWasDown = new();

    private bool KeyPressedEdge(ImGuiKey key)
    {
        bool down = ImGui.IsKeyDown(key);
        bool wasDown = _keyWasDown.TryGetValue(key, out bool w) && w;
        _keyWasDown[key] = down;
        return down && !wasDown;
    }

    public void Update(
        IGizmoTarget target, Vector3 eye, Vector2 mousePos, Vector2 viewportSize,
        Matrix4x4 view, Matrix4x4 projection, bool viewportHovered, bool allowKeyboardGrab, SceneRenderer renderer)
    {
        ConsumedEscapeThisFrame = false;
        float gizmoSize = MathF.Max(Vector3.Distance(eye, target.Position) * SizeFactor, 1f);

        if (_dragging == Handle.None)
        {
            _hovered = viewportHovered
                ? HitTest(target.Position, gizmoSize, mousePos, view, projection, viewportSize, target.SupportsRotation)
                : Handle.None;

            if (viewportHovered && _hovered != Handle.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                BeginDrag(_hovered, target, eye, mousePos, view, projection, viewportSize, modal: false);
            }
            else if (viewportHovered && allowKeyboardGrab)
            {
                if (KeyPressedEdge(ImGuiKey.G))
                {
                    BeginDrag(Handle.TranslateFree, target, eye, mousePos, view, projection, viewportSize, modal: true);
                }
                else if (target.SupportsRotation && KeyPressedEdge(ImGuiKey.R))
                {
                    BeginDrag(Handle.RotateY, target, eye, mousePos, view, projection, viewportSize, modal: true);
                }
            }
        }
        else
        {
            ApplyAxisLockKeys(target, mousePos, view, projection, viewportSize);
            UpdateDrag(target, mousePos, view, projection, viewportSize);
            target.OnChanged();

            if (_modal)
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || KeyPressedEdge(ImGuiKey.Enter))
                {
                    _dragging = Handle.None;
                }
                else
                {
                    bool escapeCancel = KeyPressedEdge(ImGuiKey.Escape);
                    if (escapeCancel || ImGui.IsMouseClicked(ImGuiMouseButton.Right) ||
                        ImGui.GetIO().MouseWheel != 0 || ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
                    {
                        target.Position = _modalStartPosition;
                        target.Rotation = _modalStartRotation;
                        target.OnChanged();
                        _dragging = Handle.None;
                        ConsumedEscapeThisFrame = escapeCancel;
                    }
                }
            }
            else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _dragging = Handle.None;
            }
        }

        Draw(target, gizmoSize, view, projection, viewportSize, renderer);
    }

    private void BeginDrag(Handle handle, IGizmoTarget target, Vector3 eye, Vector2 mousePos, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize, bool modal)
    {
        _dragging = handle;
        _modal = modal;
        _modalStartPosition = target.Position;
        _modalStartRotation = target.Rotation;

        if (handle is Handle.TranslateX or Handle.TranslateY or Handle.TranslateZ)
        {
            _dragAxisOrigin = target.Position;
            _dragAxisDir = AxisDirections[handle - Handle.TranslateX];
            BeginTranslateOffset(target.Position, mousePos, view, projection, viewportSize);
        }
        else if (handle == Handle.TranslateFree)
        {
            _dragAxisOrigin = target.Position;
            _freeTranslatePlaneNormal = Vector3.Normalize(target.Position - eye);
            BeginTranslateOffset(target.Position, mousePos, view, projection, viewportSize);
        }
        else
        {
            _dragRotateAxis = handle - Handle.RotateX;
            BeginRotateAngle(target, mousePos, view, projection, viewportSize);
        }
    }

    private void BeginTranslateOffset(Vector3 objPosition, Vector2 mousePos, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize)
    {
        Picking.Ray ray = Picking.ScreenPointToRay(mousePos, viewportSize, view, projection);
        Vector3 hit = _dragging == Handle.TranslateFree
            ? IntersectRayPlane(ray, _dragAxisOrigin, _freeTranslatePlaneNormal) ?? objPosition
            : ClosestPointOnLine(_dragAxisOrigin, _dragAxisDir, ray);
        _dragOffset = objPosition - hit;
    }

    private void BeginRotateAngle(IGizmoTarget target, Vector2 mousePos, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize)
    {
        Vector2 center = WorldToScreen(target.Position, view, projection, viewportSize) ?? mousePos;
        _dragStartMouseAngle = MathF.Atan2(mousePos.Y - center.Y, mousePos.X - center.X);
        _dragStartRotationDeg = _dragRotateAxis switch { 0 => target.Rotation.X, 1 => target.Rotation.Y, _ => target.Rotation.Z };
    }

    private void ApplyAxisLockKeys(IGizmoTarget target, Vector2 mousePos, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize)
    {
        int axis = KeyPressedEdge(ImGuiKey.X) ? 0 : KeyPressedEdge(ImGuiKey.Y) ? 1 : KeyPressedEdge(ImGuiKey.Z) ? 2 : -1;
        if (axis < 0)
        {
            return;
        }

        bool isTranslate = _dragging is Handle.TranslateX or Handle.TranslateY or Handle.TranslateZ or Handle.TranslateFree;
        if (isTranslate)
        {
            _dragging = Handle.TranslateX + axis;
            _dragAxisOrigin = target.Position;
            _dragAxisDir = AxisDirections[axis];
            BeginTranslateOffset(target.Position, mousePos, view, projection, viewportSize);
        }
        else if (target.SupportsRotation)
        {
            _dragging = Handle.RotateX + axis;
            _dragRotateAxis = axis;
            BeginRotateAngle(target, mousePos, view, projection, viewportSize);
        }
    }

    private void UpdateDrag(IGizmoTarget target, Vector2 mousePos, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize)
    {
        Picking.Ray ray = Picking.ScreenPointToRay(mousePos, viewportSize, view, projection);

        if (_dragging is Handle.TranslateX or Handle.TranslateY or Handle.TranslateZ)
        {
            target.Position = ClosestPointOnLine(_dragAxisOrigin, _dragAxisDir, ray) + _dragOffset;
        }
        else if (_dragging == Handle.TranslateFree)
        {
            if (IntersectRayPlane(ray, _dragAxisOrigin, _freeTranslatePlaneNormal) is { } hit)
            {
                target.Position = hit + _dragOffset;
            }
        }
        else
        {
            Vector2 center = WorldToScreen(target.Position, view, projection, viewportSize) ?? mousePos;
            float angle = MathF.Atan2(mousePos.Y - center.Y, mousePos.X - center.X);
            float deltaDeg = (angle - _dragStartMouseAngle) * (180f / MathF.PI);
            float newValue = _dragStartRotationDeg + deltaDeg;
            target.Rotation = _dragRotateAxis switch
            {
                0 => new Vector3(newValue, target.Rotation.Y, target.Rotation.Z),
                1 => new Vector3(target.Rotation.X, newValue, target.Rotation.Z),
                _ => new Vector3(target.Rotation.X, target.Rotation.Y, newValue),
            };
        }
    }

    private static Vector3 ClosestPointOnLine(Vector3 origin, Vector3 dir, Picking.Ray ray)
    {
        Vector3 w0 = origin - ray.Origin;
        float a = Vector3.Dot(ray.Direction, ray.Direction);
        float b = Vector3.Dot(ray.Direction, dir);
        float c = Vector3.Dot(dir, dir);
        float d = Vector3.Dot(ray.Direction, w0);
        float e = Vector3.Dot(dir, w0);
        float denom = a * c - b * b;

        if (MathF.Abs(denom) < 1e-6f)
        {
            return origin;
        }

        float tc = (a * e - b * d) / denom;
        return origin + dir * tc;
    }

    private static Vector3? IntersectRayPlane(Picking.Ray ray, Vector3 planePoint, Vector3 planeNormal)
    {
        float denom = Vector3.Dot(ray.Direction, planeNormal);
        if (MathF.Abs(denom) < 1e-6f)
        {
            return null;
        }

        float t = Vector3.Dot(planePoint - ray.Origin, planeNormal) / denom;
        return t < 0f ? null : ray.Origin + ray.Direction * t;
    }

    private static Handle HitTest(Vector3 center, float gizmoSize, Vector2 mousePos, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize, bool supportsRotation)
    {
        Handle best = Handle.None;

        float bestDist = float.MaxValue;

        Vector2? centerScreen = WorldToScreen(center, view, projection, viewportSize);
        if (centerScreen is null)
        {
            return Handle.None;
        }

        for (int axis = 0; axis < 3; axis++)
        {
            Vector2? tip = WorldToScreen(center + AxisDirections[axis] * gizmoSize * 1.3f, view, projection, viewportSize);
            if (tip is { } tipScreen)
            {
                float dist = DistancePointToSegment(mousePos, centerScreen.Value, tipScreen);
                if (dist < HitThresholdPixels && dist < bestDist)
                {
                    bestDist = dist;
                    best = Handle.TranslateX + axis;
                }
            }

            if (!supportsRotation)
            {
                continue;
            }

            const int ringSamples = 32;
            float ringRadius = gizmoSize * 1.8f;
            Vector2? prev = null;
            for (int i = 0; i <= ringSamples; i++)
            {
                float t = i / (float)ringSamples * MathF.Tau;
                Vector3 local = axis == 0
                    ? new Vector3(0f, MathF.Cos(t), MathF.Sin(t))
                    : axis == 1
                        ? new Vector3(MathF.Cos(t), 0f, MathF.Sin(t))
                        : new Vector3(MathF.Cos(t), MathF.Sin(t), 0f);
                Vector2? p = WorldToScreen(center + local * ringRadius, view, projection, viewportSize);
                if (p is { } pScreen && prev is { } prevScreen)
                {
                    float dist = DistancePointToSegment(mousePos, prevScreen, pScreen);
                    if (dist < RingHitThresholdPixels && dist < bestDist)
                    {
                        bestDist = dist;
                        best = Handle.RotateX + axis;
                    }
                }

                prev = p;
            }
        }

        return best;
    }

    private void Draw(IGizmoTarget target, float gizmoSize, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize, SceneRenderer renderer)
    {
        Matrix4x4 baseTranslate = Matrix4x4.CreateTranslation(target.Position);

        for (int axis = 0; axis < 3; axis++)
        {
            Handle translateHandle = Handle.TranslateX + axis;
            Vector3 color = ColorFor(translateHandle);
            Matrix4x4 rot = axis switch
            {
                0 => Matrix4x4.Identity,
                1 => Matrix4x4.CreateRotationZ(MathF.PI / 2f),
                _ => Matrix4x4.CreateRotationY(-MathF.PI / 2f),
            };
            Matrix4x4 world = Matrix4x4.CreateScale(gizmoSize) * rot * baseTranslate;
            renderer.RenderTranslateHandle(world, color, view, projection);

            if (!target.SupportsRotation)
            {
                continue;
            }

            Handle rotateHandle = Handle.RotateX + axis;
            Vector3 ringColor = ColorFor(rotateHandle);
            Matrix4x4 ringRot = axis switch
            {
                0 => Matrix4x4.CreateRotationY(MathF.PI / 2f),
                1 => Matrix4x4.CreateRotationX(MathF.PI / 2f),
                _ => Matrix4x4.Identity,
            };
            Matrix4x4 ringWorld = Matrix4x4.CreateScale(gizmoSize * 1.8f) * ringRot * baseTranslate;
            renderer.RenderRotateHandle(ringWorld, ringColor, view, projection, viewportSize);
        }
    }

    private Vector3 ColorFor(Handle handle)
    {
        bool isTranslate = handle is Handle.TranslateX or Handle.TranslateY or Handle.TranslateZ;
        int axis = isTranslate ? handle - Handle.TranslateX : handle - Handle.RotateX;
        bool activeOnThisAxis = _dragging == handle
            || (_dragging == Handle.TranslateFree && isTranslate)
            || (_dragging == Handle.None && _hovered == handle);
        return activeOnThisAxis ? HoverColor : AxisColors[axis];
    }

    private static Vector2? WorldToScreen(Vector3 world, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), view * projection);
        if (clip.W <= 0.0001f)
        {
            return null;
        }

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        return new Vector2((ndc.X * 0.5f + 0.5f) * viewportSize.X, (1f - (ndc.Y * 0.5f + 0.5f)) * viewportSize.Y);
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.LengthSquared();
        float t = lenSq < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        Vector2 closest = a + ab * t;
        return Vector2.Distance(p, closest);
    }
}
