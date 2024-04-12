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
        public IndirectDrawer IndirectDrawer => _indirectDrawer;

        MeshMerger _meshMerger = new MeshMerger();
        AssetManager _assetManager = new AssetManager();
        IndirectRenderUnmanaged* _unmanaged;

        DispatchHelper _dispatchHelper = new DispatchHelper();
        CullingHelper _cullingHelper = new CullingHelper();

        QuadTreeBuildPass _quadTreeBuildPass = new QuadTreeBuildPass();
        PopulateInstanceIndexPass _populateInstanceIndexPass = new PopulateInstanceIndexPass();
        QuadTreeCullingPass _quadTreeCullingPass = new QuadTreeCullingPass();
        FrustumCullingPass _frustumCullingPass = new FrustumCullingPass();
        PopulateVisibilityAndIndirectArgPass _populateVisibilityAndIndirectArgPass = new PopulateVisibilityAndIndirectArgPass();

        IndirectDrawer _indirectDrawer = new IndirectDrawer();

        BatchRendererGroup _brg;

        CommandBuffer _cmd;

        JobHandle _dispatchJobHandle;

        bool _draw;
        bool _drawQuadTree;

        bool _initialized = false;

        static IndirectRender s_instance = null;

        public void Init(IndirectRenderSetting setting, ComputerShaderCollection computerShaderCollection)
        {
            if (!CheckSetting(setting))
                return;

            _meshMerger.Init(setting.IndexCapacity, setting.VertexCapacity, setting.UnitMeshTriangleCount);
            _assetManager.Init(_meshMerger);

            _unmanaged = MemoryUtility.Malloc<IndirectRenderUnmanaged>(Allocator.Persistent);
            _unmanaged->Init(setting);

            _dispatchHelper.Init(computerShaderCollection.AdjustDispatchArgCS);
            _cullingHelper.Init();

            _quadTreeBuildPass.Init(setting, computerShaderCollection.QuadTreeBuildCS, _dispatchHelper);
            _populateInstanceIndexPass.Init(setting, computerShaderCollection.PopulateInstanceIndexCS);
            _quadTreeCullingPass.Init(setting, computerShaderCollection.QuadTreeCullingCS, _quadTreeBuildPass);
            _frustumCullingPass.Init(setting, computerShaderCollection.FrustumCullingCS, _dispatchHelper);
            _populateVisibilityAndIndirectArgPass.Init(setting, computerShaderCollection.PopulateVisibilityAndIndirectArgCS, _dispatchHelper);

            _indirectDrawer.Init(setting, _meshMerger, _assetManager, _unmanaged);

            _quadTreeCullingPass.ConnectBuffer(_quadTreeBuildPass.GetQuadTreeNodeVisibilityBuffer(),
                _populateInstanceIndexPass.GetInstanceIndicesBuffer(), _indirectDrawer.GetInstanceDescriptorBuffer());
            _frustumCullingPass.ConnectBuffer(_quadTreeCullingPass.GetInstanceIndexTodoBuffer(),
                _quadTreeCullingPass.GetInstanceIndexFinalBuffer(), _indirectDrawer.GetInstanceDescriptorBuffer());
            _populateVisibilityAndIndirectArgPass.ConnectBuffer(_frustumCullingPass.GetInstanceIndexOuutputBuffer(),
                _indirectDrawer.GetInstanceDescriptorBuffer(), _indirectDrawer.GetBatchDescriptorBuffer(), _indirectDrawer.GetVisibilityBuffer());
            _indirectDrawer.ConnectBuffer(_populateVisibilityAndIndirectArgPass.GetIndirectArgsBuffer());

            _brg = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);

            _cmd = new CommandBuffer();
            _cmd.name = "IndirectRenderCmd";

            _dispatchJobHandle = new JobHandle();

            _draw = true;
            _drawQuadTree = true;

            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;

            s_instance = this;

            _initialized = true;
        }

        bool CheckSetting(IndirectRenderSetting setting)
        {
            if (setting.MaxInstanceCountPerCmd * setting.NumMaxInstanceCountPerCmd > setting.InstanceCapacity)
            {
                Utility.LogError($"InstanceCapacity({setting.InstanceCapacity}) must be at least " +
                    $"MaxInstanceCountPerCmd({setting.MaxInstanceCountPerCmd}) * NumMaxInstanceCountPerCmd({setting.NumMaxInstanceCountPerCmd})");

                return false;
            }

            if (setting.QuadTreeSetting.MaxLod + 1 > Utility.c_QuadTreeMaxLodNum)
            {
                Utility.LogError($"MaxLod({setting.QuadTreeSetting.MaxLod} exceeds {Utility.c_QuadTreeMaxLodNum})");

                return false;
            }

            return true;
        }

        public void Dispose()
        {
            if (!_initialized)
                return;

            _meshMerger.Dispose();
            _assetManager.Dispose();

            _unmanaged->Dispose();
            MemoryUtility.Free(_unmanaged, Allocator.Persistent);

            _dispatchHelper.Dispose();
            _cullingHelper.Dispose();

            _quadTreeBuildPass.Dispose();
            _populateInstanceIndexPass.Dispose();
            _quadTreeCullingPass.Dispose();
            _frustumCullingPass.Dispose();
            _populateVisibilityAndIndirectArgPass.Dispose();

            _indirectDrawer.Dispose();

            _brg.Dispose();

            _cmd.Dispose();

            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
        }

        void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!_initialized)
                return;

            Execute();
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

        public void SetQuadTreeCullingEnable(bool enable)
        {
            if (!_initialized)
                return;

            _quadTreeCullingPass.SetEnable(enable);
        }

        public void SetFrustumCullingEnable(bool enable)
        {
            if (!_initialized)
                return;

            _frustumCullingPass.SetEnable(enable);
        }

        public int AddBatch(IndirectKey indirectKey, int meshID, bool needInverse, UnsafeList<float4x4> matrices, UnsafeList<UnsafeList<float4>> properties)
        {
            if (!_initialized)
                return -1;

            int result = _unmanaged->AddBatchImpl(indirectKey, meshID, needInverse, matrices, properties,
                _assetManager, _indirectDrawer.GetInstanceDataBuffer(), _indirectDrawer.GetInstanceDescriptorBuffer(), _quadTreeBuildPass);

            matrices.Dispose();
            for (int i = 0; i < properties.Length; ++i)
                properties[i].Dispose();
            properties.Dispose();

            return result;
        }

        public void RemoveBatch(int id)
        {
            if (!_initialized)
                return;

            _unmanaged->RemoveBatch(id);
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

            if (!ShouldDraw())
                return;

            using (s_dispatchMarker.Auto())
            {
                DispatchJob dispatchJob = new DispatchJob
                {
                    Unmanaged = _unmanaged,
                    UnitMeshIndexCount = _meshMerger.GetUnitMeshIndexCount()
                };
                _dispatchJobHandle = dispatchJob.Schedule();
            }
        }

        static readonly ProfilerMarker s_executeMarker = new ProfilerMarker("IndirectRender.Execute");
        void Execute()
        {
            if (!_initialized)
                return;

            if (!ShouldDraw())
                return;

            using (s_executeMarker.Auto())
            {
                _dispatchJobHandle.Complete();

                Prepare();

                //DrawIndirect();
            }
        }

        static readonly ProfilerMarker s_prepareMarker = new ProfilerMarker("IndirectRender.Prepare");
        void Prepare()
        {
            if (!_initialized)
                return;

            using (s_prepareMarker.Auto())
            {
                _quadTreeBuildPass.Prepare(_unmanaged);
                _populateInstanceIndexPass.Prepare(_unmanaged);
                _quadTreeCullingPass.Prepare(_unmanaged);
                _frustumCullingPass.Prepare(_unmanaged);
                _populateVisibilityAndIndirectArgPass.Prepare(_unmanaged);
            }
        }

        static readonly ProfilerMarker s_buildCommandBufferMarker = new ProfilerMarker("IndirectRender.BuildCommandBuffer");
        void BuildCommandBuffer()
        {
            if (!_initialized)
                return;

            using (s_buildCommandBufferMarker.Auto())
            {
                BatchCullingViewType viewType = _cullingHelper.GetViewType();
                if(viewType == BatchCullingViewType.Camera)
                    _cmd.name = "Indirect.Camera";
                else if(viewType == BatchCullingViewType.Light)
                    _cmd.name = "Indirect.Shadow";
                else
                    Utility.LogError("Unknown BatchCullingViewType");

                _cmd.Clear();

                _quadTreeBuildPass.BuildCommandBuffer(_cmd, _cullingHelper);
                _populateInstanceIndexPass.BuildCommandBuffer(_cmd, _cullingHelper);
                _quadTreeCullingPass.BuildCommandBuffer(_cmd, _cullingHelper);
                _frustumCullingPass.BuildCommandBuffer(_cmd, _cullingHelper);
                _populateVisibilityAndIndirectArgPass.BuildCommandBuffer(_cmd, _cullingHelper);
            }
        }

        static readonly ProfilerMarker s_drawIndirectMarker = new ProfilerMarker("IndirectRender.DrawIndirect");
        void DrawIndirect()
        {
            if (!_initialized)
                return;

            using (s_drawIndirectMarker.Auto())
            {
                //_indirectDrawer.DrawIndirect();
            }
        }

        static readonly ProfilerMarker s_cullingCallbackMarker = new ProfilerMarker("IndirectRender.CullingCallback");
        void CullingCallback(ref BatchCullingContext cullingContext)
        {
            if (!_initialized)
                return;

            using (s_cullingCallbackMarker.Auto())
            {
                _cullingHelper.UpdateCullingParameters(ref cullingContext);

                // todo
                if (cullingContext.viewType == BatchCullingViewType.Light)
                    return;

                BuildCommandBuffer();

                // todo
                //_indirectDrawer.DrawIndirect(_cmd);

                Graphics.ExecuteCommandBuffer(_cmd);
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

            CullingCallback(ref cullingContext);

            return new JobHandle();
        }
    }
}