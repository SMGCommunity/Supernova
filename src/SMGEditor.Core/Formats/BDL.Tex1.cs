using System.Buffers.Binary;

namespace SMGEditor.Core.Formats;

public enum BDLTextureFormat : byte
{
    I4 = 0x0,
    I8 = 0x1,
    IA4 = 0x2,
    IA8 = 0x3,
    Rgb565 = 0x4,
    Rgb5A3 = 0x5,
    Rgba8 = 0x6,
    C4 = 0x8,
    C8 = 0x9,
    C14X2 = 0xA,
    Cmpr = 0xE,
}

public enum BDLWrapMode : byte
{
    Clamp,
    Repeat,
    Mirror,
}

public sealed class BDLTexture
{
    public required string Name { get; init; }
    public required BDLTextureFormat Format { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required BDLWrapMode WrapS { get; init; }
    public required BDLWrapMode WrapT { get; init; }
    public required byte[] Rgba { get; init; }
}

public static class Tex1Reader
{
    public static List<BDLTexture> Read(byte[] data, int blockOffset)
    {
        ushort textureCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(blockOffset + 0x8, 2));
        int headerTableOffset = BDLTables.ReadOffset(data, blockOffset, 0xC);
        int nameTableOffset = BDLTables.ReadOffset(data, blockOffset, 0x10);
        IReadOnlyList<string> names = BDLTables.ReadNameTable(data, nameTableOffset);

        var textures = new List<BDLTexture>(textureCount);
        for (int i = 0; i < textureCount; i++)
        {
            int entry = headerTableOffset + i * 0x20;
            var format = (BDLTextureFormat)data[entry];
            int width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x2, 2));
            int height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entry + 0x4, 2));
            var wrapS = (BDLWrapMode)data[entry + 0x6];
            var wrapT = (BDLWrapMode)data[entry + 0x7];
            uint imageDataOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry + 0x1C, 4));
            int start = entry + (int)imageDataOffset;

            byte[] rgba = DecodeTexture(data, start, width, height, format);

            textures.Add(new BDLTexture
            {
                Name = i < names.Count ? names[i] : $"Texture{i}",
                Format = format,
                Width = width,
                Height = height,
                WrapS = wrapS,
                WrapT = wrapT,
                Rgba = rgba,
            });
        }

        return textures;
    }

    internal static byte[] DecodeTexture(byte[] data, int start, int width, int height, BDLTextureFormat format)
    {
        byte[] rgba = new byte[width * height * 4];

        switch (format)
        {
            case BDLTextureFormat.I4:
                DecodeBlocks(start, width, height, 8, 8, (blockPos, x, y) =>
                {
                    byte raw = data[blockPos + y * 4 + x / 2];
                    byte value = (byte)(((x & 1) == 0 ? raw >> 4 : raw) & 0xF);
                    byte expanded = (byte)(value * 0x11);
                    return Intensity(expanded, expanded);
                }, bitsPerPixel: 4, rgba, width);
                break;
            case BDLTextureFormat.I8:
                DecodeBlocks(start, width, height, 8, 4, (blockPos, x, y) =>
                {
                    byte value = data[blockPos + y * 8 + x];
                    return Intensity(value, value);
                }, bitsPerPixel: 8, rgba, width);
                break;
            case BDLTextureFormat.IA4:
                DecodeBlocks(start, width, height, 8, 4, (blockPos, x, y) =>
                {
                    byte raw = data[blockPos + y * 8 + x];
                    byte alpha = (byte)((raw >> 4) * 0x11);
                    byte luminance = (byte)((raw & 0xF) * 0x11);
                    return Intensity(luminance, alpha);
                }, bitsPerPixel: 8, rgba, width);
                break;
            case BDLTextureFormat.IA8:
                DecodeBlocks(start, width, height, 4, 4, (blockPos, x, y) =>
                {
                    int p = blockPos + (y * 4 + x) * 2;
                    return Intensity(data[p + 1], data[p]);
                }, bitsPerPixel: 16, rgba, width);
                break;
            case BDLTextureFormat.Rgb565:
                DecodeBlocks(start, width, height, 4, 4, (blockPos, x, y) =>
                {
                    int p = blockPos + (y * 4 + x) * 2;
                    ushort texel = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p, 2));
                    return DecodeRgb565(texel);
                }, bitsPerPixel: 16, rgba, width);
                break;
            case BDLTextureFormat.Rgb5A3:
                DecodeBlocks(start, width, height, 4, 4, (blockPos, x, y) =>
                {
                    int p = blockPos + (y * 4 + x) * 2;
                    ushort texel = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p, 2));
                    return DecodeRgb5A3(texel);
                }, bitsPerPixel: 16, rgba, width);
                break;
            case BDLTextureFormat.Rgba8:
                DecodeRgba8(data, start, width, height, rgba);
                break;
            case BDLTextureFormat.Cmpr:
                DecodeCmpr(data, start, width, height, rgba);
                break;
            default:
                throw new NotSupportedException($"Paletted texture format {format} isn't implemented yet.");
        }

        return rgba;

        (byte R, byte G, byte B, byte A) Intensity(byte lum, byte a) => (lum, lum, lum, a);
    }

    private static void DecodeBlocks(
        int start, int width, int height, int blockWidth, int blockHeight,
        Func<int, int, int, (byte R, byte G, byte B, byte A)> readTexel, int bitsPerPixel,
        byte[] rgba, int imageWidth)
    {
        int blockByteSize = blockWidth * blockHeight * bitsPerPixel / 8;
        int blocksX = (width + blockWidth - 1) / blockWidth;
        int blocksY = (height + blockHeight - 1) / blockHeight;

        int pos = start;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                for (int y = 0; y < blockHeight; y++)
                {
                    for (int x = 0; x < blockWidth; x++)
                    {
                        int destX = bx * blockWidth + x;
                        int destY = by * blockHeight + y;
                        if (destX >= width || destY >= height)
                        {
                            continue;
                        }

                        (byte r, byte g, byte b, byte a) = readTexel(pos, x, y);
                        int destIndex = (destY * imageWidth + destX) * 4;
                        rgba[destIndex] = r;
                        rgba[destIndex + 1] = g;
                        rgba[destIndex + 2] = b;
                        rgba[destIndex + 3] = a;
                    }
                }

                pos += blockByteSize;
            }
        }
    }

    private static void DecodeRgba8(byte[] data, int start, int width, int height, byte[] rgba)
    {
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;

        int pos = start;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int destX = bx * 4 + x;
                        int destY = by * 4 + y;
                        if (destX < width && destY < height)
                        {
                            int destIndex = (destY * width + destX) * 4;
                            rgba[destIndex + 3] = data[pos];
                            rgba[destIndex] = data[pos + 1];
                        }

                        pos += 2;
                    }
                }

                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int destX = bx * 4 + x;
                        int destY = by * 4 + y;
                        if (destX < width && destY < height)
                        {
                            int destIndex = (destY * width + destX) * 4;
                            rgba[destIndex + 1] = data[pos];
                            rgba[destIndex + 2] = data[pos + 1];
                        }

                        pos += 2;
                    }
                }
            }
        }
    }

    private static void DecodeCmpr(byte[] data, int start, int width, int height, byte[] rgba)
    {
        int blocksX = (width + 7) / 8;
        int blocksY = (height + 7) / 8;

        int pos = start;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                for (int subY = 0; subY < 2; subY++)
                {
                    for (int subX = 0; subX < 2; subX++)
                    {
                        pos = DecodeCmprSubBlock(data, pos, rgba, width, height, bx * 8 + subX * 4, by * 8 + subY * 4);
                    }
                }
            }
        }
    }

    private static int DecodeCmprSubBlock(byte[] data, int pos, byte[] rgba, int width, int height, int destX0, int destY0)
    {
        ushort color0 = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2));
        ushort color1 = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 2, 2));
        uint bits = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4, 4));
        pos += 8;

        (byte R, byte G, byte B, byte A) c0 = DecodeRgb565(color0);
        (byte R, byte G, byte B, byte A) c1 = DecodeRgb565(color1);
        (byte R, byte G, byte B, byte A) c2, c3;

        if (color0 > color1)
        {
            c2 = ((byte)((2 * c0.R + c1.R) / 3), (byte)((2 * c0.G + c1.G) / 3), (byte)((2 * c0.B + c1.B) / 3), (byte)255);
            c3 = ((byte)((c0.R + 2 * c1.R) / 3), (byte)((c0.G + 2 * c1.G) / 3), (byte)((c0.B + 2 * c1.B) / 3), (byte)255);
        }
        else
        {
            c2 = ((byte)((c0.R + c1.R) / 2), (byte)((c0.G + c1.G) / 2), (byte)((c0.B + c1.B) / 2), (byte)255);
            c3 = (0, 0, 0, 0);
        }

        var colorTable = new[] { c0, c1, c2, c3 };

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int texelIndex = y * 4 + x;
                int bitOffset = (15 - texelIndex) * 2;
                int selector = (int)((bits >> bitOffset) & 3);

                int destX = destX0 + x;
                int destY = destY0 + y;
                if (destX >= width || destY >= height)
                {
                    continue;
                }

                (byte r, byte g, byte b, byte a) = colorTable[selector];
                int destIndex = (destY * width + destX) * 4;
                rgba[destIndex] = r;
                rgba[destIndex + 1] = g;
                rgba[destIndex + 2] = b;
                rgba[destIndex + 3] = a;
            }
        }

        return pos;
    }

    private static (byte R, byte G, byte B, byte A) DecodeRgb565(ushort texel)
    {
        int r5 = (texel >> 11) & 0x1F;
        int g6 = (texel >> 5) & 0x3F;
        int b5 = texel & 0x1F;

        byte r = (byte)((r5 << 3) | (r5 >> 2));
        byte g = (byte)((g6 << 2) | (g6 >> 4));
        byte b = (byte)((b5 << 3) | (b5 >> 2));
        return (r, g, b, 255);
    }

    private static (byte R, byte G, byte B, byte A) DecodeRgb5A3(ushort texel)
    {
        if ((texel & 0x8000) != 0)
        {
            int r5 = (texel >> 10) & 0x1F;
            int g5 = (texel >> 5) & 0x1F;
            int b5 = texel & 0x1F;
            byte r = (byte)((r5 << 3) | (r5 >> 2));
            byte g = (byte)((g5 << 3) | (g5 >> 2));
            byte b = (byte)((b5 << 3) | (b5 >> 2));
            return (r, g, b, 255);
        }
        else
        {
            int a3 = (texel >> 12) & 0x7;
            int r4 = (texel >> 8) & 0xF;
            int g4 = (texel >> 4) & 0xF;
            int b4 = texel & 0xF;
            byte a = (byte)((a3 << 5) | (a3 << 2) | (a3 >> 1));
            byte r = (byte)((r4 << 4) | r4);
            byte g = (byte)((g4 << 4) | g4);
            byte b = (byte)((b4 << 4) | b4);
            return (r, g, b, a);
        }
    }
}
