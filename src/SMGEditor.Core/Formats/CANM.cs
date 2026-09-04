using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SMGEditor.Core.Formats;

public readonly record struct CANMKeyframe(float Frame, float Value, float InSlope = 0f, float OutSlope = 0f);

public enum CANMTrackType
{
    CANM,
    Ckan,
}

public sealed class CANMTrack
{
    public required CANMTrackType Type { get; init; }

    public required List<CANMKeyframe> Keyframes { get; init; }

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
                CANMKeyframe prev = Keyframes[i - 1];
                CANMKeyframe next = Keyframes[i];
                float span = next.Frame - prev.Frame;
                float t = (frame - prev.Frame) / span;

                if (Type == CANMTrackType.CANM)
                {
                    return prev.Value + ((next.Value - prev.Value) * t);
                }

                const float tangentScale = 1f / 30f;
                float t2 = t * t;
                float t3 = t2 * t;
                float h00 = (2f * t3) - (3f * t2) + 1f;
                float h10 = t3 - (2f * t2) + t;
                float h01 = (-2f * t3) + (3f * t2);
                float h11 = t3 - t2;
                return (h00 * prev.Value) + (h10 * span * prev.OutSlope * tangentScale) + (h01 * next.Value) + (h11 * span * next.InSlope * tangentScale);
            }
        }

        return Keyframes[^1].Value;
    }
}

public sealed class CANMAnimation
{
    public required int EndFrame { get; init; }
    public required CANMTrack PositionX { get; init; }
    public required CANMTrack PositionY { get; init; }
    public required CANMTrack PositionZ { get; init; }
    public required CANMTrack TargetX { get; init; }
    public required CANMTrack TargetY { get; init; }
    public required CANMTrack TargetZ { get; init; }
    public required CANMTrack Twist { get; init; }
    public required CANMTrack FovY { get; init; }

    public (Vector3 Eye, Vector3 Target, float TwistDeg, float FovYDeg) Sample(float frame) => (
        new Vector3(PositionX.Sample(frame), PositionY.Sample(frame), PositionZ.Sample(frame)),
        new Vector3(TargetX.Sample(frame), TargetY.Sample(frame), TargetZ.Sample(frame)),
        Twist.Sample(frame),
        FovY.Sample(frame));
}

public static class CANMReader
{
    public static CANMAnimation Load(byte[] data)
    {
        string frameType = Encoding.ASCII.GetString(data, 0x04, 4);
        CANMTrackType type = frameType switch
        {
            "CANM" => CANMTrackType.CANM,
            "CKAN" => CANMTrackType.Ckan,
            _ => throw new NotSupportedException($"Unknown camera animation frame type '{frameType}'."),
        };

        int endFrame = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0x18, 4));
        uint trackHeaderSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x1C, 4));
        int keyframeDataOffset = 0x20 + (int)trackHeaderSize;
        int trackHeaderStride = type == CANMTrackType.Ckan ? 0xC : 0x8;

        int headerOffset = 0x20;
        CANMTrack ReadNextTrack()
        {
            CANMTrack track = ReadTrack(data, headerOffset, keyframeDataOffset, type);
            headerOffset += trackHeaderStride;
            return track;
        }

        return new CANMAnimation
        {
            EndFrame = endFrame,
            PositionX = ReadNextTrack(),
            PositionY = ReadNextTrack(),
            PositionZ = ReadNextTrack(),
            TargetX = ReadNextTrack(),
            TargetY = ReadNextTrack(),
            TargetZ = ReadNextTrack(),
            Twist = ReadNextTrack(),
            FovY = ReadNextTrack(),
        };
    }

    private static CANMTrack ReadTrack(byte[] data, int headerOffset, int keyframeDataOffset, CANMTrackType type)
    {
        uint keyCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(headerOffset, 4));
        uint beginIndex = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(headerOffset + 4, 4));

        int valuesStart = keyframeDataOffset + 4 + (int)(4 * beginIndex);
        var keyframes = new List<CANMKeyframe>((int)keyCount);

        if (keyCount == 1)
        {
            float value = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(valuesStart, 4));
            keyframes.Add(new CANMKeyframe(0f, value));
        }
        else
        {
            int pos = valuesStart;
            for (int i = 0; i < keyCount; i++)
            {
                float frame = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(pos, 4));
                float value = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(pos + 4, 4));
                pos += 8;

                float slope = 0f;
                if (type == CANMTrackType.Ckan)
                {
                    slope = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(pos, 4));
                    pos += 4;
                }

                keyframes.Add(new CANMKeyframe(frame, value, slope, slope));
            }
        }

        return new CANMTrack { Type = type, Keyframes = keyframes };
    }
}
