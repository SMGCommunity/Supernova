using Silk.NET.OpenGL;

namespace SMGEditor.Editor;

internal sealed class ViewportFramebuffer
{
    private readonly GL _gl;
    private uint _fbo;
    private uint _depthRbo;

    public uint ColorTexture { get; private set; }
    public uint Width { get; private set; }
    public uint Height { get; private set; }

    public ViewportFramebuffer(GL gl)
    {
        _gl = gl;
    }

    public void EnsureSize(uint width, uint height)
    {
        if (width == 0 || height == 0 || (width == Width && height == Height))
        {
            return;
        }

        Destroy();

        Width = width;
        Height = height;

        _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        ColorTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, ColorTexture);
        unsafe
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        }

        int linearFilter = (int)GLEnum.Linear;
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in linearFilter);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in linearFilter);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, ColorTexture, 0);

        _depthRbo = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRbo);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, width, height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depthRbo);

        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Viewport framebuffer incomplete: {status}");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, Width, Height);
    }

    public static void Unbind(GL gl, uint windowWidth, uint windowHeight)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, windowWidth, windowHeight);
    }

    private void Destroy()
    {
        if (_fbo != 0)
        {
            _gl.DeleteFramebuffer(_fbo);
        }

        if (ColorTexture != 0)
        {
            _gl.DeleteTexture(ColorTexture);
        }

        if (_depthRbo != 0)
        {
            _gl.DeleteRenderbuffer(_depthRbo);
        }
    }
}
