#ifndef ZGAME_MESHLET_DESCRIPTOR
#define ZGAME_MESHLET_DESCRIPTOR

struct InstanceDescriptor
{
    float3 center;
    int cmdID;
    
    float3 extents;
    int pad;
};

struct MeshletDescriptor
{
    float3 center;
    int indirectID;
    
    float3 extents;
    int dataOffset;
    
    int indexOffset;
    int vertexOffset;
    int needInverse;
    int pad;
};

struct CmdDescriptor
{
    int instanceStartIndex;
    int instanceCount;
    int maxLod;
    int pad;
    
    int4 meshletStartIndices;

    int4 meshletLengths;

    float4 lodParam;
};

struct BatchDescriptor
{
    int offset;
    int3 pad;
};

#endif