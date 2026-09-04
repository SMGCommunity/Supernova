using System.Buffers.Binary;

namespace SMGEditor.Core.Formats;

public static class Yaz0
{
    private const int HeaderSize = 16;

    // Yaz0 magic is a tell that it is compressed
    public static bool IsCompressed(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize && data[0] == (byte)'Y' && data[1] == (byte)'a' && data[2] == (byte)'z' && data[3] == (byte)'0';

    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        if (!IsCompressed(input))
        {
            throw new InvalidDataException("Data does not have a Yaz0 header.");
        }

        uint decompressedSize = BinaryPrimitives.ReadUInt32BigEndian(input[4..8]);
        byte[] output = new byte[decompressedSize];

        int srcPos = HeaderSize;
        int dstPos = 0;
        byte flagByte = 0;
        int validBitCount = 0;

        while (dstPos < output.Length)
        {
            if (validBitCount == 0)
            {
                flagByte = input[srcPos++];
                validBitCount = 8;
            }

            if ((flagByte & 0x80) != 0)
            {
                output[dstPos++] = input[srcPos++];
            }
            else
            {
                byte byte1 = input[srcPos++];
                byte byte2 = input[srcPos++];

                int distance = (((byte1 & 0xF) << 8) | byte2) + 1;
                int copySrc = dstPos - distance;

                int count = byte1 >> 4;
                count = count == 0 ? input[srcPos++] + 0x12 : count + 2;

                for (int i = 0; i < count; i++)
                {
                    output[dstPos++] = output[copySrc++];
                }
            }

            flagByte <<= 1;
            validBitCount--;
        }

        return output;
    }

    private const int MinMatchLength = 3;

    private const int MaxInlineLength = 17;

    private const int MaxExtendedLength = 0xFF + 0x12;

    private const int MaxDistance = 4096;

    public static byte[] Compress(ReadOnlySpan<byte> inputSpan)
    {
        byte[] input = inputSpan.ToArray();

        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(header[0..4], 0x59617A30);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..8], (uint)input.Length);
        output.Write(header);

        var headTable = new int[1 << 16];
        Array.Fill(headTable, -1);
        var chain = new int[input.Length];

        int Hash3(int pos) => ((input[pos] << 8) | input[pos + 1]) ^ (input[pos + 2] << 3) & 0xFFFF;

        (int Length, int Distance) FindMatch(int pos)
        {
            if (pos + MinMatchLength > input.Length)
            {
                return (0, 0);
            }

            int bestLength = 0;
            int bestDistance = 0;
            int maxLength = Math.Min(MaxExtendedLength, input.Length - pos);

            int candidate = headTable[Hash3(pos)];
            int triesLeft = 64;
            while (candidate >= 0 && pos - candidate <= MaxDistance && triesLeft-- > 0)
            {
                if (input[candidate + bestLength] == input[pos + bestLength])
                {
                    int length = 0;
                    while (length < maxLength && input[candidate + length] == input[pos + length])
                    {
                        length++;
                    }

                    if (length > bestLength)
                    {
                        bestLength = length;
                        bestDistance = pos - candidate;
                        if (length >= maxLength)
                        {
                            break;
                        }
                    }
                }

                candidate = chain[candidate];
            }

            return bestLength >= MinMatchLength ? (bestLength, bestDistance) : (0, 0);
        }

        void InsertHash(int pos)
        {
            int h = Hash3(pos);
            chain[pos] = headTable[h];
            headTable[h] = pos;
        }

        var groupBuffer = new List<byte>(24);
        byte flagByte = 0;
        int flagBitsUsed = 0;

        void FlushGroup()
        {
            if (flagBitsUsed == 0)
            {
                return;
            }

            output.WriteByte(flagByte);
            output.Write(groupBuffer.ToArray());
            groupBuffer.Clear();
            flagByte = 0;
            flagBitsUsed = 0;
        }

        int i = 0;
        while (i < input.Length)
        {
            (int length, int distance) = input.Length - i >= MinMatchLength ? FindMatch(i) : (0, 0);

            if (length >= MinMatchLength)
            {
                flagByte <<= 1;
                int distMinusOne = distance - 1;
                if (length <= MaxInlineLength)
                {
                    groupBuffer.Add((byte)(((length - 2) << 4) | (distMinusOne >> 8)));
                    groupBuffer.Add((byte)(distMinusOne & 0xFF));
                }
                else
                {
                    groupBuffer.Add((byte)(distMinusOne >> 8));
                    groupBuffer.Add((byte)(distMinusOne & 0xFF));
                    groupBuffer.Add((byte)(length - 0x12));
                }

                int matchEnd = i + length;
                for (; i < matchEnd; i++)
                {
                    if (i + 2 < input.Length)
                    {
                        InsertHash(i);
                    }
                }
            }
            else
            {
                flagByte = (byte)((flagByte << 1) | 1);
                groupBuffer.Add(input[i]);
                if (i + 2 < input.Length)
                {
                    InsertHash(i);
                }

                i++;
            }

            flagBitsUsed++;
            if (flagBitsUsed == 8)
            {
                FlushGroup();
            }
        }

        if (flagBitsUsed > 0)
        {
            flagByte <<= 8 - flagBitsUsed;
            FlushGroup();
        }

        return output.ToArray();
    }
}
