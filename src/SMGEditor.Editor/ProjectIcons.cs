using System.Reflection;
using SMGEditor.Viewer;
using Silk.NET.OpenGL;

namespace SMGEditor.Editor;

// project icon loading and storing
// we only need to support png. easy and simple
internal static class ProjectIcons
{
    private const string ResourcePrefix = "ProjectIcons.";

    public static readonly string[] BuiltInIconNames = Assembly.GetExecutingAssembly().GetManifestResourceNames()
        .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        .Select(n => n[ResourcePrefix.Length..^".png".Length])
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string BuiltInIconKey(string name) => $"builtin:{name}";
    public static string CustomIconKey(string absolutePath) => $"file:{absolutePath}";

    private static byte[]? TryGetPngBytes(string iconKey)
    {
        if (iconKey.StartsWith("builtin:", StringComparison.Ordinal))
        {
            string name = iconKey["builtin:".Length..];
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + name + ".png");
            if (stream is null)
            {
                return null;
            }

            using var mem = new MemoryStream();
            stream.CopyTo(mem);
            return mem.ToArray();
        }

        if (iconKey.StartsWith("file:", StringComparison.Ordinal))
        {
            string path = iconKey["file:".Length..];
            try
            {
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool TryDecodeRgba(byte[] pngBytes, out byte[] rgba, out int width, out int height)
    {
        try
        {
            (width, height, rgba) = ImageIo.DecodeRgba(pngBytes);
            return true;
        }
        catch
        {
            rgba = [];
            width = 0;
            height = 0;
            return false;
        }
    }

    public static bool TryDecodeRgba(string iconKey, out byte[] rgba, out int width, out int height)
    {
        if (TryGetPngBytes(iconKey) is { } pngBytes)
        {
            return TryDecodeRgba(pngBytes, out rgba, out width, out height);
        }

        rgba = [];
        width = 0;
        height = 0;
        return false;
    }
}

internal sealed class IconTextureCache
{
    private readonly Dictionary<string, uint?> _handles = new(StringComparer.Ordinal);

    public unsafe uint? GetOrCreate(GL gl, string? iconKey)
    {
        if (iconKey is null)
        {
            return null;
        }

        if (_handles.TryGetValue(iconKey, out uint? cached))
        {
            return cached;
        }

        uint? handle = null;
        if (ProjectIcons.TryDecodeRgba(iconKey, out byte[] rgba, out int width, out int height))
        {
            handle = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, handle.Value);
            fixed (byte* ptr = rgba)
            {
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }

            int linear = (int)GLEnum.Linear;
            int clamp = (int)GLEnum.ClampToEdge;
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in linear);
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in linear);
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, in clamp);
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, in clamp);
        }

        _handles[iconKey] = handle;
        return handle;
    }
}
