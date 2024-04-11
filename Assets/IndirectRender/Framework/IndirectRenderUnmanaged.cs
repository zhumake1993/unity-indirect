#define ENABLE_BURST

using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace ZGame.Indirect
{
    public struct IndirectRenderUnmanaged
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

        public NativeArray<int4> IndexSegmentArray;
        public NativeArray<InstanceDescriptor> InstanceDescriptorArray;
        public NativeArray<int4> BatchDescriptorArray;
        public NativeArray<float4> InstanceDataArray;
        public NativeArray<GraphicsBuffer.IndirectDrawArgs> IndirectArgsArray;

        public int MaxIndirectID;
        public int IndexSegmentCount;
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

            IndexSegmentArray = new NativeArray<int4>(setting.IndexSegmentCapacity, Allocator.Persistent);
            InstanceDescriptorArray = new NativeArray<InstanceDescriptor>(setting.InstanceCapacity, Allocator.Persistent);
            BatchDescriptorArray = new NativeArray<int4>(setting.BatchCapacity, Allocator.Persistent);
            InstanceDataArray = new NativeArray<float4>((int)(setting.InstanceDataMaxSizeBytes * setting.InstanceDataNumMaxSizeBlocks) / Utility.c_SizeOfFloat4, Allocator.Persistent);
            IndirectArgsArray = new NativeArray<GraphicsBuffer.IndirectDrawArgs>(setting.BatchCapacity, Allocator.Persistent);

            MaxIndirectID = 0;
            IndexSegmentCount = 0;
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

            IndexSegmentArray.Dispose();
            InstanceDescriptorArray.Dispose();
            BatchDescriptorArray.Dispose();
            InstanceDataArray.Dispose();
            IndirectArgsArray.Dispose();
        }

        static readonly ProfilerMarker s_addBatchImplMarker = new ProfilerMarker("IndirectRender.AddBatchImpl");
        public int AddBatchImpl(IndirectKey indirectKey, int meshID, bool needInverse, UnsafeList<float4x4> matrices, UnsafeList<UnsafeList<float4>> properties,
            AssetManager assetManager, GraphicsBuffer instanceDataBuffer, GraphicsBuffer instanceDescriptorBuffer, QuadTreeBuildPass quadTreeBuildPass)
        {
            using (s_addBatchImplMarker.Auto())
            {
                assetManager.AddShaderLayout(indirectKey.MaterialID, new ShaderLayout
                {
                    NeedInverse = needInverse,
                    PeopertyCount = properties.Length
                });

                int instanceCount = matrices.Length;
                MeshInfo meshInfo = assetManager.GetMeshInfo(meshID);
                List<UnitMeshInfo> unitMeshInfos = meshInfo.UnitMeshInfos;
                int unitMeshCount = unitMeshInfos.Count;
                int actualInstanceCount = instanceCount * unitMeshCount;

                if (actualInstanceCount + TotalActualInstanceCount > Setting.InstanceCapacity)
                {
                    Utility.LogError($"instance capacity exceeded," +
                        $" instanceCount={instanceCount}, unitMeshCount={unitMeshCount}" +
                        $" totalInstanceCount={TotalActualInstanceCount}, instanceCapacity={Setting.InstanceCapacity}");

                    return -1;
                }

                if (!IndirectMap.TryGetValue(indirectKey, out var indirectBatch))
                {
                    if (IndirectMap.Count == Setting.BatchCapacity)
                    {
                        Utility.LogError($"batch capacity exceeded," +
                            $" batchCount={IndirectMap.Count}, batchCapacity={Setting.BatchCapacity}");

                        return -1;
                    }

                    indirectBatch = new IndirectBatch()
                    {
                        IndirectID = IndirectIDGenerator.GetID(),
                        ActualInstanceCount = 0
                    };
                    MaxIndirectID = math.max(MaxIndirectID, indirectBatch.IndirectID);
                }

                int instanceSizeF4 = Utility.c_SizeOfPackedMatrixF4 + (needInverse ? Utility.c_SizeOfPackedMatrixF4 : 0) + properties.Length;
                int instanceSize = instanceSizeF4 * Utility.c_SizeOfFloat4;
                int maxInstanceCountPerCmd = math.min((int)Setting.MaxInstanceCountPerCmd, (int)Setting.InstanceDataMaxSizeBytes / instanceSize); ;

                int indirectCmdInfoCount = (instanceCount + maxInstanceCountPerCmd - 1) / maxInstanceCountPerCmd;
                UnsafeList<int> cmdIDs = new UnsafeList<int>(indirectCmdInfoCount, Allocator.Persistent);

                int left = instanceCount;
                int offset = 0;
                while (left > 0)
                {
                    int count = math.min(left, maxInstanceCountPerCmd);
                    left -= count;

                    // instance data

                    Chunk instanceDataChunk = InstanceDataAllocator.Alloc((UInt32)(instanceSize * count));
                    if (instanceDataChunk == Chunk.s_InvalidChunk)
                    {
                        Utility.LogError($"instance data allocation failed," +
                            $" instanceDataAllocSize={instanceSize * count}");

                        return -1;
                    }

                    int instanceDataOffsetF4 = (int)instanceDataChunk.AddressOf() / Utility.c_SizeOfFloat4;

                    for (int i = 0; i < count; i++)
                    {
                        float4x4 matrix = matrices[offset + i];
                        InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 0] = new float4(matrix[0][0], matrix[0][1], matrix[0][2], matrix[1][0]);
                        InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 1] = new float4(matrix[1][1], matrix[1][2], matrix[2][0], matrix[2][1]);
                        InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 2] = new float4(matrix[2][2], matrix[3][0], matrix[3][1], matrix[3][2]);

                        if (needInverse)
                        {
                            float4x4 inverseMatrix = math.inverse(matrix);
                            InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 3] = new float4(inverseMatrix[0][0], inverseMatrix[0][1], inverseMatrix[0][2], inverseMatrix[1][0]);
                            InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 4] = new float4(inverseMatrix[1][1], inverseMatrix[1][2], inverseMatrix[2][0], inverseMatrix[2][1]);
                            InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 5] = new float4(inverseMatrix[2][2], inverseMatrix[3][0], inverseMatrix[3][1], inverseMatrix[3][2]);
                        }

                        for (int j = 0; j < properties.Length; ++j)
                        {
                            InstanceDataArray[instanceDataOffsetF4 + i * instanceSizeF4 + 3 + (needInverse ? 3 : 0) + j] = properties[j][i];
                        }
                    }

                    instanceDataBuffer.SetData(InstanceDataArray, instanceDataOffsetF4, instanceDataOffsetF4, instanceSizeF4 * count);

                    IndirectCmdInfo indirectCmdInfo = new IndirectCmdInfo
                    {
                        IndirectKey = indirectKey,
                        MeshID = meshID,
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

                        int instanceIndicesAllocSize = math.max((int)Setting.MinInstanceCountPerCmd, count);
                        Chunk instanceIndicesChunk = InstanceIndicesAllocator.Alloc((UInt32)(instanceIndicesAllocSize));
                        if (instanceIndicesChunk == Chunk.s_InvalidChunk)
                        {
                            Utility.LogError($"instance indices allocation failed," +
                                $" instanceIndicesAllocSize={instanceIndicesAllocSize}");

                            return -1;
                        }

                        int startInstanceIndex = (int)instanceIndicesChunk.AddressOf();

                        for (int i = 0; i < count; i++)
                        {
                            AABB aabb = AABB.Transform(matrices[offset + i], aabbLocal);

                            quadTreeBuildPass.CalculateQuadTreeNodeCoordAndLod(aabb, out int4 nodeCoordAndLod, out int4 subNodeMask);

                            InstanceDescriptor instanceDescriptor = new InstanceDescriptor()
                            {
                                Center_IndirectID = new float4(aabb.Center, indirectBatch.IndirectID),
                                Extents_DataOffset = new float4(aabb.Extents, (instanceDataOffsetF4 + i * instanceSizeF4) * Utility.c_SizeOfFloat4),
                                UnitMeshInfo = new int4(unitMeshInfo.IndexOffset, unitMeshInfo.VertexOffset, needInverse ? 1 : 0, 0),
                                QuadTreeNodeCoordAndLod = nodeCoordAndLod,
                                QuadTreeSubNodeMask= subNodeMask
                            };

                            InstanceDescriptorArray[startInstanceIndex + i] = instanceDescriptor;
                        }

                        instanceDescriptorBuffer.SetData(InstanceDescriptorArray, startInstanceIndex, startInstanceIndex, count);

                        IndirectSubCmdInfo indirectSubCmdInfo = new IndirectSubCmdInfo
                        {
                            StartInstanceIndex = startInstanceIndex,
                            InstanceIndicesChunk = instanceIndicesChunk
                        };

                        indirectCmdInfo.SubCmds[iUnitMesh] = indirectSubCmdInfo;
                    }

                    int cmdID = CmdIDGenerator.GetID();
                    cmdIDs.Add(cmdID);
                    CmdMap.Add(cmdID, indirectCmdInfo);

                    offset += count;
                }

                indirectBatch.ActualInstanceCount += actualInstanceCount;
                IndirectMap[indirectKey] = indirectBatch;

                int userID = UserIDGenerator.GetID();
                UserIdToCmdIDs.Add(userID, cmdIDs);

                TotalActualInstanceCount += actualInstanceCount;

                return userID;
            }
        }

        public void RemoveBatch(int id)
        {
            if (UserIdToCmdIDs.TryGetValue(id, out UnsafeList<int> cmdIDs))
            {
                foreach (int cmdID in cmdIDs)
                    RemoveCmd(cmdID);
                cmdIDs.Dispose();

                UserIdToCmdIDs.Remove(id);
            }
            else
            {
                Utility.LogError($"invalid user id {id}");
            }
        }

        void RemoveCmd(int id)
        {
            if (CmdMap.TryGetValue(id, out IndirectCmdInfo cmdInfo))
            {
                CmdMap.Remove(id);
                CmdIDGenerator.ReturnID(id);

                IndirectBatch indirectBatch = IndirectMap[cmdInfo.IndirectKey];
                indirectBatch.ActualInstanceCount -= cmdInfo.InstanceCount * cmdInfo.SubCmds.Length;

                if (indirectBatch.ActualInstanceCount == 0)
                {
                    IndirectIDGenerator.ReturnID(indirectBatch.IndirectID);
                    IndirectMap.Remove(cmdInfo.IndirectKey);
                }
                else
                {
                    IndirectMap[cmdInfo.IndirectKey] = indirectBatch;
                }

                TotalActualInstanceCount -= cmdInfo.InstanceCount * cmdInfo.SubCmds.Length;

                InstanceDataAllocator.Free(cmdInfo.InstanceDataChunk);

                foreach (var subCmd in cmdInfo.SubCmds)
                    InstanceIndicesAllocator.Free(subCmd.InstanceIndicesChunk);

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
    public unsafe struct DispatchJob : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        public IndirectRenderUnmanaged* Unmanaged;
        [ReadOnly]
        public int UnitMeshIndexCount;

        public void Execute()
        {
            UpdateIndexSegment();
            UpdateBatchDescriptor();
            UpdateIndirectArgs();
        }

        static readonly ProfilerMarker s_updateIndexSegmentMarker = new ProfilerMarker("DispatchJob.UpdateIndexSegment");
        void UpdateIndexSegment()
        {
            using (s_updateIndexSegmentMarker.Auto())
            {
                Unmanaged->IndexSegmentCount = 0;
                foreach (var pair in Unmanaged->CmdMap)
                {
                    IndirectCmdInfo indirectCmdInfo = pair.Value;
                    int instanceCount = indirectCmdInfo.InstanceCount;

                    foreach (IndirectSubCmdInfo subCmd in indirectCmdInfo.SubCmds)
                    {
                        int startIndex = subCmd.StartInstanceIndex;

                        int left = instanceCount;
                        int offset = 0;
                        while (left > 0)
                        {
                            if (Unmanaged->IndexSegmentCount >= Unmanaged->Setting.IndexSegmentCapacity)
                            {
                                Debug.LogError($"index segment capacity exceeded, indexSegmentCount={Unmanaged->IndexSegmentCount}, indexSegmentCapacity={Unmanaged->Setting.IndexSegmentCapacity}");

                                return;
                            }

                            int count = math.min(left, 64);
                            left -= count;

                            Unmanaged->IndexSegmentArray[Unmanaged->IndexSegmentCount++] = new int4(startIndex + offset, count, 0, 0);

                            offset += count;
                        }
                    }
                }
            }
        }

        static readonly ProfilerMarker s_updateBatchDescriptorMarker = new ProfilerMarker("DispatchJob.UpdateBatchDescriptor");
        void UpdateBatchDescriptor()
        {
            using (s_updateBatchDescriptorMarker.Auto())
            {
                int instanceOffset = 0;
                foreach (var pair in Unmanaged->IndirectMap)
                {
                    IndirectKey indirectKey = pair.Key;
                    IndirectBatch indirectBatch = pair.Value;
                    int indirectID = indirectBatch.IndirectID;

                    Unmanaged->BatchDescriptorArray[indirectID] = new int4(instanceOffset, indirectBatch.ActualInstanceCount, 0, 0);
                    instanceOffset += indirectBatch.ActualInstanceCount * Utility.c_MaxCullingSet;
                }
            }
        }

        static readonly ProfilerMarker s_updateIndirectArgsMarker = new ProfilerMarker("DispatchJob.UpdateIndirectArgs");
        void UpdateIndirectArgs()
        {
            using (s_updateIndirectArgsMarker.Auto())
            {
                foreach (var pair in Unmanaged->IndirectMap)
                {
                    IndirectBatch indirectBatch = pair.Value;

                    int indirectID = indirectBatch.IndirectID;

                    GraphicsBuffer.IndirectDrawArgs indirectDrawArgs = new GraphicsBuffer.IndirectDrawArgs()
                    {
                        vertexCountPerInstance = (uint)UnitMeshIndexCount,
                        instanceCount = 0,
                        startVertex = 0,
                        startInstance = 0,
                    };

                    Unmanaged->IndirectArgsArray[indirectID * Utility.c_MaxCullingSet + 0] = indirectDrawArgs;
                    Unmanaged->IndirectArgsArray[indirectID * Utility.c_MaxCullingSet + 1] = indirectDrawArgs;
                    Unmanaged->IndirectArgsArray[indirectID * Utility.c_MaxCullingSet + 2] = indirectDrawArgs;
                    Unmanaged->IndirectArgsArray[indirectID * Utility.c_MaxCullingSet + 3] = indirectDrawArgs;
                    Unmanaged->IndirectArgsArray[indirectID * Utility.c_MaxCullingSet + 4] = indirectDrawArgs;
                }
            }
        }
    }
}