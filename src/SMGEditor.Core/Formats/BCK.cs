using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SMGEditor.Core.Formats;

public readonly record struct BCKKeyframe(float Frame, float Value, float TangentIn, float TangentOut);

public sealed class BCKTrack
{
    public required List<BCKKeyframe> Keyframes { get; init; }

    public float Sample(float frame)
    {
        if (Keyframes.Count == 0)
        {
            return 0f;
        }

        if (Keyframes.Count == 1 || frame <= Keyframes[0].Frame)
        {
            return Keyframes[0].Value;
        }

        for (int i = 1; i < Keyframes.Count; i++)
        {
            if (frame < Keyframes[i].Frame)
            {
                BCKKeyframe prev = Keyframes[i - 1];
                BCKKeyframe next = Keyframes[i];
                float span = next.Frame - prev.Frame;
                if (span <= 0f)
                {
                    return prev.Value;
                }

                float t = (frame - prev.Frame) / span;
                float t2 = t * t;
                float t3 = t2 * t;
                float h00 = (2f * t3) - (3f * t2) + 1f;
                float h10 = t3 - (2f * t2) + t;
                float h01 = (-2f * t3) + (3f * t2);
                float h11 = t3 - t2;
                return (h00 * prev.Value) + (h10 * span * prev.TangentOut) + (h01 * next.Value) + (h11 * span * next.TangentIn);
            }
        }

        return Keyframes[^1].Value;
    }
}

public sealed class BCKJointAnim
{
    public required BCKTrack ScaleX { get; init; }
    public required BCKTrack ScaleY { get; init; }
    public required BCKTrack ScaleZ { get; init; }
    public required BCKTrack RotX { get; init; }
    public required BCKTrack RotY { get; init; }
    public required BCKTrack RotZ { get; init; }
    public required BCKTrack TransX { get; init; }
    public required BCKTrack TransY { get; init; }
    public required BCKTrack TransZ { get; init; }

    public (Vector3 Scale, Vector3 RotationDegrees, Vector3 Translation) Sample(float frame) => (
        new Vector3(ScaleX.Sample(frame), ScaleY.Sample(frame), ScaleZ.Sample(frame)),
        new Vector3(RotX.Sample(frame), RotY.Sample(frame), RotZ.Sample(frame)),
        new Vector3(TransX.Sample(frame), TransY.Sample(frame), TransZ.Sample(frame)));
}

public sealed class BCKAnimation
{
    public required int EndFrame { get; init; }
    public required List<BCKJointAnim> Joints { get; init; }
}

public static class BCKReader
{
    public static BCKAnimation Load(byte[] data, int jointCount)
    {
        uint blockCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x0C, 4));
        int blockOffset = 0x20;
        for (int i = 0; i < blockCount; i++)
        {
            string tag = Encoding.ASCII.GetString(data, blockOffset, 4);
            int blockSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 4, 4));

            if (tag == "ANK1")
            {
                return ReadAnk1(data, blockOffset, jointCount);
            }

            blockOffset += blockSize;
        }

        throw new NotSupportedException("No ANK1 block found in BCK file.");
    }

    private static BCKAnimation ReadAnk1(byte[] data, int blockOffset, int jointCount)
    {
        byte decShift = data[blockOffset + 0x09];
        short duration = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(blockOffset + 0x0A, 2));
        int tableOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x14, 4));
        int scaleDataOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x18, 4));
        int rotDataOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x1C, 4));
        int transDataOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x20, 4));

        float rotationScale = (1 << decShift) * (180f / 32768f);

        var joints = new List<BCKJointAnim>(jointCount);
        for (int j = 0; j < jointCount; j++)
        {
            int xOff = tableOffset + (((j * 3) + 0) * 0x12);
            int yOff = tableOffset + (((j * 3) + 1) * 0x12);
            int zOff = tableOffset + (((j * 3) + 2) * 0x12);

            joints.Add(new BCKJointAnim
            {
                ScaleX = ReadFloatTrack(data, scaleDataOffset, xOff + 0x00, defaultValue: 1f),
                RotX = ReadRotationTrack(data, rotDataOffset, xOff + 0x06, rotationScale),
                TransX = ReadFloatTrack(data, transDataOffset, xOff + 0x0C, defaultValue: 0f),
                ScaleY = ReadFloatTrack(data, scaleDataOffset, yOff + 0x00, defaultValue: 1f),
                RotY = ReadRotationTrack(data, rotDataOffset, yOff + 0x06, rotationScale),
                TransY = ReadFloatTrack(data, transDataOffset, yOff + 0x0C, defaultValue: 0f),
                ScaleZ = ReadFloatTrack(data, scaleDataOffset, zOff + 0x00, defaultValue: 1f),
                RotZ = ReadRotationTrack(data, rotDataOffset, zOff + 0x06, rotationScale),
                TransZ = ReadFloatTrack(data, transDataOffset, zOff + 0x0C, defaultValue: 0f),
            });
        }

        return new BCKAnimation { EndFrame = duration, Joints = joints };
    }

    private static BCKTrack ReadFloatTrack(byte[] data, int dataBase, int descOffset, float defaultValue)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(descOffset, 2));
        ushort keyIndex = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(descOffset + 2, 2));
        ushort type = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(descOffset + 4, 2));

        if (count == 0)
        {
            return new BCKTrack { Keyframes = [new BCKKeyframe(0f, defaultValue, 0f, 0f)] };
        }

        int pos = dataBase + (keyIndex * 4);
        if (count == 1)
        {
            float value = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(pos, 4));
            return new BCKTrack { Keyframes = [new BCKKeyframe(0f, value, 0f, 0f)] };
        }

        int stride = type == 0 ? 3 : 4;
        var keyframes = new List<BCKKeyframe>(count);
        for (int k = 0; k < count; k++)
        {
            int p = pos + (k * stride * 4);
            float kFrame = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(p, 4));
            float value = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(p + 4, 4));
            if (stride == 3)
            {
                float tangent = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(p + 8, 4));
                keyframes.Add(new BCKKeyframe(kFrame, value, tangent, tangent));
            }
            else
            {
                float tangentIn = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(p + 8, 4));
                float tangentOut = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(p + 12, 4));
                keyframes.Add(new BCKKeyframe(kFrame, value, tangentIn, tangentOut));
            }
        }

        return new BCKTrack { Keyframes = keyframes };
    }

    private static BCKTrack ReadRotationTrack(byte[] data, int dataBase, int descOffset, float rotationScale)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(descOffset, 2));
        ushort keyIndex = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(descOffset + 2, 2));
        ushort type = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(descOffset + 4, 2));

        if (count == 0)
        {
            return new BCKTrack { Keyframes = [new BCKKeyframe(0f, 0f, 0f, 0f)] };
        }

        int pos = dataBase + (keyIndex * 2);
        if (count == 1)
        {
            short raw = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(pos, 2));
            return new BCKTrack { Keyframes = [new BCKKeyframe(0f, raw * rotationScale, 0f, 0f)] };
        }

        int stride = type == 0 ? 3 : 4;
        var keyframes = new List<BCKKeyframe>(count);
        for (int k = 0; k < count; k++)
        {
            int p = pos + (k * stride * 2);
            float kFrame = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p, 2));
            float value = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 2, 2)) * rotationScale;
            if (stride == 3)
            {
                float tangent = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 4, 2)) * rotationScale;
                keyframes.Add(new BCKKeyframe(kFrame, value, tangent, tangent));
            }
            else
            {
                float tangentIn = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 4, 2)) * rotationScale;
                float tangentOut = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 6, 2)) * rotationScale;
                keyframes.Add(new BCKKeyframe(kFrame, value, tangentIn, tangentOut));
            }
        }

        return new BCKTrack { Keyframes = keyframes };
    }
}
