using System.Numerics;
using SMGEditor.Core.Formats;
using SMGEditor.Core.Simulation;
using Silk.NET.OpenGL;

namespace SMGEditor.Viewer;

public sealed partial class SceneRenderer
{
    private const string OceanRingVertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aUpVec;
        layout(location = 2) in float aCoordAcrossRail;
        layout(location = 3) in float aCoordOnRail;
        layout(location = 4) in float aTaper;
        layout(location = 5) in vec2 aUV;

        uniform mat4 uView;
        uniform mat4 uProjection;
        uniform float uWaveTheta1;
        uniform float uWaveTheta2;
        uniform float uWaveHeight1;
        uniform float uWaveHeight2;
        uniform vec2 uTex0Scroll;
        uniform vec2 uTex1Scroll;
        uniform vec2 uTex2Scroll;

        out vec2 vUV0;
        out vec2 vUV1;
        out vec2 vUV2;

        void main()
        {
            float wave2 = uWaveHeight2 * sin(uWaveTheta2 + 0.0025 * aCoordOnRail);
            float wave1 = uWaveHeight1 * sin(uWaveTheta1 + 0.003 * aCoordAcrossRail + 0.0003 * aCoordOnRail);
            float waveHeight = aTaper * (wave1 + wave2);
            vec3 displaced = aPos - aUpVec * waveHeight;

            gl_Position = uProjection * uView * vec4(displaced, 1.0);
            vUV0 = aUV + uTex0Scroll;
            vUV1 = aUV + uTex1Scroll;
            vUV2 = aUV + uTex2Scroll;
        }
        """;

    private const string OceanRingFragmentShaderSource = """
        #version 330 core
        in vec2 vUV0;
        in vec2 vUV1;
        in vec2 vUV2;
        out vec4 FragColor;

        uniform sampler2D uWaterTex;
        uniform sampler2D uReflectionTex;
        uniform sampler2D uIndirectTex;
        uniform vec2 uViewportSize;

        void main()
        {
            vec3 water0 = texture(uWaterTex, vUV0).rgb;
            vec3 water1 = texture(uWaterTex, vUV1).rgb;
            vec3 wavePattern = water0 * water1 * 0.5;

            vec3 darkTint = vec3(0x28, 0x28, 0x28) / 255.0;
            if (int(round(wavePattern.r * 255.0)) == 0x14)
                wavePattern += darkTint;

            vec2 screenUV = gl_FragCoord.xy / uViewportSize;
            vec3 indSample = texture(uIndirectTex, vUV2).rgb - 0.5;
            vec2 warpedUV = clamp(screenUV + indSample.xy * 0.1, 0.0, 1.0);
            vec3 reflection = texture(uReflectionTex, warpedUV).rgb;

            vec3 lightBlue = vec3(0x76, 0xD7, 0xFF) / 255.0;
            FragColor = vec4(wavePattern + reflection * lightBlue, 1.0);
        }
        """;

    private uint _oceanRingProgram;
    private int _uOceanViewLoc, _uOceanProjLoc;
    private int _uOceanWaveTheta1Loc, _uOceanWaveTheta2Loc, _uOceanWaveHeight1Loc, _uOceanWaveHeight2Loc;
    private int _uOceanTex0ScrollLoc, _uOceanTex1ScrollLoc, _uOceanTex2ScrollLoc;
    private int _uOceanViewportSizeLoc;

    private uint _oceanWaterTexture;
    private uint _oceanIndirectTexture;
    private bool _oceanTexturesLoaded;

    private uint _reflectionTexture;
    private int _reflectionTextureWidth;
    private int _reflectionTextureHeight;

    private void InitializeOceanRing(GL gl)
    {
        _oceanRingProgram = CreateProgram(gl, OceanRingVertexShaderSource, OceanRingFragmentShaderSource);
        _uOceanViewLoc = gl.GetUniformLocation(_oceanRingProgram, "uView");
        _uOceanProjLoc = gl.GetUniformLocation(_oceanRingProgram, "uProjection");
        _uOceanWaveTheta1Loc = gl.GetUniformLocation(_oceanRingProgram, "uWaveTheta1");
        _uOceanWaveTheta2Loc = gl.GetUniformLocation(_oceanRingProgram, "uWaveTheta2");
        _uOceanWaveHeight1Loc = gl.GetUniformLocation(_oceanRingProgram, "uWaveHeight1");
        _uOceanWaveHeight2Loc = gl.GetUniformLocation(_oceanRingProgram, "uWaveHeight2");
        _uOceanTex0ScrollLoc = gl.GetUniformLocation(_oceanRingProgram, "uTex0Scroll");
        _uOceanTex1ScrollLoc = gl.GetUniformLocation(_oceanRingProgram, "uTex1Scroll");
        _uOceanTex2ScrollLoc = gl.GetUniformLocation(_oceanRingProgram, "uTex2Scroll");
        _uOceanViewportSizeLoc = gl.GetUniformLocation(_oceanRingProgram, "uViewportSize");
        gl.UseProgram(_oceanRingProgram);
        gl.Uniform1(gl.GetUniformLocation(_oceanRingProgram, "uWaterTex"), 0);
        gl.Uniform1(gl.GetUniformLocation(_oceanRingProgram, "uReflectionTex"), 1);
        gl.Uniform1(gl.GetUniformLocation(_oceanRingProgram, "uIndirectTex"), 2);
    }

    public void EnsureOceanRingTextures(BTITexture water, BTITexture indirect)
    {
        if (_oceanTexturesLoaded)
        {
            return;
        }

        _oceanWaterTexture = CreateTexture(_gl, ToBdlTexture("Water", water));
        _oceanIndirectTexture = CreateTexture(_gl, ToBdlTexture("WaterIndirect", indirect));
        _oceanTexturesLoaded = true;
    }

    private static BDLTexture ToBdlTexture(string name, BTITexture tex) =>
        new() { Name = name, Format = tex.Format, Width = tex.Width, Height = tex.Height, WrapS = tex.WrapS, WrapT = tex.WrapT, Rgba = tex.Rgba };

    public OceanRingGpuMesh UploadOceanRingMesh(OceanRingSimState sim)
    {
        const int stride = OceanRingSimState.Stride;
        IReadOnlyList<OceanRingWaterPoint> points = sim.Points;

        var vertices = new float[points.Count * 11];
        for (int row = 0; row < sim.SegmentCount; row++)
        {
            for (int col = 0; col < stride; col++)
            {
                OceanRingWaterPoint p = points[row * stride + col];
                int b = (row * stride + col) * 11;
                // original position. this is where the water vertex rests
                vertices[b + 0] = p.OriginalPos.X;
                vertices[b + 1] = p.OriginalPos.Y;
                vertices[b + 2] = p.OriginalPos.Z;
                // surface normal / up dir
                vertices[b + 3] = p.UpVec.X;
                vertices[b + 4] = p.UpVec.Y;
                vertices[b + 5] = p.UpVec.Z;
                // coord across the rail width. uses wave1 phase
                vertices[b + 6] = p.CoordAcrossRail;
                // distance along said rail
                vertices[b + 7] = p.CoordOnRail;
                // damping factor
                vertices[b + 8] = p.Taper;
                // textureU
                vertices[b + 9] = row * 0.05f;
                //textureV
                vertices[b + 10] = col * 0.05f;
            }
        }

        int rowPairCount = sim.Closed ? sim.SegmentCount : sim.SegmentCount - 1;
        var indices = new uint[Math.Max(rowPairCount, 0) * (stride - 1) * 6];
        int ii = 0;
        for (int row = 0; row < rowPairCount; row++)
        {
            int nextRow = (row + 1) % sim.SegmentCount;
            for (int col = 0; col < stride - 1; col++)
            {
                // near left
                uint a = (uint)(row * stride + col);
                // near right
                uint b = (uint)(row * stride + col + 1);
                // far left
                uint c = (uint)(nextRow * stride + col);
                // far right
                uint d = (uint)(nextRow * stride + col + 1);
                indices[ii++] = a;
                indices[ii++] = c;
                indices[ii++] = b;
                indices[ii++] = b;
                indices[ii++] = c;
                indices[ii++] = d;
            }
        }

        uint vao = _gl.GenVertexArray();
        _gl.BindVertexArray(vao);

        uint vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        unsafe
        {
            fixed (float* ptr = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        const uint vertexStrideBytes = 11 * sizeof(float);
        unsafe
        {
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, vertexStrideBytes, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, vertexStrideBytes, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, vertexStrideBytes, (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, vertexStrideBytes, (void*)(7 * sizeof(float)));
            _gl.EnableVertexAttribArray(3);
            _gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, vertexStrideBytes, (void*)(8 * sizeof(float)));
            _gl.EnableVertexAttribArray(4);
            _gl.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, false, vertexStrideBytes, (void*)(9 * sizeof(float)));
            _gl.EnableVertexAttribArray(5);
        }

        uint ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        unsafe
        {
            fixed (uint* ptr = indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        return new OceanRingGpuMesh { Vao = vao, Vbo = vbo, Ebo = ebo, IndexCount = indices.Length };
    }

    public void DeleteOceanRingMesh(OceanRingGpuMesh mesh)
    {
        _gl.DeleteVertexArray(mesh.Vao);
        _gl.DeleteBuffer(mesh.Vbo);
        _gl.DeleteBuffer(mesh.Ebo);
    }

    public void CaptureOpaqueSceneTexture(int width, int height)
    {
        if (_reflectionTexture == 0 || _reflectionTextureWidth != width || _reflectionTextureHeight != height)
        {
            if (_reflectionTexture != 0)
            {
                _gl.DeleteTexture(_reflectionTexture);
            }

            _reflectionTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _reflectionTexture);
            unsafe
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb, (uint)width, (uint)height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, null);
            }

            int clamp = (int)GLEnum.ClampToEdge;
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, in clamp);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, in clamp);
            int linear = (int)GLEnum.Linear;
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in linear);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in linear);
            _reflectionTextureWidth = width;
            _reflectionTextureHeight = height;
        }

        _gl.BindTexture(TextureTarget.Texture2D, _reflectionTexture);
        _gl.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, (uint)width, (uint)height);
    }

    public void RenderOceanRing(OceanRingGpuMesh mesh, OceanRingSimState sim, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize)
    {
        if (!_oceanTexturesLoaded || mesh.IndexCount == 0)
        {
            return;
        }

        _gl.UseProgram(_oceanRingProgram);
        unsafe
        {
            _gl.UniformMatrix4(_uOceanViewLoc, 1, false, (float*)&view);
            _gl.UniformMatrix4(_uOceanProjLoc, 1, false, (float*)&projection);
        }

        _gl.Uniform1(_uOceanWaveTheta1Loc, sim.Theta1);
        _gl.Uniform1(_uOceanWaveTheta2Loc, sim.Theta2);
        _gl.Uniform1(_uOceanWaveHeight1Loc, sim.WaveHeight1);
        _gl.Uniform1(_uOceanWaveHeight2Loc, sim.WaveHeight2);
        _gl.Uniform2(_uOceanTex0ScrollLoc, sim.Tex0Scroll.X, sim.Tex0Scroll.Y);
        _gl.Uniform2(_uOceanTex1ScrollLoc, sim.Tex1Scroll.X, sim.Tex1Scroll.Y);
        _gl.Uniform2(_uOceanTex2ScrollLoc, sim.Tex2Scroll.X, sim.Tex2Scroll.Y);
        _gl.Uniform2(_uOceanViewportSizeLoc, viewportSize.X, viewportSize.Y);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _oceanWaterTexture);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _reflectionTexture);
        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, _oceanIndirectTexture);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);

        _gl.BindVertexArray(mesh.Vao);
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, (uint)mesh.IndexCount, DrawElementsType.UnsignedInt, null);
        }

        _gl.DepthMask(true);
    }
}
