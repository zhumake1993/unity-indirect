using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public struct IndirectRenderSetting
    {
        public int IndexCapacity;
        public int VertexCapacity;
        public int MeshletTriangleCount;

        public int InstanceCapacity;
        public int MeshletCapacity;
        public int CmdCapacity;
        public int BatchCapacity;

        public QuadTreeSetting QuadTreeSetting;

        public UInt32 InstanceIndexMinCount;
        public UInt32 InstanceIndexMaxCount;
        public UInt32 MeshletIndexMinCount;
        public UInt32 MeshletIndexMaxCount;
        public UInt32 InstanceDataMinSizeBytes;
        public UInt32 InstanceDataMaxSizeBytes;
    }

    public struct QuadTreeSetting
    {
        public int MaxLod;
        public int Lod0NodeSize;
        public int NodeHeight;
        public int3 WorldOrigin;
        public int3 MaxLodRange;
    }

    public struct IndirectVertexData : IEquatable<IndirectVertexData>
    {
        public float4 Position;
        public float4 Normal;
        public float4 Tangent;
        public float4 Color;
        public float2 UV0;
        public float2 UV1;
        public float2 UV2;
        public float2 UV3;
        public float2 UV4;
        public float2 UV5;
        public float2 UV6;
        public float2 UV7;

        public const int c_SizeF4 = 8;
        public const int c_Size = c_SizeF4 * 16;

        public override int GetHashCode()
        {
            return Position.GetHashCode()
                ^ Normal.GetHashCode()
                ^ Tangent.GetHashCode()
                ^ Color.GetHashCode()
                ^ UV0.GetHashCode()
                ^ UV1.GetHashCode()
                ^ UV2.GetHashCode()
                ^ UV3.GetHashCode()
                ^ UV4.GetHashCode()
                ^ UV5.GetHashCode()
                ^ UV6.GetHashCode()
                ^ UV7.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is IndirectVertexData)
                return Equals((IndirectVertexData)obj);

            return false;
        }

        public bool Equals(IndirectVertexData other)
        {
            return Position.Equals(other.Position)
                && Normal.Equals(other.Normal)
                && Tangent.Equals(other.Tangent)
                && Color.Equals(other.Color)
                && UV0.Equals(other.UV0)
                && UV1.Equals(other.UV1)
                && UV2.Equals(other.UV2)
                && UV3.Equals(other.UV3)
                && UV4.Equals(other.UV4)
                && UV5.Equals(other.UV5)
                && UV6.Equals(other.UV6)
                && UV7.Equals(other.UV7);
        }

        public static bool operator ==(IndirectVertexData a, IndirectVertexData b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(IndirectVertexData a, IndirectVertexData b)
        {
            return !a.Equals(b);
        }
    }

    public struct MeshletInfo
    {
        public int IndexOffset;
        public int VertexOffset;
        public int VertexCount;
        public AABB AABB;
        public Chunk IndexChunk;
        public Chunk VertexChunk;
    }

    public struct SubMeshInfo
    {
        public UnsafeList<MeshletInfo> MeshletInfos;
        public AABB AABB;

        public int MeshletLength => MeshletInfos.Length;
        public static SubMeshInfo s_Invalid = new SubMeshInfo { MeshletInfos = new UnsafeList<MeshletInfo> { Ptr = null } };
        public bool IsValid => MeshletInfos.IsCreated && MeshletInfos.Length > 0;

        public void Dispose()
        {
            if (MeshletInfos.IsCreated)
                MeshletInfos.Dispose();
        }
    }

    public struct MeshInfo
    {
        public UnsafeList<SubMeshInfo> SubMeshInfos;

        public int SubMeshCount => SubMeshInfos.Length;
        public static MeshInfo s_Invalid = new MeshInfo { SubMeshInfos = new UnsafeList<SubMeshInfo> { Ptr = null } };
        public bool IsValid => SubMeshInfos.IsCreated && SubMeshInfos.Length > 0;

        public void Dispose()
        {
            if (SubMeshInfos.IsCreated)
            {
                foreach (var subMeshInfo in SubMeshInfos)
                    subMeshInfo.Dispose();
                SubMeshInfos.Dispose();
            }
        }
    }

    public struct ShaderLayout
    {
        public bool NeedInverse;
        public int PeopertyCount;

        public static ShaderLayout s_Invalid = new ShaderLayout { NeedInverse = false, PeopertyCount = -1 };

        public int GetInstanceSizeF4()
        {
            return 3 + (NeedInverse ? 3 : 0) + PeopertyCount;
        }
    }

    public struct IndirectKey : IEquatable<IndirectKey>
    {
        public int MeshID;
        public int SubmeshIndex;
        public int MaterialID;
        public byte Layer;
        public bool ReceiveShadows;
        public ShadowCastingMode ShadowCastingMode;

        int _hashCache;

        public override int GetHashCode()
        {
            if (_hashCache != 0)
                return _hashCache;

            unchecked
            {
                int retHash = (int)(2166136261);
                retHash = (retHash * 16777619) ^ MeshID.GetHashCode();
                retHash = (retHash * 16777619) ^ SubmeshIndex.GetHashCode();
                retHash = (retHash * 16777619) ^ MaterialID.GetHashCode();
                retHash = (retHash * 16777619) ^ Layer.GetHashCode();
                retHash = (retHash * 16777619) ^ ReceiveShadows.GetHashCode();
                retHash = (retHash * 16777619) ^ ((int)(ShadowCastingMode)).GetHashCode();
                _hashCache = retHash;
                return _hashCache;
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is IndirectKey)
                return Equals((IndirectKey)obj);

            return false;
        }

        public bool Equals(IndirectKey other)
        {
            return MeshID == other.MeshID
                && SubmeshIndex == other.SubmeshIndex
                && MaterialID == other.MaterialID
                && Layer == other.Layer
                && ReceiveShadows == other.ReceiveShadows
                && ShadowCastingMode == other.ShadowCastingMode;
        }

        public static bool operator ==(IndirectKey a, IndirectKey b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(IndirectKey a, IndirectKey b)
        {
            return !a.Equals(b);
        }
    }

    public struct IndirectBatch
    {
        public int IndirectID;
        public int MeshletCount;
    }

    public struct IndirectCmd
    {
        public UnsafeList<SubMeshInfo> SubMeshInfos;
        public UnsafeList<IndirectKey> IndirectKeys;
        public int InstanceCount;
        public Chunk InstanceIndexChunk;
        public Chunk InstanceDataChunk;
        public UnsafeList<Chunk> MeshletIndexChunks;

        public int LodNum => SubMeshInfos.Length;
        public int MaxLod => SubMeshInfos.Length - 1;

        public void Dispose()
        {
            SubMeshInfos.Dispose();
            IndirectKeys.Dispose();
            MeshletIndexChunks.Dispose();
        }
    }

    public struct InstanceDescriptor
    {
        public float3 Center;
        public int CmdID;

        public float3 Extents;
        public int Pad;

        public const int c_SizeF4 = 2;
        public const int c_Size = c_SizeF4 * 16;
    }

    public struct MeshletDescriptor
    {
        public float3 Center;
        public int IndirectID;

        public float3 Extents;
        public int DataOffset;

        public int IndexOffset;
        public int VertexOffset;
        public int NeedInverse;
        public int Pad;

        public const int c_SizeF4 = 3;
        public const int c_Size = c_SizeF4 * 16;
    }

    public struct CmdDescriptor
    {
        public int InstanceStartIndex;
        public int InstanceCount;
        public int MaxLod;
        public int Pad;

        public int4 MeshletStartIndices;

        public int4 MeshletLengths;

        public float4 LodParam;

        public const int c_SizeF4 = 4;
        public const int c_Size = c_SizeF4 * 16;
    }

    public struct BatchDescriptor
    {
        public int Offset;
        public int3 Pad;

        public const int c_SizeF4 = 1;
        public const int c_Size = c_SizeF4 * 16;
    }

    public struct QuadTreeAABBInfo
    {
        public int Index;
        public AABB AABB;
        public int4 Coord;
    }

    public struct OffsetSizeF4
    {
        public int OffsetF4;
        public int SizeF4;
    }

    public struct OffsetSize
    {
        public int Offset;
        public int Size;
    }
}