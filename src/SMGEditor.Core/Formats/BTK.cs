using System.Buffers.Binary;

namespace SMGEditor.Core.Formats;

public sealed class BTKUvAnimEntry
{
    public required string MaterialName { get; init; }

    public required int TexGenIndex { get; init; }

    public required int EndFrame { get; init; }

    public required float CenterS { get; init; }
    public required float CenterT { get; init; }
    public required BCKTrack ScaleS { get; init; }
    public required BCKTrack ScaleT { get; init; }

    public required BCKTrack RotationQ { get; init; }

    public required BCKTrack TranslationS { get; init; }
    public required BCKTrack TranslationT { get; init; }

    public (float A, float B, float C, float D, float Tx, float Ty) SampleMatrix(float frame)
    {
        float scaleS = ScaleS.Sample(frame);
        float scaleT = ScaleT.Sample(frame);
        float rotationDegrees = RotationQ.Sample(frame);
        float translationS = TranslationS.Sample(frame);
        float translationT = TranslationT.Sample(frame);

        float theta = rotationDegrees * MathF.PI / 180f;
        float sinR = MathF.Sin(theta);
        float cosR = MathF.Cos(theta);

        float a = scaleS * cosR;
        float b = scaleS * -sinR;
        float tx = translationS + CenterS - ((a * CenterS) + (b * CenterT));

        float c = scaleT * sinR;
        float d = scaleT * cosR;
        float ty = translationT + CenterT - ((c * CenterS) + (d * CenterT));

        return (a, b, c, d, tx, ty);
    }
}

public sealed class BTKAnimation
{
    public required int EndFrame { get; init; }
    public required List<BTKUvAnimEntry> Entries { get; init; }

    public required bool IsMaya { get; init; }
}

public static class BTKReader
{
    public static BTKAnimation Load(byte[] data)
    {
        uint blockCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x0C, 4));
        int blockOffset = 0x20;
        for (int i = 0; i < blockCount; i++)
        {
            string tag = BDLTables.ReadTag(data, blockOffset);
            int blockSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 4, 4));

            if (tag == "TTK1")
            {
                return ReadTtk1(data, blockOffset);
            }

            blockOffset += blockSize;
        }

        throw new NotSupportedException("No TTK1 block found in BTK file.");
    }

    private static BTKAnimation ReadTtk1(byte[] data, int blockOffset)
    {
        byte rotationDecimal = data[blockOffset + 0x09];
        ushort duration = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x0A, 2));
        int materialCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x0C, 2)) / 3;

        int animTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x14);
        int materialNameTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x1C);
        int texGenIndexTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x20);
        int textureCenterTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x24);
        int sTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x28);
        int rTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x2C);
        int tTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x30);
        bool isMaya = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x5C, 4)) == 1;

        float rotationScale = MathF.Pow(2f, rotationDecimal) / 32767f * 180f;

        IReadOnlyList<string> materialNames = BDLTables.ReadNameTable(data, materialNameTableOffset);

        var entries = new List<BTKUvAnimEntry>(materialCount);
        int tableIdx = animTableOffset;
        for (int i = 0; i < materialCount; i++)
        {
            BCKTrack scaleS = ReadFloatTrack(data, sTableOffset, ref tableIdx);
            ReadFloatTrack(data, rTableOffset, ref tableIdx);
            BCKTrack translationS = ReadFloatTrack(data, tTableOffset, ref tableIdx);
            BCKTrack scaleT = ReadFloatTrack(data, sTableOffset, ref tableIdx);
            ReadFloatTrack(data, rTableOffset, ref tableIdx);
            BCKTrack translationT = ReadFloatTrack(data, tTableOffset, ref tableIdx);
            ReadFloatTrack(data, sTableOffset, ref tableIdx);
            BCKTrack rotationQ = ReadRotationTrack(data, rTableOffset, ref tableIdx, rotationScale);
            ReadFloatTrack(data, tTableOffset, ref tableIdx);

            int texGenIndex = data[texGenIndexTableOffset + i];
            float centerS = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(textureCenterTableOffset + (i * 0x0C) + 0x00, 4));
            float centerT = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(textureCenterTableOffset + (i * 0x0C) + 0x04, 4));

            entries.Add(new BTKUvAnimEntry
            {
                MaterialName = materialNames[i],
                TexGenIndex = texGenIndex,
                EndFrame = duration,
                CenterS = centerS,
                CenterT = centerT,
                ScaleS = scaleS,
                ScaleT = scaleT,
                RotationQ = rotationQ,
                TranslationS = translationS,
                TranslationT = translationT,
            });
        }

        return new BTKAnimation { EndFrame = duration, Entries = entries, IsMaya = isMaya };
    }

    private static BCKTrack ReadFloatTrack(byte[] data, int dataBase, ref int tableIdx)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tableIdx, 2));
        ushort index = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tableIdx + 2, 2));
        ushort tangentType = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tableIdx + 4, 2));
        tableIdx += 6;

        if (count == 0)
        {
            return new BCKTrack { Keyframes = [new BCKKeyframe(0f, 0f, 0f, 0f)] };
        }

        int pos = dataBase + (index * 4);
        if (count == 1)
        {
            float value = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(pos, 4));
            return new BCKTrack { Keyframes = [new BCKKeyframe(0f, value, 0f, 0f)] };
        }

        int stride = tangentType == 0 ? 3 : 4;
        var keyframes = new List<BCKKeyframe>(count);
        for (int k = 0; k < count; k++)
        {
            int p = pos + (k * stride * 4);
            float frame = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(p, 4));
            float value = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(p + 4, 4));
            if (stride == 3)
            {
                float tangent = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(p + 8, 4));
                keyframes.Add(new BCKKeyframe(frame, value, tangent, tangent));
            }
            else
            {
                float tangentIn = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(p + 8, 4));
                float tangentOut = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(p + 12, 4));
                keyframes.Add(new BCKKeyframe(frame, value, tangentIn, tangentOut));
            }
        }

        return new BCKTrack { Keyframes = keyframes };
    }

    private static BCKTrack ReadRotationTrack(byte[] data, int dataBase, ref int tableIdx, float rotationScale)
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
            short raw = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(pos, 2));
            return new BCKTrack { Keyframes = [new BCKKeyframe(0f, raw * rotationScale, 0f, 0f)] };
        }

        int stride = tangentType == 0 ? 3 : 4;
        var keyframes = new List<BCKKeyframe>(count);
        for (int k = 0; k < count; k++)
        {
            int p = pos + (k * stride * 2);
            float frame = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p, 2));
            float value = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 2, 2)) * rotationScale;
            if (stride == 3)
            {
                float tangent = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 4, 2)) * rotationScale;
                keyframes.Add(new BCKKeyframe(frame, value, tangent, tangent));
            }
            else
            {
                float tangentIn = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 4, 2)) * rotationScale;
                float tangentOut = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 6, 2)) * rotationScale;
                keyframes.Add(new BCKKeyframe(frame, value, tangentIn, tangentOut));
            }
        }

        return new BCKTrack { Keyframes = keyframes };
    }
}
