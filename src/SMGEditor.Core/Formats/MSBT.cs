using System.Buffers.Binary;
using System.Text;

namespace SMGEditor.Core.Formats;

public enum MSBTEncoding : byte
{
    Utf8 = 0,
    Utf16 = 1,
}

public abstract record MSBTTextRun
{
    public sealed record Literal(string Value) : MSBTTextRun;

    public sealed record Tag(ushort Group, ushort TagId, byte[] Payload) : MSBTTextRun;
}

public sealed class MSBTMessage
{
    public required IReadOnlyList<MSBTTextRun> Parts { get; init; }

    public required IReadOnlyList<byte> Attributes { get; init; }
}

public readonly record struct MSBTLabel(string Name, uint MessageIndex);

public sealed class MSBTFile
{
    public required MSBTEncoding Encoding { get; init; }
    public required IReadOnlyList<MSBTLabel> Labels { get; init; }
    public required IReadOnlyList<MSBTMessage> Messages { get; init; }

    public MSBTMessage? FindByLabel(string name)
    {
        foreach (MSBTLabel label in Labels)
        {
            if (label.Name == name && label.MessageIndex < Messages.Count)
            {
                return Messages[(int)label.MessageIndex];
            }
        }

        return null;
    }

    public MSBTFile WithUpsertedLabel(string label, string newText) => WithUpsertedLabel(label, (IReadOnlyList<MSBTTextRun>)[new MSBTTextRun.Literal(newText)]);

    public MSBTFile WithUpsertedLabel(string label, IReadOnlyList<MSBTTextRun> newParts)
    {
        var messages = Messages.ToList();
        var labels = Labels.ToList();

        int labelIndex = labels.FindIndex(l => l.Name == label);
        if (labelIndex >= 0)
        {
            int messageIndex = (int)labels[labelIndex].MessageIndex;
            messages[messageIndex] = new MSBTMessage { Parts = newParts, Attributes = messages[messageIndex].Attributes };
        }
        else
        {
            IReadOnlyList<byte> attributes = messages.Count > 0 ? messages[0].Attributes : [];
            messages.Add(new MSBTMessage { Parts = newParts, Attributes = attributes });
            labels.Add(new MSBTLabel(label, (uint)(messages.Count - 1)));
        }

        return new MSBTFile { Encoding = Encoding, Labels = labels, Messages = messages };
    }

    private const uint GroupCount = 101;

    public byte[] Save()
    {
        if (Encoding != MSBTEncoding.Utf16)
        {
            throw new NotSupportedException("Only MSBTEncoding.Utf16 is implemented for writing - no other encoding was ever observed in real SMG2 data.");
        }

        int atr1EntrySize = Messages.Count > 0 ? Messages[0].Attributes.Count : 12;
        foreach (MSBTMessage message in Messages)
        {
            if (message.Attributes.Count != atr1EntrySize)
            {
                throw new InvalidDataException("Every MSBTMessage in a MSBTFile must have the same Attributes length - ATR1 uses one uniform entry size for all messages.");
            }
        }

        using var lbl1Body = BuildLbl1();
        using var atr1Body = BuildAtr1(atr1EntrySize);
        using var txt2Body = BuildTxt2();

        using var output = new MemoryStream();
        output.Write(new byte[0x20]);

        void WriteSection(string tag, MemoryStream body)
        {
            output.Write(System.Text.Encoding.ASCII.GetBytes(tag));
            WriteU32BE(output, (uint)body.Length);
            output.Write(new byte[8]);

            body.Position = 0;
            body.CopyTo(output);

            int unpaddedEnd = (int)output.Position;
            int paddedEnd = AlignUp16(unpaddedEnd);
            for (int i = unpaddedEnd; i < paddedEnd; i++)
            {
                output.WriteByte(0xAB);
            }
        }

        WriteSection("LBL1", lbl1Body);
        WriteSection("ATR1", atr1Body);
        WriteSection("TXT2", txt2Body);

        byte[] result = output.ToArray();

        System.Text.Encoding.ASCII.GetBytes("MsgStdBn").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0x8, 2), 0xFEFF);
        result[0xC] = (byte)MSBTEncoding.Utf16;
        result[0xD] = 3;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0xE, 2), 3);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0x12, 4), (uint)result.Length);

        return result;
    }

    private MemoryStream BuildLbl1()
    {
        var buckets = new List<(string Name, uint MessageIndex)>[GroupCount];
        for (int g = 0; g < GroupCount; g++)
        {
            buckets[g] = [];
        }

        foreach (MSBTLabel label in Labels)
        {
            uint hash = HashLabel(label.Name);
            buckets[hash % GroupCount].Add((label.Name, label.MessageIndex));
        }

        var body = new MemoryStream();
        WriteU32BE(body, GroupCount);

        int cursor = 4 + (int)GroupCount * 8;
        var groupOffsets = new int[GroupCount];
        for (int g = 0; g < GroupCount; g++)
        {
            groupOffsets[g] = cursor;
            foreach ((string name, _) in buckets[g])
            {
                cursor += 1 + System.Text.Encoding.ASCII.GetByteCount(name) + 4;
            }
        }

        for (int g = 0; g < GroupCount; g++)
        {
            WriteU32BE(body, (uint)buckets[g].Count);
            WriteU32BE(body, (uint)groupOffsets[g]);
        }

        for (int g = 0; g < GroupCount; g++)
        {
            foreach ((string name, uint messageIndex) in buckets[g])
            {
                byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
                body.WriteByte((byte)nameBytes.Length);
                body.Write(nameBytes);
                WriteU32BE(body, messageIndex);
            }
        }

        return body;
    }

    private MemoryStream BuildAtr1(int entrySize)
    {
        var body = new MemoryStream();
        WriteU32BE(body, (uint)Messages.Count);
        WriteU32BE(body, (uint)entrySize);
        foreach (MSBTMessage message in Messages)
        {
            foreach (byte b in message.Attributes)
            {
                body.WriteByte(b);
            }
        }

        return body;
    }

    private MemoryStream BuildTxt2()
    {
        var msgBytes = new byte[Messages.Count][];
        for (int i = 0; i < Messages.Count; i++)
        {
            msgBytes[i] = SerializeMessageText(Messages[i].Parts);
        }

        var body = new MemoryStream();
        WriteU32BE(body, (uint)Messages.Count);

        int cursor = 4 + Messages.Count * 4;
        foreach (byte[] bytes in msgBytes)
        {
            WriteU32BE(body, (uint)cursor);
            cursor += bytes.Length;
        }

        foreach (byte[] bytes in msgBytes)
        {
            body.Write(bytes);
        }

        return body;
    }

    private static byte[] SerializeMessageText(IReadOnlyList<MSBTTextRun> parts)
    {
        using var ms = new MemoryStream();
        foreach (MSBTTextRun part in parts)
        {
            switch (part)
            {
                case MSBTTextRun.Literal literal:
                    foreach (char c in literal.Value)
                    {
                        WriteU16BE(ms, c);
                    }

                    break;
                case MSBTTextRun.Tag tag:
                    WriteU16BE(ms, 0x000E);
                    WriteU16BE(ms, tag.Group);
                    WriteU16BE(ms, tag.TagId);
                    WriteU16BE(ms, (ushort)tag.Payload.Length);
                    ms.Write(tag.Payload);
                    break;
            }
        }

        WriteU16BE(ms, 0);
        return ms.ToArray();
    }

    private static uint HashLabel(string name)
    {
        uint hash = 0;
        foreach (char c in name)
        {
            hash = hash * 0x492 + (byte)c;
        }

        return hash;
    }

    private static void WriteU16BE(Stream s, ushort v)
    {
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void WriteU32BE(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static int AlignUp16(int value) => (value + 0xF) & ~0xF;
}

public static class MSBTReader
{
    public static MSBTFile Load(byte[] data)
    {
        var encoding = (MSBTEncoding)data[0xC];
        ushort sectionCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(0xE, 2));

        var labels = new List<MSBTLabel>();
        var messages = new List<MSBTMessage>();
        List<byte[]> attributeEntries = [];

        int pos = 0x20;
        for (int s = 0; s < sectionCount && pos + 0x10 <= data.Length; s++)
        {
            string tag = Encoding.ASCII.GetString(data, pos, 4);
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4, 4));
            int body = pos + 0x10;

            switch (tag)
            {
                case "LBL1":
                    ReadLbl1(data, body, labels);
                    break;
                case "ATR1":
                    attributeEntries = ReadAtr1(data, body);
                    break;
                case "TXT2":
                    ReadTxt2(data, body, encoding, messages);
                    break;
            }

            pos = AlignUp16(pos + 0x10 + size);
        }

        for (int i = 0; i < messages.Count; i++)
        {
            IReadOnlyList<byte> attrs = i < attributeEntries.Count ? attributeEntries[i] : [];
            messages[i] = new MSBTMessage { Parts = messages[i].Parts, Attributes = attrs };
        }

        return new MSBTFile { Encoding = encoding, Labels = labels, Messages = messages };
    }

    private static void ReadLbl1(byte[] data, int body, List<MSBTLabel> labels)
    {
        uint groupCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body, 4));
        int groupsStart = body + 4;

        for (int g = 0; g < groupCount; g++)
        {
            int groupEntry = groupsStart + g * 8;
            uint count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(groupEntry, 4));
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(groupEntry + 4, 4));

            int entryPos = body + (int)offset;
            for (int i = 0; i < count; i++)
            {
                byte nameLength = data[entryPos];
                string name = Encoding.ASCII.GetString(data, entryPos + 1, nameLength);
                uint messageIndex = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryPos + 1 + nameLength, 4));
                labels.Add(new MSBTLabel(name, messageIndex));
                entryPos += 1 + nameLength + 4;
            }
        }
    }

    private static List<byte[]> ReadAtr1(byte[] data, int body)
    {
        uint entryCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body, 4));
        uint entrySize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body + 4, 4));
        int entriesStart = body + 8;

        var result = new List<byte[]>((int)entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            var entry = new byte[entrySize];
            Array.Copy(data, entriesStart + i * (int)entrySize, entry, 0, entrySize);
            result.Add(entry);
        }

        return result;
    }

    private static void ReadTxt2(byte[] data, int body, MSBTEncoding encoding, List<MSBTMessage> messages)
    {
        uint messageCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body, 4));
        for (int i = 0; i < messageCount; i++)
        {
            uint stringOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body + 4 + i * 4, 4));
            List<MSBTTextRun> parts = ReadMessageText(data, body + (int)stringOffset, encoding);
            messages.Add(new MSBTMessage { Parts = parts, Attributes = [] });
        }
    }

    private static List<MSBTTextRun> ReadMessageText(byte[] data, int start, MSBTEncoding encoding)
    {
        var parts = new List<MSBTTextRun>();
        var literal = new StringBuilder();
        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                parts.Add(new MSBTTextRun.Literal(literal.ToString()));
                literal.Clear();
            }
        }

        int pos = start;
        if (encoding == MSBTEncoding.Utf16)
        {
            while (true)
            {
                ushort unit = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2));
                if (unit == 0)
                {
                    break;
                }

                if (unit == 0x000E)
                {
                    FlushLiteral();
                    ushort group = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 2, 2));
                    ushort tagId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 4, 2));
                    ushort paramSize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 6, 2));
                    var payload = new byte[paramSize];
                    Array.Copy(data, pos + 8, payload, 0, paramSize);
                    parts.Add(new MSBTTextRun.Tag(group, tagId, payload));
                    pos += 8 + paramSize;
                    continue;
                }

                literal.Append((char)unit);
                pos += 2;
            }
        }
        else
        {
            while (data[pos] != 0)
            {
                if (data[pos] == 0x0E)
                {
                    throw new NotSupportedException("Embedded MSBT tag in a UTF-8-encoded message - this encoding was never observed in SMG2 data and its tag layout isn't implemented.");
                }

                pos++;
            }

            literal.Append(Encoding.UTF8.GetString(data, start, pos - start));
        }

        FlushLiteral();
        return parts;
    }

    private static int AlignUp16(int value) => (value + 0xF) & ~0xF;
}
