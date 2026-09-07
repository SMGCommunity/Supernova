using System.Buffers.Binary;
using System.Numerics;

namespace SMGEditor.Core.Formats;

public readonly record struct KclPrism(float Height, ushort PositionIndex, ushort NormalIndex, ushort EdgeIndex0, ushort EdgeIndex1, ushort EdgeIndex2, ushort Attribute)
{
    public ushort EdgeIndex(int i) => i switch
    {
        0 => EdgeIndex0,
        1 => EdgeIndex1,
        2 => EdgeIndex2,
        _ => throw new ArgumentOutOfRangeException(nameof(i)),
    };
}

public sealed class KclFile
{
    public required byte[] Data { get; init; }
    public required int PosOffset { get; init; }
    public required int NormOffset { get; init; }
    public required int PrismOffset { get; init; }
    public required KclPrism[] Prisms { get; init; }
    public required int OctreeOffset { get; init; }
    public required float Thickness { get; init; }
    public required Vector3 Min { get; init; }
    public required int XMask { get; init; }
    public required int YMask { get; init; }
    public required int ZMask { get; init; }
    public required int BlockWidthShift { get; init; }
    public required int BlockXShift { get; init; }
    public required int BlockXYShift { get; init; }

    public int PositionCount => (NormOffset - PosOffset) / 12;

    public int NormalCount => (PrismOffset - NormOffset) / 12;

    public int TriangleCount => Prisms.Length - 1;

    public Vector3 GetPosition(int index) => BinaryVectors.ReadVector3BigEndian(Data, PosOffset + index * 12);

    public Vector3 GetNormal(int index) => BinaryVectors.ReadVector3BigEndian(Data, NormOffset + index * 12);

    public KclPrism GetPrism(int triangleIndex) => Prisms[1 + triangleIndex];

    public void SetPrism(int triangleIndex, KclPrism prism) => Prisms[1 + triangleIndex] = prism;

    public static KclFile Load(byte[] raw)
    {
        byte[] data = Yaz0.IsCompressed(raw) ? Yaz0.Decompress(raw) : raw;

        uint posOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x0, 4));
        uint normOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x4, 4));
        uint prismOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x8, 4));
        uint octreeOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0xC, 4));
        float thickness = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(0x10, 4));
        Vector3 min = BinaryVectors.ReadVector3BigEndian(data, 0x14);
        int xMask = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0x20, 4));
        int yMask = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0x24, 4));
        int zMask = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0x28, 4));
        int blockWidthShift = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0x2C, 4));
        int blockXShift = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0x30, 4));
        int blockXYShift = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0x34, 4));

        int prismCount = (int)(octreeOffset - prismOffset) / 16;
        var prisms = new KclPrism[prismCount];
        for (int i = 0; i < prismCount; i++)
        {
            int o = (int)prismOffset + i * 16;
            prisms[i] = new KclPrism(
                BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(o, 4)),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 4, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 6, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 8, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 10, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 12, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 14, 2)));
        }

        return new KclFile
        {
            Data = data,
            PosOffset = (int)posOffset,
            NormOffset = (int)normOffset,
            PrismOffset = (int)prismOffset,
            Prisms = prisms,
            OctreeOffset = (int)octreeOffset,
            Thickness = thickness,
            Min = min,
            XMask = xMask,
            YMask = yMask,
            ZMask = zMask,
            BlockWidthShift = blockWidthShift,
            BlockXShift = blockXShift,
            BlockXYShift = blockXYShift,
        };
    }
}
