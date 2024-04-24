#ifndef ZGAME_INDIRECT_DRAW
#define ZGAME_INDIRECT_DRAW

#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"

#include "InstanceDescriptor.hlsl"

//#define kMaxCullingSet 5

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
};

static uint gZGameVertexID;
static uint gZGameInstanceID;
static InstanceDescriptor gZGameInstanceDescriptor;
static float4x4 gZGameWorldMatrix;

// x: instance offset
StructuredBuffer<int4> BatchDescriptorBuffer;

ByteAddressBuffer InstanceDataBuffer;

// x: visible index
StructuredBuffer<int4> VisibilityBuffer;

StructuredBuffer<int> IndirectIndexBuffer;
StructuredBuffer<IndirectVertexData> IndirectVertexBuffer;

float4x4 ZGame_Indirect_Load_Matrix()
{
    int offset = gZGameInstanceDescriptor.extents_dataOffset.w;
    
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
    int instanceOffset = BatchDescriptorBuffer[indirectID].x;
    int instanceIndex = VisibilityBuffer[instanceOffset + gZGameInstanceID].x;
    gZGameInstanceDescriptor = InstanceDescriptorBuffer[instanceIndex];

    gZGameWorldMatrix = ZGame_Indirect_Load_Matrix();
}

IndirectVertexData ZGame_Indirect_Get_IndirectVertexData()
{
    int indexOffset = gZGameInstanceDescriptor.unitMeshInfo.x;
    int vertexOffset = gZGameInstanceDescriptor.unitMeshInfo.y;

    int index = IndirectIndexBuffer[indexOffset + gZGameVertexID];
    IndirectVertexData vertexData = IndirectVertexBuffer[vertexOffset + index];

    return vertexData;
}

float4 ZGmae_Indirect_Get_Float4(int index)
{
    int offset = gZGameInstanceDescriptor.extents_dataOffset.w;
    int needInverse = gZGameInstanceDescriptor.unitMeshInfo.z;
    offset += 3 * 16 + 3 * 16 * needInverse + index * 16;
    
    return asfloat(InstanceDataBuffer.Load4(offset));
}

#undef UNITY_MATRIX_M
#define UNITY_MATRIX_M gZGameWorldMatrix

#endif