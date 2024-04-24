using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public enum IntersectResult
    {
        In = 0,
        Out = -1,
        Partial = 1,
    };

    public struct PackedMatrix
    {
        public float4 Vec1;
        public float4 Vec2;
        public float4 Vec3;

        public PackedMatrix(float4x4 matrix)
        {
            Vec1 = new float4(matrix[0][0], matrix[0][1], matrix[0][2], matrix[1][0]);
            Vec2 = new float4(matrix[1][1], matrix[1][2], matrix[2][0], matrix[2][1]);
            Vec3 = new float4(matrix[2][2], matrix[3][0], matrix[3][1], matrix[3][2]);
        }

        public static explicit operator PackedMatrix(float4x4 matrix)
        {
            return new PackedMatrix(matrix);
        }

        public const int c_Size = sizeof(float) * 12;
    }

    public struct AABB
    {
        public float3 Center;
        public float3 Extents;

        public const int c_Size = sizeof(float) * 6;

        public float3 Min { get { return Center - Extents; } }
        public float3 Max { get { return Center + Extents; } }

        public AABB(Bounds bounds)
        {
            Center = bounds.center;
            Extents = bounds.extents;
        }

        public bool Contains(float3 point)
        {
            return !math.any(point < Min | Max < point);
        }

        public bool Contains(AABB b)
        {
            return !math.any(b.Max < Min | Max < b.Min);
        }

        public void SetMinMax(float3 min, float3 max)
        {
            Extents = (max - min) * 0.5f;
            Center = min + Extents;
        }

        public void Encapsulate(float3 point)
        {
            SetMinMax(math.min(Min, point), math.max(Max, point));
        }

        static float3 RotateExtents(float3 extents, float3 m0, float3 m1, float3 m2)
        {
            return math.abs(m0 * extents.x) + math.abs(m1 * extents.y) + math.abs(m2 * extents.z);
        }

        public static AABB Transform(float4x4 transform, AABB localBounds)
        {
            AABB transformed;
            transformed.Extents = RotateExtents(localBounds.Extents, transform.c0.xyz, transform.c1.xyz, transform.c2.xyz);
            transformed.Center = math.transform(transform, localBounds.Center);
            return transformed;
        }

        public float DistanceSq(float3 point)
        {
            return math.lengthsq(math.max(math.abs(point - Center), Extents) - Extents);
        }
    }

    public struct PlanePacket4
    {
        public float4 Xs;
        public float4 Ys;
        public float4 Zs;
        public float4 Distances;

        public const int c_Size = sizeof(float) * 16;
    }

    public struct CullingPlanes
    {
        public UnsafeList<Plane> Planes;
        public UnsafeList<int> Splits;

        public void Dispose()
        {
            Planes.Dispose();
            Splits.Dispose();
        }
    }

    public static class CullingUtility
    {
        public static CullingPlanes CalculateCullingParameters(ref BatchCullingContext cullingContext, Allocator allocator)
        {
            CullingPlanes cullingPlanes = new CullingPlanes();

            NativeArray<Plane> planes = cullingContext.cullingPlanes;
            NativeArray<CullingSplit> splits = cullingContext.cullingSplits;

            cullingPlanes.Planes = new UnsafeList<Plane>(planes.Length, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < planes.Length; ++i)
            {
                cullingPlanes.Planes.Add(planes[i]);
            }

            cullingPlanes.Splits = new UnsafeList<int>(splits.Length, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < splits.Length; ++i)
            {
                cullingPlanes.Splits.Add(splits[i].cullingPlaneCount);
            }

            return cullingPlanes;
        }

        public static UnsafeList<UnsafeList<PlanePacket4>> BuildPlanePackets(ref CullingPlanes cullingPlanes, Allocator allocator)
        {
            UnsafeList<Plane> planes = cullingPlanes.Planes;
            UnsafeList<int> splits = cullingPlanes.Splits;

            UnsafeList<UnsafeList<PlanePacket4>> packedPlanes = new UnsafeList<UnsafeList<PlanePacket4>>(splits.Length, allocator);

            int planeOffset = 0;
            UnsafeList<Plane> splitCullingPlanes = new UnsafeList<Plane>(planes.Length, allocator);

            for (int iSplit = 0; iSplit < splits.Length; ++iSplit)
            {
                int planeCount = splits[iSplit];
                for (int iPlane = 0; iPlane < planeCount; ++iPlane)
                {
                    splitCullingPlanes.Add(planes[planeOffset + iPlane]);
                }

                UnsafeList<PlanePacket4> splitPackedPlanes = BuildSOAPlanePackets(splitCullingPlanes, allocator);
                packedPlanes.Add(splitPackedPlanes);

                planeOffset += planeCount;
                splitCullingPlanes.Clear();
            }

            splitCullingPlanes.Dispose();

            return packedPlanes;
        }

        public static UnsafeList<PlanePacket4> BuildSOAPlanePackets(UnsafeList<Plane> cullingPlanes, Allocator allocator)
        {
            int cullingPlaneCount = cullingPlanes.Length;
            int packetCount = (cullingPlaneCount + 3) >> 2;
            var planes = new UnsafeList<PlanePacket4>(packetCount, allocator, NativeArrayOptions.UninitializedMemory);
            planes.Length = packetCount;

            InitializeSOAPlanePackets(planes, cullingPlanes);

            return planes;
        }

        public static void InitializeSOAPlanePackets(UnsafeList<PlanePacket4> planes, UnsafeList<Plane> cullingPlanes)
        {
            int cullingPlaneCount = cullingPlanes.Length;
            int packetCount = planes.Length;

            for (int i = 0; i < cullingPlaneCount; i++)
            {
                var p = planes[i >> 2];
                p.Xs[i & 3] = cullingPlanes[i].normal.x;
                p.Ys[i & 3] = cullingPlanes[i].normal.y;
                p.Zs[i & 3] = cullingPlanes[i].normal.z;
                p.Distances[i & 3] = cullingPlanes[i].distance;
                planes[i >> 2] = p;
            }

            // Populate the remaining planes with values that are always "in"
            for (int i = cullingPlaneCount; i < 4 * packetCount; ++i)
            {
                var p = planes[i >> 2];
                p.Xs[i & 3] = 1.0f;
                p.Ys[i & 3] = 0.0f;
                p.Zs[i & 3] = 0.0f;

                // This value was before hardcoded to 32786.0f.
                // It was causing the culling system to discard the rendering of entities having a X coordinate approximately less than -32786.
                // We could not find anything relying on this number, so the value has been increased to 1 billion
                p.Distances[i & 3] = 1e9f;

                planes[i >> 2] = p;
            }
        }

        public static IntersectResult Intersect(UnsafeList<PlanePacket4> cullingPlanePackets, AABB a)
        {
            float4 mx = a.Center.xxxx;
            float4 my = a.Center.yyyy;
            float4 mz = a.Center.zzzz;

            float4 ex = a.Extents.xxxx;
            float4 ey = a.Extents.yyyy;
            float4 ez = a.Extents.zzzz;

            int4 outCounts = 0;
            int4 inCounts = 0;

            for (int i = 0; i < cullingPlanePackets.Length; i++)
            {
                var p = cullingPlanePackets[i];
                float4 distances = Dot4(p.Xs, p.Ys, p.Zs, mx, my, mz) + p.Distances;
                float4 radii = Dot4(ex, ey, ez, math.abs(p.Xs), math.abs(p.Ys), math.abs(p.Zs));

                outCounts += (int4)(distances + radii < 0);
                inCounts += (int4)(distances >= radii);
            }

            int inCount = math.csum(inCounts);
            int outCount = math.csum(outCounts);

            if (outCount != 0)
                return IntersectResult.Out;
            else
                return (inCount == 4 * cullingPlanePackets.Length) ? IntersectResult.In : IntersectResult.Partial;
        }

        public static IntersectResult IntersectNoPartial(UnsafeList<PlanePacket4> cullingPlanePackets, AABB a)
        {
            float4 mx = a.Center.xxxx;
            float4 my = a.Center.yyyy;
            float4 mz = a.Center.zzzz;

            float4 ex = a.Extents.xxxx;
            float4 ey = a.Extents.yyyy;
            float4 ez = a.Extents.zzzz;

            int4 masks = 0;

            for (int i = 0; i < cullingPlanePackets.Length; i++)
            {
                var p = cullingPlanePackets[i];
                float4 distances = Dot4(p.Xs, p.Ys, p.Zs, mx, my, mz) + p.Distances;
                float4 radii = Dot4(ex, ey, ez, math.abs(p.Xs), math.abs(p.Ys), math.abs(p.Zs));

                masks += (int4)(distances + radii <= 0);
            }

            int outCount = math.csum(masks);
            return outCount > 0 ? IntersectResult.Out : IntersectResult.In;
        }

        private static float4 Dot4(float4 xs, float4 ys, float4 zs, float4 mx, float4 my, float4 mz)
        {
            return xs * mx + ys * my + zs * mz;
        }
    }
}