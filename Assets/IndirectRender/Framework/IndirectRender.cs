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
        QuadTree _quadTree = new QuadTree();
        IndirectPipeline _indirectPipeline = new IndirectPipeline();
        CullingHelper _cullingHelper = new CullingHelper();

        CommandBuffer _cmd = new CommandBuffer();
        MaterialPropertyBlock _mpb;

        JobHandle _dispatchJobHandle = new JobHandle();

        bool _draw = true;
        bool _initialized = false;

        static IndirectRender s_instance = null;

        static readonly int s_indirectIndexBufferID = Shader.PropertyToID("IndirectIndexBuffer");
        static readonly int s_indirectVertexBufferID = Shader.PropertyToID("IndirectVertexBuffer");

        public void Init(IndirectRenderSetting setting, ComputeShader indirectPipelineCS)
        {
            if (!CheckSetting(setting))
                return;

            _unmanaged = MemoryUtility.Malloc<IndirectRenderUnmanaged>(Allocator.Persistent);
            _unmanaged->Init(setting);

            _brg = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
            _meshMerger.Init(setting.IndexCapacity, setting.VertexCapacity, setting.UnitMeshTriangleCount);
            _assetManager.Init(_meshMerger, _brg);
            _bufferManager.Init(setting);
            _quadTree.Init(setting);
            _indirectPipeline.Init(setting, indirectPipelineCS, _bufferManager);
            _cullingHelper.Init();

            _mpb = new MaterialPropertyBlock();
            _mpb.SetBuffer(BufferManager.s_InstanceDescriptorBufferID, _bufferManager.InstanceDescriptorBuffer);
            _mpb.SetBuffer(BufferManager.s_BatchDescriptorBufferID, _bufferManager.BatchDescriptorBuffer);
            _mpb.SetBuffer(BufferManager.s_InstanceDataBufferID, _bufferManager.InstanceDataBuffer);
            _mpb.SetBuffer(s_indirectVertexBufferID, _meshMerger.GetVertexBuffer());
            _mpb.SetBuffer(s_indirectIndexBufferID, _meshMerger.GetIndexBuffer());

            _brg.SetIndirect();
            _brg.SetIndirectProperties(_mpb);

            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.endContextRendering += OnEndContextRendering;

            s_instance = this;
            _initialized = true;
        }

        bool CheckSetting(IndirectRenderSetting setting)
        {
            if (setting.QuadTreeSetting.MaxLodRange.y != 1)
            {
                Utility.LogError("MaxLodRange.y must be 1");
                return false;
            }

            if (setting.MaxInstanceCountPerCmd * setting.NumMaxInstanceCountPerCmd > setting.InstanceCapacity)
            {
                Utility.LogError($"InstanceCapacity({setting.InstanceCapacity}) must be at least " +
                    $"MaxInstanceCountPerCmd({setting.MaxInstanceCountPerCmd}) * NumMaxInstanceCountPerCmd({setting.NumMaxInstanceCountPerCmd})");

                return false;
            }

            return true;
        }

        public void Dispose()
        {
            if (!_initialized)
                return;

            _cullingHelper.Dispose();
            _indirectPipeline.Dispose();
            _quadTree.Dispose();
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
            if (!_initialized)
                return;

            Prepare();
        }

        void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!_initialized)
                return;

            _bufferManager.Recycle();
        }

        public int RegisterMesh(MeshKey meshKey)
        {
            if (!_initialized)
                return -1;

            return _assetManager.RegisterMesh(meshKey);
        }

        public int RegisterMaterial(Material material)
        {
            if (!_initialized)
                return -1;

            return _assetManager.RegisterMaterial(material);
        }

        public Material GetMaterial(int id)
        {
            if (!_initialized)
                return null;

            return _assetManager.GetMaterial(id);
        }

        public int AddBatch(IndirectKey indirectKey, int meshID, bool needInverse, UnsafeList<float4x4> matrices, UnsafeList<UnsafeList<float4>> properties)
        {
            if (!_initialized)
                return -1;

            _assetManager.AddShaderLayout(indirectKey.MaterialID, new ShaderLayout
            {
                NeedInverse = needInverse,
                PeopertyCount = properties.Length
            });

            AddItem addItem = new AddItem
            {
                UserID = _unmanaged->UserIDGenerator.GetID(),
                IndirectKey = indirectKey,
                MeshInfo = _assetManager.GetMeshInfo(meshID),
                NeedInverse = needInverse,
                Matrices = matrices,
                Properties = properties,
            };

            _unmanaged->AddCache.Add(addItem);

            return addItem.UserID;
        }

        public void RemoveBatch(int id)
        {
            if (!_initialized)
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
            if (!_initialized)
                return;

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
            if (!_initialized)
                return;

            using (s_prepareMarker.Auto())
            {
                _dispatchJobHandle.Complete();

                foreach (var segment in _unmanaged->InstanceDataDirtySegments)
                    _bufferManager.InstanceDataBuffer.SetData(_unmanaged->InstanceDataArray, segment.OffsetF4, segment.OffsetF4, segment.SizeF4);
                _unmanaged->InstanceDataDirtySegments.Clear();

                foreach (var segment in _unmanaged->InstanceDescriptorDirtySegments)
                    _bufferManager.InstanceDescriptorBuffer.SetData(_unmanaged->InstanceDescriptorArray, segment.OffsetF4, segment.OffsetF4, segment.SizeF4);
                _unmanaged->InstanceDescriptorDirtySegments.Clear();

                _bufferManager.BatchDescriptorBuffer.SetData(_unmanaged->BatchDescriptorArray, 0, 0, _unmanaged->MaxIndirectID + 1);

                _brg.SetIndirectCmdCount(_unmanaged->IndirectMap.Count);
            }
        }

        static readonly ProfilerMarker s_cullingCallbackMarker = new ProfilerMarker("IndirectRender.CullingCallback");
        JobHandle CullingCallback(ref BatchCullingContext cullingContext, BatchCullingOutput cullingOutput)
        {
            using (s_cullingCallbackMarker.Auto())
            {
                _cullingHelper.UpdateCullinglanes(ref cullingContext);

                if (cullingContext.viewType == BatchCullingViewType.Camera)
                {
                    _quadTree.Cull(_cullingHelper.GetCullinglanes(0), out UnsafeList<int4> visibleIndices, out UnsafeList<int4> partialIndices);

                    GraphicsBuffer visibilityBuffer = _bufferManager.GetVisibilityBuffer();
                    GraphicsBuffer indirectArgsBuffer = _bufferManager.GetIndirectArgsBuffer();

                    indirectArgsBuffer.SetData(_unmanaged->IndirectArgsArray, 0, 0, (_unmanaged->MaxIndirectID + 1));

                    _cmd.name = $"Indirect.Camera";
                    _cmd.Clear();

                    _cullingHelper.BuildCommandBuffer(_cmd, _indirectPipeline.IndirectPipelineCS, 0);
                    _indirectPipeline.BuildCommandBuffer(_cmd, visibilityBuffer, indirectArgsBuffer, visibleIndices, partialIndices);
                    Graphics.ExecuteCommandBuffer(_cmd);

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
                else if (cullingContext.viewType == BatchCullingViewType.Light)
                {
                    int splitCount = cullingContext.cullingSplits.Length;

                    BatchCullingOutputDrawCommands outputDrawCommand = new BatchCullingOutputDrawCommands();

                    outputDrawCommand.indirectDrawCommands = MemoryUtility.MallocNoTrack<BatchDrawCommandIndirect>(_unmanaged->IndirectMap.Count * splitCount, Allocator.TempJob);
                    outputDrawCommand.indirectDrawCommandCount = _unmanaged->IndirectMap.Count * splitCount;
                    int indirectIndex = 0;

                    for (int iSplit = 0; iSplit < splitCount; ++iSplit)
                    {
                        _quadTree.Cull(_cullingHelper.GetCullinglanes(iSplit), out UnsafeList<int4> visibleIndices, out UnsafeList<int4> partialIndices);

                        GraphicsBuffer visibilityBuffer = _bufferManager.GetVisibilityBuffer();
                        GraphicsBuffer indirectArgsBuffer = _bufferManager.GetIndirectArgsBuffer();

                        indirectArgsBuffer.SetData(_unmanaged->IndirectArgsArray, 0, 0, (_unmanaged->MaxIndirectID + 1));

                        _cmd.name = $"Indirect.Shadow-{iSplit}";
                        _cmd.Clear();

                        _cullingHelper.BuildCommandBuffer(_cmd, _indirectPipeline.IndirectPipelineCS, iSplit);
                        _indirectPipeline.BuildCommandBuffer(_cmd, visibilityBuffer, indirectArgsBuffer, visibleIndices, partialIndices);
                        Graphics.ExecuteCommandBuffer(_cmd);

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
                    }

                    cullingOutput.drawCommands[0] = outputDrawCommand;
                }
                else
                {
                    Utility.LogError("Unknown BatchCullingViewType");
                    return new JobHandle();
                }

                _cullingHelper.Dispose();

                return new JobHandle();
            }
        }

        private unsafe JobHandle OnPerformCulling(
            BatchRendererGroup rendererGroup,
            BatchCullingContext cullingContext,
            BatchCullingOutput cullingOutput,
            IntPtr userContext)
        {
            if (!_initialized)
                return new JobHandle();

            if (!ShouldDraw())
                return new JobHandle();

            return CullingCallback(ref cullingContext, cullingOutput);
        }
    }
}