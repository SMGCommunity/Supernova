using StbImageSharp;
using StbImageWriteSharp;

namespace SMGEditor.Viewer;

public static class ImageIo
{
    public static (int Width, int Height, byte[] Rgba) DecodeRgba(byte[] fileBytes)
    {
        ImageResult result = ImageResult.FromMemory(fileBytes, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        return (result.Width, result.Height, result.Data);
    }

    public static (int Width, int Height, byte[] Rgba) DecodeRgba(string path) => DecodeRgba(File.ReadAllBytes(path));

    public static void WritePng(string path, int width, int height, byte[] rgba)
    {
        using FileStream stream = File.Create(path);
        new ImageWriter().WritePng(rgba, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }
}
