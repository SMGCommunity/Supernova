using System.Buffers.Binary;
using System.Text;

namespace SMGEditor.Core.Formats;

public enum BMGEncoding : byte
{
    Cp1252 = 0,
    Cp1252Legacy = 1,
    Utf16 = 2,
    ShiftJis = 3,
    Utf8 = 4,
}

public abstract record BMGTextRun
{
    public sealed record Literal(string Value) : BMGTextRun;

    public sealed record Tag(byte Group, ushort Number, byte[] Payload) : BMGTextRun;
}

public sealed class BMGMessage
{
    public required IReadOnlyList<BMGTextRun> Parts { get; init; }
    public required IReadOnlyList<byte> Attributes { get; init; }
}

public abstract record BMGFlowNode
{
    public sealed record Continuation(byte DoorQuery, ushort MessageIndex, ushort NextNodeIndex, ushort Unknown = 0) : BMGFlowNode;

    public sealed record Branch(byte DoorQuery, ushort QueryFunctionId, ushort QueryParameter, ushort IndirectionTableOffset) : BMGFlowNode;

    public sealed record Event(byte EventFunctionId, ushort IndirectionTableIndex, byte[] Arguments) : BMGFlowNode;
}

public readonly record struct BMGFlowId(ushort Id, ushort NodeIndex);

public sealed class BMGFile
{
    public required BMGEncoding Encoding { get; init; }
    public required IReadOnlyList<BMGMessage> Messages { get; init; }
    public required IReadOnlyList<BMGFlowNode> FlowNodes { get; init; }
    public required IReadOnlyList<ushort> FlowIndirectionTable { get; init; }
    public required IReadOnlyList<BMGFlowId> FlowIds { get; init; }

    public byte[] Inf1HeaderExtra { get; init; } = new byte[4];

    public ushort Fli1HeaderUnknown { get; init; }

    public byte[] Save()
    {
        // I don't think this is possible, but shrug
        if (Encoding != BMGEncoding.Utf16)
        {
            throw new NotSupportedException("Only BMGEncoding.Utf16 is implemented for writing.");
        }

        int entrySize = Messages.Count > 0 ? 4 + Messages[0].Attributes.Count : 12;
        foreach (BMGMessage message in Messages)
        {
            if (4 + message.Attributes.Count != entrySize)
            {
                throw new InvalidDataException("Message attributes count does not match the proper entry size.");
            }
        }

        using var pool = new MemoryStream();
        var poolOffsetsByContent = new Dictionary<string, int>();

        int InternMessage(IReadOnlyList<BMGTextRun> parts)
        {
            byte[] bytes = SerializeMessageText(parts);
            string key = Convert.ToBase64String(bytes);
            if (poolOffsetsByContent.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int offset = (int)pool.Position;
            pool.Write(bytes);
            poolOffsetsByContent[key] = offset;
            return offset;
        }

        InternMessage([]);

        var stringOffsets = new int[Messages.Count];
        for (int i = 0; i < Messages.Count; i++)
        {
            stringOffsets[i] = InternMessage(Messages[i].Parts);
        }

        using var inf1Body = new MemoryStream();
        WriteU16BE(inf1Body, (ushort)Messages.Count);
        WriteU16BE(inf1Body, (ushort)entrySize);
        inf1Body.Write(Inf1HeaderExtra);
        for (int i = 0; i < Messages.Count; i++)
        {
            WriteU32BE(inf1Body, (uint)stringOffsets[i]);
            foreach (byte b in Messages[i].Attributes)
            {
                inf1Body.WriteByte(b);
            }
        }

        using var flw1Body = new MemoryStream();
        WriteU16BE(flw1Body, (ushort)FlowNodes.Count);
        WriteU16BE(flw1Body, (ushort)FlowIndirectionTable.Count);
        flw1Body.Write(new byte[4]);
        foreach (BMGFlowNode node in FlowNodes)
        {
            switch (node)
            {
                case BMGFlowNode.Continuation c:
                    flw1Body.WriteByte(1);
                    flw1Body.WriteByte(c.DoorQuery);
                    WriteU16BE(flw1Body, c.MessageIndex);
                    WriteU16BE(flw1Body, c.NextNodeIndex);
                    WriteU16BE(flw1Body, c.Unknown);
                    break;
                case BMGFlowNode.Branch b:
                    flw1Body.WriteByte(2);
                    flw1Body.WriteByte(b.DoorQuery);
                    WriteU16BE(flw1Body, b.QueryFunctionId);
                    WriteU16BE(flw1Body, b.QueryParameter);
                    WriteU16BE(flw1Body, b.IndirectionTableOffset);
                    break;
                case BMGFlowNode.Event e:
                    flw1Body.WriteByte(3);
                    flw1Body.WriteByte(e.EventFunctionId);
                    WriteU16BE(flw1Body, e.IndirectionTableIndex);
                    flw1Body.Write(e.Arguments, 0, 4);
                    break;
            }
        }

        foreach (ushort indirection in FlowIndirectionTable)
        {
            WriteU16BE(flw1Body, indirection);
        }

        using var fli1Body = new MemoryStream();
        WriteU16BE(fli1Body, (ushort)FlowIds.Count);
        WriteU16BE(fli1Body, Fli1HeaderUnknown);
        fli1Body.Write(new byte[4]);
        foreach (BMGFlowId flowId in FlowIds)
        {
            WriteU16BE(fli1Body, flowId.Id);
            WriteU16BE(fli1Body, 0);
            WriteU16BE(fli1Body, flowId.NodeIndex);
            WriteU16BE(fli1Body, 0);
        }

        using var output = new MemoryStream();

        void WriteSection(string tag, MemoryStream body)
        {
            int sectionStart = (int)output.Position;
            output.Write(System.Text.Encoding.ASCII.GetBytes(tag));
            WriteU32BE(output, 0);
            body.Position = 0;
            body.CopyTo(output);

            int unpaddedEnd = (int)output.Position;
            int paddedEnd = AlignUp32(unpaddedEnd);
            for (int i = unpaddedEnd; i < paddedEnd; i++)
            {
                output.WriteByte(0);
            }

            int size = paddedEnd - sectionStart;
            long afterSection = output.Position;
            output.Position = sectionStart + 4;
            WriteU32BE(output, (uint)size);
            output.Position = afterSection;
        }

        output.Write(new byte[0x20]);
        WriteSection("INF1", inf1Body);
        WriteSection("DAT1", pool);
        int flowStart = (int)output.Position;
        WriteSection("FLW1", flw1Body);
        WriteSection("FLI1", fli1Body);

        byte[] result = output.ToArray();

        System.Text.Encoding.ASCII.GetBytes("MESGbmg1").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0x8, 4), (uint)flowStart);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0xC, 4), 4);
        result[0x10] = (byte)BMGEncoding.Utf16;

        return result;
    }

    private static byte[] SerializeMessageText(IReadOnlyList<BMGTextRun> parts)
    {
        using var ms = new MemoryStream();
        foreach (BMGTextRun part in parts)
        {
            switch (part)
            {
                case BMGTextRun.Literal literal:
                    foreach (char c in literal.Value)
                    {
                        WriteU16BE(ms, c);
                    }

                    break;
                case BMGTextRun.Tag tag:
                    WriteU16BE(ms, 0x001A);
                    ms.WriteByte((byte)(6 + tag.Payload.Length));
                    ms.WriteByte(tag.Group);
                    WriteU16BE(ms, tag.Number);
                    ms.Write(tag.Payload);
                    break;
            }
        }

        WriteU16BE(ms, 0);
        return ms.ToArray();
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

    private static int AlignUp32(int value) => (value + 31) / 32 * 32;
}

public static class BMGReader
{
    public static BMGFile Load(byte[] data)
    {
        uint sectionCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0xC, 4));
        var encoding = (BMGEncoding)data[0x10];

        var messages = new List<BMGMessage>();
        var flowNodes = new List<BMGFlowNode>();
        var flowIndirection = new List<ushort>();
        var flowIds = new List<BMGFlowId>();
        byte[] inf1HeaderExtra = new byte[4];
        ushort fli1HeaderUnknown = 0;

        int pos = 0x20;
        for (int s = 0; s < sectionCount && pos + 8 <= data.Length; s++)
        {
            string tag = ReadTag(data, pos);
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4, 4));

            switch (tag)
            {
                case "INF1":
                    ReadInf1(data, pos, encoding, messages, out inf1HeaderExtra);
                    break;
                case "FLW1":
                    ReadFlw1(data, pos, flowNodes, flowIndirection);
                    break;
                case "FLI1":
                    ReadFli1(data, pos, flowIds, out fli1HeaderUnknown);
                    break;
            }

            pos += size;
        }

        return new BMGFile
        {
            Encoding = encoding,
            Messages = messages,
            FlowNodes = flowNodes,
            FlowIndirectionTable = flowIndirection,
            FlowIds = flowIds,
            Inf1HeaderExtra = inf1HeaderExtra,
            Fli1HeaderUnknown = fli1HeaderUnknown,
        };
    }

    private static void ReadInf1(byte[] data, int inf1Offset, BMGEncoding encoding, List<BMGMessage> messages, out byte[] headerExtra)
    {
        int body = inf1Offset + 8;
        ushort entryCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body, 2));
        ushort entrySize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body + 2, 2));
        headerExtra = data.AsSpan(body + 4, 4).ToArray();
        int entriesStart = body + 8;

        int dat1Body = FindSectionBody(data, inf1Offset, "DAT1");

        for (int i = 0; i < entryCount; i++)
        {
            int entry = entriesStart + i * entrySize;
            uint stringOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry, 4));
            var attributes = new byte[entrySize - 4];
            Array.Copy(data, entry + 4, attributes, 0, attributes.Length);

            List<BMGTextRun> parts = dat1Body >= 0
                ? ReadMessageText(data, dat1Body + (int)stringOffset, encoding)
                : [];
            messages.Add(new BMGMessage { Parts = parts, Attributes = attributes });
        }
    }

    private static List<BMGTextRun> ReadMessageText(byte[] data, int start, BMGEncoding encoding)
    {
        var parts = new List<BMGTextRun>();
        var literal = new StringBuilder();
        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                parts.Add(new BMGTextRun.Literal(literal.ToString()));
                literal.Clear();
            }
        }

        int pos = start;
        if (encoding == BMGEncoding.Utf16)
        {
            while (true)
            {
                ushort unit = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2));
                if (unit == 0)
                {
                    break;
                }

                if (unit == 0x001A)
                {
                    FlushLiteral();
                    byte size = data[pos + 2];
                    byte group = data[pos + 3];
                    ushort number = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 4, 2));
                    var payload = new byte[size - 6];
                    Array.Copy(data, pos + 6, payload, 0, payload.Length);
                    parts.Add(new BMGTextRun.Tag(group, number, payload));
                    pos += size;
                    continue;
                }

                literal.Append((char)unit);
                pos += 2;
            }
        }
        else
        {
            Encoding charset = encoding switch
            {
                BMGEncoding.ShiftJis => Encoding.GetEncoding(932),
                BMGEncoding.Utf8 => Encoding.UTF8,
                _ => Encoding.GetEncoding(1252),
            };

            while (data[pos] != 0)
            {
                if (data[pos] == 0x1A)
                {
                    throw new NotSupportedException("Embedded BMG tag in a non-UTF16-encoded message.");
                }

                pos++;
            }

            literal.Append(charset.GetString(data, start, pos - start));
        }

        FlushLiteral();
        return parts;
    }

    private static void ReadFlw1(byte[] data, int flw1Offset, List<BMGFlowNode> nodes, List<ushort> indirection)
    {
        int body = flw1Offset + 8;
        ushort nodeCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body, 2));
        ushort indirectionCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body + 2, 2));

        int nodesStart = body + 8;
        for (int i = 0; i < nodeCount; i++)
        {
            int n = nodesStart + i * 8;
            byte type = data[n];
            byte b1 = data[n + 1];
            ushort u1 = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(n + 2, 2));
            ushort u2 = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(n + 4, 2));

            BMGFlowNode node = type switch
            {
                1 => new BMGFlowNode.Continuation(b1, u1, u2, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(n + 6, 2))),
                2 => new BMGFlowNode.Branch(b1, u1, u2, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(n + 6, 2))),
                3 => new BMGFlowNode.Event(b1, u1, data.AsSpan(n + 4, 4).ToArray()),
                _ => new BMGFlowNode.Event(type, 0, data.AsSpan(n + 1, 7).ToArray()),
            };
            nodes.Add(node);
        }

        int indirectionStart = nodesStart + nodeCount * 8;
        for (int i = 0; i < indirectionCount; i++)
        {
            indirection.Add(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(indirectionStart + i * 2, 2)));
        }
    }

    private static void ReadFli1(byte[] data, int fli1Offset, List<BMGFlowId> flowIds, out ushort headerUnknown)
    {
        int body = fli1Offset + 8;
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body, 2));
        headerUnknown = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body + 2, 2));
        int entriesStart = body + 8;
        for (int i = 0; i < count; i++)
        {
            int e = entriesStart + i * 8;
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(e, 2));
            ushort nodeIndex = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(e + 4, 2));
            flowIds.Add(new BMGFlowId(id, nodeIndex));
        }
    }

    private static int FindSectionBody(byte[] data, int fromOffset, string wantedTag)
    {
        int pos = 0x20;
        while (pos + 8 <= data.Length)
        {
            string tag = ReadTag(data, pos);
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4, 4));
            if (tag == wantedTag)
            {
                return pos + 8;
            }

            if (size <= 0)
            {
                break;
            }

            pos += size;
        }

        return -1;
    }

    private static string ReadTag(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);
}
