#ifndef ZGAME_FRUSTUM_CULLING_FUNCTION
#define ZGAME_FRUSTUM_CULLING_FUNCTION

#define kMaxPackedPlaneCount 16

#define kCullingResultIn 0x00000000
#define kCullingResultOut 0xFFFFFFFF
#define kCullingResultPartial 0x00000001

struct PlanePacket4
{
    float4 xs;
    float4 ys;
    float4 zs;
    float4 distances;
};

struct AABB
{
    float3 center;
    float3 extent;
};

int4 _CullingParameters;
float4 _PackedPlanes[kMaxPackedPlaneCount * 4];

float4 Dot4(float4 xs, float4 ys, float4 zs, float4 mx, float4 my, float4 mz)
{
    return xs * mx + ys * my + zs * mz;
}

uint FrustumCull(AABB aabb)
{
    float4 mx = aabb.center.xxxx;
    float4 my = aabb.center.yyyy;
    float4 mz = aabb.center.zzzz;

    float4 ex = aabb.extent.xxxx;
    float4 ey = aabb.extent.yyyy;
    float4 ez = aabb.extent.zzzz;

    int4 outCounts = 0;
    int4 inCounts = 0;
    
    for (int i = 0; i < _CullingParameters.x; i++)
    {
        PlanePacket4 p;
        p.xs = _PackedPlanes[i * 4 + 0];
        p.ys = _PackedPlanes[i * 4 + 1];
        p.zs = _PackedPlanes[i * 4 + 2];
        p.distances = _PackedPlanes[i * 4 + 3];
        
        float4 distances = Dot4(p.xs, p.ys, p.zs, mx, my, mz) + p.distances;
        float4 radii = Dot4(ex, ey, ez, abs(p.xs), abs(p.ys), abs(p.zs));

        outCounts += (int4)(distances + radii < 0);
        inCounts += (int4)(distances >= radii);
    }
    
    int inCount = inCounts.x + inCounts.y + inCounts.z + inCounts.w;
    int outCount = outCounts.x + outCounts.y + outCounts.z + outCounts.w;

    if (outCount != 0)
        return kCullingResultOut;
    else
        return (inCount == 4 * _CullingParameters.x) ? kCullingResultIn : kCullingResultPartial;
}

uint FrustumCullNoPartial(AABB aabb)
{
    float4 mx = aabb.center.xxxx;
    float4 my = aabb.center.yyyy;
    float4 mz = aabb.center.zzzz;

    float4 ex = aabb.extent.xxxx;
    float4 ey = aabb.extent.yyyy;
    float4 ez = aabb.extent.zzzz;

    int4 masks = 0;

    for (int i = 0; i < _CullingParameters.x; i++)
    {
        PlanePacket4 p;
        p.xs = _PackedPlanes[i * 4 + 0];
        p.ys = _PackedPlanes[i * 4 + 1];
        p.zs = _PackedPlanes[i * 4 + 2];
        p.distances = _PackedPlanes[i * 4 + 3];

        float4 distances = Dot4(p.xs, p.ys, p.zs, mx, my, mz) + p.distances;
        float4 radii = Dot4(ex, ey, ez, abs(p.xs), abs(p.ys), abs(p.zs));

        masks += (int4)(distances + radii <= 0);
    }

    int outCount = masks.x + masks.y + masks.z + masks.w;
    
    return (outCount > 0) ? kCullingResultOut : kCullingResultIn;
}

#endif