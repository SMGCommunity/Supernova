using System.Buffers.Binary;

namespace SMGEditor.Core.Formats;

public sealed class BRKColorAnimEntry
{
    public required string MaterialName { get; init; }

    public required int Index { get; init; }

    public required bool IsKonst { get; init; }
    public required BCKTrack R { get; init; }
    public required BCKTrack G { get; init; }
    public required BCKTrack B { get; init; }
    public required BCKTrack A { get; init; }

    public BDLColor SampleAsByteColor(float frame) => new(
        (byte)Math.Clamp(R.Sample(frame), 0f, 255f),
        (byte)Math.Clamp(G.Sample(frame), 0f, 255f),
        (byte)Math.Clamp(B.Sample(frame), 0f, 255f),
        (byte)Math.Clamp(A.Sample(frame), 0f, 255f));
}

public sealed class BRKAnimation
{
    public required int EndFrame { get; init; }
    public required List<BRKColorAnimEntry> Entries { get; init; }

    public BDLMaterial ApplyToMaterial(BDLMaterial baseMaterial, float frame)
    {
        var registers = baseMaterial.TevRegisters.ToList();
        var konsts = baseMaterial.TevKonstColors.ToList();

        foreach (BRKColorAnimEntry entry in Entries)
        {
            if (entry.MaterialName != baseMaterial.Name)
            {
                continue;
            }

            BDLColor sampled = entry.SampleAsByteColor(frame);
            if (entry.IsKonst)
            {
                if (entry.Index >= 0 && entry.Index < konsts.Count)
                {
                    konsts[entry.Index] = sampled;
                }
            }
            else if (entry.Index >= 0 && entry.Index < registers.Count)
            {
                registers[entry.Index] = new BDLTevRegisterColor(sampled.R, sampled.G, sampled.B, sampled.A);
            }
        }

        return baseMaterial.With(tevRegisters: registers, tevKonstColors: konsts);
    }
}

public static class BRKReader
{
    public static BRKAnimation Load(byte[] data)
    {
        uint blockCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x0C, 4));
        int blockOffset = 0x20;
        for (int i = 0; i < blockCount; i++)
        {
            string tag = BDLTables.ReadTag(data, blockOffset);
            int blockSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 4, 4));

            if (tag == "TRK1")
            {
                return ReadTrk1(data, blockOffset);
            }

            blockOffset += blockSize;
        }

        throw new NotSupportedException("No TRK1 block found in BRK file.");
    }

    private static BRKAnimation ReadTrk1(byte[] data, int blockOffset)
    {
        ushort duration = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x0A, 2));
        ushort registerCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x0C, 2));
        ushort konstCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x0E, 2));

        int registerAnimTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x20);
        int konstAnimTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x24);
        int registerNameTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x30);
        int konstNameTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x34);
        int registerROffset = BDLTables.ReadOffset(data, blockOffset, 0x38);
        int registerGOffset = BDLTables.ReadOffset(data, blockOffset, 0x3C);
        int registerBOffset = BDLTables.ReadOffset(data, blockOffset, 0x40);
        int registerAOffset = BDLTables.ReadOffset(data, blockOffset, 0x44);
        int konstROffset = BDLTables.ReadOffset(data, blockOffset, 0x48);
        int konstGOffset = BDLTables.ReadOffset(data, blockOffset, 0x4C);
        int konstBOffset = BDLTables.ReadOffset(data, blockOffset, 0x50);
        int konstAOffset = BDLTables.ReadOffset(data, blockOffset, 0x54);

        IReadOnlyList<string> registerNames = BDLTables.ReadNameTable(data, registerNameTableOffset);
        IReadOnlyList<string> konstNames = BDLTables.ReadNameTable(data, konstNameTableOffset);

        var entries = new List<BRKColorAnimEntry>(registerCount + konstCount);

        int tableIdx = registerAnimTableOffset;
        for (int i = 0; i < registerCount; i++)
        {
            BCKTrack r = ReadTrack(data, registerROffset, ref tableIdx);
            BCKTrack g = ReadTrack(data, registerGOffset, ref tableIdx);
            BCKTrack b = ReadTrack(data, registerBOffset, ref tableIdx);
            BCKTrack a = ReadTrack(data, registerAOffset, ref tableIdx);
            byte colorId = data[tableIdx];
            tableIdx += 4;

            entries.Add(new BRKColorAnimEntry { MaterialName = registerNames[i], Index = colorId, IsKonst = false, R = r, G = g, B = b, A = a });
        }

        tableIdx = konstAnimTableOffset;
        for (int i = 0; i < konstCount; i++)
        {
            BCKTrack r = ReadTrack(data, konstROffset, ref tableIdx);
            BCKTrack g = ReadTrack(data, konstGOffset, ref tableIdx);
            BCKTrack b = ReadTrack(data, konstBOffset, ref tableIdx);
            BCKTrack a = ReadTrack(data, konstAOffset, ref tableIdx);
            byte colorId = data[tableIdx];
            tableIdx += 4;

            entries.Add(new BRKColorAnimEntry { MaterialName = konstNames[i], Index = colorId, IsKonst = true, R = r, G = g, B = b, A = a });
        }

        return new BRKAnimation { EndFrame = duration, Entries = entries };
    }

    private static BCKTrack ReadTrack(byte[] data, int dataBase, ref int tableIdx)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tableIdx, 2));
        ushort index = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tableIdx + 2, 2));
        ushort tangentType = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tableIdx + 4, 2));
        tableIdx += 6;

        if (count == 0)
        {
            return new BCKTrack { Keyframes = [new BCKKeyframe(0f, 0f, 0f, 0f)] };
        }

        int pos = dataBase + (index * 2);
        if (count == 1)
        {
            short value = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(pos, 2));
            return new BCKTrack { Keyframes = [new BCKKeyframe(0f, value, 0f, 0f)] };
        }

        int stride = tangentType == 0 ? 3 : 4;
        var keyframes = new List<BCKKeyframe>(count);
        for (int k = 0; k < count; k++)
        {
            int p = pos + (k * stride * 2);
            float frame = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p, 2));
            float value = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 2, 2));
            if (stride == 3)
            {
                float tangent = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 4, 2));
                keyframes.Add(new BCKKeyframe(frame, value, tangent, tangent));
            }
            else
            {
                float tangentIn = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 4, 2));
                float tangentOut = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 6, 2));
                keyframes.Add(new BCKKeyframe(frame, value, tangentIn, tangentOut));
            }
        }

        return new BCKTrack { Keyframes = keyframes };
    }
}
