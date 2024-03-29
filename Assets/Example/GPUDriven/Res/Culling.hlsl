#ifndef ZGAME_CULLING
#define ZGAME_CULLING

struct AABB
{
    float3 center;
    float3 extent;
};

struct PlanePacket4
{
    float4 Xs;
    float4 Ys;
    float4 Zs;
    float4 Distances;
};

int4 cullingParameter;
StructuredBuffer<PlanePacket4> PackedPlaneBuffer;

float4 Dot4(float4 xs, float4 ys, float4 zs, float4 mx, float4 my, float4 mz)
{
    return xs * mx + ys * my + zs * mz;
}

// true for out
bool IntersectNoPartial(AABB aabb)
{
    float4 mx = aabb.center.xxxx;
    float4 my = aabb.center.yyyy;
    float4 mz = aabb.center.zzzz;

    float4 ex = aabb.extent.xxxx;
    float4 ey = aabb.extent.yyyy;
    float4 ez = aabb.extent.zzzz;

    int4 masks = 0;

    for (int i = 0; i < cullingParameter.x; i++)
    {
        PlanePacket4 p = PackedPlaneBuffer[i];
        float4 distances = Dot4(p.Xs, p.Ys, p.Zs, mx, my, mz) + p.Distances;
        float4 radii = Dot4(ex, ey, ez, abs(p.Xs), abs(p.Ys), abs(p.Zs));

        masks += (int4)(distances + radii <= 0);
    }

    int outCount = masks.x + masks.y + masks.z + masks.w;
    return outCount > 0;
}

// true for out
bool Cull(AABB aabb)
{
    if(IntersectNoPartial(aabb))
        return true;

    return false;
}

#endif