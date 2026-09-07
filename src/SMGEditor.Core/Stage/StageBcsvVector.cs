using System.Numerics;

namespace SMGEditor.Core.Stage;

internal static class StageBcsvVector
{
    public static Vector3 ReadVector3(IReadOnlyDictionary<string, object?> row, string x, string y, string z, Vector3? fallback = null)
    {
        if (row.TryGetValue(x, out object? xv) && xv is float xf &&
            row.TryGetValue(y, out object? yv) && yv is float yf &&
            row.TryGetValue(z, out object? zv) && zv is float zf)
        {
            return new Vector3(xf, yf, zf);
        }

        return fallback ?? Vector3.Zero;
    }
}
