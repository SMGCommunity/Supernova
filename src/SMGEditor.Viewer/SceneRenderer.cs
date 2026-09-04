using System.Numerics;
using SMGEditor.Core.Formats;
using SMGEditor.Core.Simulation;
using Silk.NET.OpenGL;

namespace SMGEditor.Viewer;

public sealed partial class SceneRenderer
{
    private const int MaxTevStages = 4;

    public const float PlaceholderBoxHalfExtent = 50f;

    private const string VertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUV0;
        layout(location = 3) in vec2 aUV1;
        layout(location = 4) in vec4 aColor;
        layout(location = 5) in vec2 aUV2;
        layout(location = 6) in vec2 aUV3;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;
        uniform bool uUv0Generated;
        uniform bool uUv1Generated;
        uniform bool uUv2Generated;
        uniform bool uUv3Generated;
        uniform mat4 uEnvMapMatrix0;
        uniform mat4 uEnvMapMatrix1;
        uniform mat4 uEnvMapMatrix2;
        uniform mat4 uEnvMapMatrix3;

        out vec3 vNormal;
        out vec3 vViewPos;
        out vec3 vViewNormal;
        out vec2 vUV0;
        out vec2 vUV1;
        out vec4 vColor;
        out vec2 vUV2;
        out vec2 vUV3;

        void main()
        {
            gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
            vNormal = mat3(uModel) * aNormal;
            vec4 viewPos = uView * uModel * vec4(aPos, 1.0);
            vViewPos = viewPos.xyz;
            vViewNormal = mat3(uView * uModel) * aNormal;
            vUV0 = uUv0Generated ? (uEnvMapMatrix0 * vec4(aNormal, 1.0)).xy : aUV0;
            vUV1 = uUv1Generated ? (uEnvMapMatrix1 * vec4(aNormal, 1.0)).xy : aUV1;
            vUV2 = uUv2Generated ? (uEnvMapMatrix2 * vec4(aNormal, 1.0)).xy : aUV2;
            vUV3 = uUv3Generated ? (uEnvMapMatrix3 * vec4(aNormal, 1.0)).xy : aUV3;
            vColor = aColor;
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec3 vNormal;
        in vec3 vViewPos;
        in vec3 vViewNormal;
        in vec2 vUV0;
        in vec2 vUV1;
        in vec4 vColor;
        in vec2 vUV2;
        in vec2 vUV3;
        out vec4 FragColor;

        uniform bool uUseLightData;
        uniform vec3 uLd0Pos;
        uniform vec3 uLd0Color;
        uniform vec3 uLd1Pos;
        uniform vec3 uLd1Color;
        uniform vec3 uLdAmbient;
        uniform int uLitMask;
        uniform int uDiffuseFn;

        uniform sampler2D uTexture0;
        uniform sampler2D uTexture1;
        uniform sampler2D uTexture2;
        uniform sampler2D uTexture3;
        uniform sampler2D uTexture4;
        uniform bool uIndirectEnabled;
        uniform int uIndAffectsStage;
        uniform int uIndBiasSel;
        uniform vec3 uIndMtxRowS;
        uniform vec3 uIndMtxRowT;
        uniform vec2 uIndCoordScale;

        uniform bool uAlphaTest;
        uniform float uAlphaRef;
        uniform vec4 uMaterialColor;
        uniform bool uUseVertexColor;
        uniform bool uLightingEnabled;

        uniform bool uForceOpaqueAlpha;

        uniform int uStageCount;
        uniform int uStageColorInA[4];
        uniform int uStageColorInB[4];
        uniform int uStageColorInC[4];
        uniform int uStageColorInD[4];
        uniform int uStageColorOp[4];
        uniform int uStageColorBias[4];
        uniform int uStageColorScale[4];
        uniform bool uStageColorClamp[4];
        uniform int uStageColorOutReg[4];
        uniform int uStageAlphaInA[4];
        uniform int uStageAlphaInB[4];
        uniform int uStageAlphaInC[4];
        uniform int uStageAlphaInD[4];
        uniform int uStageAlphaOp[4];
        uniform int uStageAlphaBias[4];
        uniform int uStageAlphaScale[4];
        uniform bool uStageAlphaClamp[4];
        uniform int uStageAlphaOutReg[4];
        uniform int uStageKonstColorSel[4];
        uniform int uStageKonstAlphaSel[4];
        uniform int uStageTexUnit[4];
        uniform bool uStageHasRas[4];

        uniform vec4 uTevReg0;
        uniform vec4 uTevReg1;
        uniform vec4 uTevReg2;
        uniform vec4 uTevReg3;
        uniform vec4 uKonst0;
        uniform vec4 uKonst1;
        uniform vec4 uKonst2;
        uniform vec4 uKonst3;

        vec4 konstAt(int i)
        {
            if (i == 0) return uKonst0;
            if (i == 1) return uKonst1;
            if (i == 2) return uKonst2;
            return uKonst3;
        }

        vec3 resolveKonstColor(int sel)
        {
            if (sel == 0x00) return vec3(1.0);
            if (sel == 0x01) return vec3(7.0 / 8.0);
            if (sel == 0x02) return vec3(3.0 / 4.0);
            if (sel == 0x03) return vec3(5.0 / 8.0);
            if (sel == 0x04) return vec3(1.0 / 2.0);
            if (sel == 0x05) return vec3(3.0 / 8.0);
            if (sel == 0x06) return vec3(1.0 / 4.0);
            if (sel == 0x07) return vec3(1.0 / 8.0);
            if (sel >= 0x0C && sel <= 0x0F) return konstAt(sel - 0x0C).rgb;
            if (sel >= 0x10 && sel <= 0x13) return vec3(konstAt(sel - 0x10).r);
            if (sel >= 0x14 && sel <= 0x17) return vec3(konstAt(sel - 0x14).g);
            if (sel >= 0x18 && sel <= 0x1B) return vec3(konstAt(sel - 0x18).b);
            if (sel >= 0x1C && sel <= 0x1F) return vec3(konstAt(sel - 0x1C).a);
            return vec3(1.0);
        }

        float resolveKonstAlpha(int sel)
        {
            if (sel == 0x00) return 1.0;
            if (sel == 0x01) return 7.0 / 8.0;
            if (sel == 0x02) return 3.0 / 4.0;
            if (sel == 0x03) return 5.0 / 8.0;
            if (sel == 0x04) return 1.0 / 2.0;
            if (sel == 0x05) return 3.0 / 8.0;
            if (sel == 0x06) return 1.0 / 4.0;
            if (sel == 0x07) return 1.0 / 8.0;
            if (sel >= 0x10 && sel <= 0x13) return konstAt(sel - 0x10).r;
            if (sel >= 0x14 && sel <= 0x17) return konstAt(sel - 0x14).g;
            if (sel >= 0x18 && sel <= 0x1B) return konstAt(sel - 0x18).b;
            if (sel >= 0x1C && sel <= 0x1F) return konstAt(sel - 0x1C).a;
            return 1.0;
        }

        vec3 resolveColorInput(int sel, vec4 reg[4], vec3 texColor, float texAlpha, vec3 ras, float rasAlpha, vec3 konst)
        {
            if (sel == 0) return reg[0].rgb;
            if (sel == 1) return vec3(reg[0].a);
            if (sel == 2) return reg[1].rgb;
            if (sel == 3) return vec3(reg[1].a);
            if (sel == 4) return reg[2].rgb;
            if (sel == 5) return vec3(reg[2].a);
            if (sel == 6) return reg[3].rgb;
            if (sel == 7) return vec3(reg[3].a);
            if (sel == 8) return texColor;
            if (sel == 9) return vec3(texAlpha);
            if (sel == 10) return ras;
            if (sel == 11) return vec3(rasAlpha);
            if (sel == 12) return vec3(1.0);
            if (sel == 13) return vec3(0.5);
            if (sel == 14) return konst;
            return vec3(0.0);
        }

        float resolveAlphaInput(int sel, vec4 reg[4], float texAlpha, float rasAlpha, float konstAlpha)
        {
            if (sel == 0) return reg[0].a;
            if (sel == 1) return reg[1].a;
            if (sel == 2) return reg[2].a;
            if (sel == 3) return reg[3].a;
            if (sel == 4) return texAlpha;
            if (sel == 5) return rasAlpha;
            if (sel == 6) return konstAlpha;
            return 0.0;
        }

        float tevScaleFactor(int scale)
        {
            if (scale == 1) return 2.0;
            if (scale == 2) return 4.0;
            if (scale == 3) return 0.5;
            return 1.0;
        }

        vec3 tevCompareColor(vec3 a, vec3 b, vec3 c, vec3 d, int op)
        {
            ivec3 ai = ivec3(round(a * 255.0));
            ivec3 bi = ivec3(round(b * 255.0));
            bool passed;
            if (op == 8) passed = ai.r > bi.r;
            else if (op == 9) passed = ai.r == bi.r;
            else if (op == 10 || op == 11) 
            {
                int av = ai.g * 256 + ai.r;
                int bv = bi.g * 256 + bi.r;
                passed = (op == 10) ? (av > bv) : (av == bv);
            }
            else if (op == 12 || op == 13) 
            {
                int av = ai.b * 65536 + ai.g * 256 + ai.r;
                int bv = bi.b * 65536 + bi.g * 256 + bi.r;
                passed = (op == 12) ? (av > bv) : (av == bv);
            }
            else
            {
                bvec3 cmp = (op == 14) ? greaterThan(ai, bi) : equal(ai, bi);
                return d + c * vec3(cmp);
            }

            return d + (passed ? c : vec3(0.0));
        }

        float tevCompareAlpha(float a, float b, float c, float d, int op)
        {
            int ai = int(round(a * 255.0));
            int bi = int(round(b * 255.0));
            bool passed = (op == 9) ? (ai == bi) : (ai > bi);
            return d + (passed ? c : 0.0);
        }

        vec3 combineColor(vec3 a, vec3 b, vec3 c, vec3 d, int op, int bias, int scale, bool doClamp)
        {
            if (op >= 8) return doClamp ? clamp(tevCompareColor(a, b, c, d, op), 0.0, 1.0) : tevCompareColor(a, b, c, d, op);

            vec3 lerped = mix(a, b, c);
            vec3 result = (op == 1) ? (d - lerped) : (d + lerped);
            if (bias == 1) result += 0.5;
            else if (bias == 2) result -= 0.5;
            result *= tevScaleFactor(scale);
            return doClamp ? clamp(result, 0.0, 1.0) : result;
        }

        float combineAlpha(float a, float b, float c, float d, int op, int bias, int scale, bool doClamp)
        {
            if (op >= 8) return doClamp ? clamp(tevCompareAlpha(a, b, c, d, op), 0.0, 1.0) : tevCompareAlpha(a, b, c, d, op);

            float lerped = mix(a, b, c);
            float result = (op == 1) ? (d - lerped) : (d + lerped);
            if (bias == 1) result += 0.5;
            else if (bias == 2) result -= 0.5;
            result *= tevScaleFactor(scale);
            return doClamp ? clamp(result, 0.0, 1.0) : result;
        }

        void main()
        {
            vec4 baseRas = uUseVertexColor ? vColor : uMaterialColor;

            vec3 rasColor;
            float rasAlpha = baseRas.a;
            if (uLightingEnabled && uUseLightData)
            {
                vec3 nv = normalize(vViewNormal);
                vec3 illum = uLdAmbient;
                if ((uLitMask & 1) != 0)
                {
                    float d = dot(nv, normalize(uLd0Pos - vViewPos));
                    d = uDiffuseFn == 2 ? max(d, 0.0) : (uDiffuseFn == 1 ? d : 1.0);
                    illum += d * uLd0Color;
                }
                if ((uLitMask & 2) != 0)
                {
                    float d = dot(nv, normalize(uLd1Pos - vViewPos));
                    d = uDiffuseFn == 2 ? max(d, 0.0) : (uDiffuseFn == 1 ? d : 1.0);
                    illum += d * uLd1Color;
                }
                rasColor = baseRas.rgb * clamp(illum, 0.0, 1.0);
            }
            else
            {
                vec3 n = normalize(vNormal);
                vec3 lightDir = normalize(vec3(0.4, 0.8, 0.6));
                float diff = max(dot(n, lightDir), 0.0);
                float lighting = uLightingEnabled ? (0.5 + 0.5 * diff) : 1.0;
                rasColor = baseRas.rgb * lighting;
            }

            vec4 reg[4];
            reg[0] = uTevReg0;
            reg[1] = uTevReg1;
            reg[2] = uTevReg2;
            reg[3] = uTevReg3;

            vec2 indOffset = vec2(0.0);
            if (uIndirectEnabled)
            {
                vec3 indSample = texture(uTexture2, vUV1).rgb;
                if (uIndBiasSel == 1 || uIndBiasSel == 3 || uIndBiasSel == 5 || uIndBiasSel == 7) indSample.x -= 0.5;
                if (uIndBiasSel == 2 || uIndBiasSel == 3 || uIndBiasSel == 6 || uIndBiasSel == 7) indSample.y -= 0.5;
                if (uIndBiasSel == 4 || uIndBiasSel == 5 || uIndBiasSel == 6 || uIndBiasSel == 7) indSample.z -= 0.5;
                indOffset = vec2(dot(uIndMtxRowS, indSample), dot(uIndMtxRowT, indSample));
            }

            for (int s = 0; s < uStageCount && s < 4; s++)
            {
                vec2 uv0 = vUV0;
                vec2 uv1 = vUV1;
                if (uIndirectEnabled && s == uIndAffectsStage)
                {
                    if (uStageTexUnit[s] == 0) uv0 = uv0 * uIndCoordScale + indOffset;
                    else uv1 = uv1 * uIndCoordScale + indOffset;
                }

                vec4 texSample = vec4(1.0);
                if (uStageTexUnit[s] == 0) texSample = texture(uTexture0, uv0);
                else if (uStageTexUnit[s] == 1) texSample = texture(uTexture1, uv1);
                else if (uStageTexUnit[s] == 2) texSample = texture(uTexture3, vUV2);
                else if (uStageTexUnit[s] == 3) texSample = texture(uTexture4, vUV3);

                vec3 ras = uStageHasRas[s] ? rasColor : vec3(0.0);
                float ra = uStageHasRas[s] ? rasAlpha : 0.0;

                vec3 konstColor = resolveKonstColor(uStageKonstColorSel[s]);
                float konstAlpha = resolveKonstAlpha(uStageKonstAlphaSel[s]);

                vec3 cA = resolveColorInput(uStageColorInA[s], reg, texSample.rgb, texSample.a, ras, ra, konstColor);
                vec3 cB = resolveColorInput(uStageColorInB[s], reg, texSample.rgb, texSample.a, ras, ra, konstColor);
                vec3 cC = resolveColorInput(uStageColorInC[s], reg, texSample.rgb, texSample.a, ras, ra, konstColor);
                vec3 cD = resolveColorInput(uStageColorInD[s], reg, texSample.rgb, texSample.a, ras, ra, konstColor);
                vec3 colorResult = combineColor(cA, cB, cC, cD, uStageColorOp[s], uStageColorBias[s], uStageColorScale[s], uStageColorClamp[s]);

                float aA = resolveAlphaInput(uStageAlphaInA[s], reg, texSample.a, ra, konstAlpha);
                float aB = resolveAlphaInput(uStageAlphaInB[s], reg, texSample.a, ra, konstAlpha);
                float aC = resolveAlphaInput(uStageAlphaInC[s], reg, texSample.a, ra, konstAlpha);
                float aD = resolveAlphaInput(uStageAlphaInD[s], reg, texSample.a, ra, konstAlpha);
                float alphaResult = combineAlpha(aA, aB, aC, aD, uStageAlphaOp[s], uStageAlphaBias[s], uStageAlphaScale[s], uStageAlphaClamp[s]);

                int cOut = uStageColorOutReg[s];
                reg[cOut] = vec4(colorResult, reg[cOut].a);
                int aOut = uStageAlphaOutReg[s];
                reg[aOut] = vec4(reg[aOut].rgb, alphaResult);
            }

            vec4 color = uStageCount > 0 ? reg[0] : baseRas;

            if (uAlphaTest && color.a < uAlphaRef)
                discard;

            FragColor = vec4(color.rgb, uForceOpaqueAlpha ? 1.0 : color.a);
        }
        """;

    private const string GizmoVertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aColor;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 vColor;

        void main()
        {
            gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
            vColor = aColor;
        }
        """;

    private const string GizmoFragmentShaderSource = """
        #version 330 core
        in vec3 vColor;
        out vec4 FragColor;

        uniform bool uUseOverrideColor;
        uniform vec3 uOverrideColor;

        void main()
        {
            FragColor = vec4(uUseOverrideColor ? uOverrideColor : vColor, 1.0);
        }
        """;

    private const string ThickLineVertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec3 aPos;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        void main()
        {
            gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
        }
        """;

    private const string ThickLineGeometryShaderSource = """
        #version 330 core
        layout(lines) in;
        layout(triangle_strip, max_vertices = 4) out;

        uniform vec2 uViewportSize;
        uniform float uLineWidthPixels;

        void main()
        {
            vec4 p0 = gl_in[0].gl_Position;
            vec4 p1 = gl_in[1].gl_Position;

            vec2 ndc0 = p0.xy / p0.w;
            vec2 ndc1 = p1.xy / p1.w;

            vec2 dir = ndc1 - ndc0;
            float len = length(dir);
            dir = len < 1e-9 ? vec2(1.0, 0.0) : dir / len;

            vec2 normal = vec2(-dir.y, dir.x);
            vec2 halfOffsetNdc = normal * uLineWidthPixels * 0.5 * (2.0 / uViewportSize);

            vec4 offset0 = vec4(halfOffsetNdc * p0.w, 0.0, 0.0);
            vec4 offset1 = vec4(halfOffsetNdc * p1.w, 0.0, 0.0);

            gl_Position = p0 + offset0; EmitVertex();
            gl_Position = p0 - offset0; EmitVertex();
            gl_Position = p1 + offset1; EmitVertex();
            gl_Position = p1 - offset1; EmitVertex();
            EndPrimitive();
        }
        """;

    private const string ThickLineFragmentShaderSource = """
        #version 330 core
        out vec4 FragColor;

        uniform vec3 uOverrideColor;

        void main()
        {
            FragColor = vec4(uOverrideColor, 1.0);
        }
        """;

    private readonly GL _gl;
    private readonly uint _program;
    private readonly int _uModelLoc, _uViewLoc, _uProjLoc;
    private readonly int _uUv0GeneratedLoc, _uUv1GeneratedLoc, _uEnvMapMatrix0Loc, _uEnvMapMatrix1Loc;
    private readonly int _uUv2GeneratedLoc, _uUv3GeneratedLoc, _uEnvMapMatrix2Loc, _uEnvMapMatrix3Loc;
    private readonly int _uAlphaTestLoc, _uAlphaRefLoc;
    private readonly int _uForceOpaqueAlphaLoc;
    private readonly int _uIndirectEnabledLoc, _uIndAffectsStageLoc, _uIndBiasSelLoc, _uIndMtxRowSLoc, _uIndMtxRowTLoc, _uIndCoordScaleLoc;
    private readonly int _uMaterialColorLoc, _uUseVertexColorLoc, _uLightingEnabledLoc;
    private readonly int _uUseLightDataLoc, _uLd0PosLoc, _uLd0ColorLoc, _uLd1PosLoc, _uLd1ColorLoc, _uLdAmbientLoc, _uLitMaskLoc, _uDiffuseFnLoc;
    private bool _lightDataEnabled;
    private readonly int _uStageCountLoc;
    private readonly int _uTevReg0Loc, _uTevReg1Loc, _uTevReg2Loc, _uTevReg3Loc;
    private readonly int _uKonst0Loc, _uKonst1Loc, _uKonst2Loc, _uKonst3Loc;
    private readonly StageUniformLocations[] _stageLocs = new StageUniformLocations[MaxTevStages];

    private readonly uint _gizmoProgram;
    private readonly int _uGizmoModelLoc, _uGizmoViewLoc, _uGizmoProjLoc;
    private readonly int _uGizmoUseOverrideLoc, _uGizmoOverrideColorLoc;

    private readonly uint _thickLineProgram;
    private readonly int _uThickModelLoc, _uThickViewLoc, _uThickProjLoc;
    private readonly int _uThickColorLoc, _uThickViewportSizeLoc, _uThickLineWidthLoc;

    private readonly uint _gizmoTriVao;
    private readonly int _gizmoTriVertexCount;
    private readonly uint _gizmoLineVao;
    private readonly int _gizmoLineVertexCount;

    private readonly uint _arrowShaftVao;
    private readonly int _arrowShaftVertexCount;
    private readonly uint _arrowConeVao;
    private readonly int _arrowConeVertexCount;
    private readonly uint _ringLineVao;
    private readonly int _ringLineVertexCount;
    private readonly uint _boxOutlineLineVao;
    private readonly int _boxOutlineLineVertexCount;

    private readonly uint _areaBaseBoxVao;
    private readonly int _areaBaseBoxVertexCount;
    private readonly uint _areaCenterBoxVao;
    private readonly int _areaCenterBoxVertexCount;
    private readonly uint _areaSphereVao;
    private readonly int _areaSphereVertexCount;
    private readonly uint _areaCylinderVao;
    private readonly int _areaCylinderVertexCount;
    private readonly uint _areaBowlVao;
    private readonly int _areaBowlVertexCount;

    private readonly uint _pathLineVao;
    private readonly uint _pathLineVbo;
    private int _pathLineCapacityVerts;

    private readonly struct StageUniformLocations
    {
        public required int ColorInA { get; init; }
        public required int ColorInB { get; init; }
        public required int ColorInC { get; init; }
        public required int ColorInD { get; init; }
        public required int ColorOp { get; init; }
        public required int ColorBias { get; init; }
        public required int ColorScale { get; init; }
        public required int ColorClamp { get; init; }
        public required int ColorOutReg { get; init; }
        public required int AlphaInA { get; init; }
        public required int AlphaInB { get; init; }
        public required int AlphaInC { get; init; }
        public required int AlphaInD { get; init; }
        public required int AlphaOp { get; init; }
        public required int AlphaBias { get; init; }
        public required int AlphaScale { get; init; }
        public required int AlphaClamp { get; init; }
        public required int AlphaOutReg { get; init; }
        public required int KonstColorSel { get; init; }
        public required int KonstAlphaSel { get; init; }
        public required int TexUnit { get; init; }
        public required int HasRas { get; init; }
    }

    public SceneRenderer(GL gl)
    {
        _gl = gl;

        gl.FrontFace(FrontFaceDirection.CW);

        _program = CreateProgram(gl, VertexShaderSource, FragmentShaderSource);

        _uModelLoc = gl.GetUniformLocation(_program, "uModel");
        _uViewLoc = gl.GetUniformLocation(_program, "uView");
        _uProjLoc = gl.GetUniformLocation(_program, "uProjection");
        _uUv0GeneratedLoc = gl.GetUniformLocation(_program, "uUv0Generated");
        _uUv1GeneratedLoc = gl.GetUniformLocation(_program, "uUv1Generated");
        _uEnvMapMatrix0Loc = gl.GetUniformLocation(_program, "uEnvMapMatrix0");
        _uEnvMapMatrix1Loc = gl.GetUniformLocation(_program, "uEnvMapMatrix1");
        _uUv2GeneratedLoc = gl.GetUniformLocation(_program, "uUv2Generated");
        _uUv3GeneratedLoc = gl.GetUniformLocation(_program, "uUv3Generated");
        _uEnvMapMatrix2Loc = gl.GetUniformLocation(_program, "uEnvMapMatrix2");
        _uEnvMapMatrix3Loc = gl.GetUniformLocation(_program, "uEnvMapMatrix3");
        _uAlphaTestLoc = gl.GetUniformLocation(_program, "uAlphaTest");
        _uAlphaRefLoc = gl.GetUniformLocation(_program, "uAlphaRef");
        _uForceOpaqueAlphaLoc = gl.GetUniformLocation(_program, "uForceOpaqueAlpha");
        _uIndirectEnabledLoc = gl.GetUniformLocation(_program, "uIndirectEnabled");
        _uIndAffectsStageLoc = gl.GetUniformLocation(_program, "uIndAffectsStage");
        _uIndBiasSelLoc = gl.GetUniformLocation(_program, "uIndBiasSel");
        _uIndMtxRowSLoc = gl.GetUniformLocation(_program, "uIndMtxRowS");
        _uIndMtxRowTLoc = gl.GetUniformLocation(_program, "uIndMtxRowT");
        _uIndCoordScaleLoc = gl.GetUniformLocation(_program, "uIndCoordScale");
        _uMaterialColorLoc = gl.GetUniformLocation(_program, "uMaterialColor");
        _uUseVertexColorLoc = gl.GetUniformLocation(_program, "uUseVertexColor");
        _uLightingEnabledLoc = gl.GetUniformLocation(_program, "uLightingEnabled");
        _uUseLightDataLoc = gl.GetUniformLocation(_program, "uUseLightData");
        _uLd0PosLoc = gl.GetUniformLocation(_program, "uLd0Pos");
        _uLd0ColorLoc = gl.GetUniformLocation(_program, "uLd0Color");
        _uLd1PosLoc = gl.GetUniformLocation(_program, "uLd1Pos");
        _uLd1ColorLoc = gl.GetUniformLocation(_program, "uLd1Color");
        _uLdAmbientLoc = gl.GetUniformLocation(_program, "uLdAmbient");
        _uLitMaskLoc = gl.GetUniformLocation(_program, "uLitMask");
        _uDiffuseFnLoc = gl.GetUniformLocation(_program, "uDiffuseFn");
        _uStageCountLoc = gl.GetUniformLocation(_program, "uStageCount");
        _uTevReg0Loc = gl.GetUniformLocation(_program, "uTevReg0");
        _uTevReg1Loc = gl.GetUniformLocation(_program, "uTevReg1");
        _uTevReg2Loc = gl.GetUniformLocation(_program, "uTevReg2");
        _uTevReg3Loc = gl.GetUniformLocation(_program, "uTevReg3");
        _uKonst0Loc = gl.GetUniformLocation(_program, "uKonst0");
        _uKonst1Loc = gl.GetUniformLocation(_program, "uKonst1");
        _uKonst2Loc = gl.GetUniformLocation(_program, "uKonst2");
        _uKonst3Loc = gl.GetUniformLocation(_program, "uKonst3");

        for (int i = 0; i < MaxTevStages; i++)
        {
            _stageLocs[i] = new StageUniformLocations
            {
                ColorInA = gl.GetUniformLocation(_program, $"uStageColorInA[{i}]"),
                ColorInB = gl.GetUniformLocation(_program, $"uStageColorInB[{i}]"),
                ColorInC = gl.GetUniformLocation(_program, $"uStageColorInC[{i}]"),
                ColorInD = gl.GetUniformLocation(_program, $"uStageColorInD[{i}]"),
                ColorOp = gl.GetUniformLocation(_program, $"uStageColorOp[{i}]"),
                ColorBias = gl.GetUniformLocation(_program, $"uStageColorBias[{i}]"),
                ColorScale = gl.GetUniformLocation(_program, $"uStageColorScale[{i}]"),
                ColorClamp = gl.GetUniformLocation(_program, $"uStageColorClamp[{i}]"),
                ColorOutReg = gl.GetUniformLocation(_program, $"uStageColorOutReg[{i}]"),
                AlphaInA = gl.GetUniformLocation(_program, $"uStageAlphaInA[{i}]"),
                AlphaInB = gl.GetUniformLocation(_program, $"uStageAlphaInB[{i}]"),
                AlphaInC = gl.GetUniformLocation(_program, $"uStageAlphaInC[{i}]"),
                AlphaInD = gl.GetUniformLocation(_program, $"uStageAlphaInD[{i}]"),
                AlphaOp = gl.GetUniformLocation(_program, $"uStageAlphaOp[{i}]"),
                AlphaBias = gl.GetUniformLocation(_program, $"uStageAlphaBias[{i}]"),
                AlphaScale = gl.GetUniformLocation(_program, $"uStageAlphaScale[{i}]"),
                AlphaClamp = gl.GetUniformLocation(_program, $"uStageAlphaClamp[{i}]"),
                AlphaOutReg = gl.GetUniformLocation(_program, $"uStageAlphaOutReg[{i}]"),
                KonstColorSel = gl.GetUniformLocation(_program, $"uStageKonstColorSel[{i}]"),
                KonstAlphaSel = gl.GetUniformLocation(_program, $"uStageKonstAlphaSel[{i}]"),
                TexUnit = gl.GetUniformLocation(_program, $"uStageTexUnit[{i}]"),
                HasRas = gl.GetUniformLocation(_program, $"uStageHasRas[{i}]"),
            };
        }

        gl.UseProgram(_program);
        gl.Uniform1(gl.GetUniformLocation(_program, "uTexture0"), 0);
        gl.Uniform1(gl.GetUniformLocation(_program, "uTexture1"), 1);
        gl.Uniform1(gl.GetUniformLocation(_program, "uTexture2"), 2);
        gl.Uniform1(gl.GetUniformLocation(_program, "uTexture3"), 3);
        gl.Uniform1(gl.GetUniformLocation(_program, "uTexture4"), 4);

        _gizmoProgram = CreateProgram(gl, GizmoVertexShaderSource, GizmoFragmentShaderSource);
        _uGizmoModelLoc = gl.GetUniformLocation(_gizmoProgram, "uModel");
        _uGizmoViewLoc = gl.GetUniformLocation(_gizmoProgram, "uView");
        _uGizmoProjLoc = gl.GetUniformLocation(_gizmoProgram, "uProjection");
        _uGizmoUseOverrideLoc = gl.GetUniformLocation(_gizmoProgram, "uUseOverrideColor");
        _uGizmoOverrideColorLoc = gl.GetUniformLocation(_gizmoProgram, "uOverrideColor");

        _thickLineProgram = CreateProgram(gl, ThickLineVertexShaderSource, ThickLineFragmentShaderSource, ThickLineGeometryShaderSource);
        _uThickModelLoc = gl.GetUniformLocation(_thickLineProgram, "uModel");
        _uThickViewLoc = gl.GetUniformLocation(_thickLineProgram, "uView");
        _uThickProjLoc = gl.GetUniformLocation(_thickLineProgram, "uProjection");
        _uThickColorLoc = gl.GetUniformLocation(_thickLineProgram, "uOverrideColor");
        _uThickViewportSizeLoc = gl.GetUniformLocation(_thickLineProgram, "uViewportSize");
        _uThickLineWidthLoc = gl.GetUniformLocation(_thickLineProgram, "uLineWidthPixels");

        InitializeOceanRing(gl);

        (_gizmoTriVao, _gizmoTriVertexCount, _gizmoLineVao, _gizmoLineVertexCount) = BuildGizmoGeometry(gl);
        (_arrowShaftVao, _arrowShaftVertexCount) = BuildArrowShaftGeometry(gl);
        (_arrowConeVao, _arrowConeVertexCount) = BuildArrowConeGeometry(gl);
        (_ringLineVao, _ringLineVertexCount) = BuildRingGeometry(gl);
        (_boxOutlineLineVao, _boxOutlineLineVertexCount) = BuildBoxOutlineGeometry(gl);
        (_areaBaseBoxVao, _areaBaseBoxVertexCount) = BuildAreaBoxGeometry(gl, 500f);
        (_areaCenterBoxVao, _areaCenterBoxVertexCount) = BuildAreaBoxGeometry(gl, 0f);
        (_areaSphereVao, _areaSphereVertexCount) = BuildAreaSphereGeometry(gl, -1f, 1f);
        (_areaCylinderVao, _areaCylinderVertexCount) = BuildAreaCylinderGeometry(gl);
        (_areaBowlVao, _areaBowlVertexCount) = BuildAreaSphereGeometry(gl, -1f, 0f);

        _pathLineVao = gl.GenVertexArray();
        gl.BindVertexArray(_pathLineVao);
        _pathLineVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _pathLineVbo);
        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        }

        gl.EnableVertexAttribArray(0);
    }

    public void UploadObject(LoadedObject obj)
    {
        for (int i = 0; i < obj.Model.Textures.Count; i++)
        {
            obj.TextureHandles[i] = CreateTexture(_gl, obj.Model.Textures[i]);
        }

        foreach (GpuMesh mesh in obj.Meshes)
        {
            obj.RenderMeshes.Add(UploadMesh(_gl, mesh));
        }
    }

    public RenderMesh UploadMeshOnly(GpuMesh mesh) => UploadMesh(_gl, mesh);

    public void UpdateMeshVertices(RenderMesh rm, float[] vertexData)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, rm.Vbo);
        unsafe
        {
            fixed (float* ptr = vertexData)
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertexData.Length * sizeof(float)), ptr);
            }
        }
    }

    public void SetPreviewLighting(bool enabled, PreviewLightGroup[] groups, int forceGroup)
    {
        _lightDataEnabled = enabled && groups.Length == 4;
        if (_lightDataEnabled)
        {
            _lightGroups = groups;
        }

        _forceGroup = forceGroup;
    }

    private PreviewLightGroup[] _lightGroups = new PreviewLightGroup[4];
    private int _forceGroup = -1;
    private readonly (Vector3 L0, Vector3 L1)[] _lightViewPos = new (Vector3, Vector3)[4];
    private int _lastUploadedGroup;

    public void Render(IEnumerable<ObjectInstance> instances, Matrix4x4 view, Matrix4x4 projection)
    {
        _gl.UseProgram(_program);

        unsafe
        {
            _gl.UniformMatrix4(_uViewLoc, 1, false, (float*)&view);
            _gl.UniformMatrix4(_uProjLoc, 1, false, (float*)&projection);
        }

        _gl.Uniform1(_uUseLightDataLoc, _lightDataEnabled ? 1 : 0);
        if (_lightDataEnabled)
        {
            for (int g = 0; g < 4; g++)
            {
                PreviewLight a = _lightGroups[g].Light0;
                PreviewLight b = _lightGroups[g].Light1;
                _lightViewPos[g] = (
                    a.FollowCamera ? a.Position : Vector3.Transform(a.Position, view),
                    b.FollowCamera ? b.Position : Vector3.Transform(b.Position, view));
            }

            _lastUploadedGroup = -1;
        }

        List<ObjectInstance> instanceList = instances as List<ObjectInstance> ?? instances.ToList();

        DrawInstances(instanceList, view, wantTranslucent: false);
        DrawInstances(instanceList, view, wantTranslucent: true);
    }

    private void UploadLightGroup(int g)
    {
        if (g == _lastUploadedGroup)
        {
            return;
        }

        _lastUploadedGroup = g;
        (Vector3 l0, Vector3 l1) = _lightViewPos[g];
        PreviewLightGroup grp = _lightGroups[g];
        _gl.Uniform3(_uLd0PosLoc, l0.X, l0.Y, l0.Z);
        _gl.Uniform3(_uLd1PosLoc, l1.X, l1.Y, l1.Z);
        _gl.Uniform3(_uLd0ColorLoc, grp.Light0.Color.X, grp.Light0.Color.Y, grp.Light0.Color.Z);
        _gl.Uniform3(_uLd1ColorLoc, grp.Light1.Color.X, grp.Light1.Color.Y, grp.Light1.Color.Z);
        _gl.Uniform3(_uLdAmbientLoc, grp.Ambient.X, grp.Ambient.Y, grp.Ambient.Z);
    }

    private void DrawInstances(List<ObjectInstance> instances, Matrix4x4 view, bool wantTranslucent)
    {
        foreach (ObjectInstance instance in instances)
        {
            LoadedObject obj = instance.Object;
            bool modelSet = false;

            if (_lightDataEnabled)
            {
                int g = _forceGroup >= 0 ? _forceGroup : Math.Clamp(instance.LightGroup, 0, 3);
                UploadLightGroup(g);
            }

            foreach (RenderMesh rm in obj.RenderMeshes)
            {
                BDLMaterial material = obj.Model.Materials[rm.MaterialIndex];
                if ((material.BlendMode.Type == BDLBlendType.Blend) != wantTranslucent)
                {
                    continue;
                }

                if (!modelSet)
                {
                    Matrix4x4 world = instance.WorldMatrix;
                    unsafe
                    {
                        _gl.UniformMatrix4(_uModelLoc, 1, false, (float*)&world);
                    }

                    modelSet = true;
                }

                DrawMesh(obj, rm, material, instance.WorldMatrix, view);
            }
        }
    }

    private void DrawMesh(LoadedObject obj, RenderMesh rm, BDLMaterial material, Matrix4x4 world, Matrix4x4 view)
    {
        ApplyMaterialState(_gl, material);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, rm.Texture0Index is ushort t0 && obj.TextureHandles.TryGetValue(t0, out uint handle0) ? handle0 : 0);

        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, rm.Texture1Index is ushort t1 && obj.TextureHandles.TryGetValue(t1, out uint handle1) ? handle1 : 0);

        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, rm.IndirectTextureIndex is ushort t2 && obj.TextureHandles.TryGetValue(t2, out uint handle2) ? handle2 : 0);

        _gl.ActiveTexture(TextureUnit.Texture3);
        _gl.BindTexture(TextureTarget.Texture2D, rm.Texture2Index is ushort t3 && obj.TextureHandles.TryGetValue(t3, out uint handle3) ? handle3 : 0);

        _gl.ActiveTexture(TextureUnit.Texture4);
        _gl.BindTexture(TextureTarget.Texture2D, rm.Texture3Index is ushort t4 && obj.TextureHandles.TryGetValue(t4, out uint handle4) ? handle4 : 0);

        bool alphaTest = material.AlphaCompare.Compare0 is BDLCompare.Greater or BDLCompare.GreaterEqual;
        _gl.Uniform1(_uAlphaTestLoc, alphaTest ? 1 : 0);
        _gl.Uniform1(_uAlphaRefLoc, material.AlphaCompare.Reference0 / 255f);
        _gl.Uniform1(_uForceOpaqueAlphaLoc, material.BlendMode.Type != BDLBlendType.Blend ? 1 : 0);

        BDLColor mc = material.MaterialColor;
        _gl.Uniform4(_uMaterialColorLoc, mc.R / 255f, mc.G / 255f, mc.B / 255f, mc.A / 255f);
        _gl.Uniform1(_uUseVertexColorLoc, material.ColorChannel0.MaterialSource == BDLColorSource.Vertex ? 1 : 0);
        _gl.Uniform1(_uLightingEnabledLoc, material.ColorChannel0.Enabled ? 1 : 0);
        _gl.Uniform1(_uLitMaskLoc, material.ColorChannel0.LitMask);
        _gl.Uniform1(_uDiffuseFnLoc, (int)material.ColorChannel0.DiffuseFn);

        ApplyTevUniforms(_gl, material, rm);
        ApplyIndirectUniforms(_gl, material, rm);
        ApplyEnvMapUniforms(_gl, rm, world, view);

        _gl.BindVertexArray(rm.Vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)rm.VertexCount);
    }

    public void RenderPlaceholder(Matrix4x4 world, Matrix4x4 view, Matrix4x4 projection, Vector3? color = null, bool depthTest = false)
    {
        _gl.UseProgram(_gizmoProgram);
        unsafe
        {
            _gl.UniformMatrix4(_uGizmoModelLoc, 1, false, (float*)&world);
            _gl.UniformMatrix4(_uGizmoViewLoc, 1, false, (float*)&view);
            _gl.UniformMatrix4(_uGizmoProjLoc, 1, false, (float*)&projection);
        }

        if (depthTest)
        {
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Lequal);
            _gl.DepthMask(true);
        }
        else
        {
            _gl.Disable(EnableCap.DepthTest);
        }
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        if (color is { } c)
        {
            _gl.Uniform1(_uGizmoUseOverrideLoc, 1);
            _gl.Uniform3(_uGizmoOverrideColorLoc, c.X, c.Y, c.Z);
        }
        else
        {
            _gl.Uniform1(_uGizmoUseOverrideLoc, 0);
        }

        _gl.BindVertexArray(_gizmoTriVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_gizmoTriVertexCount);

        _gl.Uniform1(_uGizmoUseOverrideLoc, 0);
        _gl.BindVertexArray(_gizmoLineVao);
        _gl.LineWidth(2f);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gizmoLineVertexCount);
    }

    private void BeginOverrideColorDraw(Matrix4x4 world, Vector3 color, Matrix4x4 view, Matrix4x4 projection)
    {
        _gl.UseProgram(_gizmoProgram);
        unsafe
        {
            _gl.UniformMatrix4(_uGizmoModelLoc, 1, false, (float*)&world);
            _gl.UniformMatrix4(_uGizmoViewLoc, 1, false, (float*)&view);
            _gl.UniformMatrix4(_uGizmoProjLoc, 1, false, (float*)&projection);
        }

        _gl.Uniform1(_uGizmoUseOverrideLoc, 1);
        _gl.Uniform3(_uGizmoOverrideColorLoc, color.X, color.Y, color.Z);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);
    }

    public void RenderTranslateHandle(Matrix4x4 world, Vector3 color, Matrix4x4 view, Matrix4x4 projection)
    {
        BeginOverrideColorDraw(world, color, view, projection);

        _gl.BindVertexArray(_arrowConeVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_arrowConeVertexCount);

        _gl.BindVertexArray(_arrowShaftVao);
        _gl.LineWidth(3f);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_arrowShaftVertexCount);
    }

    public void RenderRotateHandle(Matrix4x4 world, Vector3 color, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize, float lineWidthPixels = 4f)
    {
        _gl.UseProgram(_thickLineProgram);
        unsafe
        {
            _gl.UniformMatrix4(_uThickModelLoc, 1, false, (float*)&world);
            _gl.UniformMatrix4(_uThickViewLoc, 1, false, (float*)&view);
            _gl.UniformMatrix4(_uThickProjLoc, 1, false, (float*)&projection);
        }

        _gl.Uniform3(_uThickColorLoc, color.X, color.Y, color.Z);
        _gl.Uniform2(_uThickViewportSizeLoc, viewportSize.X, viewportSize.Y);
        _gl.Uniform1(_uThickLineWidthLoc, lineWidthPixels);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        _gl.BindVertexArray(_ringLineVao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_ringLineVertexCount);
    }

    public void RenderBoundsOutline(Matrix4x4 world, Vector3 color, Matrix4x4 view, Matrix4x4 projection, bool depthTest = false)
    {
        BeginOverrideColorDraw(world, color, view, projection);

        if (depthTest)
        {
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Lequal);
            _gl.DepthMask(true);
        }

        _gl.BindVertexArray(_boxOutlineLineVao);
        _gl.LineWidth(2f);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_boxOutlineLineVertexCount);
    }

    public enum AreaShapeKind
    {
        BaseOriginBox = 0,
        CenterOriginBox = 1,
        Sphere = 2,
        Cylinder = 3,
        Bowl = 4,
    }

    public void RenderAreaShape(AreaShapeKind shape, Matrix4x4 world, Vector3 color, Matrix4x4 view, Matrix4x4 projection, Vector2 viewportSize, float lineWidthPixels = 6f)
    {
        _gl.UseProgram(_thickLineProgram);
        unsafe
        {
            _gl.UniformMatrix4(_uThickModelLoc, 1, false, (float*)&world);
            _gl.UniformMatrix4(_uThickViewLoc, 1, false, (float*)&view);
            _gl.UniformMatrix4(_uThickProjLoc, 1, false, (float*)&projection);
        }

        _gl.Uniform3(_uThickColorLoc, color.X, color.Y, color.Z);
        _gl.Uniform2(_uThickViewportSizeLoc, viewportSize.X, viewportSize.Y);
        _gl.Uniform1(_uThickLineWidthLoc, lineWidthPixels);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        (uint vao, int count) = shape switch
        {
            AreaShapeKind.BaseOriginBox => (_areaBaseBoxVao, _areaBaseBoxVertexCount),
            AreaShapeKind.CenterOriginBox => (_areaCenterBoxVao, _areaCenterBoxVertexCount),
            AreaShapeKind.Sphere => (_areaSphereVao, _areaSphereVertexCount),
            AreaShapeKind.Cylinder => (_areaCylinderVao, _areaCylinderVertexCount),
            AreaShapeKind.Bowl => (_areaBowlVao, _areaBowlVertexCount),
            _ => (_areaBaseBoxVao, _areaBaseBoxVertexCount),
        };

        _gl.BindVertexArray(vao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)count);
    }

    public void RenderPath(IReadOnlyList<Vector3> worldPoints, Vector3 color, Matrix4x4 view, Matrix4x4 projection, float lineWidth = 2f)
    {
        if (worldPoints.Count < 2)
        {
            return;
        }

        var data = new float[worldPoints.Count * 3];
        for (int i = 0; i < worldPoints.Count; i++)
        {
            data[i * 3 + 0] = worldPoints[i].X;
            data[i * 3 + 1] = worldPoints[i].Y;
            data[i * 3 + 2] = worldPoints[i].Z;
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _pathLineVbo);
        unsafe
        {
            fixed (float* ptr = data)
            {
                if (worldPoints.Count > _pathLineCapacityVerts)
                {
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), ptr, BufferUsageARB.DynamicDraw);
                    _pathLineCapacityVerts = worldPoints.Count;
                }
                else
                {
                    _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(data.Length * sizeof(float)), ptr);
                }
            }
        }

        _gl.UseProgram(_gizmoProgram);
        unsafe
        {
            Matrix4x4 identity = Matrix4x4.Identity;
            _gl.UniformMatrix4(_uGizmoModelLoc, 1, false, (float*)&identity);
            _gl.UniformMatrix4(_uGizmoViewLoc, 1, false, (float*)&view);
            _gl.UniformMatrix4(_uGizmoProjLoc, 1, false, (float*)&projection);
        }

        _gl.Uniform1(_uGizmoUseOverrideLoc, 1);
        _gl.Uniform3(_uGizmoOverrideColorLoc, color.X, color.Y, color.Z);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        _gl.BindVertexArray(_pathLineVao);
        _gl.LineWidth(lineWidth);
        _gl.DrawArrays(PrimitiveType.LineStrip, 0, (uint)worldPoints.Count);
    }

    public void DeleteRenderMesh(RenderMesh rm)
    {
        _gl.DeleteVertexArray(rm.Vao);
        _gl.DeleteBuffer(rm.Vbo);
    }

    private void ApplyTevUniforms(GL gl, BDLMaterial material, RenderMesh rm)
    {
        int stageCount = Math.Min(material.TevStages.Count, MaxTevStages);
        gl.Uniform1(_uStageCountLoc, stageCount);

        var white = new BDLTevRegisterColor(255, 255, 255, 255);
        SetReg(gl, _uTevReg0Loc, material.TevRegisters.Count > 3 ? material.TevRegisters[3] : white);
        SetReg(gl, _uTevReg1Loc, material.TevRegisters.Count > 0 ? material.TevRegisters[0] : white);
        SetReg(gl, _uTevReg2Loc, material.TevRegisters.Count > 1 ? material.TevRegisters[1] : white);
        SetReg(gl, _uTevReg3Loc, material.TevRegisters.Count > 2 ? material.TevRegisters[2] : white);

        SetColor(gl, _uKonst0Loc, material.TevKonstColors.Count > 0 ? material.TevKonstColors[0] : new BDLColor(255, 255, 255, 255));
        SetColor(gl, _uKonst1Loc, material.TevKonstColors.Count > 1 ? material.TevKonstColors[1] : new BDLColor(255, 255, 255, 255));
        SetColor(gl, _uKonst2Loc, material.TevKonstColors.Count > 2 ? material.TevKonstColors[2] : new BDLColor(255, 255, 255, 255));
        SetColor(gl, _uKonst3Loc, material.TevKonstColors.Count > 3 ? material.TevKonstColors[3] : new BDLColor(255, 255, 255, 255));

        for (int i = 0; i < stageCount; i++)
        {
            BDLTevStage stage = material.TevStages[i];
            BDLTevOrder order = i < material.TevOrders.Count ? material.TevOrders[i] : new BDLTevOrder(0xFF, 0xFF, 0xFF);
            StageUniformLocations loc = _stageLocs[i];

            gl.Uniform1(loc.ColorInA, (int)stage.ColorInA);
            gl.Uniform1(loc.ColorInB, (int)stage.ColorInB);
            gl.Uniform1(loc.ColorInC, (int)stage.ColorInC);
            gl.Uniform1(loc.ColorInD, (int)stage.ColorInD);
            gl.Uniform1(loc.ColorOp, (int)stage.ColorOp);
            gl.Uniform1(loc.ColorBias, (int)stage.ColorBias);
            gl.Uniform1(loc.ColorScale, (int)stage.ColorScale);
            gl.Uniform1(loc.ColorClamp, stage.ColorClamp ? 1 : 0);
            gl.Uniform1(loc.ColorOutReg, (int)stage.ColorOutReg);

            gl.Uniform1(loc.AlphaInA, (int)stage.AlphaInA);
            gl.Uniform1(loc.AlphaInB, (int)stage.AlphaInB);
            gl.Uniform1(loc.AlphaInC, (int)stage.AlphaInC);
            gl.Uniform1(loc.AlphaInD, (int)stage.AlphaInD);
            gl.Uniform1(loc.AlphaOp, (int)stage.AlphaOp);
            gl.Uniform1(loc.AlphaBias, (int)stage.AlphaBias);
            gl.Uniform1(loc.AlphaScale, (int)stage.AlphaScale);
            gl.Uniform1(loc.AlphaClamp, stage.AlphaClamp ? 1 : 0);
            gl.Uniform1(loc.AlphaOutReg, (int)stage.AlphaOutReg);

            gl.Uniform1(loc.KonstColorSel, (int)stage.KonstColorSel);
            gl.Uniform1(loc.KonstAlphaSel, (int)stage.KonstAlphaSel);

            int texUnit = ResolveStageTexUnit(material, order, rm);
            gl.Uniform1(loc.TexUnit, texUnit);
            gl.Uniform1(loc.HasRas, order.ColorChannel != 0xFF ? 1 : 0);
        }
    }

    private static int ResolveStageTexUnit(BDLMaterial material, BDLTevOrder order, RenderMesh rm)
    {
        if (order.TexMapIndex == 0xFF || order.TexMapIndex >= material.TextureIndices.Count)
        {
            return -1;
        }

        if (rm.Texture0Slot == order.TexMapIndex)
        {
            return 0;
        }

        if (rm.Texture1Slot == order.TexMapIndex)
        {
            return 1;
        }

        if (rm.Texture2Slot == order.TexMapIndex)
        {
            return 2;
        }

        if (rm.Texture3Slot == order.TexMapIndex)
        {
            return 3;
        }

        return -1;
    }

    private void ApplyIndirectUniforms(GL gl, BDLMaterial material, RenderMesh rm)
    {
        if (rm.IndirectTextureIndex is null)
        {
            gl.Uniform1(_uIndirectEnabledLoc, 0);
            return;
        }

        for (int t = 0; t < material.IndTevStages.Count; t++)
        {
            if (material.IndTevStages[t] is not { } indStage)
            {
                continue;
            }

            int mtxIndex = indStage.MtxSel - 1;
            if (mtxIndex < 0 || mtxIndex >= material.IndTexMatrices.Count || material.IndTexMatrices[mtxIndex] is not { } mtx)
            {
                gl.Uniform1(_uIndirectEnabledLoc, 0);
                return;
            }

            BDLIndTexCoordScale coordScale = indStage.IndStage < material.IndTexCoordScales.Count
                ? material.IndTexCoordScales[indStage.IndStage]
                : new BDLIndTexCoordScale(1f, 1f);

            gl.Uniform1(_uIndirectEnabledLoc, 1);
            gl.Uniform1(_uIndAffectsStageLoc, t);
            gl.Uniform1(_uIndBiasSelLoc, (int)indStage.BiasSel);
            gl.Uniform3(_uIndMtxRowSLoc, mtx.M00 * mtx.ScaleMultiplier, mtx.M01 * mtx.ScaleMultiplier, mtx.M02 * mtx.ScaleMultiplier);
            gl.Uniform3(_uIndMtxRowTLoc, mtx.M10 * mtx.ScaleMultiplier, mtx.M11 * mtx.ScaleMultiplier, mtx.M12 * mtx.ScaleMultiplier);
            gl.Uniform2(_uIndCoordScaleLoc, coordScale.ScaleS, coordScale.ScaleT);
            return;
        }

        gl.Uniform1(_uIndirectEnabledLoc, 0);
    }

    private void ApplyEnvMapUniforms(GL gl, RenderMesh rm, Matrix4x4 world, Matrix4x4 view)
    {
        gl.Uniform1(_uUv0GeneratedLoc, rm.Uv0EnvMapMatrix is not null ? 1 : 0);
        if (rm.Uv0EnvMapMatrix is { } tm0)
        {
            Matrix4x4 m = ComputeEnvMapMatrix(tm0, world, view);
            unsafe { gl.UniformMatrix4(_uEnvMapMatrix0Loc, 1, false, (float*)&m); }
        }

        gl.Uniform1(_uUv1GeneratedLoc, rm.Uv1EnvMapMatrix is not null ? 1 : 0);
        if (rm.Uv1EnvMapMatrix is { } tm1)
        {
            Matrix4x4 m = ComputeEnvMapMatrix(tm1, world, view);
            unsafe { gl.UniformMatrix4(_uEnvMapMatrix1Loc, 1, false, (float*)&m); }
        }

        gl.Uniform1(_uUv2GeneratedLoc, rm.Uv2EnvMapMatrix is not null ? 1 : 0);
        if (rm.Uv2EnvMapMatrix is { } tm2)
        {
            Matrix4x4 m = ComputeEnvMapMatrix(tm2, world, view);
            unsafe { gl.UniformMatrix4(_uEnvMapMatrix2Loc, 1, false, (float*)&m); }
        }

        gl.Uniform1(_uUv3GeneratedLoc, rm.Uv3EnvMapMatrix is not null ? 1 : 0);
        if (rm.Uv3EnvMapMatrix is { } tm3)
        {
            Matrix4x4 m = ComputeEnvMapMatrix(tm3, world, view);
            unsafe { gl.UniformMatrix4(_uEnvMapMatrix3Loc, 1, false, (float*)&m); }
        }
    }

    private static Matrix4x4 ComputeEnvMapMatrix(BDLTexMatrix tm, Matrix4x4 world, Matrix4x4 view)
    {
        Matrix4x4 inputMtx = world * view;
        inputMtx.M41 = 0f;
        inputMtx.M42 = 0f;
        inputMtx.M43 = 0f;
        inputMtx.M44 = 1f;

        Matrix4x4 srtMtx = ComputeSrtMatrix(tm);

        const byte envMapOld = 6;
        if (tm.TexEffect == envMapOld)
        {
            Matrix4x4 envMtxOld = new(
                0.5f, 0f, 0f, 0f,
                0f, -0.5f, 0f, 0f,
                0f, 0f, 1f, 0f,
                0.5f, 0.5f, 0f, 1f);
            return inputMtx * envMtxOld * srtMtx;
        }

        return inputMtx * srtMtx;
    }

    private static Matrix4x4 ComputeSrtMatrix(BDLTexMatrix tm)
    {
        float rot = tm.RotationRadians;
        float cos = MathF.Cos(rot);
        float sin = MathF.Sin(rot);
        float a, b, c, d, e, f;

        if (tm.IsMaya)
        {
            a = tm.Scale.X * cos;
            b = tm.Scale.Y * sin;
            c = (tm.Translation.X - 0.5f) * cos - (tm.Translation.Y - 0.5f + tm.Scale.Y) * sin + 0.5f;
            d = -tm.Scale.X * sin;
            e = tm.Scale.Y * cos;
            f = -(tm.Translation.X - 0.5f) * sin - (tm.Translation.Y - 0.5f + tm.Scale.Y) * cos + 0.5f;
        }
        else
        {
            a = tm.Scale.X * cos;
            b = tm.Scale.X * -sin;
            c = -cos * tm.Origin.X + sin * tm.Origin.Y + tm.Origin.X + tm.Translation.X;
            d = tm.Scale.Y * sin;
            e = tm.Scale.Y * cos;
            f = sin * tm.Origin.X - cos * tm.Origin.Y + tm.Origin.Y + tm.Translation.Y;
        }

        return new Matrix4x4(
            a, d, 0f, 0f,
            b, e, 0f, 0f,
            0f, 0f, 1f, 0f,
            c, f, 0f, 1f);
    }

    private static void SetReg(GL gl, int location, BDLTevRegisterColor c) =>
        gl.Uniform4(location, c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    private static void SetColor(GL gl, int location, BDLColor c) =>
        gl.Uniform4(location, c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    private static void ApplyMaterialState(GL gl, BDLMaterial material)
    {
        switch (material.CullMode)
        {
            case BDLCullMode.None:
                gl.Disable(EnableCap.CullFace);
                break;
            case BDLCullMode.Front:
                gl.Enable(EnableCap.CullFace);
                gl.CullFace(TriangleFace.Front);
                break;
            case BDLCullMode.Back:
                gl.Enable(EnableCap.CullFace);
                gl.CullFace(TriangleFace.Back);
                break;
            case BDLCullMode.All:
                gl.Enable(EnableCap.CullFace);
                gl.CullFace(TriangleFace.FrontAndBack);
                break;
        }

        gl.DepthMask(material.ZMode.DepthWriteEnable);
        if (material.ZMode.DepthTestEnable)
        {
            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(ToDepthFunction(material.ZMode.Function));
        }
        else
        {
            gl.Disable(EnableCap.DepthTest);
        }

        if (material.BlendMode.Type == BDLBlendType.Blend)
        {
            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(ToBlendFactor(material.BlendMode.SrcFactor), ToBlendFactor(material.BlendMode.DstFactor));
        }
        else
        {
            gl.Disable(EnableCap.Blend);
        }
    }

    private static DepthFunction ToDepthFunction(BDLCompare compare) => compare switch
    {
        BDLCompare.Never => DepthFunction.Never,
        BDLCompare.Less => DepthFunction.Less,
        BDLCompare.Equal => DepthFunction.Equal,
        BDLCompare.LessEqual => DepthFunction.Lequal,
        BDLCompare.Greater => DepthFunction.Greater,
        BDLCompare.NotEqual => DepthFunction.Notequal,
        BDLCompare.GreaterEqual => DepthFunction.Gequal,
        _ => DepthFunction.Always,
    };

    private static BlendingFactor ToBlendFactor(BDLBlendFactor factor) => factor switch
    {
        BDLBlendFactor.Zero => BlendingFactor.Zero,
        BDLBlendFactor.One => BlendingFactor.One,
        BDLBlendFactor.SrcColor => BlendingFactor.SrcColor,
        BDLBlendFactor.InverseSrcColor => BlendingFactor.OneMinusSrcColor,
        BDLBlendFactor.SrcAlpha => BlendingFactor.SrcAlpha,
        BDLBlendFactor.InverseSrcAlpha => BlendingFactor.OneMinusSrcAlpha,
        BDLBlendFactor.DstAlpha => BlendingFactor.DstAlpha,
        BDLBlendFactor.InverseDstAlpha => BlendingFactor.OneMinusDstAlpha,
        _ => BlendingFactor.One,
    };

    private static GLEnum ToGLWrap(BDLWrapMode mode) => mode switch
    {
        BDLWrapMode.Clamp => GLEnum.ClampToEdge,
        BDLWrapMode.Mirror => GLEnum.MirroredRepeat,
        _ => GLEnum.Repeat,
    };

    private static uint CreateTexture(GL gl, BDLTexture tex)
    {
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);

        unsafe
        {
            fixed (byte* ptr = tex.Rgba)
            {
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)tex.Width, (uint)tex.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        gl.GenerateMipmap(TextureTarget.Texture2D);
        int wrapS = (int)ToGLWrap(tex.WrapS);
        int wrapT = (int)ToGLWrap(tex.WrapT);
        int minFilter = (int)GLEnum.LinearMipmapLinear;
        int magFilter = (int)GLEnum.Linear;
        gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, in wrapS);
        gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, in wrapT);
        gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in minFilter);
        gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in magFilter);
        return handle;
    }

    private static RenderMesh UploadMesh(GL gl, GpuMesh mesh)
    {
        uint vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);

        uint vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        unsafe
        {
            fixed (float* ptr = mesh.Vertices)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(mesh.Vertices.Length * sizeof(float)), ptr, BufferUsageARB.DynamicDraw);
            }
        }

        const uint stride = 18 * sizeof(float);
        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));
            gl.EnableVertexAttribArray(3);
            gl.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, stride, (void*)(10 * sizeof(float)));
            gl.EnableVertexAttribArray(4);
            gl.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, false, stride, (void*)(14 * sizeof(float)));
            gl.EnableVertexAttribArray(5);
            gl.VertexAttribPointer(6, 2, VertexAttribPointerType.Float, false, stride, (void*)(16 * sizeof(float)));
            gl.EnableVertexAttribArray(6);
        }

        return new RenderMesh
        {
            Vao = vao,
            Vbo = vbo,
            MaterialIndex = mesh.MaterialIndex,
            Texture0Index = mesh.Texture0Index,
            Texture1Index = mesh.Texture1Index,
            Texture2Index = mesh.Texture2Index,
            Texture3Index = mesh.Texture3Index,
            Texture0Slot = mesh.Texture0Slot,
            Texture1Slot = mesh.Texture1Slot,
            Texture2Slot = mesh.Texture2Slot,
            Texture3Slot = mesh.Texture3Slot,
            IndirectTextureIndex = mesh.IndirectTextureIndex,
            Uv0EnvMapMatrix = mesh.Uv0EnvMapMatrix,
            Uv1EnvMapMatrix = mesh.Uv1EnvMapMatrix,
            Uv2EnvMapMatrix = mesh.Uv2EnvMapMatrix,
            Uv3EnvMapMatrix = mesh.Uv3EnvMapMatrix,
            VertexCount = mesh.VertexCount,
        };
    }

    private static (uint TriVao, int TriVertexCount, uint LineVao, int LineVertexCount) BuildGizmoGeometry(GL gl)
    {
        const float h = PlaceholderBoxHalfExtent;
        const float armLength = 120f;

        var white = new Vector3(1f, 1f, 1f);
        var red = new Vector3(1f, 0.15f, 0.15f);
        var green = new Vector3(0.15f, 1f, 0.15f);
        var blue = new Vector3(0.25f, 0.45f, 1f);
        var fill = new Vector3(0.2f, 0.4f, 0.85f);

        var tris = new List<float>();
        var lines = new List<float>();

        void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 color)
        {
            foreach (Vector3 p in (ReadOnlySpan<Vector3>)[a, b, c])
            {
                tris.Add(p.X); tris.Add(p.Y); tris.Add(p.Z);
                tris.Add(color.X); tris.Add(color.Y); tris.Add(color.Z);
            }
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 color)
        {
            AddTri(a, b, c, color);
            AddTri(a, c, d, color);
        }

        void AddLine(Vector3 a, Vector3 b, Vector3 color)
        {
            lines.Add(a.X); lines.Add(a.Y); lines.Add(a.Z);
            lines.Add(color.X); lines.Add(color.Y); lines.Add(color.Z);
            lines.Add(b.X); lines.Add(b.Y); lines.Add(b.Z);
            lines.Add(color.X); lines.Add(color.Y); lines.Add(color.Z);
        }

        var c000 = new Vector3(-h, -h, -h);
        var c001 = new Vector3(-h, -h, h);
        var c010 = new Vector3(-h, h, -h);
        var c011 = new Vector3(-h, h, h);
        var c100 = new Vector3(h, -h, -h);
        var c101 = new Vector3(h, -h, h);
        var c110 = new Vector3(h, h, -h);
        var c111 = new Vector3(h, h, h);

        AddQuad(c000, c001, c011, c010, fill);
        AddQuad(c100, c110, c111, c101, fill);
        AddQuad(c000, c100, c101, c001, fill);
        AddQuad(c010, c011, c111, c110, fill);
        AddQuad(c000, c010, c110, c100, fill);
        AddQuad(c001, c101, c111, c011, fill);

        AddLine(c000, c001, white); AddLine(c001, c011, white); AddLine(c011, c010, white); AddLine(c010, c000, white);
        AddLine(c100, c101, white); AddLine(c101, c111, white); AddLine(c111, c110, white); AddLine(c110, c100, white);
        AddLine(c000, c100, white); AddLine(c001, c101, white); AddLine(c011, c111, white); AddLine(c010, c110, white);

        AddLine(new Vector3(h, 0f, 0f), new Vector3(h + armLength, 0f, 0f), red);
        AddLine(new Vector3(0f, h, 0f), new Vector3(0f, h + armLength, 0f), green);
        AddLine(new Vector3(0f, 0f, h), new Vector3(0f, 0f, h + armLength), blue);

        float[] triData = tris.ToArray();
        float[] lineData = lines.ToArray();
        return (UploadGizmoBuffer(gl, triData), triData.Length / 6, UploadGizmoBuffer(gl, lineData), lineData.Length / 6);
    }

    private static uint UploadGizmoBuffer(GL gl, float[] data)
    {
        uint vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);

        uint vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        unsafe
        {
            fixed (float* ptr = data)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        const uint stride = 6 * sizeof(float);
        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);
        }

        return vao;
    }

    private static (uint Vao, int VertexCount) BuildArrowShaftGeometry(GL gl)
    {
        float[] data = [0f, 0f, 0f, 1f, 0f, 0f];
        return (UploadPositionOnlyBuffer(gl, data), 2);
    }

    private static (uint Vao, int VertexCount) BuildArrowConeGeometry(GL gl)
    {
        const int segments = 10;
        const float baseX = 1f;
        const float apexX = 1.3f;
        const float radius = 0.06f;

        var verts = new List<float>();

        void AddTri(Vector3 a, Vector3 b, Vector3 c)
        {
            foreach (Vector3 p in (ReadOnlySpan<Vector3>)[a, b, c])
            {
                verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);
            }
        }

        var apex = new Vector3(apexX, 0f, 0f);
        var center = new Vector3(baseX, 0f, 0f);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * MathF.Tau;
            float a1 = (i + 1) / (float)segments * MathF.Tau;
            var p0 = new Vector3(baseX, MathF.Cos(a0) * radius, MathF.Sin(a0) * radius);
            var p1 = new Vector3(baseX, MathF.Cos(a1) * radius, MathF.Sin(a1) * radius);
            AddTri(apex, p0, p1);
            AddTri(center, p1, p0);
        }

        float[] data = verts.ToArray();
        return (UploadPositionOnlyBuffer(gl, data), data.Length / 3);
    }

    private static (uint Vao, int VertexCount) BuildRingGeometry(GL gl)
    {
        const int segments = 64;
        var verts = new List<float>();
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * MathF.Tau;
            float a1 = (i + 1) / (float)segments * MathF.Tau;
            verts.Add(MathF.Cos(a0)); verts.Add(MathF.Sin(a0)); verts.Add(0f);
            verts.Add(MathF.Cos(a1)); verts.Add(MathF.Sin(a1)); verts.Add(0f);
        }

        float[] data = verts.ToArray();
        return (UploadPositionOnlyBuffer(gl, data), data.Length / 3);
    }

    private static (uint Vao, int VertexCount) BuildBoxOutlineGeometry(GL gl)
    {
        Vector3 c000 = new(-1, -1, -1), c001 = new(-1, -1, 1), c010 = new(-1, 1, -1), c011 = new(-1, 1, 1);
        Vector3 c100 = new(1, -1, -1), c101 = new(1, -1, 1), c110 = new(1, 1, -1), c111 = new(1, 1, 1);

        var verts = new List<float>();
        void AddLine(Vector3 a, Vector3 b)
        {
            verts.Add(a.X); verts.Add(a.Y); verts.Add(a.Z);
            verts.Add(b.X); verts.Add(b.Y); verts.Add(b.Z);
        }

        AddLine(c000, c001); AddLine(c001, c011); AddLine(c011, c010); AddLine(c010, c000);
        AddLine(c100, c101); AddLine(c101, c111); AddLine(c111, c110); AddLine(c110, c100);
        AddLine(c000, c100); AddLine(c001, c101); AddLine(c011, c111); AddLine(c010, c110);

        float[] data = verts.ToArray();
        return (UploadPositionOnlyBuffer(gl, data), data.Length / 3);
    }

    private static (uint Vao, int VertexCount) UploadAreaEdges(GL gl, List<(Vector3 A, Vector3 B)> edges)
    {
        var verts = new List<float>(edges.Count * 6);
        foreach ((Vector3 a, Vector3 b) in edges)
        {
            verts.Add(a.X); verts.Add(a.Y); verts.Add(a.Z);
            verts.Add(b.X); verts.Add(b.Y); verts.Add(b.Z);
        }

        float[] data = verts.ToArray();
        return (UploadPositionOnlyBuffer(gl, data), data.Length / 3);
    }

    private static (uint Vao, int VertexCount) BuildAreaBoxGeometry(GL gl, float offsetY) =>
        UploadAreaEdges(gl, AreaShapeGeometry.Box(offsetY));

    private static (uint Vao, int VertexCount) BuildAreaSphereGeometry(GL gl, float minYFrac, float maxYFrac) =>
        UploadAreaEdges(gl, AreaShapeGeometry.Sphere(minYFrac, maxYFrac));

    private static (uint Vao, int VertexCount) BuildAreaCylinderGeometry(GL gl) =>
        UploadAreaEdges(gl, AreaShapeGeometry.Cylinder());

    private static uint UploadPositionOnlyBuffer(GL gl, float[] data)
    {
        uint vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);

        uint vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        unsafe
        {
            fixed (float* ptr = data)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(0);
        }

        return vao;
    }

    private static uint CompileShader(GL gl, ShaderType type, string source)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            throw new InvalidOperationException($"{type} compile error: {gl.GetShaderInfoLog(shader)}");
        }

        return shader;
    }

    private static uint CreateProgram(GL gl, string vertexSource, string fragmentSource, string? geometrySource = null)
    {
        uint vs = CompileShader(gl, ShaderType.VertexShader, vertexSource);
        uint fs = CompileShader(gl, ShaderType.FragmentShader, fragmentSource);
        uint? gs = geometrySource is null ? null : CompileShader(gl, ShaderType.GeometryShader, geometrySource);

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        if (gs is { } gsHandle)
        {
            gl.AttachShader(program, gsHandle);
        }

        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            throw new InvalidOperationException($"Program link error: {gl.GetProgramInfoLog(program)}");
        }

        gl.DetachShader(program, vs);
        gl.DetachShader(program, fs);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        if (gs is { } gsHandle2)
        {
            gl.DetachShader(program, gsHandle2);
            gl.DeleteShader(gsHandle2);
        }

        return program;
    }
}
