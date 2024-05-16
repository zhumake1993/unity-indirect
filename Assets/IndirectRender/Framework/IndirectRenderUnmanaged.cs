#define ENABLE_BURST

using System;
using System.Linq;
#if ENABLE_BURST
using Unity.Burst;
#endif
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace ZGame.Indirect
{
    public struct AddItem
    {
        public int CmdID;
        public UnsafeList<SubMeshInfo> SubMeshInfos;
        public UnsafeList<IndirectKey> IndirectKeys;
        public float4 LodParam;
        public bool NeedInverse;
        public UnsafeList<float4x4> Matrices;
        public UnsafeList<UnsafeList<float4>> Properties;

        public int LodNum => SubMeshInfos.Length;
        public int MaxLod => SubMeshInfos.Length - 1;
        public SubMeshInfo MaxLodSubMeshInfo => SubMeshInfos[MaxLod];
    }

    public unsafe struct IndirectRenderUnmanaged
    {
        public IndirectRenderSetting Setting;

        public IDGenerator CmdIDGenerator;
        public UnsafeHashMap<int, IndirectCmd> CmdMap;
        public IDGenerator IndirectIDGenerator;
        public UnsafeHashMap<IndirectKey, IndirectBatch> IndirectMap;

        public BuddyAllocator InstanceIndexAllocator;
        public BuddyAllocator InstanceDataAllocator;
        public BuddyAllocator MeshletIndexAllocator;

        public NativeArray<InstanceDescriptor> InstanceDescriptorArray;
        public NativeArray<MeshletDescriptor> MeshletDescriptorArray;
        public NativeArray<CmdDescriptor> CmdDescriptorArray;
        public NativeArray<BatchDescriptor> BatchDescriptorArray;
        public NativeArray<float4> InstanceDataArray;
        public NativeArray<GraphicsBuffer.IndirectDrawArgs> IndirectArgsArray;

        public NativeList<AddItem> AddCache;
        public NativeList<int> RemoveCache;
        public NativeParallelHashMap<int2, bool> EnableCache;
        public UnsafeList<QuadTreeAABBInfo>* QuadTreeAABBInfos;
        public UnsafeHashSet<int4>* QuadTreeIndexToRemoveSet;
        public UnsafeList<OffsetSizeF4> InstanceDataDirtySegments;
        public UnsafeList<OffsetSize> InstanceDescriptorDirtySegments;
        public UnsafeList<OffsetSize> MeshletDescriptorDirtySegments;

        public SimpleSpinLock* Lock;

        public int InstanceCount;
        public int MeshletCount;
        public int MaxCmdID;
        public int MaxIndirectID;

        public void Init(IndirectRenderSetting setting)
        {
            Setting = setting;

            CmdIDGenerator.Init(setting.CmdCapacity);
            CmdMap = new UnsafeHashMap<int, IndirectCmd>(setting.CmdCapacity, Allocator.Persistent);
            IndirectIDGenerator.Init(setting.BatchCapacity);
            IndirectMap = new UnsafeHashMap<IndirectKey, IndirectBatch>(setting.BatchCapacity, Allocator.Persistent);

            InstanceIndexAllocator.Init(setting.InstanceIndexMinCount, setting.InstanceIndexMaxCount, 1);
            InstanceDataAllocator.Init(setting.InstanceDataMinSizeBytes, setting.InstanceDataMaxSizeBytes, 1);
            MeshletIndexAllocator.Init(setting.MeshletIndexMinCount, setting.MeshletIndexMaxCount, 1);

            InstanceDescriptorArray = new NativeArray<InstanceDescriptor>(setting.InstanceCapacity, Allocator.Persistent);
            MeshletDescriptorArray = new NativeArray<MeshletDescriptor>(setting.MeshletCapacity, Allocator.Persistent);
            CmdDescriptorArray = new NativeArray<CmdDescriptor>(setting.CmdCapacity, Allocator.Persistent);
            BatchDescriptorArray = new NativeArray<BatchDescriptor>(setting.BatchCapacity, Allocator.Persistent);
            InstanceDataArray = new NativeArray<float4>((int)(setting.InstanceDataMaxSizeBytes) / Utility.c_SizeOfFloat4, Allocator.Persistent);
            IndirectArgsArray = new NativeArray<GraphicsBuffer.IndirectDrawArgs>(setting.BatchCapacity, Allocator.Persistent);

            AddCache = new NativeList<AddItem>(Allocator.Persistent);
            RemoveCache = new NativeList<int>(Allocator.Persistent);
            EnableCache = new NativeParallelHashMap<int2, bool>(16, Allocator.Persistent);
            QuadTreeAABBInfos = MemoryUtility.Malloc<UnsafeList<QuadTreeAABBInfo>>(Allocator.Persistent);
            *QuadTreeAABBInfos = new UnsafeList<QuadTreeAABBInfo>(16, Allocator.Persistent);
            QuadTreeIndexToRemoveSet = MemoryUtility.Malloc<UnsafeHashSet<int4>>(Allocator.Persistent);
            *QuadTreeIndexToRemoveSet = new UnsafeHashSet<int4>(16, Allocator.Persistent);
            InstanceDataDirtySegments = new UnsafeList<OffsetSizeF4>(16, Allocator.Persistent);
            InstanceDescriptorDirtySegments = new UnsafeList<OffsetSize>(16, Allocator.Persistent);
            MeshletDescriptorDirtySegments = new UnsafeList<OffsetSize>(16, Allocator.Persistent);

            Lock = MemoryUtility.Malloc<SimpleSpinLock>(Allocator.Persistent);
            Lock->Reset();

            InstanceCount = 0;
            MeshletCount = 0;
            MaxCmdID = 0;
            MaxIndirectID = 0;
        }

        public void Dispose()
        {
            CmdIDGenerator.Dispose();
            foreach (var pair in CmdMap)
                pair.Value.Dispose();
            CmdMap.Dispose();
            IndirectIDGenerator.Dispose();
            IndirectMap.Dispose();

            InstanceIndexAllocator.Dispose();
            InstanceDataAllocator.Dispose();
            MeshletIndexAllocator.Dispose();

            InstanceDescriptorArray.Dispose();
            MeshletDescriptorArray.Dispose();
            CmdDescriptorArray.Dispose();
            BatchDescriptorArray.Dispose();
            InstanceDataArray.Dispose();
            IndirectArgsArray.Dispose();

            AddCache.Dispose();
            RemoveCache.Dispose();
            EnableCache.Dispose();
            QuadTreeAABBInfos->Dispose();
            MemoryUtility.Free(QuadTreeAABBInfos, Allocator.Persistent);
            QuadTreeIndexToRemoveSet->Dispose();
            MemoryUtility.Free(QuadTreeIndexToRemoveSet, Allocator.Persistent);
            InstanceDataDirtySegments.Dispose();
            InstanceDescriptorDirtySegments.Dispose();
            MeshletDescriptorDirtySegments.Dispose();

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
                if (Unmanaged->CmdMap.TryGetValue(id, out IndirectCmd indirectCmd))
                {
                    Unmanaged->CmdMap.Remove(id);
                    Unmanaged->CmdIDGenerator.ReturnID(id);

                    int startIndex = (int)indirectCmd.InstanceIndexChunk.AddressOf();
                    int instanceCount = indirectCmd.InstanceCount;
                    for (int i = 0; i < instanceCount; i++)
                        Unmanaged->QuadTreeIndexToRemoveSet->Add(new int4(startIndex + i, 0, 0, 0));

                    for (int i = 0; i < indirectCmd.LodNum; ++i)
                    {
                        var subMeshInfo = indirectCmd.SubMeshInfos[i];
                        var indirectKey = indirectCmd.IndirectKeys[i];

                        IndirectBatch indirectBatch = Unmanaged->IndirectMap[indirectKey];
                        indirectBatch.MeshletCount -= indirectCmd.InstanceCount * subMeshInfo.MeshletLength;

                        if (indirectBatch.MeshletCount == 0)
                        {
                            Unmanaged->IndirectIDGenerator.ReturnID(indirectBatch.IndirectID);
                            Unmanaged->IndirectMap.Remove(indirectKey);
                        }
                        else
                        {
                            Unmanaged->IndirectMap[indirectKey] = indirectBatch;
                        }

                        Unmanaged->MeshletCount -= indirectCmd.InstanceCount * subMeshInfo.MeshletLength;
                    }

                    Unmanaged->InstanceIndexAllocator.Free(indirectCmd.InstanceIndexChunk);
                    Unmanaged->InstanceDataAllocator.Free(indirectCmd.InstanceDataChunk);
                    foreach (var meshletIndexChunk in indirectCmd.MeshletIndexChunks)
                        Unmanaged->MeshletIndexAllocator.Free(meshletIndexChunk);

                    Unmanaged->InstanceCount -= indirectCmd.InstanceCount;

                    indirectCmd.Dispose();
                }
                else
                {
                    Utility.LogErrorBurst($"invalid user id {id}");
                }
            }
            Unmanaged->RemoveCache.Clear();
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
                int instanceCount = addItem.Matrices.Length;
                Unmanaged->InstanceCount += instanceCount;

                {
                    int meshletCount = 0;
                    foreach (var subMeshInfo in addItem.SubMeshInfos)
                        meshletCount += instanceCount * subMeshInfo.MeshletLength;

                    if (meshletCount + Unmanaged->MeshletCount > Unmanaged->Setting.MeshletCapacity)
                    {
                        Utility.LogErrorBurst("meshlet capacity exceeded");
                        return;
                    }
                }

                for (int i = 0; i < addItem.LodNum; ++i)
                {
                    var subMeshInfo = addItem.SubMeshInfos[i];
                    var indirectKey = addItem.IndirectKeys[i];

                    if (!Unmanaged->IndirectMap.TryGetValue(indirectKey, out var indirectBatch))
                    {
                        if (Unmanaged->IndirectMap.Count >= Unmanaged->Setting.BatchCapacity)
                        {
                            Utility.LogErrorBurst("batch capacity exceeded");
                            return;
                        }

                        indirectBatch = new IndirectBatch()
                        {
                            IndirectID = Unmanaged->IndirectIDGenerator.GetID(),
                            MeshletCount = 0
                        };
                        Unmanaged->MaxIndirectID = math.max(Unmanaged->MaxIndirectID, indirectBatch.IndirectID);

                        Unmanaged->IndirectArgsArray[indirectBatch.IndirectID] = new GraphicsBuffer.IndirectDrawArgs()
                        {
                            vertexCountPerInstance = (uint)Unmanaged->Setting.MeshletTriangleCount * 3,
                            instanceCount = 0,
                            startVertex = 0,
                            startInstance = 0,
                        };
                    }

                    int meshletCount = instanceCount * subMeshInfo.MeshletLength;
                    indirectBatch.MeshletCount += meshletCount;

                    Unmanaged->IndirectMap[indirectKey] = indirectBatch;
                    Unmanaged->MeshletCount += meshletCount;
                }
            }

            Unmanaged->QuadTreeAABBInfos->Clear();
            Unmanaged->InstanceDataDirtySegments.Clear();
            Unmanaged->InstanceDescriptorDirtySegments.Clear();
            Unmanaged->MeshletDescriptorDirtySegments.Clear();
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

            Chunk instanceIndexChunk;
            {
                instanceIndexChunk = Unmanaged->InstanceIndexAllocator.Alloc((UInt32)(instanceCount));
                if (instanceIndexChunk == Chunk.s_InvalidChunk)
                {
                    Utility.LogErrorBurst($"instance index allocation failed, instanceCount={instanceCount}");
                    return;
                }

                int startIndex = (int)instanceIndexChunk.AddressOf();
                AABB aabbLocal = addItem.MaxLodSubMeshInfo.AABB;

                for (int i = 0; i < instanceCount; i++)
                {
                    AABB aabb = AABB.Transform(matrices[i], aabbLocal);

                    Unmanaged->InstanceDescriptorArray[startIndex + i] = new InstanceDescriptor()
                    {
                        Center = aabb.Center,
                        CmdID = addItem.CmdID,
                        Extents = aabb.Extents,
                        Enable = 1,
                    };

                    using (new SimpleSpinLock.AutoLock(Unmanaged->Lock))
                    {
                        Unmanaged->QuadTreeAABBInfos->Add(new QuadTreeAABBInfo
                        {
                            Index = startIndex + i,
                            AABB = aabb,
                            Coord = new int4(0, 0, 0, 0),
                        });
                    }
                }

                using (new SimpleSpinLock.AutoLock(Unmanaged->Lock))
                {
                    Unmanaged->InstanceDescriptorDirtySegments.Add(new OffsetSize
                    {
                        Offset = startIndex,
                        Size = instanceCount
                    });
                }
            }

            Chunk instanceDataChunk;
            int instanceDataOffsetF4;
            int instanceSizeF4 = Utility.c_SizeOfPackedMatrixF4 + (needInverse ? Utility.c_SizeOfPackedMatrixF4 : 0) + properties.Length;
            int instanceSize = instanceSizeF4 * Utility.c_SizeOfFloat4;
            {
                instanceDataChunk = Unmanaged->InstanceDataAllocator.Alloc((UInt32)(instanceSize * instanceCount));
                if (instanceDataChunk == Chunk.s_InvalidChunk)
                {
                    Utility.LogErrorBurst($"instance data allocation failed, instanceDataAllocSize={instanceSize * instanceCount}");
                    return;
                }

                instanceDataOffsetF4 = (int)instanceDataChunk.AddressOf() / Utility.c_SizeOfFloat4;

                for (int i = 0; i < instanceCount; i++)
                {
                    float4x4 matrix = matrices[i];
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
                        SizeF4 = instanceSizeF4 * instanceCount
                    });
                }
            }

            UnsafeList<Chunk> meshletIndexChunks = new UnsafeList<Chunk>(addItem.LodNum, Allocator.Persistent);
            UnsafeList<int2> meshletIndexInfos = new UnsafeList<int2>(Utility.c_MaxLodNum, Allocator.Temp);
            for (int iLod = 0; iLod < addItem.LodNum; ++iLod)
            {
                var subMeshInfo = addItem.SubMeshInfos[iLod];
                var indirectKey = addItem.IndirectKeys[iLod];

                int indirectID = Unmanaged->IndirectMap[indirectKey].IndirectID;
                int meshletCount = instanceCount * subMeshInfo.MeshletLength;

                Chunk meshletIndexChunk = Unmanaged->MeshletIndexAllocator.Alloc((UInt32)meshletCount);
                if (meshletIndexChunk == Chunk.s_InvalidChunk)
                {
                    Utility.LogErrorBurst($"meshlet index allocation failed, meshletCount={meshletCount}");
                    return;
                }

                int startIndex = (int)meshletIndexChunk.AddressOf();

                for (int i = 0; i < meshletCount; i++)
                {
                    int instanceIndex = i % instanceCount;
                    int meshletIndex = i / instanceCount;

                    MeshletInfo meshletInfo = subMeshInfo.MeshletInfos[meshletIndex];
                    AABB aabb = AABB.Transform(matrices[instanceIndex], meshletInfo.AABB);

                    Unmanaged->MeshletDescriptorArray[startIndex + i] = new MeshletDescriptor()
                    {
                        Center = aabb.Center,
                        IndirectID = indirectID,
                        Extents = aabb.Extents,
                        DataOffset = (instanceDataOffsetF4 + instanceIndex * instanceSizeF4) * Utility.c_SizeOfFloat4,
                        IndexOffset = meshletInfo.IndexOffset,
                        VertexOffset = meshletInfo.VertexOffset,
                        NeedInverse = needInverse ? 1 : 0,
                        Pad = 0,
                    };
                }

                using (new SimpleSpinLock.AutoLock(Unmanaged->Lock))
                {
                    Unmanaged->MeshletDescriptorDirtySegments.Add(new OffsetSize
                    {
                        Offset = startIndex,
                        Size = meshletCount
                    });
                }

                meshletIndexChunks.Add(meshletIndexChunk);
                meshletIndexInfos.Add(new int2(startIndex, subMeshInfo.MeshletLength));
            }

            for (int i = addItem.LodNum; i < Utility.c_MaxLodNum; ++i)
                meshletIndexInfos.Add(new int2(0, 0));

            UnsafeList<SubMeshInfo> subMeshInfos = new UnsafeList<SubMeshInfo>(addItem.SubMeshInfos.Length, Allocator.Persistent);
            for (int i = 0; i < addItem.SubMeshInfos.Length; ++i)
                subMeshInfos.Add(addItem.SubMeshInfos[i]);

            UnsafeList<IndirectKey> indirectKeys = new UnsafeList<IndirectKey>(addItem.IndirectKeys.Length, Allocator.Persistent);
            for (int i = 0; i < addItem.IndirectKeys.Length; ++i)
                indirectKeys.Add(addItem.IndirectKeys[i]);

            IndirectCmd indirectCmd = new IndirectCmd
            {
                SubMeshInfos = subMeshInfos,
                IndirectKeys = indirectKeys,
                InstanceCount = instanceCount,
                EnableCount = instanceCount,
                InstanceIndexChunk = instanceIndexChunk,
                InstanceDataChunk = instanceDataChunk,
                MeshletIndexChunks = meshletIndexChunks,
            };

            Unmanaged->CmdDescriptorArray[addItem.CmdID] = new CmdDescriptor()
            {
                InstanceStartIndex = (int)instanceIndexChunk.AddressOf(),
                InstanceCount = instanceCount,
                MaxLod = addItem.MaxLod,
                Pad = 0,
                MeshletStartIndices = new int4(meshletIndexInfos[0].x, meshletIndexInfos[1].x, meshletIndexInfos[2].x, meshletIndexInfos[3].x),
                MeshletLengths = new int4(meshletIndexInfos[0].y, meshletIndexInfos[1].y, meshletIndexInfos[2].y, meshletIndexInfos[3].y),
                LodParam = addItem.LodParam,
            };

            using (new SimpleSpinLock.AutoLock(Unmanaged->Lock))
            {
                Unmanaged->CmdMap.Add(addItem.CmdID, indirectCmd);
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
                addItem.SubMeshInfos.Dispose();
                addItem.IndirectKeys.Dispose();
                addItem.Matrices.Dispose();
                for (int i = 0; i < addItem.Properties.Length; ++i)
                    addItem.Properties[i].Dispose();
                addItem.Properties.Dispose();
            }
            Unmanaged->AddCache.Clear();
        }
    }

#if ENABLE_BURST
    [BurstCompile(CompileSynchronously = true)]
#endif
    public unsafe struct FlushEnableJob : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        public IndirectRenderUnmanaged* Unmanaged;

        public void Execute()
        {
            foreach (var pair in Unmanaged->EnableCache)
            {
                int cmdID = pair.Key.x;
                int index = pair.Key.y;
                int enable = pair.Value ? 1 : 0;

                if (Unmanaged->CmdMap.TryGetValue(cmdID, out IndirectCmd indirectCmd))
                {
                    int startIndex = (int)indirectCmd.InstanceIndexChunk.AddressOf();
                    int instanceCount = indirectCmd.InstanceCount;
                    if (index < instanceCount)
                    {
                        var des = Unmanaged->InstanceDescriptorArray[startIndex + index];

                        if (des.Enable == 0 && enable == 1)
                            indirectCmd.EnableCount++;
                        else if (des.Enable == 1 && enable == 0)
                            indirectCmd.EnableCount--;

                        des.Enable = enable;
                        Unmanaged->InstanceDescriptorArray[startIndex + index] = des;

                        Unmanaged->InstanceDescriptorDirtySegments.Add(new OffsetSize
                        {
                            Offset = startIndex + index,
                            Size = 1
                        });
                    }

                    Unmanaged->CmdMap[cmdID] = indirectCmd;
                }
            }
            Unmanaged->EnableCache.Clear();

            NativeList<int> cmdToRemove = new NativeList<int>(Allocator.Temp);
            foreach (var pair in Unmanaged->CmdMap)
            {
                int cmdID = pair.Key;
                IndirectCmd indirectCmd = pair.Value;

                if (indirectCmd.EnableCount == 0)
                    Unmanaged->RemoveCache.Add(cmdID);
            }
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
                IndirectBatch indirectBatch = pair.Value;
                int indirectID = indirectBatch.IndirectID;

                Unmanaged->BatchDescriptorArray[indirectID] = new BatchDescriptor
                {
                    Offset = instanceOffset,
                    Pad = new int3(0, 0, 0),
                };
                instanceOffset += indirectBatch.MeshletCount;
            }
        }
    }
}