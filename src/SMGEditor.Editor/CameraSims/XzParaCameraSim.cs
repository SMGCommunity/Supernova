using System.Numerics;
using SMGEditor.Viewer;

namespace SMGEditor.Editor.CameraSims;

internal static class XzParaCameraSim
{
    public readonly record struct Result(Vector3 Eye, Vector3 WatchPos, Matrix4x4 View, Matrix4x4 Projection, string DebugText);

    public static Result Compute(
        IReadOnlyDictionary<string, object?> camFields,
        Vector3 zoneRotation,
        Vector3 playerPos,
        float playerYawDeg,
        float panAngleDeg,
        float aspect,
        float nearPlane,
        float farPlane)
    {
        float angleBRad = camFields.TryGetValue("angleB", out object? ab) && ab is float abf ? abf : 0f;
        float angleARad = camFields.TryGetValue("angleA", out object? aa) && aa is float aaf ? aaf : 0f;
        float camDist = camFields.TryGetValue("dist", out object? dv) && dv is float dvf ? MathF.Max(dvf, 300f) : 1200f;

        bool noFovy = camFields.TryGetValue("flag.nofovy", out object? nfv) && nfv is int nfvi && nfvi != 0;
        float camFovyDeg = camFields.TryGetValue("fovy", out object? fv) && fv is float fvf ? fvf : 45f;
        float wOffsetX = camFields.TryGetValue("woffset.X", out object? wx) && wx is float wxf ? wxf : 0f;
        float wOffsetY = camFields.TryGetValue("woffset.Y", out object? wy) && wy is float wyf ? wyf : 0f;
        float wOffsetZ = camFields.TryGetValue("woffset.Z", out object? wz) && wz is float wzf ? wzf : 0f;
        float frontOffset = camFields.TryGetValue("loffset", out object? lo) && lo is float lof ? lof : 0f;
        float upperOffset = camFields.TryGetValue("loffsetv", out object? lv) && lv is float lvf ? lvf : 0f;

        float elevationDeg = 180f * angleBRad / MathF.PI;
        float azimuthBaseDeg = 180f * angleARad / MathF.PI;
        float elevationRad = elevationDeg * MathF.PI / 180f;
        float azimuthRad = (azimuthBaseDeg - 90f + panAngleDeg) * MathF.PI / 180f;

        Vector3 localOrbitOffset = new(
            camDist * MathF.Cos(elevationRad) * MathF.Sin(azimuthRad),
            camDist * MathF.Sin(elevationRad),
            camDist * MathF.Cos(elevationRad) * MathF.Cos(azimuthRad));

        Matrix4x4 zoneMatrix = GalaxyLoader.ComposePlacementMatrix(Vector3.Zero, zoneRotation, Vector3.One);
        Vector3 orbitOffset = Vector3.TransformNormal(localOrbitOffset, zoneMatrix);
        Vector3 zoneUp = Vector3.TransformNormal(Vector3.UnitY, zoneMatrix);

        float yawRad = playerYawDeg * MathF.PI / 180f;
        Vector3 playerFront = Vector3.TransformNormal(Vector3.UnitZ, Matrix4x4.CreateRotationY(yawRad));
        Vector3 localOffs = playerFront * frontOffset + Vector3.UnitY * upperOffset;
        Vector3 globalOffs = Vector3.TransformNormal(new Vector3(wOffsetX, wOffsetY, wOffsetZ), zoneMatrix);
        Vector3 watchPos = playerPos + globalOffs + localOffs;
        Vector3 eye = watchPos + orbitOffset;

        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, watchPos, zoneUp);
        float fovyRad = Math.Clamp(camFovyDeg, 1f, 179f) * MathF.PI / 180f;
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(fovyRad, aspect, nearPlane, farPlane);

        string debugText =
            $"elev {elevationDeg:F1} deg | azimuth(base) {azimuthBaseDeg:F1} deg | azimuth(final) {(azimuthBaseDeg - 90f + panAngleDeg):F1} deg | " +
            $"dist {camDist:F0} | fovy {camFovyDeg:F0} (nofovy={noFovy}) | woffset ({wOffsetX:F0},{wOffsetY:F0},{wOffsetZ:F0}) | zoneRotY {zoneRotation.Y:F1} | " +
            $"player ({playerPos.X:F0},{playerPos.Y:F0},{playerPos.Z:F0}) | " +
            $"watch ({watchPos.X:F0},{watchPos.Y:F0},{watchPos.Z:F0}) | eye ({eye.X:F0},{eye.Y:F0},{eye.Z:F0})";

        return new Result(eye, watchPos, view, projection, debugText);
    }
}
