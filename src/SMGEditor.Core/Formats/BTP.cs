using System.Buffers.Binary;

namespace SMGEditor.Core.Formats;

public readonly record struct BTPTextureOverride(string MaterialName, IReadOnlyList<ushort> FrameTextureIndices)
{
    public ushort Frame0TextureIndex => FrameTextureIndices.Count > 0 ? FrameTextureIndices[0] : (ushort)0;

    public ushort TextureIndexAtFrame(int frame)
    {
        if (FrameTextureIndices.Count == 0)
        {
            return 0;
        }

        return FrameTextureIndices[Math.Clamp(frame, 0, FrameTextureIndices.Count - 1)];
    }
}

public static class BTPReader
{
    public static List<BTPTextureOverride> Read(byte[] data)
    {
        uint blockCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0xC, 4));

        var overrides = new List<BTPTextureOverride>();
        int blockOffset = 0x20;
        for (int i = 0; i < blockCount; i++)
        {
            string tag = BDLTables.ReadTag(data, blockOffset);
            int blockSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 4, 4));

            if (tag == "TPT1")
            {
                overrides.AddRange(ReadTexPattern(data, blockOffset));
            }

            blockOffset += blockSize;
        }

        return overrides;
    }

    private static List<BTPTextureOverride> ReadTexPattern(byte[] data, int blockOffset)
    {
        ushort materialCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0xC, 2));
        int tableOffset = BDLTables.ReadOffset(data, blockOffset, 0x10);
        int valuesOffset = BDLTables.ReadOffset(data, blockOffset, 0x14);
        int nameTabOffset = BDLTables.ReadOffset(data, blockOffset, 0x1C);

        IReadOnlyList<string> names = BDLTables.ReadNameTable(data, nameTabOffset);

        var overrides = new List<BTPTextureOverride>(materialCount);
        for (int i = 0; i < materialCount && i < names.Count; i++)
        {
            int entry = tableOffset + i * 0x8;
            ushort frameCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x0, 2));
            ushort firstIndex = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x2, 2));

            var frames = new ushort[Math.Max((int)frameCount, 1)];
            for (int f = 0; f < frames.Length; f++)
            {
                frames[f] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(valuesOffset + (firstIndex + f) * 2, 2));
            }

            overrides.Add(new BTPTextureOverride(names[i], frames));
        }

        return overrides;
    }
}
