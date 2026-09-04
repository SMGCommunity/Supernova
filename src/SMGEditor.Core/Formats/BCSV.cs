using System.Buffers.Binary;
using System.Text;

namespace SMGEditor.Core.Formats;

public enum BCSVValueType : byte
{
    Long = 0,
    String = 1,
    Float = 2,
    Long2 = 3,
    Short = 4,
    Byte = 5,
    StringOffset = 6,
    Null = 7,
}

public sealed class BCSVField
{
    public required uint Hash { get; init; }
    public required string Name { get; init; }
    public required uint Mask { get; init; }
    public required ushort DataOffset { get; init; }
    public required byte Shift { get; init; }
    public required BCSVValueType Type { get; init; }

    public BCSVField() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public BCSVField(string name, uint hash, uint mask, ushort dataOffset, byte shift, BCSVValueType type)
    {
        Name = name;
        Hash = hash;
        Mask = mask;
        DataOffset = dataOffset;
        Shift = shift;
        Type = type;
    }
}

public sealed class BCSVTable
{
    private static readonly Encoding ShiftJis = CreateShiftJisEncoding();

    public required IReadOnlyList<BCSVField> Fields { get; init; }
    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; }

    public required uint EntrySize { get; init; }

    public required uint DataOffset { get; init; }

    private static Encoding CreateShiftJisEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    public static BCSVTable Load(byte[] raw)
    {
        byte[] data = Yaz0.IsCompressed(raw) ? Yaz0.Decompress(raw) : raw;

        uint numEntries = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x0, 4));
        uint numFields = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x4, 4));
        uint dataOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x8, 4));
        uint entrySize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0xC, 4));

        var fields = new BCSVField[numFields];
        for (int i = 0; i < numFields; i++)
        {
            int entryStart = 0x10 + i * 0xC;
            uint hash = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryStart + 0x0, 4));
            uint mask = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryStart + 0x4, 4));
            ushort fieldDataOffset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entryStart + 0x8, 2));
            byte shift = data[entryStart + 0xA];
            byte type = data[entryStart + 0xB];

            fields[i] = new BCSVField
            {
                Hash = hash,
                Name = BCSVHashLookup.Resolve(hash),
                Mask = mask,
                DataOffset = fieldDataOffset,
                Shift = shift,
                Type = (BCSVValueType)type,
            };
        }

        int stringPoolStart = (int)dataOffset + (int)(numEntries * entrySize);

        var rows = new IReadOnlyDictionary<string, object?>[numEntries];
        for (int e = 0; e < numEntries; e++)
        {
            int recordStart = (int)dataOffset + e * (int)entrySize;
            var row = new Dictionary<string, object?>();

            foreach (BCSVField field in fields)
            {
                int valueStart = recordStart + field.DataOffset;
                row[field.Name] = ReadValue(data, valueStart, stringPoolStart, field);
            }

            rows[e] = row;
        }

        return new BCSVTable { Fields = fields, Rows = rows, EntrySize = entrySize, DataOffset = dataOffset };
    }

    private static object? ReadValue(byte[] data, int valueStart, int stringPoolStart, BCSVField field)
    {
        switch (field.Type)
        {
            case BCSVValueType.Long:
            case BCSVValueType.Long2:
            {
                uint raw = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(valueStart, 4));
                return unchecked((int)((raw & field.Mask) >> field.Shift));
            }
            case BCSVValueType.Short:
            {
                ushort raw = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(valueStart, 2));
                return unchecked((int)((raw & field.Mask) >> field.Shift));
            }
            case BCSVValueType.Byte:
            {
                byte raw = data[valueStart];
                return unchecked((int)((raw & field.Mask) >> field.Shift));
            }
            case BCSVValueType.Float:
                return BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(valueStart, 4));
            case BCSVValueType.String:
                return ReadCString(data, valueStart);
            case BCSVValueType.StringOffset:
            {
                uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(valueStart, 4));
                return ReadCString(data, stringPoolStart + (int)offset);
            }
            default:
                return null;
        }
    }

    private static string ReadCString(byte[] data, int offset)
    {
        int end = offset;
        while (data[end] != 0)
        {
            end++;
        }

        return ShiftJis.GetString(data, offset, end - offset);
    }

    public byte[] Save()
    {
        int numFields = Fields.Count;

        List<int> boundaries = [.. Fields.Select(f => (int)f.DataOffset).Distinct().OrderBy(x => x), (int)EntrySize];

        int StringWidth(BCSVField field)
        {
            int idx = boundaries.IndexOf((int)field.DataOffset);
            return boundaries[idx + 1] - (int)field.DataOffset;
        }

        using var output = new MemoryStream();

        Span<byte> header = stackalloc byte[0x10];
        BinaryPrimitives.WriteUInt32BigEndian(header[0..4], (uint)Rows.Count);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..8], (uint)numFields);
        BinaryPrimitives.WriteUInt32BigEndian(header[8..12], DataOffset);
        BinaryPrimitives.WriteUInt32BigEndian(header[12..16], EntrySize);
        output.Write(header);

        Span<byte> entry = stackalloc byte[0xC];
        foreach (BCSVField field in Fields)
        {
            BinaryPrimitives.WriteUInt32BigEndian(entry[0..4], field.Hash);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..8], field.Mask);
            BinaryPrimitives.WriteUInt16BigEndian(entry[8..10], field.DataOffset);
            entry[10] = field.Shift;
            entry[11] = (byte)field.Type;
            output.Write(entry);
        }

        long afterFieldDescriptors = output.Position;
        long rowDataStart = DataOffset;
        for (long i = afterFieldDescriptors; i < rowDataStart; i++)
        {
            output.WriteByte(0);
        }

        var poolOffsetsByString = new Dictionary<string, int>();
        var pool = new MemoryStream();

        int InternString(string s)
        {
            if (poolOffsetsByString.TryGetValue(s, out int existingOffset))
            {
                return existingOffset;
            }

            int offset = (int)pool.Position;
            byte[] encoded = ShiftJis.GetBytes(s);
            pool.Write(encoded);
            pool.WriteByte(0);
            poolOffsetsByString[s] = offset;
            return offset;
        }

        foreach (IReadOnlyDictionary<string, object?> row in Rows)
        {
            byte[] rowBuf = new byte[EntrySize];

            foreach (BCSVField field in Fields)
            {
                row.TryGetValue(field.Name, out object? val);

                switch (field.Type)
                {
                    case BCSVValueType.Long:
                    case BCSVValueType.Long2:
                    {
                        uint existing = BinaryPrimitives.ReadUInt32BigEndian(rowBuf.AsSpan((int)field.DataOffset, 4));
                        uint raw = unchecked((uint)ToInt32(val));
                        uint packed = existing | ((raw << field.Shift) & field.Mask);
                        BinaryPrimitives.WriteUInt32BigEndian(rowBuf.AsSpan((int)field.DataOffset, 4), packed);
                        break;
                    }
                    case BCSVValueType.Short:
                    {
                        ushort existing = BinaryPrimitives.ReadUInt16BigEndian(rowBuf.AsSpan((int)field.DataOffset, 2));
                        uint raw = unchecked((uint)ToInt32(val));
                        ushort packed = (ushort)(existing | ((raw << field.Shift) & field.Mask));
                        BinaryPrimitives.WriteUInt16BigEndian(rowBuf.AsSpan((int)field.DataOffset, 2), packed);
                        break;
                    }
                    case BCSVValueType.Byte:
                    {
                        byte existing = rowBuf[field.DataOffset];
                        uint raw = unchecked((uint)ToInt32(val));
                        byte packed = (byte)(existing | ((raw << field.Shift) & field.Mask));
                        rowBuf[field.DataOffset] = packed;
                        break;
                    }
                    case BCSVValueType.Float:
                    {
                        float f = val switch { float fv => fv, null => 0f, _ => Convert.ToSingle(val) };
                        BinaryPrimitives.WriteSingleBigEndian(rowBuf.AsSpan((int)field.DataOffset, 4), f);
                        break;
                    }
                    case BCSVValueType.String:
                    {
                        string s = val as string ?? string.Empty;
                        int width = StringWidth(field);
                        byte[] encoded = ShiftJis.GetBytes(s);
                        int copyLen = Math.Min(encoded.Length, width - 1);
                        Array.Copy(encoded, 0, rowBuf, field.DataOffset, copyLen);
                        break;
                    }
                    case BCSVValueType.StringOffset:
                    {
                        string s = val as string ?? string.Empty;
                        int poolOffset = InternString(s);
                        BinaryPrimitives.WriteUInt32BigEndian(rowBuf.AsSpan((int)field.DataOffset, 4), (uint)poolOffset);
                        break;
                    }
                }
            }

            output.Write(rowBuf);
        }

        pool.Position = 0;
        pool.CopyTo(output);

        long total = output.Position;
        long alignedTotal = ((total + 31) / 32) * 32;
        for (long i = total; i < alignedTotal; i++)
        {
            output.WriteByte(0x40);
        }

        return output.ToArray();
    }

    private static int ToInt32(object? val) => val switch
    {
        int i => i,
        null => 0,
        _ => Convert.ToInt32(val),
    };
}
