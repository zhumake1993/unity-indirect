using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StoreForRef
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

//var resizeJob = new ResizeJob
//{
//    PingCoordsRef = pingCoordsRef,
//    PongCoordsRef = pongCoordsRef,
//    VisibleCoordsRef = visibleCoordsRef,
//    PartialCoordsRef = partialCoordsRef,
//};

//var cullQuadTreeJob = new CullQuadTreeJob
//{
//    PackedPlanes = packedPlanes,
//    PingCoordsRef = pingCoordsRef,
//    PongCoordsRef = pongCoordsRef,
//    VisibleCoordsRef = visibleCoordsRef,
//    PartialCoordsRef = partialCoordsRef,
//    Lod = 0,
//    LodNodeSize = 0,
//    Setting = _unmanaged->Setting
//};

//var switchPingPongJob = new ClearAndSwitchJob
//{
//    PingCoordsRef = pingCoordsRef,
//    PongCoordsRef = pongCoordsRef,
//};

//for (int lod = _unmanaged->Setting.MaxLod; lod >= 0; --lod)
//{
//    cullQuadTreeJob.Lod = lod;
//    cullQuadTreeJob.LodNodeSize = math.pow(2, lod) * _unmanaged->Setting.Lod0NodeSize;

//    jobHandle = resizeJob.Schedule(jobHandle);
//    jobHandle = cullQuadTreeJob.Schedule((int*)((byte*)pingCoordsRef.GetUnsafePtr() + sizeof(void*)), 16, jobHandle);

//    if (lod > 0)
//    {
//        jobHandle = switchPingPongJob.Schedule(jobHandle);
//    }
//}

//#if ENABLE_BURST
//        [BurstCompile(CompileSynchronously = true)]
//#endif
//        public unsafe struct CullQuadTreeSingleJob : IJob
//        {
//            [ReadOnly]
//            public UnsafeList<PlanePacket4> PackedPlanes;
//            public UnsafeRef<UnsafeList<int2>> PingCoordsRef;
//            public UnsafeRef<UnsafeList<int2>> PongCoordsRef;
//            public UnsafeRef<UnsafeList<int4>> VisibleCoordsRef;
//            public UnsafeRef<UnsafeList<int4>> PartialCoordsRef;
//            [ReadOnly]
//            public QuadTreeSetting Setting;

//            public void Execute()
//            {
//                ref UnsafeList<int2> pingCoords = ref PingCoordsRef.RefValue;
//                ref UnsafeList<int2> pongCoords = ref PongCoordsRef.RefValue;
//                ref UnsafeList<int4> visibleCoords = ref VisibleCoordsRef.RefValue;
//                ref UnsafeList<int4> partialCoords = ref PartialCoordsRef.RefValue;

//                for (int lod = Setting.MaxLod; lod >= 0; --lod)
//                {
//                    float lodNodeSize = math.pow(2, lod) * Setting.Lod0NodeSize;

//                    for (int i = 0; i < pingCoords.Length; ++i)
//                    {
//                        int2 coord = pingCoords[i];

//                        AABB aabb;
//                        aabb.Center.x = Setting.WorldOrigin.x + coord.x * lodNodeSize + 0.5f * lodNodeSize;
//                        aabb.Center.y = Setting.WorldOrigin.y + 0.5f * Setting.NodeHeight;
//                        aabb.Center.z = Setting.WorldOrigin.z + coord.y * lodNodeSize + 0.5f * lodNodeSize;
//                        aabb.Extents = new float3(lodNodeSize, Setting.NodeHeight, lodNodeSize) * 0.5f;

//                        IntersectResult result = CullingUtility.Intersect(PackedPlanes, aabb);
//                        if (result == IntersectResult.In)
//                        {
//                            visibleCoords.Add(new int4(coord.x, 0, coord.y, lod));
//                        }
//                        else if (result == IntersectResult.Partial)
//                        {
//                            partialCoords.Add(new int4(coord.x, 0, coord.y, lod));

//                            if (lod > 0)
//                            {
//                                pongCoords.Add(new int2(coord.x * 2, coord.y * 2));
//                                pongCoords.Add(new int2(coord.x * 2 + 1, coord.y * 2));
//                                pongCoords.Add(new int2(coord.x * 2, coord.y * 2 + 1));
//                                pongCoords.Add(new int2(coord.x * 2 + 1, coord.y * 2 + 1));
//                            }
//                        }
//                    }

//                    if (lod > 0)
//                    {
//                        pingCoords.Clear();

//                        var temp = pingCoords;
//                        pingCoords = pongCoords;
//                        pongCoords = temp;
//                    }
//                }
//            }
//        }

//#if ENABLE_BURST
//        [BurstCompile(CompileSynchronously = true)]
//#endif
//        public unsafe struct ResizeJob : IJob
//        {
//            public UnsafeRef<UnsafeList<int2>> PingCoordsRef;
//            public UnsafeRef<UnsafeList<int2>> PongCoordsRef;
//            public UnsafeRef<UnsafeList<int4>> VisibleCoordsRef;
//            public UnsafeRef<UnsafeList<int4>> PartialCoordsRef;

//            public void Execute()
//            {
//                ref UnsafeList<int2> pingCoords = ref PingCoordsRef.RefValue;
//                ref UnsafeList<int2> pongCoords = ref PongCoordsRef.RefValue;
//                ref UnsafeList<int4> visibleCoords = ref VisibleCoordsRef.RefValue;
//                ref UnsafeList<int4> partialCoords = ref PartialCoordsRef.RefValue;

//                pongCoords.Capacity = pingCoords.Length * 4;
//                visibleCoords.Capacity += pingCoords.Length;
//                partialCoords.Capacity += pingCoords.Length;
//            }
//        }

//#if ENABLE_BURST
//        [BurstCompile(CompileSynchronously = true)]
//#endif
//        public unsafe struct CullQuadTreeJob : IJobParallelForDefer
//        {
//            [ReadOnly]
//            public UnsafeList<PlanePacket4> PackedPlanes;
//            public UnsafeRef<UnsafeList<int2>> PingCoordsRef;
//            public UnsafeRef<UnsafeList<int2>> PongCoordsRef;
//            public UnsafeRef<UnsafeList<int4>> VisibleCoordsRef;
//            public UnsafeRef<UnsafeList<int4>> PartialCoordsRef;
//            public int Lod;
//            public float LodNodeSize;
//            [ReadOnly]
//            public QuadTreeSetting Setting;

//            public void Execute(int index)
//            {
//                ref UnsafeList<int2> pingCoords = ref PingCoordsRef.RefValue;
//                ref UnsafeList<int2> pongCoords = ref PongCoordsRef.RefValue;
//                ref UnsafeList<int4> visibleCoords = ref VisibleCoordsRef.RefValue;
//                ref UnsafeList<int4> partialCoords = ref PartialCoordsRef.RefValue;

//                int2 coord = pingCoords[index];

//                AABB aabb;
//                aabb.Center.x = Setting.WorldOrigin.x + coord.x * LodNodeSize + 0.5f * LodNodeSize;
//                aabb.Center.y = Setting.WorldOrigin.y + 0.5f * Setting.NodeHeight;
//                aabb.Center.z = Setting.WorldOrigin.z + coord.y * LodNodeSize + 0.5f * LodNodeSize;
//                aabb.Extents = new float3(LodNodeSize, Setting.NodeHeight, LodNodeSize) * 0.5f;

//                IntersectResult result = CullingUtility.Intersect(PackedPlanes, aabb);
//                if (result == IntersectResult.In)
//                {
//                    visibleCoords.AsParallelWriter().AddNoResize(new int4(coord.x, 0, coord.y, Lod));
//                }
//                else if (result == IntersectResult.Partial)
//                {
//                    partialCoords.AsParallelWriter().AddNoResize(new int4(coord.x, 0, coord.y, Lod));

//                    if (Lod > 0)
//                    {
//                        var writer = pongCoords.AsParallelWriter();
//                        writer.AddNoResize(new int2(coord.x * 2, coord.y * 2));
//                        writer.AddNoResize(new int2(coord.x * 2 + 1, coord.y * 2));
//                        writer.AddNoResize(new int2(coord.x * 2, coord.y * 2 + 1));
//                        writer.AddNoResize(new int2(coord.x * 2 + 1, coord.y * 2 + 1));
//                    }
//                }
//            }
//        }

//DynamicRenderManager
