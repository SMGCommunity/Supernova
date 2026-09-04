using System.Buffers.Binary;
using System.Text;

namespace SMGEditor.Core.Formats;

public abstract record MSBFNode
{
    public sealed record Entry(ushort NextNodeIndex) : MSBFNode;

    public sealed record Message(ushort MessageIndex, ushort NextNodeIndex, ushort Argument1 = 0x88) : MSBFNode;

    public sealed record Branch(ushort Condition, ushort Parameter, ushort BranchTableIndex, ushort Argument1 = 2) : MSBFNode;

    public sealed record Event(ushort EventType, ushort NextNodeIndex, ushort Parameter) : MSBFNode;

    public sealed record Unknown(ushort Type, ushort[] Arguments) : MSBFNode;

    public const ushort NoNext = 0xFFFF;
}

public enum MSBFConditions : ushort
{
    YesNoResult = 0,
    BranchFunc = 1,
    PlayerDistance = 2,
    SW_A = 3,
    SW_B = 4,
    PlayerMode_Normal = 5,
    PlayerMode_Bee = 6,
    PlayerMode_Boo = 7,
    PowerStarAppeared = 8,
    IsLuigi = 9,
    IsInDemo = 10,
    MessageAlreadyReadFlag = 11,
    _120StarEnding = 12,
    Unknown_0x0D = 13,
    PlayerMode_Yoshi = 14,
    PlayerMode_Cloud = 15,
    PlayerMode_Rock = 16,
}

public enum MSBFEvents : ushort
{
    EventFuncAndChain = 0,
    EventFuncAndEnd = 1,
    ChainToNextNode = 2,
    ForwardFlow = 3,
    AnimeFunc = 4,
    ON_SW_A = 5,
    ON_SW_B = 6,
    KillFunc = 7,
    OFF_SW_A = 8,
    OFF_SW_B = 9,
    HideBubblePointer = 10,
    ShowBubblePointer = 11,
}

public sealed class MSBFEntryPoint
{
    public required string Name { get; init; }
    public required uint NodeIndex { get; init; }
}

public sealed class MSBFFile
{
    public required byte Encoding { get; init; }

    public required IReadOnlyList<MSBFNode> Nodes { get; init; }

    public required IReadOnlyList<ushort> BranchTable { get; init; }

    public required IReadOnlyList<MSBFEntryPoint> EntryPoints { get; init; }

    public MSBFEntryPoint? FindEntryPoint(string name) => EntryPoints.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    private const uint GroupCount = 59;

    public byte[] Save()
    {
        using var flw2Body = BuildFlw2();
        using var fen1Body = BuildFen1();

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

        WriteSection("FLW2", flw2Body);
        WriteSection("FEN1", fen1Body);

        byte[] result = output.ToArray();

        System.Text.Encoding.ASCII.GetBytes("MsgFlwBn").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0x8, 2), 0xFEFF);
        result[0xC] = Encoding;
        result[0xD] = 3;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0xE, 2), 2);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0x12, 4), (uint)result.Length);

        return result;
    }

    private MemoryStream BuildFlw2()
    {
        var body = new MemoryStream();
        WriteU16BE(body, (ushort)Nodes.Count);
        WriteU16BE(body, (ushort)BranchTable.Count);
        body.Write(new byte[4]);

        foreach (MSBFNode node in Nodes)
        {
            (ushort type, ushort[] args) = ToRaw(node);
            WriteU16BE(body, type);
            foreach (ushort arg in args)
            {
                WriteU16BE(body, arg);
            }
        }

        foreach (ushort branch in BranchTable)
        {
            WriteU16BE(body, branch);
        }

        return body;
    }

    private static (ushort Type, ushort[] Arguments) ToRaw(MSBFNode node) => node switch
    {
        MSBFNode.Entry n => (4, [0, n.NextNodeIndex, 0, 0, 0]),
        MSBFNode.Message n => (1, [0, n.Argument1, n.MessageIndex, n.NextNodeIndex, 0]),
        MSBFNode.Branch n => (2, [0, n.Argument1, n.Condition, n.Parameter, n.BranchTableIndex]),
        MSBFNode.Event n => (3, [0, n.EventType, n.NextNodeIndex, 0, n.Parameter]),
        MSBFNode.Unknown n => (n.Type, n.Arguments),
        _ => throw new NotSupportedException($"Unhandled MSBFNode kind: {node.GetType().Name}"),
    };

    private MemoryStream BuildFen1()
    {
        var buckets = new List<MSBFEntryPoint>[GroupCount];
        for (int g = 0; g < GroupCount; g++)
        {
            buckets[g] = [];
        }

        foreach (MSBFEntryPoint entry in EntryPoints)
        {
            uint hash = HashName(entry.Name);
            buckets[hash % GroupCount].Add(entry);
        }

        var body = new MemoryStream();
        WriteU32BE(body, GroupCount);

        int cursor = 4 + (int)GroupCount * 8;
        var groupOffsets = new int[GroupCount];
        for (int g = 0; g < GroupCount; g++)
        {
            groupOffsets[g] = cursor;
            foreach (MSBFEntryPoint entry in buckets[g])
            {
                cursor += 1 + System.Text.Encoding.ASCII.GetByteCount(entry.Name) + 4;
            }
        }

        for (int g = 0; g < GroupCount; g++)
        {
            WriteU32BE(body, (uint)buckets[g].Count);
            WriteU32BE(body, (uint)groupOffsets[g]);
        }

        for (int g = 0; g < GroupCount; g++)
        {
            foreach (MSBFEntryPoint entry in buckets[g])
            {
                byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(entry.Name);
                body.WriteByte((byte)nameBytes.Length);
                body.Write(nameBytes);
                WriteU32BE(body, entry.NodeIndex);
            }
        }

        return body;
    }

    private static uint HashName(string name)
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

public static class MSBFReader
{
    public static MSBFFile Load(byte[] data)
    {
        byte encoding = data[0xC];
        ushort sectionCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(0xE, 2));

        var nodes = new List<MSBFNode>();
        var branchTable = new List<ushort>();
        var entryPoints = new List<MSBFEntryPoint>();

        int pos = 0x20;
        for (int s = 0; s < sectionCount && pos + 0x10 <= data.Length; s++)
        {
            string tag = Encoding.ASCII.GetString(data, pos, 4);
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4, 4));
            int body = pos + 0x10;

            switch (tag)
            {
                case "FLW2":
                    ReadFlw2(data, body, nodes, branchTable);
                    break;
                case "FEN1":
                    ReadFen1(data, body, entryPoints);
                    break;
            }

            pos = AlignUp16(pos + 0x10 + size);
        }

        return new MSBFFile { Encoding = encoding, Nodes = nodes, BranchTable = branchTable, EntryPoints = entryPoints };
    }

    private static void ReadFlw2(byte[] data, int body, List<MSBFNode> nodes, List<ushort> branchTable)
    {
        ushort nodeCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body, 2));
        ushort branchCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body + 2, 2));
        int nodesStart = body + 8;

        for (int i = 0; i < nodeCount; i++)
        {
            int n = nodesStart + i * 12;
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(n, 2));
            var args = new ushort[5];
            for (int a = 0; a < 5; a++)
            {
                args[a] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(n + 2 + a * 2, 2));
            }

            MSBFNode node = type switch
            {
                4 => new MSBFNode.Entry(args[1]),
                1 => new MSBFNode.Message(args[2], args[3], args[1]),
                2 => new MSBFNode.Branch(args[2], args[3], args[4], args[1]),
                3 => new MSBFNode.Event(args[1], args[2], args[4]),
                _ => new MSBFNode.Unknown(type, args),
            };
            nodes.Add(node);
        }

        int branchTableStart = nodesStart + nodeCount * 12;
        for (int i = 0; i < branchCount; i++)
        {
            branchTable.Add(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(branchTableStart + i * 2, 2)));
        }
    }

    private static void ReadFen1(byte[] data, int body, List<MSBFEntryPoint> entryPoints)
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
                uint nodeIndex = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryPos + 1 + nameLength, 4));
                entryPoints.Add(new MSBFEntryPoint { Name = name, NodeIndex = nodeIndex });
                entryPos += 1 + nameLength + 4;
            }
        }
    }

    private static int AlignUp16(int value) => (value + 0xF) & ~0xF;
}
