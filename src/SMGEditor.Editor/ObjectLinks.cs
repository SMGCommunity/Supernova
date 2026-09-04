using System.Numerics;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal enum ObjectLinkKind
{
    Switch,
    ObjId,
}

internal readonly record struct ObjectLink(EditableObject Target, ObjectLinkKind Kind, string SourceField, string TargetField, int Value);

internal static class ObjectLinks
{
    private static readonly string[] SwitchFieldNames = ["SW_APPEAR", "SW_DEAD", "SW_A", "SW_B", "SW_SLEEP", "SW_AWAKE", "SW_PARAM"];

    public static readonly Vector3 SwitchColor = new(1f, 0.85f, 0.2f);
    public static readonly Vector3 ObjIdColor = new(0.3f, 0.7f, 1f);

    public static List<ObjectLink> FindLinks(EditableObject source, IReadOnlyList<EditableObject> candidates)
    {
        var links = new List<ObjectLink>();

        foreach (string sourceField in SwitchFieldNames)
        {
            if (!TryGetId(source, sourceField, out int switchId))
            {
                continue;
            }

            foreach (EditableObject other in candidates)
            {
                if (ReferenceEquals(other, source))
                {
                    continue;
                }

                foreach (string otherField in SwitchFieldNames)
                {
                    if (TryGetId(other, otherField, out int otherId) && otherId == switchId)
                    {
                        links.Add(new ObjectLink(other, ObjectLinkKind.Switch, sourceField, otherField, switchId));
                    }
                }
            }
        }

        if (TryGetId(source, "Obj_ID", out int objId))
        {
            foreach (EditableObject other in candidates)
            {
                if (!ReferenceEquals(other, source) && TryGetId(other, "l_id", out int otherLId) && otherLId == objId)
                {
                    links.Add(new ObjectLink(other, ObjectLinkKind.ObjId, "Obj_ID", "l_id", objId));
                }
            }
        }

        if (TryGetId(source, "l_id", out int sourceLId))
        {
            foreach (EditableObject other in candidates)
            {
                if (!ReferenceEquals(other, source) && TryGetId(other, "Obj_ID", out int otherObjId) && otherObjId == sourceLId)
                {
                    links.Add(new ObjectLink(other, ObjectLinkKind.ObjId, "l_id", "Obj_ID", sourceLId));
                }
            }
        }

        return links;
    }

    private static bool TryGetId(EditableObject obj, string field, out int value)
    {
        if (obj.Fields.TryGetValue(field, out object? raw) && raw is int i && i != -1)
        {
            value = i;
            return true;
        }

        value = -1;
        return false;
    }

    public static (Matrix4x4 OutlineWorld, Vector3 Center) ComputeOutline(EditableObject obj)
    {
        if (obj.Instance is { } inst)
        {
            LoadedObject model = inst.Object;
            Vector3 center = (model.LocalBoundsMin + model.LocalBoundsMax) / 2f;
            Vector3 half = (model.LocalBoundsMax - model.LocalBoundsMin) / 2f;
            Matrix4x4 outlineWorld = Matrix4x4.CreateScale(Vector3.Max(half, new Vector3(1f))) * Matrix4x4.CreateTranslation(center) * inst.WorldMatrix;
            return (outlineWorld, Vector3.Transform(center, inst.WorldMatrix));
        }

        Matrix4x4 world = GalaxyLoader.ComposePlacementMatrix(obj.Position, obj.Rotation, Vector3.One);
        Matrix4x4 placeholderOutline = Matrix4x4.CreateScale(SceneRenderer.PlaceholderBoxHalfExtent) * world;
        return (placeholderOutline, obj.Position);
    }

    public static void DrawArrow(SceneRenderer renderer, Vector3 from, Vector3 to, Vector3 color, Matrix4x4 view, Matrix4x4 projection, float lineWidth = 2.5f)
    {
        renderer.RenderPath([from, to], color, view, projection, lineWidth);

        Vector3 dir = to - from;
        float len = dir.Length();
        if (len < 1f)
        {
            return;
        }

        dir /= len;
        Vector3 reference = MathF.Abs(Vector3.Dot(dir, Vector3.UnitY)) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 side = Vector3.Normalize(Vector3.Cross(dir, reference));

        float headLen = Math.Min(len * 0.15f, 80f);
        Vector3 back = to - dir * headLen;
        Vector3 wingA = back + side * headLen * 0.5f;
        Vector3 wingB = back - side * headLen * 0.5f;

        renderer.RenderPath([wingA, to, wingB], color, view, projection, lineWidth);
    }
}
