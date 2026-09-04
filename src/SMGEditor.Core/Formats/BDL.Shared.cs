using System.Buffers.Binary;
using System.Text;

namespace SMGEditor.Core.Formats;

internal static class BDLTables
{
    public static int ReadOffset(byte[] data, int blockOffset, int fieldOffset) =>
        blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + fieldOffset, 4));

    public static int? ReadOptionalOffset(byte[] data, int blockOffset, int fieldOffset)
    {
        uint raw = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + fieldOffset, 4));
        return raw == 0 ? null : blockOffset + (int)raw;
    }

    public static IReadOnlyList<string> ReadNameTable(byte[] data, int tableStart)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tableStart, 2));

        var names = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int entry = tableStart + 4 + i * 4;
            ushort stringOffset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 2, 2));
            names.Add(ReadCString(data, tableStart + stringOffset));
        }

        return names;
    }

    public static string ReadTag(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);

    public static string ReadCString(byte[] data, int offset)
    {
        int end = offset;
        while (data[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(data, offset, end - offset);
    }
}
