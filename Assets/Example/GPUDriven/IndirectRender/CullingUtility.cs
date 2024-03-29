using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace ZGame.IndirectExample
{
    public struct PlanePacket4
    {
        public float4 Xs;
        public float4 Ys;
        public float4 Zs;
        public float4 Distances;

        public const int c_Size = sizeof(float) * 16;
    }

    public enum IntersectResult
    {
        Out,
        In,
        Partial
    };

    public static class CullingUtility
    {
        public static NativeArray<PlanePacket4> BuildSOAPlanePackets(NativeArray<Plane> cullingPlanes, Allocator allocator)
        {
            int cullingPlaneCount = cullingPlanes.Length;
            int packetCount = (cullingPlaneCount + 3) >> 2;
            var planes = new NativeArray<PlanePacket4>(packetCount, allocator, NativeArrayOptions.UninitializedMemory);

            InitializeSOAPlanePackets(planes, cullingPlanes);

            return planes;
        }

        //public static float4[] BuildSOAPlanePackets(Plane[] cullingPlanes)
        //{
        //    int cullingPlaneCount = cullingPlanes.Length;
        //    int packetCount = (cullingPlaneCount + 3) >> 2;
        //    var planes = new float4[packetCount * 4];

        //    InitializeSOAPlanePackets(planes, cullingPlanes);

        //    return planes;
        //}

        public static void InitializeSOAPlanePackets(NativeArray<PlanePacket4> planes, NativeArray<Plane> cullingPlanes)
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

        //public static void InitializeSOAPlanePackets(float4[] packedPlanes, Plane[] cullingPlanes)
        //{
        //    int cullingPlaneCount = cullingPlanes.Length;
        //    int packetCount = packedPlanes.Length / 4;

        //    for (int i = 0; i < cullingPlaneCount; i++)
        //    {
        //        packedPlanes[i >> 2 + 0][i & 3] = cullingPlanes[i].normal.x;

        //        var p = planes[i >> 2];
        //        p.Xs[i & 3] = cullingPlanes[i].normal.x;
        //        p.Ys[i & 3] = cullingPlanes[i].normal.y;
        //        p.Zs[i & 3] = cullingPlanes[i].normal.z;
        //        p.Distances[i & 3] = cullingPlanes[i].distance;
        //        planes[i >> 2] = p;
        //    }

        //    // Populate the remaining planes with values that are always "in"
        //    for (int i = cullingPlaneCount; i < 4 * packetCount; ++i)
        //    {
        //        var p = planes[i >> 2];
        //        p.Xs[i & 3] = 1.0f;
        //        p.Ys[i & 3] = 0.0f;
        //        p.Zs[i & 3] = 0.0f;

        //        // This value was before hardcoded to 32786.0f.
        //        // It was causing the culling system to discard the rendering of entities having a X coordinate approximately less than -32786.
        //        // We could not find anything relying on this number, so the value has been increased to 1 billion
        //        p.Distances[i & 3] = 1e9f;

        //        planes[i >> 2] = p;
        //    }
        //}
    }

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
}