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
        public int UnitMeshTriangleCount;

        public int InstanceCapacity;
        public int BatchCapacity;
        public int IndexSegmentCapacity;

        public QuadTreeSetting QuadTreeSetting;

        public UInt32 MinInstanceCountPerCmd;
        public UInt32 MaxInstanceCountPerCmd;
        public UInt32 NumMaxInstanceCountPerCmd;

        public UInt32 InstanceDataMinSizeBytes;
        public UInt32 InstanceDataMaxSizeBytes;
        public UInt32 InstanceDataNumMaxSizeBlocks;
    }

    public struct QuadTreeSetting
    {
        public int3 WorldOrigin;
        public int3 MaxLodRange;
        public int MaxLod;
    }

    public class ComputerShaderCollection
    {
        public ComputeShader AdjustDispatchArgCS;
        public ComputeShader QuadTreeBuildCS;
        public ComputeShader PopulateInstanceIndexCS;
        public ComputeShader QuadTreeCullingCS;
        public ComputeShader FrustumCullingCS;
        public ComputeShader PopulateVisibilityAndIndirectArgCS;
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

        public const int c_SizeF4 = 6;
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
                ^ UV3.GetHashCode();
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
                && UV3.Equals(other.UV3);
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

    public struct MeshKey : IEquatable<MeshKey>
    {
        public Mesh Mesh;
        public int SubmeshIndex;
        public bool FlipZ;

        public static MeshKey s_Invalid = new MeshKey { Mesh = null, SubmeshIndex = 0, FlipZ = false };

        public override int GetHashCode()
        {
            return Mesh.GetHashCode()
                ^ SubmeshIndex.GetHashCode()
                ^ FlipZ.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is MeshKey)
                return Equals((MeshKey)obj);

            return false;
        }

        public bool Equals(MeshKey other)
        {
            return Mesh == other.Mesh
                && SubmeshIndex == other.SubmeshIndex
                && FlipZ == other.FlipZ;
        }

        public static bool operator ==(MeshKey a, MeshKey b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(MeshKey a, MeshKey b)
        {
            return !a.Equals(b);
        }
    }

    public struct MeshInfo
    {
        public MeshKey MeshKey; // need???
        public List<UnitMeshInfo> UnitMeshInfos;

        public static MeshInfo s_Invalid = new MeshInfo { MeshKey = MeshKey.s_Invalid, UnitMeshInfos = null };
        public bool IsValid => MeshKey != MeshKey.s_Invalid && UnitMeshInfos != null && UnitMeshInfos.Count > 0;
    }

    public struct UnitMeshInfo
    {
        public int IndexOffset;
        public int VertexOffset;
        public int VertexCount;
        public AABB AABB;
    }

    public struct MeshSliceInfo : IEquatable<MeshSliceInfo>
    {
        public uint IndexCountPerInstance;
        public uint StartIndex;
        public uint BaseVertexIndex;

        public static MeshSliceInfo s_Invalid = new MeshSliceInfo { IndexCountPerInstance = 0, StartIndex = 0, BaseVertexIndex = 0 };

        public override int GetHashCode()
        {
            return IndexCountPerInstance.GetHashCode()
                ^ StartIndex.GetHashCode()
                ^ BaseVertexIndex.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is MeshSliceInfo)
                return Equals((MeshSliceInfo)obj);

            return false;
        }

        public bool Equals(MeshSliceInfo other)
        {
            return IndexCountPerInstance == other.IndexCountPerInstance
                && StartIndex == other.StartIndex
                && BaseVertexIndex == other.BaseVertexIndex;
        }

        public static bool operator ==(MeshSliceInfo a, MeshSliceInfo b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(MeshSliceInfo a, MeshSliceInfo b)
        {
            return !a.Equals(b);
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
        public int MaterialID;
        public byte Layer;
        public bool ReceiveShadows;
        public ShadowCastingMode ShadowCastingMode;

        public override int GetHashCode()
        {
            return MaterialID.GetHashCode()
                ^ Layer.GetHashCode()
                ^ ReceiveShadows.GetHashCode()
                ^ ((int)(ShadowCastingMode)).GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is IndirectKey)
                return Equals((IndirectKey)obj);

            return false;
        }

        public bool Equals(IndirectKey other)
        {
            return MaterialID == other.MaterialID
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
        public int ActualInstanceCount;
    }

    public struct IndirectCmdInfo
    {
        public IndirectKey IndirectKey;
        public int MeshID;
        public int InstanceCount;
        public Chunk InstanceDataChunk;
        public UnsafeList<IndirectSubCmdInfo> SubCmds;
    }

    public struct IndirectSubCmdInfo
    {
        public int StartInstanceIndex;
        public Chunk InstanceIndicesChunk;
    }

    public struct InstanceDescriptor
    {
        public float4 Center_IndirectID;
        public float4 Extents_DataOffset;
        public int4 UnitMeshInfo;
        public int4 QuadTreeNodeCoordAndLod;
        public int4 QuadTreeSubNodeMask;

        public const int c_SizeF4 = 5;
        public const int c_Size = c_SizeF4 * 16;
    }
}