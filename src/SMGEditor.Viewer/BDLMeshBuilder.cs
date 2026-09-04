using System.Numerics;
using SMGEditor.Core.Formats;

namespace SMGEditor.Viewer;

public sealed class GpuMesh
{
    public required int MaterialIndex { get; init; }

    public ushort? Texture0Index { get; init; }
    public ushort? Texture1Index { get; init; }
    public ushort? Texture2Index { get; init; }
    public ushort? Texture3Index { get; init; }

    public int? Texture0Slot { get; init; }
    public int? Texture1Slot { get; init; }
    public int? Texture2Slot { get; init; }
    public int? Texture3Slot { get; init; }

    public ushort? IndirectTextureIndex { get; init; }

    public BDLTexMatrix? Uv0EnvMapMatrix { get; init; }
    public BDLTexMatrix? Uv1EnvMapMatrix { get; init; }
    public BDLTexMatrix? Uv2EnvMapMatrix { get; init; }
    public BDLTexMatrix? Uv3EnvMapMatrix { get; init; }

    public required float[] Vertices { get; init; }

    public required int VertexCount { get; init; }

    public required int[][] VertexJointIndices { get; init; }
    public required float[][] VertexJointWeights { get; init; }

    public required bool[] VertexIsWeighted { get; init; }

    public required Vector3[] LocalPositions { get; init; }
    public required Vector3[] LocalNormals { get; init; }

    public float[]? RebakeScratch { get; set; }
}

public static class BDLMeshBuilder
{
    public static List<GpuMesh> Build(BDLModel model, IReadOnlyDictionary<string, ushort>? textureOverrides = null)
    {
        Matrix4x4[] jointWorldMatrices = ComputeJointWorldMatrices(model);
        List<(int Joint, int Material, int Shape)> assignments = AssignShapesToMaterials(model);

        var meshes = new List<GpuMesh>();
        foreach ((int joint, int material, int shapeIndex) in assignments)
        {
            BDLMaterial mat = model.Materials[material];
            ushort? textureOverride = textureOverrides is not null && textureOverrides.TryGetValue(mat.Name, out ushort ov) ? ov : null;
            (ushort? tex0, int uvChannel0, ushort? tex1, int uvChannel1, ushort? tex2, int uvChannel2, ushort? tex3, int uvChannel3, int? tex0Slot, int? tex1Slot, int? tex2Slot, int? tex3Slot) = ResolveTextureStages(mat, textureOverride);

            ushort? indirectTex = null;
            if (tex1 is null && ResolveIndirectTexture(mat) is { } ind)
            {
                indirectTex = ind.Texture;
                uvChannel1 = ind.UvChannel;
            }

            BDLTexMatrix? uv0EnvMap = ResolveEnvMapMatrix(mat, uvChannel0);
            BDLTexMatrix? uv1EnvMap = ResolveEnvMapMatrix(mat, uvChannel1);
            BDLTexMatrix? uv2EnvMap = ResolveEnvMapMatrix(mat, uvChannel2);
            BDLTexMatrix? uv3EnvMap = ResolveEnvMapMatrix(mat, uvChannel3);

            BDLShape shape = model.Shapes[shapeIndex];
            foreach (BDLPacket packet in shape.Packets)
            {
                var vertices = new List<float>();
                var vertexJointIndices = new List<int[]>();
                var vertexJointWeights = new List<float[]>();
                var vertexIsWeighted = new List<bool>();
                var localPositions = new List<Vector3>();
                var localNormals = new List<Vector3>();

                foreach (BDLPrimitive primitive in packet.Primitives)
                {
                    foreach ((BDLShapeVertex a, BDLShapeVertex b, BDLShapeVertex c) in Triangulate(primitive))
                    {
                        AppendVertex(vertices, vertexJointIndices, vertexJointWeights, vertexIsWeighted, localPositions, localNormals, model, jointWorldMatrices, a, packet.DrawMatrixIndex, uvChannel0, uvChannel1, uvChannel2, uvChannel3);
                        AppendVertex(vertices, vertexJointIndices, vertexJointWeights, vertexIsWeighted, localPositions, localNormals, model, jointWorldMatrices, b, packet.DrawMatrixIndex, uvChannel0, uvChannel1, uvChannel2, uvChannel3);
                        AppendVertex(vertices, vertexJointIndices, vertexJointWeights, vertexIsWeighted, localPositions, localNormals, model, jointWorldMatrices, c, packet.DrawMatrixIndex, uvChannel0, uvChannel1, uvChannel2, uvChannel3);
                    }
                }

                if (vertices.Count > 0)
                {
                    float[] array = vertices.ToArray();
                    meshes.Add(new GpuMesh
                    {
                        MaterialIndex = material,
                        Texture0Index = tex0,
                        Texture1Index = tex1,
                        Texture2Index = tex2,
                        Texture3Index = tex3,
                        Texture0Slot = tex0Slot,
                        Texture1Slot = tex1Slot,
                        Texture2Slot = tex2Slot,
                        Texture3Slot = tex3Slot,
                        IndirectTextureIndex = indirectTex,
                        Uv0EnvMapMatrix = uv0EnvMap,
                        Uv1EnvMapMatrix = uv1EnvMap,
                        Uv2EnvMapMatrix = uv2EnvMap,
                        Uv3EnvMapMatrix = uv3EnvMap,
                        Vertices = array,
                        VertexCount = array.Length / 18,
                        VertexJointIndices = vertexJointIndices.ToArray(),
                        VertexJointWeights = vertexJointWeights.ToArray(),
                        VertexIsWeighted = vertexIsWeighted.ToArray(),
                        LocalPositions = localPositions.ToArray(),
                        LocalNormals = localNormals.ToArray(),
                    });
                }
            }
        }

        return meshes;
    }

    private static (ushort? Tex0, int UvChannel0, ushort? Tex1, int UvChannel1, ushort? Tex2, int UvChannel2, ushort? Tex3, int UvChannel3, int? Tex0Slot, int? Tex1Slot, int? Tex2Slot, int? Tex3Slot) ResolveTextureStages(
        BDLMaterial material, ushort? textureOverride)
    {
        List<BDLTevOrder> realStages = material.TevOrders
            .Where(o => o.TexMapIndex != 0xFF && o.TexMapIndex < material.TextureIndices.Count && material.TextureIndices[o.TexMapIndex] is not null)
            .ToList();

        var tex = new ushort?[4];
        var uvChannel = new int[4];
        var texSlot = new int?[4];
        int found = 0;

        if (realStages.Count > 0)
        {
            ushort realTex0 = material.TextureIndices[realStages[0].TexMapIndex]!.Value;
            tex[0] = realStages[0].TexMapIndex == 0 && textureOverride is not null ? textureOverride : realTex0;
            texSlot[0] = realStages[0].TexMapIndex;
            uvChannel[0] = realStages[0].TexCoordIndex < 8 ? realStages[0].TexCoordIndex : 0;
            for (int i = 1; i < 4; i++)
            {
                uvChannel[i] = uvChannel[0];
            }

            found = 1;
            var seen = new List<ushort> { realTex0 };

            foreach (BDLTevOrder candidate in realStages.Skip(1))
            {
                if (found >= 4)
                {
                    break;
                }

                ushort candidateTex = material.TextureIndices[candidate.TexMapIndex]!.Value;
                if (seen.Contains(candidateTex))
                {
                    continue;
                }

                tex[found] = candidateTex;
                texSlot[found] = candidate.TexMapIndex;
                uvChannel[found] = candidate.TexCoordIndex < 8 ? candidate.TexCoordIndex : 0;
                seen.Add(candidateTex);
                found++;
            }
        }

        return (tex[0], uvChannel[0], tex[1], uvChannel[1], tex[2], uvChannel[2], tex[3], uvChannel[3], texSlot[0], texSlot[1], texSlot[2], texSlot[3]);
    }

    private static (ushort Texture, int UvChannel)? ResolveIndirectTexture(BDLMaterial material)
    {
        for (int t = 0; t < material.IndTevStages.Count; t++)
        {
            if (material.IndTevStages[t] is not { } indStage)
            {
                continue;
            }

            if (indStage.IndStage >= material.IndTexOrders.Count)
            {
                return null;
            }

            BDLIndTexOrder order = material.IndTexOrders[indStage.IndStage];
            if (order.TexMapIndex == 0xFF || order.TexMapIndex >= material.TextureIndices.Count
                || material.TextureIndices[order.TexMapIndex] is not { } realTex)
            {
                return null;
            }

            return (realTex, order.TexCoordIndex < 8 ? order.TexCoordIndex : 0);
        }

        return null;
    }

    private static BDLTexMatrix? ResolveEnvMapMatrix(BDLMaterial material, int uvChannel)
    {
        if (uvChannel >= material.TexCoordGens.Count)
        {
            return null;
        }

        BDLTexCoordGen gen = material.TexCoordGens[uvChannel];
        const byte matrix3X4 = 0, matrix2X4 = 1, sourceNormal = 1;
        if (gen.GenType != matrix2X4 && gen.GenType != matrix3X4 || gen.Source != sourceNormal)
        {
            return null;
        }

        int slot = (gen.Matrix - 30) / 3;
        if (slot < 0 || slot >= material.TexMatrices.Count || material.TexMatrices[slot] is not { } texMatrix)
        {
            return null;
        }

        const byte envMapBasic = 1, envMapOld = 6;
        return texMatrix.TexEffect is envMapBasic or envMapOld ? texMatrix : null;
    }

    private static Matrix4x4 ResolveDrawMatrix(BDLModel model, Matrix4x4[] jointWorldMatrices, ushort drawMatrixIndex)
    {
        BDLDrawMatrix dm = model.DrawMatrices[drawMatrixIndex];
        return dm.IsWeighted ? Matrix4x4.Identity : jointWorldMatrices[dm.Index];
    }

    private static (int[] Joints, float[] Weights, bool IsWeighted) ResolveSkinInfluences(BDLModel model, ushort drawMatrixIndex)
    {
        BDLDrawMatrix dm = model.DrawMatrices[drawMatrixIndex];
        if (!dm.IsWeighted)
        {
            return ([dm.Index], [1f], false);
        }

        BDLEnvelope env = model.Envelopes[dm.Index];
        if (env.JointIndices.Count == 0)
        {
            return ([0], [1f], false);
        }

        var joints = new int[env.JointIndices.Count];
        for (int i = 0; i < joints.Length; i++)
        {
            joints[i] = env.JointIndices[i];
        }

        return (joints, env.Weights.ToArray(), true);
    }

    private static void AppendVertex(
        List<float> dest, List<int[]> vertexJointIndices, List<float[]> vertexJointWeights, List<bool> vertexIsWeighted, List<Vector3> localPositions, List<Vector3> localNormals,
        BDLModel model, Matrix4x4[] jointWorldMatrices, BDLShapeVertex v, ushort packetDrawMatrixIndex,
        int uvChannel0, int uvChannel1, int uvChannel2, int uvChannel3)
    {
        BDLVertexData vd = model.VertexData!;
        ushort drawMatrixIndex = v.DrawMatrixIndexOverride ?? packetDrawMatrixIndex;
        Matrix4x4 worldMatrix = ResolveDrawMatrix(model, jointWorldMatrices, drawMatrixIndex);

        Vector3 localPos = vd.Positions!.GetVector3(v.PositionIndex);
        bool hasNormal = v.NormalIndex is int;
        Vector3 localNrm = hasNormal ? vd.Normals!.GetVector3(v.NormalIndex!.Value) : Vector3.UnitY;

        Vector3 pos = Vector3.Transform(localPos, worldMatrix);
        Vector3 nrm = hasNormal ? Vector3.TransformNormal(localNrm, worldMatrix) : Vector3.UnitY;
        if (nrm.LengthSquared() > 0.0001f)
        {
            nrm = Vector3.Normalize(nrm);
        }

        Vector2 uv0 = v.TexCoordIndices[uvChannel0] is int ti0 ? vd.TexCoords[uvChannel0]!.GetVector2(ti0) : Vector2.Zero;
        Vector2 uv1 = v.TexCoordIndices[uvChannel1] is int ti1 ? vd.TexCoords[uvChannel1]!.GetVector2(ti1) : uv0;
        Vector2 uv2 = v.TexCoordIndices[uvChannel2] is int ti2 ? vd.TexCoords[uvChannel2]!.GetVector2(ti2) : uv0;
        Vector2 uv3 = v.TexCoordIndices[uvChannel3] is int ti3 ? vd.TexCoords[uvChannel3]!.GetVector2(ti3) : uv0;
        Vector4 color = v.Color0Index is int ci ? vd.Color0!.GetColor(ci) : Vector4.One;

        dest.Add(pos.X); dest.Add(pos.Y); dest.Add(pos.Z);
        dest.Add(nrm.X); dest.Add(nrm.Y); dest.Add(nrm.Z);
        dest.Add(uv0.X); dest.Add(uv0.Y);
        dest.Add(uv1.X); dest.Add(uv1.Y);
        dest.Add(color.X); dest.Add(color.Y); dest.Add(color.Z); dest.Add(color.W);
        dest.Add(uv2.X); dest.Add(uv2.Y);
        dest.Add(uv3.X); dest.Add(uv3.Y);

        (int[] joints, float[] weights, bool isWeighted) = ResolveSkinInfluences(model, drawMatrixIndex);
        vertexJointIndices.Add(joints);
        vertexJointWeights.Add(weights);
        vertexIsWeighted.Add(isWeighted);
        localPositions.Add(localPos);
        localNormals.Add(localNrm);
    }

    private static IEnumerable<(BDLShapeVertex, BDLShapeVertex, BDLShapeVertex)> Triangulate(BDLPrimitive primitive)
    {
        IReadOnlyList<BDLShapeVertex> v = primitive.Vertices;
        switch (primitive.Type)
        {
            case BDLPrimitiveType.Triangles:
                for (int i = 0; i + 2 < v.Count; i += 3)
                {
                    yield return (v[i], v[i + 1], v[i + 2]);
                }

                break;
            case BDLPrimitiveType.TriangleStrip:
                for (int i = 2; i < v.Count; i++)
                {
                    yield return (i % 2 == 0) ? (v[i - 2], v[i - 1], v[i]) : (v[i - 1], v[i - 2], v[i]);
                }

                break;
            case BDLPrimitiveType.TriangleFan:
                for (int i = 2; i < v.Count; i++)
                {
                    yield return (v[0], v[i - 1], v[i]);
                }

                break;
            case BDLPrimitiveType.Quads:
                for (int i = 0; i + 3 < v.Count; i += 4)
                {
                    yield return (v[i], v[i + 1], v[i + 2]);
                    yield return (v[i], v[i + 2], v[i + 3]);
                }

                break;
            default:
                throw new NotSupportedException($"Primitive type {primitive.Type} isn't implemented for rendering.");
        }
    }

    private static Matrix4x4[] ComputeJointWorldMatrices(BDLModel model)
    {
        var local = new Matrix4x4[model.Joints.Count];
        for (int i = 0; i < model.Joints.Count; i++)
        {
            BDLJoint j = model.Joints[i];
            local[i] = Matrix4x4.CreateScale(j.Scale) *
                       Matrix4x4.CreateRotationX(DegToRad(j.RotationDegrees.X)) *
                       Matrix4x4.CreateRotationY(DegToRad(j.RotationDegrees.Y)) *
                       Matrix4x4.CreateRotationZ(DegToRad(j.RotationDegrees.Z)) *
                       Matrix4x4.CreateTranslation(j.Translation);
        }

        return ComposeWorldMatrices(model, local);
    }

    public static Matrix4x4[] ComputeAnimatedJointWorldMatrices(BDLModel model, BCKAnimation anim, float frame)
    {
        var local = new Matrix4x4[model.Joints.Count];
        for (int i = 0; i < model.Joints.Count; i++)
        {
            BDLJoint j = model.Joints[i];
            (Vector3 scale, Vector3 rotDeg, Vector3 trans) = i < anim.Joints.Count
                ? anim.Joints[i].Sample(frame)
                : (j.Scale, j.RotationDegrees, j.Translation);

            local[i] = Matrix4x4.CreateScale(scale) *
                       Matrix4x4.CreateRotationX(DegToRad(rotDeg.X)) *
                       Matrix4x4.CreateRotationY(DegToRad(rotDeg.Y)) *
                       Matrix4x4.CreateRotationZ(DegToRad(rotDeg.Z)) *
                       Matrix4x4.CreateTranslation(trans);
        }

        return ComposeWorldMatrices(model, local);
    }

    private static Matrix4x4 ToRowVectorMatrix(BDLMatrix3x4 gx) => new(
        gx[0, 0], gx[1, 0], gx[2, 0], 0f,
        gx[0, 1], gx[1, 1], gx[2, 1], 0f,
        gx[0, 2], gx[1, 2], gx[2, 2], 0f,
        gx[0, 3], gx[1, 3], gx[2, 3], 1f);

    public static Matrix4x4[] ComputeInverseBindMatrices(BDLModel model)
    {
        var result = new Matrix4x4[model.InverseBindMatrices.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = ToRowVectorMatrix(model.InverseBindMatrices[i]);
        }

        return result;
    }

    public static void RebakeVertices(GpuMesh mesh, Matrix4x4[] animatedJointWorldMatrices, Matrix4x4[] inverseBindMatrices, float[] dest)
    {
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            int baseIdx = v * 18;
            int[] joints = mesh.VertexJointIndices[v];
            float[] weights = mesh.VertexJointWeights[v];
            bool isWeighted = mesh.VertexIsWeighted[v];

            Matrix4x4 blended = default;
            for (int i = 0; i < joints.Length; i++)
            {
                Matrix4x4 skin = isWeighted
                    ? inverseBindMatrices[joints[i]] * animatedJointWorldMatrices[joints[i]]
                    : animatedJointWorldMatrices[joints[i]];
                blended += skin * weights[i];
            }

            Vector3 pos = Vector3.Transform(mesh.LocalPositions[v], blended);
            Vector3 nrm = Vector3.TransformNormal(mesh.LocalNormals[v], blended);
            if (nrm.LengthSquared() > 0.0001f)
            {
                nrm = Vector3.Normalize(nrm);
            }

            dest[baseIdx] = pos.X; dest[baseIdx + 1] = pos.Y; dest[baseIdx + 2] = pos.Z;
            dest[baseIdx + 3] = nrm.X; dest[baseIdx + 4] = nrm.Y; dest[baseIdx + 5] = nrm.Z;
            Array.Copy(mesh.Vertices, baseIdx + 6, dest, baseIdx + 6, 12);
        }
    }

    private static Matrix4x4[] ComposeWorldMatrices(BDLModel model, Matrix4x4[] local)
    {
        var parent = new int[model.Joints.Count];
        Array.Fill(parent, -1);

        var parentStack = new Stack<int>();
        parentStack.Push(-1);
        int currentJoint = -1;

        foreach (BDLHierarchyNode node in model.HierarchyNodes)
        {
            switch (node.Type)
            {
                case BDLHierarchyNodeType.Begin:
                    parentStack.Push(currentJoint);
                    break;
                case BDLHierarchyNodeType.End:
                    currentJoint = parentStack.Pop();
                    break;
                case BDLHierarchyNodeType.Joint:
                    parent[node.Index] = parentStack.Peek();
                    currentJoint = node.Index;
                    break;
            }
        }

        var world = new Matrix4x4[model.Joints.Count];
        var resolved = new bool[model.Joints.Count];

        Matrix4x4 Resolve(int index)
        {
            if (resolved[index])
            {
                return world[index];
            }

            Matrix4x4 result = parent[index] < 0 ? local[index] : local[index] * Resolve(parent[index]);
            world[index] = result;
            resolved[index] = true;
            return result;
        }

        for (int i = 0; i < model.Joints.Count; i++)
        {
            Resolve(i);
        }

        return world;
    }

    private static List<(int Joint, int Material, int Shape)> AssignShapesToMaterials(BDLModel model)
    {
        var assignments = new List<(int, int, int)>();
        var parentStack = new Stack<int>();
        parentStack.Push(-1);
        int currentJoint = -1;
        int currentMaterial = -1;

        foreach (BDLHierarchyNode node in model.HierarchyNodes)
        {
            switch (node.Type)
            {
                case BDLHierarchyNodeType.Begin:
                    parentStack.Push(currentJoint);
                    break;
                case BDLHierarchyNodeType.End:
                    currentJoint = parentStack.Pop();
                    break;
                case BDLHierarchyNodeType.Joint:
                    currentJoint = node.Index;
                    break;
                case BDLHierarchyNodeType.Material:
                    currentMaterial = node.Index;
                    break;
                case BDLHierarchyNodeType.Shape:
                    assignments.Add((currentJoint, currentMaterial, node.Index));
                    break;
            }
        }

        return assignments;
    }

    private static float DegToRad(float degrees) => degrees * MathF.PI / 180f;
}
