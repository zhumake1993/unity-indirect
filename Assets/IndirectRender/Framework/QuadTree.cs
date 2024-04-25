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

            public void Init(IndirectRenderSetting setting)
            {
                Setting = setting.QuadTreeSetting;

                Map = new NativeParallelHashMap<int4, UnsafeList<int4>>(1024, Allocator.Persistent);
                Outsiders = new UnsafeList<int4>(1024, Allocator.Persistent);
                IndexToCoord = new UnsafeHashMap<int, int4>(1024, Allocator.Persistent);

                QuadTreeCull = true;
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
        public JobHandle Cull(UnsafeList<PlanePacket4> packedPlanes, UnsafeList<int4>* visibleIndices, UnsafeList<int4>* partialIndices)
        {
            using (s_cullMarker.Auto())
            {
                *visibleIndices = new UnsafeList<int4>(1024, Allocator.TempJob);
                *partialIndices = new UnsafeList<int4>(1024, Allocator.TempJob);

                if (!_unmanaged->QuadTreeCull)
                {
                    foreach (var pair in _unmanaged->Map)
                    {
                        UnsafeList<int4> indices = pair.Value;
                        for (int i = 0; i < indices.Length; ++i)
                            partialIndices->Add(indices[i]);
                    }

                    for (int i = 0; i < _unmanaged->Outsiders.Length; ++i)
                        partialIndices->Add(_unmanaged->Outsiders[i]);

                    return new JobHandle();
                }
                else
                {
                    UnsafeList<int4>* visibleCoords = MemoryUtility.Malloc<UnsafeList<int4>>(Allocator.TempJob);
                    UnsafeList<int4>* partialCoords = MemoryUtility.Malloc<UnsafeList<int4>>(Allocator.TempJob);

                    JobHandle jobHandle = CullQuadTree(packedPlanes, visibleCoords, partialCoords);

                    jobHandle = new CollectIndicesJob
                    {
                        VisibleCoords = visibleCoords,
                        PartialCoords = partialCoords,
                        VisibleIndices = visibleIndices,
                        PartialIndices = partialIndices,
                        Unmanaged = _unmanaged
                    }.Schedule(jobHandle);

                    return jobHandle;
                }
            }
        }

        JobHandle CullQuadTree(UnsafeList<PlanePacket4> packedPlanes, UnsafeList<int4>* visibleCoords, UnsafeList<int4>* partialCoords)
        {
            *visibleCoords = new UnsafeList<int4>(256, Allocator.TempJob);
            *partialCoords = new UnsafeList<int4>(256, Allocator.TempJob);

            return new CullQuadTreeJob
            {
                PackedPlanes = packedPlanes,
                VisibleCoords = visibleCoords,
                PartialCoords = partialCoords,
                Setting = _unmanaged->Setting
            }.Schedule();
        }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
        public unsafe struct CullQuadTreeJob : IJob
        {
            [ReadOnly]
            public UnsafeList<PlanePacket4> PackedPlanes;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<int4>* VisibleCoords;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<int4>* PartialCoords;
            [ReadOnly]
            public QuadTreeSetting Setting;

            public void Execute()
            {
                int maxLodNodeCount = Setting.MaxLodRange.x * Setting.MaxLodRange.z;

                UnsafeList<int2> pingCoords = new UnsafeList<int2>(maxLodNodeCount, Allocator.TempJob);
                UnsafeList<int2> pongCoords = new UnsafeList<int2>(maxLodNodeCount * 4, Allocator.TempJob);

                for (int x = 0; x < Setting.MaxLodRange.x; ++x)
                    for (int z = 0; z < Setting.MaxLodRange.z; ++z)
                        pingCoords.Add(new int2(x, z));

                ref UnsafeList<int4> visibleCoords = ref *VisibleCoords;
                ref UnsafeList<int4> partialCoords = ref *PartialCoords;

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

                pingCoords.Dispose();
                pongCoords.Dispose();
            }
        }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
        public unsafe struct CollectIndicesJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<int4>* VisibleCoords;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<int4>* PartialCoords;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<int4>* VisibleIndices;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<int4>* PartialIndices;
            [NativeDisableUnsafePtrRestriction]
            public Unmanaged* Unmanaged;

            public void Execute()
            {
                UnsafeList<int4> pingVisibleCoords = *VisibleCoords;
                UnsafeList<int4> pongVisibleCoords = new UnsafeList<int4>(VisibleCoords->Length, Allocator.TempJob);
                UnsafeList<int4> partialCoords = *PartialCoords;

                ref UnsafeList<int4> visibleIndices = ref *VisibleIndices;
                ref UnsafeList<int4> partialIndices = ref *PartialIndices;

                while (pingVisibleCoords.Length > 0)
                {
                    for (int i = 0; i < pingVisibleCoords.Length; ++i)
                    {
                        int4 coord = pingVisibleCoords[i];

                        if (Unmanaged->Map.TryGetValue(coord, out var list))
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

                for (int i = 0; i < partialCoords.Length; ++i)
                {
                    if (Unmanaged->Map.TryGetValue(partialCoords[i], out var list))
                    {
                        AddIndices(ref partialIndices, list);
                    }
                }

                AddIndices(ref partialIndices, Unmanaged->Outsiders);

                pingVisibleCoords.Dispose();
                pongVisibleCoords.Dispose();
                partialCoords.Dispose();

                MemoryUtility.Free(VisibleCoords, Allocator.TempJob);
                MemoryUtility.Free(PartialCoords, Allocator.TempJob);
            }

            void AddIndices(ref UnsafeList<int4> dst, UnsafeList<int4> src)
            {
                dst.Length = dst.Length + src.Length;
                UnsafeUtility.MemCpy(dst.Ptr + dst.Length - src.Length, src.Ptr, src.Length * UnsafeUtility.SizeOf<int4>());
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

            UnsafeList<int4>* visibleCoords = MemoryUtility.Malloc<UnsafeList<int4>>(Allocator.TempJob);
            UnsafeList<int4>* partialCoords = MemoryUtility.Malloc<UnsafeList<int4>>(Allocator.TempJob);

            CullQuadTree(packedPlanes, visibleCoords, partialCoords).Complete();

            NativeHashSet<int4> visibleCoordSet = new NativeHashSet<int4>(visibleCoords->Length, Allocator.Temp);
            for (int i = 0; i < visibleCoords->Length; ++i)
                visibleCoordSet.Add((*visibleCoords)[i]);

            NativeHashSet<int4> partialCoordSet = new NativeHashSet<int4>(partialCoords->Length, Allocator.Temp);
            for (int i = 0; i < partialCoords->Length; ++i)
                partialCoordSet.Add((*partialCoords)[i]);

            visibleCoords->Dispose();
            partialCoords->Dispose();
            MemoryUtility.Free(visibleCoords, Allocator.TempJob);
            MemoryUtility.Free(partialCoords, Allocator.TempJob);

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