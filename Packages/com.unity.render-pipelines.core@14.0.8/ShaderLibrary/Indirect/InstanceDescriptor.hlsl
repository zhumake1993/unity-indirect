#ifndef ZGAME_INSTANCE_DESCRIPTOR
#define ZGAME_INSTANCE_DESCRIPTOR

struct InstanceDescriptor
{
    float4 center_indirectID;
    float4 extents_dataOffset;
    int4 unitMeshInfo;
    int4 quadTreeNodeCoordAndLod;
    int4 quadTreeSubNodeMask;
};

StructuredBuffer<InstanceDescriptor> InstanceDescriptorBuffer;

#endif