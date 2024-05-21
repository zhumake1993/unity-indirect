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

        IndirectRenderSetting _setting;

        IndirectRenderUnmanaged* _unmanaged;

        BatchRendererGroup _brg;
        MeshMerger _meshMerger = new MeshMerger();
        MaterialMerger _materialMerger = new MaterialMerger();
        AssetManager _assetManager = new AssetManager();
        BufferManager _bufferManager = new BufferManager();
        DispatchHelper _dispatchHelper = new DispatchHelper();
        IndirectPipelinePool _indirectPipelinePool = new IndirectPipelinePool();
        CullingHelper _cullingHelper = new CullingHelper();

        QuadTree _quadTree = new QuadTree();

        ComputeShader _indirectPipelineCS;

        MaterialPropertyBlock _mpb;

        JobHandle _dispatchJobHandle = new JobHandle();

        List<IndirectPipeline> _cameraPileines = new List<IndirectPipeline>();
        List<IndirectPipeline> _shadowPileines = new List<IndirectPipeline>();
        List<JobHandle> _cameraJobHandles = new List<JobHandle>();
        List<JobHandle> _shadowJobHandles = new List<JobHandle>();

        bool _draw = true;

        static IndirectRender s_instance = null;

        static readonly int s_indirectIndexBufferID = Shader.PropertyToID("IndirectIndexBuffer");
        static readonly int s_indirectVertexBufferID = Shader.PropertyToID("IndirectVertexBuffer");

        public bool Init(IndirectRenderSetting setting, ComputeShader indirectPipelineCS, ComputeShader adjustDispatchArgCS)
        {
            if (!CheckSetting(setting))
                return false;

            _setting = setting;

            _unmanaged = MemoryUtility.Malloc<IndirectRenderUnmanaged>(Allocator.Persistent);
            _unmanaged->Init(setting);

#if ZGAME_BRG_INDIRECT
            _brg = new BatchRendererGroup(OnPerformCulling, OnFinish, IntPtr.Zero);
#else
            _brg = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
#endif
            _meshMerger.Init(setting.IndexCapacity, setting.VertexCapacity, setting.MeshletTriangleCount);
            _materialMerger.Init();
            _assetManager.Init(_meshMerger, _materialMerger, _brg);
            _bufferManager.Init(setting);
            _dispatchHelper.Init(adjustDispatchArgCS);
            _indirectPipelinePool.Init();
            _cullingHelper.Init();

            _quadTree.Init(setting);

            _indirectPipelineCS = indirectPipelineCS;

            _mpb = new MaterialPropertyBlock();
            _mpb.SetBuffer(BufferManager.s_MeshletDescriptorBufferID, _bufferManager.MeshletDescriptorBuffer);
            _mpb.SetBuffer(BufferManager.s_BatchDescriptorBufferID, _bufferManager.BatchDescriptorBuffer);
            _mpb.SetBuffer(BufferManager.s_InstanceDataBufferID, _bufferManager.InstanceDataBuffer);
            _mpb.SetBuffer(s_indirectVertexBufferID, _meshMerger.GetVertexBuffer());
            _mpb.SetBuffer(s_indirectIndexBufferID, _meshMerger.GetIndexBuffer());

#if ZGAME_BRG_INDIRECT
            _brg.SetIndirect();
            _brg.SetIndirectProperties(_mpb);
#endif

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
            _indirectPipelinePool.Dispose();
            _dispatchHelper.Dispose();
            _bufferManager.Dispose();
            _assetManager.Dispose();
            _materialMerger.Dispose();
            _meshMerger.Dispose();
            _brg.Dispose();

            _unmanaged->Dispose();
            MemoryUtility.Free(_unmanaged, Allocator.Persistent);

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
            _indirectPipelinePool.Recycle();

            if (_cameraPileines.Count > 0)
                Utility.LogError($"_cameraPileines.Count > 0");

            if (_shadowPileines.Count > 0)
                Utility.LogError($"_shadowPileines.Count > 0");
        }

        public int RegisterMesh(Mesh mesh)
        {
            return _assetManager.RegisterMesh(mesh);
        }

        public void UnregisterMesh(int id)
        {
            _assetManager.UnregisterMesh(id);
        }

        public int RegisterMaterial(Material material, bool merge)
        {
            return _assetManager.RegisterMaterial(material, merge);
        }

        public void UnregisterMaterial(int id)
        {
            _assetManager.UnregisterMaterial(id);
        }

        public Material GetMaterial(int id)
        {
            return _assetManager.GetMaterial(id);
        }

        bool CheckBatch(UnsafeList<RenderData> renderDatas, UnsafeList<float4x4> matrices, UnsafeList<UnsafeList<float4>> properties)
        {
            for (int i = 0; i < renderDatas.Length; ++i)
            {
                if (renderDatas[i].MeshID < 0)
                {
                    Utility.LogError($"renderDatas[{i}].MeshID < 0");
                    return false;
                }

                if (renderDatas[i].MaterialID < 0)
                {
                    Utility.LogError($"renderDatas[{i}].MaterialID < 0");
                    return false;
                }
            }

            return true;
        }

        public int AddBatch(UnsafeList<RenderData> renderDatas, float4 lodParam, bool needInverse, UnsafeList<float4x4> matrices, UnsafeList<UnsafeList<float4>> properties)
        {
            if (!CheckBatch(renderDatas, matrices, properties))
                return -1;

            int cmdID = _unmanaged->CmdIDGenerator.GetID();
            if (cmdID >= _unmanaged->Setting.CmdCapacity)
            {
                Utility.LogError($"cmdID({cmdID}) >= CmdCapacity({_unmanaged->Setting.CmdCapacity})");
                return -1;
            }

            foreach (var renderData in renderDatas)
            {
                _assetManager.AddShaderLayout(renderData.MaterialID, new ShaderLayout
                {
                    NeedInverse = needInverse,
                    PeopertyCount = properties.Length
                });
            }

            UnsafeList<SubMeshInfo> subMeshInfos = new UnsafeList<SubMeshInfo>(renderDatas.Length, Allocator.TempJob);
            for (int i = 0; i < renderDatas.Length; ++i)
                subMeshInfos.Add(_assetManager.GetMeshInfo(renderDatas[i].MeshID, renderDatas[i].SubmeshIndex));

            UnsafeList<IndirectKey> indirectKeys = new UnsafeList<IndirectKey>(renderDatas.Length, Allocator.TempJob);
            for (int i = 0; i < renderDatas.Length; ++i)
            {
                indirectKeys.Add(new IndirectKey
                {
                    MaterialID = renderDatas[i].MaterialID,
                    Layer = renderDatas[i].Layer,
                    ReceiveShadows = renderDatas[i].ReceiveShadows,
                    ShadowCastingMode = renderDatas[i].ShadowCastingMode,
                });
            }

            renderDatas.Dispose();

            AddItem addItem = new AddItem
            {
                CmdID = cmdID,
                SubMeshInfos = subMeshInfos,
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

        public bool GetInstanceEnable(int cmdID, int index)
        {
            if (_unmanaged->EnableCache.TryGetValue(new int2(cmdID, index), out bool enable))
                return enable;

            if (_unmanaged->CmdMap.ContainsKey(cmdID))
            {
                CmdDescriptor cmdDescriptor = _unmanaged->CmdDescriptorArray[cmdID];
                if (index < cmdDescriptor.InstanceCount)
                {
                    return _unmanaged->InstanceDescriptorArray[cmdDescriptor.InstanceStartIndex + index].Enable != 0;
                }
            }

            Utility.LogError($"GetInstanceEnable failed, cmdID={cmdID}, index={index}");
            return false;
        }

        public void SetInstanceEnable(int cmdID, int index, bool enable)
        {
            _unmanaged->EnableCache[new int2(cmdID, index)] = enable;
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
                    jobHandle = new FlushRemoveJob { Unmanaged = _unmanaged, QuadTree = _quadTree, }.Schedule(jobHandle);
                    jobHandle = _quadTree.DispatchDeleteJob(_unmanaged->QuadTreeIndexToRemoveSet, jobHandle);
                }

                if (_unmanaged->AddCache.Length > 0)
                {
                    jobHandle = new PreFlushAddJob { Unmanaged = _unmanaged, }.Schedule(jobHandle);
                    jobHandle = new FlushAddJob { Unmanaged = _unmanaged, }.Schedule(_unmanaged->AddCache.Length, 1, jobHandle);
                    jobHandle = new PostFlushAddJob { Unmanaged = _unmanaged, }.Schedule(jobHandle);
                    jobHandle = _quadTree.DispatchAddJob(_unmanaged->QuadTreeAABBInfos, jobHandle);
                }

                if (!_unmanaged->EnableCache.IsEmpty)
                {
                    jobHandle = new FlushEnableJob { Unmanaged = _unmanaged, }.Schedule(jobHandle);
                    jobHandle = new FlushRemoveJob { Unmanaged = _unmanaged, QuadTree = _quadTree, }.Schedule(jobHandle);
                    jobHandle = _quadTree.DispatchDeleteJob(_unmanaged->QuadTreeIndexToRemoveSet, jobHandle);
                }

                _dispatchJobHandle = new UpdateBatchDescriptorJob { Unmanaged = _unmanaged, }.Schedule(jobHandle);
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

                _cameraPileines.Clear();
                _shadowPileines.Clear();

#if ZGAME_BRG_INDIRECT
                _brg.SetIndirectCmdCount(_unmanaged->IndirectMap.Count);
#endif
            }
        }

        static readonly ProfilerMarker s_cullingCallbackMarker = new ProfilerMarker("IndirectRender.CullingCallback");
        JobHandle CullingCallback(ref BatchCullingContext cullingContext, BatchCullingOutput cullingOutput)
        {
            using (s_cullingCallbackMarker.Auto())
            {
#if ZGAME_BRG_INDIRECT

                if (cullingContext.viewType != BatchCullingViewType.Camera && cullingContext.viewType != BatchCullingViewType.Light)
                {
                    Utility.LogError("Unknown BatchCullingViewType");
                    return new JobHandle();
                }

                int splitCount = cullingContext.cullingSplits.Length;
                NativeList<JobHandle> jobHandles = new NativeList<JobHandle>(splitCount, Allocator.Temp);

                UnsafeList<GraphicsBufferHandle> visibilityBufferHandles = new UnsafeList<GraphicsBufferHandle>(splitCount, Allocator.TempJob);
                UnsafeList<GraphicsBufferHandle> indirectArgsBufferHandles = new UnsafeList<GraphicsBufferHandle>(splitCount, Allocator.TempJob);

                _cullingHelper.UpdateCullinglanes(ref cullingContext);

                for (int iSplit = 0; iSplit < splitCount; ++iSplit)
                {
                    IndirectPipeline pipeline = _indirectPipelinePool.Get();
                    if (pipeline == null)
                    {
                        pipeline = new IndirectPipeline();
                        pipeline.Init(_setting, _indirectPipelineCS, _bufferManager, _dispatchHelper);
                        _indirectPipelinePool.Use(pipeline);
                    }

                    GraphicsBuffer visibilityBuffer = pipeline.GetVisibilityBuffer();
                    GraphicsBuffer indirectArgsBuffer = pipeline.GetIndirectArgsBuffer();
                    visibilityBufferHandles.Add(visibilityBuffer.bufferHandle);
                    indirectArgsBufferHandles.Add(indirectArgsBuffer.bufferHandle);

                    indirectArgsBuffer.SetData(_unmanaged->IndirectArgsArray, 0, 0, (_unmanaged->MaxIndirectID + 1));

                    jobHandles.Add(pipeline.Dispatch(_cullingHelper, _quadTree, iSplit));

                    if (cullingContext.viewType == BatchCullingViewType.Camera)
                        _cameraPileines.Add(pipeline);
                    else if (cullingContext.viewType == BatchCullingViewType.Light)
                        _shadowPileines.Add(pipeline);
                    else
                        Utility.LogError("Unknown BatchCullingViewType");
                }

                if (cullingContext.viewType == BatchCullingViewType.Camera)
                    _cameraJobHandles.Add(JobHandle.CombineDependencies(jobHandles.AsArray()));
                else if (cullingContext.viewType == BatchCullingViewType.Light)
                    _shadowJobHandles.Add(JobHandle.CombineDependencies(jobHandles.AsArray()));
                else
                    Utility.LogError("Unknown BatchCullingViewType");

                JobHandle createDrawCmdJobHandle = new CreateDrawCmdJob
                {
                    OutputDrawCommands = cullingOutput.drawCommands,
                    Unmanaged = _unmanaged,
                    IdToBatchMaterialID = _assetManager.GetIdToBatchMaterialID(),
                    VisibilityBufferHandles = visibilityBufferHandles,
                    IndirectArgsBufferHandles = indirectArgsBufferHandles,
                    SplitCount = splitCount,
                }.Schedule();

                _cullingHelper.Dispose();

                return createDrawCmdJobHandle;
#else
                return new JobHandle();
#endif
            }
        }

        static readonly ProfilerMarker s_finishCallbackMarker = new ProfilerMarker("IndirectRender.FinishCallback");
        void FinishCallback(BatchCullingViewType viewType)
        {
            using (s_finishCallbackMarker.Auto())
            {
                if (viewType == BatchCullingViewType.Camera)
                {
                    foreach(var jobHandle in _cameraJobHandles)
                        jobHandle.Complete();
                    _cameraJobHandles.Clear();

                    foreach (var pipeline in _cameraPileines)
                        pipeline.Finish();
                    _cameraPileines.Clear();
                }
                else if (viewType == BatchCullingViewType.Light)
                {
                    foreach (var jobHandle in _shadowJobHandles)
                        jobHandle.Complete();
                    _shadowJobHandles.Clear();

                    foreach (var pipeline in _shadowPileines)
                        pipeline.Finish();
                    _shadowPileines.Clear();
                }
                else
                {
                    Utility.LogError("Unknown BatchCullingViewType");
                }
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

        private unsafe void OnFinish(BatchCullingViewType viewType, IntPtr userContext)
        {
            if (!ShouldDraw())
                return;

            FinishCallback(viewType);
        }
    }
}