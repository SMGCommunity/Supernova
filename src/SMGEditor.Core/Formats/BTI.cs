using System.Buffers.Binary;

namespace SMGEditor.Core.Formats;

public sealed class BTITexture
{
    public required BDLTextureFormat Format { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required BDLWrapMode WrapS { get; init; }
    public required BDLWrapMode WrapT { get; init; }

    public required byte[] Rgba { get; init; }
}

public static class BTIReader
{
    public static BTITexture Load(byte[] data)
    {
        var format = (BDLTextureFormat)data[0x0];
        int width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(0x2, 2));
        int height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(0x4, 2));
        var wrapS = (BDLWrapMode)data[0x6];
        var wrapT = (BDLWrapMode)data[0x7];
        uint imageDataOffsetRaw = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x1C, 4));

        byte[] rgba = Tex1Reader.DecodeTexture(data, (int)imageDataOffsetRaw, width, height, format);

        return new BTITexture
        {
            Format = format,
            Width = width,
            Height = height,
            WrapS = wrapS,
            WrapT = wrapT,
            Rgba = rgba,
        };
    }
}
