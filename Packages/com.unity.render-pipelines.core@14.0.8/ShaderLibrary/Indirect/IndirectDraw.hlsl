#ifndef ZGAME_INDIRECT_DRAW
#define ZGAME_INDIRECT_DRAW

#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"

#include "IndirectDescriptor.hlsl"

struct IndirectVertexData
{
    float4 position;
    float4 normal;
    float4 tangent;
    float4 color;
    float2 uv0;
    float2 uv1;
    float2 uv2;
    float2 uv3;
    float2 uv4;
    float2 uv5;
    float2 uv6;
    float2 uv7;
};

StructuredBuffer<MeshletDescriptor> MeshletDescriptorBuffer;
StructuredBuffer<BatchDescriptor> BatchDescriptorBuffer;
ByteAddressBuffer InstanceDataBuffer;

static uint gZGameVertexID;
static uint gZGameInstanceID;
static MeshletDescriptor gZGameMeshletDescriptor;
static float4x4 gZGameWorldMatrix;

StructuredBuffer<int4> VisibilityBuffer;
StructuredBuffer<int> IndirectIndexBuffer;
StructuredBuffer<IndirectVertexData> IndirectVertexBuffer;

float4x4 ZGame_Indirect_Load_Matrix()
{
    int offset = gZGameMeshletDescriptor.dataOffset;
    
    float4 p1 = asfloat(InstanceDataBuffer.Load4(offset + 0 * 16));
    float4 p2 = asfloat(InstanceDataBuffer.Load4(offset + 1 * 16));
    float4 p3 = asfloat(InstanceDataBuffer.Load4(offset + 2 * 16));

    return float4x4(
        p1.x, p1.w, p2.z, p3.y,
        p1.y, p2.x, p2.w, p3.z,
        p1.z, p2.y, p3.x, p3.w,
        0.0,  0.0,  0.0,  1.0
    );
}

void ZGmae_Indirect_Setup(uint svVertexID, uint svInstanceID)
{
    gZGameVertexID = svVertexID;
    gZGameInstanceID = svInstanceID;

    int indirectID = GetCommandID(0);
    int indexOffset = BatchDescriptorBuffer[indirectID].offset;
    int meshletIndex = VisibilityBuffer[indexOffset + gZGameInstanceID].x;
    gZGameMeshletDescriptor = MeshletDescriptorBuffer[meshletIndex];

    gZGameWorldMatrix = ZGame_Indirect_Load_Matrix();
}

IndirectVertexData ZGame_Indirect_Get_IndirectVertexData()
{
    int indexOffset = gZGameMeshletDescriptor.indexOffset;
    int vertexOffset = gZGameMeshletDescriptor.vertexOffset;

    int index = IndirectIndexBuffer[indexOffset + gZGameVertexID];
    IndirectVertexData vertexData = IndirectVertexBuffer[vertexOffset + index];

    return vertexData;
}

float4 ZGmae_Indirect_Get_Float4(int index)
{
    int offset = gZGameMeshletDescriptor.dataOffset;
    int needInverse = gZGameMeshletDescriptor.needInverse;
    offset += 3 * 16 + 3 * 16 * needInverse + index * 16;
    
    return asfloat(InstanceDataBuffer.Load4(offset));
}

#undef UNITY_MATRIX_M
#define UNITY_MATRIX_M gZGameWorldMatrix

#endif