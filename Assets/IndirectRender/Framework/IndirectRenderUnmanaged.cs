#define ENABLE_BURST

using System;
#if ENABLE_BURST
using Unity.Burst;
#endif
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace ZGame.Indirect
{
    public struct AddItem
    {
        public int UserID;
        public IndirectKey IndirectKey;
        public MeshInfo MeshInfo;
        public bool NeedInverse;
        public UnsafeList<float4x4> Matrices;
        public UnsafeList<UnsafeList<float4>> Properties;
    }

    public unsafe struct IndirectRenderUnmanaged
    {
        public IndirectRenderSetting Setting;

        public IDGenerator UserIDGenerator;
        public UnsafeHashMap<int, UnsafeList<int>> UserIdToCmdIDs;

        public IDGenerator CmdIDGenerator;
        public UnsafeHashMap<int, IndirectCmdInfo> CmdMap;
        public IDGenerator IndirectIDGenerator;
        public UnsafeHashMap<IndirectKey, IndirectBatch> IndirectMap;

        public BuddyAllocator InstanceIndicesAllocator;
        public BuddyAllocator InstanceDataAllocator;

        public NativeArray<InstanceDescriptor> InstanceDescriptorArray;
        public NativeArray<int4> BatchDescriptorArray;
        public NativeArray<float4> InstanceDataArray;
        public NativeArray<GraphicsBuffer.IndirectDrawArgs> IndirectArgsArray;

        public NativeList<AddItem> AddCache;
        public NativeList<int> RemoveCache;
        public UnsafeList<QuadTreeAABBInfo>* QuadTreeAABBInfos;
        public UnsafeHashSet<int4>* QuadTreeIndexToRemoveSet;
        public UnsafeList<OffsetSizeF4> InstanceDataDirtySegments;
        public UnsafeList<OffsetSizeF4> InstanceDescriptorDirtySegments;

        public SimpleSpinLock* Lock;

        public int MaxIndirectID;
        public int TotalActualInstanceCount;

        public void Init(IndirectRenderSetting setting)
        {
            Setting = setting;

            UserIDGenerator.Init(Utility.c_UserIDInitialCapacity);
            UserIdToCmdIDs = new UnsafeHashMap<int, UnsafeList<int>>(Utility.c_UserIDInitialCapacity, Allocator.Persistent);

            CmdIDGenerator.Init(Utility.c_CmdIDInitialCapacity);
            CmdMap = new UnsafeHashMap<int, IndirectCmdInfo>(Utility.c_CmdIDInitialCapacity, Allocator.Persistent);
            IndirectIDGenerator.Init(setting.BatchCapacity);
            IndirectMap = new UnsafeHashMap<IndirectKey, IndirectBatch>(setting.BatchCapacity, Allocator.Persistent);

            InstanceIndicesAllocator.Init(setting.MinInstanceCountPerCmd, setting.MaxInstanceCountPerCmd, setting.NumMaxInstanceCountPerCmd);
            InstanceDataAllocator.Init(setting.InstanceDataMinSizeBytes, setting.InstanceDataMaxSizeBytes, setting.InstanceDataNumMaxSizeBlocks);

            InstanceDescriptorArray = new NativeArray<InstanceDescriptor>(setting.InstanceCapacity, Allocator.Persistent);
            BatchDescriptorArray = new NativeArray<int4>(setting.BatchCapacity, Allocator.Persistent);
            InstanceDataArray = new NativeArray<float4>((int)(setting.InstanceDataMaxSizeBytes * setting.InstanceDataNumMaxSizeBlocks) / Utility.c_SizeOfFloat4, Allocator.Persistent);
            IndirectArgsArray = new NativeArray<GraphicsBuffer.IndirectDrawArgs>(setting.BatchCapacity, Allocator.Persistent);

            AddCache = new NativeList<AddItem>(Allocator.Persistent);
            RemoveCache = new NativeList<int>(Allocator.Persistent);
            QuadTreeAABBInfos = MemoryUtility.Malloc<UnsafeList<QuadTreeAABBInfo>>(Allocator.Persistent);
            *QuadTreeAABBInfos = new UnsafeList<QuadTreeAABBInfo>(16, Allocator.Persistent);
            QuadTreeIndexToRemoveSet = MemoryUtility.Malloc<UnsafeHashSet<int4>>(Allocator.Persistent);
            *QuadTreeIndexToRemoveSet = new UnsafeHashSet<int4>(16, Allocator.Persistent);
            InstanceDataDirtySegments = new UnsafeList<OffsetSizeF4>(16, Allocator.Persistent);
            InstanceDescriptorDirtySegments = new UnsafeList<OffsetSizeF4>(16, Allocator.Persistent);

            Lock = MemoryUtility.Malloc<SimpleSpinLock>(Allocator.Persistent);
            Lock->Reset();

            MaxIndirectID = 0;
            TotalActualInstanceCount = 0;
        }

        public void Dispose()
        {
            UserIDGenerator.Dispose();
            foreach (var cmdIDs in UserIdToCmdIDs)
                cmdIDs.Value.Dispose();
            UserIdToCmdIDs.Dispose();

            CmdIDGenerator.Dispose();
            foreach (var cmd in CmdMap)
                cmd.Value.SubCmds.Dispose();
            CmdMap.Dispose();
            IndirectIDGenerator.Dispose();
            IndirectMap.Dispose();

            InstanceIndicesAllocator.Dispose();
            InstanceDataAllocator.Dispose();

            InstanceDescriptorArray.Dispose();
            BatchDescriptorArray.Dispose();
            InstanceDataArray.Dispose();
            IndirectArgsArray.Dispose();

            AddCache.Dispose();
            RemoveCache.Dispose();
            MemoryUtility.Free(QuadTreeAABBInfos, Allocator.Persistent);
            MemoryUtility.Free(QuadTreeIndexToRemoveSet, Allocator.Persistent);
            InstanceDataDirtySegments.Dispose();
            InstanceDescriptorDirtySegments.Dispose();

            MemoryUtility.Free(Lock, Allocator.Persistent);
        }
    }

#if ENABLE_BURST
    [BurstCompile(CompileSynchronously = true)]
#endif
    public unsafe struct FlushRemoveJob : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        public IndirectRenderUnmanaged* Unmanaged;
        [NativeDisableUnsafePtrRestriction]
        public QuadTree QuadTree;

        public void Execute()
        {
            Unmanaged->QuadTreeIndexToRemoveSet->Clear();

            foreach (int id in Unmanaged->RemoveCache)
            {
                if (Unmanaged->UserIdToCmdIDs.TryGetValue(id, out UnsafeList<int> cmdIDs))
                {
                    foreach (int cmdID in cmdIDs)
                        RemoveCmd(cmdID);
                    cmdIDs.Dispose();

                    Unmanaged->UserIdToCmdIDs.Remove(id);
                }
                else
                {
                    Utility.LogError($"invalid user id {id}");
                }
            }
            Unmanaged->RemoveCache.Clear();
        }

        void RemoveCmd(int id)
        {
            if (Unmanaged->CmdMap.TryGetValue(id, out IndirectCmdInfo cmdInfo))
            {
                Unmanaged->CmdMap.Remove(id);
                Unmanaged->CmdIDGenerator.ReturnID(id);

                foreach (var subCmd in cmdInfo.SubCmds)
                {
                    int startInstanceIndex = subCmd.StartInstanceIndex;
                    int instanceCount = cmdInfo.InstanceCount;

                    for (int i = 0; i < instanceCount; i++)
                        Unmanaged->QuadTreeIndexToRemoveSet->Add(new int4(startInstanceIndex + i, 0, 0, 0));
                }

                IndirectBatch indirectBatch = Unmanaged->IndirectMap[cmdInfo.IndirectKey];
                indirectBatch.ActualInstanceCount -= cmdInfo.InstanceCount * cmdInfo.SubCmds.Length;

                if (indirectBatch.ActualInstanceCount == 0)
                {
                    Unmanaged->IndirectIDGenerator.ReturnID(indirectBatch.IndirectID);
                    Unmanaged->IndirectMap.Remove(cmdInfo.IndirectKey);
                }
                else
                {
                    Unmanaged->IndirectMap[cmdInfo.IndirectKey] = indirectBatch;
                }

                Unmanaged->TotalActualInstanceCount -= cmdInfo.InstanceCount * cmdInfo.SubCmds.Length;

                Unmanaged->InstanceDataAllocator.Free(cmdInfo.InstanceDataChunk);

                foreach (var subCmd in cmdInfo.SubCmds)
                    Unmanaged->InstanceIndicesAllocator.Free(subCmd.InstanceIndicesChunk);

                cmdInfo.SubCmds.Dispose();
            }
            else
            {
                Utility.LogError($"invalid cmd id={id}");
            }
        }
    }

#if ENABLE_BURST
        [BurstCompile(CompileSynchronously = true)]
#endif
    public unsafe struct PreFlushAddJob : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        public IndirectRenderUnmanaged* Unmanaged;

        public void Execute()
        {
            foreach (var addItem in Unmanaged->AddCache)
            {
                IndirectKey indirectKey = addItem.IndirectKey;

                int instanceCount = addItem.Matrices.Length;
                UnsafeList<UnitMeshInfo> unitMeshInfos = addItem.MeshInfo.UnitMeshInfos;
                int unitMeshCount = unitMeshInfos.Length;
                int actualInstanceCount = instanceCount * unitMeshCount;

                if (actualInstanceCount + Unmanaged->TotalActualInstanceCount > Unmanaged->Setting.InstanceCapacity)
                {
                    Utility.LogError($"instance capacity exceeded, instanceCount={instanceCount}, unitMeshCount={unitMeshCount}, totalInstanceCount={Unmanaged->TotalActualInstanceCount}, instanceCapacity={Unmanaged->Setting.InstanceCapacity}");
                    return;
                }

                if (!Unmanaged->IndirectMap.TryGetValue(indirectKey, out var indirectBatch))
                {
                    if (Unmanaged->IndirectMap.Count == Unmanaged->Setting.BatchCapacity)
                    {
                        Utility.LogError($"batch capacity exceeded, batchCount={Unmanaged->IndirectMap.Count}, batchCapacity={Unmanaged->Setting.BatchCapacity}");
                        return;
                    }

                    indirectBatch = new IndirectBatch()
                    {
                        IndirectID = Unmanaged->IndirectIDGenerator.GetID(),
                        ActualInstanceCount = 0
                    };

                    Unmanaged->IndirectArgsArray[indirectBatch.IndirectID] = new GraphicsBuffer.IndirectDrawArgs()
                    {
                        vertexCountPerInstance = (uint)Unmanaged->Setting.UnitMeshTriangleCount * 3,
                        instanceCount = 0,
                        startVertex = 0,
                        startInstance = 0,
                    };
                    Unmanaged->MaxIndirectID = math.max(Unmanaged->MaxIndirectID, indirectBatch.IndirectID);
                }

                indirectBatch.ActualInstanceCount += actualInstanceCount;
                Unmanaged->IndirectMap[indirectKey] = indirectBatch;

                Unmanaged->TotalActualInstanceCount += actualInstanceCount;
            }

            Unmanaged->QuadTreeAABBInfos->Clear();
            Unmanaged->InstanceDataDirtySegments.Clear();
            Unmanaged->InstanceDescriptorDirtySegments.Clear();
        }
    }

#if ENABLE_BURST
    [BurstCompile(CompileSynchronously = true)]
#endif
    public unsafe struct FlushAddJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction]
        public IndirectRenderUnmanaged* Unmanaged;

        public void Execute(int index)
        {
            AddItem addItem = Unmanaged->AddCache[index];
            bool needInverse = addItem.NeedInverse;
            UnsafeList<float4x4> matrices = addItem.Matrices;
            UnsafeList<UnsafeList<float4>> properties = addItem.Properties;

            int instanceCount = addItem.Matrices.Length;
            UnsafeList<UnitMeshInfo> unitMeshInfos = addItem.MeshInfo.UnitMeshInfos;
            int unitMeshCount = unitMeshInfos.Length;
            int actualInstanceCount = instanceCount * unitMeshCount;

            int indirectID = Unmanaged->IndirectMap[addItem.IndirectKey].IndirectID;

            int instanceSizeF4 = Utility.c_SizeOfPackedMatrixF4 + (needInverse ? Utility.c_SizeOfPackedMatrixF4 : 0) + properties.Length;
            int instanceSize = instanceSizeF4 * Utility.c_SizeOfFloat4;
            int maxInstanceCountPerCmd = math.min((int)Unmanaged->Setting.MaxInstanceCountPerCmd, (int)Unmanaged->Setting.InstanceDataMaxSizeBytes / instanceSize); ;
            int indirectCmdInfoCount = (instanceCount + maxInstanceCountPerCmd - 1) / maxInstanceCountPerCmd;

            UnsafeList<int> cmdIDs = new UnsafeList<int>(indirectCmdInfoCount, Allocator.Persistent);

            int left = instanceCount;
            int offset = 0;
            while (left > 0)
            {
                int count = math.min(left, maxInstanceCountPerCmd);
                left -= count;

                // instance data

                Chunk instanceDataChunk = Unmanaged->InstanceDataAllocator.Alloc((UInt32)(instanceSize * count));
                if (instanceDataChunk == Chunk.s_InvalidChunk)
                {
                    Utility.LogError($"instance data allocation failed, instanceDataAllocSize={instanceSize * count}");
                    return;
                }

                int instanceDataOffsetF4 = (int)instanceDataChunk.AddressOf() / Utility.c_SizeOfFloat4;

                for (int i = 0; i < count; i++)
                {
                    float4x4 matrix = matrices[offset + i];
                    Unmanaged->InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 0] = new float4(matrix[0][0], matrix[0][1], matrix[0][2], matrix[1][0]);
                    Unmanaged->InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 1] = new float4(matrix[1][1], matrix[1][2], matrix[2][0], matrix[2][1]);
                    Unmanaged->InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 2] = new float4(matrix[2][2], matrix[3][0], matrix[3][1], matrix[3][2]);

                    if (needInverse)
                    {
                        float4x4 inverseMatrix = math.inverse(matrix);
                        Unmanaged->InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 3] = new float4(inverseMatrix[0][0], inverseMatrix[0][1], inverseMatrix[0][2], inverseMatrix[1][0]);
                        Unmanaged->InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 4] = new float4(inverseMatrix[1][1], inverseMatrix[1][2], inverseMatrix[2][0], inverseMatrix[2][1]);
                        Unmanaged->InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 5] = new float4(inverseMatrix[2][2], inverseMatrix[3][0], inverseMatrix[3][1], inverseMatrix[3][2]);
                    }

                    for (int j = 0; j < properties.Length; ++j)
                    {
                        Unmanaged->InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 3 + (needInverse ? 3 : 0) + j] = properties[j][i];
                    }
                }

                using (new SimpleSpinLock.AutoLock(Unmanaged->Lock))
                {
                    Unmanaged->InstanceDataDirtySegments.Add(new OffsetSizeF4
                    {
                        OffsetF4 = instanceDataOffsetF4,
                        SizeF4 = instanceSizeF4 * count
                    });
                }

                IndirectCmdInfo indirectCmdInfo = new IndirectCmdInfo
                {
                    IndirectKey = addItem.IndirectKey,
                    InstanceCount = count,
                    InstanceDataChunk = instanceDataChunk,
                    SubCmds = new UnsafeList<IndirectSubCmdInfo>(unitMeshCount, Allocator.Persistent)
                };
                indirectCmdInfo.SubCmds.Length = unitMeshCount;

                // descriptor

                for (int iUnitMesh = 0; iUnitMesh < unitMeshCount; ++iUnitMesh)
                {
                    UnitMeshInfo unitMeshInfo = unitMeshInfos[iUnitMesh];
                    AABB aabbLocal = unitMeshInfo.AABB;

                    int instanceIndicesAllocSize = math.max((int)Unmanaged->Setting.MinInstanceCountPerCmd, count);
                    Chunk instanceIndicesChunk = Unmanaged->InstanceIndicesAllocator.Alloc((UInt32)(instanceIndicesAllocSize));
                    if (instanceIndicesChunk == Chunk.s_InvalidChunk)
                    {
                        Utility.LogError($"instance indices allocation failed, instanceIndicesAllocSize={instanceIndicesAllocSize}");
                        return;
                    }

                    int startInstanceIndex = (int)instanceIndicesChunk.AddressOf();

                    for (int i = 0; i < count; i++)
                    {
                        AABB aabb = AABB.Transform(matrices[offset + i], aabbLocal);

                        using (new SimpleSpinLock.AutoLock(Unmanaged->Lock))
                        {
                            Unmanaged->QuadTreeAABBInfos->Add(new QuadTreeAABBInfo
                            {
                                Index = startInstanceIndex + i,
                                AABB = aabb,
                                Coord = new int4(0, 0, 0, 0),
                            });
                        }

                        InstanceDescriptor instanceDescriptor = new InstanceDescriptor()
                        {
                            Center_IndirectID = new float4(aabb.Center, indirectID),
                            Extents_DataOffset = new float4(aabb.Extents, (instanceDataOffsetF4 + i * instanceSizeF4) * Utility.c_SizeOfFloat4),
                            UnitMeshInfo = new int4(unitMeshInfo.IndexOffset, unitMeshInfo.VertexOffset, needInverse ? 1 : 0, 0),
                        };

                        Unmanaged->InstanceDescriptorArray[startInstanceIndex + i] = instanceDescriptor;
                    }

                    using (new SimpleSpinLock.AutoLock(Unmanaged->Lock))
                    {
                        Unmanaged->InstanceDescriptorDirtySegments.Add(new OffsetSizeF4
                        {
                            OffsetF4 = startInstanceIndex,
                            SizeF4 = count
                        });
                    }

                    IndirectSubCmdInfo indirectSubCmdInfo = new IndirectSubCmdInfo
                    {
                        StartInstanceIndex = startInstanceIndex,
                        InstanceIndicesChunk = instanceIndicesChunk
                    };

                    indirectCmdInfo.SubCmds[iUnitMesh] = indirectSubCmdInfo;
                }

                int cmdID;

                using (new SimpleSpinLock.AutoLock(Unmanaged->Lock))
                {
                    cmdID = Unmanaged->CmdIDGenerator.GetID();
                    Unmanaged->CmdMap.Add(cmdID, indirectCmdInfo);
                }

                cmdIDs.Add(cmdID);

                offset += count;
            }

            using (new SimpleSpinLock.AutoLock(Unmanaged->Lock))
            {
                Unmanaged->UserIdToCmdIDs.Add(addItem.UserID, cmdIDs);
            }
        }
    }

#if ENABLE_BURST
    [BurstCompile(CompileSynchronously = true)]
#endif
    public unsafe struct PostFlushAddJob : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        public IndirectRenderUnmanaged* Unmanaged;

        public void Execute()
        {
            foreach (var addItem in Unmanaged->AddCache)
            {
                UnsafeList<float4x4> matrices = addItem.Matrices;
                UnsafeList<UnsafeList<float4>> properties = addItem.Properties;

                matrices.Dispose();
                for (int i = 0; i < properties.Length; ++i)
                    properties[i].Dispose();
                properties.Dispose();
            }
            Unmanaged->AddCache.Clear();
        }
    }

#if ENABLE_BURST
    [BurstCompile(CompileSynchronously = true)]
#endif
    public unsafe struct UpdateBatchDescriptorJob : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        public IndirectRenderUnmanaged* Unmanaged;

        public void Execute()
        {
            int instanceOffset = 0;
            foreach (var pair in Unmanaged->IndirectMap)
            {
                IndirectKey indirectKey = pair.Key;
                IndirectBatch indirectBatch = pair.Value;
                int indirectID = indirectBatch.IndirectID;

                Unmanaged->BatchDescriptorArray[indirectID] = new int4(instanceOffset, 0, 0, 0);
                instanceOffset += indirectBatch.ActualInstanceCount;
            }
        }
    }
}