using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public unsafe partial class IndirectRender
    {
        public static IndirectRender s_Instance => s_instance;

        IndirectRenderUnmanaged* _unmanaged;

        BatchRendererGroup _brg;
        MeshMerger _meshMerger = new MeshMerger();
        AssetManager _assetManager = new AssetManager();
        BufferManager _bufferManager = new BufferManager();
        DispatchHelper _dispatchHelper = new DispatchHelper();
        IndirectPipeline _indirectPipeline = new IndirectPipeline();
        CullingHelper _cullingHelper = new CullingHelper();

        QuadTree _quadTree = new QuadTree();

        CommandBuffer _cmd = new CommandBuffer();
        MaterialPropertyBlock _mpb;

        JobHandle _dispatchJobHandle = new JobHandle();

        bool _draw = true;

        static IndirectRender s_instance = null;

        static readonly int s_indirectIndexBufferID = Shader.PropertyToID("IndirectIndexBuffer");
        static readonly int s_indirectVertexBufferID = Shader.PropertyToID("IndirectVertexBuffer");

        public bool Init(IndirectRenderSetting setting, ComputeShader indirectPipelineCS, ComputeShader adjustDispatchArgCS)
        {
            if (!CheckSetting(setting))
                return false;

            _unmanaged = MemoryUtility.Malloc<IndirectRenderUnmanaged>(Allocator.Persistent);
            _unmanaged->Init(setting);

            _brg = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
            _meshMerger.Init(setting.IndexCapacity, setting.VertexCapacity, setting.MeshletTriangleCount);
            _assetManager.Init(_meshMerger, _brg);
            _bufferManager.Init(setting);
            _dispatchHelper.Init(adjustDispatchArgCS);
            _indirectPipeline.Init(setting, indirectPipelineCS, _bufferManager, _dispatchHelper);
            _cullingHelper.Init();

            _quadTree.Init(setting);

            _mpb = new MaterialPropertyBlock();
            _mpb.SetBuffer(BufferManager.s_MeshletDescriptorBufferID, _bufferManager.MeshletDescriptorBuffer);
            _mpb.SetBuffer(BufferManager.s_BatchDescriptorBufferID, _bufferManager.BatchDescriptorBuffer);
            _mpb.SetBuffer(BufferManager.s_InstanceDataBufferID, _bufferManager.InstanceDataBuffer);
            _mpb.SetBuffer(s_indirectVertexBufferID, _meshMerger.GetVertexBuffer());
            _mpb.SetBuffer(s_indirectIndexBufferID, _meshMerger.GetIndexBuffer());

            _brg.SetIndirect();
            _brg.SetIndirectProperties(_mpb);

            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.endContextRendering += OnEndContextRendering;

            s_instance = this;

            return true;
        }

        bool CheckSetting(IndirectRenderSetting setting)
        {
            if (setting.QuadTreeSetting.MaxLodRange.y != 1)
            {
                Utility.LogError("MaxLodRange.y must be 1");
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            _quadTree.Dispose();

            _cullingHelper.Dispose();
            _indirectPipeline.Dispose();
            _dispatchHelper.Dispose();
            _bufferManager.Dispose();
            _assetManager.Dispose();
            _meshMerger.Dispose();
            _brg.Dispose();

            _unmanaged->Dispose();
            MemoryUtility.Free(_unmanaged, Allocator.Persistent);

            _cmd.Dispose();

            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
        }

        void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            Prepare();
        }

        void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            _bufferManager.Recycle();
        }

        public int RegisterMesh(MeshKey meshKey)
        {
            return _assetManager.RegisterMesh(meshKey);
        }

        public int RegisterMaterial(Material material)
        {
            return _assetManager.RegisterMaterial(material);
        }

        public Material GetMaterial(int id)
        {
            return _assetManager.GetMaterial(id);
        }

        bool CheckBatch(UnsafeList<int> meshIDs, UnsafeList<IndirectKey> indirectKeys, UnsafeList<float4x4> matrices, UnsafeList<UnsafeList<float4>> properties)
        {
            if (meshIDs.Length == 0)
            {
                Utility.LogError("meshIDs.Length == 0");
                return false;
            }

            if (meshIDs.Length != indirectKeys.Length)
            {
                Utility.LogError("meshIDs.Length != indirectKeys.Length");
                return false;
            }

            for (int i = 0; i < meshIDs.Length; ++i)
            {
                if (meshIDs[i] < 0)
                {
                    Utility.LogError($"meshIDs[{i}] < 0");
                    return false;
                }

                if (indirectKeys[i].MaterialID < 0)
                {
                    Utility.LogError($"indirectKeys[{i}].MaterialID < 0");
                    return false;
                }
            }

            return true;
        }

        public int AddBatch(UnsafeList<int> meshIDs, UnsafeList<IndirectKey> indirectKeys, float4 lodParam, bool needInverse, UnsafeList<float4x4> matrices, UnsafeList<UnsafeList<float4>> properties)
        {
            if (!CheckBatch(meshIDs, indirectKeys, matrices, properties))
            {
                return -1;
            }

            foreach (var indirectKey in indirectKeys)
            {
                _assetManager.AddShaderLayout(indirectKey.MaterialID, new ShaderLayout
                {
                    NeedInverse = needInverse,
                    PeopertyCount = properties.Length
                });
            }

            UnsafeList<MeshInfo> meshInfos = new UnsafeList<MeshInfo>(meshIDs.Length, Allocator.TempJob);
            for (int i = 0; i < meshIDs.Length; ++i)
                meshInfos.Add(_assetManager.GetMeshInfo(meshIDs[i]));
            meshIDs.Dispose();

            AddItem addItem = new AddItem
            {
                CmdID = _unmanaged->CmdIDGenerator.GetID(),
                MeshInfos = meshInfos,
                IndirectKeys = indirectKeys,
                LodParam = lodParam,
                NeedInverse = needInverse,
                Matrices = matrices,
                Properties = properties,
            };
            _unmanaged->MaxCmdID = math.max(_unmanaged->MaxCmdID, addItem.CmdID);

            _unmanaged->AddCache.Add(addItem);

            return addItem.CmdID;
        }

        public void RemoveBatch(int id)
        {
            if (id < 0)
                return;

            _unmanaged->RemoveCache.Add(id);
        }

        bool ShouldDraw()
        {
            return _draw && _unmanaged->CmdMap.Count > 0;
        }

        static readonly ProfilerMarker s_dispatchMarker = new ProfilerMarker("IndirectRender.Dispatch");
        public void Dispatch()
        {
            using (s_dispatchMarker.Auto())
            {
                JobHandle jobHandle = default;

                if (_unmanaged->RemoveCache.Length > 0)
                {
                    var flushRemoveJob = new FlushRemoveJob
                    {
                        Unmanaged = _unmanaged,
                        QuadTree = _quadTree,
                    };
                    jobHandle = flushRemoveJob.Schedule(jobHandle);

                    jobHandle = _quadTree.DispatchDeleteJob(_unmanaged->QuadTreeIndexToRemoveSet, jobHandle);
                }

                if (_unmanaged->AddCache.Length > 0)
                {
                    var preFlushAddJob = new PreFlushAddJob
                    {
                        Unmanaged = _unmanaged,
                    };
                    jobHandle = preFlushAddJob.Schedule(jobHandle);

                    var flushAddJob = new FlushAddJob
                    {
                        Unmanaged = _unmanaged,
                    };
                    jobHandle = flushAddJob.Schedule(_unmanaged->AddCache.Length, 1, jobHandle);

                    var postFlushAddJob = new PostFlushAddJob
                    {
                        Unmanaged = _unmanaged,
                    };
                    jobHandle = postFlushAddJob.Schedule(jobHandle);

                    jobHandle = _quadTree.DispatchAddJob(_unmanaged->QuadTreeAABBInfos, jobHandle);
                }

                var updateBatchDescriptorJob = new UpdateBatchDescriptorJob
                {
                    Unmanaged = _unmanaged,
                };
                _dispatchJobHandle = updateBatchDescriptorJob.Schedule(jobHandle);
            }
        }

        static readonly ProfilerMarker s_prepareMarker = new ProfilerMarker("IndirectRender.Prepare");
        void Prepare()
        {
            using (s_prepareMarker.Auto())
            {
                _dispatchJobHandle.Complete();

                foreach (var segment in _unmanaged->InstanceDataDirtySegments)
                    _bufferManager.InstanceDataBuffer.SetData(_unmanaged->InstanceDataArray, segment.OffsetF4, segment.OffsetF4, segment.SizeF4);
                _unmanaged->InstanceDataDirtySegments.Clear();

                foreach (var segment in _unmanaged->InstanceDescriptorDirtySegments)
                    _bufferManager.InstanceDescriptorBuffer.SetData(_unmanaged->InstanceDescriptorArray, segment.Offset, segment.Offset, segment.Size);
                _unmanaged->InstanceDescriptorDirtySegments.Clear();

                foreach (var segment in _unmanaged->MeshletDescriptorDirtySegments)
                    _bufferManager.MeshletDescriptorBuffer.SetData(_unmanaged->MeshletDescriptorArray, segment.Offset, segment.Offset, segment.Size);
                _unmanaged->MeshletDescriptorDirtySegments.Clear();

                _bufferManager.CmdDescriptorBuffer.SetData(_unmanaged->CmdDescriptorArray, 0, 0, _unmanaged->MaxCmdID + 1);
                _bufferManager.BatchDescriptorBuffer.SetData(_unmanaged->BatchDescriptorArray, 0, 0, _unmanaged->MaxIndirectID + 1);

                _brg.SetIndirectCmdCount(_unmanaged->IndirectMap.Count);
            }
        }

        static readonly ProfilerMarker s_cullingCallbackMarker = new ProfilerMarker("IndirectRender.CullingCallback");
        JobHandle CullingCallback(ref BatchCullingContext cullingContext, BatchCullingOutput cullingOutput)
        {
            using (s_cullingCallbackMarker.Auto())
            {
                _cmd.Clear();

                _cullingHelper.UpdateCullinglanes(ref cullingContext);

                if (cullingContext.viewType == BatchCullingViewType.Camera)
                {
                    UnsafeList<int4>* visibleIndices = MemoryUtility.Malloc<UnsafeList<int4>>(Allocator.TempJob);
                    UnsafeList<int4>* partialIndices = MemoryUtility.Malloc<UnsafeList<int4>>(Allocator.TempJob);
                    JobHandle jobHandle = _quadTree.Cull(_cullingHelper.GetCullinglanes(0), visibleIndices, partialIndices);

                    _cmd.name = $"Indirect.Camera";

                    GraphicsBuffer inputIndexBuffer = _bufferManager.InputIndexBufferPool.Get();
                    GraphicsBuffer outputIndexBuffer = _bufferManager.OutputIndexBufferPool.Get();
                    GraphicsBuffer visibilityBuffer = _bufferManager.VisibilityBufferPool.Get();
                    GraphicsBuffer indirectArgsBuffer = _bufferManager.IndirectArgsBufferPool.Get();

                    indirectArgsBuffer.SetData(_unmanaged->IndirectArgsArray, 0, 0, (_unmanaged->MaxIndirectID + 1));

                    {
                        BatchCullingOutputDrawCommands outputDrawCommand = new BatchCullingOutputDrawCommands();

                        outputDrawCommand.indirectDrawCommands = MemoryUtility.MallocNoTrack<BatchDrawCommandIndirect>(_unmanaged->IndirectMap.Count, Allocator.TempJob);
                        outputDrawCommand.indirectDrawCommandCount = _unmanaged->IndirectMap.Count;

                        int indirectIndex = 0;
                        foreach (var pair in _unmanaged->IndirectMap)
                        {
                            IndirectKey indirectKey = pair.Key;
                            IndirectBatch indirectBatch = pair.Value;

                            BatchMaterialID batchMaterialID = _assetManager.GetBatchMaterialID(indirectKey.MaterialID);
                            int indirectID = indirectBatch.IndirectID;

                            BatchDrawCommandIndirect indirectCmd = new BatchDrawCommandIndirect()
                            {
                                topology = MeshTopology.Triangles,
                                materialID = batchMaterialID,
                                visibilityBufferHandle = visibilityBuffer.bufferHandle,
                                indirectArgsBufferHandle = indirectArgsBuffer.bufferHandle,
                                indirectArgsOffset = (uint)indirectID,
                                renderingLayerMask = 0xffffffff,
                                layer = indirectKey.Layer,
                                shadowCastingMode = indirectKey.ShadowCastingMode,
                                receiveShadows = indirectKey.ReceiveShadows,
                                splitVisibilityMask = 0,
                            };

                            outputDrawCommand.indirectDrawCommands[indirectIndex++] = indirectCmd;
                        }

                        cullingOutput.drawCommands[0] = outputDrawCommand;
                    }

                    jobHandle.Complete();

                    _cullingHelper.BuildCommandBuffer(_cmd, _indirectPipeline.IndirectPipelineCS, 0);
                    _indirectPipeline.SerLodParam(_cmd, true);
                    _indirectPipeline.BuildCommandBuffer(_cmd, visibilityBuffer, indirectArgsBuffer, inputIndexBuffer, outputIndexBuffer, *visibleIndices, *partialIndices);

                    visibleIndices->Dispose();
                    partialIndices->Dispose();
                    MemoryUtility.Free(visibleIndices, Allocator.TempJob);
                    MemoryUtility.Free(partialIndices, Allocator.TempJob);
                }
                else if (cullingContext.viewType == BatchCullingViewType.Light)
                {
                    _cmd.name = $"Indirect.Shadow";

                    int splitCount = cullingContext.cullingSplits.Length;

                    UnsafeList<int4>** visibleIndicesArray = stackalloc UnsafeList<int4>*[splitCount];
                    UnsafeList<int4>** partialIndicesArray = stackalloc UnsafeList<int4>*[splitCount];
                    NativeArray<JobHandle> jobHandles = new NativeArray<JobHandle>(splitCount, Allocator.Temp);

                    for (int iSplit = 0; iSplit < splitCount; ++iSplit)
                    {
                        UnsafeList<int4>* visibleIndices = MemoryUtility.Malloc<UnsafeList<int4>>(Allocator.TempJob);
                        UnsafeList<int4>* partialIndices = MemoryUtility.Malloc<UnsafeList<int4>>(Allocator.TempJob);
                        jobHandles[iSplit] = _quadTree.Cull(_cullingHelper.GetCullinglanes(0), visibleIndices, partialIndices);
                        visibleIndicesArray[iSplit] = visibleIndices;
                        partialIndicesArray[iSplit] = partialIndices;
                    }

                    BatchCullingOutputDrawCommands outputDrawCommand = new BatchCullingOutputDrawCommands();

                    outputDrawCommand.indirectDrawCommands = MemoryUtility.MallocNoTrack<BatchDrawCommandIndirect>(_unmanaged->IndirectMap.Count * splitCount, Allocator.TempJob);
                    outputDrawCommand.indirectDrawCommandCount = _unmanaged->IndirectMap.Count * splitCount;
                    int indirectIndex = 0;

                    for (int iSplit = 0; iSplit < splitCount; ++iSplit)
                    {
                        GraphicsBuffer inputIndexBuffer = _bufferManager.InputIndexBufferPool.Get();
                        GraphicsBuffer outputIndexBuffer = _bufferManager.OutputIndexBufferPool.Get();
                        GraphicsBuffer visibilityBuffer = _bufferManager.VisibilityBufferPool.Get();
                        GraphicsBuffer indirectArgsBuffer = _bufferManager.IndirectArgsBufferPool.Get();

                        indirectArgsBuffer.SetData(_unmanaged->IndirectArgsArray, 0, 0, (_unmanaged->MaxIndirectID + 1));

                        foreach (var pair in _unmanaged->IndirectMap)
                        {
                            IndirectKey indirectKey = pair.Key;
                            IndirectBatch indirectBatch = pair.Value;

                            BatchMaterialID batchMaterialID = _assetManager.GetBatchMaterialID(indirectKey.MaterialID);
                            int indirectID = indirectBatch.IndirectID;

                            BatchDrawCommandIndirect indirectCmd = new BatchDrawCommandIndirect()
                            {
                                topology = MeshTopology.Triangles,
                                materialID = batchMaterialID,
                                visibilityBufferHandle = visibilityBuffer.bufferHandle,
                                indirectArgsBufferHandle = indirectArgsBuffer.bufferHandle,
                                indirectArgsOffset = (uint)indirectID,
                                renderingLayerMask = 0xffffffff,
                                layer = indirectKey.Layer,
                                shadowCastingMode = indirectKey.ShadowCastingMode,
                                receiveShadows = indirectKey.ReceiveShadows,
                                splitVisibilityMask = (ushort)(1u << iSplit),
                            };

                            outputDrawCommand.indirectDrawCommands[indirectIndex++] = indirectCmd;
                        }

                        jobHandles[iSplit].Complete();

                        _cmd.BeginSample($"Split-{iSplit}");

                        _cullingHelper.BuildCommandBuffer(_cmd, _indirectPipeline.IndirectPipelineCS, iSplit);
                        _indirectPipeline.SerLodParam(_cmd, true);
                        _indirectPipeline.BuildCommandBuffer(_cmd, visibilityBuffer, indirectArgsBuffer, inputIndexBuffer, outputIndexBuffer, *visibleIndicesArray[iSplit], *partialIndicesArray[iSplit]);

                        _cmd.EndSample($"Split-{iSplit}");
                    }

                    cullingOutput.drawCommands[0] = outputDrawCommand;

                    for (int iSplit = 0; iSplit < splitCount; ++iSplit)
                    {
                        visibleIndicesArray[iSplit]->Dispose();
                        partialIndicesArray[iSplit]->Dispose();
                        MemoryUtility.Free(visibleIndicesArray[iSplit], Allocator.TempJob);
                        MemoryUtility.Free(partialIndicesArray[iSplit], Allocator.TempJob);
                    }
                }
                else
                {
                    Utility.LogError("Unknown BatchCullingViewType");
                    return new JobHandle();
                }

                _cullingHelper.Dispose();

                Graphics.ExecuteCommandBuffer(_cmd);

                _bufferManager.RecycleIndexBuffer();

                return new JobHandle();
            }
        }

        private unsafe JobHandle OnPerformCulling(
            BatchRendererGroup rendererGroup,
            BatchCullingContext cullingContext,
            BatchCullingOutput cullingOutput,
            IntPtr userContext)
        {
            if (!ShouldDraw())
                return new JobHandle();

            return CullingCallback(ref cullingContext, cullingOutput);
        }
    }
}