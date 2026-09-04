using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SMGEditor.Core.Formats;

public enum BDLHierarchyNodeType : ushort
{
    Exit = 0,
    Begin = 1,
    End = 2,
    Joint = 16,
    Material = 17,
    Shape = 18,
}

public readonly record struct BDLHierarchyNode(BDLHierarchyNodeType Type, ushort Index);

public sealed class BDLChunk
{
    public required string Tag { get; init; }
    public required int Offset { get; init; }
    public required int Size { get; init; }
}

public sealed class BDLJoint
{
    public required string Name { get; init; }
    public required ushort MatrixType { get; init; }
    public required byte AttachFlag { get; init; }
    public required Vector3 Scale { get; init; }

    public required Vector3 RotationDegrees { get; init; }

    public required Vector3 Translation { get; init; }
    public required float BoundingSphereRadius { get; init; }
    public required Vector3 BoundingBoxMin { get; init; }
    public required Vector3 BoundingBoxMax { get; init; }
}

public sealed class BDLEnvelope
{
    public required IReadOnlyList<ushort> JointIndices { get; init; }
    public required IReadOnlyList<float> Weights { get; init; }
}

public readonly record struct BDLDrawMatrix(bool IsWeighted, ushort Index);

public readonly struct BDLMatrix3x4
{
    private readonly float[] _m;

    public BDLMatrix3x4(float[] rowMajor3X4)
    {
        if (rowMajor3X4.Length != 12)
        {
            throw new ArgumentException("Expected 12 elements (3 rows x 4 columns).", nameof(rowMajor3X4));
        }

        _m = rowMajor3X4;
    }

    public float this[int row, int column] => _m[row * 4 + column];
}

internal enum GXAttr : uint
{
    Position = 9, // GX_VA_POS
    Normal = 10, // GX_VA_NRM
    Color0 = 11, // GX_VA_CLR0
    Color1 = 12, // GX_VA_CLR1
    TexCoord0 = 13, // GX_VA_TEX0
    Null = 0xFF, // GX_VA_NULL
}

internal enum GXCompType : uint
{
    Unsigned8 = 0, // GX_U8
    Signed8 = 1, // GX_S8
    Unsigned16 = 2, // GX_U16
    Signed16 = 3, // GX_S16
    Float32 = 4, // GX_F32
}

internal enum BDLColorFormat : uint
{
    Rgb565 = 0, // GX_RGB565
    Rgb8 = 1, // GX_RGB8
    Rgbx8 = 2, // GX_RGBX8
    Rgba4 = 3, // GX_RGBA4
    Rgba6 = 4, // GX_RGBA6
    Rgba8 = 5, // GX_RGBA8
}

internal readonly record struct BDLVertexAttributeFormat(uint Attribute, uint ComponentCount, uint ComponentType, byte FractionalBits);

public sealed class BDLVertexArray
{
    public required byte[] Data { get; init; }
    public required int Offset { get; init; }
    public required int Stride { get; init; }
    public required int ComponentCount { get; init; }
    public required uint ComponentType { get; init; }
    public required byte FractionalBits { get; init; }
    public required bool IsColor { get; init; }

    public Vector2 GetVector2(int index) => new(GetComponents(index)[0], GetComponents(index)[1]);

    public Vector3 GetVector3(int index)
    {
        float[] c = GetComponents(index);
        return new Vector3(c[0], c[1], c[2]);
    }

    public Vector4 GetColor(int index)
    {
        float[] c = GetComponents(index);
        return c.Length == 4 ? new Vector4(c[0], c[1], c[2], c[3]) : new Vector4(c[0], c[1], c[2], 1f);
    }

    private float[] GetComponents(int index)
    {
        int elementStart = Offset + index * Stride;

        if (IsColor)
        {
            return ((BDLColorFormat)ComponentType) switch
            {
                // RGB8 has R, G, B, no alpha.
                BDLColorFormat.Rgb8 => new[] { Data[elementStart] / 255f, Data[elementStart + 1] / 255f, Data[elementStart + 2] / 255f },
                // RGBX8 has R, G, B, and a dummy alpha. It is skipped
                BDLColorFormat.Rgbx8 => new[] { Data[elementStart] / 255f, Data[elementStart + 1] / 255f, Data[elementStart + 2] / 255f },
                // RGBA8 has all 4 components
                BDLColorFormat.Rgba8 => new[]
                {
                    Data[elementStart] / 255f, Data[elementStart + 1] / 255f, Data[elementStart + 2] / 255f, Data[elementStart + 3] / 255f,
                },
                _ => throw new NotSupportedException(
                    $"Packed color format {(BDLColorFormat)ComponentType} isn't implemented yet"),
            };
        }

        float scale = MathF.Pow(0.5f, FractionalBits);
        var result = new float[ComponentCount];
        int componentSize = (GXCompType)ComponentType switch
        {
            GXCompType.Unsigned8 or GXCompType.Signed8 => 1,
            GXCompType.Unsigned16 or GXCompType.Signed16 => 2,
            GXCompType.Float32 => 4,
            _ => throw new NotSupportedException($"Unknown GX component type {ComponentType}."),
        };

        for (int i = 0; i < ComponentCount; i++)
        {
            int compOffset = elementStart + i * componentSize;
            float raw = (GXCompType)ComponentType switch
            {
                GXCompType.Unsigned8 => Data[compOffset],
                GXCompType.Signed8 => (sbyte)Data[compOffset],
                GXCompType.Unsigned16 => BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(compOffset, 2)),
                GXCompType.Signed16 => BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(compOffset, 2)),
                GXCompType.Float32 => BinaryPrimitives.ReadSingleBigEndian(Data.AsSpan(compOffset, 4)),
                _ => throw new NotSupportedException($"Unknown GX component type {ComponentType}."),
            };
            result[i] = raw * scale;
        }

        return result;
    }
}

public sealed class BDLVertexData
{
    public BDLVertexArray? Positions { get; init; }
    public BDLVertexArray? Normals { get; init; }
    public BDLVertexArray? Color0 { get; init; }
    public BDLVertexArray? Color1 { get; init; }
    public required IReadOnlyList<BDLVertexArray?> TexCoords { get; init; }
}

public enum BDLPrimitiveType : byte
{
    Quads = 0x80, // GX_QUADS
    Triangles = 0x90, // GX_TRIANGLES
    TriangleStrip = 0x98, // GX_TRIANGLESTRIP
    TriangleFan = 0xA0, // GX_TRIANGLEFAN
    Lines = 0xA8, // GX_LINES
    LineStrip = 0xB0, // GX_LINESTRIP
    Points = 0xB8, // GX_POINTS
}

public sealed class BDLShapeVertex
{
    public required int PositionIndex { get; init; }
    public int? NormalIndex { get; init; }
    public int? Color0Index { get; init; }
    public int? Color1Index { get; init; }
    public required IReadOnlyList<int?> TexCoordIndices { get; init; }

    public ushort? DrawMatrixIndexOverride { get; init; }
}

public sealed class BDLPrimitive
{
    public required BDLPrimitiveType Type { get; init; }
    public required IReadOnlyList<BDLShapeVertex> Vertices { get; init; }
}

public sealed class BDLPacket
{
    public required ushort DrawMatrixIndex { get; init; }
    public required IReadOnlyList<BDLPrimitive> Primitives { get; init; }
}

public sealed class BDLShape
{
    public required byte MatrixType { get; init; }
    public required float BoundingSphereRadius { get; init; }
    public required Vector3 BoundingBoxMin { get; init; }
    public required Vector3 BoundingBoxMax { get; init; }
    public required IReadOnlyList<BDLPacket> Packets { get; init; }
}

public sealed class BDLModel
{
    public required string FormatTag { get; init; }
    public required IReadOnlyList<BDLChunk> Chunks { get; init; }
    public required IReadOnlyList<BDLHierarchyNode> HierarchyNodes { get; init; }
    public required IReadOnlyList<BDLJoint> Joints { get; init; }
    public required IReadOnlyList<BDLEnvelope> Envelopes { get; init; }
    public required IReadOnlyList<BDLMatrix3x4> InverseBindMatrices { get; init; }
    public required IReadOnlyList<BDLDrawMatrix> DrawMatrices { get; init; }
    public BDLVertexData? VertexData { get; init; }
    public required IReadOnlyList<BDLShape> Shapes { get; init; }
    public required IReadOnlyList<BDLMaterial> Materials { get; init; }
    public required IReadOnlyList<BDLTexture> Textures { get; init; }

    public BDLModel WithMaterials(IReadOnlyList<BDLMaterial> materials) => new()
    {
        FormatTag = FormatTag,
        Chunks = Chunks,
        HierarchyNodes = HierarchyNodes,
        Joints = Joints,
        Envelopes = Envelopes,
        InverseBindMatrices = InverseBindMatrices,
        DrawMatrices = DrawMatrices,
        VertexData = VertexData,
        Shapes = Shapes,
        Materials = materials,
        Textures = Textures,
    };

    public static BDLModel Load(byte[] raw)
    {
        byte[] data = Yaz0.IsCompressed(raw) ? Yaz0.Decompress(raw) : raw;

        string version = BDLTables.ReadTag(data, 0x0);
        if (version != "J3D2")
        {
            throw new InvalidDataException($"Not a J3D model (expected 'J3D2', got '{version}').");
        }

        string formatTag = BDLTables.ReadTag(data, 0x4);
        uint blockCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0xC, 4));

        var chunks = new List<BDLChunk>();
        var hierarchyNodes = new List<BDLHierarchyNode>();
        var joints = new List<BDLJoint>();
        var envelopes = new List<BDLEnvelope>();
        var inverseBindMatrices = new List<BDLMatrix3x4>();
        var drawMatrices = new List<BDLDrawMatrix>();
        BDLVertexData? vertexData = null;
        var shapes = new List<BDLShape>();
        var materials = new List<BDLMaterial>();
        var textures = new List<BDLTexture>();

        int blockOffset = 0x20;
        for (int i = 0; i < blockCount; i++)
        {
            string tag = BDLTables.ReadTag(data, blockOffset);
            int blockSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 4, 4));
            chunks.Add(new BDLChunk { Tag = tag, Offset = blockOffset, Size = blockSize });

            switch (tag)
            {
                case "INF1":
                    hierarchyNodes.AddRange(ReadHierarchy(data, blockOffset));
                    break;
                case "JNT1":
                    joints.AddRange(ReadJoints(data, blockOffset));
                    break;
                case "EVP1":
                    ReadEnvelopes(data, blockOffset, blockSize, envelopes, inverseBindMatrices);
                    break;
                case "DRW1":
                    drawMatrices.AddRange(ReadDrawMatrices(data, blockOffset));
                    break;
                case "VTX1":
                    vertexData = ReadVertexData(data, blockOffset);
                    break;
                case "SHP1":
                    shapes.AddRange(ReadShapes(data, blockOffset));
                    break;
                case "MAT3":
                    materials.AddRange(Mat3Reader.Read(data, blockOffset));
                    break;
                case "TEX1":
                    textures.AddRange(Tex1Reader.Read(data, blockOffset));
                    break;
            }

            blockOffset += blockSize;
        }

        return new BDLModel
        {
            FormatTag = formatTag,
            Chunks = chunks,
            HierarchyNodes = hierarchyNodes,
            Joints = joints,
            Envelopes = envelopes,
            InverseBindMatrices = inverseBindMatrices,
            DrawMatrices = drawMatrices,
            VertexData = vertexData,
            Shapes = shapes,
            Materials = materials,
            Textures = textures,
        };
    }

    private static List<BDLHierarchyNode> ReadHierarchy(byte[] data, int blockOffset)
    {
        uint hierarchyOffsetRaw = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x14, 4));
        int hierarchyOffset = blockOffset + (int)hierarchyOffsetRaw;

        var nodes = new List<BDLHierarchyNode>();
        int pos = hierarchyOffset;
        BDLHierarchyNodeType type;
        do
        {
            type = (BDLHierarchyNodeType)BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2));
            ushort index = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 2, 2));
            nodes.Add(new BDLHierarchyNode(type, index));
            pos += 4;
        } while (type != BDLHierarchyNodeType.Exit);

        return nodes;
    }

    private static List<BDLJoint> ReadJoints(byte[] data, int blockOffset)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x8, 2));
        int initDataOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0xC, 4));
        int nameTableOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x14, 4));

        IReadOnlyList<string> names = BDLTables.ReadNameTable(data, nameTableOffset);

        var joints = new List<BDLJoint>(count);
        const int entrySize = 0x40;
        for (int i = 0; i < count; i++)
        {
            int entry = initDataOffset + i * entrySize;

            ushort matrixType = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x0, 2));
            byte attachFlag = data[entry + 0x2];

            Vector3 scale = ReadVector3(data, entry + 0x4);
            Vector3 rotationDegrees = new(
                RotationUnitsToDegrees(BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(entry + 0x10, 2))),
                RotationUnitsToDegrees(BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(entry + 0x12, 2))),
                RotationUnitsToDegrees(BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(entry + 0x14, 2))));
            Vector3 translation = ReadVector3(data, entry + 0x18);

            float boundingSphereRadius = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(entry + 0x24, 4));
            Vector3 boundingBoxMin = ReadVector3(data, entry + 0x28);
            Vector3 boundingBoxMax = ReadVector3(data, entry + 0x34);

            joints.Add(new BDLJoint
            {
                Name = i < names.Count ? names[i] : $"Joint{i}",
                MatrixType = matrixType,
                AttachFlag = attachFlag,
                Scale = scale,
                RotationDegrees = rotationDegrees,
                Translation = translation,
                BoundingSphereRadius = boundingSphereRadius,
                BoundingBoxMin = boundingBoxMin,
                BoundingBoxMax = boundingBoxMax,
            });
        }

        return joints;
    }

    private static void ReadEnvelopes(
        byte[] data, int blockOffset, int blockSize, List<BDLEnvelope> envelopes, List<BDLMatrix3x4> inverseBindMatrices)
    {
        ushort envelopeCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x8, 2));
        int jointCountsOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0xC, 4));
        int jointIndicesOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x10, 4));
        int weightsOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x14, 4));
        int matricesOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x18, 4));

        int runningIndex = 0;
        for (int i = 0; i < envelopeCount; i++)
        {
            byte jointCount = data[jointCountsOffset + i];

            var jointIndices = new ushort[jointCount];
            var weights = new float[jointCount];
            for (int j = 0; j < jointCount; j++)
            {
                jointIndices[j] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(jointIndicesOffset + (runningIndex + j) * 2, 2));
                weights[j] = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(weightsOffset + (runningIndex + j) * 4, 4));
            }

            envelopes.Add(new BDLEnvelope { JointIndices = jointIndices, Weights = weights });
            runningIndex += jointCount;
        }

        const int matrixByteSize = 12 * sizeof(float);
        int matrixCount = (blockOffset + blockSize - matricesOffset) / matrixByteSize;
        for (int i = 0; i < matrixCount; i++)
        {
            int matrixStart = matricesOffset + i * matrixByteSize;
            var values = new float[12];
            for (int j = 0; j < 12; j++)
            {
                values[j] = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(matrixStart + j * 4, 4));
            }

            inverseBindMatrices.Add(new BDLMatrix3x4(values));
        }
    }

    private static List<BDLDrawMatrix> ReadDrawMatrices(byte[] data, int blockOffset)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x8, 2));
        int flagsOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0xC, 4));
        int indicesOffset = blockOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blockOffset + 0x10, 4));

        var matrices = new List<BDLDrawMatrix>(count);
        for (int i = 0; i < count; i++)
        {
            bool isWeighted = data[flagsOffset + i] != 0;
            ushort index = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(indicesOffset + i * 2, 2));
            matrices.Add(new BDLDrawMatrix(isWeighted, index));
        }

        return matrices;
    }

    private static BDLVertexData ReadVertexData(byte[] data, int blockOffset)
    {
        int fmtListOffset = BDLTables.ReadOffset(data, blockOffset, 0x8);
        int? posOffset = BDLTables.ReadOptionalOffset(data, blockOffset, 0xC);
        int? nrmOffset = BDLTables.ReadOptionalOffset(data, blockOffset, 0x10);
        int? color0Offset = BDLTables.ReadOptionalOffset(data, blockOffset, 0x18);
        int? color1Offset = BDLTables.ReadOptionalOffset(data, blockOffset, 0x1C);
        var texCoordOffsets = new int?[8];
        for (int i = 0; i < 8; i++)
        {
            texCoordOffsets[i] = BDLTables.ReadOptionalOffset(data, blockOffset, 0x20 + i * 4);
        }

        var formats = new Dictionary<uint, BDLVertexAttributeFormat>();
        int fmtPos = fmtListOffset;
        while (true)
        {
            uint attr = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(fmtPos, 4));
            if (attr == (uint)GXAttr.Null)
            {
                break;
            }

            uint cnt = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(fmtPos + 4, 4));
            uint type = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(fmtPos + 8, 4));
            byte frac = data[fmtPos + 12];
            formats[attr] = new BDLVertexAttributeFormat(attr, cnt, type, frac);
            fmtPos += 16;
        }

        var texCoords = new BDLVertexArray?[8];
        for (int i = 0; i < 8; i++)
        {
            texCoords[i] = MakeArray(data, texCoordOffsets[i], formats, (uint)GXAttr.TexCoord0 + (uint)i, isColor: false);
        }

        return new BDLVertexData
        {
            Positions = MakeArray(data, posOffset, formats, (uint)GXAttr.Position, isColor: false),
            Normals = MakeArray(data, nrmOffset, formats, (uint)GXAttr.Normal, isColor: false),
            Color0 = MakeArray(data, color0Offset, formats, (uint)GXAttr.Color0, isColor: true),
            Color1 = MakeArray(data, color1Offset, formats, (uint)GXAttr.Color1, isColor: true),
            TexCoords = texCoords,
        };
    }

    private static BDLVertexArray? MakeArray(
        byte[] data, int? offset, Dictionary<uint, BDLVertexAttributeFormat> formats, uint attr, bool isColor)
    {
        if (offset is null || !formats.TryGetValue(attr, out BDLVertexAttributeFormat fmt))
        {
            return null;
        }

        int componentCount;
        if (isColor)
        {
            componentCount = (BDLColorFormat)fmt.ComponentType switch
            {
                BDLColorFormat.Rgb8 or BDLColorFormat.Rgbx8 => 3,
                BDLColorFormat.Rgba8 => 4,
                _ => 0,
            };
            int colorStride = (BDLColorFormat)fmt.ComponentType switch
            {
                BDLColorFormat.Rgb8 => 3,
                BDLColorFormat.Rgbx8 or BDLColorFormat.Rgba8 => 4,
                BDLColorFormat.Rgb565 or BDLColorFormat.Rgba4 => 2,
                BDLColorFormat.Rgba6 => 3,
                _ => throw new NotSupportedException($"Unknown color format {fmt.ComponentType}."),
            };
            return new BDLVertexArray
            {
                Data = data,
                Offset = offset.Value,
                Stride = colorStride,
                ComponentCount = componentCount,
                ComponentType = fmt.ComponentType,
                FractionalBits = 0,
                IsColor = true,
            };
        }

        if (attr == (uint)GXAttr.Normal && fmt.ComponentCount != 0)
        {
            throw new NotSupportedException("NBT/NBT3 normal encoding isn't implemented yet.");
        }

        componentCount = attr switch
        {
            (uint)GXAttr.Position => fmt.ComponentCount == 0 ? 2 : 3,
            (uint)GXAttr.Normal => 3,
            _ => fmt.ComponentCount == 0 ? 1 : 2,
        };

        int componentSize = (GXCompType)fmt.ComponentType switch
        {
            GXCompType.Unsigned8 or GXCompType.Signed8 => 1,
            GXCompType.Unsigned16 or GXCompType.Signed16 => 2,
            GXCompType.Float32 => 4,
            _ => throw new NotSupportedException($"Unknown GX component type {fmt.ComponentType}."),
        };

        return new BDLVertexArray
        {
            Data = data,
            Offset = offset.Value,
            Stride = componentCount * componentSize,
            ComponentCount = componentCount,
            ComponentType = fmt.ComponentType,
            FractionalBits = fmt.FractionalBits,
            IsColor = false,
        };
    }

    private static List<BDLShape> ReadShapes(byte[] data, int blockOffset)
    {
        ushort shapeCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x8, 2));
        int shapeInitDataOffset = BDLTables.ReadOffset(data, blockOffset, 0xC);
        int indexTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x10);
        int vtxDescListTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x18);
        int mtxTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x1C);
        int displayListDataOffset = BDLTables.ReadOffset(data, blockOffset, 0x20);
        int mtxInitDataOffset = BDLTables.ReadOffset(data, blockOffset, 0x24);
        int drawInitDataOffset = BDLTables.ReadOffset(data, blockOffset, 0x28);

        var shapes = new List<BDLShape>(shapeCount);
        for (int shapeNo = 0; shapeNo < shapeCount; shapeNo++)
        {
            ushort initDataSlot = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(indexTableOffset + shapeNo * 2, 2));
            int entry = shapeInitDataOffset + initDataSlot * 0x28;

            byte matrixType = data[entry + 0x0];

            ushort mtxGroupNum = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x2, 2));
            ushort vcdOffset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x4, 2));
            ushort mtxOffset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x6, 2));
            ushort drawOffset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x8, 2));
            float boundingSphereRadius = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(entry + 0xC, 4));
            Vector3 bboxMin = ReadVector3(data, entry + 0x10);
            Vector3 bboxMax = ReadVector3(data, entry + 0x1C);

            List<(uint Attribute, uint IndexType)> vcd = ReadVertexDescriptorList(data, vtxDescListTableOffset + vcdOffset);

            var packets = new List<BDLPacket>(mtxGroupNum);
            for (int packetIndex = 0; packetIndex < mtxGroupNum; packetIndex++)
            {
                int mtxInitEntry = mtxInitDataOffset + (mtxOffset + packetIndex) * 0x8;
                ushort useMtxIndex = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(mtxInitEntry + 0x0, 2));

                int drawInitEntry = drawInitDataOffset + (drawOffset + packetIndex) * 0x8;
                uint displayListSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(drawInitEntry + 0x0, 4));
                uint displayListStartOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(drawInitEntry + 0x4, 4));

                int displayListStart = displayListDataOffset + (int)displayListStartOffset;

                Func<int, ushort>? resolvePosMtxIndex = null;
                if (matrixType == 3)
                {
                    int capturedPacketIndex = packetIndex;
                    resolvePosMtxIndex = slot =>
                        ResolvePerVertexDrawMatrixIndex(data, mtxInitDataOffset, mtxTableOffset, mtxOffset, capturedPacketIndex, slot);
                }

                List<BDLPrimitive> primitives = ReadPrimitives(data, displayListStart, (int)displayListSize, vcd, resolvePosMtxIndex);

                packets.Add(new BDLPacket { DrawMatrixIndex = useMtxIndex, Primitives = primitives });
            }

            shapes.Add(new BDLShape
            {
                MatrixType = matrixType,
                BoundingSphereRadius = boundingSphereRadius,
                BoundingBoxMin = bboxMin,
                BoundingBoxMax = bboxMax,
                Packets = packets,
            });
        }

        return shapes;
    }

    private static List<(uint Attribute, uint IndexType)> ReadVertexDescriptorList(byte[] data, int start)
    {
        var list = new List<(uint, uint)>();
        int pos = start;
        while (true)
        {
            uint attr = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
            if (attr == (uint)GXAttr.Null)
            {
                break;
            }

            uint type = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4, 4));
            list.Add((attr, type));
            pos += 8;
        }

        return list;
    }

    private static List<BDLPrimitive> ReadPrimitives(
        byte[] data, int start, int size, List<(uint Attribute, uint IndexType)> vcd, Func<int, ushort>? resolvePosMtxIndex)
    {
        var primitives = new List<BDLPrimitive>();
        int pos = start;
        int end = start + size;

        while (pos < end)
        {
            byte primTypeByte = data[pos];
            pos += 1;
            if (primTypeByte == 0)
            {
                break;
            }

            ushort vertexCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2));
            pos += 2;

            var vertices = new List<BDLShapeVertex>(vertexCount);
            for (int v = 0; v < vertexCount; v++)
            {
                int? positionIndex = null;
                int? normalIndex = null;
                int? color0Index = null;
                int? color1Index = null;
                var texCoordIndices = new int?[8];
                ushort? drawMatrixIndexOverride = null;

                foreach ((uint attr, uint indexType) in vcd)
                {
                    int? value = indexType switch
                    {
                        0 => null,
                        1 => ReadDirectValue(data, ref pos, attr),
                        2 => data[pos++],
                        3 => ReadU16Advance(data, ref pos),
                        _ => throw new NotSupportedException($"Unknown GX attribute index type {indexType}."),
                    };

                    if (value is null)
                    {
                        continue;
                    }

                    if (attr == (uint)GXAttr.Position)
                    {
                        positionIndex = value;
                    }
                    else if (attr == (uint)GXAttr.Normal)
                    {
                        normalIndex = value;
                    }
                    else if (attr == (uint)GXAttr.Color0)
                    {
                        color0Index = value;
                    }
                    else if (attr == (uint)GXAttr.Color1)
                    {
                        color1Index = value;
                    }
                    else if (attr >= (uint)GXAttr.TexCoord0 && attr <= (uint)GXAttr.TexCoord0 + 7)
                    {
                        texCoordIndices[attr - (uint)GXAttr.TexCoord0] = value;
                    }
                    else if (attr == 0 && resolvePosMtxIndex is not null)
                    {
                        drawMatrixIndexOverride = resolvePosMtxIndex(value.Value / 3);
                    }
                }

                vertices.Add(new BDLShapeVertex
                {
                    PositionIndex = positionIndex ?? throw new InvalidDataException("Shape vertex has no position index."),
                    NormalIndex = normalIndex,
                    Color0Index = color0Index,
                    Color1Index = color1Index,
                    TexCoordIndices = texCoordIndices,
                    DrawMatrixIndexOverride = drawMatrixIndexOverride,
                });
            }

            primitives.Add(new BDLPrimitive { Type = (BDLPrimitiveType)primTypeByte, Vertices = vertices });
        }

        return primitives;
    }

    private static int? ReadDirectValue(byte[] data, ref int pos, uint attr)
    {
        bool isMatrixIndexAttr = attr <= 8;
        if (!isMatrixIndexAttr)
        {
            throw new NotSupportedException($"Direct-mode vertex data for attribute {attr} isn't implemented.");
        }

        int value = data[pos];
        pos += 1;
        return value;
    }

    private static int ReadU16Advance(byte[] data, ref int pos)
    {
        int value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2));
        pos += 2;
        return value;
    }

    private static ushort ResolvePerVertexDrawMatrixIndex(
        byte[] data, int mtxInitDataOffset, int mtxTableOffset, int shapeMtxOffset, int packetIndex, int slot)
    {
        for (int matrixInitIndex = shapeMtxOffset + packetIndex; matrixInitIndex >= 0; matrixInitIndex--)
        {
            int mtxInitEntry = mtxInitDataOffset + matrixInitIndex * 0x8;
            uint tableStart = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(mtxInitEntry + 0x4, 4));
            ushort candidate = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(mtxTableOffset + (int)(tableStart + slot) * 2, 2));
            if (candidate != 0xFFFF)
            {
                return candidate;
            }
        }

        throw new InvalidDataException("Could not resolve a per-vertex draw matrix index (every candidate back to index 0 was 0xFFFF).");
    }

    private static float RotationUnitsToDegrees(short value) => value * (180f / 32768f);

    private static Vector3 ReadVector3(byte[] data, int offset) => new(
        BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 0, 4)),
        BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 4, 4)),
        BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 8, 4)));
}
