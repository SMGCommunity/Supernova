using System.Buffers.Binary;
using System.Numerics;
using SMGEditor.Core.Formats;

namespace SMGEditor.Core.Collision;

public readonly record struct KclHit(int TriangleIndex, float Distance, byte Flag);

public sealed class KclCollisionServer(KclFile file)
{
    private readonly record struct V3u(int X, int Y, int Z);

    public float MaxVertexDistance { get; private set; } = 1f;

    public Vector3 GetVertex(KclPrism prism, int vertexIndex)
    {
        switch (vertexIndex)
        {
            case 0:
                return file.GetPosition(prism.PositionIndex);
            case 1:
            {
                Vector3 pos = file.GetPosition(prism.PositionIndex);
                Vector3 edge2 = file.GetNormal(prism.EdgeIndex2);
                Vector3 cross = CalXvec(file.GetNormal(prism.EdgeIndex1), file.GetNormal(prism.NormalIndex));
                float sideLength = prism.Height / Vector3.Dot(cross, edge2);
                return pos + cross * sideLength;
            }
            case 2:
            {
                Vector3 pos = file.GetPosition(prism.PositionIndex);
                Vector3 edge2 = file.GetNormal(prism.EdgeIndex2);
                Vector3 cross = CalXvec(file.GetNormal(prism.NormalIndex), file.GetNormal(prism.EdgeIndex0));
                float sideLength = prism.Height / Vector3.Dot(cross, edge2);
                return pos + cross * sideLength;
            }
            default:
                return Vector3.Zero;
        }
    }

    public Vector3 GetFaceNormal(KclPrism prism) => file.GetNormal(prism.NormalIndex);

    public Vector3 GetEdgeNormal0(KclPrism prism) => file.GetNormal(prism.EdgeIndex0);

    public Vector3 GetEdgeNormal1(KclPrism prism) => file.GetNormal(prism.EdgeIndex1);

    public Vector3 GetEdgeNormal2(KclPrism prism) => file.GetNormal(prism.EdgeIndex2);

    private static Vector3 CalXvec(Vector3 a, Vector3 b) => Vector3.Cross(b, a);

    public bool IsNearParallelNormal(KclPrism prism)
    {
        Vector3 edge0 = file.GetNormal(prism.EdgeIndex0);
        Vector3 edge1 = file.GetNormal(prism.EdgeIndex1);
        Vector3 edge2 = file.GetNormal(prism.EdgeIndex2);

        return KclMath.IsSameDirection(edge0, edge1) || KclMath.IsSameDirection(edge0, edge2) || KclMath.IsSameDirection(edge1, edge2);
    }

    public float CalcFarthestVertexDistance()
    {
        float maxDistance = 0f;

        for (int i = 0; i < file.TriangleCount; i++)
        {
            KclPrism prism = file.GetPrism(i);

            if (IsNearParallelNormal(prism))
            {
                file.SetPrism(i, prism with { Height = -MathF.Abs(prism.Height) });
                continue;
            }

            for (int v = 0; v < 3; v++)
            {
                float distSq = GetVertex(prism, v).LengthSquared();
                if (maxDistance < distSq)
                {
                    maxDistance = distSq;
                }
            }
        }

        MaxVertexDistance = MathF.Sqrt(maxDistance);
        return MaxVertexDistance;
    }

    public int? CheckPoint(Vector3 point, float param, out float distance)
    {
        distance = 0f;
        float maxDist = file.Thickness * param;

        int x = (int)(point.X - file.Min.X);
        if ((x & file.XMask) != 0)
        {
            return null;
        }

        int y = (int)(point.Y - file.Min.Y);
        if ((y & file.YMask) != 0)
        {
            return null;
        }

        int z = (int)(point.Z - file.Min.Z);
        if ((z & file.ZMask) != 0)
        {
            return null;
        }

        (_, int listOffset) = SearchBlock((uint)x, (uint)y, (uint)z);

        foreach (int prismIndex in PrismListIndices(listOffset))
        {
            KclPrism prism = file.Prisms[prismIndex];
            if (prism.Height <= 0f)
            {
                continue;
            }

            Vector3 vtx = file.GetPosition(prism.PositionIndex);
            Vector3 dir = point - vtx;

            if (Vector3.Dot(dir, file.GetNormal(prism.EdgeIndex0)) > 0f)
            {
                continue;
            }

            if (Vector3.Dot(dir, file.GetNormal(prism.EdgeIndex1)) > 0f)
            {
                continue;
            }

            if (Vector3.Dot(dir, file.GetNormal(prism.EdgeIndex2)) > prism.Height)
            {
                continue;
            }

            float dist = -Vector3.Dot(dir, file.GetNormal(prism.NormalIndex));
            if (dist < 0f || maxDist < dist)
            {
                continue;
            }

            distance = dist;
            return prismIndex - 1;
        }

        return null;
    }

    public List<int> CheckArea3D(Vector3 boxA, Vector3 boxB, int maxCount)
    {
        var found = new List<int>();

        Vector3 queryMin = Vector3.Min(boxA, boxB);
        Vector3 queryMax = Vector3.Max(boxA, boxB);

        if (queryMin.X == queryMax.X)
        {
            queryMin.X -= 1f;
            queryMax.X += 1f;
        }

        if (queryMin.Y == queryMax.Y)
        {
            queryMin.Y -= 1f;
            queryMax.Y += 1f;
        }

        if (queryMin.Z == queryMax.Z)
        {
            queryMin.Z -= 1f;
            queryMax.Z += 1f;
        }

        if (!OutCheck(queryMin, queryMax, out V3u pointMin, out V3u pointMax))
        {
            return found;
        }

        for (uint z = (uint)pointMin.Z; z <= (uint)pointMax.Z;)
        {
            uint zStep = 1_000_000;

            for (uint y = (uint)pointMin.Y; y <= (uint)pointMax.Y;)
            {
                uint yStep = 1_000_000;

                for (uint x = (uint)pointMin.X; x <= (uint)pointMax.X;)
                {
                    (int shift, int listOffset) = SearchBlock(x, y, z);
                    uint blockSize = 1u << shift;
                    uint mask = blockSize - 1;
                    uint remX = blockSize - (x & mask);
                    uint remY = blockSize - (y & mask);
                    uint remZ = blockSize - (z & mask);

                    if (remZ < zStep)
                    {
                        zStep = remZ;
                    }

                    if (remY < yStep)
                    {
                        yStep = remY;
                    }

                    foreach (int prismIndex in PrismListIndices(listOffset))
                    {
                        KclPrism prism = file.Prisms[prismIndex];
                        if (prism.Height <= 0f)
                        {
                            continue;
                        }

                        int triangleIndex = prismIndex - 1;
                        if (found.Contains(triangleIndex))
                        {
                            continue;
                        }

                        Vector3 v0 = GetVertex(prism, 0);
                        Vector3 v1 = GetVertex(prism, 1);
                        Vector3 v2 = GetVertex(prism, 2);

                        Vector3 prismMin = Vector3.Min(Vector3.Min(v0, v1), v2);
                        Vector3 prismMax = Vector3.Max(Vector3.Max(v0, v1), v2);

                        if (prismMax.X < queryMin.X || prismMax.Y < queryMin.Y || prismMax.Z < queryMin.Z)
                        {
                            continue;
                        }

                        if (queryMax.X < prismMin.X || queryMax.Y < prismMin.Y || queryMax.Z < prismMin.Z)
                        {
                            continue;
                        }

                        found.Add(triangleIndex);
                        if (found.Count == maxCount)
                        {
                            return found;
                        }
                    }

                    x += remX;
                }

                y += yStep;
            }

            z += zStep;
        }

        return found;
    }

    public List<KclHit> CheckSphere(Vector3 center, float radius, float param, int maxCount) =>
        CheckSphereCore(center, radius, param, maxCount, file.Thickness);

    public List<KclHit> CheckSphereWithThickness(Vector3 center, float radius, float param, int maxCount, float thickness) =>
        CheckSphereCore(center, radius, param, maxCount, thickness);

    private List<KclHit> CheckSphereCore(Vector3 center, float radius, float param, int maxCount, float thickness)
    {
        var found = new List<KclHit>();

        Vector3 boxMin = center - new Vector3(radius);
        Vector3 boxMax = center + new Vector3(radius);

        if (!OutCheck(boxMin, boxMax, out V3u pointMin, out V3u pointMax))
        {
            return found;
        }

        for (uint z = (uint)pointMin.Z; z <= (uint)pointMax.Z;)
        {
            uint zStep = 1_000_000;

            for (uint y = (uint)pointMin.Y; y <= (uint)pointMax.Y;)
            {
                uint yStep = 1_000_000;

                for (uint x = (uint)pointMin.X; x <= (uint)pointMax.X;)
                {
                    (int shift, int listOffset) = SearchBlock(x, y, z);
                    uint blockSize = 1u << shift;
                    uint mask = blockSize - 1;
                    uint remX = blockSize - (x & mask);
                    uint remY = blockSize - (y & mask);
                    uint remZ = blockSize - (z & mask);

                    if (remZ < zStep)
                    {
                        zStep = remZ;
                    }

                    if (remY < yStep)
                    {
                        yStep = remY;
                    }

                    foreach (int prismIndex in PrismListIndices(listOffset))
                    {
                        KclPrism prism = file.Prisms[prismIndex];
                        if (prism.Height <= 0f)
                        {
                            continue;
                        }

                        int triangleIndex = prismIndex - 1;
                        if (found.Count >= maxCount || found.Exists(h => h.TriangleIndex == triangleIndex))
                        {
                            continue;
                        }

                        if (!HitSphere(prism, center, radius, param, thickness, out float dist, out byte flag))
                        {
                            continue;
                        }

                        found.Add(new KclHit(triangleIndex, dist, flag));
                    }

                    x += remX;
                }

                y += yStep;
            }

            z += zStep;
        }

        return found;
    }

    public KclHit? CheckArrowClosest(Vector3 origin, Vector3 dir) => CheckArrow(origin, dir, null, 0);

    public List<KclHit> CheckArrowAll(Vector3 origin, Vector3 dir, int maxCount)
    {
        var hits = new List<KclHit>();
        CheckArrow(origin, dir, hits, maxCount);
        return hits;
    }

    private KclHit? CheckArrow(Vector3 origin, Vector3 dir, List<KclHit>? collectInto, int maxCount)
    {
        if (dir == Vector3.Zero)
        {
            return null;
        }

        KclMath.SeparateScalarAndDirection(dir, out float length, out Vector3 unitDir);
        if (KclMath.IsNearZeroComponentwise(unitDir, 0.001f))
        {
            return null;
        }

        Vector3 start = origin - file.Min;
        V3u cell = CastToInt(start);

        Vector3 hitPoint = start;
        float startT = 0f;

        if (!IsInsideMinMaxInLocalSpace(cell))
        {
            var boxMax = new Vector3((uint)~file.XMask, (uint)~file.YMask, (uint)~file.ZMask);
            bool entered = false;

            if (unitDir.X != 0f)
            {
                float edge = unitDir.X <= 0f ? boxMax.X : 0f;
                startT = (edge - start.X) / unitDir.X;

                if (startT >= 0f && startT <= length)
                {
                    hitPoint = start + unitDir * startT;
                    cell = CastToInt(hitPoint);
                    entered = IsInsideMinMaxInLocalSpace(cell);
                }
            }

            if (!entered && unitDir.Y != 0f)
            {
                float edge = unitDir.Y <= 0f ? boxMax.Y : 0f;
                startT = (edge - start.Y) / unitDir.Y;

                if (startT >= 0f && startT <= length)
                {
                    hitPoint = start + unitDir * startT;
                    cell = CastToInt(hitPoint);
                    entered = IsInsideMinMaxInLocalSpace(cell);
                }
            }

            if (!entered && unitDir.Z != 0f)
            {
                float edge = unitDir.Z <= 0f ? boxMax.Z : 0f;
                startT = (edge - start.Z) / unitDir.Z;

                if (startT >= 0f && startT <= length)
                {
                    hitPoint = start + unitDir * startT;
                    cell = CastToInt(hitPoint);
                    entered = IsInsideMinMaxInLocalSpace(cell);
                }
            }

            if (!entered)
            {
                return null;
            }
        }

        int foundCount = 0;
        KclHit? best = null;
        float bestFraction = 1f;

        int stepX = unitDir.X < 0f ? -1 : 1;
        int stepY = unitDir.Y < 0f ? -1 : 1;
        int stepZ = unitDir.Z < 0f ? -1 : 1;

        float accumT = startT;

        do
        {
            (int shift, int listOffset) = SearchBlock((uint)cell.X, (uint)cell.Y, (uint)cell.Z);
            int blockSize = 1 << shift;
            int mask = blockSize - 1;

            int deltaPosX = blockSize - (cell.X & mask);
            int deltaPosY = blockSize - (cell.Y & mask);
            int deltaPosZ = blockSize - (cell.Z & mask);
            int deltaNegX = -(cell.X & mask);
            int deltaNegY = -(cell.Y & mask);
            int deltaNegZ = -(cell.Z & mask);

            int deltaX = stepX < 0 ? deltaNegX : deltaPosX;
            int deltaY = stepY < 0 ? deltaNegY : deltaPosY;
            int deltaZ = stepZ < 0 ? deltaNegZ : deltaPosZ;

            if (deltaX == 0)
            {
                deltaX = stepX;
            }

            if (deltaY == 0)
            {
                deltaY = stepY;
            }

            if (deltaZ == 0)
            {
                deltaZ = stepZ;
            }

            foreach (int prismIndex in PrismListIndices(listOffset))
            {
                KclPrism prism = file.Prisms[prismIndex];
                if (prism.Height <= 0f)
                {
                    continue;
                }

                if (!HitArrow(prism, origin, dir, out float dist, out byte flag))
                {
                    continue;
                }

                int triangleIndex = prismIndex - 1;

                if (collectInto != null)
                {
                    var hit = new KclHit(triangleIndex, dist, flag);
                    collectInto.Add(hit);
                    foundCount++;

                    if (dist < bestFraction)
                    {
                        bestFraction = dist;
                        best = hit;
                    }

                    if (foundCount == maxCount)
                    {
                        return best;
                    }
                }
                else
                {
                    if (dist >= bestFraction)
                    {
                        continue;
                    }

                    bestFraction = dist;
                    best = new KclHit(triangleIndex, dist, flag);
                }
            }

            if (collectInto == null && best != null)
            {
                break;
            }

            float tX = KclMath.IsNearZero(unitDir.X, 0.001f) ? 1e9f : deltaX / unitDir.X;
            float tY = KclMath.IsNearZero(unitDir.Y, 0.001f) ? 1e9f : deltaY / unitDir.Y;
            float tZ = KclMath.IsNearZero(unitDir.Z, 0.001f) ? 1e9f : deltaZ / unitDir.Z;

            float tMin = MathF.Min(tX, MathF.Min(tY, tZ));

            if (length - accumT <= tMin)
            {
                break;
            }

            hitPoint += unitDir * tMin;
            accumT += tMin;

            cell = CastToInt(hitPoint);

            if (!IsInsideMinMaxInLocalSpace(cell))
            {
                break;
            }
        } while (accumT < length);

        return best;
    }

    private bool HitSphere(KclPrism prism, Vector3 center, float radius, float param, float thickness, out float dist, out byte flag)
    {
        float radiusSq = radius * radius;
        float threshold = thickness * param;
        flag = 0;
        dist = 0f;

        Vector3 v0 = file.GetPosition(prism.PositionIndex);
        Vector3 n0 = file.GetNormal(prism.EdgeIndex0);

        Vector3 dir = center - v0;

        float d0 = Vector3.Dot(dir, n0);
        if (d0 >= radius)
        {
            return false;
        }

        Vector3 n1 = file.GetNormal(prism.EdgeIndex1);
        float d1 = Vector3.Dot(dir, n1);
        if (d1 >= radius)
        {
            return false;
        }

        Vector3 n2 = file.GetNormal(prism.EdgeIndex2);
        float d2 = Vector3.Dot(dir, n2) - prism.Height;
        if (d2 >= radius)
        {
            return false;
        }

        Vector3 faceNormal = file.GetNormal(prism.NormalIndex);
        float faceDot = Vector3.Dot(dir, faceNormal);
        dist = radius - faceDot;
        if (dist < 0f)
        {
            return false;
        }

        float nn;

        if (d0 > d1)
        {
            if (d0 > d2)
            {
                if (d0 <= 0f)
                {
                    if (threshold < dist)
                    {
                        return false;
                    }

                    flag = 1;
                    return true;
                }

                if (d1 > d2)
                {
                    nn = Vector3.Dot(n0, n1);
                    if (nn * d0 > d1)
                    {
                        goto vertex2;
                    }

                    goto region5;
                }

                nn = Vector3.Dot(n0, n2);
                if (nn * d0 > d2)
                {
                    goto vertex2;
                }

                goto region7;
            }
        }
        else if (d1 > d2)
        {
            if (d1 <= 0f)
            {
                if (threshold < dist)
                {
                    return false;
                }

                flag = 1;
                return true;
            }

            if (d2 > d0)
            {
                nn = Vector3.Dot(n1, n2);
                if (nn * d1 > d2)
                {
                    goto vertex3;
                }

                goto region6;
            }

            nn = Vector3.Dot(n1, n0);
            if (nn * d1 > d0)
            {
                goto vertex3;
            }

            goto region5;
        }

        if (d2 <= 0f)
        {
            if (threshold < dist)
            {
                return false;
            }

            flag = 1;
            return true;
        }

        if (d0 > d1)
        {
            nn = Vector3.Dot(n2, n0);
            if (nn * d2 > d0)
            {
                goto vertex4;
            }

            goto region7;
        }

        nn = Vector3.Dot(n2, n1);
        if (nn * d2 > d1)
        {
            goto vertex4;
        }

        goto region6;

    vertex2:
        if (d0 > faceDot)
        {
            return false;
        }

        dist = radiusSq - d0 * d0;
        flag = 2;
        goto finish;

    vertex3:
        if (d1 > faceDot)
        {
            return false;
        }

        dist = radiusSq - d1 * d1;
        flag = 3;
        goto finish;

    vertex4:
        if (d2 > faceDot)
        {
            return false;
        }

        dist = radiusSq - d2 * d2;
        flag = 4;
        goto finish;

    region5:
        {
            float t = (nn * d1 - d0) / (nn * nn - 1f);
            float s = d1 - t * nn;
            flag = 5;
            dir = n0 * t + n1 * s;
            goto edgeFinish;
        }

    region6:
        {
            float t = (nn * d2 - d1) / (nn * nn - 1f);
            float s = d2 - t * nn;
            flag = 6;
            dir = n1 * t + n2 * s;
            goto edgeFinish;
        }

    region7:
        {
            float t = (nn * d0 - d2) / (nn * nn - 1f);
            float s = d0 - t * nn;
            flag = 7;
            dir = n2 * t + n0 * s;
        }

    edgeFinish:
        {
            float closestSq = dir.LengthSquared();
            float closeDist = MathF.Sqrt(closestSq);

            if (closeDist > faceDot || closeDist >= radius)
            {
                flag = 0;
                return false;
            }

            dist = radiusSq - closestSq;
        }

    finish:
        dist = MathF.Sqrt(dist) - faceDot;
        if (dist < 0f || threshold < dist)
        {
            flag = 0;
            return false;
        }

        return true;
    }

    private bool HitArrow(KclPrism prism, Vector3 origin, Vector3 dir, out float dist, out byte flag)
    {
        dist = 0f;
        flag = 0;

        Vector3 v0 = file.GetPosition(prism.PositionIndex);
        Vector3 faceNormal = file.GetNormal(prism.NormalIndex);

        Vector3 rel = origin - v0;
        float t = Vector3.Dot(rel, faceNormal);
        if (t <= 0f)
        {
            return false;
        }

        float dirDotFace = Vector3.Dot(faceNormal, dir);
        if (0f < t + dirDotFace)
        {
            return false;
        }

        t /= -dirDotFace;

        Vector3 hit = dir * t + rel;

        bool onEdge0 = false;
        bool onEdge1 = false;
        bool onEdge2 = false;

        float e0 = Vector3.Dot(hit, file.GetNormal(prism.EdgeIndex0));
        if (e0 > 0.01f)
        {
            return false;
        }

        if (e0 >= 0f && e0 <= 0.01f)
        {
            onEdge0 = true;
        }

        float e1 = Vector3.Dot(hit, file.GetNormal(prism.EdgeIndex1));
        if (e1 > 0.01f)
        {
            return false;
        }

        if (e1 >= 0f && e1 <= 0.01f)
        {
            onEdge1 = true;
        }

        float e2 = Vector3.Dot(hit, file.GetNormal(prism.EdgeIndex2));
        if (e2 > 0.01f + prism.Height)
        {
            return false;
        }

        if (e2 >= 0f && e2 <= 0.01f)
        {
            onEdge2 = true;
        }

        dist = t;

        if (onEdge0)
        {
            if (onEdge1)
            {
                flag = onEdge2 ? (byte)1 : (byte)5;
            }
            else
            {
                flag = onEdge2 ? (byte)7 : (byte)2;
            }
        }
        else
        {
            if (onEdge1)
            {
                flag = onEdge2 ? (byte)6 : (byte)3;
            }
            else
            {
                flag = onEdge2 ? (byte)4 : (byte)1;
            }
        }

        return true;
    }

    private static V3u CastToInt(Vector3 v) => new((int)v.X, (int)v.Y, (int)v.Z);

    private bool IsInsideMinMaxInLocalSpace(V3u point)
    {
        if ((point.X & file.XMask) != 0 || (point.Y & file.YMask) != 0)
        {
            return false;
        }

        return (point.Z & file.ZMask) == 0;
    }

    private void ObjectSpaceToLocalSpace(out V3u point, Vector3 pos)
    {
        Vector3 rel = pos - file.Min;
        point = new V3u((int)rel.X, (int)rel.Y, (int)rel.Z);
    }

    private bool OutCheck(Vector3 posA, Vector3 posB, out V3u pointMin, out V3u pointMax)
    {
        ObjectSpaceToLocalSpace(out V3u pointA, posA);
        ObjectSpaceToLocalSpace(out V3u pointB, posB);

        pointA = new V3u(Math.Max(pointA.X, 0), Math.Max(pointA.Y, 0), Math.Max(pointA.Z, 0));

        int invXMask = ~file.XMask;
        int invYMask = ~file.YMask;
        int invZMask = ~file.ZMask;
        pointB = new V3u(Math.Min(pointB.X, invXMask), Math.Min(pointB.Y, invYMask), Math.Min(pointB.Z, invZMask));

        pointMin = pointA;
        pointMax = pointB;

        return pointB.X >= pointA.X && pointB.Y >= pointA.Y && pointB.Z >= pointA.Z;
    }

    private (int Shift, int ListOffset) SearchBlock(uint x, uint y, uint z)
    {
        int shift = file.BlockWidthShift;
        int octree = file.OctreeOffset;

        int offset = (int)(((x >> shift) | ((z >> shift) << file.BlockXYShift) | ((y >> shift) << file.BlockXShift)) * 4);

        if (file.BlockXYShift == -1 && file.BlockXShift == -1)
        {
            offset = 0;
        }

        while (true)
        {
            int word = BinaryPrimitives.ReadInt32BigEndian(file.Data.AsSpan(octree + offset, 4));
            if (word < 0)
            {
                offset = word;
                break;
            }

            octree += word;
            shift--;

            offset = (int)(((((z >> shift) & 1) << 2) | (((y >> shift) & 1) << 1) | ((x >> shift) & 1)) * 4);
        }

        return (shift, octree + (offset & 0x7FFFFFFF));
    }

    private IEnumerable<int> PrismListIndices(int listOffset)
    {
        int pos = listOffset + 2;
        while (true)
        {
            ushort index = BinaryPrimitives.ReadUInt16BigEndian(file.Data.AsSpan(pos, 2));
            if (index == 0)
            {
                yield break;
            }

            yield return index;
            pos += 2;
        }
    }
}
