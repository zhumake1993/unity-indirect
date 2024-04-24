#define ENABLE_BURST

using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace ZGame.Indirect
{
    public unsafe struct QuadTree
    {
        public struct Unmanaged
        {
            public QuadTreeSetting Setting;

            public NativeParallelHashMap<int4, UnsafeList<int4>> Map;
            public UnsafeList<int4> Outsiders;
            public UnsafeHashMap<int, int4> IndexToCoord;

            public bool QuadTreeCull;
            public bool UseSingleJob;

            public void Init(IndirectRenderSetting setting)
            {
                Setting = setting.QuadTreeSetting;

                Map = new NativeParallelHashMap<int4, UnsafeList<int4>>(1024, Allocator.Persistent);
                Outsiders = new UnsafeList<int4>(1024, Allocator.Persistent);
                IndexToCoord = new UnsafeHashMap<int, int4>(1024, Allocator.Persistent);

                QuadTreeCull = true;
                UseSingleJob = true;
            }

            public void Dispose()
            {
                foreach (var pair in Map)
                    pair.Value.Dispose();
                Map.Dispose();

                Outsiders.Dispose();
                IndexToCoord.Dispose();
            }
        }

        Unmanaged* _unmanaged;

        public void Init(IndirectRenderSetting setting)
        {
            _unmanaged = MemoryUtility.Malloc<Unmanaged>(Allocator.Persistent);
            _unmanaged->Init(setting);
        }

        public void Dispose()
        { 
            _unmanaged->Dispose();
            MemoryUtility.Free(_unmanaged, Allocator.Persistent);
        }

        public void SetQuadTreeCull(bool enable)
        {
            _unmanaged->QuadTreeCull = enable;
        }

        public void Add(int4 coord, int index)
        {
            if (coord.w == -1)
            {
                _unmanaged->Outsiders.Add(new int4(index, 0, 0, 0));
                _unmanaged->IndexToCoord.Add(index, new int4(0, 0, 0, -1));
                return;
            }

            if (!_unmanaged->Map.TryGetValue(coord, out var list))
            {
                list = new UnsafeList<int4>(16, Allocator.Persistent);
            }

            list.Add(new int4(index, 0, 0, 0));
            _unmanaged->Map[coord] = list;
            _unmanaged->IndexToCoord.Add(index, coord);
        }

        public void Delete(int index)
        {
            if (_unmanaged->IndexToCoord.TryGetValue(index, out int4 key))
            {
                if (key.w == -1)
                {
                    var pos = _unmanaged->Outsiders.IndexOf(new int4(index, 0, 0, 0));
                    _unmanaged->Outsiders.RemoveAtSwapBack(pos);
                }
                else
                {
                    if (_unmanaged->Map.TryGetValue(key, out var list))
                    {
                        var pos = list.IndexOf(new int4(index, 0, 0, 0));
                        list.RemoveAtSwapBack(pos);
                        _unmanaged->Map[key] = list;
                    }
                }

                _unmanaged->IndexToCoord.Remove(index);
            }
        }

        public void MarkForDelete(int index)
        {
            if (_unmanaged->IndexToCoord.TryGetValue(index, out int4 key))
            {
                if (key.w == -1)
                {
                    var pos = _unmanaged->Outsiders.IndexOf(new int4(index, 0, 0, 0));
                    _unmanaged->Outsiders[pos] = new int4(-1, 0, 0, 0);
                }
                else
                {
                    if (_unmanaged->Map.TryGetValue(key, out var list))
                    {
                        var pos = list.IndexOf(new int4(index, 0, 0, 0));
                        list[pos] = new int4(-1, 0, 0, 0);
                        _unmanaged->Map[key] = list;
                    }
                }
            }
        }

        public JobHandle DispatchDeleteJob(UnsafeHashSet<int4>* quadTreeIndexToRemoveSet, JobHandle dependency)
        {
            JobHandle jobHandle = new DeletePart1Job
            {
                CoordArray = _unmanaged->Map.GetKeyArray(Allocator.TempJob),
                IndexListArray = _unmanaged->Map.GetValueArray(Allocator.TempJob),
                Unmanaged = _unmanaged,
                QuadTreeIndexToRemoveSet = quadTreeIndexToRemoveSet,
            }.Schedule(_unmanaged->Map.Count(), 4, dependency);

            jobHandle = new DeletePart2Job
            {
                Unmanaged = _unmanaged,
                QuadTreeIndexToRemoveSet = quadTreeIndexToRemoveSet,
            }.Schedule(jobHandle);

            return jobHandle;
        }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
        public unsafe struct DeletePart1Job : IJobParallelFor
        {
            [DeallocateOnJobCompletion]
            public NativeArray<int4> CoordArray;
            [DeallocateOnJobCompletion]
            public NativeArray<UnsafeList<int4>> IndexListArray;
            [NativeDisableUnsafePtrRestriction]
            public Unmanaged* Unmanaged;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeHashSet<int4>* QuadTreeIndexToRemoveSet;

            public void Execute(int index)
            {
                int4 coord = CoordArray[index];
                UnsafeList<int4> indexList = IndexListArray[index];

                for (int i = indexList.Length - 1; i >= 0; --i)
                {
                    if (QuadTreeIndexToRemoveSet->Contains(indexList[i]))
                    {
                        indexList.RemoveAtSwapBack(i);
                    }
                }

                Unmanaged->Map[coord] = indexList;
            }
        }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
        public unsafe struct DeletePart2Job : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public Unmanaged* Unmanaged;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeHashSet<int4>* QuadTreeIndexToRemoveSet;

            public void Execute()
            {
                ref UnsafeList<int4> outsiders = ref Unmanaged->Outsiders;
                for (int i = outsiders.Length - 1; i >= 0; --i)
                {
                    if (QuadTreeIndexToRemoveSet->Contains(outsiders[i]))
                    {
                        outsiders.RemoveAtSwapBack(i);
                    }
                }

                foreach (var index in *QuadTreeIndexToRemoveSet)
                {
                    if (Unmanaged->IndexToCoord.ContainsKey(index.x))
                    {
                        Unmanaged->IndexToCoord.Remove(index.x);
                    }
                }
            }
        }

        public JobHandle DispatchAddJob(UnsafeList<QuadTreeAABBInfo>* quadTreeAABBInfos, JobHandle dependency)
        {
            JobHandle jobHandle = new CalculateCoordJob
            {
                QuadTree = this,
                QuadTreeAABBInfos = quadTreeAABBInfos,
            }.Schedule((int*)((byte*)quadTreeAABBInfos + sizeof(void*)), 16, dependency);

            jobHandle = new AddJob
            {
                QuadTree = this,
                QuadTreeAABBInfos = quadTreeAABBInfos,
            }.Schedule(jobHandle);

            return jobHandle;
        }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
        public unsafe struct CalculateCoordJob : IJobParallelForDefer
        {
            [NativeDisableUnsafePtrRestriction]
            public QuadTree QuadTree;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<QuadTreeAABBInfo>* QuadTreeAABBInfos;

            public void Execute(int index)
            {
                ref var quadTreeAABBInfo = ref QuadTreeAABBInfos->ElementAt(index);
                quadTreeAABBInfo.Coord = QuadTree.CalculateCoord(quadTreeAABBInfo.AABB);
            }
        }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
        public unsafe struct AddJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public QuadTree QuadTree;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<QuadTreeAABBInfo>* QuadTreeAABBInfos;

            public void Execute()
            {
                foreach (var info in *QuadTreeAABBInfos)
                {
                    QuadTree.Add(info.Coord, info.Index);
                }
                QuadTreeAABBInfos->Clear();
            }
        }

        public int4 CalculateCoord(AABB aabb)
        {
            int4 coord = new int4(0, 0, 0, -1);

            var setting = _unmanaged->Setting;

            float3 aabbMax = aabb.Max;
            float3 aabbMin = aabb.Min;
            int maxLodNodeSize = (int)math.pow(2, setting.MaxLod) * setting.Lod0NodeSize;

            if (aabbMin.x < setting.WorldOrigin.x || aabbMax.x > setting.WorldOrigin.x + setting.MaxLodRange.x * maxLodNodeSize)
                return coord;

            if (aabbMin.y < setting.WorldOrigin.y || aabbMax.y > setting.WorldOrigin.y + setting.NodeHeight)
                return coord;

            if (aabbMin.z < setting.WorldOrigin.z || aabbMax.z > setting.WorldOrigin.z + setting.MaxLodRange.z * maxLodNodeSize)
                return coord;

            int lodNodeSize = maxLodNodeSize;
            for (int lod = setting.MaxLod; lod >= 0; --lod)
            {
                int3 maxIndex = (int3)((aabbMax - setting.WorldOrigin) / lodNodeSize);
                int3 minIndex = (int3)((aabbMin - setting.WorldOrigin) / lodNodeSize);

                if (maxIndex.x != minIndex.x || maxIndex.z != minIndex.z)
                    return coord;

                coord.x = maxIndex.x;
                coord.z = maxIndex.z;
                coord.w = lod;

                lodNodeSize /= 2;
            }

            return coord;
        }

        static readonly ProfilerMarker s_cullMarker = new ProfilerMarker("QuadTree.Cull");
        public void Cull(UnsafeList<PlanePacket4> packedPlanes, out UnsafeList<int4> visibleIndices, out UnsafeList<int4> partialIndices)
        {
            if (!_unmanaged->QuadTreeCull)
            {
                visibleIndices = new UnsafeList<int4>(1024, Allocator.Temp);
                partialIndices = new UnsafeList<int4>(1024, Allocator.Temp);

                foreach (var pair in _unmanaged->Map)
                {
                    UnsafeList<int4> indices = pair.Value;
                    for (int i = 0; i < indices.Length; ++i)
                        partialIndices.Add(indices[i]);
                }

                for (int i = 0; i < _unmanaged->Outsiders.Length; ++i)
                    partialIndices.Add(_unmanaged->Outsiders[i]);

                return;
            }

            using (s_cullMarker.Auto())
            {
                CullQuadTree(packedPlanes, out UnsafeList<int4> visibleCoords, out UnsafeList<int4> partialCoords);

                visibleIndices = new UnsafeList<int4>(1024, Allocator.Temp);
                partialIndices = new UnsafeList<int4>(1024, Allocator.Temp);

                UnsafeList<int4> pingVisibleCoords = visibleCoords;
                UnsafeList<int4> pongVisibleCoords = new UnsafeList<int4>(visibleCoords.Length, Allocator.TempJob);

                while (pingVisibleCoords.Length > 0)
                {
                    for (int i = 0; i < pingVisibleCoords.Length; ++i)
                    {
                        int4 coord = pingVisibleCoords[i];

                        if (_unmanaged->Map.TryGetValue(coord, out var list))
                        {
                            AddIndices(ref visibleIndices, list);
                        }

                        if (coord.w > 0)
                        {
                            pongVisibleCoords.Add(new int4(coord.x * 2, 0, coord.z * 2, coord.w - 1));
                            pongVisibleCoords.Add(new int4(coord.x * 2 + 1, 0, coord.z * 2, coord.w - 1));
                            pongVisibleCoords.Add(new int4(coord.x * 2, 0, coord.z * 2 + 1, coord.w - 1));
                            pongVisibleCoords.Add(new int4(coord.x * 2 + 1, 0, coord.z * 2 + 1, coord.w - 1));
                        }
                    }

                    pingVisibleCoords.Clear();

                    var temp = pingVisibleCoords;
                    pingVisibleCoords = pongVisibleCoords;
                    pongVisibleCoords = temp;
                }

                pingVisibleCoords.Dispose();
                pongVisibleCoords.Dispose();

                for (int i = 0; i < partialCoords.Length; ++i)
                {
                    if (_unmanaged->Map.TryGetValue(partialCoords[i], out var list))
                    {
                        AddIndices(ref partialIndices, list);
                    }
                }

                AddIndices(ref partialIndices, _unmanaged->Outsiders);

                partialCoords.Dispose();
            }
        }

        void AddIndices(ref UnsafeList<int4> dst, UnsafeList<int4> src)
        {
            dst.Length = dst.Length + src.Length;
            UnsafeUtility.MemCpy(dst.Ptr + dst.Length - src.Length, src.Ptr, src.Length * UnsafeUtility.SizeOf<int4>());
        }

        static readonly ProfilerMarker s_cullQuadTreeMarker = new ProfilerMarker("QuadTree.CullQuadTree");
        void CullQuadTree(UnsafeList<PlanePacket4> packedPlanes, out UnsafeList<int4> visibleCoords, out UnsafeList<int4> partialCoords)
        {
            using (s_cullQuadTreeMarker.Auto())
            {
                int maxLodNodeCount = _unmanaged->Setting.MaxLodRange.x * _unmanaged->Setting.MaxLodRange.z;

                UnsafeList<int2> pingCoords = new UnsafeList<int2>(maxLodNodeCount, Allocator.TempJob);
                UnsafeList<int2> pongCoords = new UnsafeList<int2>(maxLodNodeCount * 4, Allocator.TempJob);
                visibleCoords = new UnsafeList<int4>(maxLodNodeCount, Allocator.TempJob);
                partialCoords = new UnsafeList<int4>(maxLodNodeCount, Allocator.TempJob);

                for (int x = 0; x < _unmanaged->Setting.MaxLodRange.x; ++x)
                {
                    for (int z = 0; z < _unmanaged->Setting.MaxLodRange.z; ++z)
                    {
                        pingCoords.Add(new int2(x, z));
                    }
                }

                JobHandle jobHandle = default;

                UnsafeRef<UnsafeList<int2>> pingCoordsRef = new UnsafeRef<UnsafeList<int2>>(pingCoords, Allocator.TempJob);
                UnsafeRef<UnsafeList<int2>> pongCoordsRef = new UnsafeRef<UnsafeList<int2>>(pongCoords, Allocator.TempJob);
                UnsafeRef<UnsafeList<int4>> visibleCoordsRef = new UnsafeRef<UnsafeList<int4>>(visibleCoords, Allocator.TempJob);
                UnsafeRef<UnsafeList<int4>> partialCoordsRef = new UnsafeRef<UnsafeList<int4>>(partialCoords, Allocator.TempJob);

                if (_unmanaged->UseSingleJob)
                {
                    var cullQuadTreeSingleJob = new CullQuadTreeSingleJob
                    {
                        PackedPlanes = packedPlanes,
                        PingCoordsRef = pingCoordsRef,
                        PongCoordsRef = pongCoordsRef,
                        VisibleCoordsRef = visibleCoordsRef,
                        PartialCoordsRef = partialCoordsRef,
                        Setting = _unmanaged->Setting
                    };
                    jobHandle = cullQuadTreeSingleJob.Schedule();
                }
                else
                {
                    var resizeJob = new ResizeJob
                    {
                        PingCoordsRef = pingCoordsRef,
                        PongCoordsRef = pongCoordsRef,
                        VisibleCoordsRef = visibleCoordsRef,
                        PartialCoordsRef = partialCoordsRef,
                    };

                    var cullQuadTreeJob = new CullQuadTreeJob
                    {
                        PackedPlanes = packedPlanes,
                        PingCoordsRef = pingCoordsRef,
                        PongCoordsRef = pongCoordsRef,
                        VisibleCoordsRef = visibleCoordsRef,
                        PartialCoordsRef = partialCoordsRef,
                        Lod = 0,
                        LodNodeSize = 0,
                        Setting = _unmanaged->Setting
                    };

                    var switchPingPongJob = new ClearAndSwitchJob
                    {
                        PingCoordsRef = pingCoordsRef,
                        PongCoordsRef = pongCoordsRef,
                    };

                    for (int lod = _unmanaged->Setting.MaxLod; lod >= 0; --lod)
                    {
                        cullQuadTreeJob.Lod = lod;
                        cullQuadTreeJob.LodNodeSize = math.pow(2, lod) * _unmanaged->Setting.Lod0NodeSize;

                        jobHandle = resizeJob.Schedule(jobHandle);
                        jobHandle = cullQuadTreeJob.Schedule((int*)((byte*)pingCoordsRef.GetUnsafePtr() + sizeof(void*)), 16, jobHandle);

                        if (lod > 0)
                        {
                            jobHandle = switchPingPongJob.Schedule(jobHandle);
                        }
                    }
                }

                jobHandle.Complete();

                pingCoords = pingCoordsRef.RefValue;
                pongCoords = pongCoordsRef.RefValue;
                visibleCoords = visibleCoordsRef.RefValue;
                partialCoords = partialCoordsRef.RefValue;

                pingCoordsRef.Dispose(jobHandle);
                pongCoordsRef.Dispose(jobHandle);
                visibleCoordsRef.Dispose(jobHandle);
                partialCoordsRef.Dispose(jobHandle);

                pingCoords.Dispose();
                pongCoords.Dispose();
            }
        }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
        public unsafe struct CullQuadTreeSingleJob : IJob
        {
            [ReadOnly]
            public UnsafeList<PlanePacket4> PackedPlanes;
            public UnsafeRef<UnsafeList<int2>> PingCoordsRef;
            public UnsafeRef<UnsafeList<int2>> PongCoordsRef;
            public UnsafeRef<UnsafeList<int4>> VisibleCoordsRef;
            public UnsafeRef<UnsafeList<int4>> PartialCoordsRef;
            [ReadOnly]
            public QuadTreeSetting Setting;

            public void Execute()
            {
                ref UnsafeList<int2> pingCoords = ref PingCoordsRef.RefValue;
                ref UnsafeList<int2> pongCoords = ref PongCoordsRef.RefValue;
                ref UnsafeList<int4> visibleCoords = ref VisibleCoordsRef.RefValue;
                ref UnsafeList<int4> partialCoords = ref PartialCoordsRef.RefValue;

                for (int lod = Setting.MaxLod; lod >= 0; --lod)
                {
                    float lodNodeSize = math.pow(2, lod) * Setting.Lod0NodeSize;

                    for (int i = 0; i < pingCoords.Length; ++i)
                    {
                        int2 coord = pingCoords[i];

                        AABB aabb;
                        aabb.Center.x = Setting.WorldOrigin.x + coord.x * lodNodeSize + 0.5f * lodNodeSize;
                        aabb.Center.y = Setting.WorldOrigin.y + 0.5f * Setting.NodeHeight;
                        aabb.Center.z = Setting.WorldOrigin.z + coord.y * lodNodeSize + 0.5f * lodNodeSize;
                        aabb.Extents = new float3(lodNodeSize, Setting.NodeHeight, lodNodeSize) * 0.5f;

                        IntersectResult result = CullingUtility.Intersect(PackedPlanes, aabb);
                        if (result == IntersectResult.In)
                        {
                            visibleCoords.Add(new int4(coord.x, 0, coord.y, lod));
                        }
                        else if (result == IntersectResult.Partial)
                        {
                            partialCoords.Add(new int4(coord.x, 0, coord.y, lod));

                            if (lod > 0)
                            {
                                pongCoords.Add(new int2(coord.x * 2, coord.y * 2));
                                pongCoords.Add(new int2(coord.x * 2 + 1, coord.y * 2));
                                pongCoords.Add(new int2(coord.x * 2, coord.y * 2 + 1));
                                pongCoords.Add(new int2(coord.x * 2 + 1, coord.y * 2 + 1));
                            }
                        }
                    }

                    if (lod > 0)
                    {
                        pingCoords.Clear();

                        var temp = pingCoords;
                        pingCoords = pongCoords;
                        pongCoords = temp;
                    }
                }
            }
        }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
        public unsafe struct ResizeJob : IJob
        {
            public UnsafeRef<UnsafeList<int2>> PingCoordsRef;
            public UnsafeRef<UnsafeList<int2>> PongCoordsRef;
            public UnsafeRef<UnsafeList<int4>> VisibleCoordsRef;
            public UnsafeRef<UnsafeList<int4>> PartialCoordsRef;

            public void Execute()
            {
                ref UnsafeList<int2> pingCoords = ref PingCoordsRef.RefValue;
                ref UnsafeList<int2> pongCoords = ref PongCoordsRef.RefValue;
                ref UnsafeList<int4> visibleCoords = ref VisibleCoordsRef.RefValue;
                ref UnsafeList<int4> partialCoords = ref PartialCoordsRef.RefValue;

                pongCoords.Capacity = pingCoords.Length * 4;
                visibleCoords.Capacity += pingCoords.Length;
                partialCoords.Capacity += pingCoords.Length;
            }
        }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
        public unsafe struct CullQuadTreeJob : IJobParallelForDefer
        {
            [ReadOnly]
            public UnsafeList<PlanePacket4> PackedPlanes;
            public UnsafeRef<UnsafeList<int2>> PingCoordsRef;
            public UnsafeRef<UnsafeList<int2>> PongCoordsRef;
            public UnsafeRef<UnsafeList<int4>> VisibleCoordsRef;
            public UnsafeRef<UnsafeList<int4>> PartialCoordsRef;
            public int Lod;
            public float LodNodeSize;
            [ReadOnly]
            public QuadTreeSetting Setting;

            public void Execute(int index)
            {
                ref UnsafeList<int2> pingCoords = ref PingCoordsRef.RefValue;
                ref UnsafeList<int2> pongCoords = ref PongCoordsRef.RefValue;
                ref UnsafeList<int4> visibleCoords = ref VisibleCoordsRef.RefValue;
                ref UnsafeList<int4> partialCoords = ref PartialCoordsRef.RefValue;

                int2 coord = pingCoords[index];

                AABB aabb;
                aabb.Center.x = Setting.WorldOrigin.x + coord.x * LodNodeSize + 0.5f * LodNodeSize;
                aabb.Center.y = Setting.WorldOrigin.y + 0.5f * Setting.NodeHeight;
                aabb.Center.z = Setting.WorldOrigin.z + coord.y * LodNodeSize + 0.5f * LodNodeSize;
                aabb.Extents = new float3(LodNodeSize, Setting.NodeHeight, LodNodeSize) * 0.5f;

                IntersectResult result = CullingUtility.Intersect(PackedPlanes, aabb);
                if (result == IntersectResult.In)
                {
                    visibleCoords.AsParallelWriter().AddNoResize(new int4(coord.x, 0, coord.y, Lod));
                }
                else if (result == IntersectResult.Partial)
                {
                    partialCoords.AsParallelWriter().AddNoResize(new int4(coord.x, 0, coord.y, Lod));

                    if (Lod > 0)
                    {
                        var writer = pongCoords.AsParallelWriter();
                        writer.AddNoResize(new int2(coord.x * 2, coord.y * 2));
                        writer.AddNoResize(new int2(coord.x * 2 + 1, coord.y * 2));
                        writer.AddNoResize(new int2(coord.x * 2, coord.y * 2 + 1));
                        writer.AddNoResize(new int2(coord.x * 2 + 1, coord.y * 2 + 1));
                    }
                }
            }
        }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
        public unsafe struct ClearAndSwitchJob : IJob
        {
            public UnsafeRef<UnsafeList<int2>> PingCoordsRef;
            public UnsafeRef<UnsafeList<int2>> PongCoordsRef;

            public void Execute()
            {
                ref UnsafeList<int2> pingCoords = ref PingCoordsRef.RefValue;
                ref UnsafeList<int2> pongCoords = ref PongCoordsRef.RefValue;

                pingCoords.Clear();

                var temp = pingCoords;
                pingCoords = pongCoords;
                pongCoords = temp;
            }
        }

#if UNITY_EDITOR
        public void DrawGizmo()
        {
            Plane[] _planeArray = new Plane[6];
            GeometryUtility.CalculateFrustumPlanes(Camera.main, _planeArray);

            UnsafeList<Plane> planes = new UnsafeList<Plane>(6, Allocator.Temp);
            planes.Length = 6;
            for (int i = 0; i < 6; ++i)
                planes[i] = _planeArray[i];

            UnsafeList<PlanePacket4> packedPlanes = CullingUtility.BuildSOAPlanePackets(planes, Allocator.Temp);

            CullQuadTree(packedPlanes, out UnsafeList<int4> visibleCoords, out UnsafeList<int4> partialCoords);

            NativeHashSet<int4> visibleCoordSet = new NativeHashSet<int4>(visibleCoords.Length, Allocator.Temp);
            for (int i = 0; i < visibleCoords.Length; ++i)
                visibleCoordSet.Add(visibleCoords[i]);

            NativeHashSet<int4> partialCoordSet = new NativeHashSet<int4>(partialCoords.Length, Allocator.Temp);
            for (int i = 0; i < partialCoords.Length; ++i)
                partialCoordSet.Add(partialCoords[i]);

            visibleCoords.Dispose();
            partialCoords.Dispose();

            for (int x = 0; x < _unmanaged->Setting.MaxLodRange.x; ++x)
            {
                for (int z = 0; z < _unmanaged->Setting.MaxLodRange.z; ++z)
                {
                    DrawGizmoRecursive(new int4(x, 0, z, _unmanaged->Setting.MaxLod), visibleCoordSet, partialCoordSet);
                }
            }
        }

        public void DrawGizmoRecursive(int4 coord, NativeHashSet<int4> visibleCoordSet, NativeHashSet<int4> partialCoordSet)
        {
            if (visibleCoordSet.Contains(coord))
            {
                DrawNode(coord, Color.green);
            }
            else if (partialCoordSet.Contains(coord))
            {
                if (coord.w == 0)
                {
                    DrawNode(coord, Color.blue);
                }
                else
                {
                    DrawGizmoRecursive(new int4(coord.x * 2, coord.y, coord.z * 2, coord.w - 1), visibleCoordSet, partialCoordSet);
                    DrawGizmoRecursive(new int4(coord.x * 2 + 1, coord.y, coord.z * 2, coord.w - 1), visibleCoordSet, partialCoordSet);
                    DrawGizmoRecursive(new int4(coord.x * 2, coord.y, coord.z * 2 + 1, coord.w - 1), visibleCoordSet, partialCoordSet);
                    DrawGizmoRecursive(new int4(coord.x * 2 + 1, coord.y, coord.z * 2 + 1, coord.w - 1), visibleCoordSet, partialCoordSet);
                }
            }
            else
            {
                DrawNode(coord, Color.red);
            }
        }

        void DrawNode(int4 coord, Color color)
        {
            float lodNodeSize = math.pow(2, coord.w) * _unmanaged->Setting.Lod0NodeSize;

            Vector3 center;
            center.x = _unmanaged->Setting.WorldOrigin.x + coord.x * lodNodeSize + 0.5f * lodNodeSize;
            center.y = _unmanaged->Setting.WorldOrigin.y + 0.5f * _unmanaged->Setting.NodeHeight;
            center.z = _unmanaged->Setting.WorldOrigin.z + coord.z * lodNodeSize + 0.5f * lodNodeSize;

            Gizmos.color = color;
            Gizmos.DrawWireCube(center, new Vector3(lodNodeSize, _unmanaged->Setting.NodeHeight, lodNodeSize));

            UnityEditor.Handles.Label(center, string.Format("({0},{1},{2},{3})", coord.x, coord.y, coord.z, coord.w));
        }
#endif
    }
}