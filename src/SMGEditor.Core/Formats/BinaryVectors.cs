using System.Buffers.Binary;
using System.Numerics;

namespace SMGEditor.Core.Formats;

internal static class BinaryVectors
{
    public static Vector3 ReadVector3BigEndian(byte[] data, int offset) => new(
        BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset, 4)),
        BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 4, 4)),
        BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 8, 4)));
}
