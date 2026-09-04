using System.Buffers.Binary;
using System.Numerics;

namespace SMGEditor.Core.Formats;

public enum BDLCullMode : uint
{
    None = 0,
    Front = 1,
    Back = 2,
    All = 3,
}

public enum BDLCompare : byte
{
    Never = 0,
    Less = 1,
    Equal = 2,
    LessEqual = 3,
    Greater = 4,
    NotEqual = 5,
    GreaterEqual = 6,
    Always = 7,
}

public enum BDLBlendType : byte
{
    None = 0,
    Blend = 1,
    Logic = 2,
    Subtract = 3,
}

public enum BDLBlendFactor : byte
{
    Zero = 0,
    One = 1,
    SrcColor = 2,
    InverseSrcColor = 3,
    SrcAlpha = 4,
    InverseSrcAlpha = 5,
    DstAlpha = 6,
    InverseDstAlpha = 7,
}

public readonly record struct BDLColor(byte R, byte G, byte B, byte A);

public readonly record struct BDLTevRegisterColor(short R, short G, short B, short A);

public readonly record struct BDLTevStage(
    byte ColorInA, byte ColorInB, byte ColorInC, byte ColorInD,
    byte ColorOp, byte ColorBias, byte ColorScale, bool ColorClamp, byte ColorOutReg,
    byte AlphaInA, byte AlphaInB, byte AlphaInC, byte AlphaInD,
    byte AlphaOp, byte AlphaBias, byte AlphaScale, bool AlphaClamp, byte AlphaOutReg,
    byte KonstColorSel, byte KonstAlphaSel);

public readonly record struct BDLTexMatrix(
    byte TexEffect, bool IsMaya, Vector3 Origin, Vector2 Scale, float RotationRadians, Vector2 Translation);

public readonly record struct BDLZMode(bool DepthTestEnable, BDLCompare Function, bool DepthWriteEnable);

public readonly record struct BDLBlendMode(BDLBlendType Type, BDLBlendFactor SrcFactor, BDLBlendFactor DstFactor);

public readonly record struct BDLAlphaCompare(BDLCompare Compare0, byte Reference0, byte Operation, BDLCompare Compare1, byte Reference1);

public readonly record struct BDLTexCoordGen(byte GenType, byte Source, byte Matrix);

public readonly record struct BDLTevOrder(byte TexCoordIndex, byte TexMapIndex, byte ColorChannel);

public readonly record struct BDLIndTexOrder(byte TexCoordIndex, byte TexMapIndex);

public readonly record struct BDLIndTexMatrix(float M00, float M01, float M02, float M10, float M11, float M12, float ScaleMultiplier);

public readonly record struct BDLIndTexCoordScale(float ScaleS, float ScaleT);

public readonly record struct BDLIndTevStage(byte IndStage, byte Format, byte BiasSel, byte MtxSel, byte WrapS, byte WrapT, byte AddPrev, byte UtcLod, byte AlphaSel);

public enum BDLColorSource : byte
{
    Register = 0,
    Vertex = 1,
}

public enum BDLDiffuseFn : byte
{
    None = 0,
    Sign = 1,
    Clamp = 2,
}

public readonly record struct BDLColorChan(
    bool Enabled,
    BDLColorSource MaterialSource,
    byte LitMask,
    BDLDiffuseFn DiffuseFn,
    byte AttenuationFn,
    BDLColorSource AmbientSource);

public sealed class BDLMaterial
{
    public required string Name { get; init; }
    public required BDLCullMode CullMode { get; init; }
    public required BDLColor MaterialColor { get; init; }
    public required BDLColor AmbientColor { get; init; }
    public required byte TevStageCount { get; init; }

    public required IReadOnlyList<ushort?> TextureIndices { get; init; }

    public required IReadOnlyList<BDLTexCoordGen> TexCoordGens { get; init; }

    public required IReadOnlyList<BDLTexMatrix?> TexMatrices { get; init; }

    public required IReadOnlyList<BDLTevOrder> TevOrders { get; init; }
    public required IReadOnlyList<BDLTevStage> TevStages { get; init; }

    public required IReadOnlyList<BDLIndTexOrder> IndTexOrders { get; init; }

    public required IReadOnlyList<BDLIndTexMatrix?> IndTexMatrices { get; init; }

    public required IReadOnlyList<BDLIndTexCoordScale> IndTexCoordScales { get; init; }

    public required IReadOnlyList<BDLIndTevStage?> IndTevStages { get; init; }

    public required IReadOnlyList<BDLTevRegisterColor> TevRegisters { get; init; }

    public required IReadOnlyList<BDLColor> TevKonstColors { get; init; }

    public required BDLColorChan ColorChannel0 { get; init; }
    public required BDLAlphaCompare AlphaCompare { get; init; }
    public required BDLBlendMode BlendMode { get; init; }
    public required BDLZMode ZMode { get; init; }

    public BDLMaterial With(BDLColor? materialColor = null, IReadOnlyList<BDLTevRegisterColor>? tevRegisters = null, IReadOnlyList<BDLColor>? tevKonstColors = null) => new()
    {
        Name = Name,
        CullMode = CullMode,
        MaterialColor = materialColor ?? MaterialColor,
        AmbientColor = AmbientColor,
        TevStageCount = TevStageCount,
        TextureIndices = TextureIndices,
        TexCoordGens = TexCoordGens,
        TexMatrices = TexMatrices,
        TevOrders = TevOrders,
        TevStages = TevStages,
        IndTexOrders = IndTexOrders,
        IndTexMatrices = IndTexMatrices,
        IndTexCoordScales = IndTexCoordScales,
        IndTevStages = IndTevStages,
        TevRegisters = tevRegisters ?? TevRegisters,
        TevKonstColors = tevKonstColors ?? TevKonstColors,
        ColorChannel0 = ColorChannel0,
        AlphaCompare = AlphaCompare,
        BlendMode = BlendMode,
        ZMode = ZMode,
    };
}

public static class Mat3Reader
{
    private const int InitDataSize = 0x14C;

    public static List<BDLMaterial> Read(byte[] data, int blockOffset)
    {
        ushort materialCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x8, 2));
        int initDataOffset = BDLTables.ReadOffset(data, blockOffset, 0xC);
        int materialIdOffset = BDLTables.ReadOffset(data, blockOffset, 0x10);
        int nameTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x14);
        int cullModeOffset = BDLTables.ReadOffset(data, blockOffset, 0x1C);
        int matColorOffset = BDLTables.ReadOffset(data, blockOffset, 0x20);
        int colorChanInfoOffset = BDLTables.ReadOffset(data, blockOffset, 0x28);
        int? ambColorOffset = BDLTables.ReadOptionalOffset(data, blockOffset, 0x2C);
        int texGenNumOffset = BDLTables.ReadOffset(data, blockOffset, 0x34);
        int texCoordInfoOffset = BDLTables.ReadOffset(data, blockOffset, 0x38);
        int texMatrixInfoOffset = BDLTables.ReadOffset(data, blockOffset, 0x40);
        int texNoOffset = BDLTables.ReadOffset(data, blockOffset, 0x48);
        int tevOrderInfoOffset = BDLTables.ReadOffset(data, blockOffset, 0x4C);
        int tevColorOffset = BDLTables.ReadOffset(data, blockOffset, 0x50);
        int tevKColorOffset = BDLTables.ReadOffset(data, blockOffset, 0x54);
        int tevStageNumOffset = BDLTables.ReadOffset(data, blockOffset, 0x58);
        int tevStageInfoOffset = BDLTables.ReadOffset(data, blockOffset, 0x5C);
        int alphaCompInfoOffset = BDLTables.ReadOffset(data, blockOffset, 0x6C);
        int blendInfoOffset = BDLTables.ReadOffset(data, blockOffset, 0x70);
        int zModeInfoOffset = BDLTables.ReadOffset(data, blockOffset, 0x74);

        int? indInitDataOffset = BDLTables.ReadOptionalOffset(data, blockOffset, 0x18);

        IReadOnlyList<string> names = BDLTables.ReadNameTable(data, nameTableOffset);

        var materials = new List<BDLMaterial>(materialCount);
        for (int i = 0; i < materialCount; i++)
        {
            ushort initDataSlot = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(materialIdOffset + i * 2, 2));
            int entry = initDataOffset + initDataSlot * InitDataSize;

            byte cullModeIdx = data[entry + 0x1];
            BDLCullMode cullMode = cullModeIdx != 0xFF
                ? (BDLCullMode)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(cullModeOffset + cullModeIdx * 4, 4))
                : BDLCullMode.Back;

            byte tevStageNumIdx = data[entry + 0x4];
            byte tevStageCount = tevStageNumIdx != 0xFF ? data[tevStageNumOffset + tevStageNumIdx] : (byte)1;

            ushort matColorIdx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x8, 2));
            BDLColor matColor = matColorIdx != 0xFFFF ? ReadColor(data, matColorOffset + matColorIdx * 4) : new BDLColor(255, 255, 255, 255);

            ushort colorChanIdx0 = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0xC, 2));
            BDLColorChan colorChannel0 = colorChanIdx0 != 0xFFFF
                ? ReadColorChan(data, colorChanInfoOffset + colorChanIdx0 * 8)
                : new BDLColorChan(false, BDLColorSource.Vertex, 0, BDLDiffuseFn.None, 0, BDLColorSource.Register);

            ushort ambColorIdx0 = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x14, 2));
            BDLColor ambColor = ambColorOffset is int ao && ambColorIdx0 != 0xFFFF
                ? ReadColor(data, ao + ambColorIdx0 * 4)
                : new BDLColor(0, 0, 0, 0);

            byte texGenNumIdx = data[entry + 0x3];
            byte texGenNum = texGenNumIdx != 0xFF ? data[texGenNumOffset + texGenNumIdx] : (byte)0;

            var texCoordGens = new List<BDLTexCoordGen>(texGenNum);
            for (int t = 0; t < texGenNum; t++)
            {
                ushort idx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x28 + t * 2, 2));
                texCoordGens.Add(idx != 0xFFFF
                    ? ReadTexCoordGen(data, texCoordInfoOffset + idx * 4)
                    : new BDLTexCoordGen(0, 0, 0));
            }

            var texMatrices = new BDLTexMatrix?[10];
            for (int t = 0; t < 10; t++)
            {
                ushort idx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x48 + t * 2, 2));
                texMatrices[t] = idx != 0xFFFF ? ReadTexMatrix(data, texMatrixInfoOffset + idx * 100) : null;
            }

            var textureIndices = new ushort?[8];
            for (int t = 0; t < 8; t++)
            {
                ushort idx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x84 + t * 2, 2));
                textureIndices[t] = idx != 0xFFFF ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(texNoOffset + idx * 2, 2)) : null;
            }

            var tevOrders = new List<BDLTevOrder>(tevStageCount);
            for (int t = 0; t < tevStageCount; t++)
            {
                ushort idx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0xBC + t * 2, 2));
                tevOrders.Add(idx != 0xFFFF
                    ? ReadTevOrder(data, tevOrderInfoOffset + idx * 4)
                    : new BDLTevOrder(0xFF, 0xFF, 0xFF));
            }

            var tevStages = new List<BDLTevStage>(tevStageCount);
            for (int t = 0; t < tevStageCount; t++)
            {
                ushort idx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0xE4 + t * 2, 2));
                byte konstColorSel = data[entry + 0x9C + t];
                byte konstAlphaSel = data[entry + 0xAC + t];
                tevStages.Add(idx != 0xFFFF
                    ? ReadTevStage(data, tevStageInfoOffset + idx * 20, konstColorSel, konstAlphaSel)
                    : DefaultModulateTevStage(konstColorSel, konstAlphaSel));
            }

            var tevRegisters = new BDLTevRegisterColor[4];
            var tevKonstColors = new BDLColor[4];
            for (int r = 0; r < 4; r++)
            {
                ushort colorIdx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0xDC + r * 2, 2));
                tevRegisters[r] = colorIdx != 0xFFFF ? ReadTevRegisterColor(data, tevColorOffset + colorIdx * 8) : new BDLTevRegisterColor(255, 255, 255, 255);

                ushort konstIdx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x94 + r * 2, 2));
                tevKonstColors[r] = konstIdx != 0xFFFF ? ReadColor(data, tevKColorOffset + konstIdx * 4) : new BDLColor(255, 255, 255, 255);
            }

            (IReadOnlyList<BDLIndTexOrder> indTexOrders, IReadOnlyList<BDLIndTexMatrix?> indTexMatrices,
                IReadOnlyList<BDLIndTexCoordScale> indTexCoordScales, IReadOnlyList<BDLIndTevStage?> indTevStages) =
                ReadIndBlock(data, indInitDataOffset, i, tevStageCount);

            ushort alphaCompIdx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x146, 2));
            BDLAlphaCompare alphaCompare = alphaCompIdx != 0xFFFF
                ? ReadAlphaCompare(data, alphaCompInfoOffset + alphaCompIdx * 8)
                : new BDLAlphaCompare(BDLCompare.Always, 0, 0, BDLCompare.Always, 0);

            ushort blendIdx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x148, 2));
            BDLBlendMode blendMode = blendIdx != 0xFFFF
                ? ReadBlendMode(data, blendInfoOffset + blendIdx * 4)
                : new BDLBlendMode(BDLBlendType.None, BDLBlendFactor.One, BDLBlendFactor.Zero);

            byte zModeIdx = data[entry + 0x6];
            BDLZMode zMode = zModeIdx != 0xFF
                ? ReadZMode(data, zModeInfoOffset + zModeIdx * 4)
                : new BDLZMode(true, BDLCompare.LessEqual, true);

            materials.Add(new BDLMaterial
            {
                Name = i < names.Count ? names[i] : $"Material{i}",
                CullMode = cullMode,
                MaterialColor = matColor,
                AmbientColor = ambColor,
                TevStageCount = tevStageCount,
                TextureIndices = textureIndices,
                TexCoordGens = texCoordGens,
                TexMatrices = texMatrices,
                TevOrders = tevOrders,
                TevStages = tevStages,
                IndTexOrders = indTexOrders,
                IndTexMatrices = indTexMatrices,
                IndTexCoordScales = indTexCoordScales,
                IndTevStages = indTevStages,
                TevRegisters = tevRegisters,
                TevKonstColors = tevKonstColors,
                ColorChannel0 = colorChannel0,
                AlphaCompare = alphaCompare,
                BlendMode = blendMode,
                ZMode = zMode,
            });
        }

        return materials;
    }

    private static BDLColor ReadColor(byte[] data, int offset) => new(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]);

    private static BDLTexCoordGen ReadTexCoordGen(byte[] data, int offset) => new(data[offset], data[offset + 1], data[offset + 2]);

    private static BDLTevOrder ReadTevOrder(byte[] data, int offset) => new(data[offset], data[offset + 1], data[offset + 2]);

    private static BDLTevStage ReadTevStage(byte[] data, int offset, byte konstColorSel, byte konstAlphaSel) => new(
        ColorInA: data[offset + 1], ColorInB: data[offset + 2], ColorInC: data[offset + 3], ColorInD: data[offset + 4],
        ColorOp: data[offset + 5], ColorBias: data[offset + 6], ColorScale: data[offset + 7], ColorClamp: data[offset + 8] != 0, ColorOutReg: data[offset + 9],
        AlphaInA: data[offset + 10], AlphaInB: data[offset + 11], AlphaInC: data[offset + 12], AlphaInD: data[offset + 13],
        AlphaOp: data[offset + 14], AlphaBias: data[offset + 15], AlphaScale: data[offset + 16], AlphaClamp: data[offset + 17] != 0, AlphaOutReg: data[offset + 18],
        KonstColorSel: konstColorSel, KonstAlphaSel: konstAlphaSel);

    private static BDLTevStage DefaultModulateTevStage(byte konstColorSel, byte konstAlphaSel) => new(
        ColorInA: 15, ColorInB: 15, ColorInC: 15, ColorInD: 10,
        ColorOp: 0, ColorBias: 0, ColorScale: 0, ColorClamp: true, ColorOutReg: 0,
        AlphaInA: 7, AlphaInB: 7, AlphaInC: 7, AlphaInD: 5,
        AlphaOp: 0, AlphaBias: 0, AlphaScale: 0, AlphaClamp: true, AlphaOutReg: 0,
        KonstColorSel: konstColorSel, KonstAlphaSel: konstAlphaSel);

    private static BDLTexMatrix ReadTexMatrix(byte[] data, int offset)
    {
        byte attributes = data[offset + 1];
        bool isMaya = (attributes & 0x80) != 0;
        byte texEffect = (byte)(attributes & 0x7F);

        var origin = new Vector3(
            BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 0x4, 4)),
            BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 0x8, 4)),
            BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 0xC, 4)));

        var scale = new Vector2(
            BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 0x10, 4)),
            BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 0x14, 4)));

        short rawRotation = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset + 0x18, 2));
        float rotationRadians = rawRotation * MathF.PI / 32768f;

        var translation = new Vector2(
            BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 0x1C, 4)),
            BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + 0x20, 4)));

        return new BDLTexMatrix(texEffect, isMaya, origin, scale, rotationRadians, translation);
    }

    private static BDLTevRegisterColor ReadTevRegisterColor(byte[] data, int offset) => new(
        BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2)),
        BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset + 2, 2)),
        BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset + 4, 2)),
        BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset + 6, 2)));

    private static BDLColorChan ReadColorChan(byte[] data, int offset) => new(
        Enabled: data[offset] != 0,
        MaterialSource: (BDLColorSource)data[offset + 1],
        LitMask: data[offset + 2],
        DiffuseFn: (BDLDiffuseFn)Math.Min(data[offset + 3], (byte)2),
        AttenuationFn: data[offset + 4],
        AmbientSource: (BDLColorSource)data[offset + 5]);

    private static BDLAlphaCompare ReadAlphaCompare(byte[] data, int offset) =>
        new((BDLCompare)data[offset], data[offset + 1], data[offset + 2], (BDLCompare)data[offset + 3], data[offset + 4]);

    private static BDLBlendMode ReadBlendMode(byte[] data, int offset) =>
        new((BDLBlendType)data[offset], (BDLBlendFactor)data[offset + 1], (BDLBlendFactor)data[offset + 2]);

    private static BDLZMode ReadZMode(byte[] data, int offset) => new(data[offset] != 0, (BDLCompare)data[offset + 1], data[offset + 2] != 0);

    private const int IndInitDataSize = 0x138;

    private static (IReadOnlyList<BDLIndTexOrder> Orders, IReadOnlyList<BDLIndTexMatrix?> Matrices,
        IReadOnlyList<BDLIndTexCoordScale> CoordScales, IReadOnlyList<BDLIndTevStage?> Stages) ReadIndBlock(
        byte[] data, int? indInitDataOffset, int materialIndex, byte tevStageCount)
    {
        if (indInitDataOffset is not int baseOffset)
        {
            return (Array.Empty<BDLIndTexOrder>(), Array.Empty<BDLIndTexMatrix?>(), Array.Empty<BDLIndTexCoordScale>(), new BDLIndTevStage?[tevStageCount]);
        }

        int entry = baseOffset + materialIndex * IndInitDataSize;
        bool enabled = data[entry] != 0;
        byte indTexStageNum = enabled ? data[entry + 1] : (byte)0;

        var orders = new BDLIndTexOrder[Math.Min((int)indTexStageNum, 3)];
        for (int s = 0; s < orders.Length; s++)
        {
            int o = entry + 0x4 + s * 4;
            orders[s] = new BDLIndTexOrder(data[o], data[o + 1]);
        }

        var matrices = new BDLIndTexMatrix?[3];
        var coordScales = new BDLIndTexCoordScale[3];
        if (enabled)
        {
            for (int m = 0; m < 3; m++)
            {
                int o = entry + 0x14 + m * 0x1C;
                float m00 = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(o + 0x00, 4));
                float m01 = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(o + 0x04, 4));
                float m02 = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(o + 0x08, 4));
                float m10 = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(o + 0x0C, 4));
                float m11 = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(o + 0x10, 4));
                float m12 = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(o + 0x14, 4));
                sbyte scaleExp = unchecked((sbyte)data[o + 0x18]);
                matrices[m] = new BDLIndTexMatrix(m00, m01, m02, m10, m11, m12, MathF.Pow(2f, scaleExp));
            }

            for (int c = 0; c < 3; c++)
            {
                int o = entry + 0x68 + c * 4;
                coordScales[c] = new BDLIndTexCoordScale(MathF.Pow(0.5f, data[o]), MathF.Pow(0.5f, data[o + 1]));
            }
        }

        var stages = new BDLIndTevStage?[tevStageCount];
        if (enabled)
        {
            for (int t = 0; t < tevStageCount; t++)
            {
                int o = entry + 0x78 + t * 0xC;
                byte mtxSel = data[o + 3];

                if (mtxSel == 0)
                {
                    continue;
                }

                stages[t] = new BDLIndTevStage(
                    IndStage: data[o], Format: data[o + 1], BiasSel: data[o + 2], MtxSel: mtxSel,
                    WrapS: data[o + 4], WrapT: data[o + 5], AddPrev: data[o + 6], UtcLod: data[o + 7], AlphaSel: data[o + 8]);
            }
        }

        return (orders, matrices, coordScales, stages);
    }
}
